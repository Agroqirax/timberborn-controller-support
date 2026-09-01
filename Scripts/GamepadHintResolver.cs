using System;
using System.Collections.Generic;
using System.Linq;
using KeyBindingRegistry = Timberborn.KeyBindingSystem.KeyBindingRegistry;

namespace ControllerSupport
{
	// What the player is doing right now, as far as picking hints is concerned. Built fresh by
	// GamepadHintStripController from state this mod already tracks elsewhere (PanelTracker,
	// DropdownTracker, ToolService, EntitySelectionService, GamepadPlacementState,
	// GamepadNavigationInputProcessor) - this struct exists purely to keep GamepadHintResolver a pure,
	// dependency-free function of its inputs, the same shape as DialogDefaultAction/BottomBarNavigation.
	internal readonly struct GamepadHintContext
	{
		public readonly bool HasDialogDefaultAction;
		public readonly bool DropdownOpen;
		public readonly bool ToolEngaged;
		public readonly bool BuildingPlacementActive;
		public readonly bool AreaSelectionActive;
		public readonly bool EntityPanelOpen;
		public readonly bool ScrollableListPresent;

		// Selected is a bottom-bar MainSection category button, whether or not its row has ever been
		// opened - matches BottomBarNavigation.SubSectionFor's own semantics exactly (confirming OPENS
		// the row, so Confirm's hint applies here regardless of the row's current state).
		public readonly bool BottomBarCategorySelected;

		// Selected has actually moved onto a tool inside an OPEN category's row - see
		// BottomBarNavigation.IsWithinOpenSubSection. Weaker than BottomBarCategorySelected above:
		// merely highlighting a category (row not open yet) satisfies that one but not this one. Cancel
		// needs this stronger check specifically - B only usefully closes an open row, and conflating
		// the two made Cancel show while just browsing MainSection with nothing open (reported
		// 2026-08-31, "it's just when the cursor is on the bottombar").
		public readonly bool WithinOpenBottomBarSubSection;

		// The three LB/RB meanings this mod's own real input processors already prioritise between
		// (ConstructionSiteFragmentFinishedPriorityPatch/FloodgateFragmentUnderConstructionPatch/
		// GamepadEntitySliderController) - construction beats workplace/slider beats nothing selected.
		// See GamepadHintResolver.Rules' "Shoulders" section for where that precedence is reproduced
		// for display.
		public readonly bool IsUnderConstruction;
		public readonly bool HasWorkplace;
		public readonly bool HasEntitySlider;

		// Mirrors BlockObjectPlacementPanel.OnToolEntered's own
		// _tool.Template.GetSpec<BlockObjectSpec>().Flippable check - only meaningful (and only
		// computed) while BuildingPlacementActive; Flip must not be offered as a hint for a building
		// that has no flip orientation at all (most don't).
		public readonly bool Flippable;

		// False in the main menu - MainMenuUI has no camera to zoom and no game speed to adjust, so
		// Zoom and the Shoulders section's bare fallback (Speed) both gate on this to stay out of a
		// scene that has neither. Everything else in this struct already reads false there naturally
		// (ToolEngaged/EntityPanelOpen/etc. all depend on Game/MapEditor-only services), so this is the
		// one signal that genuinely needed adding rather than falling out for free.
		public readonly bool InGameplayScene;

		// True for any panel PanelStack has pushed, not just ones DialogDefaultAction can recognise a
		// button on. PanelStack.ProcessInput's own Cancel handling (TopPanel.PanelController.
		// OnUICancelled()) works generically for every pushed panel regardless of its content - a
		// submenu with no "ConfirmButton"/"Exit" (main menu's new-game creation screen, the load-game
		// save selector) still genuinely backs out on B, it just has no equally generic *default
		// confirm* action for Confirm's own HasDialogDefaultAction rule to key on. Cancel needs this
		// weaker, more general signal instead of HasDialogDefaultAction (reported 2026-08-31: "on a
		// submenu... it doesn't show B back").
		public readonly bool HasStackedPanel;

		public GamepadHintContext(bool hasDialogDefaultAction, bool dropdownOpen, bool toolEngaged,
			bool buildingPlacementActive, bool areaSelectionActive, bool entityPanelOpen,
			bool scrollableListPresent, bool bottomBarCategorySelected, bool withinOpenBottomBarSubSection,
			bool isUnderConstruction, bool hasWorkplace, bool hasEntitySlider, bool flippable,
			bool inGameplayScene, bool hasStackedPanel)
		{
			HasDialogDefaultAction = hasDialogDefaultAction;
			DropdownOpen = dropdownOpen;
			ToolEngaged = toolEngaged;
			BuildingPlacementActive = buildingPlacementActive;
			AreaSelectionActive = areaSelectionActive;
			EntityPanelOpen = entityPanelOpen;
			ScrollableListPresent = scrollableListPresent;
			BottomBarCategorySelected = bottomBarCategorySelected;
			WithinOpenBottomBarSubSection = withinOpenBottomBarSubSection;
			IsUnderConstruction = isUnderConstruction;
			HasWorkplace = hasWorkplace;
			HasEntitySlider = hasEntitySlider;
			Flippable = flippable;
			InGameplayScene = inGameplayScene;
			HasStackedPanel = hasStackedPanel;
		}

		public bool Equals(GamepadHintContext other)
		{
			return HasDialogDefaultAction == other.HasDialogDefaultAction
				&& DropdownOpen == other.DropdownOpen
				&& ToolEngaged == other.ToolEngaged
				&& BuildingPlacementActive == other.BuildingPlacementActive
				&& AreaSelectionActive == other.AreaSelectionActive
				&& EntityPanelOpen == other.EntityPanelOpen
				&& ScrollableListPresent == other.ScrollableListPresent
				&& BottomBarCategorySelected == other.BottomBarCategorySelected
				&& WithinOpenBottomBarSubSection == other.WithinOpenBottomBarSubSection
				&& IsUnderConstruction == other.IsUnderConstruction
				&& HasWorkplace == other.HasWorkplace
				&& HasEntitySlider == other.HasEntitySlider
				&& Flippable == other.Flippable
				&& InGameplayScene == other.InGameplayScene
				&& HasStackedPanel == other.HasStackedPanel;
		}
	}

