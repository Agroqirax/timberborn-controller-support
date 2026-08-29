using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.AreaSelectionSystem;
using Timberborn.AreaSelectionSystemUI;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.SelectionSystem;
using UnityEngine;

namespace ControllerSupport
{
	// BuildingBlueprints (optional workshop mod, 3667559269) is optional and this mod must stay fully
	// inert without it - same reasoning as FPPCameraIntegration in FPPCameraAnalogPatch.cs, so
	// BuildingBlueprints.dll is never referenced at compile time and everything below is applied
	// manually from TryApply, only after confirming the assembly is actually loaded.
	//
	// CreateBuildingBlueprintTool and DemolishBlueprintTool are both already covered for the tool
	// switching/input-bridge itself (see GamepadAreaSelectionController.InternalAreaSelectionToolTypeNames) -
	// what's missing is a way to see the gamepad-tracked cell while hovering, since the real mouse
	// cursor no longer indicates it once GamepadPlacementState.Active hides it.
	internal static class BuildingBlueprintsIntegration
	{
		public static void TryApply(Harmony harmony)
		{
			var assembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name == "BuildingBlueprints");
			if (assembly == null)
			{
				return;
			}

			try
			{
				TryApplyCreateToolPatch(harmony, assembly);
				TryApplyDemolishToolPatch(harmony, assembly);
			}
			catch (Exception e)
			{
				Debug.LogError($"[ControllerSupport] BuildingBlueprints integration failed to start: {e}");
			}
		}

		// CreateBuildingBlueprintTool.PreviewCallback only ever calls its BlockObjectSelectionDrawer
		// (the "highlighter" field) while selectingArea is true - a plain hover, drag not yet started,
		// calls ClearHighlights() instead and draws nothing at all. A real mouse player still has their
		// literal pointer to go by; a gamepad player driven through GamepadPlacementState has nothing
		// else, so the cursor visibly vanishes until A is actually held.
		//
		// The fix draws through the *same* highlighter the tool already owns, with selectingArea: false,
		// exactly like BuilderPriorityTool's own hover branch. That has two effects for free: any
		// building already under the cursor gets highlighted (matching real-mouse behaviour once the
		// player does start dragging), and - when nothing is there -
		// BlockObjectSelectionDrawerPatch's existing postfix (it patches BlockObjectSelectionDrawer.Draw
		// itself, so it fires no matter who calls it) draws the box outline it already draws for
		// BuilderPriorityTool/demolish/deletion tools in the equivalent empty-cell case. No new drawing
		// code needed here at all - only routing this tool's hover frame into a call the rest of the mod
		// already handles.
		private static void TryApplyCreateToolPatch(Harmony harmony, Assembly assembly)
		{
			var toolType = assembly.GetType("BuildingBlueprints.Tools.CreateBuildingBlueprintTool");
			if (toolType == null)
			{
				return;
			}

			var highlighterField = AccessTools.Field(toolType, "highlighter");
			var previewCallback = AccessTools.Method(toolType, "PreviewCallback",
				new[] { typeof(IEnumerable<BlockObject>), typeof(Vector3Int), typeof(Vector3Int), typeof(bool), typeof(bool) });
			if (highlighterField == null || previewCallback == null)
			{
				Debug.LogWarning("[ControllerSupport] CreateBuildingBlueprintTool shape has changed - skipping its cursor-visibility fix.");
				return;
			}

			CreateToolPreviewPatch.HighlighterField = highlighterField;
			harmony.Patch(previewCallback,
				prefix: new HarmonyMethod(typeof(CreateToolPreviewPatch), nameof(CreateToolPreviewPatch.Prefix)));
		}

		private static class CreateToolPreviewPatch
		{
			public static FieldInfo HighlighterField;
			private static bool _failed;

			// Same reasoning as DemolishToolPostfix._failed: this runs every hover frame the tool is
			// active, so an uncaught exception here is not a one-off, it's every-frame - fail once and
			// fall back to running the original method (true is also what the ordinary selectingArea
			// case already returns) rather than risk it again.
			public static bool Prefix(object __instance, IEnumerable<BlockObject> blockObjects,
				Vector3Int start, Vector3Int end, bool selectingArea)
			{
				if (_failed || !GamepadPlacementState.Active || selectingArea)
				{
					return true;
				}

				try
				{
					var highlighter = (BlockObjectSelectionDrawer)HighlighterField.GetValue(__instance);
					highlighter.Draw(blockObjects, start, end, false);
					return false;
				}
				catch (Exception e)
				{
					_failed = true;
					Debug.LogError($"[ControllerSupport] CreateBuildingBlueprintTool cursor-visibility fix failed, disabling it: {e}");
					return true;
				}
			}
		}

