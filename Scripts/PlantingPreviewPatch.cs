using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Timberborn.Planting;
using Timberborn.PlantingUI;
using Timberborn.TerrainQueryingSystem;
using UnityEngine;

namespace ControllerSupport
{
	// PlantingSelectionService.HighlightMarkableArea only ever draws a tile for cells the player CAN
	// plant on - there is no else branch, so a cell that is out of range, already holds this exact
	// plantable, or is otherwise obstructed shows nothing at all. A mouse player still sees their
	// literal pointer there; a gamepad player driven by GamepadAreaSelectionController has nothing else
	// to go by, so the cursor visibly vanishes.
	//
	// Same fix as BlockValidatorPlacementPatch applies to the building ghost: leave the real validity
	// check (CanPlant, also used by MarkArea to decide whether planting actually happens) completely
	// untouched, and only add a cosmetic red tile wherever gamepad placement is active and CanPlant
	// would say no. TerrainAreaService and PlantingAreaValidator are both public and stateless enough
	// to call a second time here at no risk - they're read via reflection only because
	// PlantingSelectionService keeps them in private fields with no public seam to reach them through.
	[HarmonyPatch(typeof(PlantingSelectionService), nameof(PlantingSelectionService.HighlightMarkableArea))]
	internal static class PlantingPreviewPatch
	{
		private static readonly FieldInfo TerrainAreaServiceField =
			AccessTools.Field(typeof(PlantingSelectionService), "_terrainAreaService");

		private static readonly FieldInfo PlantingAreaValidatorField =
			AccessTools.Field(typeof(PlantingSelectionService), "_plantingAreaValidator");

		[HarmonyPostfix]
		private static void Postfix(PlantingSelectionService __instance, IEnumerable<Vector3Int> inputBlocks,
			Ray ray, string templateName)
		{
			if (!GamepadPlacementState.Active || GamepadPlacementState.InvalidTileDrawer == null)
			{
				return;
			}

			var terrainAreaService = (TerrainAreaService)TerrainAreaServiceField.GetValue(__instance);
			var validator = (PlantingAreaValidator)PlantingAreaValidatorField.GetValue(__instance);

			foreach (var coordinates in terrainAreaService.InMapLeveledCoordinates(inputBlocks, ray))
			{
				if (!validator.CanPlant(coordinates, templateName))
				{
					GamepadPlacementState.InvalidTileDrawer.DrawAtCoordinates(coordinates,
						GamepadPlacementState.InvalidTileHeight, GamepadPlacementState.InvalidColor);
				}
			}
		}
	}
}
