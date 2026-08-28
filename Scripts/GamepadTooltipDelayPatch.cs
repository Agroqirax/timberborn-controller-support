using HarmonyLib;
using Timberborn.TooltipSystem;

namespace ControllerSupport
{
	// Tooltip.Enable is the one place that knows, in the same instant it actually runs, both "was this
	// hover the gamepad's" and "which element" - GamepadTooltipAnchor.Current is set from here so
	// GamepadTooltipPositionPatch can anchor the tooltip to the right element even when the mouse and
	// gamepad are both live (this mod supports mixed input, e.g. Steam Deck trackpads mapped to mouse).
	// A genuine mouse-triggered Enable() clears it back to null so the mouse-driven tooltip still
	// follows the mouse.
	//
	// This used to also force Tooltip's private _wasVisibleLastUpdate false here, to make every gamepad
	// hover restart the show-delay timer instead of appearing instantly. That broke the delay in a
	// different way: forcing it false *before* Tooltip.UpdateSingleton ever observed it true meant
	// UpdateSingleton's own true-to-false edge detection never fired, so the old tooltip's Clear() call
	// was skipped and Enable()'s unconditional content update just silently overwrote the still-visible
	// old tooltip's text - no delay, no hide-then-reshow, just instant new content. The actual fix is in
	// SelectionHighlighter.Tick(): the MouseEnterEvent dispatch that calls Enable() is deferred one whole
	// frame so UpdateSingleton gets a real intervening tick with nothing enabled, the same gap a real
	// mouse gets crossing from one hover target straight to another - letting the game's own edge
	// detection do the hide/reshow cycle correctly instead of needing to be told the internal state
	// directly.
	//
	// Tooltip compiles as public against this mod's Plugins reference assembly (publicized for modding),
	// but that's a compile-time stub only - the assembly the running game actually loads keeps the real
	// (internal) accessibility. Naming the class and method via typeof()/nameof() still works fine since
	// Harmony's own method patching doesn't go through the call/stfld instruction the CLR verifies - it
	// was specifically the direct field write in the old version of this file that crashed the game
	// (see git history), not this attribute usage.
	[HarmonyPatch(typeof(Tooltip))]
	internal static class GamepadTooltipDelayPatch
	{
		[HarmonyPatch(nameof(Tooltip.Enable))]
		[HarmonyPrefix]
		private static void Prefix()
		{
			GamepadTooltipAnchor.Current = VisualElementProbe.IsSyntheticDispatch ? VisualElementProbe.DispatchTarget : null;
		}
	}
}
