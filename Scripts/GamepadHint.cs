using KeyBindingRegistry = Timberborn.KeyBindingSystem.KeyBindingRegistry;

namespace ControllerSupport
{
	// One entry in the input-hint strip: a gamepad button/stick icon plus a short label.
	//
	// Exactly one of KeyBindingId/FixedIconKey is set. A hint backed by a real single action
	// (Confirm, Cancel, Rotate, ...) always carries a KeyBindingId, resolved through the same
	// KeyBindingRegistry -> GamepadBindingSelector -> GamepadIconRegistry pipeline the existing
	// shortcut-hint-icon feature already uses (GamepadShortcutIcon.cs), so the icon can never show a
	// button that doesn't match what's actually bound. Only a composite action with no single
	// keybinding behind it - the stick/d-pad "Move" hint - uses a hand-picked FixedIconKey instead,
	// since there is no one KeyBinding to resolve for "any direction".
	internal readonly struct GamepadHint
	{
		public readonly string LabelLocKey;
		public readonly string KeyBindingId;
		public readonly string FixedIconKey;

		private GamepadHint(string labelLocKey, string keyBindingId, string fixedIconKey)
		{
			LabelLocKey = labelLocKey;
			KeyBindingId = keyBindingId;
			FixedIconKey = fixedIconKey;
		}

		public static GamepadHint ForBinding(string labelLocKey, string keyBindingId)
		{
			return new GamepadHint(labelLocKey, keyBindingId, null);
		}

		public static GamepadHint Fixed(string labelLocKey, string fixedIconKey)
		{
			return new GamepadHint(labelLocKey, null, fixedIconKey);
		}

		// The physical control this hint actually points at, as a sprite-lookup key - shared by
		// GamepadHintResolver (to recognise two candidate hints as "the same shoulder/trigger/button,
		// only one of them should show") and GamepadHintStripRenderer (to fetch the sprite to draw),
		// so the two never drift into resolving a binding two different ways. Null when a KeyBindingId
		// hint has no gamepad binding at all - callers treat that as "this hint can't show right now".
		public string ResolveIconKey(KeyBindingRegistry keyBindingRegistry)
		{
			if (FixedIconKey != null)
			{
				return FixedIconKey;
			}

			var keyBinding = keyBindingRegistry.Get(KeyBindingId);
			var gamepadBinding = keyBinding != null ? GamepadBindingSelector.GetGamepadBinding(keyBinding) : null;
			return gamepadBinding != null ? GamepadShortcutIcon.GetIconKey(gamepadBinding) : null;
		}
	}
}
