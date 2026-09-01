using System.Collections.Generic;
using Timberborn.BlockObjectTools;
using Timberborn.BlockSystem;
using Timberborn.ConstructionSites;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.MapStateSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.ToolSystem;
using Timberborn.UILayoutSystem;
using Timberborn.WorkSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using KeyBindingRegistry = Timberborn.KeyBindingSystem.KeyBindingRegistry;

namespace ControllerSupport
{
	// Owns both possible mounts (Top/Bottom) for the input-hint strip and decides when to re-resolve
	// and re-render it.
	//
	// UILayout exposes no way to remove an element once added (AddOrderablePanel only ever inserts),
	// so both roots are built and mounted once at load and left in the tree for the rest of the
	// session - switching the Top|Bottom|None setting just toggles which one is visible, rather than
	// trying to detach/reattach from UILayout's own tracked containers.
	//
	// Runs as a plain IUpdatableSingleton polling a small context struct once a frame, rather than
	// hooking into GamepadNavigationInputProcessor's own event/ordering logic - that class's own
	// comments repeatedly stress how delicate its re-registration order is, and this feature only
	// needs to read its selection state, not participate in input processing at all. The per-frame
	// comparison itself is cheap (a handful of bool/reference reads), the same cost
	// GamepadNavigationInputProcessor.TryRecoverFromClosedToolGroup already accepts as fine to run
	// unconditionally every frame - only an actual context change triggers the more expensive resolve
	// + render.
	internal class GamepadHintStripController : ILoadableSingleton, IUpdatableSingleton
	{
		// Top-right, alongside SpeedControlPanel(2)/WorkingHoursPanel(3)/LevelVisibilityPanel(4)/
		// DatePanel(5)/WeatherPanel(6)/DistrictPanel(9)/ClockPanel(int.MaxValue) in the Game scene.
		// Placed immediately next to DistrictPanel (order 9) rather than at the very front of the row
		// (order 1 was tried first and shifted every other widget's wrap position, since Top-right's
		// column/wrap-reverse layout re-flows based on cumulative width from the front) - inserting
		// right next to one existing neighbour instead disturbs the smallest possible slice of that
		// flow.
		private const int TopOrder = 8;

		// MapEditor's Top-right column is capped at a fixed height (game-ui__top-right's own
		// height: 100px), and the three widgets already there - MapEditorSimulationPanel(1),
		// LevelVisibilityPanel(2), MapEditorHazardousWeatherPanel(3, the weather/season selector) -
		// already use roughly all of it (top-right-item's own min-height: 26px + margin-top: 4px each,
		// ~90px for three), so a fourth item the same size pushes the column over 100px and Yoga wraps
		// the overflow into a brand new column to the left (wrap-reverse). A Top-left mount was tried to
		// dodge that entirely, but ended up on the opposite side of the screen from every other
		// MapEditor top widget, which read as more out-of-place than a wrapped column near them would -
		// back to Top-right, right after the weather/season selector (order 4), even though the wrap
		// this causes is still expected. Inserting ahead of the weather panel instead (to land "above"
		// it) was considered and rejected: with the same 100px budget already short by ~20px, an earlier
		// insertion point doesn't remove the overflow, it just decides which of the four items gets
		// pushed into the wrapped column - moving our own hint box there is far less disruptive than
		// bumping the weather selector itself out of its usual spot.
		private const int MapEditorTopOrder = 4;

		// 300f (the original guess, deliberately conservative for a cramped screen) turned out too
		// small once GamepadHintResolver could return 5 hints at once (select mode: Select, Cancel,
		// Height, Move, Zoom) - the width-fit renderer isn't broken, it was just given too little room
		// and correctly dropped everything past Select/Cancel (reported 2026-08-31, "only A & B show up
		// on the select tool" - not a tie-break issue, since Height/Move/Zoom all rank strictly below
		// Cancel and lost fairly to running out of width, same as intended on a genuinely narrow
		// screen). Sized instead to comfortably fit that same 5-hint worst case at this box's actual
		// icon/label sizes, rather than another arbitrary guess.
		private const float TopMaxWidth = 460f;

		// Name BlockObjectPlacementPanel gives its own root (Views/Common/ToolPanel/
		// BlockObjectPlacementPanel.uxml) - hidden while the bottom strip is substituting its own
		// Rotate/Flip/Cancel/Move hints for it, so the player sees one set of placement hints, not two.
		private const string BlockObjectPlacementPanelName = "BlockObjectPlacementPanel";

		// Distinct from, and before, ToolPanel's own AddBottomBar(_, 50) - see ToolPanel.cs - so the
		// strip sits as a genuine sibling in Bottom-bar rather than inside its fragment row. Bottom-bar
		// is a bottom-anchored flex column with no explicit width of its own, so its full-width
		// screen budget is used directly rather than an arbitrary fraction.
		private const int BottomOrder = 40;

