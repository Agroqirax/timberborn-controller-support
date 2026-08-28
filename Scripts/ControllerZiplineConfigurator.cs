using Bindito.Core;

namespace ControllerSupport
{
	// Separate from ControllerGameConfigurator (bound in both Game and MapEditor) because
	// ZiplineTowerRegistry only exists in the Game context - ziplines can't be placed in the map
	// editor - so binding GamepadZiplineConnectionController alongside the shared controllers there
	// would fail to resolve its constructor in MapEditor.
	[Context("Game")]
	internal class ControllerZiplineConfigurator : Configurator
	{
		protected override void Configure()
		{
			Bind<GamepadZiplineConnectionController>().AsSingleton();
		}
	}
}
