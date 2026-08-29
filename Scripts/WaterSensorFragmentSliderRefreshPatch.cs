using System.Reflection;
using HarmonyLib;
using Timberborn.AutomationBuildings;
using Timberborn.CoreUI;

namespace ControllerSupport
{
	// FlowSensorFragment/ContaminationSensorFragment's own UpdateFragment refreshes the threshold
	// label and marker every tick but never re-syncs the slider handle's own position - unlike
	// DepthSensorFragment, which does call ThresholdSlider.UpdateValuesWithoutNotify from its
	// UpdateFragment too. Each only ever calls it once, from ShowFragment.
	//
	// That gap is invisible with mouse-only play: the only way Threshold changes is dragging the
	// slider itself, which already leaves the handle in the right place, so nothing ever needed a
	// resync. GamepadEntitySliderController breaks that assumption - it changes Threshold from
	// outside the slider entirely (the floodgate-height shoulders), and without this the handle
	// visibly lags the panel's own Threshold label until the panel is closed and reopened.
	//
	// Both fragments are internal to the game's assembly, so TargetMethod() is used instead of typeof
	// - same pattern as FloodgateFragmentUnderConstructionPatch. Postfixing UpdateFragment (rather
	// than only patching around GamepadEntitySliderController's own writes) keeps this correct
	// regardless of what changed Threshold - a future mod or console command included - the same way
	// DepthSensorFragment's own always-on resync already is.
	[HarmonyPatch]
	internal static class FlowSensorFragmentSliderRefreshPatch
	{
		private static readonly System.Type FragmentType =
			AccessTools.TypeByName("Timberborn.AutomationBuildingsUI.FlowSensorFragment");

		private static readonly FieldInfo SensorField = AccessTools.Field(FragmentType, "_flowSensor");
		private static readonly FieldInfo SliderField = AccessTools.Field(FragmentType, "_thresholdSlider");

		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(FragmentType, "UpdateFragment");
		}

		[HarmonyPostfix]
		private static void Postfix(object __instance)
		{
			var sensor = (FlowSensor)SensorField.GetValue(__instance);
			if (!sensor)
			{
				return;
			}

			var slider = (PreciseSlider)SliderField.GetValue(__instance);
			slider.UpdateValuesWithoutNotify(sensor.Threshold, 0f, sensor.MaxThreshold);
		}
	}

	[HarmonyPatch]
	internal static class ContaminationSensorFragmentSliderRefreshPatch
	{
		private static readonly System.Type FragmentType =
			AccessTools.TypeByName("Timberborn.AutomationBuildingsUI.ContaminationSensorFragment");

		private static readonly FieldInfo SensorField = AccessTools.Field(FragmentType, "_contaminationSensor");
		private static readonly FieldInfo SliderField = AccessTools.Field(FragmentType, "_thresholdSlider");

		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(FragmentType, "UpdateFragment");
		}

		[HarmonyPostfix]
		private static void Postfix(object __instance)
		{
			var sensor = (ContaminationSensor)SensorField.GetValue(__instance);
			if (!sensor)
			{
				return;
			}

			var slider = (PreciseSlider)SliderField.GetValue(__instance);
			slider.UpdateValuesWithoutNotify(sensor.Threshold, 1f);
		}
	}

	// ResourceCounterFragment has the exact same gap for its FillRate mode slider - UpdateFragment
	// refreshes the label and the marker every tick but never re-syncs the slider handle itself,
	// only setting it once from ShowFragment. Same fix, same reason: GamepadEntitySliderController's
	// shoulder-driven ProcessResourceCounter changes FillRateThreshold from outside the slider.
	[HarmonyPatch]
	internal static class ResourceCounterFragmentSliderRefreshPatch
	{
		private static readonly System.Type FragmentType =
			AccessTools.TypeByName("Timberborn.AutomationBuildingsUI.ResourceCounterFragment");

		private static readonly FieldInfo ResourceCounterField = AccessTools.Field(FragmentType, "_resourceCounter");
		private static readonly FieldInfo SliderField = AccessTools.Field(FragmentType, "_fillRateSlider");

		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(FragmentType, "UpdateFragment");
		}

		[HarmonyPostfix]
		private static void Postfix(object __instance)
		{
			var resourceCounter = (ResourceCounter)ResourceCounterField.GetValue(__instance);
			if (!resourceCounter)
			{
				return;
			}

			var slider = (PreciseSlider)SliderField.GetValue(__instance);
			slider.UpdateValuesWithoutNotify(resourceCounter.FillRateThreshold, 1f);
		}
	}
}