		private readonly UILayout _uiLayout;
		private readonly ILoc _loc;
		private readonly KeyBindingRegistry _keyBindingRegistry;
		private readonly PanelTracker _panelTracker;
		private readonly DropdownTracker _dropdownTracker;
		private readonly ToolService _toolService;
		private readonly EntitySelectionService _entitySelectionService;
		private readonly GamepadNavigationInputProcessor _navigationInputProcessor;
		private readonly GamepadHintStripSettings _settings;
		private readonly MapEditorMode _mapEditorMode;

		// NineSliceVisualElement, not a plain VisualElement: square-large--green only paints anything
		// through NineSliceBackground's mesh-generation, which is baked into that class (and the
		// internal NineSliceButton, unusable from a mod) - a plain element with the same class would
		// stay invisible. One shared green box holds every hint flat inside it (see
		// GamepadHintStripRenderer's wrapEachHintInPill: false), matching the district/weather/speed
		// panels' own look and the "[(A) Select (B) Cancel ...] in one bigger container" request.
		private readonly NineSliceVisualElement _topRoot = new();

		// Plain VisualElement: the bottom mount mirrors BlockObjectPlacementPanel's row of separate
		// pills instead, each pill supplying its own background (see wrapEachHintInPill: true), so the
		// row container itself needs no background of its own. Used for every bottom-strip context
		// EXCEPT building placement - see _placementReplacementRoot below for why that one is different.
		private readonly VisualElement _bottomRoot = new();

		// Building placement is the one context with an existing base-game element to take the exact
		// slot of: BlockObjectPlacementPanel sits inside ToolPanel's own column, positioned relative to
		// its sibling fragments (e.g. the "can't place here" warning) by ToolPanel's own fragment-order
		// sort - a relationship this mod has no reason to know or reproduce. Rather than mount a second
		// independent row via UILayout (which only ever sits as a whole extra sibling above or below the
		// *entire* ToolPanel block, not interleaved with its internal children - the earlier version of
		// this feature did exactly that and put the warning message on the wrong side of the hints),
		// this element is inserted directly into ToolPanel's own tree, immediately next to the hidden
		// BlockObjectPlacementPanel, so it inherits that exact slot and every sibling keeps its original
		// relative position.
		private readonly VisualElement _placementReplacementRoot = new();
		private bool _placementReplacementInserted;

		private GamepadHintStripRenderer _topRenderer;
		private GamepadHintStripRenderer _bottomRenderer;
		private GamepadHintStripRenderer _placementReplacementRenderer;

		private GamepadHintContext _lastContext;
		private bool _hasContext;
		private bool _lastGamepadConnected;
		private int _lastScreenWidth;

		// Resolved lazily the first time it's needed - ToolPanel (which owns it) may not have finished
		// its own Load() yet when this controller's Load() runs, since Bindito gives no ordering
		// guarantee between unrelated singleton chains (same reasoning as GamepadIconRegistry.Load's
		// own comment on this).
		private VisualElement _blockObjectPlacementPanel;
		private bool _hidingBlockObjectPlacementPanel;

		public GamepadHintStripController(UILayout uiLayout, ILoc loc, KeyBindingRegistry keyBindingRegistry,
			PanelTracker panelTracker, DropdownTracker dropdownTracker, ToolService toolService,
			EntitySelectionService entitySelectionService, GamepadNavigationInputProcessor navigationInputProcessor,
			GamepadHintStripSettings settings, MapEditorMode mapEditorMode)
		{
			_uiLayout = uiLayout;
			_loc = loc;
			_keyBindingRegistry = keyBindingRegistry;
			_panelTracker = panelTracker;
			_dropdownTracker = dropdownTracker;
			_toolService = toolService;
			_entitySelectionService = entitySelectionService;
			_navigationInputProcessor = navigationInputProcessor;
			_settings = settings;
			_mapEditorMode = mapEditorMode;
		}