	// Picks which hints to show for the current context.
	//
	// Not one fixed hint list per context (an earlier version of this class was exactly that - a
	// per-context if-cascade each returning its own hardcoded array). That fell apart on the gamepad's
	// shoulders/triggers specifically: this mod and the base game between them give LB/RB five
	// different meanings depending on what's selected (RotateClockwise/RotateCounterclockwise while
	// placing, IncreaseBuildersPriority/DecreaseBuildersPriority under construction,
	// IncreaseWorkplacePriority/DecreaseWorkplacePriority on a finished workplace,
	// IncreaseFloodgateHeight/DecreaseFloodgateHeight for the generalized entity slider, IncreaseSpeed/
	// DecreaseSpeed with nothing selected) - hardcoding "the bottom-bar-open case shows X" would have
	// needed the same five-way shoulder logic re-derived and kept in sync inside every branch that can
	// coexist with a selection. Generalized instead: every hint rule below is independent and only
	// knows its own applicability + how specific/important it is; rules that turn out to point at the
	// same physical control (five different KeyBindingIds, but LB/RB either way) are recognised by
	// their shared resolved icon, and only the single most specific one for that control survives.
	internal static class GamepadHintResolver
	{
		private const string MoveLabel = "ControllerSupport.Hints.Move";
		private const string MoveFixedIcon = "leftStick_all";
		private const string ScrollLabel = "ControllerSupport.Hints.Scroll";
		private const string ScrollFixedIcon = "rightStick_all";
		private const string ZoomLabel = "ControllerSupport.Hints.Zoom";
		private const string CursorHeightLabel = "ControllerSupport.Hints.CursorHeight";

		// One rule = "if Applies(context), this hint is a candidate at this CollisionPriority, and
		// belongs in this DisplayOrder tier". Two or more applicable rules whose hints resolve to the
		// same physical control (ResolveIconKey) collapse down to whichever has the highest
		// CollisionPriority - see Resolve. Rules with a control nothing else ever shares (Confirm/
		// Cancel/Move/Scroll/Zoom's own buttons) never collide with anything, so CollisionPriority only
		// matters for tie-breaking there, not exclusion. DisplayOrder is a separate concern - it fixes
		// each control FAMILY's place in the rendered strip (face buttons, then stick/d-pad movement,
		// then triggers/shoulders) regardless of which specific rule within that family fired, so e.g.
		// Cancel always renders after Confirm even though the two are tuned independently against each
		// other's CollisionPriority per context (reported 2026-09-01: a bottom-bar tool row showed
		// "B/Move/A/Zoom/Speed" - Cancel's WithinOpenBottomBarSubSection rule outranks Confirm's bare
		// fallback in CollisionPriority for THAT context, which is correct for confirm/cancel resolving
		// independently, but had no business also flipping their left-to-right display order).
		private readonly struct Rule
		{
			public readonly Func<GamepadHintContext, bool> Applies;
			public readonly int DisplayOrder;
			public readonly int CollisionPriority;
			public readonly GamepadHint Hint;

			public Rule(Func<GamepadHintContext, bool> applies, int displayOrder, int collisionPriority, GamepadHint hint)
			{
				Applies = applies;
				DisplayOrder = displayOrder;
				CollisionPriority = collisionPriority;
				Hint = hint;
			}
		}

