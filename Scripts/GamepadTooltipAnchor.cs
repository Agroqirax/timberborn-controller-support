using Timberborn.CameraSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Which element the currently-shown Tooltip should be anchored next to, if it was triggered by the
	// gamepad cursor rather than the real mouse. Set by GamepadTooltipDelayPatch from inside
	// Tooltip.Enable - the one place that sees both "did this Enable call come from our synthetic
	// dispatch" and "which element" in the same moment - and read by GamepadTooltipPositionPatch.
	//
	// This mod lets mouse/keyboard and gamepad drive the UI at the same time (a Steam Deck can map its
	// trackpads to the real mouse, for one), so "a gamepad selection currently exists" is not the same
	// question as "did the gamepad trigger the tooltip that's on screen right now" - a real mouse hover
	// happening alongside an idle gamepad selection must still anchor to the mouse. Every Enable() call
	// updates this one way or the other, so it always reflects whichever hover most recently won.
	internal static class GamepadTooltipAnchor
	{
		public static VisualElement Current { get; set; }

		// Same idea as Current, but for a tooltip that isn't hovering a UI element at all -
		// ZiplinePreviewTooltip.ShowTooltip goes straight through TooltipContainer.ShowPriority, never
		// touching Tooltip.Enable, so Current is never set for it and the tooltip would otherwise keep
		// following the real mouse cursor no matter how far that is from the towers actually being
		// connected. Set to a world-space point (e.g. the midpoint between the two zipline towers) while
		// the gamepad is actively driving the connection tool, and cleared whenever the mouse takes over
		// or the tool exits, so a real mouse click still gets the ordinary cursor-following tooltip.
		public static Vector3? WorldPosition { get; set; }

		// Set once (constructor injection reaches every Harmony patch's static context otherwise), by
		// whichever gamepad controller needs world-space tooltip anchoring - currently only
		// GamepadZiplineConnectionController.
		public static CameraService CameraService { get; set; }
	}
}
