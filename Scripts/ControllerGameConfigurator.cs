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
			Bind<RecentInputDeviceTracker>().AsSingleton();

			// One instance per cursor controller, not a singleton: Collect() hands back its own reusable
			// buffer, so a shared instance would be a list two controllers could overwrite for each
			// other. They cost nothing (three small objects, created once at load) and this mod has
			// already been bitten once by shared mutable state between these three classes - see
			// GamepadPlacementState's own notes on the clear/write hazard.
			Bind<GamepadCursorLevels>().AsTransient();
			Bind<GamepadBuildingPlacementController>().AsSingleton();
			Bind<GamepadAreaSelectionController>().AsSingleton();
			Bind<GamepadSelectionController>().AsSingleton();
			Bind<GamepadEntitySliderController>().AsSingleton();
		}
	}
}