		// DemolishBlueprintTool has no drag/area concept at all - SelectableObjectRaycaster picks
		// whatever single object sits under one ray - so unlike Create there is nothing to "restrict to
		// 1x1", it already only ever acts on the one cell the cursor is over. The gap is the same one as
		// Create's, though: when that cell holds nothing demolishable, ProcessInput calls
		// UnhighlightDestructionEntities and draws nothing, so the gamepad cursor disappears there too.
		//
		// Fixed with a postfix on ProcessInput that redoes the same lookup (raycast, then
		// BuildingBlueprintComponent + HasGroup + a non-empty group, exactly what the real method
		// requires before it calls HighlightDestructionEntities) and, only when that comes up empty,
		// draws a degenerate 1x1 RectangleBoundsDrawer box at the gamepad cell - the same mechanism
		// BlockObjectSelectionDrawerPatch already uses for the equivalent empty-cell case on
		// BuilderPriorityTool/demolish/deletion tools (a flat MeshDrawer tile was tried here first and
		// wasn't reliably visible - see GamepadPlacementState.BoundsDrawerFactory's own comment),
		// coloured with the base game's own DemolishingColorsSpec (red tile/white sides - the exact
		// colours DemolishableSelectionTool/BuildingDeconstructionTool already draw with) rather than
		// GamepadPlacementState.InvalidColor's tree-cutting yellow, since this tool demolishes, it
		// doesn't mark "nothing happens here". Re-running the raycast is redundant but harmless:
		// CameraServicePlacementPatch.WorldSpacePrefix makes it a pure function of
		// GamepadPlacementState.GridCursor while gamepad placement is active, so both calls this frame
		// hit the same thing.
		private static void TryApplyDemolishToolPatch(Harmony harmony, Assembly assembly)
		{
			var toolType = assembly.GetType("BuildingBlueprints.Tools.DemolishBlueprintTool");
			var componentType = assembly.GetType("BuildingBlueprints.Components.BuildingBlueprintComponent");
			var groupServiceType = assembly.GetType("BuildingBlueprints.Services.BlueprintGroupService");
			if (toolType == null || componentType == null || groupServiceType == null)
			{
				return;
			}

			var raycasterField = FieldOfType(toolType, typeof(SelectableObjectRaycaster));
			var groupServiceField = FieldOfType(toolType, groupServiceType);
			var hasGroupProperty = AccessTools.Property(componentType, "HasGroup");
			var getGroupMethod = AccessTools.Method(groupServiceType, "GetGroup", new[] { componentType });
			var getComponentMethod = AccessTools.Method(typeof(BaseComponent), "GetComponent")
				?.MakeGenericMethod(componentType);
			var processInput = AccessTools.Method(toolType, "ProcessInput");

			if (raycasterField == null || groupServiceField == null || hasGroupProperty == null
				|| getGroupMethod == null || getComponentMethod == null || processInput == null)
			{
				Debug.LogWarning("[ControllerSupport] DemolishBlueprintTool shape has changed - skipping its cursor-visibility fix.");
				return;
			}

			DemolishToolPostfix.RaycasterField = raycasterField;
			DemolishToolPostfix.GroupServiceField = groupServiceField;
			DemolishToolPostfix.HasGroupProperty = hasGroupProperty;
			DemolishToolPostfix.GetGroupMethod = getGroupMethod;
			DemolishToolPostfix.GetComponentMethod = getComponentMethod;
			harmony.Patch(processInput,
				postfix: new HarmonyMethod(typeof(DemolishToolPostfix), nameof(DemolishToolPostfix.Postfix)));
		}

		private static FieldInfo FieldOfType(Type declaringType, Type fieldType)
		{
			return declaringType
				.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.FirstOrDefault(f => f.FieldType == fieldType);
		}

		private static class DemolishToolPostfix
		{
			public static FieldInfo RaycasterField;
			public static FieldInfo GroupServiceField;
			public static PropertyInfo HasGroupProperty;
			public static MethodInfo GetGroupMethod;
			public static MethodInfo GetComponentMethod;

			private static bool _failed;
			private static RectangleBoundsDrawer _cursorDrawer;

