using HarmonyLib;
using Timberborn.BlockSystem;

namespace ControllerSupport
{
	// PreviewPlacer.ShowSinglePreview decides between showing a red "invalid" ghost and showing
	// nothing at all based on BlockObject.IsAlmostValid(), which comes down to
	// BlockValidator.BlocksAlmostValid returning true only if ANY block in the footprint is clear.
	// A single-cell building fully on top of an obstruction has zero clear blocks, so this reads
	// false and the ghost is hidden outright - the same as it would be for a mouse, except a mouse
	// user still sees their literal pointer regardless. A gamepad player has nothing else to go by.
	//
	// Forcing this true while gamepad placement is driving makes a fully-blocked cell behave the
	// same way a partly-blocked multi-cell footprint already does: shown, in red, never silently
	// hidden. This only feeds the preview's show/hide choice - actual placement still goes through
	// BlockObjectValidationService untouched, so nothing invalid can ever actually get built.
	[HarmonyPatch(typeof(BlockValidator), nameof(BlockValidator.BlocksAlmostValid))]
	internal static class BlockValidatorPlacementPatch
	{
		[HarmonyPostfix]
		private static void Postfix(ref bool __result)
		{
			if (GamepadPlacementState.Active)
			{
				__result = true;
			}
		}
	}
}