		public void Load()
		{
			// top-right-item/square-large--green/content-row-centered all come from CoreStyle.uss/
			// CommonStyle.uss/GameMiscStyle.uss - each already attached at the root of "Common/GameUI"
			// (see the class-level comment on GamepadHintStripRenderer), so they apply here even though
			// _topRoot is built in code and mounted well after that document first loaded.
			_topRoot.AddToClassList("top-right-item");
			_topRoot.AddToClassList("square-large--green");

			// --no-grow forces a single line (flex-wrap: nowrap) - plain content-row-centered's
			// flex-wrap: wrap let the hints spill onto a second line inside the box well before running
			// out of actual room, roughly doubling its height next to the single-line district/weather/
			// speed boxes for no reason: the same hints fit comfortably on one line at this box's size.
			_topRoot.AddToClassList("content-row-centered--no-grow");

			_bottomRoot.style.flexDirection = FlexDirection.Row;
			_bottomRoot.style.marginTop = 2;

			_placementReplacementRoot.style.flexDirection = FlexDirection.Row;

			_topRenderer = new GamepadHintStripRenderer(_topRoot, _loc, _keyBindingRegistry, () => TopMaxWidth,
				wrapEachHintInPill: false);
			_bottomRenderer = new GamepadHintStripRenderer(_bottomRoot, _loc, _keyBindingRegistry, () => Screen.width,
				wrapEachHintInPill: true);
			_placementReplacementRenderer = new GamepadHintStripRenderer(_placementReplacementRoot, _loc,
				_keyBindingRegistry, () => Screen.width, wrapEachHintInPill: true);

			_uiLayout.AddTopRight(_topRoot, _mapEditorMode.IsMapEditor ? MapEditorTopOrder : TopOrder);
			_uiLayout.AddBottomBar(_bottomRoot, BottomOrder);

			_settings.Position.ValueChanged += (_, value) => ApplyVisibility();
			ApplyVisibility();
		}

		public void UpdateSingleton()
		{
			var gamepad = Gamepad.current;
			if (gamepad == null)
			{
				if (_lastGamepadConnected)
				{
					_lastGamepadConnected = false;
					Clear();
				}

				return;
			}

			_lastGamepadConnected = true;

			if (_settings.Position.Value == "None")
			{
				ApplyPlacementSubstitution(false, IsPlacementToolActive());
				return;
			}

			// Screen.width can change (window resize, UI scale change) without any navigation/context
			// change of its own, and the bottom strip's fit budget is tied to it - re-render so the
			// greedy width-fit re-evaluates against the new size instead of staying stale until the
			// player happens to change selection too.
			if (Screen.width != _lastScreenWidth)
			{
				_lastScreenWidth = Screen.width;
				_hasContext = false;
			}

			var context = BuildContext();

			// Computed every tick, independent of the hasContext/Equals early-out below - which of the
			// two bottom containers is showing must track this exactly, not just whenever the strip
			// happens to re-render.
			var placementToolActive = context.ToolEngaged && context.BuildingPlacementActive;
			var substitutingForPlacementPanel = _settings.Position.Value == "Bottom" && placementToolActive;
			ApplyPlacementSubstitution(substitutingForPlacementPanel, placementToolActive);

			if (_hasContext && context.Equals(_lastContext))
			{
				return;
			}

			_hasContext = true;
			_lastContext = context;
			Render(context, substitutingForPlacementPanel);
		}

		private void ApplyVisibility()
		{
			var position = _settings.Position.Value;
			_topRoot.ToggleDisplayStyle(visible: position == "Top");

			// UpdateSingleton's own "None" early-return would otherwise never run the code that
			// restores this, since it returns before BuildContext ever runs.
			if (position != "Bottom")
			{
				ApplyPlacementSubstitution(false, IsPlacementToolActive());
				_bottomRoot.ToggleDisplayStyle(visible: false);
			}
			else
			{
				_bottomRoot.ToggleDisplayStyle(visible: !_hidingBlockObjectPlacementPanel);
			}

			if (position != "None")
			{
				_hasContext = false; // force a re-render onto whichever root just became visible
			}
		}

		private bool IsPlacementToolActive()
		{
			return GamepadPlacementState.ToolEngaged
				&& GamepadBuildingPlacementController.IsBuildingPlacementTool(_toolService.ActiveTool);
		}

		// Building placement gets its hints inserted directly next to (hidden) BlockObjectPlacementPanel
		// instead of into the normal _bottomRoot - see that field's own comment for why. This switches
		// between the two: hides BlockObjectPlacementPanel and _bottomRoot, inserts/shows
		// _placementReplacementRoot in its slot when substituting.
		//
		// Restoring is NOT simply the mirror image of hiding. BlockObjectPlacementPanel is a real
		// base-game element with its own visibility lifecycle (IToolFragment shows/hides it on
		// ToolEnteredEvent/ToolExitedEvent, independent of this controller) - forcing it back to
		// visible:true unconditionally on every un-substitute used to fight that: leaving the placement
		// tool entirely flips substituting back to false on the very same frame the base game hides the
		// fragment for real, and if this ran after that, it would force the panel visible again with no
		// tool active to ever hide it a second time - "the original hints appear and stay" (reported
		// 2026-08-31). Only force it visible when the placement tool itself is still genuinely active -
		// the one case where restoring is actually this controller's job, e.g. the player switches the
		// mod setting away from Bottom mid-placement. Otherwise, leave the panel alone entirely and
		// trust the base game's own hide-on-exit, exactly as it would behave with this mod's hint strip
		// turned off.
		private void ApplyPlacementSubstitution(bool substituting, bool placementToolActive)
		{
			if (substituting == _hidingBlockObjectPlacementPanel)
			{
				return;
			}

			_blockObjectPlacementPanel ??= _bottomRoot.parent?.Q<VisualElement>(BlockObjectPlacementPanelName);
			if (_blockObjectPlacementPanel == null)
			{
				return;
			}

			if (substituting && !_placementReplacementInserted)
			{
				var parent = _blockObjectPlacementPanel.hierarchy.parent;
				parent.Insert(parent.IndexOf(_blockObjectPlacementPanel) + 1, _placementReplacementRoot);
				_placementReplacementInserted = true;
			}

			if (substituting)
			{
				_blockObjectPlacementPanel.ToggleDisplayStyle(visible: false);
			}
			else if (placementToolActive)
			{
				_blockObjectPlacementPanel.ToggleDisplayStyle(visible: true);
			}

			_placementReplacementRoot.ToggleDisplayStyle(visible: substituting);
			_bottomRoot.ToggleDisplayStyle(visible: !substituting);
			_hidingBlockObjectPlacementPanel = substituting;
		}

