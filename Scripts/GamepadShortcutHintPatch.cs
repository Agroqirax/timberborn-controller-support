using HarmonyLib;
using Timberborn.KeyBindingSystem;
using Timberborn.KeyBindingSystemUI;
using UnityEngine.InputSystem;
using InputBinding = Timberborn.KeyBindingSystem.InputBinding;
using KeyBinding = Timberborn.KeyBindingSystem.KeyBinding;
using KeyBindingRegistry = Timberborn.KeyBindingSystem.KeyBindingRegistry;

namespace ControllerSupport
{
	// Shared by both patches below: whenever a gamepad is connected at all - regardless of whether
	// mouse/keyboard is also connected or was more recently touched - prefer whichever of
	// Primary/Secondary is actually bound to a Gamepad control, regardless of which slot it lives in
	// (a rebound keybind could put the gamepad button on either).
	internal static class GamepadBindingSelector
	{
		public static InputBinding GetGamepadBinding(KeyBinding keyBinding)
		{
			if (IsGamepadBinding(keyBinding.PrimaryInputBinding))
			{
				return keyBinding.PrimaryInputBinding;
			}
			if (IsGamepadBinding(keyBinding.SecondaryInputBinding))
			{
				return keyBinding.SecondaryInputBinding;
			}
			return null;
		}

		private static bool IsGamepadBinding(InputBinding binding)
		{
			return binding.IsDefined && binding.InputControl?.device is Gamepad;
		}
	}

	// Base-game shortcut hints (e.g. "F" above the rotate/flip buttons on the placement panel) are
	// built by KeyBindingShortcutService.CreateAny -> new DefinableInputBinding(keyBinding, null) ->
	// KeyBindingShortcut.Update() -> DefinableInputBinding.TryGetDefinedInputBinding(out inputBinding).
	// That last call is where Primary-vs-Secondary actually gets decided (Primary if defined, else
	// Secondary) - InputBindingDescriber.GetInputBindingText(InputBinding) only ever formats whatever
	// binding it's handed, it never chooses between them. (An earlier version of this patch targeted
	// GetInputBindingText(string) instead, which turned out to be dead code for this path - nothing
	// here ever calls that overload.)
	//
	// This mod's own gamepad bindings are added as SecondaryInputBindingSpec overrides on top of the
	// base game's keyboard Primary (see Root/KeyBindings/**/*.blueprint.json), so the vanilla
	// Primary-else-Secondary rule always picked the keyboard key.
	[HarmonyPatch(typeof(DefinableInputBinding))]
	internal static class GamepadShortcutHintPatch
	{
		[HarmonyPatch(nameof(DefinableInputBinding.TryGetDefinedInputBinding))]
		[HarmonyPostfix]
		private static void Postfix(DefinableInputBinding __instance, bool? ____isPrimary, ref InputBinding inputBinding, ref bool __result)
		{
			// Only override the "any" query CreateAny uses (isPrimary == null). CreateSingle passes an
			// explicit true/false when a UI specifically wants to show/rebind one particular slot -
			// that choice must not be second-guessed here.
			if (____isPrimary.HasValue || Gamepad.current == null)
			{
				return;
			}

			var gamepadBinding = GamepadBindingSelector.GetGamepadBinding(__instance.KeyBinding);
			if (gamepadBinding == null)
			{
				return;
			}

			inputBinding = gamepadBinding;
			__result = true;
		}
	}

	// Separate code path from the one above: tooltips registered via
	// ITooltipRegistrar.RegisterWithKeyBinding (e.g. the speed control buttons' "Game Speed" tooltip)
	// AND KeyBindingTooltipFactory.AddKeyBindingInfo (the water-opacity/construction-guidelines/
	// stockpile-overlay toggle panels' "Toggle X / Hold Y" tooltips) both go through
	// KeyBindingDescriber.TryGetKeyBindingText, which reads KeyBinding.PrimaryInputBinding/
	// SecondaryInputBinding directly and never touches DefinableInputBinding at all - so
	// GamepadShortcutHintPatch above does not cover any of them. Same gamepad-preference rule,
	// different choke point. (The tooltip's key-binding getter is a Func<string> re-invoked fresh
	// every time the tooltip is shown, so no live-refresh event is needed here the way it is for the
	// other path.)
	[HarmonyPatch(typeof(KeyBindingDescriber))]
	internal static class GamepadTooltipShortcutHintPatch
	{
		[HarmonyPatch(nameof(KeyBindingDescriber.TryGetKeyBindingText))]
		[HarmonyPrefix]
		private static bool Prefix(KeyBindingRegistry ____keyBindingRegistry, InputBindingDescriber ____inputBindingDescriber, string keyBindingKey, ref string keyBindingText, ref bool __result)
		{
			if (keyBindingKey == null || Gamepad.current == null)
			{
				return true;
			}

			var gamepadBinding = GamepadBindingSelector.GetGamepadBinding(____keyBindingRegistry.Get(keyBindingKey));
			if (gamepadBinding == null)
			{
				return true;
			}

			keyBindingText = ____inputBindingDescriber.GetInputBindingText(gamepadBinding);
			__result = true;
			return false;
		}
	}

	// A third, independent code path: InputBindingDescriber.GetInputBindingText(string keyBindingId)
	// does its own Primary-vs-Secondary selection inline and is called directly (bypassing both
	// choke points above) by CharacterControlFragment.InitializeFragment (the entity panel's
	// "Move To [F]" button label) and DevPanel (debug-only button labels). Same rule, third choke
	// point - this is the overload an earlier version of GamepadShortcutHintPatch targeted, abandoned
	// when it turned out unrelated to rotate/flip; it turns out to be exactly the right target for
	// these two labels.
	[HarmonyPatch(typeof(InputBindingDescriber))]
	internal static class GamepadDirectShortcutHintPatch
	{
		[HarmonyPatch(nameof(InputBindingDescriber.GetInputBindingText), typeof(string))]
		[HarmonyPrefix]
		private static bool Prefix(InputBindingDescriber __instance, KeyBindingRegistry ____keyBindingRegistry, string keyBindingId, ref string __result)
		{
			if (Gamepad.current == null)
			{
				return true;
			}

			var gamepadBinding = GamepadBindingSelector.GetGamepadBinding(____keyBindingRegistry.Get(keyBindingId));
			if (gamepadBinding == null)
			{
				return true;
			}

			__result = __instance.GetInputBindingText(gamepadBinding);
			return false;
		}
	}
}
