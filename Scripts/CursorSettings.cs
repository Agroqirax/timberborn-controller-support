using ModSettings.Core;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace ControllerSupport
{
	internal class CursorSettings : ModSettingsOwner
	{
		public ModSetting<bool> AutohideCursor { get; } =
			new(true, ModSettingDescriptor.CreateLocalized("ControllerSupport.Settings.AutohideCursor"));

		public CursorSettings(ISettings settings, ModSettingsOwnerRegistry modSettingsOwnerRegistry,
			ModRepository modRepository) : base(settings, modSettingsOwnerRegistry, modRepository)
		{
		}

		public override string HeaderLocKey => "ControllerSupport.Settings.CursorHeader";

		protected override string ModId => "Agroqirax.ControllerSupport";
	}
}
