using HarmonyLib;
using Timberborn.GridTraversing;
using Timberborn.TerrainQueryingSystem;
using UnityEngine;

namespace ControllerSupport
{
	// SculptingTerrainBrushTool (Timberborn.MapEditorBrushesUI) is the one tool whose whole purpose -
	// creating overhangs - needs the cursor to float in genuinely empty air with nothing directly
	// below it. But SculptingTerrainPicker doesn't place its target wherever
	// CameraServicePlacementPatch's ray originates: TerrainPicker.PickTerrainCoordinatesWithStump
	// walks the ray *down* through the voxel grid (GridTraversal, not a physics raycast) until it hits
	// real terrain, so a straight-down ray always resolves back to the actual ground surface no matter
	// how high or low its own origin sits - CursorHeightUp/Down had zero effect on this tool even
	// though GamepadCursorHeight's free-height offset tracking was working correctly underneath it.
	//
	// Short-circuiting this one TerrainPicker method - only while the gamepad is actively driving the
	// sculpting brush (GamepadPlacementState.SculptingActive) - is what makes that height actually
	// stick. Scoped this narrowly rather than reading GamepadPlacementState.GridCursor directly:
	// SculptingTerrainPicker calls this twice per drag (once for the frozen start ray, once for the
	// live end ray), and by the time either call happens GridCursor may already be several frames
	// ahead of whichever one this is - so the target is recovered from the ray's own origin instead,
	// which CameraServicePlacementPatch already stamped with the intended height at the moment each
	// ray was actually built.
	//
	// Also reads GamepadPlacementState.SculptAdd (see SculptingTerrainAddRemovePatch) to decide which
	// voxel Coordinates should actually name - see the prefix's own comment for why Add and Remove
	// need a different answer here, not just a different picker method.
	[HarmonyPatch(typeof(TerrainPicker))]
	internal static class SculptingTerrainPickerPatch
	{
		[HarmonyPatch(nameof(TerrainPicker.PickTerrainCoordinatesWithStump))]
		[HarmonyPrefix]
		private static bool PickTerrainCoordinatesWithStumpPrefix(Ray ray, ref TraversedCoordinates? __result)
		{
			if (!GamepadPlacementState.Active || !GamepadPlacementState.SculptingActive)
			{
				return true;
			}

			// No RayHeight-sentinel escape hatch any more. This used to fall through to the real
			// terrain walk whenever the ray originated at RayHeight - i.e. whenever the cursor sat at
			// its column's surface - on the grounds that the sentinel carried no useful height. The
			// cost was that the tool's behaviour changed shape depending on whether the player had
			// pressed a height key yet: at the surface the picker re-derived its own answer (which
			// could disagree with the drawn cursor over a stackable block), and only below it did the
			// gamepad's own cell win. GamepadAreaSelectionController now publishes an exact
			// cursor.z + 1 origin on every sculpting frame instead, so this is unconditional and one
			// press always moves the target exactly one voxel.
			var x = Mathf.FloorToInt(ray.origin.x);
			var y = Mathf.FloorToInt(ray.origin.y);

			// origin.z was set to an exact integer (cursor.z + 1f) by CameraServicePlacementPatch, so
			// RoundToInt recovers it exactly - no floating-point drift to round the wrong way.
			var cellZ = Mathf.RoundToInt(ray.origin.z) - 1;

			// Coordinates means different things depending on which picker method the caller is
			// about to use it for, and this one patched method feeds both: GetBlocksToAdd reads
			// CoordinatesWithFaceOffset (Coordinates + Face) as the empty cell to add into, while
			// GetBlocksToRemove reads Coordinates directly as the solid voxel to remove - vanilla's
			// own mouse picking gets this right for free because Coordinates is always "whatever solid
			// voxel the ray's downward walk actually hit". A gamepad Add press means the cursor's own
			// cell (cellZ) is empty air waiting to become solid, so Coordinates has to fall back to
			// "whatever's below" purely so the +1 offset lands back on cellZ. A Remove press means the
			// cursor's own cell already *is* the solid target - Coordinates must be cellZ directly, not
			// cellZ - 1, or GetBlocksToRemove ends up unsetting the layer directly below the one the
			// player is actually looking at (reported as "it removes two layers": the wrong layer goes,
			// and whatever it was propping up above it can cascade-collapse with it).
			var solidZ = GamepadPlacementState.SculptAdd ? cellZ - 1 : cellZ;

			var groundVoxel = new Vector3Int(x, y, solidZ);
			var face = new Vector3Int(0, 0, 1);
			var intersection = new Vector3(x + 0.5f, y + 0.5f, solidZ + 1f);
			__result = new TraversedCoordinates(groundVoxel, face, intersection);
			return false;
		}
	}
}
