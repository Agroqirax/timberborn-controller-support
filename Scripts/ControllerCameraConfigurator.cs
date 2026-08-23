using Bindito.Core;

namespace ControllerSupport
{
	// Separate from the navigation configurator because CameraService only exists in the scenes that
	// have a camera - binding this in MainMenu would fail to resolve.
	[Context("Game")]
	[Context("MapEditor")]
	internal class ControllerCameraConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadCameraInputProcessor>().AsSingleton();
		}
	}
}
