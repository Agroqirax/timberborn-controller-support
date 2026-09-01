using Bindito.Core;

namespace ControllerSupport
{
	[Context("MainMenu")]
	[Context("Game")]
	[Context("MapEditor")]
	internal class CursorSettingsConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<CursorSettings>().AsSingleton();
		}
	}

	// All three contexts, unlike ControllerGameConfigurator's four world-tool controllers: neither
	// class here depends on CameraService/ToolService/ToolGroupService, and cursor autohide has to
	// run in the main menu too.
	[Context("MainMenu")]
	[Context("Game")]
	[Context("MapEditor")]
	internal class CursorAutohideConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<RecentInputDeviceTracker>().AsSingleton();
			Bind<CursorAutohideController>().AsSingleton();
		}
	}
}
