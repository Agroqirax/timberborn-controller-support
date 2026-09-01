using System;
using System.Reflection;
using HarmonyLib;
using Timberborn.CameraSystem;
using Timberborn.Coordinates;
using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.SingletonSystem;
using Timberborn.ToolSystem;
using Timberborn.ZiplineSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Drives ZiplineConnectionAddingTool (opened from a zipline tower's entity panel "Add connection"
	// button, ZiplineConnectionButtonFactory) with the gamepad. Unlike every other tool this mod
	// bridges, the stick deliberately does not move a cursor over the map - the tool picks a single
	// ZiplineTower out of a fixed, known set (ZiplineTowerRegistry.ZiplineTowers), so a moving cursor
	// would have to sweep across empty ground between towers for no reason. Instead each stick push
	// (camera-relative, via GamepadGridStepReader - "up" always means "away from the camera") jumps
	// _selected straight to whichever other tower is the best match in that direction from wherever
	// it currently is, the same "Next" idiom SpatialNavigator uses for UI, worked out fresh here since
	// SpatialNavigator's row/column/overlap rules are built for axis-aligned screen rects and don't
	// transfer to towers scattered freely across the map.
	//
	// GamepadPlacementState.GridCursor is set to the selected tower's own cable-anchor cell every
	// active frame, which is enough on its own: ZiplineConnectionAddingTool.ProcessInput reads the
	// hovered tower through SelectableObjectRaycaster.TryHitSelectableObject(), which bottoms out in
	// CameraService.ScreenPointToRayInWorldSpace(mouse position) - already redirected through
	// GridCursor whenever GamepadPlacementState.Active by CameraServicePlacementPatch.WorldSpacePrefix
	// - and its MainMouseButtonDown/MouseOverUI reads are already redirected the same way by
	// InputServicePlacementPatch. No tool-specific Harmony patch is needed; this is the same free ride
	// GamepadAreaSelectionController's own comment describes for DemolishBlueprintTool. The tool's own
	// ZiplinePreviewCableRenderer.DrawPreview call (every ProcessInput frame, gamepad or not) then
	// draws the preview cable to whatever GridCursor resolved to, which is also the only "you're
	// currently aiming at this one" feedback this controller needs - no highlight of its own to draw.
	//
	// One highlight this controller DOES have to clean up, though: DrawPreview's obstructed-path
	// branch (DrawUnconnectableCable) calls RollingHighlighter.HighlightPrimary on whatever buildings
	// are blocking the cable, in red - but the connectable branch never touches the highlighter at
	// all, and HidePreview (the only other place that clears it) only runs when the hovered tower
	// actually goes null. A real mouse gets that clear for free just by crossing empty ground between
	// two towers; a gamepad jump never does; land on a clear tower right after an obstructed one and
	// the previous tower's blocking buildings stay lit red until another obstructed tower happens to
	// overwrite the highlighter's own diff.
	//
	// Fixing this needs the tool's own ZiplinePreviewCableRenderer, not a fresh one of this
	// controller's own: RollingHighlighter/Highlighter are both bound AsTransient (SelectionSystem-
	// Configurator), and HighlightableObject keys each of its highlight colours by the exact Highlighter
	// *instance* that requested it (HighlightableObject._primaryColors is a List<(Highlighter, Color)>,
	// matched by reference) - so a constructor-injected RollingHighlighter of this class's own would be
	// a completely different token, and calling UnhighlightAllPrimary on it would not touch anything
	// the tool's own instance highlighted (confirmed the hard way: an earlier version of this fix did
	// exactly that and the highlight persisted). PreviewRendererField/HidePreviewMethod instead reach
	// into the live ZiplineConnectionAddingTool's own private _ziplinePreviewCableRenderer field by
	// reflection and call its public HidePreview() directly - the exact method+instance the tool itself
	// would use, which correctly unhighlights through the same Highlighter token that lit it up, hides
	// the preview cable and tooltip, and gets redrawn fresh (or left hidden) by the tool's own
	// ProcessInput later this same frame.
	//
	// Deliberately does NOT filter the stick to only the towers ZiplineConnectionAddingTool/
	// ConnectionCandidates would consider valid (free slots, distance, inclination, matching
	// district...) - CanBeConnected already gates the actual connect click, and ZiplinePreviewTooltip
	// already explains exactly which rule failed for whatever is currently hovered. Hiding invalid
	// towers from the stick would hide that explanation along with them: the player could see *that* a
	// nearby tower can't be reached, never *why* - and could never aim at it to find out.
	//
	// One exception: a tower already connected to the origin (origin.IsConnectedTo(candidate)) IS
	// excluded. ZiplinePreviewCableRenderer.DrawPreview draws nothing at all for an already-connected
	// pair (there is no "already connected" preview state, unlike every other invalid reason), so
	// landing the cursor there would silently blank the preview with no visual trace of where the
	// gamepad's own selection went - effectively losing the cursor rather than showing an explainable
	// invalid state.
	//
	// Needs no special-casing for the optional "Zipline Levee" workshop mod (3428936016): it ships no
	// code at all, just a building whose blueprint carries the base game's own ZiplineTowerSpec, so it
	// self-registers into ZiplineTowerRegistry exactly like a vanilla pole or entrance and this
	// controller sees it automatically, present or not.
	internal class GamepadZiplineConnectionController : ILoadableSingleton, IUnloadableSingleton, IPriorityInputProcessor
	{
		private const float FailureLogInterval = 30f;

		// Matches the Id of the game's own KeyBinding.Confirm.blueprint.json, same as
		// GamepadAreaSelectionController - reading it through InputService keeps this rebindable.
		private const string ConfirmKey = "Confirm";

		// How strongly a candidate off to the side of the pushed direction is penalised relative to one
		// straight ahead - towers aren't grid-aligned the way UI rows/columns are, so (unlike
		// SpatialNavigator's UI navigation) an absolute cross-axis cutoff would too easily leave the
		// player with nothing to jump to; a heavy weight on the perpendicular term instead favours a
		// closer, better-aligned tower over a farther, near-perfectly-aligned one without ever ruling a
		// candidate out entirely.
		private const float PerpendicularWeight = 3f;

		// ZiplineConnectionAddingTool is internal to Timberborn.ZiplineSystemUI - matched by name, the
		// same way every other internal tool type this mod recognises is (see
		// GamepadAreaSelectionController.InternalAreaSelectionToolTypeNames). AccessTools.TypeByName
		// resolves this purely at runtime, so no reference to that assembly is needed in the asmdef.
		private const string ToolTypeName = "Timberborn.ZiplineSystemUI.ZiplineConnectionAddingTool";

		// _currentZiplineTower is a private field with no public getter - SwitchTo(ZiplineTower) only
		// ever sets it, both for the initial entry from ZiplineConnectionButtonFactory and for the
		// tool's own "keep connecting" chain in Connect(). Read by reflection once per active frame
		// rather than patched, since this mod only ever needs to read it.
		private static readonly Type ZiplineToolType = AccessTools.TypeByName(ToolTypeName);

		private static readonly FieldInfo OriginField =
			ZiplineToolType == null ? null : AccessTools.Field(ZiplineToolType, "_currentZiplineTower");

		// See the class comment above - the tool's own preview renderer instance, reached by reflection
		// so a selection change made by the stick can clear whatever it last highlighted through the
		// same Highlighter token that lit it, rather than a token of this controller's own.
		private static readonly FieldInfo PreviewRendererField =
			ZiplineToolType == null ? null : AccessTools.Field(ZiplineToolType, "_ziplinePreviewCableRenderer");

		private static readonly MethodInfo HidePreviewMethod =
			PreviewRendererField == null ? null : AccessTools.Method(PreviewRendererField.FieldType, "HidePreview");

		private readonly InputService _inputService;
		private readonly CameraService _cameraService;
		private readonly ToolService _toolService;
		private readonly PanelTracker _panelTracker;
		private readonly ZiplineTowerRegistry _ziplineTowerRegistry;
		private readonly KeyBindingRegistry _keyBindingRegistry;

		private readonly GamepadGridStepReader _stepReader = new GamepadGridStepReader();
		private readonly ConfirmReleaseGate _confirmGate;
		private readonly GamepadMouseHandoff _handoff;

		private bool _active;
		private ZiplineTower _origin;
		private ZiplineTower _selected;
		private object _previewRenderer;
		private float _nextFailureLogTime;

		public GamepadZiplineConnectionController(InputService inputService, CameraService cameraService,
			ToolService toolService, PanelTracker panelTracker, ZiplineTowerRegistry ziplineTowerRegistry,
			KeyBindingRegistry keyBindingRegistry, RecentInputDeviceTracker recentInputDeviceTracker)
		{
			_inputService = inputService;
			_cameraService = cameraService;
			_toolService = toolService;
			_panelTracker = panelTracker;
			_ziplineTowerRegistry = ziplineTowerRegistry;
			_keyBindingRegistry = keyBindingRegistry;
			_confirmGate = new ConfirmReleaseGate(inputService);
			_handoff = new GamepadMouseHandoff(keyBindingRegistry, recentInputDeviceTracker);
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
			GamepadTooltipAnchor.CameraService = _cameraService;
		}

		// See GamepadBuildingPlacementController.Unload for why an IPriorityInputProcessor can never be
		// unregistered - the same MainMouseButtonUp-starvation risk applies here, since this drives the
		// same GamepadPlacementState-fed patches every other controller does.
		public void Unload()
		{
			GamepadPlacementState.Clear();
			ClearTooltipAnchor();
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
			var activeTool = _toolService.ActiveTool;
			if (gamepad == null || OriginField == null || HidePreviewMethod == null
				|| activeTool?.GetType().FullName != ToolTypeName)
			{
				Deactivate();
				return;
			}

			// See GamepadAreaSelectionController.Update - a dialog raised while this tool stays active
			// underneath it (e.g. the district-mismatch/too-far confirmation the base game doesn't
			// actually raise here, but the pattern is identical to every other tool this mod bridges)
			// needs the real cursor back rather than a full Deactivate.
			if (_panelTracker.HasStackedPanel)
			{
				GamepadPlacementState.Clear();
				ClearTooltipAnchor();
				return;
			}

			var origin = (ZiplineTower)OriginField.GetValue(activeTool);
			_previewRenderer = PreviewRendererField.GetValue(activeTool);
			if (!_active || origin != _origin)
			{
				Activate(origin);
			}

			if (!_selected)
			{
				SetSelected(NearestOtherTower(_origin, _ziplineTowerRegistry.ZiplineTowers));
			}

			GamepadPlacementState.ToolEngaged = true;

			if (!_selected)
			{
				// Nothing else on the map to connect to - nothing for the stick to jump between, but a
				// real mouse click still works untouched below since Active stays false.
				GamepadPlacementState.Active = false;
				ClearTooltipAnchor();
				return;
			}

			var step = _stepReader.ReadStep(_keyBindingRegistry, _cameraService.HorizontalAngle);
			var confirmDown = _inputService.IsKeyDown(ConfirmKey);

			if (!_handoff.Update(step, confirmDown))
			{
				// The real mouse is driving this frame - stand fully down, same as
				// GamepadAreaSelectionController does, and leave _selected exactly where the stick last
				// left it for whenever the gamepad resumes. Clearing the world anchor here is what lets
				// ZiplinePreviewTooltip go back to following the real cursor while the mouse is in
				// control, rather than staying pinned to wherever the stick last left it.
				GamepadPlacementState.Active = false;
				GamepadPlacementState.MainMouseButtonDown = false;
				GamepadPlacementState.MainMouseButtonHeld = false;
				GamepadPlacementState.MainMouseButtonUp = false;
				ClearTooltipAnchor();
				return;
			}

			if (step != Vector2Int.zero)
			{
				var next = PickNext(_selected, _origin, _ziplineTowerRegistry.ZiplineTowers, step);
				if (next)
				{
					SetSelected(next);
				}
			}

			GamepadPlacementState.Active = true;
			GamepadPlacementState.GridCursor = _selected.CableAnchorPointInt;

			// Every writer of GridCursor has to publish this too, every active frame, or a value another
			// controller left behind decides where this one's ray starts - see GamepadPlacementState's
			// own comment on the field and this mod's notes on the shared-static clear/write hazard.
			// This controller was the one that didn't: with 3D cursor movement, a demolish or priority
			// cursor dialled down below its column's top leaves cursor.z + 1 behind, and the very next
			// frame the zipline tool's ray would have originated from inside the tower it was trying to
			// hit. RayHeight is the right answer here regardless of levels - the anchor cell is the top
			// of the tower, so a ray from clear air straight down its column always finds it.
			GamepadPlacementState.CursorRayOriginHeight = GamepadCursorLevels.RayHeight;

			// Midpoint of the two towers being connected - the same "here's what you're looking at"
			// spot the preview cable itself spans, so the tooltip sits between them rather than at
			// wherever the real mouse happens to be resting while the stick drives the tool.
			// CableAnchorPoint is grid space (x, y=north/south, z=height, see ZiplineTower.CableAnchorPoint
			// and CoordinateSystem.WorldToGrid/GridToWorld) - GamepadTooltipAnchor.WorldPosition and
			// CameraService.WorldSpaceToPanelSpace both expect real Unity world space (y=up), the same
			// conversion ZiplineCableModel does before rendering the actual cable mesh.
			var midpointGrid = (_origin.CableAnchorPoint + _selected.CableAnchorPoint) / 2f;
			GamepadTooltipAnchor.WorldPosition = CoordinateSystem.GridToWorld(midpointGrid);

			// See ConfirmReleaseGate - suppresses the same physical Confirm press that just clicked the
			// entity panel's "Add connection" button (or the previous tower this chained from) from
			// also reading as an immediate connect at the freshly-seeded nearest tower.
			if (_confirmGate.ShouldSuppress())
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
		}

		// Picks the tower whose cable-anchor point is the best match for the pushed direction, starting
		// from "current"'s own anchor point rather than the origin's - so repeated pushes the same way
		// walk further out instead of always re-scoring from the tower the player started at.
		// Grid-plane (x, y) only, matching every other camera-relative stick read in this mod - height
		// (z) plays no part in "which way did the player push".
		private static ZiplineTower PickNext(ZiplineTower current, ZiplineTower origin,
			Timberborn.Common.ReadOnlyList<ZiplineTower> all, Vector2Int step)
		{
			var direction = new Vector2(step.x, step.y).normalized;
			var from = ToPlane(current.CableAnchorPoint);

			ZiplineTower best = null;
			var bestScore = float.MaxValue;
			for (var i = 0; i < all.Count; i++)
			{
				var candidate = all[i];
				if (candidate == origin || candidate == current || origin.IsConnectedTo(candidate))
				{
					continue;
				}

				var delta = ToPlane(candidate.CableAnchorPoint) - from;
				var forward = Vector2.Dot(delta, direction);
				if (forward <= 0f)
				{
					continue;
				}

				var perpendicular = (delta - forward * direction).magnitude;
				var score = forward + perpendicular * PerpendicularWeight;
				if (score < bestScore)
				{
					bestScore = score;
					best = candidate;
				}
			}

			return best;
		}

		private static ZiplineTower NearestOtherTower(ZiplineTower origin,
			Timberborn.Common.ReadOnlyList<ZiplineTower> all)
		{
			ZiplineTower nearest = null;
			var nearestDistance = float.MaxValue;
			for (var i = 0; i < all.Count; i++)
			{
				var candidate = all[i];
				if (candidate == origin || origin.IsConnectedTo(candidate))
				{
					continue;
				}

				var distance = Vector3.Distance(origin.CableAnchorPoint, candidate.CableAnchorPoint);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearest = candidate;
				}
			}

			return nearest;
		}

		private static Vector2 ToPlane(Vector3 gridPosition)
		{
			return new Vector2(gridPosition.x, gridPosition.y);
		}

		// ZiplinePreviewTooltip never calls Tooltip.Enable (see GamepadTooltipAnchor.WorldPosition's own
		// comment), so GamepadTooltipAnchor.Current is never touched by this tool at all and can be left
		// holding a stale VisualElement from an unrelated gamepad UI hover earlier in the session -
		// nothing hovering the HUD while the mouse is over the game world ever overwrites it back to
		// null. Left alone, GamepadTooltipPositionPatch would fall through to that stale element the
		// instant WorldPosition itself clears, instead of all the way through to the real mouse-cursor
		// positioning - so both anchors have to clear together, not just this controller's own.
		private static void ClearTooltipAnchor()
		{
			GamepadTooltipAnchor.WorldPosition = null;
			GamepadTooltipAnchor.Current = null;
		}

		// See the class comment above - clears whatever the tool's own ZiplinePreviewCableRenderer last
		// highlighted red for the tower being left, through its own Highlighter instance, so it does
		// not linger once GridCursor moves on to a different one this same frame.
		private void SetSelected(ZiplineTower tower)
		{
			HidePreviewMethod.Invoke(_previewRenderer, null);
			_selected = tower;
		}

		private void Activate(ZiplineTower origin)
		{
			_active = true;
			_origin = origin;
			_selected = null;
			_stepReader.Reset();
			_confirmGate.Arm();
			_handoff.Reset();
		}

		private void Deactivate()
		{
			if (!_active)
			{
				return;
			}

			_active = false;
			_origin = null;
			_selected = null;
			GamepadPlacementState.Clear();
			ClearTooltipAnchor();
		}

		private void ReportFailure(Exception e)
		{
			GamepadPlacementState.Clear();
			ClearTooltipAnchor();

			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] Gamepad zipline connection failed: {e}");
		}
	}
}