			// A postfix on a per-frame ProcessInput has no caller here that catches for it - an uncaught
			// exception (an InvalidCastException from a bad guess at GetGroup's concrete return type
			// crashed the whole game, not just this mod, before this try/catch existed) runs straight
			// through the Harmony-generated trampoline every single frame the tool is active. Fail once,
			// log once, then stand down for good rather than spamming the log or risking a repeat.
			public static void Postfix(object __instance)
			{
				if (_failed || !GamepadPlacementState.Active)
				{
					return;
				}

				var drawer = GetCursorDrawer();
				if (drawer == null)
				{
					return;
				}

				try
				{
					if (!HasDemolishableGroup(__instance))
					{
						// GridCursor is already the surface cell (the empty one resting on whatever is
						// being pointed at), refreshed every frame including as the stick moves x/y - so
						// this just draws there. It used to re-derive the ground with its own straight-down
						// TerrainPicker ray from 1000f, plus a +1 to convert that picker's solid voxel into
						// the surface cell above it, because GridCursor.z was then only ever seeded at
						// Activate() and went stale the moment the cursor moved. That is no longer true,
						// and re-deriving now actively hurts: it would ignore the level the player dialled
						// in with CursorHeightUp/Down and always snap the box back to the topmost terrain.
						var cursor = GamepadPlacementState.GridCursor;
						var xy = new Vector2Int(cursor.x, cursor.y);
						drawer.DrawOnLevel(xy, xy, cursor.z);
					}
				}
				catch (Exception e)
				{
					_failed = true;
					Debug.LogError($"[ControllerSupport] DemolishBlueprintTool cursor-visibility fix failed, disabling it: {e}");
				}
			}

			// Built lazily rather than in TryApplyDemolishToolPatch - that runs from ModStarter at mod
			// startup, well before GamepadAreaSelectionController.Load publishes BoundsDrawerFactory/
			// SpecService (singleton loading happens later, once a game/map is actually entered). Cached
			// after the first successful build since neither dependency changes afterwards.
			private static RectangleBoundsDrawer GetCursorDrawer()
			{
				if (_cursorDrawer != null)
				{
					return _cursorDrawer;
				}

				var factory = GamepadPlacementState.BoundsDrawerFactory;
				var specService = GamepadPlacementState.SpecService;
				if (factory == null || specService == null)
				{
					return null;
				}

				var colors = GetDemolishColors(specService);
				if (colors == null)
				{
					return null;
				}

				_cursorDrawer = factory.Create(colors.Value.tile, colors.Value.side);
				return _cursorDrawer;
			}

			// DemolishingColorsSpec is internal to Timberborn.DemolishingUI, so it can't be named as a
			// generic argument here the normal way - same MakeGenericMethod dance
			// GamepadAreaSelectionController.GetTreeCuttingNoActionColor already does for
			// TreeCuttingColorsSpec. DeletedAreaTileColor/DeletedAreaSideColor are the exact colours
			// DemolishableSelectionTool/BuildingDeconstructionTool already draw their own empty/marked
			// cells with (red tile, white sides per Blueprints.zip's DemolishingColors.blueprint.json) -
			// reusing them keeps this looking like a demolish tool, not a tree-cutting one.
			private static (Color tile, Color side)? GetDemolishColors(ISpecService specService)
			{
				var specType = AccessTools.TypeByName("Timberborn.DemolishingUI.DemolishingColorsSpec");
				if (specType == null)
				{
					return null;
				}

				var spec = typeof(ISpecService).GetMethod(nameof(ISpecService.GetSingleSpec))
					.MakeGenericMethod(specType).Invoke(specService, null);
				var tile = (Color)AccessTools.Property(specType, "DeletedAreaTileColor").GetValue(spec);
				var side = (Color)AccessTools.Property(specType, "DeletedAreaSideColor").GetValue(spec);
				return (tile, side);
			}

			private static bool HasDemolishableGroup(object instance)
			{
				var raycaster = (SelectableObjectRaycaster)RaycasterField.GetValue(instance);
				if (!raycaster.TryHitSelectableObject(out var hitObject) || !hitObject)
				{
					return false;
				}

				var component = GetComponentMethod.Invoke(hitObject, null);
				if (component == null || !(bool)HasGroupProperty.GetValue(component))
				{
					return false;
				}

				var groupService = GroupServiceField.GetValue(instance);
				var group = GetGroupMethod.Invoke(groupService, new[] { component });

				// GetGroup's declared return type is IReadOnlyCollection<T> but the concrete instance is
				// a HashSet<T>, which implements the generic ICollection<T>/IReadOnlyCollection<T> but not
				// the non-generic System.Collections.ICollection - casting to that (tried first, and
				// confirmed by an in-game crash) throws InvalidCastException every frame this tool is
				// active, which is bad enough news for Mono to bring the whole game down, not just this
				// mod. IEnumerable is implemented by every collection type, generic or not, so it's the
				// only safe non-generic view here.
				return group is IEnumerable enumerable && enumerable.GetEnumerator().MoveNext();
			}
		}
	}
}
