using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BlockObjectTools;
using Timberborn.BlueprintSystem;
using Timberborn.CameraSystem;
using Timberborn.DemolishingUI;
using Timberborn.ForestryUI;
using Timberborn.InputSystem;
using Timberborn.PlantingUI;
using Timberborn.Rendering;
using Timberborn.SingletonSystem;
using Timberborn.TerrainQueryingSystem;
using Timberborn.ToolSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Drives every non-building area-selection tool with the gamepad - planting and un-planting
	// (CancelPlantingTool is the eraser shared by the Fields and Forestry/natural-resource planting
	// groups - two bottom-bar entry points for the one tool), tree-cutting-area marking/unmarking,
	// builder priority, marking/unmarking buildings for demolition, and deleting buildings/objects/
	// recovered-good stacks - the same way GamepadBuildingPlacementController drives
	// BlockObjectTool: the left stick/d-pad nudges a grid cursor one voxel at a time, A presses/holds/
	// releases exactly like a mouse click-drag-release.
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
	// No rotate/flip: none of these tools have an orientation to change.
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
	internal class GamepadAreaSelectionController : ILoadableSingleton, IUnloadableSingleton, IPriorityInputProcessor
	{
		private const float FailureLogInterval = 30f;

		private readonly InputService _inputService;
		private readonly CameraService _cameraService;
		private readonly ToolService _toolService;
		private readonly TerrainPicker _terrainPicker;
		private readonly PanelTracker _panelTracker;
		private readonly MarkerDrawerFactory _markerDrawerFactory;
		private readonly ISpecService _specService;

		private readonly GamepadGridStepReader _stepReader = new GamepadGridStepReader();

		private bool _active;
		private Vector3Int _cursor;
		private float _nextFailureLogTime;

		public GamepadAreaSelectionController(InputService inputService, CameraService cameraService,
			ToolService toolService, TerrainPicker terrainPicker, PanelTracker panelTracker,
			MarkerDrawerFactory markerDrawerFactory, ISpecService specService)
		{
			_inputService = inputService;
			_cameraService = cameraService;
			_toolService = toolService;
			_terrainPicker = terrainPicker;
			_panelTracker = panelTracker;
			_markerDrawerFactory = markerDrawerFactory;
			_specService = specService;
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
			GamepadPlacementState.InvalidTileDrawer = _markerDrawerFactory.CreateTileDrawer();
			GamepadPlacementState.InvalidColor = GetTreeCuttingNoActionColor();
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

		// See GamepadBuildingPlacementController.Unload for why this has to stay an IPriorityInputProcessor
		// with no way to unregister it - the same MainMouseButtonUp-starvation deadlock applies here too,
		// since these tools drive the exact same AreaSelectionController.
		public void Unload()
		{
			GamepadPlacementState.Clear();
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
				return;
			}

			if (!_active)
			{
				Activate();
			}

			var step = _stepReader.ReadStep(gamepad, _cameraService.HorizontalAngle);
			if (step != Vector2Int.zero)
			{
				_cursor += new Vector3Int(step.x, step.y, 0);
			}

			GamepadPlacementState.Active = true;
			GamepadPlacementState.GridCursor = _cursor;
			GamepadPlacementState.MainMouseButtonDown = gamepad.buttonSouth.wasPressedThisFrame;
			GamepadPlacementState.MainMouseButtonHeld = gamepad.buttonSouth.isPressed;
			GamepadPlacementState.MainMouseButtonUp = gamepad.buttonSouth.wasReleasedThisFrame;
		}

		// TreeCuttingAreaUnselectionTool and BuilderPriorityTool are declared `internal` in their own
		// assemblies, so they can't be named with `is TypeName` here - only their full type name is
		// reachable from outside. Matched by name rather than skipped, the same as every other tool in
		// this list, rather than leaving half of a symmetric pair (selection but not unselection,
		// planting but not priority) without gamepad support.
		private static readonly HashSet<string> InternalAreaSelectionToolTypeNames = new HashSet<string>
		{
			"Timberborn.ForestryUI.TreeCuttingAreaUnselectionTool",
			"Timberborn.BuilderPrioritySystemUI.BuilderPriorityTool",
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
				|| tool is DemolishableSelectionTool || tool is DemolishableUnselectionTool)
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

		private void Activate()
		{
			_active = true;
			_stepReader.Reset();

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
		}

		private void ReportFailure(Exception e)
		{
			GamepadPlacementState.Clear();

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
