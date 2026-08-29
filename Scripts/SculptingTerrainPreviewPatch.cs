using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Timberborn.MapEditorBrushesUI;
using UnityEngine;

namespace ControllerSupport
{
	// SculptingTerrainBrushTool.DrawPreview only ever draws a marker for cells IsValidCandidateBlock
	// accepts - a target the gamepad has floated into open mid-air, clipped into existing terrain, or
	// otherwise pushed somewhere invalid draws nothing at all, so the cursor visibly vanishes exactly
	// the way PlantingPreviewPatch/BlockValidatorPlacementPatch already fix for other tools. Same fix
	// here: leave the real validity check and the tool's own marker colours completely untouched, and
	// only add a cosmetic marker at the gamepad's own tracked cell whenever nothing else got drawn -
	// GamepadPlacementState.InvalidBoxDrawer/InvalidBoxColor, not InvalidTileDrawer/InvalidColor,
	// since this tool's own valid preview is a full block-shaped box (a face on every side), not a
	// flat ground tile - an invalid cursor needs the same shape to read as the same kind of cursor.
	//
	// UpdateBlocksCache (not DrawPreview itself) is the patch point: it's the one place _blocksToApply
	// is freshly populated but not yet cleared - DrawPreview's very last statement clears it, so a
	// postfix on DrawPreview itself would have nothing left to inspect. UpdateBlocksCache also runs
	// from ApplyChanges (the actual commit, not just preview) - drawing an extra frame's marker there
	// too is harmless, since it is only ever a single frame right as the tool commits.
	[HarmonyPatch(typeof(SculptingTerrainBrushTool))]
	internal static class SculptingTerrainPreviewPatch
	{
		private static readonly FieldInfo BlocksToApplyField =
			AccessTools.Field(typeof(SculptingTerrainBrushTool), "_blocksToApply");

		[HarmonyPatch("UpdateBlocksCache")]
		[HarmonyPostfix]
		private static void Postfix(SculptingTerrainBrushTool __instance)
		{
			if (!GamepadPlacementState.Active || !GamepadPlacementState.SculptingActive
				|| GamepadPlacementState.InvalidBoxDrawer == null)
			{
				return;
			}

			var blocksToApply = (HashSet<Vector3Int>)BlocksToApplyField.GetValue(__instance);
			if (blocksToApply.Count > 0)
			{
				return;
			}

			GamepadPlacementState.InvalidBoxDrawer.DrawAtCoordinates(GamepadPlacementState.GridCursor,
				GamepadPlacementState.InvalidTileHeight, GamepadPlacementState.InvalidBoxColor);
		}
	}
}
