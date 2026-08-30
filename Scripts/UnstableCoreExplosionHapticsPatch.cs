using System.Reflection;
using HarmonyLib;

namespace ControllerSupport
{
	// UnstableCoreEffectsSpawner.SpawnEffects() is the single choke point for every unstable-core
	// explosion's VFX + sound (called from UnstableCore.Explode()), so it's also the cleanest place
	// to trigger a gamepad rumble. The containing class is internal to Timberborn.Explosions, so it
	// can't be named with typeof from this assembly - same situation as
	// ConstructionSiteFragmentFinishedPriorityPatch.cs, and the same fix applies: resolve the type by
	// name via TargetMethod() rather than a class-level [HarmonyPatch(type, "Method")] attribute,
	// which silently fails to register in this project's Harmony setup.
	[HarmonyPatch]
	internal static class UnstableCoreExplosionHapticsPatch
	{
		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(
				AccessTools.TypeByName("Timberborn.Explosions.UnstableCoreEffectsSpawner"), "SpawnEffects");
		}

		[HarmonyPostfix]
		private static void Postfix()
		{
			GamepadHapticsController.Pulse(0.7f, 0.4f, 0.35f);
		}
	}
}
