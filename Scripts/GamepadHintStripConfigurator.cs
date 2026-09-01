using Bindito.Core;

namespace ControllerSupport
{
	[Context("MainMenu")]
	[Context("Game")]
	[Context("MapEditor")]
	internal class GamepadHintStripSettingsConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadHintStripSettings>().AsSingleton();
		}
	}

	[Context("Game")]
	[Context("MapEditor")]
	internal class GamepadHintStripFeatureConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadHintStripController>().AsSingleton();
		}
	}

	[Context("MainMenu")]
	internal class GamepadMainMenuHintStripFeatureConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadMainMenuHintStripController>().AsSingleton();
		}
	}
}