		private void Clear()
		{
			_topRoot.Clear();
			_bottomRoot.Clear();
			_placementReplacementRoot.Clear();
			_hasContext = false;
			ApplyPlacementSubstitution(false, IsPlacementToolActive());
		}

		private void Render(GamepadHintContext context, bool substitutingForPlacementPanel)
		{
			var hints = GamepadHintResolver.Resolve(context, _keyBindingRegistry);
			var position = _settings.Position.Value;

			if (position == "Top")
			{
				_topRenderer.Render(hints);
			}
			else if (substitutingForPlacementPanel)
			{
				_placementReplacementRenderer.Render(hints);
			}
			else if (position == "Bottom")
			{
				_bottomRenderer.Render(hints);
			}
		}

		private GamepadHintContext BuildContext()
		{
			var activeTool = _toolService.ActiveTool;
			var selected = _navigationInputProcessor.Selected;
			var hasStackedPanel = _panelTracker.HasStackedPanel;
			var entityPanelOpen = _entitySelectionService.IsAnythingSelected && !hasStackedPanel;

			var hasDialogDefaultAction = false;
			if (hasStackedPanel)
			{
				var candidates = new List<VisualElement>();
				NavigationCandidates.Collect(_panelTracker.TopElement, candidates);
				hasDialogDefaultAction = DialogDefaultAction.Find(candidates) != null;
			}

			// Only meaningful (and only computed) while an entity panel is actually open - mirrors the
			// same three-way LB/RB precedence FloodgateFragmentUnderConstructionPatch/
			// ConstructionSiteFragmentFinishedPriorityPatch/GamepadEntitySliderController already
			// establish for the real input, so GamepadHintResolver's Shoulders rules can reproduce it for
			// display without re-deriving it a third time.
			var isUnderConstruction = false;
			var hasWorkplace = false;
			var hasEntitySlider = false;
			if (entityPanelOpen)
			{
				var selectedObject = _entitySelectionService.SelectedObject;
				var constructionSite = selectedObject.GetComponent<ConstructionSite>();
				isUnderConstruction = constructionSite && constructionSite.Enabled;
				hasWorkplace = selectedObject.GetComponent<Workplace>() && selectedObject.GetComponent<WorkplacePriority>();
				hasEntitySlider = GamepadEntitySliderController.HasApplicableSlider(selectedObject);
			}

			// Mirrors BlockObjectPlacementPanel.OnToolEntered's own
			// _tool.Template.GetSpec<BlockObjectSpec>().Flippable check exactly, so the hint strip only
			// ever offers Flip for the same buildings the vanilla panel would show its own Flip button
			// for.
			var flippable = activeTool is BlockObjectTool blockObjectTool
				&& blockObjectTool.Template.GetSpec<BlockObjectSpec>().Flippable;

			return new GamepadHintContext(
				hasDialogDefaultAction: hasDialogDefaultAction,
				dropdownOpen: _dropdownTracker.IsOpen,
				toolEngaged: GamepadPlacementState.ToolEngaged,
				buildingPlacementActive: GamepadBuildingPlacementController.IsBuildingPlacementTool(activeTool),
				areaSelectionActive: GamepadAreaSelectionController.IsAreaSelectionTool(activeTool),
				entityPanelOpen: entityPanelOpen,
				scrollableListPresent: _navigationInputProcessor.HasScrollableList,
				bottomBarCategorySelected: BottomBarNavigation.SubSectionFor(selected) != null,
				withinOpenBottomBarSubSection: BottomBarNavigation.IsWithinOpenSubSection(selected),
				isUnderConstruction: isUnderConstruction,
				hasWorkplace: hasWorkplace,
				hasEntitySlider: hasEntitySlider,
				flippable: flippable,
				inGameplayScene: true,
				hasStackedPanel: hasStackedPanel);
		}
	}
}
