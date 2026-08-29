using HarmonyLib;
using Timberborn.GridTraversing;
using Timberborn.TerrainQueryingSystem;
using UnityEngine;

namespace ControllerSupport
{
	// The MapEditor brush tools work out where they are from
	// `TerrainPicker.PickTerrainCoordinates(ray).Coordinates + Face` on the cursor's own column - and
	// that walks down until it finds terrain. So a level the gamepad picked because it exists somewhere
	// under the *brush*, but not directly under the cursor, was silently discarded: the tool snapped its
	// origin back to the ground below and the height key looked like it had done nothing. That is the
	// second half of "resolve the allowed levels over the whole selection" - offering the level is no
	// use if the tool then refuses to start there.
	//
	// Short-circuiting the picker to the gamepad's own cell fixes it, the same way
	// SculptingTerrainPickerPatch already does for the sculpting brush's PickTerrainCoordinatesWithStump.
	// The target is recovered from the ray's own origin rather than read straight off
	// GamepadPlacementState.GridCursor, because the brush tools call this for both a frozen drag-start
	// ray and a live one, and by the time either call happens GridCursor may already be a frame or two
	// ahead of whichever this is - CameraServicePlacementPatch stamped each ray with the height it was
	// built with, so the ray is the reliable source.
	//
	// Gated on ExactTerrainPick, not on Active, and deliberately so: PickTerrainCoordinates is also how
	// SelectableObjectRaycaster decides whether terrain occludes an object hit, and how planting and
	// tree-cutting resolve their own cells - none of which want their answer replaced. The flag is set
	// only while GamepadAreaSelectionController is driving a sized terrain brush, which is the one case
	// where the cursor can legitimately name a cell the centre column does not contain.
	[HarmonyPatch(typeof(TerrainPicker), nameof(TerrainPicker.PickTerrainCoordinates), new[] { typeof(Ray) })]
	internal static class TerrainPickerExactCellPatch
	{
		[HarmonyPrefix]
		private static bool Prefix(Ray ray, ref TraversedCoordinates? __result)
		{
			if (!GamepadPlacementState.Active || !GamepadPlacementState.ExactTerrainPick)
			{
				return true;
			}

			// A cursor still at its column's natural top publishes RayHeight rather than a real height
			// (see GamepadCursorLevels), which carries nothing to recover - let the real walk run and
			// find the surface itself, exactly as it did before any of this.
			if (ray.origin.z >= GamepadCursorLevels.RayHeight / 2f)
			{
				return true;
			}

			var x = Mathf.FloorToInt(ray.origin.x);
			var y = Mathf.FloorToInt(ray.origin.y);

			// origin.z was set to an exact integer (cursor.z + 1) by CameraServicePlacementPatch, so
			// RoundToInt recovers it exactly - no floating-point drift to round the wrong way.
			var cellZ = Mathf.RoundToInt(ray.origin.z) - 1;

			// Every caller here reads `Coordinates + Face`, so Coordinates has to name the solid voxel
			// one below the cursor's own cell for that sum to land back on the cursor.
			var groundVoxel = new Vector3Int(x, y, cellZ - 1);
			var face = new Vector3Int(0, 0, 1);
			var intersection = new Vector3(x + 0.5f, y + 0.5f, cellZ);
			__result = new TraversedCoordinates(groundVoxel, face, intersection);
			return false;
		}
	}
}
