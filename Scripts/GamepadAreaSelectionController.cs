using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.AreaSelectionSystem;
using Timberborn.BlockObjectTools;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.CameraSystem;
using Timberborn.Coordinates;
using Timberborn.DemolishingUI;
using Timberborn.ForestryUI;
using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.MapEditorBrushesUI;
using Timberborn.MapEditorNaturalResourcesUI;
using Timberborn.MapStateSystem;
using Timberborn.PlantingUI;
using Timberborn.Rendering;
using Timberborn.SingletonSystem;
using Timberborn.TerrainQueryingSystem;
using Timberborn.TerrainSystem;
using Timberborn.ToolSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Drives every non-building area-selection tool with the gamepad - planting and un-planting
	// (CancelPlantingTool is the eraser shared by the Fields and Forestry/natural-resource planting
	// groups - two bottom-bar entry points for the one tool), tree-cutting-area marking/unmarking
	// (base game, plus the optional Cordial.Mods.CutterTool and SourcePulp.GridCutting workshop mods),
	// creating/demolishing a saved building-group blueprint (optional BuildingBlueprints workshop mod -
	// see the InternalAreaSelectionToolTypeNames comment below for all of these), builder priority,
	// marking/unmarking buildings for demolition, deleting buildings/objects/recovered-good stacks, and -
	// MapEditor only - the absolute/relative terrain height brushes and the natural-resource spawn/removal
	// brushes - the same way GamepadBuildingPlacementController drives BlockObjectTool: the left stick/
	// d-pad nudges a grid cursor one voxel at a time, A presses/holds/releases exactly like a mouse
	// click-drag-release.
	//
	// SculptingTerrainBrushTool (MapEditor only) is in this list too, now that the cursor genuinely
	// moves in z (CursorHeightUp/Down) - it gets free height, the same as every other tool here except
	// the four terrain-snapped ones, since sculpting terrain at a chosen height in mid-air is the whole
	// point of the tool. It reaches AreaSelectionController the same way as everything else here -
	// SculptingTerrainPicker.PickTerrainAreaToAdd/Remove both wrap
	// AreaSelectionController.ProcessInput - so nothing tool-specific was needed beyond adding it to
	// IsAreaSelectionTool below.
	//
	// All of these tools reach AreaSelectionController through a different picker than BlockObjectTool
	// does (SelectionToolProcessor, AreaBlockObjectPicker or AreaBlockObjectAndTerrainPicker rather than
	// the placement path), but that picker still bottoms out in the same
	// AreaSelectionController.ProcessInput, reading the same InputService button getters and
	// CameraService.ScreenPointToRayInGridSpace that InputServicePlacementPatch and
	// CameraServicePlacementPatch already redirect whenever GamepadPlacementState.Active is set -
	// including a click-and-drag rectangle, which falls out of holding A while moving the cursor for
	// free, exactly as line-shaped buildings do. Nothing here needs a tool-specific patch; this class
	// only has to know which tool is active and publish the same bridge state
	// GamepadBuildingPlacementController does.
	//
	// Deliberately not covered: tools that pick a single object under the literal mouse position
	// instead of going through AreaSelectionController at all (SelectableObjectRaycaster-based, e.g.
	// ZiplineConnectionAddingTool, TransmitterPickerTool, DuplicateSettingsTool) - none of them are
	// reached from the main bottom bar, they're opened from an entity panel button, and following the
	// literal mouse the way MouseOverUI/CameraServicePlacementPatch do for area tools would need a
	// different raycast-origin patch, not this one. And dev-mode-only tools (WaterHeightBrushTool,
	// BeaverGeneratorTool, BotGeneratorTool), which aren't reachable in normal play.
	//
	// FPPCameraActivationTool (optional kulesz.FPPCamera mod - see the InternalAreaSelectionToolTypeNames
	// entry below) is a related-but-different case: it doesn't go through AreaSelectionController at all
	// either, but unlike the SelectableObjectRaycaster-only tools above, it reaches world/grid space
	// entirely through Timberborn.CursorToolSystem.CursorCoordinatesPicker, whose two branches both
	// bottom out in CameraService (ScreenPointToRayInGridSpace for the terrain fallback,
	// ScreenPointToRayInWorldSpace via SelectableObjectRaycaster for the "standing on a finished
	// floor/path/stackable" branch) - both already patched by CameraServicePlacementPatch whenever
	// GamepadPlacementState.Active. So this one is covered for free, just by counting as an active tool
	// here; it needs no picker-specific code of its own.
	//
	// Rotate/flip: every other tool here has no orientation to change, but FPPCameraActivationTool does
	// (it places a directional arrow) - it already reads RotateClockwise/RotateCounterclockwise straight
	// off InputService itself rather than through AreaSelectionController, and those are already
	// gamepad-bound (Root/KeyBindings/Objects/KeyBinding.Rotate*.blueprint.json), so that also needs
	// nothing from this class.
	//
	// Doesn't draw its own cursor marker - a separate reticle stacked on top of whatever the tool
	// itself renders turned out to be the wrong fix for "the cursor disappears over an empty cell".
	// PlantingPreviewPatch instead makes PlantingTool's own preview always show something, in red,
	// where it otherwise would have shown nothing - the same fix BlockValidatorPlacementPatch already
	// applies to the building ghost. (TreeCuttingAreaSelectionTool turned out not to need this - an
	// already-selected cell showing nothing extra reads fine on its own - and BuilderPriorityTool/
	// demolish/deletion tools are handled by BlockObjectSelectionDrawerPatch, which only forces the
	// existing selection box on, no colour of its own.) The one thing this class does for
	// PlantingPreviewPatch is create and publish the shared MeshDrawer it draws with, plus the colour
	// itself, in GamepadPlacementState.InvalidTileDrawer/InvalidColor, since a static Harmony patch
	// class has no constructor DI of its own to get a MarkerDrawerFactory or ISpecService from.
	// RecoverableGoodTooltip (BuildingDeconstructionTool's "what you get back" preview) is the same
	// shape of bug GamepadZiplineConnectionController already fixed for ZiplinePreviewTooltip: it shows
	// through ITooltipRegistrar.ShowPriority, which never calls Tooltip.Enable, so GamepadTooltipAnchor.
	// Current is never touched and GamepadTooltipPositionPatch falls through to the real (hidden, unmoved)
	// mouse cursor unless WorldPosition is set. Fixed the same way here, generalized to every frame this
	// class drives an area-selection tool rather than only the deconstruction one - harmless for every
	// other tool in the list since nothing reads WorldPosition unless a priority tooltip is actually
	// showing, and no mouse-hover tooltip can fire while this class hides the OS cursor anyway.
	internal class GamepadAreaSelectionController : ILoadableSingleton, IUnloadableSingleton, IPriorityInputProcessor
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
		private readonly ITerrainService _terrainService;
		private readonly IBlockService _blockService;
		private readonly PanelTracker _panelTracker;
		private readonly MarkerDrawerFactory _markerDrawerFactory;
		private readonly RectangleBoundsDrawerFactory _rectangleBoundsDrawerFactory;
		private readonly ISpecService _specService;
		private readonly KeyBindingRegistry _keyBindingRegistry;
		private readonly MapSize _mapSize;

		private readonly GamepadGridStepReader _stepReader = new GamepadGridStepReader();
		private readonly GamepadHeightStepReader _heightStepReader = new GamepadHeightStepReader();
		private readonly ConfirmReleaseGate _confirmGate;
		private readonly GamepadMouseHandoff _handoff;

		private bool _active;
		private Vector3Int _cursor;

		// See GamepadBuildingPlacementController's own fields of the same names - only meaningful for
		// tools that don't require terrain snapping (see RequiresTerrainSnap).
		private bool _heightLocked;
		private int _lockedHeight;

		private float _nextFailureLogTime;

		public GamepadAreaSelectionController(InputService inputService, CameraService cameraService,
			ToolService toolService, TerrainPicker terrainPicker, ITerrainService terrainService,
			IBlockService blockService, PanelTracker panelTracker, MarkerDrawerFactory markerDrawerFactory,
			RectangleBoundsDrawerFactory rectangleBoundsDrawerFactory, ISpecService specService,
			KeyBindingRegistry keyBindingRegistry, RecentInputDeviceTracker recentInputDeviceTracker,
			MapSize mapSize)
		{
			_inputService = inputService;
			_cameraService = cameraService;
			_toolService = toolService;
			_terrainPicker = terrainPicker;
			_terrainService = terrainService;
			_blockService = blockService;
			_panelTracker = panelTracker;
			_markerDrawerFactory = markerDrawerFactory;
			_rectangleBoundsDrawerFactory = rectangleBoundsDrawerFactory;
			_specService = specService;
			_keyBindingRegistry = keyBindingRegistry;
			_mapSize = mapSize;
			_confirmGate = new ConfirmReleaseGate(inputService);
			_handoff = new GamepadMouseHandoff(keyBindingRegistry, inputService, recentInputDeviceTracker);
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
			GamepadTooltipAnchor.CameraService = _cameraService;
			GamepadPlacementState.InvalidTileDrawer = _markerDrawerFactory.CreateTileDrawer();
			GamepadPlacementState.InvalidColor = GetTreeCuttingNoActionColor();
			GamepadPlacementState.InvalidBoxDrawer = _markerDrawerFactory.CreateLargeBlockTileDrawer();
			GamepadPlacementState.InvalidBoxColor = GetSculptingNegativeColor();

			// Published for optional-mod Harmony patches with no constructor DI of their own (see
			// GamepadPlacementState.BoundsDrawerFactory/SpecService/TerrainPicker) - e.g.
			// BuildingBlueprintsIntegration.DemolishToolPostfix, which needs its own correctly-coloured
			// RectangleBoundsDrawer and the real terrain height under the cursor, neither of which
			// InvalidTileDrawer/InvalidColor above are the right fit for.
			GamepadPlacementState.BoundsDrawerFactory = _rectangleBoundsDrawerFactory;
			GamepadPlacementState.SpecService = _specService;
			GamepadPlacementState.TerrainPicker = _terrainPicker;
		}

		// TreeCuttingColorsSpec is internal to Timberborn.ForestryUI, so it can't be named as a generic
		// argument here the normal way (ISpecService.GetSingleSpec<TreeCuttingColorsSpec>() wouldn't
		// compile) - MakeGenericMethod sidesteps that the same way AccessTools.Method already does for
		// internal methods elsewhere in this mod, since reflection only cares that the runtime type
		// satisfies the `where T : ComponentSpec` constraint, not whether our assembly could name it.
		private Color GetTreeCuttingNoActionColor()
		{
			var specType = AccessTools.TypeByName("Timberborn.ForestryUI.TreeCuttingColorsSpec");
			var spec = typeof(ISpecService).GetMethod(nameof(ISpecService.GetSingleSpec))
				.MakeGenericMethod(specType).Invoke(_specService, null);
			return (Color)AccessTools.Property(specType, "ToolNoActionTile").GetValue(spec);
		}

		// Same reflection trick as GetTreeCuttingNoActionColor - BrushColorSpec is internal to
		// Timberborn.MapEditorBrushesUI. .Negative is the exact red SculptingTerrainBrushTool itself
		// uses for its own "removing something here" box preview, reused rather than inventing a new
		// shade so the invalid-cursor box reads as the same tool's own colour language.
		private Color GetSculptingNegativeColor()
		{
			var specType = AccessTools.TypeByName("Timberborn.MapEditorBrushesUI.BrushColorSpec");
			var spec = typeof(ISpecService).GetMethod(nameof(ISpecService.GetSingleSpec))
				.MakeGenericMethod(specType).Invoke(_specService, null);
			return (Color)AccessTools.Property(specType, "Negative").GetValue(spec);
		}

		// See GamepadBuildingPlacementController.Unload for why this has to stay an IPriorityInputProcessor
		// with no way to unregister it - the same MainMouseButtonUp-starvation deadlock applies here too,
		// since these tools drive the exact same AreaSelectionController.
		public void Unload()
		{
			GamepadPlacementState.Clear();
			ClearTooltipAnchor();
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
			if (gamepad == null || !IsAreaSelectionTool(_toolService.ActiveTool))
			{
				Deactivate();
				return;
			}

			// Deletion tools raise a confirmation dialog (or the odd building-unlock/settlement-name
			// dialog planting can trigger) while staying the active tool underneath it - see
			// GamepadBuildingPlacementController.Update for why clearing published state here, rather
			// than a full Deactivate, is what lets the gamepad reach the dialog's own buttons while
			// keeping the cursor exactly where it was for when the dialog closes.
			if (_panelTracker.HasStackedPanel)
			{
				GamepadPlacementState.Clear();
				ClearTooltipAnchor();

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

			// See GamepadBuildingPlacementController.Update - the tool is engaged this frame
			// regardless of which device ends up driving the cursor below, and
			// GamepadNavigationInputProcessor reads this flag, not Active, to stand down for the
			// tool's whole lifetime rather than only the frames the stick happens to be moving it.
			GamepadPlacementState.ToolEngaged = true;

			if (_handoff.Update(step, confirmDown))
			{
				if (step != Vector2Int.zero)
				{
					var moved = new Vector2Int(_cursor.x + step.x, _cursor.y + step.y);
					var clamped = GamepadCursorHeight.ClampToMap(moved, _mapSize.TerrainSize2D);
					_cursor.x = clamped.x;
					_cursor.y = clamped.y;
				}

				var activeTool = _toolService.ActiveTool;

				// Published every active frame - see SculptingTerrainPickerPatch for why this specific
				// tool needs its own picker patched to make CursorHeightUp/Down do anything at all.
				GamepadPlacementState.SculptingActive = activeTool is SculptingTerrainBrushTool;

				var heightStep = _heightStepReader.ReadStep(_inputService);
				if (RequiresTerrainSnap(activeTool))
				{
					// Can't mark air in between two terrain layers - only ever lands on a real level.
					var xy = new Vector2Int(_cursor.x, _cursor.y);
					_cursor.z = GamepadCursorHeight.ApplyTerrainSnappedHeight(_terrainService, xy, _cursor.z,
						heightStep, out var isTopLevel);
					GamepadPlacementState.CursorRayOriginHeight =
						isTopLevel ? GamepadCursorHeight.RayHeight : _cursor.z + 1f;
				}
				else
				{
					// Free 3D movement for now - every other tool here can be nudged into mid-air.
					// Rules for when/whether that should be allowed come later.
					var naturalTop = GamepadCursorHeight.NaturalTop(_terrainService, _blockService, _cursor.x,
						_cursor.y, _mapSize.TotalSize.z);
					_cursor.z = GamepadCursorHeight.ApplyFreeHeight(naturalTop, ref _heightLocked, ref _lockedHeight,
						heightStep, _mapSize.TotalSize.z);
					GamepadPlacementState.CursorRayOriginHeight =
						_heightLocked ? _cursor.z + 1f : GamepadCursorHeight.RayHeight;
				}

				GamepadPlacementState.Active = true;
				GamepadPlacementState.GridCursor = _cursor;
				GamepadTooltipAnchor.WorldPosition = CoordinateSystem.GridToWorldCentered(_cursor);

				// With a cursor that can be placed anywhere, the sculpting tool's own Add/Remove
				// toggle is redundant on a gamepad - decide instead from whichever cell the cursor is
				// actually on: already terrain means Remove, anything else means Add. Live while idle
				// (so the preview box's colour tracks the cursor immediately - a stale decision here
				// used to leave the preview showing the *previous* press's colour for a whole extra
				// press before it caught up), but frozen for the rest of an actual held press/drag
				// (IsKeyHeld true and this isn't the fresh Down edge) so a drag can't flip modes
				// partway through just because it crossed onto existing terrain - see
				// SculptingTerrainAddRemovePatch, which reads this instead of the tool's own
				// IsIncreasing while gamepad-driven, leaving a mouse user's own toggle untouched.
				if (GamepadPlacementState.SculptingActive
					&& !(_inputService.IsKeyHeld(ConfirmKey) && !confirmDown))
				{
					GamepadPlacementState.SculptAdd = !_terrainService.Underground(_cursor);
				}

				// See ConfirmReleaseGate: without this, the same physical Confirm press that just
				// confirmed this tool's own bottom-bar button reads as a fresh action to the newly
				// active tool - either directly, for a tool that starts (or, for the natural-resource
				// brushes, repeats) on Held rather than Down, or via the stale MainMouseButtonUp on
				// that press's eventual release, which is all AreaSelectionController-driven tools
				// (planting, tree-cutting, priority, demolish, deletion) need to commit at the hover
				// position with no real Down ever having happened this activation.
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

				return;
			}

			// See GamepadBuildingPlacementController.Update - the real mouse is driving this frame,
			// stand fully down (not Clear()) and keep _cursor in sync with it for continuity. Clearing
			// the world anchor here is what lets RecoverableGoodTooltip (and any other priority tooltip)
			// go back to following the real cursor while the mouse is in control.
			GamepadPlacementState.Active = false;
			ClearTooltipAnchor();
			GamepadPlacementState.MainMouseButtonDown = false;
			GamepadPlacementState.MainMouseButtonHeld = false;
			GamepadPlacementState.MainMouseButtonUp = false;

			var mouseRay = _cameraService.ScreenPointToRayInGridSpace(_inputService.MousePosition);
			var mousePicked = _terrainPicker.PickTerrainCoordinates(mouseRay);
			if (mousePicked.HasValue)
			{
				_cursor = mousePicked.Value.Coordinates;
				_heightLocked = false;
			}
		}

		// TreeCuttingAreaUnselectionTool and BuilderPriorityTool are declared `internal` in their own
		// assemblies, so they can't be named with `is TypeName` here - only their full type name is
		// reachable from outside. Matched by name rather than skipped, the same as every other tool in
		// this list, rather than leaving half of a symmetric pair (selection but not unselection,
		// planting but not priority) without gamepad support.
		//
		// FPPCameraActivationTool is here for a different reason: it belongs to the optional
		// kulesz.FPPCamera mod, so its assembly may not even be loaded. A string match against
		// whatever ITool happens to be active costs nothing and never touches the type if that
		// assembly isn't present - unlike FPPCameraAnalogPatch.cs, which has to reach into a live
		// instance's private fields and therefore does need the type resolved, this one doesn't.
		// It's the tool FPPCameraActivationButton opens to place the "where FPP will start" arrow;
		// it already reads MouseOverUI/MainMouseButtonDown and CursorCoordinatesPicker like every
		// other tool here, and already has its own rotate handling via the RotateClockwise/
		// RotateCounterclockwise keybindings (already gamepad-bound, see
		// Root/KeyBindings/Objects/KeyBinding.Rotate*.blueprint.json) - so folding it in here needs
		// no FPP-specific code at all, just this line, plus the CameraServicePlacementPatch world-
		// space companion patch below that CursorCoordinatesPicker's BlockObject-hit branch needs.
		// Cordial.Mods.CutterTool.Scripts.CutterToolService (workshop 3334584916, one configurable
		// tree-cutting tool - pattern/species/stump/sapling/clear-cut options live in a side panel) and
		// SourcePulp.GridCutting.PatternCuttingTool (workshop 3739849811, one type instantiated once per
		// pattern - Checkerboard/Heavy/Sparse/StripesHorizontal/StripesVertical/Thin all show up as this
		// same type, so the one string entry covers all of them) both decompile down to exactly the
		// TreeCuttingAreaSelectionTool shape: they build a Timberborn.SelectionToolSystem.
		// SelectionToolProcessor from the injected SelectionToolProcessorFactory and drive Enter/Exit
		// through it, so they inherit AreaSelectionController for free the same way every other tool in
		// this list does - nothing tool-specific needed beyond recognising them here.
		//
		// This is also the answer to "can this list be generated instead of hand-maintained": every tool
		// above reaches AreaSelectionController by a different route (SelectionToolProcessor for most,
		// a bespoke MainMouseButtonDown/ScreenPointToRayInGridSpace read for the MapEditor brushes), and
		// there is no common base type or marker interface across them - ITool is the only thing they all
		// share, and every other ITool in the game (dialogs, camera tools, single-object pickers) is that
		// too. Reflecting for a SelectionToolProcessor-typed field would catch most of this list but miss
		// the brushes entirely and would just as happily catch SculptingTerrainBrushTool, which is
		// deliberately excluded (see the class comment) - a field-shape probe can't express "except this
		// one". A hand-maintained list matched by name, extended the same way for each new mod, is the
		// actual generalization already in place here.
		//
		// CutterToolPanel (CutterTool's option panel) is a HUD-style absolute item, not a stacked panel,
		// so _panelTracker.HasStackedPanel never sees it and it stays on screen while the tool is active -
		// but GamepadNavigationInputProcessor stands down entirely while ToolEngaged (see
		// GamepadPlacementState.ToolEngaged), so its toggles are reachable by mouse only for now. No base
		// game tool pairs an always-visible side panel with area-selection cursor driving, so there's no
		// existing pattern here to extend; leaving it mouse-only rather than inventing one.
		//
		// BuildingBlueprints.Tools.CreateBuildingBlueprintTool and BuildingBlueprints.Tools.
		// DemolishBlueprintTool (optional BuildingBlueprints workshop mod, 3667559269) are the other two
		// of that mod's three bottom-bar tools - the third, BuildBuildingBlueprintTool, stamps the saved
		// group back into the world with a BlockObjectTool-shaped placement loop and belongs in
		// GamepadBuildingPlacementController instead (see its own InternalBuildingPlacementToolTypeNames).
		// CreateBuildingBlueprintTool drags a rectangle over existing buildings to save as a blueprint via
		// AreaBlockObjectPickerFactory.CreatePickingUpwards() - the exact same AreaBlockObjectPicker family
		// the base game's deletion tools use, just built directly instead of through
		// SelectionToolProcessor. DemolishBlueprintTool is the FPPCameraActivationTool case again: it
		// reads SelectableObjectRaycaster.TryHitSelectableObject, which itself reads
		// CameraService.ScreenPointToRayInWorldSpace(InputService.MousePosition) - already redirected to
		// the gamepad-tracked cell by CameraServicePlacementPatch.WorldSpacePrefix - plus a plain
		// InputService.MainMouseButtonDown click to confirm, so it needs nothing beyond this line either.
		private static readonly HashSet<string> InternalAreaSelectionToolTypeNames = new HashSet<string>
		{
			"Timberborn.ForestryUI.TreeCuttingAreaUnselectionTool",
			"Timberborn.BuilderPrioritySystemUI.BuilderPriorityTool",
			"FPPCamera.FPPCameraActivationTool",
			"Cordial.Mods.CutterTool.Scripts.CutterToolService",
			"SourcePulp.GridCutting.PatternCuttingTool",
			"BuildingBlueprints.Tools.CreateBuildingBlueprintTool",
			"BuildingBlueprints.Tools.DemolishBlueprintTool",
		};

		// PlantingTool, TreeCuttingAreaSelectionTool and the two Demolishable selection tools are
		// concrete, unrelated, publicly accessible classes; every building/object/recovered-good deletion
		// tool instead shares one open-generic base, BlockObjectDeletionTool<T>, so a plain "is" check
		// can't name it directly - the base has to be walked. This picks up EntityBlockObjectDeletionTool
		// and RecoveredGoodStackDeletionTool the same way it picks up BuildingDeconstructionTool, with
		// nothing tool-specific to add if a future mod or DLC introduces another BlockObjectDeletionTool<T>
		// subclass.
		private static bool IsAreaSelectionTool(ITool tool)
		{
			if (tool is PlantingTool || tool is CancelPlantingTool || tool is TreeCuttingAreaSelectionTool
				|| tool is DemolishableSelectionTool || tool is DemolishableUnselectionTool
				|| tool is AbsoluteTerrainHeightBrushTool || tool is RelativeTerrainHeightBrushTool
				|| tool is NaturalResourceSpawningBrushTool || tool is NaturalResourceRemovalBrushTool
				|| tool is SculptingTerrainBrushTool)
			{
				return true;
			}

			var type = tool?.GetType();
			if (type != null && InternalAreaSelectionToolTypeNames.Contains(type.FullName))
			{
				return true;
			}

			for (; type != null; type = type.BaseType)
			{
				if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BlockObjectDeletionTool<>))
				{
					return true;
				}
			}

			return false;
		}

		// The four tools this feature's first pass forces onto real terrain - can't mark the empty air
		// between two platform layers for planting or tree-cutting. Every other area-selection tool
		// gets free vertical movement instead (see RequiresTerrainSnap's caller); narrower rules for
		// those come later. TreeCuttingAreaUnselectionTool is internal, matched by name the same way
		// IsAreaSelectionTool already does for it above.
		private static bool RequiresTerrainSnap(ITool tool)
		{
			if (tool is PlantingTool || tool is CancelPlantingTool || tool is TreeCuttingAreaSelectionTool)
			{
				return true;
			}

			return tool?.GetType().FullName == "Timberborn.ForestryUI.TreeCuttingAreaUnselectionTool";
		}

		private void Activate()
		{
			_active = true;
			_stepReader.Reset();
			_heightStepReader.Reset();
			_heightLocked = false;
			_confirmGate.Arm();
			_handoff.Reset();

			// See GamepadBuildingPlacementController.Activate for why this seeds through the camera via
			// PickTerrainCoordinates instead of a fixed-height plane.
			var screenCentre = new Vector2(Screen.width / 2f, Screen.height / 2f);
			var ray = _cameraService.ScreenPointToRayInGridSpace(screenCentre);
			var picked = _terrainPicker.PickTerrainCoordinates(ray);
			_cursor = picked?.Coordinates ?? Vector3Int.zero;
		}

		// Guarded, not unconditional - see GamepadBuildingPlacementController.Deactivate for why:
		// three separate controllers share the one static GamepadPlacementState, and clearing it
		// every frame the tool isn't one of this class's own, rather than only once on the actual
		// active -> inactive edge, made whichever controller happened to run last each frame the
		// deciding vote regardless of which tool was genuinely active.
		private void Deactivate()
		{
			if (!_active)
			{
				return;
			}

			_active = false;
			GamepadPlacementState.Clear();
			ClearTooltipAnchor();
			_inputService.ShowCursor();
		}

		// See GamepadZiplineConnectionController.ClearTooltipAnchor - Current has to clear alongside
		// WorldPosition, or GamepadTooltipPositionPatch falls through to whatever stale UI element it
		// last held from an unrelated gamepad hover instead of all the way through to the real mouse.
		private static void ClearTooltipAnchor()
		{
			GamepadTooltipAnchor.WorldPosition = null;
			GamepadTooltipAnchor.Current = null;
		}

		private void ReportFailure(Exception e)
		{
			GamepadPlacementState.Clear();
			ClearTooltipAnchor();
			_inputService.ShowCursor();

			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] Area selection tool failed: {e}");
		}
	}
}
