using Bindito.Core;

namespace ControllerSupport
{
	[Context("MainMenu")]
	[Context("Game")]
	internal class ControllerHapticsSettingsConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<ControllerHapticsSettings>().AsSingleton();
		}
	}

	[Context("Game")]
	internal class ControllerHapticsConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadHapticsController>().AsSingleton();
			Bind<HazardousWeatherHapticsNotifier>().AsSingleton();
			Bind<WonderCompletionHapticsNotifier>().AsSingleton();
			Bind<DynamiteExplosionHapticsNotifier>().AsSingleton();
		}
	}
}
