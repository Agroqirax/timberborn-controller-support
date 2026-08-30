using Bindito.Core;

namespace ControllerSupport
{
	[Context("MainMenu")]
	[Context("Game")]
	[Context("MapEditor")]
	internal class GamepadShortcutIconSettingsConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadShortcutIconSettings>().AsSingleton();
		}
	}

	[Context("Game")]
	[Context("MapEditor")]
	internal class GamepadShortcutIconConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadIconRegistry>().AsSingleton();
		}
	}
}
