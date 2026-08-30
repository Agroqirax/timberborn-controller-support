using ModSettings.Core;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace ControllerSupport
{
	internal class ControllerHapticsSettings : ModSettingsOwner
	{
		public ModSetting<bool> EnableVibration { get; } =
			new(true, ModSettingDescriptor.CreateLocalized("ControllerSupport.Settings.EnableVibration"));

		public ControllerHapticsSettings(ISettings settings,
			ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) : base(
			settings, modSettingsOwnerRegistry, modRepository)
		{
		}

		public override string HeaderLocKey => "ControllerSupport.Settings.HapticsHeader";

		protected override string ModId => "Agroqirax.ControllerSupport";
	}
}
