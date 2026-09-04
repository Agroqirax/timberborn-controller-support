using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ControllerSupport
{
	// RadialToolbar (optional workshop mod, "Radial Toolbar [ModContest1]", 3714134407) is optional and
	// this mod must stay fully inert without it - same reasoning as FPPCameraIntegration/
	// BuildingBlueprintsIntegration, so RadialToolbar.dll is never referenced at compile time and
	// everything below is applied manually from TryApply, only after confirming the assembly is
	// actually loaded.
	//
	// Opening/closing the toolbar itself is left entirely to RadialToolbar's own native handling
	// (its ToggleRadialToolbar keybind, driven by whatever the player's Steam Input config maps to it)
	// and to its own mouse-hover-and-click path (already works with zero patches, since it's genuine UI
	// Toolkit pointer picking). What's added here is a parallel left-stick preview
	// (RadialToolbarGamepadController.LateUpdateSingleton, driven by the state captured below) and a
	// repurposed UIConfirm ("A") that commits whatever is currently highlighted - see
	// RadialToolbarGamepadController for why that needs a registered IInputProcessor, not just a patch.
	internal static class RadialToolbarIntegration
	{
		public static void TryApply(Harmony harmony)
		{
			var assembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name == "RadialToolbar");
			if (assembly == null)
			{
				return;
			}

			try
			{
				TryApplyPatches(assembly, harmony);
			}
			catch (Exception e)
			{
				Debug.LogError($"[ControllerSupport] RadialToolbar integration failed to start: {e}");
			}
		}

		private static void TryApplyPatches(Assembly assembly, Harmony harmony)
		{
			var elementType = assembly.GetType("RadialToolbar.UI.ToolbarElement");
			var controllerType = assembly.GetType("RadialToolbar.UI.ToolbarController");
			var providerType = assembly.GetType("RadialToolbar.Services.ToolbarSegmentProvider");
			if (elementType == null || controllerType == null || providerType == null)
			{
				Debug.LogWarning("[ControllerSupport] RadialToolbar shape has changed - skipping gamepad support for it.");
				return;
			}

			var elementLoad = AccessTools.Method(elementType, "Load");
			var highlightSegment = AccessTools.Method(elementType, "HighlightSegment", new[] { typeof(int?) });
			var controllerLoad = AccessTools.Method(controllerType, "Load");
			var show = AccessTools.Method(controllerType, "Show");
			var dismiss = AccessTools.Method(controllerType, "Dismiss");
			var onSegmentChosen = AccessTools.Method(controllerType, "OnSegmentChosen", new[] { typeof(int) });
			var getSegments = AccessTools.Method(providerType, "GetSegments", new[] { typeof(Rect) });
			var getSegmentAt = AccessTools.Method(providerType, "GetSegmentAt", new[] { typeof(Vector3) });

			if (elementLoad == null || highlightSegment == null || controllerLoad == null || show == null
				|| dismiss == null || onSegmentChosen == null || getSegments == null || getSegmentAt == null)
			{
				Debug.LogWarning("[ControllerSupport] RadialToolbar shape has changed - skipping gamepad support for it.");
				return;
			}

			RadialToolbarState.HighlightSegmentMethod = highlightSegment;
			RadialToolbarState.OnSegmentChosenMethod = onSegmentChosen;
			RadialToolbarState.GetSegmentAtMethod = getSegmentAt;

			harmony.Patch(elementLoad, postfix: new HarmonyMethod(typeof(ElementPatches), nameof(ElementPatches.LoadPostfix)));
			harmony.Patch(highlightSegment,
				postfix: new HarmonyMethod(typeof(ElementPatches), nameof(ElementPatches.HighlightSegmentPostfix)));
			harmony.Patch(controllerLoad,
				postfix: new HarmonyMethod(typeof(ControllerPatches), nameof(ControllerPatches.LoadPostfix)));
			harmony.Patch(show, postfix: new HarmonyMethod(typeof(ControllerPatches), nameof(ControllerPatches.ShowPostfix)));
			harmony.Patch(dismiss,
				postfix: new HarmonyMethod(typeof(ControllerPatches), nameof(ControllerPatches.DismissPostfix)));
			harmony.Patch(onSegmentChosen,
				postfix: new HarmonyMethod(typeof(ControllerPatches), nameof(ControllerPatches.OnSegmentChosenPostfix)));
			harmony.Patch(getSegments,
				postfix: new HarmonyMethod(typeof(ProviderPatches), nameof(ProviderPatches.GetSegmentsPostfix)));

			TryApplyAutoHighlight(assembly, harmony, controllerType);
		}

		// RadialToolbar has no "primary wedge" concept of its own (confirmed by reading its decompiled
		// source - ToolbarSegmentItem/ToolbarNavigator/ToolbarSegmentItemProvider have no such field),
		// so opening the menu or navigating a level leaves nothing highlighted until the player either
		// moves the mouse or pushes the stick. This auto-highlights the first populated wedge (index
		// order in Children already runs N, NE, E, SE, S, SW, W, NW - confirmed from
		// ToolbarSegmentProvider.GetLocation's own index-to-Direction mapping) every time a ring
		// appears, so A/UIConfirm has an immediate sensible default without blocking the player from
		// looking around first. Kept optional and separate from the mandatory patches above: if
		// RadialToolbar's shape has changed just for this piece, everything else still works.
		private static void TryApplyAutoHighlight(Assembly assembly, Harmony harmony, Type controllerType)
		{
			var navigatorType = assembly.GetType("RadialToolbar.UI.ToolbarNavigator");
			var itemType = assembly.GetType("RadialToolbar.Models.ToolbarSegmentItem");
			if (navigatorType == null || itemType == null)
			{
				Debug.LogWarning("[ControllerSupport] RadialToolbar shape has changed - skipping the auto-highlight fallback.");
				return;
			}

			var reset = AccessTools.Method(navigatorType, "Reset");
			var navigateBack = AccessTools.Method(controllerType, "NavigateBack");
			var currentItem = AccessTools.PropertyGetter(navigatorType, "CurrentItem");
			var children = AccessTools.PropertyGetter(itemType, "Children");
			if (reset == null || navigateBack == null || currentItem == null || children == null)
			{
				Debug.LogWarning("[ControllerSupport] RadialToolbar shape has changed - skipping the auto-highlight fallback.");
				return;
			}

			RadialToolbarState.CurrentItemMethod = currentItem;
			RadialToolbarState.ChildrenMethod = children;

			harmony.Patch(reset, postfix: new HarmonyMethod(typeof(NavigatorPatches), nameof(NavigatorPatches.ResetPostfix)));
			harmony.Patch(navigateBack,
				postfix: new HarmonyMethod(typeof(ControllerPatches), nameof(ControllerPatches.NavigateBackPostfix)));
		}

		// Called once a ring is known to be showing with nothing highlighted yet: right after Show()
		// finishes (which is what leaves navigator/toolbarElement/frame all freshly reset), after
		// OnSegmentChosen recurses into a child ring, and after stepping back up to a parent ring.
		private static void AutoHighlightFirstPopulated()
		{
			if (RadialToolbarState.NavigatorInstance == null || RadialToolbarState.CurrentItemMethod == null
				|| RadialToolbarState.ChildrenMethod == null || RadialToolbarState.ToolbarElementInstance == null
				|| RadialToolbarState.HighlightSegmentMethod == null)
			{
				return;
			}

			var currentItem = RadialToolbarState.CurrentItemMethod.Invoke(RadialToolbarState.NavigatorInstance, null);
			if (currentItem == null)
			{
				return;
			}

			if (!(RadialToolbarState.ChildrenMethod.Invoke(currentItem, null) is Array children))
			{
				return;
			}

			for (var i = 0; i < children.Length; i++)
			{
				if (children.GetValue(i) != null)
				{
					RadialToolbarState.HighlightSegmentMethod.Invoke(RadialToolbarState.ToolbarElementInstance,
						new object[] { i });
					return;
				}
			}
		}

		private static class ElementPatches
		{
			public static void LoadPostfix(object __instance)
			{
				RadialToolbarState.ToolbarElementInstance = __instance;
			}

			// Fires for every call regardless of caller - the native mouse-hover path and
			// RadialToolbarGamepadController's own stick-preview calls both go through this one public
			// method, so tracking it here is enough to make confirm work for either source.
			public static void HighlightSegmentPostfix(int? segment)
			{
				RadialToolbarState.LastHighlighted = segment;
			}
		}

		private static class ControllerPatches
		{
			public static void LoadPostfix(object __instance)
			{
				RadialToolbarState.ToolbarControllerInstance = __instance;
			}

			public static void ShowPostfix()
			{
				RadialToolbarState.IsOpen = true;
				RadialToolbarState.LastHighlighted = null;
				RadialToolbarGamepadController.Instance?.NotifyShown();

				// Runs after Show()'s own body (including toolbarElement.Show(), which resets the
				// highlight to null directly rather than through the patched HighlightSegment) has
				// already finished - the only point where auto-highlighting here won't immediately be
				// wiped out again.
				AutoHighlightFirstPopulated();
			}

			public static void DismissPostfix()
			{
				RadialToolbarState.IsOpen = false;
				RadialToolbarState.LastHighlighted = null;
				RadialToolbarGamepadController.Instance?.NotifyDismissed();
			}

			// OnSegmentChosen always clears the highlight to null as its own last line, whether it just
			// recursed into a child ring or fired a leaf action and dismissed - IsOpen already reflects
			// which one happened by the time this postfix runs (Dismiss(), if called, happens
			// synchronously inside OnSegmentChosen's own body, ahead of this), so this only re-highlights
			// when the menu is still genuinely showing a new ring.
			public static void OnSegmentChosenPostfix()
			{
				if (RadialToolbarState.IsOpen)
				{
					AutoHighlightFirstPopulated();
				}
			}

			// NavigateBack either steps up to the parent ring (still open) or, at the root, dismisses
			// the whole menu - same IsOpen-guard reasoning as OnSegmentChosenPostfix.
			public static void NavigateBackPostfix()
			{
				if (RadialToolbarState.IsOpen)
				{
					AutoHighlightFirstPopulated();
				}
			}
		}

		private static class NavigatorPatches
		{
			// Fires at the start of every Show() (navigator.Reset() runs before toolbarElement.Show()),
			// and is the only reliable capture point for the singleton instance - ToolbarNavigator has
			// no Load()/lifecycle hook of its own to patch instead.
			public static void ResetPostfix(object __instance)
			{
				RadialToolbarState.NavigatorInstance = __instance;
			}
		}

		private static class ProviderPatches
		{
			public static void GetSegmentsPostfix(object __instance)
			{
				RadialToolbarState.SegmentProviderInstance = __instance;
			}
		}
	}
}
