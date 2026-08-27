using Timberborn.InputSystem;

namespace ControllerSupport
{
	// Suppresses MainMouseButtonDown/Held/Up on GamepadPlacementState for as long as the physical
	// Confirm button is still down from whatever earlier press activated the owning controller - most
	// commonly, confirming the bottom-bar button that just switched to this tool. Without this, a tool
	// that starts its own action on Held rather than Down (NaturalResourceSpawningBrushTool/
	// NaturalResourceRemovalBrushTool apply every frame Held is true with no state machine at all;
	// RelativeTerrainHeightBrushTool starts its drag on Held) fires immediately at the freshly-seeded
	// cursor, since the very same physical press that opened the tool is still read as "held" the
	// moment its controller starts publishing button state - Down itself never repeats (it is a
	// one-frame edge, already consumed switching the tool), which is exactly why
	// AreaSelectionController-driven tools (planting, tree-cutting, priority, demolish, deletion,
	// building placement - anything starting its own action on Down) were never vulnerable to this.
	// Applied uniformly rather than only where it was proven to bite, since nothing here can assume how
	// a downstream ITool chooses to start its own drag.
	internal class ConfirmReleaseGate
	{
		private const string ConfirmKey = "Confirm";

		private readonly InputService _inputService;
		private bool _suppress;

		public ConfirmReleaseGate(InputService inputService)
		{
			_inputService = inputService;
		}

		// Call once, from the controller's own Activate(), before anything reads button state for
		// this activation.
		public void Arm()
		{
			_suppress = _inputService.IsKeyHeld(ConfirmKey);
		}

		// Call every active frame, before publishing GamepadPlacementState. Returns whether button
		// state should be forced to "nothing pressed" this frame.
		public bool ShouldSuppress()
		{
			if (_suppress && !_inputService.IsKeyHeld(ConfirmKey))
			{
				_suppress = false;
			}

			return _suppress;
		}
	}
}
