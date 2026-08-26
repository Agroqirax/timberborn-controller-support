using System;
using System.Reflection;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.ConstructionSites;
using Timberborn.InputSystem;

namespace ControllerSupport
{
	// FloodgateFragment.ShowFragment enables its own height-adjustment IInputProcessor
	// (IncreaseFloodgateHeight/DecreaseFloodgateHeight) as soon as the Floodgate component exists -
	// unlike Workplace, Floodgate is present from placement, so this fires *during* construction too.
	// Meanwhile ConstructionSiteFragment's own builder-priority group is also legitimately enabled for
	// the same entity while it's still being built (see ConstructionSiteFragmentFinishedPriorityPatch
	// for the separate, already-fixed bug where that group wrongly outlives construction). With both
	// IncreaseBuildersPriority/DecreaseBuildersPriority and IncreaseFloodgateHeight/
	// DecreaseFloodgateHeight bound to the same physical shoulders, both processors are simultaneously
	// active on a floodgate under construction, and whichever got registered more recently wins every
	// press - deterministic, but arbitrary from the player's seat.
	//
	// Builder priority already wins this exact tug-of-war for workplaces (construction priority beats
	// the post-construction control while a building is unfinished, purely as a side effect of
	// ConstructionSiteFragmentFinishedPriorityPatch only switching the balance back once finished) - so
	// this patch makes floodgates follow the same explicit rule instead of leaving the outcome to
	// registration order: under construction, builder priority is the only one listening; once
	// finished, ConstructionSiteFragmentFinishedPriorityPatch has already disabled the priority group,
	// so height naturally has the shoulders to itself.
	//
	// FloodgateFragment can't be named with typeof from this assembly (internal to the game's) -
	// TargetMethod() is the same reliable pattern used in ConstructionSiteFragmentFinishedPriorityPatch.
	[HarmonyPatch]
	internal static class FloodgateFragmentUnderConstructionPatch
	{
		private static readonly Type FragmentType =
			AccessTools.TypeByName("Timberborn.WaterBuildingsUI.FloodgateFragment");

		private static readonly FieldInfo InputServiceField = AccessTools.Field(FragmentType, "_inputService");

		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(FragmentType, "ShowFragment");
		}

		[HarmonyPostfix]
		private static void Postfix(object __instance, BaseComponent entity)
		{
			var constructionSite = entity.GetComponent<ConstructionSite>();
			if (constructionSite && constructionSite.Enabled)
			{
				var inputService = (InputService)InputServiceField.GetValue(__instance);
				inputService.RemoveInputProcessor((IInputProcessor)__instance);
			}
		}
	}
}
