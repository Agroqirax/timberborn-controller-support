using System;
using System.Collections.Generic;
using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.SingletonSystem;
using Timberborn.ToolSystem;
using Timberborn.UISound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	internal class GamepadNavigationInputProcessor : ILoadableSingleton, IUnloadableSingleton, IInputProcessor,
		ILateUpdatableSingleton
	{
		private const float FailureLogInterval = 30f;
		private const int SubSectionSettleFrames = 8;
		private const int InitialSelectionFrames = 60;
		private const float ScrollSpeed = 1200f;
		private const float MaxFrameTime = 0.2f;

		private readonly InputService _inputService;
		private readonly InputBlocker _inputBlocker;
		private readonly PanelTracker _panelTracker;
		private readonly DropdownTracker _dropdownTracker;
		private readonly UISoundController _uiSoundController;
		private readonly EventBus _eventBus;
		private readonly KeyBindingRegistry _keyBindingRegistry;

		private readonly GamepadReader _reader = new GamepadReader();
		private readonly SelectionHighlighter _highlighter = new SelectionHighlighter();
		private readonly SelectionMemory _memory = new SelectionMemory();
		private readonly List<VisualElement> _candidates = new List<VisualElement>();
		private readonly List<ScrollView> _scrollViewBuffer = new List<ScrollView>();

		private VisualElement _scope;
		private VisualElement _selected;
		private Vector2 _lastSelectionCentre;
		private Vector2 _highlightCentre;
		private VisualElement _pendingSubSection;
		private VisualElement _pendingSubSectionOwner;
		private int _pendingSubSectionFrames;
		private VisualElement _activeToolGroupRow;
		private VisualElement _activeToolGroupOwner;
		private int _initialSelectionFrames;
		private bool _pendingEntityPanelFocus;
		private float _nextFailureLogTime;

		public GamepadNavigationInputProcessor(InputService inputService, InputBlocker inputBlocker,
			PanelTracker panelTracker, DropdownTracker dropdownTracker, UISoundController uiSoundController,
			EventBus eventBus, KeyBindingRegistry keyBindingRegistry)
		{
			_inputService = inputService;
			_inputBlocker = inputBlocker;
			_panelTracker = panelTracker;
			_dropdownTracker = dropdownTracker;
			_uiSoundController = uiSoundController;
			_eventBus = eventBus;
			_keyBindingRegistry = keyBindingRegistry;
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
			_panelTracker.PanelChanged += OnPanelChanged;
			_eventBus.Register(this);
		}

		public void Unload()
		{
			_eventBus.Unregister(this);
			_panelTracker.PanelChanged -= OnPanelChanged;
			ClearSelection();
			_inputService.RemoveInputProcessor(this);
		}

		// CursorTool (and any other tool) re-registers itself to the front of InputService's regular
		// chain on every tool switch (Enter()/Exit()), same pattern PanelStack uses for panels - and
		// exactly as unrelated to PanelShownEvent/PanelHiddenEvent as it sounds, so the re-registration
		// above never sees it. Without this, a tool switch (including CursorTool re-entering itself,
		// which ToolService always force-does even when it was already active - see
		// GamepadSelectionController's own notes on that) can silently put a native tool processor ahead
		// of this one. ToolService.SwitchToolInternal calls tool.Enter() (which is where CursorTool adds
		// itself) synchronously before posting ToolEnteredEvent - the same "register, then post" order
		// PanelStack.Show() uses - so re-registering from this handler lands after it for the same
		// deterministic reason OnPanelChanged already relies on.
		[OnEvent]
		public void OnToolEntered(ToolEnteredEvent toolEnteredEvent)
		{
			_inputService.RemoveInputProcessor(this);
			_inputService.AddInputProcessor(this);
		}

		// InputService walks its processors last-registered-first, and PanelStack re-registers itself
		// every time a panel is shown. Worse, its ProcessInput returns TopPanel.IsOverlay, so while an
		// overlay or dialog is up it swallows input for everything registered before it - which is
		// every dialog in the game. Re-registering here puts us back in front of it, and it has to
		// happen on the event: if we let PanelStack swallow our input first we would never run again
		// to notice we had been buried.
		private void OnPanelChanged()
		{
			try
			{
				// This fires synchronously off PanelStack's own show/hide event, ahead of the next
				// ProcessInputCore - which is also where EnterScope normally banks the outgoing scope's
				// selection before moving on. Wiping _scope below without remembering first meant
				// EnterScope always found it already null and never had anything to save: closing a
				// panel would put the player back in the scope behind it with no memory of where they
				// had been in it.
				if (_scope != null && _selected != null)
				{
					_memory.Remember(_scope, _selected, _lastSelectionCentre);
				}

				ClearSelection();
				_pendingSubSection = null;
				_pendingSubSectionOwner = null;
				_activeToolGroupRow = null;
				_activeToolGroupOwner = null;
				_initialSelectionFrames = 0;
				_candidates.Clear();
				_scope = null;
				_reader.Reset();
			}
			finally
			{
				// PanelStack.Show() calls its own AddInputProcessor(self) before posting the very
				// PanelShownEvent this handler answers, so re-registering here always lands us after
				// it in InputService's list - and since CallInputProcessors walks last-added-first,
				// that is what guarantees this processor is asked before PanelStack every time a panel
				// is open. That guarantee is why it is safe for Confirm()/the TextField-blur check to
				// win over PanelStack's own native Confirm/Cancel handling instead of racing it. If
				// anything above throws, skipping this would silently give that position up - so it
				// has to run unconditionally, not just on the happy path.
				_inputService.RemoveInputProcessor(this);
				_inputService.AddInputProcessor(this);
			}
		}

		public bool ProcessInput()
		{
			return SafeProcessInputCore();
		}

		// Read-only, for GamepadHintStripController: the strip needs to know what the player is
		// currently aimed at (for BottomBarNavigation.SubSectionFor) and whether a list is scrollable
		// (below) to pick the right hints, without duplicating this processor's own selection/scroll
		// state or its delicate event-ordering logic.
		internal VisualElement Selected => _selected;

		internal bool HasScrollableList => FindScrollView() != null;

		// For GamepadSelectionController's FocusEntityPanelOnDeselect setting: puts the selection ring
		// on the entity panel the instant select mode exits, rather than leaving it wherever the bottom
		// bar's Select button sits. Deferred rather than acted on immediately - GamepadSelectionController
		// calls this from Disengage(), while GamepadPlacementState.ToolEngaged (still true from the frame
		// just ending) has _scope wiped to null every frame this processor runs while select mode is
		// engaged (see the ToolEngaged branch in ProcessInputCore), and the very same B press that exits
		// select mode also short-circuits this frame's own ProcessInputCore early via
		// GamepadSelectModeCancelGate, before scope would even be re-established. Recording the request
		// and applying it once EnterScope has actually run - this frame if nothing gated it, the next
		// otherwise - is what makes it survive both.
		internal void RequestFocusEntityPanel()
		{
			_pendingEntityPanelFocus = true;
		}

		// The entity panel is a permanent, name-tagged sibling in the bare HUD's own tree
		// (Timberborn.EntityPanelSystem.EntityPanel loads it once and only ever toggles its display
		// style - see EntityPanel.Load/Show/Hide), so it is always reachable by name off the current
		// scope; NavigationCandidates.IsDisplayed is what actually tells "open" from "hidden" apart, the
		// same check RefreshCandidates/Collect already prune on. Silently does nothing when the panel
		// isn't showing (nothing was selected, or a dialog is stacked over the HUD and _scope is that
		// dialog instead) rather than searching elsewhere for it - and always consumes the request either
		// way, so a panel that never reappears doesn't leave this retrying forever.
		private void ApplyPendingEntityPanelFocus()
		{
			if (!_pendingEntityPanelFocus)
			{
				return;
			}

			_pendingEntityPanelFocus = false;

			var entityPanel = _scope?.Q<VisualElement>("EntityPanel");
			if (entityPanel == null || !NavigationCandidates.IsDisplayed(entityPanel))
			{
				return;
			}

			NavigationCandidates.Collect(entityPanel, _candidates);

			// Prefer the Focus/follow button (Timberborn.EntityPanelSystem.FollowObjectFragment, name
			// "FollowObjectFragment") over SpatialNavigator.First's plain top-left rule - LeftButtons
			// holds nothing but DeleteBuildingFragment for a regular building, so "top-left" landed on
			// Delete every time, which is a far worse thing to have armed under A than a camera recentre.
			// Falls back to top-left for panels that don't have a Focus button at all (e.g. non-followable
			// entities), rather than selecting nothing.
			var target = _candidates.Find(c => c.name == "FollowObjectFragment" && NavigationCandidates.IsDisplayed(c))
				?? SpatialNavigator.First(_candidates);
			if (target == null)
			{
				return;
			}

			Select(target);
			ScrollIntoView(target);
		}

		// Focusing a TextField blocks the whole regular input-processor chain - see the guard at the
		// top of ProcessInputCore for why - so this is the only way B still reaches the game while the
		// player is editing a name. LateUpdate runs after InputService.UpdateSingleton has decided
		// whether it is blocked for the frame, so the two paths never both fire the same frame.
		public void LateUpdateSingleton()
		{
			if (_inputBlocker.IsBlocked)
			{
				SafeProcessInputCore();
			}
		}

		// A mod must never take the game down with it: this runs every frame and reaches into UI
		// Toolkit internals that can throw when the tree is mid-rebuild.
		private bool SafeProcessInputCore()
		{
			try
			{
				return ProcessInputCore();
			}
			catch (Exception e)
			{
				ReportFailure(e);
				return false;
			}
		}

		private bool ProcessInputCore()
		{
			// Flushes whatever MouseEnterEvent dispatch last frame's Apply() queued, before anything
			// below can queue a new one this frame - see SelectionHighlighter.Tick for why the gap
			// matters (letting Tooltip's own show/hide bookkeeping see a real frame in between).
			_highlighter.Tick();

			var gamepad = Gamepad.current;
			if (gamepad == null)
			{
				return false;
			}

			// TextElementInitializer blocks all normal input processing for as long as a TextField has
			// real UI Toolkit focus (see LateUpdateSingleton above for how this method still runs at
			// all during that time). Typing itself goes through the keyboard/on-screen keyboard as
			// normal - the only thing missing without this is a way back out, since B can't reach
			// anything else while blocked either. Confirming or cancelling the dialog stays on its own
			// buttons rather than teaching it to fire from the field: SetConfirmCancelActions treats a
			// blur as a no-op unless InputService's real Confirm/Cancel key was actually pressed, so a
			// plain Blur() here is always safe and never doubles as an accidental commit.
			if (_selected is TextField focusedField && IsFocused(focusedField))
			{
				if (_inputService.UICancel)
				{
					focusedField.Blur();
				}

				return false;
			}

			// GamepadSelectionController set this the instant it exited the gamepad cursor submode on
			// this same B press, staying one step ahead of CursorTool's own native Cancel handling
			// (guaranteed by the OnToolEntered re-registration above). Consuming it here - unconditionally,
			// before anything else this frame - is what makes closing the entity panel need a genuinely
			// separate press instead of happening in the same frame as the mode exit.
			if (GamepadSelectModeCancelGate.ConsumeNextCancel)
			{
				GamepadSelectModeCancelGate.ConsumeNextCancel = false;
				return true;
			}

			// While a building is being placed, the stick and d-pad move the ghost instead of the UI -
			// there is nothing to navigate to that matters more than where the building is going. The
			// bare HUD would otherwise still count as a scope below and keep stealing the stick to walk
			// the bottom bar out from under the player's thumb.
			//
			// Banking the selection into memory first, exactly like EnterScope does for a real scope
			// change, is what lets B during placement land back on the specific building button rather
			// than falling through to BottomBarNavigation.DefaultTool - without it there is nothing
			// recorded for this scope to restore once the tool group row reappears. Clearing _scope too
			// means the frame placement ends this re-enters fresh via EnterScope below rather than
			// sitting with nothing highlighted until the next push.
			if (GamepadPlacementState.ToolEngaged)
			{
				if (_scope != null && _selected != null)
				{
					_memory.Remember(_scope, _selected, _lastSelectionCentre);
				}

				ClearSelection();
				_scope = null;
				return false;
			}

			// An open dropdown list owns navigation outright: it lives in its own UIDocument root,
			// and nothing behind it should be reachable until it closes.
			var scope = _dropdownTracker.Scope ?? _panelTracker.TopElement;
			if (scope == null)
			{
				ClearSelection();
				return false;
			}

			if (!ReferenceEquals(scope, _scope))
			{
				EnterScope(scope);
			}

			ApplyPendingEntityPanelFocus();
			RefreshHighlightIfMoved();
			TryRecoverFromClosedToolGroup();
			TryInitialSelection();
			TrySubSectionJump();

			// Unlike the left stick below, the right stick very much has something behind it to
			// protect: KeyboardCameraController reads it for pan/rotate every frame it gets to run.
			// Reporting a successful scroll as handled is what stops that same frame - the same trick
			// GamepadEntitySliderController's shoulders already use to steal input from the game's own
			// zoom/priority use whenever they have a value to adjust instead. It only ever fires when
			// Scroll() actually found a genuinely overflowing list to move, so the ordinary case -
			// looking around with nothing scrollable on screen - leaves the camera untouched.
			var handled = Scroll();

			var direction = _reader.ReadMove(_keyBindingRegistry);
			if (direction != Vector2Int.zero)
			{
				Move(direction);
			}

			// Deliberately not reported as handled. CallInputProcessors stops at the first processor
			// returning true, and this one re-registers itself to the front of the queue - so claiming
			// a held stick would freeze every processor behind it for as long as it is held, including
			// camera panning and the game's own WASD camera. Nothing else in the chain reads the left
			// stick, so there is nothing to protect it from anyway. A button press is momentary and
			// costs at most a frame, so those still report honestly.

			if (_inputService.UIConfirm)
			{
				handled |= Confirm();
			}

			return handled;
		}

		// Leaving a scope banks where the selection was; entering one puts it back. This is what
		// makes choosing a dropdown value return the player to that dropdown rather than to the top
		// of the page, and it does the same favour when backing out of a submenu.
		private void EnterScope(VisualElement scope)
		{
			if (_scope != null && _selected != null)
			{
				_memory.Remember(_scope, _selected, _lastSelectionCentre);
			}

			ClearSelection();
			_pendingSubSection = null;
			_pendingSubSectionOwner = null;
			_activeToolGroupRow = null;
			_activeToolGroupOwner = null;
			_scope = scope;

			// The new scope may not have been laid out yet, in which case there are no candidates to
			// restore onto and the first push simply starts from the top - an acceptable miss.
			NavigationCandidates.Collect(_scope, _candidates);

			var restored = _memory.Restore(_scope, _candidates) ?? ResolveBottomBarOrDialogDefault();

			// With no history to go on: the bare HUD starts on the cursor tool rather than whatever
			// happens to sit top-left, since that is the tool the player almost always wants next. A
			// dialog starts on whatever it names "ConfirmButton" - the same element keyboard Enter
			// would trigger - so confirming a warning or a science-point unlock prompt needs nothing
			// more than a tap of A. Everywhere else, start at the top of the panel. Waiting for the
			// player to push the stick first meant arriving in a scene with nothing highlighted while A
			// would still press whatever the panel considers its default - the cursor was there, just
			// invisible.
			//
			// On the bare HUD (no stacked panel) SpatialNavigator.First is deliberately not tried yet
			// even when it would find something: MapEditor's Top-left/Top-right HUD pieces (FilePanel,
			// the simulation-speed panel, the weather panel) can finish loading and laying out a frame
			// or two before BottomBarPanel does, so grabbing whatever is available immediately locks
			// onto FilePanel's SaveButton - there is no MainSection among the candidates *yet*, not
			// because this scope will never have one. TryInitialSelection's retry loop exists exactly
			// for this "not laid out yet" gap; a stacked dialog/menu has no such race (it will never
			// grow a MainSection of its own), so it still resolves immediately below.
			if (restored == null && _panelTracker.HasStackedPanel)
			{
				restored = SpatialNavigator.First(_candidates);
			}

			if (restored != null)
			{
				Select(restored);
				ScrollIntoView(restored);
				return;
			}

			// Nothing to land on yet because the panel has not been laid out. Keep looking for a while.
			_initialSelectionFrames = InitialSelectionFrames;
		}

		private VisualElement ResolveBottomBarOrDialogDefault()
		{
			return BottomBarNavigation.DefaultTool(_candidates) ?? DialogDefaultAction.Find(_candidates);
		}

		private void Move(Vector2Int direction)
		{
			RefreshCandidates();
			if (_candidates.Count == 0)
			{
				ClearSelection();
				return;
			}

			// A push can land on a control that would rather absorb it than be left, e.g. sideways on a
			// slider changes its value. Re-applying the highlight afterwards matters because adjusting
			// moves the dragger, which changes what sits under the control's centre.
			if (_selected != null && ControlActivator.TryAdjust(_selected, direction))
			{
				_highlighter.Apply(_selected);
				return;
			}

			var next = SpatialNavigator.Next(_candidates, _selected, direction)
				?? BottomBarNavigation.WrapHorizontal(_candidates, _selected, direction);
			if (next != null)
			{
				Select(next);
				ScrollIntoView(next);
			}
		}

		// A panel is often one or two frames away from having a usable layout when it is first shown, so
		// the opening selection cannot always be made on the spot. Keep trying briefly, and stop the
		// moment there is a selection - including one the player made themselves in the meantime.
		private void TryInitialSelection()
		{
			if (_initialSelectionFrames <= 0)
			{
				return;
			}

			if (_selected != null)
			{
				_initialSelectionFrames = 0;
				return;
			}

			_initialSelectionFrames--;
			RefreshCandidates();

			// Same reasoning as EnterScope: don't settle for SpatialNavigator.First on the bare HUD
			// while there is still time left to wait for the bottom bar to finish mounting, or a
			// scene where it mounts later than its other HUD pieces (MapEditor's FilePanel/simulation
			// panel) permanently locks onto one of those instead of the cursor tool.
			var first = ResolveBottomBarOrDialogDefault();
			if (first == null && (_panelTracker.HasStackedPanel || _initialSelectionFrames <= 0))
			{
				first = SpatialNavigator.First(_candidates);
			}

			if (first == null)
			{
				return;
			}

			_initialSelectionFrames = 0;
			Select(first);
			ScrollIntoView(first);
		}

		// The right stick scrolls whichever list on screen actually has more content than it can show.
		// Claiming it away from KeyboardCameraController this way, only when there is genuinely
		// something to scroll, is what makes an entity panel's needs list reachable at all - the game
		// gives a gamepad no other input for it, on a par with GamepadEntitySliderController's shoulders
		// claiming input the game's own zoom/priority use would otherwise get whenever they have a
		// value to adjust instead. Deliberately does not move the selection - this is the player looking
		// around the list, and yanking the cursor with the viewport would make it impossible to just
		// read something.
		//
		// Returns whether a list was actually scrolled, so the caller can report the frame as handled
		// and stop the camera from also reading the same stick push.
		private bool Scroll()
		{
			var stick = CameraKeyBindingAxes.ReadSecondaryAxes(_keyBindingRegistry, CameraKeyBindingAxes.Move);

			// Matches CameraMovementAnalogPatch's own "magnitude <= 0f" threshold exactly, rather than
			// the larger ScrollDeadzone this used to compare against. Camera reads the same raw axis
			// (already deadzone-processed by the Input System's own stick handling) with no additional
			// threshold of its own, so a bigger deadzone here only opened a window - roughly Unity's
			// device deadzone up to this one - where a push registered for camera but not yet for us,
			// letting the camera visibly twitch for a frame or two before scrolling took over.
			if (stick.magnitude <= 0f)
			{
				return false;
			}

			var scrollView = FindScrollView();
			if (scrollView == null)
			{
				return false;
			}

			// Capped frame time for the same reason the camera caps it: one long hitch should not fling
			// the list to the far end.
			var step = ScrollSpeed * Mathf.Min(Time.unscaledDeltaTime, MaxFrameTime);
			var offset = scrollView.scrollOffset;
			offset.x += stick.x * step;
			offset.y -= stick.y * step;

			// The setter runs both axes through their scrollers, which clamp, so no range check is needed.
			scrollView.scrollOffset = offset;
			return true;
		}

		// The list the selection is actually sitting in - an open dropdown *is* a ScrollView - so long
		// as it actually needs scrolling; otherwise (or with nothing selected inside any list) whichever
		// genuinely overflowing list in the scope sits nearest the selection. That fallback is what makes
		// the right stick scroll the correct one of two side-by-side lists - the mismatched-mods save
		// dialog's active-mods/saved-mods columns - depending on which button below them is selected,
		// rather than always the first ScrollView found in tree order (always the left one).
		private ScrollView FindScrollView()
		{
			var direct = FindDirectScrollView();
			return IsScrollable(direct) ? direct : NearestScrollableView();
		}

		private ScrollView FindDirectScrollView()
		{
			if (_selected is BaseVerticalCollectionView collectionView)
			{
				return collectionView.Q<ScrollView>();
			}

			for (var current = _selected?.hierarchy.parent; current != null; current = current.hierarchy.parent)
			{
				if (current is ScrollView scrollView)
				{
					return scrollView;
				}
			}

			return _scope as ScrollView;
		}

		private ScrollView NearestScrollableView()
		{
			if (_scope == null)
			{
				return null;
			}

			_scope.Query<ScrollView>().ToList(_scrollViewBuffer);

			ScrollView best = null;
			var bestDistance = float.MaxValue;
			var point = _selected?.worldBound.center ?? _scope.worldBound.center;

			foreach (var candidate in _scrollViewBuffer)
			{
				// Query<T> walks every live descendant regardless of display style - unlike
				// NavigationCandidates' own top-down walk, which prunes a hidden subtree before ever
				// reaching what is inside it. A panel that hides part of itself rather than tearing it
				// down (the pause menu's own layered panels, popped back through on the way to
				// resuming) can leave a still-attached, still-"scrollable" list sitting behind a
				// display:none ancestor - which would otherwise claim the stick forever, since nothing
				// ever makes it visible again to fail the IsScrollable check honestly.
				if (!IsScrollable(candidate) || !IsDisplayedInScope(candidate))
				{
					continue;
				}

				var distance = (candidate.worldBound.center - point).sqrMagnitude;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = candidate;
				}
			}

			return best;
		}

		// Walks up from candidate to (and including) _scope, requiring every level along the way to
		// pass NavigationCandidates.IsDisplayed - the same per-element check its own pruned walk applies
		// while descending, just applied bottom-up here since Query<T> already did the descending.
		private bool IsDisplayedInScope(VisualElement candidate)
		{
			for (var current = candidate; current != null; current = current.hierarchy.parent)
			{
				if (!NavigationCandidates.IsDisplayed(current))
				{
					return false;
				}

				if (ReferenceEquals(current, _scope))
				{
					return true;
				}
			}

			return false;
		}

		// scrollableWidth/scrollableHeight are internal, so this compares the content container's own
		// layout against the clipped viewport's instead - close enough for "is there anything to scroll
		// to" without reflecting into UI Toolkit internals for it.
		private static bool IsScrollable(ScrollView scrollView)
		{
			if (scrollView?.contentContainer == null || scrollView.contentViewport == null)
			{
				return false;
			}

			var content = scrollView.contentContainer.layout;
			var viewport = scrollView.contentViewport.layout;
			return content.height > viewport.height + 1f || content.width > viewport.width + 1f;
		}

		// Confirming a bottom bar category opens its row of tools above the bar without moving the
		// selection, which leaves the player pointing at the category they just chose. Take them to the
		// first tool in the row instead - the leftmost, since that is where the common ones live.
		//
		// Spread over several frames because the row is only shown by the click handler, and neither its
		// display style nor its layout has settled by the time this frame ends. Giving up quietly is the
		// right answer when nothing appears: pressing the open category again closes it, and the player
		// should stay where they are.
		private void TrySubSectionJump()
		{
			if (_pendingSubSection == null)
			{
				return;
			}

			if (_pendingSubSectionFrames-- <= 0)
			{
				_pendingSubSection = null;
				_pendingSubSectionOwner = null;
				return;
			}

			RefreshCandidates();

			var leftmost = BottomBarNavigation.Leftmost(_candidates, _pendingSubSection);
			if (leftmost == null)
			{
				return;
			}

			// _pendingSubSection is the bottom bar's one shared row container - every category's tools
			// live in it, only the open category's showing. What TryRecoverFromClosedToolGroup needs to
			// watch is this category's own slice of it, since that is the part the game actually toggles
			// off - so pin down the direct child of the shared container that owns the row we just
			// landed in, not the shared container itself, which never itself goes display:none.
			_activeToolGroupRow = ChildOf(_pendingSubSection, leftmost);
			_activeToolGroupOwner = _pendingSubSectionOwner;

			_pendingSubSection = null;
			_pendingSubSectionOwner = null;
			Select(leftmost);
			ScrollIntoView(leftmost);
		}

		// Confirming a category button leaves its row selected. If the row then closes for any reason
		// other than the player picking a different category - B, placing a building, switching tools
		// by keyboard - nothing else notices: the row's ToggleDisplayStyle(false) is the game's own
		// business, not an event this mod observes. Left alone, the next confirm press would activate a
		// button that is no longer part of the tree the player can see, and Move would only notice once
		// the stick was pushed. Checking every frame is cheap here - a couple of field reads, not the
		// candidate walk RefreshCandidates does - unlike making RefreshCandidates itself run unconditionally.
		private void TryRecoverFromClosedToolGroup()
		{
			if (_activeToolGroupRow == null)
			{
				return;
			}

			if (_activeToolGroupRow.resolvedStyle.display != DisplayStyle.None)
			{
				return;
			}

			if (_selected != null && _activeToolGroupOwner != null && _activeToolGroupOwner.panel != null
				&& IsDescendantOf(_selected, _activeToolGroupRow))
			{
				Select(_activeToolGroupOwner);
				ScrollIntoView(_activeToolGroupOwner);
			}

			_activeToolGroupRow = null;
			_activeToolGroupOwner = null;
		}

		private static VisualElement ChildOf(VisualElement ancestor, VisualElement descendant)
		{
			for (var current = descendant; current != null; current = current.hierarchy.parent)
			{
				if (current.hierarchy.parent == ancestor)
				{
					return current;
				}
			}

			return null;
		}

		private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
		{
			for (var current = element; current != null; current = current.hierarchy.parent)
			{
				if (ReferenceEquals(current, ancestor))
				{
					return true;
				}
			}

			return false;
		}

		// Rebuilt on every step rather than cached. A step happens at most a handful of times a
		// second and the walk is linear, whereas a cache would have to guess when a panel rebuilt its
		// children - which is precisely the guess that used to strand the selection on a dead element.
		private void RefreshCandidates()
		{
			NavigationCandidates.Collect(_scope, _candidates);

			if (_selected == null)
			{
				return;
			}

			if (_candidates.Contains(_selected))
			{
				_lastSelectionCentre = _selected.worldBound.center;
				return;
			}

			// The panel rebuilt underneath us. Take the selection to whatever now occupies that spot.
			_selected = SpatialNavigator.NearestTo(_candidates, _lastSelectionCentre);
			if (_selected != null)
			{
				_highlightCentre = _selected.worldBound.center;
			}

			_highlighter.Apply(_selected);
		}

		private bool Confirm()
		{
			if (_selected == null || _selected.panel == null)
			{
				// Nothing aimed at, so fall back to the panel's own default action - the same thing
				// Enter does.
				var controller = _panelTracker.TopController;
				if (controller != null && controller.OnUIConfirmed())
				{
					_uiSoundController.PlayClickSound();
					return true;
				}

				return false;
			}

			// Read before activating: the row this category owns is about to appear, but the category
			// button itself is where the selection would otherwise be stranded.
			var subSection = BottomBarNavigation.SubSectionFor(_selected);

			ControlActivator.Activate(_selected);
			_uiSoundController.PlayClickSound();

			if (subSection != null)
			{
				_pendingSubSection = subSection;
				_pendingSubSectionOwner = _selected;
				_pendingSubSectionFrames = SubSectionSettleFrames;
			}

			return true;
		}

		// Long panels live inside a ScrollView, so the selection has to drag the viewport with it.
		// ScrollTo throws unless the element is genuinely inside that ScrollView's content-container,
		// so check before asking, and keep walking outwards - with nested ScrollViews the nearest one
		// is not always the right one.
		private static void ScrollIntoView(VisualElement element)
		{
			for (var current = element.hierarchy.parent; current != null; current = current.hierarchy.parent)
			{
				if (current is ScrollView scrollView
					&& scrollView.contentContainer != null
					&& scrollView.contentContainer.Contains(element))
				{
					scrollView.ScrollTo(element);
					return;
				}
			}
		}

		// The highlight is built from what a pointer at the control's centre would hit, so it can only
		// be worked out once the control is actually on screen. Selecting something below the fold
		// highlights it *before* ScrollIntoView has brought it up - at which point the pick lands
		// outside the clipped viewport and comes back empty, leaving the control looking unselected.
		// Rather than guess at how many frames the scroll needs, re-apply whenever the selection has
		// moved. That self-corrects for deferred scrolls and reflows alike, and settles on its own.
		private void RefreshHighlightIfMoved()
		{
			if (_selected == null)
			{
				return;
			}

			// A UI change can take the selected element out of play without the player ever pushing the
			// stick - a demolished building's entity-panel row, a priority marker toggled off, a bottom
			// bar row closed by something other than confirm. Checking _selected.panel == null alone
			// missed most of this: NavigationCandidates also drops anything hidden (display:none,
			// invisible, zero-size, disabled) while it is still attached to the panel, which is the more
			// common way a UI change removes an element - so the ring kept drawing around a collapsed
			// element instead of vanishing, a small leftover amber square with nothing behind it.
			// RefreshCandidates already knows how to recover (it's the same "panel rebuilt underneath us"
			// case Move hits, driven by candidate-list membership rather than panel nullity), so just run
			// it every frame instead of only from Move - panels here are small enough that the walk is
			// cheap, and it is the only check that actually matches what NavigationCandidates considers
			// gone.
			RefreshCandidates();
			if (_selected == null)
			{
				return;
			}

			var centre = _selected.worldBound.center;
			if ((centre - _highlightCentre).sqrMagnitude < 1f)
			{
				return;
			}

			_highlightCentre = centre;
			_highlighter.Apply(_selected);
		}

		private void Select(VisualElement element)
		{
			if (ReferenceEquals(_selected, element))
			{
				return;
			}

			_selected = element;
			_lastSelectionCentre = element.worldBound.center;
			_highlightCentre = _lastSelectionCentre;
			_highlighter.Apply(element);
		}

		private void ClearSelection()
		{
			_highlighter.Clear();
			_selected = null;
		}

		private static bool IsFocused(VisualElement element)
		{
			return element.panel != null && element.panel.focusController.focusedElement == element;
		}

		// Throttled rather than latched. The old build silenced itself permanently after one failure,
		// so a transient error during a panel rebuild left the mod misbehaving in total silence for
		// the rest of the session.
		private void ReportFailure(Exception e)
		{
			ClearSelection();

			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] Input processing failed: {e}");
		}
	}
}
