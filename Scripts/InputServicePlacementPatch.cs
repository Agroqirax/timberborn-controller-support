using HarmonyLib;
using Timberborn.InputSystem;
using UnityEngine;

namespace ControllerSupport
{
	// AreaSelectionController - the class that actually drives ghost preview, drag and placement for
	// every area/block-object tool - reads these InputService properties directly, with no public
	// seam to hand it button state some other way. Feeding it gamepad-driven values here, while
	// GamepadBuildingPlacementController is steering, is the only way in without reimplementing its
	// validation logic. Mouse-driven play falls through untouched. The grid cell itself is injected
	// separately, in CameraServicePlacementPatch - MousePosition plays no part in that any more.
	[HarmonyPatch(typeof(InputService))]
	internal static class InputServicePlacementPatch
	{
		[HarmonyPatch("MainMouseButtonDown", MethodType.Getter)]
		[HarmonyPrefix]
		private static bool MainMouseButtonDown(ref bool __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			__result = GamepadPlacementState.MainMouseButtonDown;
			return false;
		}

		[HarmonyPatch("MainMouseButtonHeld", MethodType.Getter)]
		[HarmonyPrefix]
		private static bool MainMouseButtonHeld(ref bool __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			__result = GamepadPlacementState.MainMouseButtonHeld;
			return false;
		}

		[HarmonyPatch("MainMouseButtonUp", MethodType.Getter)]
		[HarmonyPrefix]
		private static bool MainMouseButtonUp(ref bool __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			__result = GamepadPlacementState.MainMouseButtonUp;
			return false;
		}

		// MouseOverUI comes from EventSystem.IsPointerOverGameObject() against the real OS cursor,
		// which this mod never moves - it is wherever the desktop mouse was last left, often resting
		// over some UI chrome. AreaSelectionController forces _startRay to null whenever this is true,
		// which means no ghost preview at all. Placement driven by the gamepad is always a
		// world-space action, never blocked by whatever the literal mouse happens to be sitting over.
		[HarmonyPatch("MouseOverUI", MethodType.Getter)]
		[HarmonyPrefix]
		private static bool MouseOverUI(ref bool __result)
		{
			if (!GamepadPlacementState.Active)
			{
				return true;
			}

			__result = false;
			return false;
		}
	}
}
