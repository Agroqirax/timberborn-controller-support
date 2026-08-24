namespace ControllerSupport
{
	// One-shot signal from GamepadSelectionController (writer, a priority processor that always runs
	// before the regular chain, every frame, unconditionally) to GamepadNavigationInputProcessor
	// (reader, a regular processor).
	//
	// Exiting the gamepad cursor submode on B is this mod's own invented behaviour, with no keybind
	// of its own - it piggybacks on the shared "Cancel" signal (see GamepadSelectionController.Update),
	// which the game's own CursorTool ALSO reacts to directly: CursorTool.ProcessUnselectObject
	// deselects whatever is selected (closing the entity panel) on the very same Cancel value, every
	// frame it's true, regardless of what this mod does. Left alone, the same B press that exits
	// select mode would also deselect/close the panel this same frame, since KeyBindingRegistry's
	// Cancel value is frame-global and a priority processor can never block the regular chain from
	// also seeing it (ProcessInput returns void, there is nothing to swallow with).
	//
	// Set true for exactly the frame select mode was exited via Cancel; the regular chain reads and
	// clears it once, swallowing that frame's Cancel before CursorTool's own regular processor can act
	// on it - so closing the panel genuinely needs a second, separate press.
	internal static class GamepadSelectModeCancelGate
	{
		public static bool ConsumeNextCancel;
	}
}
