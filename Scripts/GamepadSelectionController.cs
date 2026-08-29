using System;
using Timberborn.AreaSelectionSystem;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.CameraSystem;
using Timberborn.Coordinates;
using Timberborn.CursorToolSystem;
using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.MapStateSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TerrainQueryingSystem;
using Timberborn.TerrainSystem;
using Timberborn.ToolSystem;
using Timberborn.WaterSystemRendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Drives the default Select tool (CursorTool) with the gamepad: a single grid cell, moved with
	// the stick/d-pad exactly like GamepadBuildingPlacementController's ghost, highlights whatever
	// SelectableObject sits under it, A selects it (opening its entity panel the same way a real
	// click does), B (Cancel) backs out to UI navigation. Always exactly one cell - unlike the
	// area-selection tools there is no click-and-drag rectangle here, so holding A never grows it.
	//
	// Two ways in and out. The dedicated ToggleSelectMode keybind (<Gamepad>/select by default) is a
	// straight toggle from anywhere - switches to CursorTool if needed and engages, or disengages if
	// already engaged - and never touches the shared Cancel signal, so it can't collide with
	// anything else watching it. B/Cancel, by contrast, is shared with CursorTool's own native
	// deselect-on-Cancel (ProcessUnselectObject) - exiting this mod's own submode on B is staged
	// ahead of that via GamepadSelectModeCancelGate, so the first B press only exits select mode and
	// a second, separate press is what actually deselects/closes the entity panel.
	//
	// CursorTool has no public seam for injecting a fake click the way BlockObjectTool/
	// AreaSelectionController do, but it doesn't need one: SelectableObjectRaycaster already exposes
	// an overload taking an explicit world-space Ray
	// (TryHitSelectableObjectIncludeTerrainStump(Ray, out SelectableObject, out RaycastHit)), and
	// EntitySelectionService.Select/Unselect are both public. So rather than faking mouse state into
	// GamepadPlacementState for CameraServicePlacementPatch/InputServicePlacementPatch to feed back to
	// CursorTool.ProcessInput, this calls those two services directly with a ray built by hand -
	// straight down through the cursor's cell, the same construction CameraServicePlacementPatch uses
	// for its own patch, converted from grid to world space via CoordinateSystem since
	// Physics.Raycast (which the raycaster runs) only understands world space.
	//
	// GamepadPlacementState.Active/ToolEngaged are still set every engaged frame even though nothing
	// here reads Active back - it is the same "gamepad owns world input right now" signal the other
	// two controllers publish, and GamepadNavigationInputProcessor already stands down on ToolEngaged,
	// which is what stops the left stick driving bottom-bar/entity-panel navigation out from under
	// this cursor while it is live.
	//
	// The box/cursor itself is deliberately gamepad-only, unlike GamepadBuildingPlacementController/
	// GamepadAreaSelectionController: those two hand off cursor *position* to a real mouse mid-session
	// because a mouse user has no other way to place a building or mark an area once the tool is open.
	// Select mode is different - the player can always click an object with the plain mouse without
	// ever touching the gamepad's own box, so the box stays wherever the stick last left it regardless
	// of what the mouse does; it is never repositioned or hidden in response to mouse movement.
	//
	// A real mouse click is still let through, though - see the MouseLeftKey handling in Update() -
	// since hiding the cursor for select mode's entire engagement, with no way to click anything with
	// it even though the player can still reach the bottom bar/entity panel by hand, read as a bug in
	// its own right the first time it shipped. GamepadMouseHandoff is reused here purely for its
	// cursor-visibility side effect (Cursor.visible tracking recent device activity); its returned
	// control decision is ignored, since the box's *position* is never handed to the mouse.
	//
	// CursorTool is also the game's default tool - active whenever nothing else is - so unlike an
	// area tool that only becomes ActiveTool when the player deliberately opens it, ActiveTool alone
	// can't tell "just landed back on Select" from "player wants the cursor now". The signal used
	// instead is ToolEnteredEvent: ToolButton's click handler always calls
	// ToolService.SwitchTool(Tool), and SwitchTool computes forceSwitch = IsDefaultTool(tool) - true
	// for CursorTool always - so ToolService re-enters and re-posts ToolEnteredEvent(CursorTool) even
	// when it was already the active tool. SwitchToDefaultTool() (used by B-cancel returning from
	// another tool) does not force this, so it is the one path that does not re-post the event.
	// Tracking the previously-known active tool and arming only when a CursorTool ToolEnteredEvent
	// arrives while CursorTool was *already* active isolates exactly "the player confirmed the Select
	// tool's own bottom-bar button" - gamepad UI navigation's Confirm() activates that same button, so
	// the same event fires for it as for a real click.
	internal class GamepadSelectionController : ILoadableSingleton, IUnloadableSingleton, IPriorityInputProcessor
	{
		private const float FailureLogInterval = 30f;

		// Matches the Id in this mod's own KeyBinding.ToggleSelectMode.blueprint.json - a keybind this
		// mod defines from scratch (no vanilla equivalent), primary-bound to <Gamepad>/select.
		private const string ToggleSelectModeKey = "ToggleSelectMode";

		// Matches InputService's own MouseLeftKey constant, same as GamepadMouseHandoff - read
		// directly off KeyBindingRegistry rather than through InputService.MainMouseButtonDown so a
		// real click can be detected before this frame's own GamepadPlacementState write decides
		// whether that getter is even patched.
		private const string MouseLeftKey = "MouseLeft";

		// Not game colours - Timberborn's own SelectionColorsSpec.SelectionToolHighlight is a dark
		// red (0.55, 0.03, 0.05), verified against Blueprints.zip, not amber at all. Picked these to
		// read as "amber" while sitting at the same brightness/opacity as the game's own tool colours
		// (BlockObjectDeletionToolSpec, DemolishingColorsSpec, BuilderPriorityToolSpec, all in
		// Blueprints.zip): a fully opaque, moderately saturated tile colour paired with a near-white
		// side colour for the box (matching DeletedAreaTileColor/DeletedAreaSideColor and
		// PriorityTileColor/PrioritySideColor), and a dimmer, half-brightness version for the actual
		// entity highlight (matching DeletedObjectHighlightColor at 0.5 magnitude rather than the tile
		// colour's near-1.0 - an emissive highlight that bright would blow out the object it's on).
		private static readonly Color CursorTileColor = new Color(0.85f, 0.5f, 0f, 1f);
		private static readonly Color CursorSideColor = new Color(1f, 1f, 1f, 1f);
		private static readonly Color EntityHighlightColor = new Color(0.5f, 0.3f, 0f, 1f);

		private static readonly Vector3 Down = new Vector3(0f, 0f, -1f);

		private readonly InputService _inputService;
		private readonly CameraService _cameraService;
		private readonly ToolService _toolService;
		private readonly TerrainPicker _terrainPicker;
		private readonly ITerrainService _terrainService;
		private readonly IBlockService _blockService;
		private readonly PanelTracker _panelTracker;
		private readonly EventBus _eventBus;
		private readonly EntitySelectionService _entitySelectionService;
		private readonly SelectableObjectRaycaster _selectableObjectRaycaster;
		private readonly RollingHighlighter _rollingHighlighter;
		private readonly RectangleBoundsDrawerFactory _rectangleBoundsDrawerFactory;
		private readonly WaterOpacityService _waterOpacityService;
		private readonly KeyBindingRegistry _keyBindingRegistry;
		private readonly MapSize _mapSize;

		private readonly GamepadGridStepReader _stepReader = new GamepadGridStepReader();
		private readonly GamepadHeightStepReader _heightStepReader = new GamepadHeightStepReader();
		private readonly GamepadMouseHandoff _handoff;

		private RectangleBoundsDrawer _cursorBoundsDrawer;
		private WaterOpacityToggle _waterOpacityToggle;

		private bool _engaged;
		private bool _armPending;
		private ITool _lastKnownActiveTool;
		private Vector3Int _cursor;

		// See GamepadBuildingPlacementController's own fields of the same names.
		private bool _heightLocked;
		private int _lockedHeight;

		// The height this controller's own hand-built rays (TryPickSelectable, RefreshCursorHeight)
		// originate from - GamepadCursorHeight.RayHeight by default, cursor.z + 1 once _heightLocked.
		// See GamepadPlacementState.CursorRayOriginHeight for why this mirrors that field.
		private float _rayOriginHeight = GamepadCursorHeight.RayHeight;

		private float _nextFailureLogTime;

		public GamepadSelectionController(InputService inputService, CameraService cameraService,
			ToolService toolService, TerrainPicker terrainPicker, ITerrainService terrainService,
			IBlockService blockService, PanelTracker panelTracker, EventBus eventBus,
			EntitySelectionService entitySelectionService, SelectableObjectRaycaster selectableObjectRaycaster,
			RollingHighlighter rollingHighlighter, RectangleBoundsDrawerFactory rectangleBoundsDrawerFactory,
			WaterOpacityService waterOpacityService, KeyBindingRegistry keyBindingRegistry,
			RecentInputDeviceTracker recentInputDeviceTracker, MapSize mapSize)
		{
			_inputService = inputService;
			_cameraService = cameraService;
			_toolService = toolService;
			_terrainPicker = terrainPicker;
			_terrainService = terrainService;
			_blockService = blockService;
			_panelTracker = panelTracker;
			_eventBus = eventBus;
			_entitySelectionService = entitySelectionService;
			_selectableObjectRaycaster = selectableObjectRaycaster;
			_rollingHighlighter = rollingHighlighter;
			_rectangleBoundsDrawerFactory = rectangleBoundsDrawerFactory;
			_waterOpacityService = waterOpacityService;
			_keyBindingRegistry = keyBindingRegistry;
			_mapSize = mapSize;
			_handoff = new GamepadMouseHandoff(keyBindingRegistry, inputService, recentInputDeviceTracker);
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
			_eventBus.Register(this);
			_cursorBoundsDrawer = _rectangleBoundsDrawerFactory.Create(CursorTileColor, CursorSideColor);
			_waterOpacityToggle = _waterOpacityService.GetWaterOpacityToggle();
		}

		// See GamepadBuildingPlacementController.Unload for why an IPriorityInputProcessor can never
		// be unregistered - the same applies here, even though this class doesn't share that method's
		// starvation risk (it never leaves a stale "up" edge for anything downstream to get stuck on).
		public void Unload()
		{
			_eventBus.Unregister(this);
			_rollingHighlighter.UnhighlightAllPrimary();
			_waterOpacityToggle.ShowWater();
			_inputService.ShowCursor();
			GamepadPlacementState.Clear();
		}

		[OnEvent]
		public void OnToolEntered(ToolEnteredEvent toolEnteredEvent)
		{
			if (toolEnteredEvent.Tool is CursorTool && _lastKnownActiveTool is CursorTool)
			{
				_armPending = true;
			}
		}

		public void ProcessInput()
		{
			try
			{
				Update();
			}
			catch (Exception e)
			{
				ReportFailure(e);
			}
		}

		private void Update()
		{
			var activeTool = _toolService.ActiveTool;
			_lastKnownActiveTool = activeTool;

			var gamepad = Gamepad.current;
			if (gamepad == null)
			{
				_armPending = false;
				Disengage();
				return;
			}

			// Works from any tool - a shortcut past the bottom bar entirely, so it never moves
			// GamepadNavigationInputProcessor's own selection onto the Select tool's button the way
			// navigating there and confirming it would. Gated on HasStackedPanel for the same reason
			// as everywhere else below: a dialog underneath shouldn't lose focus to a tool switch.
			if (!_panelTracker.HasStackedPanel && _inputService.IsKeyDown(ToggleSelectModeKey))
			{
				ToggleSelectMode(activeTool);
				return;
			}

			if (!(activeTool is CursorTool))
			{
				_armPending = false;
				Disengage();
				return;
			}

			// A dialog raised while engaged (the options box, opened by ProcessShowOptions on
			// UICancel, is the one CursorTool can trigger) stacks on top while CursorTool stays active
			// underneath it - see GamepadBuildingPlacementController.Update for why clearing published
			// state here, rather than a full Disengage, is what lets the gamepad reach the dialog's
			// own buttons while keeping the cursor exactly where it was for when it closes.
			if (_panelTracker.HasStackedPanel)
			{
				GamepadPlacementState.Clear();

				// The dialog needs the real cursor visible to be clickable - re-hidden below once
				// engagement resumes on whatever frame the dialog closes.
				_inputService.ShowCursor();
				return;
			}

			if (!_engaged)
			{
				if (!_armPending)
				{
					return;
				}

				_armPending = false;
				Engage();
			}

			// Reads the keybinding-driven signal rather than the raw button so this backs out on
			// whatever the player's Cancel is bound to - including the gamepad secondary this mod now
			// registers for it - instead of being hardcoded to B specifically. CursorTool's own
			// ProcessUnselectObject reacts to the exact same signal (deselecting whatever's selected,
			// closing the entity panel) every frame it's true, regardless of what this mod does - so
			// without the gate below, the very same press that exits select mode would also close the
			// panel this same frame. Flagging it here defers that to a genuinely separate press; see
			// GamepadSelectModeCancelGate and GamepadNavigationInputProcessor.OnToolEntered for why the
			// mod's own nav processor is guaranteed to see this before CursorTool does.
			if (_inputService.UICancel)
			{
				Disengage();
				GamepadSelectModeCancelGate.ConsumeNextCancel = true;
				return;
			}

			var step = _stepReader.ReadStep(_keyBindingRegistry, _cameraService.HorizontalAngle);

			// Cursor-visibility side effect only - see the class comment above for why the returned
			// control decision is ignored here. UIConfirm doubles as the "gamepad action" signal since
			// this controller has no separate placement-style Confirm key.
			_handoff.Update(step, _inputService.UIConfirm);

			if (step != Vector2Int.zero)
			{
				var moved = new Vector2Int(_cursor.x + step.x, _cursor.y + step.y);
				var clamped = GamepadCursorHeight.ClampToMap(moved, _mapSize.TerrainSize2D);
				_cursor.x = clamped.x;
				_cursor.y = clamped.y;
			}

			// Free 3D movement, same as GamepadBuildingPlacementController/GamepadAreaSelectionController
			// - the stored z only ever comes from Engage()'s seed unless refreshed here, and moving purely
			// in x/y leaves it stale the instant the player crosses onto ground at a different height,
			// which would draw the box below at the wrong level instead of tracking the terrain (or
			// whatever's dialled in with the height offset) the way a real mouse cursor would.
			var heightStep = _heightStepReader.ReadStep(_inputService);
			RefreshCursorHeight(heightStep);

			GamepadPlacementState.ToolEngaged = true;
			GamepadPlacementState.GridCursor = _cursor;

			// A genuine real mouse click this frame is let through untouched to the real, unmodified
			// CursorTool.ProcessSelectObject (a normal-priority processor running later this same
			// frame) instead of being forced to false like every other frame below - see the class
			// comment above for why. Active false for just this one frame is enough: CameraService's
			// ray patch is irrelevant here (this controller builds its own ray by hand, never through
			// CameraService), so the only other thing Active gates is MouseOverUI, which needs to read
			// real too - a click landing on a UI element must not also try to select whatever the
			// gamepad's box happens to be sitting on. Every other frame keeps forcing
			// MainMouseButtonDown/Held/Up false so this controller's own gamepad-driven UIConfirm below
			// is the only thing that can select/deselect through it.
			var mouseClickDown = _keyBindingRegistry.IsDown(MouseLeftKey);
			GamepadPlacementState.Active = !mouseClickDown;

			// Publish every active frame, even though nothing else reads this controller's own hand-
			// built rays through it - a leftover value from a different tool must never bleed into this
			// one. See GamepadPlacementState's own comment on CursorRayOriginHeight and this mod's notes
			// on the shared-static clear hazard.
			GamepadPlacementState.CursorRayOriginHeight = _rayOriginHeight;

			if (!mouseClickDown)
			{
				GamepadPlacementState.MainMouseButtonDown = false;
				GamepadPlacementState.MainMouseButtonHeld = false;
				GamepadPlacementState.MainMouseButtonUp = false;
			}

			var hasTarget = TryPickSelectable(out var selectable);
			if (hasTarget)
			{
				_rollingHighlighter.HighlightPrimary(selectable, EntityHighlightColor);
			}
			else
			{
				_rollingHighlighter.UnhighlightAllPrimary();

				// Same mechanism BlockObjectSelectionDrawerPatch already relies on for the demolition/
				// priority/deletion tools' own empty-cell cursor - a degenerate one-cell "rectangle",
				// rather than a hand-drawn tile mesh. That drawer is proven visible in real play; an
				// earlier attempt at a standalone MeshDrawer tile here was not (see
				// GamepadAreaSelectionController's own comment about a "separate reticle" being the
				// wrong fix for the same class of problem).
				var xy = new Vector2Int(_cursor.x, _cursor.y);
				_cursorBoundsDrawer.DrawOnLevel(xy, xy, _cursor.z);
			}

			if (_inputService.UIConfirm)
			{
				if (hasTarget)
				{
					_entitySelectionService.Select(selectable);
				}
				else
				{
					_entitySelectionService.Unselect();
				}
			}
		}

		// Free 3D movement - see GamepadCursorHeight.ApplyFreeHeight. `naturalTop` is a plain integer
		// voxel scan (terrain OR block object, whichever is higher), not a raycast - see
		// GamepadCursorHeight.NaturalTop for why that matters. `_rayOriginHeight` is what
		// TryPickSelectable's own hand-built ray (and the box drawn below when nothing is hit) uses -
		// GamepadCursorHeight.RayHeight while the offset is zero (identical to this controller's old,
		// always-track-the-top behaviour), cursor.z + 1 once the player has genuinely moved away from
		// that natural top.
		private void RefreshCursorHeight(int heightStep)
		{
			var naturalTop = GamepadCursorHeight.NaturalTop(_terrainService, _blockService, _cursor.x, _cursor.y,
				_mapSize.TotalSize.z);
			_cursor.z = GamepadCursorHeight.ApplyFreeHeight(naturalTop, ref _heightLocked, ref _lockedHeight,
				heightStep, _mapSize.TotalSize.z);
			_rayOriginHeight = _heightLocked ? _cursor.z + 1f : GamepadCursorHeight.RayHeight;
		}

		// Same ray CameraServicePlacementPatch builds for the InGridSpace patch, only converted to
		// world space instead of returned in grid space: SelectableObjectRaycaster runs
		// Physics.Raycast, which only understands real GameObject colliders in world space.
		//
		// Physics.Raycast has no max distance here (SelectableObjectRaycaster doesn't take one), so a
		// straight-down ray from _rayOriginHeight always hits the *nearest* thing below it, however far
		// below that is - including something the player has long since risen well above. Left
		// unchecked, raising the cursor above e.g. a stack of levees kept re-selecting the same top
		// levee on every further press with no visible change, since there's nothing else up there to
		// hit and a plain hit test can't tell "genuinely nothing here" from "something far below". Only
		// accepting a hit whose own grid height lands within one cell of the cursor's own is what makes
		// rising into genuine empty air actually deselect.
		private bool TryPickSelectable(out SelectableObject selectable)
		{
			var gridRay = new Ray(new Vector3(_cursor.x + 0.5f, _cursor.y + 0.5f, _rayOriginHeight), Down);
			var worldRay = CoordinateSystem.GridToWorld(gridRay);
			if (!_selectableObjectRaycaster.TryHitSelectableObjectIncludeTerrainStump(worldRay, out selectable,
				out var hit))
			{
				return false;
			}

			var hitGridZ = CoordinateSystem.WorldToGrid(hit.point).z;
			if (hitGridZ < _cursor.z - 1f)
			{
				selectable = null;
				return false;
			}

			return true;
		}

		// A toggle: pressed while already engaged, backs straight out - a second, independent way to
		// exit besides staged B (see the UICancel branch above), on a button that never touches the
		// shared Cancel signal at all, so it can't trip GamepadSelectModeCancelGate or CursorTool's own
		// Cancel-driven deselect.
		private void ToggleSelectMode(ITool activeTool)
		{
			if (_engaged)
			{
				Disengage();
				return;
			}

			if (activeTool is CursorTool)
			{
				Engage();
				return;
			}

			// ActiveTool won't read as CursorTool until next frame, so there's nothing to Engage() onto
			// yet - arm instead and let the normal !_engaged/_armPending path above pick it up as soon
			// as it does. One frame of delay, imperceptible.
			_toolService.SwitchToDefaultTool();
			_armPending = true;
		}

		private void Engage()
		{
			_engaged = true;
			_stepReader.Reset();
			_heightStepReader.Reset();
			_heightLocked = false;
			_rayOriginHeight = GamepadCursorHeight.RayHeight;
			_handoff.Reset();

			// Seeds through the camera via PickTerrainCoordinates rather than a fixed-height plane -
			// see GamepadBuildingPlacementController.Activate for why.
			var screenCentre = new Vector2(Screen.width / 2f, Screen.height / 2f);
			var ray = _cameraService.ScreenPointToRayInGridSpace(screenCentre);
			var picked = _terrainPicker.PickTerrainCoordinates(ray);
			_cursor = picked?.CoordinatesWithFaceOffset ?? Vector3Int.zero;

			// Same call BlockObjectTool/BlockObjectDeletionTool<T> get for free from
			// Timberborn.ToolSystemUI.ToolWaterToggler, which hides water for any non-default tool -
			// CursorTool never triggers that (it *is* the default tool, so IsDefaultToolActive never
			// flips while this submode is armed), so it has to be done by hand here instead.
			_waterOpacityToggle.HideWater();
		}

		private void Disengage()
		{
			if (!_engaged)
			{
				return;
			}

			_engaged = false;
			_rollingHighlighter.UnhighlightAllPrimary();
			_waterOpacityToggle.ShowWater();
			_inputService.ShowCursor();
			GamepadPlacementState.Clear();
		}

		private void ReportFailure(Exception e)
		{
			GamepadPlacementState.Clear();
			_inputService.ShowCursor();

			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] Gamepad selection failed: {e}");
		}
	}
}
