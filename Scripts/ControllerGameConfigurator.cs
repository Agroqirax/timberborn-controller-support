using Bindito.Core;

namespace ControllerSupport
{
	// Separate from the navigation configurator because these only exist in the scenes that have a
	// world in them - CameraService, ToolService and ToolGroupService are all absent from the main
	// menu, and binding them there would fail to resolve.
	[Context("Game")]
	[Context("MapEditor")]
	internal class ControllerGameConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadCameraInputProcessor>().AsSingleton();
			Bind<GamepadToolCancelInputProcessor>().AsSingleton();
			Bind<GamepadBuildingPlacementController>().AsSingleton();
			Bind<GamepadAreaSelectionController>().AsSingleton();
			Bind<GamepadSelectionController>().AsSingleton();
		}
	}
}
