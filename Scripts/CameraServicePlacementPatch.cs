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
	[HarmonyPatch(typeof(Timberborn.CameraSystem.CameraService))]
	internal static class CameraServicePlacementPatch
	{
		// GamepadPlacementState.CursorRayOriginHeight is GamepadCursorLevels.RayHeight - well above any
		// real map height (grid.z is world height in voxel units, and Timberborn maps never come close
		// to this) - whenever the cursor is on its column's topmost level, so the common case travels
		// down from clear air onto whatever is actually there, same as a real mouse ray from the camera.
		// A cursor on a lower level publishes `cursor.z + 1` instead, which is an exact integer voxel
		// boundary: GridTraversal's first step from there lands on the cursor's own (empty) cell and the
		// second on the thing resting under it, so the pick is precise rather than "whatever is nearest
		// below". That origin is deliberately never inside the target - a level is by construction an
		// EMPTY cell (see GamepadCursorLevels), which is what makes it safe for the physics-raycast
		// pickers too, since a raycast starting inside a collider registers no hit for it. An earlier
		// version put the origin at the *solid* voxel instead and hit exactly that: a cell occupied by a
		// tree or bush showed no preview at all rather than the usual red one.
		private static readonly Vector3 Down = new Vector3(0f, 0f, -1f);

		private static Ray GridSpaceRay()
		{
			var cursor = GamepadPlacementState.GridCursor;
			var origin = new Vector3(cursor.x + 0.5f, cursor.y + 0.5f, GamepadPlacementState.CursorRayOriginHeight);
			return new Ray(origin, Down);
		}

		[HarmonyPatch(nameof(Timberborn.CameraSystem.CameraService.ScreenPointToRayInGridSpace))]
		[HarmonyPrefix]
		private static bool GridSpacePrefix(ref Ray __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			__result = GridSpaceRay();
			return false;
		}

		// CursorCoordinatesPicker (used by FPPCameraActivationTool) doesn't only go through
		// ScreenPointToRayInGridSpace - it first asks SelectableObjectRaycaster.TryHitSelectableObject
		// whether the cursor is over a finished floor/path/stackable BlockObject, and that call reaches
		// world space through ScreenPointToRayInWorldSpace(InputService.MousePosition), a method this
		// mod never touched before because none of the tools GamepadAreaSelectionController previously
		// drove used CursorCoordinatesPicker. Left unpatched, that branch would still raycast from
		// wherever the real desktop mouse happens to be resting (stale, unrelated to the gamepad-tracked
		// cell) and, on a hit, return that position outright - overriding the correct gamepad cell
		// silently, since PickCoordinates returns as soon as this branch succeeds and never reaches the
		// terrain fallback. CoordinateSystem.GridToWorld(Ray) is the same axis-swap WorldToGrid(Ray)
		// uses (grid space swaps world's Y/Z, and swapping twice is a no-op), so this is exactly
		// GridSpaceRay() re-expressed in world space - the same synthetic cell, not a second cursor.
		[HarmonyPatch(nameof(Timberborn.CameraSystem.CameraService.ScreenPointToRayInWorldSpace))]
		[HarmonyPrefix]
		private static bool WorldSpacePrefix(ref Ray __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			__result = Timberborn.Coordinates.CoordinateSystem.GridToWorld(GridSpaceRay());
			return false;
		}
	}
}
