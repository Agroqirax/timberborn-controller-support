using HarmonyLib;
using UnityEngine;

namespace ControllerSupport
{
	// AreaSelectionController derives the ray that drives ghost preview, drag and placement by
	// round-tripping a screen point through the camera: grid cell -> world -> screen (this mod's own
	// forward projection) -> back through CameraService.ScreenPointToRayInGridSpace -> re-picked by
	// the game. That round trip depends on the camera's exact position, zoom and rotation at every
	// step and does not perfectly cancel out, so panning or zooming while placing a building could
	// shift which cell the ghost actually landed on, or lose it off-screen entirely.
	//
	// Injecting the grid cell directly - Harmony is already in the room for the InputService patches
	// - removes the round trip altogether: the returned ray always points straight down through the
	// exact centre of the gamepad's tracked cell, in grid space, with zero camera dependence.
	[HarmonyPatch(typeof(Timberborn.CameraSystem.CameraService), nameof(Timberborn.CameraSystem.CameraService.ScreenPointToRayInGridSpace))]
	internal static class CameraServicePlacementPatch
	{
		// Well above any real map height (grid.z is world height in voxel units, and Timberborn maps
		// never come close to this), so the ray always travels down from clear air onto whatever is
		// actually there - same as a real mouse ray cast down from the camera. Starting the ray right
		// at the target cell instead, as this used to, put its origin inside a tree or bush's own
		// collider whenever one occupied that cell: a raycast that starts inside a collider does not
		// register a hit for it, so the picker found nothing at all rather than an occupied cell,
		// which is why obstructed cells showed no preview instead of the usual red one.
		private const float RayHeight = 1000f;

		private static readonly Vector3 Down = new Vector3(0f, 0f, -1f);

		[HarmonyPrefix]
		private static bool Prefix(ref Ray __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			var cursor = GamepadPlacementState.GridCursor;
			var origin = new Vector3(cursor.x + 0.5f, cursor.y + 0.5f, RayHeight);
			__result = new Ray(origin, Down);
			return false;
		}
	}
}
