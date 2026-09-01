using System.Collections.Generic;
using ModSettings.Common;
using ModSettings.Core;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace ControllerSupport
{
	internal class CursorSettings : ModSettingsOwner
	{
		public LimitedStringModSetting HideCursor { get; } = new(
			1, // "Auto"
			new List<LimitedStringModSettingValue>
			{
				new("Always", "ControllerSupport.Settings.HideCursor.Always"),
				new("Auto", "ControllerSupport.Settings.HideCursor.Auto"),
				new("Never", "ControllerSupport.Settings.HideCursor.Never"),
			},
			ModSettingDescriptor.CreateLocalized("ControllerSupport.Settings.HideCursor"));

		public ModSetting<bool> FocusEntityPanelOnDeselect { get; } = new(true,
			ModSettingDescriptor.CreateLocalized("ControllerSupport.Settings.FocusEntityPanelOnDeselect"));

		public CursorSettings(ISettings settings, ModSettingsOwnerRegistry modSettingsOwnerRegistry,
			ModRepository modRepository) : base(settings, modSettingsOwnerRegistry, modRepository)
		{
		}

		public override string HeaderLocKey => "ControllerSupport.Settings.CursorHeader";

		protected override string ModId => "Agroqirax.ControllerSupport";
	}
}
