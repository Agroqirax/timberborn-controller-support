using ModSettings.Core;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace ControllerSupport
{
	internal class GamepadShortcutIconSettings : ModSettingsOwner
	{
		public ModSetting<bool> ShowIcons { get; } =
			new(true, ModSettingDescriptor.CreateLocalized("ControllerSupport.Settings.ShowGamepadShortcutIcons"));

		public GamepadShortcutIconSettings(ISettings settings,
			ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) : base(
			settings, modSettingsOwnerRegistry, modRepository)
		{
		}

		public override string HeaderLocKey => "ControllerSupport.Settings.ShortcutHintsHeader";

		protected override string ModId => "Agroqirax.ControllerSupport";
	}
}
