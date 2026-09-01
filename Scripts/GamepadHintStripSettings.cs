using System.Collections.Generic;
using ModSettings.Common;
using ModSettings.Core;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace ControllerSupport
{
	internal class GamepadHintStripSettings : ModSettingsOwner
	{
		public LimitedStringModSetting Position { get; } = new(
			1, // "Bottom"
			new List<LimitedStringModSettingValue>
			{
				new("Top", "ControllerSupport.Settings.HintStripPosition.Top"),
				new("Bottom", "ControllerSupport.Settings.HintStripPosition.Bottom"),
				new("None", "ControllerSupport.Settings.HintStripPosition.None"),
			},
			ModSettingDescriptor.CreateLocalized("ControllerSupport.Settings.ShowInputHints"));

		public GamepadHintStripSettings(ISettings settings,
			ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) : base(
			settings, modSettingsOwnerRegistry, modRepository)
		{
		}

		public override string HeaderLocKey => "ControllerSupport.Settings.ShortcutHintsHeader";

		protected override string ModId => "Agroqirax.ControllerSupport";
	}
}
