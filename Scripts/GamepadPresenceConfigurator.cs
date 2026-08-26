using Bindito.Core;

namespace ControllerSupport
{
	// MainMenu only: it's the first scene loaded, so this is the natural place to warn about a
	// missing/misconfigured gamepad before the player ever reaches the game where this mod matters.
	[Context("MainMenu")]
	internal class GamepadPresenceConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadPresenceNotifier>().AsSingleton();
		}
	}
}
