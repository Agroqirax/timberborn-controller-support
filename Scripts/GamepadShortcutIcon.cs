using System.Reflection;
using Timberborn.CoreUI;
using Timberborn.KeyBindingSystem;
using Timberborn.KeyBindingSystemUI;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using InputBinding = Timberborn.KeyBindingSystem.InputBinding;

namespace ControllerSupport
{
	// Swaps a shortcut hint's text (e.g. "Xbox: Y") for the matching button icon whenever the resolved
	// binding is a gamepad control and an icon is available - GamepadShortcutIconPatch's Update()
	// postfix is the only caller. ShortcutTextElement wraps its TextElement privately with no accessor,
	// so this reflects the field once rather than pull it into the patch's Harmony field-injection
	// (that convention only reaches KeyBindingShortcut's own private fields, not ones nested a level
	// deeper on ShortcutTextElement).
	internal static class GamepadShortcutIcon
	{
		private const string IconElementName = "ControllerSupport-ShortcutIcon";
		private const float DisplayHeight = 20f;

		private static readonly FieldInfo TextElementField =
			typeof(ShortcutTextElement).GetField("_textElement", BindingFlags.NonPublic | BindingFlags.Instance);

		private static readonly FieldInfo IsPrimaryField =
			typeof(DefinableInputBinding).GetField("_isPrimary", BindingFlags.NonPublic | BindingFlags.Instance);

		internal static void Apply(ShortcutTextElement shortcutTextElement, DefinableInputBinding definableInputBinding)
		{
			if (TextElementField?.GetValue(shortcutTextElement) is not TextElement textElement)
			{
				return;
			}

			// Only the CreateAny query (KeyBindingShortcutService.CreateAny, used by every actual game-UI
			// hint - rotate/flip, brush tools, tutorial steps) constructs its DefinableInputBinding with a
			// null isPrimary. CreateSingle (used only by KeyBindingRowFactory, the keybind rebinding menu's
			// per-slot Primary/Secondary rows) always passes an explicit true/false - those rows are meant
			// to show the real raw binding for that specific slot, not a gamepad-preferred icon, so this
			// mirrors GamepadShortcutHintPatch's own "____isPrimary.HasValue" scoping in the patch file.
			if (IsPrimaryField?.GetValue(definableInputBinding) is bool)
			{
				ShowText(textElement);
				return;
			}

			if (!TryGetIconSprite(definableInputBinding, out var sprite))
			{
				ShowText(textElement);
				return;
			}

			ShowIcon(textElement, sprite);
		}

		private static bool TryGetIconSprite(DefinableInputBinding definableInputBinding, out UnityEngine.Sprite sprite)
		{
			sprite = null;
			if (!GamepadIconRegistry.IconsEnabled)
			{
				return false;
			}
			if (!definableInputBinding.TryGetDefinedInputBinding(out var inputBinding))
			{
				return false;
			}
			if (inputBinding.InputControl?.device is not Gamepad)
			{
				return false;
			}

			var key = GetIconKey(inputBinding);
			sprite = GamepadIconRegistry.Get(key);
			return sprite != null;
		}

		// InputControl.path looks like "/XInputControllerWindows/leftStick/up" - dropping the leading
		// empty segment (from the leading slash) and the device-name segment leaves exactly the control
		// path this mod's icon PNGs are named after (e.g. "leftStick_up"), the same names the SVG source
		// groups already use.
		private static string GetIconKey(InputBinding inputBinding)
		{
			var segments = inputBinding.InputControl.path.Split('/');
			return string.Join("_", segments, 2, segments.Length - 2);
		}

		// Only touches the text element's own display state when reverting away from a previously-shown
		// icon - ShortcutTextElement.SetShortcut/SetUndefinedShortcut (called by the original Update()
		// this method is postfixed onto) already decided the correct display state for plain text
		// (including the alwaysVisible-false "hide when undefined" case), and that decision must not be
		// second-guessed here when no icon was ever shown for this element.
		private static void ShowText(TextElement textElement)
		{
			var iconElement = GetExistingIconElement(textElement);
			if (iconElement == null || !iconElement.IsDisplayed())
			{
				return;
			}

			iconElement.ToggleDisplayStyle(visible: false);
			if (!string.IsNullOrEmpty(textElement.text))
			{
				textElement.ToggleDisplayStyle(visible: true);
			}
		}

		private static void ShowIcon(TextElement textElement, UnityEngine.Sprite sprite)
		{
			textElement.ToggleDisplayStyle(visible: false);

			var iconElement = GetExistingIconElement(textElement) ?? CreateIconElement(textElement);
			iconElement.ToggleDisplayStyle(visible: true);
			iconElement.style.backgroundImage = new StyleBackground(sprite);

			var aspectRatio = sprite.rect.width / sprite.rect.height;
			iconElement.style.height = DisplayHeight;
			iconElement.style.width = DisplayHeight * aspectRatio;
		}

		private static VisualElement GetExistingIconElement(TextElement textElement)
		{
			return textElement.parent?.Q<VisualElement>(IconElementName);
		}

		private static VisualElement CreateIconElement(TextElement textElement)
		{
			var iconElement = new VisualElement { name = IconElementName };
			var parent = textElement.parent;
			parent.Insert(parent.IndexOf(textElement) + 1, iconElement);
			return iconElement;
		}
	}
}
