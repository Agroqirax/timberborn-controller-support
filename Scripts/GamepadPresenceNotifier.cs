using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.StoreSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Steam Input defaults every controller to a desktop (keyboard/mouse) layout, under which the
	// device drives the cursor instead of appearing as Gamepad.current - see the Environment note in
	// ARCHITECTURE.md. A player who hasn't switched to a gamepad-style layout gets no feedback at all
	// as to why this mod's controls do nothing, so warn them once at startup and offer a shortcut
	// straight to the layout picker for this game's Steam Input configuration. This is Steam-specific
	// (Steam Input plus the steam:// deep link), so it's gated on IStore actually resolving to
	// SteamStore - Timberborn also ships GOG/Epic builds with no equivalent layout picker or URL
	// scheme, and IStore is the game's own store-agnostic abstraction for telling them apart.
	internal class GamepadPresenceNotifier : ILoadableSingleton
	{
		private const string ControllerConfigUrl = "steam://controllerconfig/1062090/3790637915";
		private const string SteamStoreTypeName = "Timberborn.SteamStoreSystem.SteamStore";

		private readonly DialogBoxShower _dialogBoxShower;
		private readonly ILoc _loc;
		private readonly IStore _store;

		public GamepadPresenceNotifier(DialogBoxShower dialogBoxShower, ILoc loc, IStore store)
		{
			_dialogBoxShower = dialogBoxShower;
			_loc = loc;
			_store = store;
		}

		public void Load()
		{
			if (Gamepad.current != null || _store.GetType().FullName != SteamStoreTypeName)
			{
				return;
			}

			_dialogBoxShower.Create()
				.SetLocalizedMessage("ControllerSupport.NoGamepadDialog.Message")
				.SetConfirmButton(OpenControllerLayout, _loc.T("ControllerSupport.NoGamepadDialog.OpenLayoutButton"))
				.SetCancelButton(() => { }, _loc.T("ControllerSupport.NoGamepadDialog.IgnoreButton"))
				.Show();
		}

		private static void OpenControllerLayout()
		{
			Application.OpenURL(ControllerConfigUrl);
		}
	}
}
