using System.Collections.Generic;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using KeyBindingRegistry = Timberborn.KeyBindingSystem.KeyBindingRegistry;

namespace ControllerSupport
{
	// Main-menu equivalent of GamepadHintStripController - much smaller, since none of the
	// Game/MapEditor-only concepts the full resolver also covers (tools, entity panels, bottom bar,
	// construction/workplace/slider priorities, zoom, game speed) apply here; they all simply read
	// false/gated-off via GamepadHintContext.InGameplayScene, leaving Confirm/Cancel/Move/Scroll as the
	// only hints that can ever actually show. Always mounts at the bottom regardless of the Top/Bottom
	// setting - a top-of-screen strip would sit awkwardly over the main menu's own title art, which
	// has nothing at the top to sit alongside the way the district/weather cluster does in-game.
	//
	// There is no UILayout-equivalent extension point for the main menu to mount into (UILayout itself
	// is only bound for Game/MapEditor - see PanelTracker's own comment on TitleScreen handling
	// PanelStack.Initialize instead), so this reaches PanelTracker's own reflected root
	// ("TitleScreenContent", Views/MainMenu/TitleScreen.uxml) and walks up one level to the real
	// "TitleScreen" element, which already hosts a "Footer" sibling positioned exactly this way
	// (title-screen__footer: position: absolute; bottom: 0) - mounting a same-shaped sibling there
	// just above it keeps our box out of the footer's own version/language-button row.
	internal class GamepadMainMenuHintStripController : ILoadableSingleton, IUpdatableSingleton
	{
		private const float BottomOffset = 40f;

		// Name of the loaded UXML's top-level element (Views/MainMenu/TitleScreen.uxml), sibling to
		// "Footer" - the same name TitleScreen.Initialize itself queries for.
		private const string TitleScreenName = "TitleScreen";

		private readonly ILoc _loc;
		private readonly KeyBindingRegistry _keyBindingRegistry;
		private readonly PanelTracker _panelTracker;
		private readonly DropdownTracker _dropdownTracker;
		private readonly GamepadNavigationInputProcessor _navigationInputProcessor;
		private readonly GamepadHintStripSettings _settings;

		private readonly VisualElement _root = new();
		private GamepadHintStripRenderer _renderer;
		private bool _mounted;

		private GamepadHintContext _lastContext;
		private bool _hasContext;
		private bool _lastGamepadConnected;

		public GamepadMainMenuHintStripController(ILoc loc, KeyBindingRegistry keyBindingRegistry,
			PanelTracker panelTracker, DropdownTracker dropdownTracker,
			GamepadNavigationInputProcessor navigationInputProcessor, GamepadHintStripSettings settings)
		{
			_loc = loc;
			_keyBindingRegistry = keyBindingRegistry;
			_panelTracker = panelTracker;
			_dropdownTracker = dropdownTracker;
			_navigationInputProcessor = navigationInputProcessor;
			_settings = settings;
		}

		public void Load()
		{
			_root.style.position = Position.Absolute;
			_root.style.left = 0;
			_root.style.right = 0;
			_root.style.bottom = BottomOffset;
			_root.style.flexDirection = FlexDirection.Row;
			_root.style.justifyContent = Justify.Center;
			_root.pickingMode = PickingMode.Ignore;

			_renderer = new GamepadHintStripRenderer(_root, _loc, _keyBindingRegistry, () => Screen.width * 0.8f,
				wrapEachHintInPill: true);
		}

		public void UpdateSingleton()
		{
			var gamepad = Gamepad.current;
			if (gamepad == null)
			{
				if (_lastGamepadConnected)
				{
					_lastGamepadConnected = false;
					_root.Clear();
					_hasContext = false;
				}

				return;
			}

			_lastGamepadConnected = true;

			if (_settings.Position.Value == "None")
			{
				_root.ToggleDisplayStyle(visible: false);
				return;
			}

			if (!EnsureMounted())
			{
				return;
			}

			_root.ToggleDisplayStyle(visible: true);

			var context = BuildContext();
			if (_hasContext && context.Equals(_lastContext))
			{
				return;
			}

			_hasContext = true;
			_lastContext = context;
			_renderer.Render(GamepadHintResolver.Resolve(context, _keyBindingRegistry));
		}

		// PanelTracker.StableRoot mirrors PanelStack._root exactly - per PanelStack.Initialize, that's
		// the whole UIDocument's own rootVisualElement, i.e. the parent of "TitleScreen" (the loaded
		// UXML's top-level element), not "TitleScreen" itself and not something further up from it.
		// UILayout's own AddTopLeft/AddBottomBar do the equivalent lookup for the Game scene
		// (Q<VisualElement>("Top-left") etc. on that same kind of root) - this does the same thing by
		// hand for the main menu, querying down for "TitleScreen" (sibling of "Footer") rather than
		// walking up from it, which was the actual bug (nothing ever mounted, since walking .parent
		// from the UIDocument root goes nowhere meaningful).
		//
		// Null until TitleScreen has actually run PanelStack.Initialize, so this retries every tick
		// until it isn't - StableRoot is used specifically (not TopElement) because TopElement prefers
		// whatever's currently stacked, and in the main menu something almost always is (the main
		// buttons/settings/load-game screens are themselves pushed panels, unlike the Game scene's bare
		// HUD), so waiting for "nothing stacked" would never succeed here.
		private bool EnsureMounted()
		{
			if (_mounted)
			{
				return true;
			}

			var titleScreen = _panelTracker.StableRoot?.Q<VisualElement>(TitleScreenName);
			if (titleScreen == null)
			{
				return false;
			}

			titleScreen.Add(_root);
			_mounted = true;
			return true;
		}

		private GamepadHintContext BuildContext()
		{
			var hasStackedPanel = _panelTracker.HasStackedPanel;
			var hasDialogDefaultAction = false;
			if (hasStackedPanel)
			{
				var candidates = new List<VisualElement>();
				NavigationCandidates.Collect(_panelTracker.TopElement, candidates);
				hasDialogDefaultAction = DialogDefaultAction.Find(candidates) != null;
			}

			return new GamepadHintContext(
				hasDialogDefaultAction: hasDialogDefaultAction,
				dropdownOpen: _dropdownTracker.IsOpen,
				toolEngaged: false,
				buildingPlacementActive: false,
				areaSelectionActive: false,
				entityPanelOpen: false,
				scrollableListPresent: _navigationInputProcessor.HasScrollableList,
				bottomBarCategorySelected: false,
				withinOpenBottomBarSubSection: false,
				isUnderConstruction: false,
				hasWorkplace: false,
				hasEntitySlider: false,
				flippable: false,
				inGameplayScene: false,
				hasStackedPanel: hasStackedPanel);
		}
	}
}
