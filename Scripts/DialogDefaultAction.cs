using System.Collections.Generic;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Finds the element that would be a dialog's default action - the same one keyboard Enter
	// triggers via IPanelController.OnUIConfirmed(). There is no marker class or interface exposing
	// this generically - no default-button/accept-button CSS, no IsDefault flag anywhere in the
	// game's UI. The one consistent signal is that DialogBoxShower's own dialogs always name their
	// confirm button "ConfirmButton", which covers exactly the confirmation and warning dialogs this
	// matters most for (not enough science points, unlock this building for N points, and the like).
	// Falls through to nothing for panels that don't follow that convention, the same way
	// BottomBarNavigation.DefaultTool falls through for anything that isn't the bare HUD.
	internal static class DialogDefaultAction
	{
		private const string ConfirmButtonName = "ConfirmButton";

		public static VisualElement Find(List<VisualElement> candidates)
		{
			foreach (var candidate in candidates)
			{
				if (candidate.name == ConfirmButtonName)
				{
					return candidate;
				}
			}

			return null;
		}
	}
}
