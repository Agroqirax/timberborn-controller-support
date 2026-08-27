using Timberborn.InputSystem;

namespace ControllerSupport
{
	// Suppresses MainMouseButtonDown/Held/Up on GamepadPlacementState for as long as the physical
	// Confirm button is still down from whatever earlier press activated the owning controller - most
	// commonly, confirming the bottom-bar button that just switched to this tool - and for the single
	// frame after that, the one where the button's release itself surfaces as MainMouseButtonUp.
	//
	// Both halves matter. A tool that starts its own action on Held rather than Down
	// (NaturalResourceSpawningBrushTool/NaturalResourceRemovalBrushTool apply every frame Held is true
	// with no state machine at all; RelativeTerrainHeightBrushTool starts its drag on Held) fires
	// immediately at the freshly-seeded cursor if Held carries over - that's the first half. But
	// AreaSelectionController (planting, tree-cutting, priority, demolish, deletion, building
	// placement) turned out to be vulnerable too, just to the *other* edge: confirmed via Player.log,
	// its very first check only requires _startRay.HasValue, not a prior Down/_selectionStarted -
	// and _startRay is continuously refreshed every idle frame to drive the hover preview, so it is
	// essentially always populated. That means a bare MainMouseButtonUp commits a placement at
	// whatever the hover ray currently points at, with no preceding Down required at all - so merely
	// swallowing Held while the button was still down (stopping the instant Held reads false) let the
	// stale Up on that exact release-transition frame straight through, auto-placing at the freshly
	// center-seeded cursor the moment the player let go of the button that had opened the tool.
	// Suppressing one extra frame past the release closes that gap: by the following frame Up has
	// already lapsed back to false on its own (it is a one-frame edge, same as Down), so nothing is
	// lost by waiting.
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

		// Call at most once per active frame, before publishing GamepadPlacementState - it mutates
		// state, so a second call the same frame would wrongly see the release already consumed.
		// Returns whether button state should be forced to "nothing pressed" this frame.
		public bool ShouldSuppress()
		{
			if (!_suppress)
			{
				return false;
			}

			if (!_inputService.IsKeyHeld(ConfirmKey))
			{
				// Just detected the release. This exact frame carries the stale MainMouseButtonUp
				// edge for the same physical press - suppress it too, then stop from next frame on.
				_suppress = false;
			}

			return true;
		}
	}
}
