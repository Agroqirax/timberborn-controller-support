using System;
using System.Collections.Generic;
using Timberborn.BlockObjectTools;
using Timberborn.BlockSystem;
using Timberborn.CameraSystem;
using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.MapStateSystem;
using Timberborn.SingletonSystem;
using Timberborn.TerrainQueryingSystem;
using Timberborn.TerrainSystem;
using Timberborn.ToolSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Drives building placement with the gamepad: the left stick/d-pad nudges the ghost one voxel
	// at a time, A places (held while moving, drags a line/path the same way a mouse-drag would).
	// LB/RB (rotate) and Y (flip) are handled natively now - see the mod's RotateClockwise/
	// RotateCounterclockwise/Flip keybinding blueprints - since BlockObjectPlacementPanel's own
	// BindableButtons already call PreviewPlacement for those once a gamepad control is registered
	// as their secondary binding, and it also skips Flip on objects that aren't flippable, which
	// this controller's own unconditional call never did.
	//
	// There is no public seam into AreaSelectionController - the class that actually owns ghost
	// preview, drag and placement for every BlockObjectTool - so this never talks to it directly.
	// Instead it tracks its own grid cursor and publishes it, plus synthetic button edges, through
	// GamepadPlacementState for CameraServicePlacementPatch and InputServicePlacementPatch to hand
	// back wherever the game reads a screen-derived ray or mouse button state. Everything downstream
	// of that - preview, validation, placement - runs completely unmodified.
	//
	// Must be a priority processor, not a regular one: BlockObjectTool is a regular processor too,
	// registered later (when the tool is entered) and so run earlier than this in the reverse-order
	// regular chain. On the exact frame MainMouseButtonUp is true, AreaSelectionController commits
	// and returns true, which stops the chain before this ever gets a turn to reset that flag back
	// to false - MainMouseButtonUp then reads true forever, AreaSelectionController nulls its ray and
	// returns true on every subsequent frame too, and nothing downstream (including B-cancel) ever
	// runs again. A priority processor always refreshes the state before anything can read it, every
	// frame, with no way for another processor to starve it - the one guarantee this cannot give up.
	internal class GamepadBuildingPlacementController : ILoadableSingleton, IUnloadableSingleton, IPriorityInputProcessor
	{
		private const float FailureLogInterval = 30f;

		// Matches the Id of the game's own KeyBinding.Confirm.blueprint.json - reading it through
		// InputService rather than the raw A/South button keeps this rebindable, same as everywhere
		// else in this mod.
		private const string ConfirmKey = "Confirm";

		private readonly InputService _inputService;
		private readonly CameraService _cameraService;
		private readonly ToolService _toolService;
		private readonly TerrainPicker _terrainPicker;
		private readonly GamepadCursorLevels _cursorLevels;
		private readonly PanelTracker _panelTracker;
		private readonly KeyBindingRegistry _keyBindingRegistry;

		private readonly GamepadGridStepReader _stepReader = new GamepadGridStepReader();
		private readonly GamepadHeightStepReader _heightStepReader = new GamepadHeightStepReader();
		private readonly ConfirmReleaseGate _confirmGate;
		private readonly GamepadMouseHandoff _handoff;

		private bool _active;
		private Vector3Int _cursor;

		// See GamepadCursorHeight.ApplyFreeHeight - false means "follow the surface, exactly like before
		// 3D cursor movement existed"; a CursorHeightUp/Down press locks it true and _lockedHeight
		// becomes the absolute height from then on, independent of whatever column the cursor is over.
		private bool _heightLocked;
		private int _lockedHeight;

		// Last published GamepadPlacementState.CursorRayOriginHeight, kept so a drag can go on
		// republishing the value from its own press frame - see the drag branch in Update.
		private float _rayOriginHeight = GamepadCursorLevels.RayHeight;

		private float _nextFailureLogTime;

		public GamepadBuildingPlacementController(InputService inputService, CameraService cameraService,
			ToolService toolService, TerrainPicker terrainPicker, GamepadCursorLevels cursorLevels,
			PanelTracker panelTracker, KeyBindingRegistry keyBindingRegistry,
			RecentInputDeviceTracker recentInputDeviceTracker)
		{
			_inputService = inputService;
			_cameraService = cameraService;
			_toolService = toolService;
			_terrainPicker = terrainPicker;
			_cursorLevels = cursorLevels;
			_panelTracker = panelTracker;
			_keyBindingRegistry = keyBindingRegistry;
			_confirmGate = new ConfirmReleaseGate(inputService);
			_handoff = new GamepadMouseHandoff(keyBindingRegistry, inputService, recentInputDeviceTracker);
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
		}

		// InputService has no way to remove an IPriorityInputProcessor once added - only the plain
		// IInputProcessor overload of RemoveInputProcessor exists. That is a real engine gap, but the
		// alternative (a regular processor) can be starved by exactly the deadlock described above,
		// which is worse: a stale registration surviving a scene reload is at most cosmetic jitter
		// from two writers, never a hard freeze. Clearing the shared state still matters so nothing
		// outlives the tool that was driving it.
		public void Unload()
		{
			GamepadPlacementState.Clear();
			_inputService.ShowCursor();
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
			var gamepad = Gamepad.current;
			if (gamepad == null || !IsBuildingPlacementTool(_toolService.ActiveTool))
			{
				Deactivate();
				return;
			}

			// A dialog raised mid-placement - not enough science points, confirm unlocking a
			// building, entering a settlement name - stacks on top while the BlockObjectTool stays
			// active underneath it, since none of these switch tools. Left alone, this kept feeding A
			// to the world as a synthetic click and starved GamepadNavigationInputProcessor of the
			// stick exactly the way it starves itself during real placement, so the dialog's own
			// buttons were never reachable. Clearing published state (not a full Deactivate) hands the
			// gamepad back to UI navigation for as long as the dialog is up, while keeping _active and
			// _cursor intact so placement resumes exactly where it left off once it closes, rather than
			// reseeding at screen centre.
			if (_panelTracker.HasStackedPanel)
			{
				GamepadPlacementState.Clear();

				// The dialog needs the real cursor visible to be clickable - re-hidden by _handoff on
				// whatever frame the dialog closes and the gamepad resumes driving.
				_inputService.ShowCursor();
				return;
			}

			if (!_active)
			{
				Activate();
			}

			var step = _stepReader.ReadStep(_keyBindingRegistry, _cameraService.HorizontalAngle);
			var confirmDown = _inputService.IsKeyDown(ConfirmKey);

			// Read before the handoff, and fed into it - see GamepadAreaSelectionController.Update for
			// why a height press has to be able to take control back from the mouse on its own.
			var heightStep = _heightStepReader.ReadStep(_inputService);

			// The tool is engaged this frame regardless of which device ends up driving the cursor
			// below - GamepadNavigationInputProcessor reads this one, not Active, so it keeps
			// standing down for the whole time the tool is up rather than just the frames the stick
			// happens to be the one moving it. See GamepadPlacementState.ToolEngaged.
			GamepadPlacementState.ToolEngaged = true;

			if (_handoff.Update(step, confirmDown || heightStep != 0))
			{
				if (step != Vector2Int.zero)
				{
					var clamped = _cursorLevels.ClampToMap(new Vector2Int(_cursor.x + step.x, _cursor.y + step.y));
					_cursor.x = clamped.x;
					_cursor.y = clamped.y;
				}

				// Evaluated here rather than just before the button state is published, because the drag
				// check below needs it: a Confirm still held over from the press that opened this tool
				// isn't a drag, it's a press this controller is deliberately swallowing.
				var suppressConfirm = _confirmGate.ShouldSuppress();

				if (!suppressConfirm && !confirmDown && _inputService.IsKeyHeld(ConfirmKey))
				{
					// Mid-drag (a line of paths, a rectangle of platforms). AreaPicker.GetEndCoords
					// resolves the far end by intersecting the end ray with a horizontal plane at the
					// start placement's own ReferenceTerrainLevel, and Plane.Raycast returns false for a
					// ray that starts *below* the plane it is asked about - so a ghost that dropped to a
					// lower level partway through a drag made the end resolve to nothing and the line
					// silently collapsed back to its single starting tile. The drag is flat either way
					// (AreaIterator pins every coordinate to start.z), so holding both the level and the
					// published ray origin still for the duration costs nothing.
					GamepadPlacementState.CursorRayOriginHeight = _rayOriginHeight;
				}
				else
				{
					// Free movement in z. The ghost is not pinned to the set of heights a building could
					// sit at: the player says where the cursor is and the placement picker decides what
					// that resolves to, exactly as it does for a mouse. Unlocked (no height key pressed
					// yet) it just tracks the column's topmost surface, which is what a mouse would be
					// pointing at, and keeps the obstruction-proof RayHeight ray origin with it.
					var xy = new Vector2Int(_cursor.x, _cursor.y);
					_cursor.z = GamepadCursorHeight.ApplyFreeHeight(_cursorLevels.SurfaceTop(xy), ref _heightLocked,
						ref _lockedHeight, heightStep, _cursorLevels.CeilingExclusive);
					GamepadPlacementState.CursorRayOriginHeight = _rayOriginHeight =
						_heightLocked ? GamepadCursorLevels.RayOriginFor(_cursor.z) : GamepadCursorLevels.RayHeight;
				}

				GamepadPlacementState.Active = true;
				GamepadPlacementState.GridCursor = _cursor;

				// See ConfirmReleaseGate: confirmed via Player.log that AreaSelectionController's own
				// action-commit check only requires a hover ray to exist (true on essentially every
				// idle frame) rather than a genuine prior Down, so the stale MainMouseButtonUp on the
				// release tail of the same press that confirmed this building's bottom-bar button was
				// enough to auto-place at the freshly center-seeded cursor with no real press ever
				// happening.
				if (suppressConfirm)
				{
					GamepadPlacementState.MainMouseButtonDown = false;
					GamepadPlacementState.MainMouseButtonHeld = false;
					GamepadPlacementState.MainMouseButtonUp = false;
				}
				else
				{
					GamepadPlacementState.MainMouseButtonDown = confirmDown;
					GamepadPlacementState.MainMouseButtonHeld = _inputService.IsKeyHeld(ConfirmKey);
					GamepadPlacementState.MainMouseButtonUp = _inputService.IsKeyUp(ConfirmKey);
				}

				return;
			}

			// The real mouse is driving this frame - stand fully down (not Clear(), which would also
			// drop ToolEngaged above) and let CameraServicePlacementPatch/InputServicePlacementPatch
			// pass everything through to the real mouse untouched. Still keep _cursor in sync with
			// wherever the mouse actually points, purely so a later stick nudge resumes from there
			// instead of snapping back to the last gamepad-tracked cell.
			GamepadPlacementState.Active = false;
			GamepadPlacementState.MainMouseButtonDown = false;
			GamepadPlacementState.MainMouseButtonHeld = false;
			GamepadPlacementState.MainMouseButtonUp = false;

			var mouseRay = _cameraService.ScreenPointToRayInGridSpace(_inputService.MousePosition);
			var mousePicked = _terrainPicker.PickTerrainCoordinates(mouseRay);
			if (mousePicked.HasValue)
			{
				_cursor = mousePicked.Value.CoordinatesWithFaceOffset;
				_heightLocked = false;
			}
		}

		// BuildingBlueprints.Tools.BuildBuildingBlueprintTool (optional BuildingBlueprints workshop mod,
		// 3667559269) stamps a saved multi-building group back into the world: its own
		// BlueprintPlacementService.ProcessCursorLocation reads CameraService.ScreenPointToRayInGridSpace
		// and ProcessPlacement reads InputService.MainMouseButtonDown - the exact same primitives
		// BlockObjectTool itself uses for a single-click, then-await-the-next-one placement loop - so it
		// belongs with this controller, not GamepadAreaSelectionController, even though it isn't literally
		// a BlockObjectTool. Rotate/flip already read the mod's own RotateClockwise/RotateCounterclockwise/
		// Flip keybindings (already gamepad-bound); its "Nudge" mode is an alternate WASD-driven movement
		// scheme the tool offers as an option, unrelated to and not replaced by this controller's own
		// stick-driven cursor.
		private static readonly HashSet<string> InternalBuildingPlacementToolTypeNames = new HashSet<string>
		{
			"BuildingBlueprints.Tools.BuildBuildingBlueprintTool",
		};

		// Internal rather than private: GamepadHintResolver reuses this to classify ToolService.ActiveTool
		// the same way this controller already does, instead of re-deriving a second copy of the tool-type
		// walk for the hint strip.
		internal static bool IsBuildingPlacementTool(ITool tool)
		{
			if (tool is BlockObjectTool)
			{
				return true;
			}

			var type = tool?.GetType();
			return type != null && InternalBuildingPlacementToolTypeNames.Contains(type.FullName);
		}

		private void Activate()
		{
			_active = true;
			_stepReader.Reset();
			_heightStepReader.Reset();
			_heightLocked = false;
			_rayOriginHeight = GamepadCursorLevels.RayHeight;
			_confirmGate.Arm();
			_handoff.Reset();

			// The one and only place a screen point is turned into a grid cell: seed the cursor at
			// whatever screen-centre would show a mouse user. GamepadPlacementState.Active is still
			// false at this point, so this genuinely goes through the camera rather than looping back
			// into CameraServicePlacementPatch. Every frame after this one is injected directly, with
			// no camera involved at all.
			//
			// PickTerrainCoordinates walks the ray voxel by voxel until it hits the real ground - the
			// same way a mouse pick works - rather than intersecting a fixed height plane the way
			// FindCoordinatesOnLevelInMap does. That matters because MaxVisibleLevel can sit well above
			// the actual terrain the camera is looking at: intersecting a plane that high pulls the
			// result back towards the camera's own position rather than where it's actually looking,
			// which is what put the seed near the bottom of the screen instead of dead centre.
			var screenCentre = new Vector2(Screen.width / 2f, Screen.height / 2f);
			var ray = _cameraService.ScreenPointToRayInGridSpace(screenCentre);
			// CoordinatesWithFaceOffset, not Coordinates: _cursor.z means the empty cell a building
			// would occupy, and the picker hands back the solid voxel supporting it.
			var picked = _terrainPicker.PickTerrainCoordinates(ray);
			_cursor = picked?.CoordinatesWithFaceOffset ?? Vector3Int.zero;
		}

		// Guarded, not unconditional: this runs every frame BlockObjectTool isn't active, and three
		// separate controllers (this one, GamepadAreaSelectionController, GamepadSelectionController)
		// all share the one static GamepadPlacementState. An unconditional Clear() here would stomp
		// on whichever of the OTHER two is legitimately driving the state this frame, with the
		// outcome depending entirely on which priority processor happens to run last - true even
		// though only one tool can ever be active at a time, since every non-owning controller was
		// still clearing every single frame, not just on the transition away from its own tool. Only
		// clearing once, on the actual _active -> inactive edge, means a controller only ever touches
		// state it itself previously owned.
		private void Deactivate()
		{
			if (!_active)
			{
				return;
			}

			_active = false;
			GamepadPlacementState.Clear();
			_inputService.ShowCursor();
		}

		private void ReportFailure(Exception e)
		{
			// Always clear, not just when the log actually fires - otherwise a recurring failure
			// leaves the ghost frozen on stale state for up to FailureLogInterval seconds instead of
			// cleanly standing down.
			GamepadPlacementState.Clear();
			_inputService.ShowCursor();

			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] Building placement failed: {e}");
		}
	}
}
