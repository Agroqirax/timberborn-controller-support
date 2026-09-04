using System.Reflection;

namespace ControllerSupport
{
	// Bridges the RadialToolbarIntegration Harmony patches (writers, static, can't take constructor
	// DI) to RadialToolbarGamepadController (reader, a real singleton) - same shape as
	// GamepadPlacementState. Left entirely at defaults (null/false) for the whole session whenever
	// RadialToolbar isn't installed or its shape has changed, since TryApply bails out before ever
	// touching this class in that case.
	internal static class RadialToolbarState
	{
		// Edge-triggered by the Show()/Dismiss() postfixes only, never per-frame - see this mod's own
		// notes on GamepadPlacementState's shared-static clear/write hazard for why.
		public static bool IsOpen;

		// The wedge index most recently passed to ToolbarElement.HighlightSegment, from *either* the
		// native mouse-hover path or RadialToolbarGamepadController's stick-preview path - both funnel
		// through the same patched method, so this one field is enough to make confirm work no matter
		// which drove the current highlight. Cleared on Show()/Dismiss() to match ToolbarElement.Show()
		// setting its own frame.HighlightedSegment to null directly (bypassing HighlightSegment, so the
		// patch alone wouldn't see it).
		public static int? LastHighlighted;

		// Captured once each from a Load() postfix - both ToolbarElement and ToolbarController are
		// [BindSingleton], so one instance lives for the whole session.
		public static object ToolbarElementInstance;
		public static object ToolbarControllerInstance;

		// Captured from a GetSegments(Rect) postfix instead of a Load() - ToolbarSegmentProvider has no
		// loadable lifecycle hook of its own, but ToolbarElement.Show() always calls GetSegments every
		// time the toolbar opens, so this is refreshed reliably every session regardless of whether the
		// player ever touches a real mouse.
		public static object SegmentProviderInstance;

		// Captured from a Reset() postfix - fires at the start of every Show(), before
		// ToolbarNavigator has no loadable lifecycle hook of its own either.
		public static object NavigatorInstance;

		public static MethodInfo HighlightSegmentMethod;
		public static MethodInfo OnSegmentChosenMethod;
		public static MethodInfo GetSegmentAtMethod;

		// Property getters, not methods, but MethodInfo (via AccessTools.PropertyGetter) works
		// identically for Invoke - kept in the same field shape as the rest for consistency.
		// CurrentItemMethod: ToolbarNavigator.CurrentItem (a RadialToolbar.Models.ToolbarSegmentItem).
		// ChildrenMethod: ToolbarSegmentItem.Children (a ToolbarSegmentItem?[]?), invoked on whatever
		// CurrentItemMethod returns. Used only for the auto-highlight fallback (RadialToolbar has no
		// "primary wedge" concept of its own - see RadialToolbarIntegration.AutoHighlightFirstPopulated)
		// - null if RadialToolbar's shape changed just for this optional piece, in which case that
		// fallback is silently skipped while the rest of the integration still works.
		public static MethodInfo CurrentItemMethod;
		public static MethodInfo ChildrenMethod;
	}
}