		// Fixed left-to-right tiers for the rendered strip: primary action buttons first, then
		// movement, then camera/world controls. Every rule in a family shares its tier's DisplayOrder -
		// only CollisionPriority varies within a family, to resolve which same-physical-control rule
		// wins (see Rule/Resolve above).
		private const int OrderConfirm = 0;
		private const int OrderCancel = 1;
		private const int OrderFlip = 2;
		private const int OrderMove = 3;
		private const int OrderScroll = 4;
		private const int OrderCursorHeight = 5;
		private const int OrderZoom = 6;
		private const int OrderShoulders = 7;

		// Ordered by DisplayOrder purely for readability - Resolve re-sorts explicitly rather than
		// relying on this order, so declaration order here is not load-bearing.
		private static readonly Rule[] Rules =
		{
			// --- Confirm (A) ---
			new Rule(c => c.HasDialogDefaultAction, OrderConfirm, 5, ForBinding("KeyBinding.Confirm", "Confirm")),
			new Rule(c => c.ToolEngaged && c.BuildingPlacementActive, OrderConfirm, 4, ForBinding("ControllerSupport.Hints.Place", "Confirm")),
			new Rule(c => c.ToolEngaged && c.AreaSelectionActive, OrderConfirm, 4, ForBinding("ControllerSupport.Hints.Mark", "Confirm")),
			// Any other engaged tool - gamepad select mode (GamepadSelectionController) and the zipline
			// connection tool both set GamepadPlacementState.ToolEngaged without being a
			// BlockObjectTool/area-selection tool, and both still use Confirm to pick whatever's under
			// the cursor. One generic rule rather than naming each tool, same reasoning as the Shoulders
			// rules below.
			new Rule(c => c.ToolEngaged && !c.BuildingPlacementActive && !c.AreaSelectionActive, OrderConfirm, 4,
				ForBinding("ControllerSupport.Hints.Select", "Confirm")),
			new Rule(c => c.DropdownOpen, OrderConfirm, 3, ForBinding("ControllerSupport.Hints.Select", "Confirm")),
			new Rule(c => c.BottomBarCategorySelected, OrderConfirm, 2, ForBinding("Tool.Cursor.Tooltip", "Confirm")),
			new Rule(c => c.EntityPanelOpen, OrderConfirm, 1, ForBinding("ControllerSupport.Hints.Select", "Confirm")),
			new Rule(_ => true, OrderConfirm, 0, ForBinding("ControllerSupport.Hints.Select", "Confirm")),

			// --- Cancel (B) - no bare-HUD fallback, and none for "on a bottom-bar category, row not
			// open" either; B does nothing useful in either case. ToolEngaged alone (not gated on
			// BuildingPlacementActive/AreaSelectionActive, unlike an earlier version of this rule) covers
			// every engaged tool uniformly, including select mode/zipline - GamepadSelectModeCancelGate
			// exists specifically so B can back out of select mode, and this hint was missing for exactly
			// that case (reported 2026-08-31: "while in select mode there is no Cancel"). The bottom-bar
			// rule below deliberately uses WithinOpenBottomBarSubSection, not BottomBarCategorySelected -
			// using the latter (as an earlier version of this rule did) showed Cancel for every category
			// button regardless of whether its row was actually open, since that flag only means
			// "this is a category button", not "a row is open" (reported 2026-08-31, "it's just when the
			// cursor is on the bottombar").
			new Rule(c => c.HasDialogDefaultAction, OrderCancel, 5, ForBinding("KeyBinding.Cancel", "Cancel")),
			// Any pushed panel, not just ones with a recognisable default confirm button -
			// PanelStack.ProcessInput's own Cancel handling backs out of any of them generically. Covers
			// submenus HasDialogDefaultAction can't (main menu's new-game creation screen, the load-game
			// save selector - reported 2026-08-31, "if I'm on a submenu... it doesn't show B back").
			new Rule(c => c.HasStackedPanel, OrderCancel, 4, ForBinding("Core.NavigationBack", "Cancel")),
			new Rule(c => c.ToolEngaged, OrderCancel, 4, ForBinding("KeyBinding.Cancel", "Cancel")),
			new Rule(c => c.DropdownOpen, OrderCancel, 3, ForBinding("Core.NavigationBack", "Cancel")),
			new Rule(c => c.WithinOpenBottomBarSubSection, OrderCancel, 2, ForBinding("KeyBinding.Cancel", "Cancel")),
			new Rule(c => c.EntityPanelOpen, OrderCancel, 1, ForBinding("EntityPanel.Close", "Cancel")),

			// --- Flip (Y) - placement only, and only for a building that actually has a flip
			// orientation (BlockObjectPlacementPanel itself hides the vanilla Flip button the same way -
			// most buildings aren't flippable) - separate button from Rotate, so no collision priority
			// needed against it ---
			new Rule(c => c.ToolEngaged && c.BuildingPlacementActive && c.Flippable, OrderFlip, 4, ForBinding("KeyBinding.Flip", "Flip")),

			// --- Move (stick/d-pad) - always relevant ---
			new Rule(_ => true, OrderMove, 0, Fixed(MoveLabel, MoveFixedIcon)),

			// --- Scroll (right stick) - only when there's something to scroll ---
			new Rule(c => c.ScrollableListPresent, OrderScroll, 0, Fixed(ScrollLabel, ScrollFixedIcon)),

			// --- Cursor height (d-pad up/down) - only meaningful while a world cursor is actually live
			// (placement/area-selection/select-mode/zipline all read CursorHeightUp/Down for this, see
			// GamepadReader/GamepadCursorLevels) - the d-pad's horizontal half and the left stick both
			// already drive Move above, this is specifically the vertical step.
			new Rule(c => c.ToolEngaged, OrderCursorHeight, 0, ForBinding(CursorHeightLabel, "CursorHeightUp")),

			// --- Zoom (triggers) - available whenever the player isn't stuck in a menu/dropdown, and
			// only in the game/map editor - the main menu has no camera to zoom ---
			new Rule(c => c.InGameplayScene && !c.HasDialogDefaultAction && !c.DropdownOpen, OrderZoom, 0,
				ForBinding(ZoomLabel, "ZoomIn")),

			// --- Shoulders (LB/RB) - five meanings sharing one physical control, most specific wins.
			// Mirrors the real precedence FloodgateFragmentUnderConstructionPatch/
			// ConstructionSiteFragmentFinishedPriorityPatch/GamepadEntitySliderController already
			// establish for the actual input (construction beats workplace/slider beats nothing
			// selected) - Rotate is highest because it only ever applies while genuinely placing a
			// building, a state that can't coexist with any of the other four.
			new Rule(c => c.ToolEngaged && c.BuildingPlacementActive, OrderShoulders, 4, ForBinding("ControllerSupport.Hints.Rotate", "RotateClockwise")),
			new Rule(c => c.EntityPanelOpen && c.IsUnderConstruction, OrderShoulders, 3, ForBinding("ControllerSupport.Hints.Priority", "IncreaseBuildersPriority")),
			new Rule(c => c.EntityPanelOpen && c.HasWorkplace, OrderShoulders, 2, ForBinding("ControllerSupport.Hints.Priority", "IncreaseWorkplacePriority")),
			new Rule(c => c.EntityPanelOpen && c.HasEntitySlider, OrderShoulders, 2, ForBinding("ControllerSupport.Hints.Adjust", "IncreaseFloodgateHeight")),
			// Fallback only in the game/map editor - the main menu has no game speed to adjust, and
			// without this gate this rule (being an unconditional _ => true) would show a meaningless
			// Speed hint there.
			new Rule(c => c.InGameplayScene, OrderShoulders, 0, ForBinding("ControllerSupport.Hints.Speed", "IncreaseSpeed")),
		};

