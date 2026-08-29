using HarmonyLib;
using Timberborn.AreaSelectionSystem;
using UnityEngine;

namespace ControllerSupport
{
	// SculptingTerrainPicker asks TryGetStackableBlockObjectCoordinates *first*, before it ever reaches
	// the TerrainPicker call SculptingTerrainPickerPatch redirects, and that question is answered by a
	// physics raycast against block-object colliders rather than by the voxel grid. Two separate things
	// go wrong when a gamepad-driven straight-down ray meets a platform or path on the way:
	//
	//  - Add mode returns `blockObjectHit.HitBlock.Coordinates.Above()` outright, silently overriding
	//    the height the player dialled in with CursorHeightUp/Down with the top of whatever the ray
	//    happened to clip.
	//  - Remove mode has no override to give and bails out entirely (`return Enumerable.Empty`), so the
	//    tool just does nothing at all, with no feedback about why.
	//
	// Neither is what a gamepad cursor means: its cell is chosen, not discovered, and
	// SculptingTerrainPickerPatch already knows the exact answer. Refusing this one branch for the
	// duration is what lets that answer through.
	//
	// Patched here rather than on BlockObjectRaycaster.TryHitBlockObject, which is where the raycast
	// actually lives: that method is generic, so Harmony would be patching one shared implementation
	// used by every area tool in the game and the gate would have to hold for all of them. This method
	// is private but concrete, belongs to the sculpting picker alone, and is the only caller that
	// matters - a strictly narrower blast radius for the same effect. Still gated on SculptingActive,
	// which is only ever true while GamepadAreaSelectionController is driving SculptingTerrainBrushTool.
	[HarmonyPatch(typeof(SculptingTerrainPicker), "TryGetStackableBlockObjectCoordinates")]
	internal static class SculptingBlockObjectRaycasterPatch
	{
		[HarmonyPrefix]
		private static bool Prefix(ref bool __result, ref Vector3Int coordinates)
		{
			if (!GamepadPlacementState.Active || !GamepadPlacementState.SculptingActive)
			{
				return true;
			}

			coordinates = default;
			__result = false;
			return false;
		}
	}
}
