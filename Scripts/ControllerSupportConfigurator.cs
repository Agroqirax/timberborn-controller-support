using Bindito.Core;

namespace ControllerSupport
{
	[Context("MainMenu")]
	internal class ControllerSupportConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<PanelTracker>().AsSingleton();
			Bind<GamepadNavigationInputProcessor>().AsSingleton();
		}
	}
}
