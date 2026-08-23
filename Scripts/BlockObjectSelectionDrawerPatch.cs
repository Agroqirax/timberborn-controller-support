using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.AreaSelectionSystem;
using Timberborn.AreaSelectionSystemUI;
using Timberborn.BlockSystem;
using Timberborn.Common;
using UnityEngine;

namespace ControllerSupport
{
	// BlockObjectSelectionDrawer.Draw is the shared preview renderer behind BuilderPriorityTool,
	// DemolishableSelectionTool, DemolishableUnselectionTool and every BlockObjectDeletionTool<T>
	// subclass (BuildingDeconstructionTool, EntityBlockObjectDeletionTool,
	// RecoveredGoodStackDeletionTool) - one patch here covers all of them, the same way
	// GamepadAreaSelectionController's IsAreaSelectionTool check does.
	//
	// Draw() unconditionally calls _rollingHighlighter.HighlightPrimary(blockObjects, color) - that
	// line isn't behind the selectingArea gate, only the box outline is - so a valid single-cell
	// target (blockObjects non-empty) already gets marked correctly the instant the cursor lands on
	// it, gamepad or mouse, click or no click. The real gap is narrower than it first looked: only
	// when blockObjects comes back EMPTY does nothing render at all, because the box outline is the
	// one thing gated on selectingArea and there's no object to highlight either.
	//
	// Only the box outline gets forced on here - no extra red tile. An earlier version also drew one
	// via GamepadPlacementState.InvalidTileDrawer, on top of this same box, whenever blockObjects was
	// empty; the two stacked made every single-cell selection look red regardless of what was under
	// the cursor, while a real multi-cell drag (which already draws this same box unpatched, in
	// whatever neutral colour RectangleBoundsDrawer's own meshes use) stayed plain. Dropping the red
	// tile makes a single-cell selection look exactly like a multi-cell one now - the same box, same
	// colour, the only difference being this patch is the one drawing it instead of Draw() itself.
	//
	// Fixed with a postfix rather than reimplementing Draw(): let the real call run first exactly as
	// before, then, only while gamepad placement is active and only when it found nothing to
	// highlight, draw the box outline so the cursor is still visible somewhere. This never touches
	// which objects actually get acted on; that's still decided entirely by the picker upstream of
	// this drawer.
	// The class also has a private, no-argument Draw() - name-only [HarmonyPatch] matches both and
	// Harmony refuses to guess, so the public overload's parameter types have to be spelled out.
	[HarmonyPatch(typeof(BlockObjectSelectionDrawer), nameof(BlockObjectSelectionDrawer.Draw),
		new[] { typeof(IEnumerable<BlockObject>), typeof(Vector3Int), typeof(Vector3Int), typeof(bool) })]
	internal static class BlockObjectSelectionDrawerPatch
	{
		private static readonly FieldInfo RectangleBoundsDrawerField =
			AccessTools.Field(typeof(BlockObjectSelectionDrawer), "_rectangleBoundsDrawer");

		[HarmonyPostfix]
		private static void Postfix(BlockObjectSelectionDrawer __instance, IEnumerable<BlockObject> blockObjects,
			Vector3Int start, Vector3Int end, bool selectingArea)
		{
			if (!GamepadPlacementState.Active || selectingArea || blockObjects.Any())
			{
				return;
			}

			var rectangleBoundsDrawer = (RectangleBoundsDrawer)RectangleBoundsDrawerField.GetValue(__instance);
			rectangleBoundsDrawer.DrawOnLevel(start.XY(), end.XY(), start.z);
		}
	}
}
