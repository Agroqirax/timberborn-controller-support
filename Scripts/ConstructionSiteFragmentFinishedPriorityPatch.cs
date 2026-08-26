using System;
using System.Reflection;
using HarmonyLib;
using Timberborn.ConstructionSites;
using Timberborn.PrioritySystemUI;

namespace ControllerSupport
{
	// ConstructionSiteFragment.ShowFragment enables its own builder-priority PriorityToggleGroup
	// (IncreaseBuildersPriority/DecreaseBuildersPriority) whenever the selected entity still HAS a
	// ConstructionSite component - it never checks ConstructionSite.Enabled, only that the component
	// exists. ConstructionSite.OnExitUnfinishedState() only *disables* the component when a building
	// finishes (BaseComponent.Enabled = false); the component is only ever actually destroyed if the
	// entity also carries a DeleteOnFinishConstructionSite marker
	// (ConstructionSite.FinishIfRequirementsMet), which not every building type has. So for any
	// finished building whose ConstructionSite component survives (disabled, not destroyed),
	// reselecting it re-enables the stale group every time - UpdateFragment does check .Enabled before
	// showing the fragment's own UI, but ShowFragment's input-registration path never got the same
	// check.
	//
	// That stale group's ProcessInput() (Timberborn.PrioritySystemUI.PriorityToggleGroup) returns true
	// unconditionally whenever its own IsKeyDown fires, regardless of whether raising/lowering priority
	// actually changed anything - stopping the input-processor chain (last-registered-first) before any
	// other, more-recently-registered processor bound to the same physical control gets a turn. On
	// keyboard this is invisible: BuildersPriority and WorkplacePriority have entirely separate default
	// keys, so nothing ever collides. On gamepad, with only two shoulder buttons to go around,
	// double-binding both IncreaseBuildersPriority/DecreaseBuildersPriority and
	// IncreaseWorkplacePriority/DecreaseWorkplacePriority to the same physical shoulders (a reasonable
	// remap, since only one of the two ever applies to a given building) means the stale
	// BuildersPriority group silently swallows every press meant for WorkplaceFragment's own priority
	// buttons - permanently, since a still-disabled-but-present ConstructionSite component means this
	// keeps re-enabling on every reselect. Floodgates never collide this way since they have no
	// Workplace component to begin with. Builder priority winning while genuinely under construction is
	// the more useful default anyway; this only stops that rule from continuing to apply once the
	// building is actually finished.
	//
	// Fix: postfix ShowFragment and undo the Enable() call when the component that triggered it is
	// present but disabled, mirroring the .Enabled check UpdateFragment already applies to visibility.
	// Both touched fields are private on an internal type with no public seam, hence the reflection.
	//
	// ConstructionSiteFragment can't be named with typeof from this assembly (it's internal to the
	// game's), and attribute-driven [HarmonyPatch("TypeName")]/[HarmonyPatch("MethodName")] stacking
	// does not resolve a bare type name the way it looks like it should - Harmony threw "Undefined
	// target method" for that form. TargetMethod() is the documented, reliable way to hand Harmony a
	// MethodBase for a type only reachable via AccessTools.TypeByName.
	[HarmonyPatch]
	internal static class ConstructionSiteFragmentFinishedPriorityPatch
	{
		private static readonly Type FragmentType =
			AccessTools.TypeByName("Timberborn.ConstructionSitesUI.ConstructionSiteFragment");

		private static readonly FieldInfo ConstructionSiteField = AccessTools.Field(FragmentType, "_constructionSite");

		private static readonly FieldInfo PriorityToggleGroupField =
			AccessTools.Field(FragmentType, "_priorityToggleGroup");

		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(FragmentType, "ShowFragment");
		}

		[HarmonyPostfix]
		private static void Postfix(object __instance)
		{
			var constructionSite = (ConstructionSite)ConstructionSiteField.GetValue(__instance);
			if (constructionSite && !constructionSite.Enabled)
			{
				((PriorityToggleGroup)PriorityToggleGroupField.GetValue(__instance)).Disable();
			}
		}
	}
}
