using System.Reflection;
using HarmonyLib;
using Timberborn.GridTraversing;
using Timberborn.TerrainQueryingSystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Every drag in the game resolves its far end the same way: intersect the current ray with a
	// horizontal plane at the *start's* level. AreaSelector.GetSelectionEnd does it for the demolish/
	// priority/deletion tools, AreaPicker.GetEndCoords for building lines and rectangles,
	// AreaPicker.GetTerrainBlocks for the terrain tools, SculptingTerrainPicker for both of its modes.
	// All four land in TerrainPicker.FindCoordinatesOnLevelInMap, which is
	// GridSpaceRaycasting.HitHorizontalPlane plus a floor/floor/round.
	//
	// That is fine for a camera ray, which comes in from far above at an angle and always crosses the
	// plane. It is not fine for this mod's injected ray, which points straight down from just above the
	// cursor's own cell: UnityEngine.Plane.Raycast reports no hit for a ray that starts *below* the
	// plane it is asked about (a negative enter distance is rejected), so as soon as the cursor sits
	// lower than the level the drag started on, the end point resolves to null and the caller falls
	// back to the start coordinate - the whole selection silently collapsing to one cell mid-drag.
	//
	// GamepadAreaSelectionController and GamepadBuildingPlacementController both hold the cursor's
	// level still for the duration of a drag, which avoids the common way in. This closes the rest:
	// SelectionStart.HitLevel is a raw float world height taken off a block object's collider
	// (blockObjectHit.HitPoint.y), so a model whose mesh stands proud of its own block can put the
	// plane above the ray's origin even with the level frozen.
	//
	// The replacement is not an approximation. The injected ray is exactly vertical, so its
	// intersection with any horizontal plane is its own origin x/y at that plane's height - which is
	// what this computes, matching the real method's rounding (floor for x/y, round for z, then
	// ITerrainService.Clamp) and its hard-coded (0,0,1) face. Mouse-driven play never reaches this;
	// only frames where the mod is actually injecting a ray do.
	[HarmonyPatch(typeof(TerrainPicker), nameof(TerrainPicker.FindCoordinatesOnLevelInMap))]
	internal static class TerrainPickerLevelPlanePatch
	{
		private static readonly FieldInfo TerrainServiceField =
			AccessTools.Field(typeof(TerrainPicker), "_terrainService");

		[HarmonyPrefix]
		private static bool Prefix(TerrainPicker __instance, Ray ray, float level,
			ref TraversedCoordinates? __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			var terrainService = (ITerrainService)TerrainServiceField.GetValue(__instance);
			var x = Mathf.FloorToInt(ray.origin.x);
			var y = Mathf.FloorToInt(ray.origin.y);
			var coordinates = terrainService.Clamp(new Vector3Int(x, y, Mathf.RoundToInt(level)));
			var intersection = new Vector3(x + 0.5f, y + 0.5f, level);
			__result = new TraversedCoordinates(coordinates, new Vector3Int(0, 0, 1), intersection);
			return false;
		}
	}
}
