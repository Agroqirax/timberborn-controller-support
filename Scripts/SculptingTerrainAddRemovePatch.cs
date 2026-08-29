using System;
using System.Reflection;
using HarmonyLib;
using Timberborn.AreaSelectionSystem;
using Timberborn.MapEditorBrushesUI;

namespace ControllerSupport
{
	// SculptingTerrainBrushTool.ProcessInput picks PickTerrainAreaToAdd vs PickTerrainAreaToRemove
	// off its own IsIncreasing property (Increase/Inverse, set by the tool's own Add/Remove UI
	// buttons) - one fixed mode for the whole time the tool is open. That makes sense for a mouse,
	// which can only ever point at one thing at a time and needs an explicit mode switch to say what
	// to do with it. It stops making sense once the cursor can be placed anywhere in open space
	// (CursorHeightUp/Down): the cursor's own cell already says everything needed - already terrain
	// means Remove, anything else means Add - so gamepad play no longer needs the mode switch at all.
	//
	// GamepadAreaSelectionController decides GamepadPlacementState.SculptAdd from exactly that check
	// once per press (see its own Update comment for why not every held frame); this patch is what
	// actually acts on it.
	//
	// Calling the matching SculptingTerrainPicker method directly isn't enough on its own: DrawPreview
	// and ApplyChanges (the two callbacks the picker invokes) each independently re-read IsIncreasing
	// themselves - IsValidCandidateBlock's own filter requires Underground(block) to already agree
	// with IsIncreasing, and ApplyChanges picks SetTerrain vs UnsetTerrain off it too - so calling
	// PickTerrainAreaToRemove while the tool's own toggle still says Increase would get every
	// candidate block rejected as "invalid" (Underground didn't match the *stale* IsIncreasing),
	// silently doing nothing. Increase/Inverse are saved and forced to agree with SculptAdd only for
	// the duration of this one synchronous call, then restored immediately after - a mouse user's own
	// Add/Remove buttons are never left touched once this returns.
	[HarmonyPatch(typeof(SculptingTerrainBrushTool))]
	internal static class SculptingTerrainAddRemovePatch
	{
		private static readonly FieldInfo PickerField =
			AccessTools.Field(typeof(SculptingTerrainBrushTool), "_sculptingTerrainPicker");

		private static readonly MethodInfo DrawPreviewMethod =
			AccessTools.Method(typeof(SculptingTerrainBrushTool), "DrawPreview");

		private static readonly MethodInfo ApplyChangesMethod =
			AccessTools.Method(typeof(SculptingTerrainBrushTool), "ApplyChanges");

		[HarmonyPatch(nameof(SculptingTerrainBrushTool.ProcessInput))]
		[HarmonyPrefix]
		private static bool ProcessInputPrefix(SculptingTerrainBrushTool __instance, ref bool __result)
		{
			if (!GamepadPlacementState.Active || !GamepadPlacementState.SculptingActive)
			{
				return true;
			}

			var picker = (SculptingTerrainPicker)PickerField.GetValue(__instance);
			var preview = (AreaPicker.IntAreaCallback)Delegate.CreateDelegate(typeof(AreaPicker.IntAreaCallback),
				__instance, DrawPreviewMethod);
			var action = (AreaPicker.IntAreaCallback)Delegate.CreateDelegate(typeof(AreaPicker.IntAreaCallback),
				__instance, ApplyChangesMethod);

			var originalIncrease = __instance.Increase;
			var originalInverse = __instance.Inverse;
			__instance.Increase = GamepadPlacementState.SculptAdd;
			__instance.Inverse = false;
			try
			{
				__result = GamepadPlacementState.SculptAdd
					? picker.PickTerrainAreaToAdd(preview, action)
					: picker.PickTerrainAreaToRemove(preview, action);
			}
			finally
			{
				__instance.Increase = originalIncrease;
				__instance.Inverse = originalInverse;
			}

			return false;
		}
	}
}
