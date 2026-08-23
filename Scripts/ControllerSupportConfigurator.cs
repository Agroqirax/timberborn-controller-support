using Bindito.Core;

namespace ControllerSupport
{
	// UI navigation works the same in every scene: PanelStack, DropdownListDrawer and
	// UISoundController are all bound in all three contexts.
	[Context("MainMenu")]
	[Context("Game")]
	[Context("MapEditor")]
	internal class ControllerSupportConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<PanelTracker>().AsSingleton();
			Bind<DropdownTracker>().AsSingleton();
			Bind<GamepadNavigationInputProcessor>().AsSingleton();
		}
	}
}
