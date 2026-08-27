using HarmonyLib;

namespace ControllerSupport
{
	// ShadowDistanceUpdater (Timberborn.CameraSystem) recomputes QualitySettings.shadowDistance every
	// LateUpdate by ray-casting from the real camera through all four screen corners onto the ground
	// plane and taking the farthest hit, via CameraService.ScreenPointToRayInWorldSpace - a completely
	// unrelated caller from the placement one CameraServicePlacementPatch.WorldSpacePrefix exists for.
	// While GamepadPlacementState.Active, that patch hands back the same synthetic straight-down ray
	// (from CameraServicePlacementPatch.RayHeight, above whatever cell the gamepad is tracking) no
	// matter which screenPoint is asked for, so all four "corners" report the same ~1000-unit hit on
	// the ground plane, clamped to ShadowDistanceUpdater.MaxDistance (150) - shadow distance gets
	// pinned to its maximum on every frame a gamepad tool is active, regardless of how close the camera
	// actually is. URP spreads its shadow-map resolution across that wrongly enlarged distance, so
	// nearby shadows come out blurry or fade out entirely - the reported symptom, and exactly why it
	// never happens with the mouse (GamepadPlacementState.Active is never true then) or in the unmodded
	// game.
	//
	// Suspending Active for the one call is enough: it is the only field CameraServicePlacementPatch
	// and InputServicePlacementPatch check, ShadowDistanceUpdater never touches GridCursor or button
	// state, and every writer (GamepadBuildingPlacementController/GamepadAreaSelectionController)
	// reasserts Active on its very next ProcessInput regardless, so a one-LateUpdate dip is invisible
	// to placement itself.
	// Two-arg [HarmonyPatch(type, "MethodName")] at the class level - the pattern every other
	// single-method patch in this mod uses - turned out to register with Harmony (GetPatchInfo
	// after PatchAll showed prefixes=0/postfixes=0 in Player.log even though AccessTools.Method
	// resolved the same MethodInfo just fine) but never actually apply. Every OTHER patch class in
	// this codebase that targets more than a bare getter repeats [HarmonyPatch(nameof(Method))] on
	// each individual Prefix/Postfix method, stacked on top of a class-level [HarmonyPatch(typeof(X))]
	// - matching that proven pattern here instead of the shorthand.
	[HarmonyPatch(typeof(Timberborn.CameraSystem.ShadowDistanceUpdater))]
	internal static class ShadowDistanceUpdaterPatch
	{
		[HarmonyPatch("UpdateShadowDistance")]
		[HarmonyPrefix]
		private static void Prefix(out bool __state)
		{
			__state = GamepadPlacementState.Active;
			GamepadPlacementState.Active = false;
		}

		[HarmonyPatch("UpdateShadowDistance")]
		[HarmonyPostfix]
		private static void Postfix(bool __state)
		{
			GamepadPlacementState.Active = __state;
		}
	}
}