		public static IReadOnlyList<GamepadHint> Resolve(GamepadHintContext context, KeyBindingRegistry keyBindingRegistry)
		{
			var candidates = Rules
				.Where(rule => rule.Applies(context))
				.Select(rule => (rule.DisplayOrder, rule.CollisionPriority, rule.Hint,
					IconKey: rule.Hint.ResolveIconKey(keyBindingRegistry)))
				.Where(candidate => candidate.IconKey != null);

			// Within each physical control (icon key), keep only the most specific applicable rule; the
			// final left-to-right order then comes purely from each surviving rule's DisplayOrder tier,
			// not from CollisionPriority (which only decided the collision above).
			return candidates
				.GroupBy(candidate => candidate.IconKey)
				.Select(group => group.OrderByDescending(candidate => candidate.CollisionPriority).First())
				.OrderBy(candidate => candidate.DisplayOrder)
				.Select(candidate => candidate.Hint)
				.ToList();
		}

		private static GamepadHint ForBinding(string labelLocKey, string keyBindingId)
		{
			return GamepadHint.ForBinding(labelLocKey, keyBindingId);
		}

		private static GamepadHint Fixed(string labelLocKey, string fixedIconKey)
		{
			return GamepadHint.Fixed(labelLocKey, fixedIconKey);
		}
	}
}
