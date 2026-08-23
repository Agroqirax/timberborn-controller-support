using System;
using System.Collections.Generic;
using Timberborn.InputSystem;
using Timberborn.SingletonSystem;
using Timberborn.UISound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	internal class GamepadNavigationInputProcessor : ILoadableSingleton, IUnloadableSingleton, IInputProcessor
	{
		private const float FailureLogInterval = 30f;
		private const int SubSectionSettleFrames = 8;
		private const int InitialSelectionFrames = 60;
		private const float ScrollDeadzone = 0.2f;
		private const float ScrollSpeed = 1200f;
		private const float MaxFrameTime = 0.2f;

		private readonly InputService _inputService;
		private readonly PanelTracker _panelTracker;
		private readonly DropdownTracker _dropdownTracker;
		private readonly UISoundController _uiSoundController;

		private readonly GamepadReader _reader = new GamepadReader();
		private readonly SelectionHighlighter _highlighter = new SelectionHighlighter();
		private readonly SelectionMemory _memory = new SelectionMemory();
		private readonly List<VisualElement> _candidates = new List<VisualElement>();

		private VisualElement _scope;
		private VisualElement _selected;
		private Vector2 _lastSelectionCentre;
		private Vector2 _highlightCentre;
		private VisualElement _pendingSubSection;
		private int _pendingSubSectionFrames;
		private int _initialSelectionFrames;
		private float _nextFailureLogTime;

		public GamepadNavigationInputProcessor(InputService inputService, PanelTracker panelTracker,
			DropdownTracker dropdownTracker, UISoundController uiSoundController)
		{
			_inputService = inputService;
			_panelTracker = panelTracker;
			_dropdownTracker = dropdownTracker;
			_uiSoundController = uiSoundController;
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
			_panelTracker.PanelChanged += OnPanelChanged;
		}

		public void Unload()
		{
			_panelTracker.PanelChanged -= OnPanelChanged;
			ClearSelection();
			_inputService.RemoveInputProcessor(this);
		}

		// InputService walks its processors last-registered-first, and PanelStack re-registers itself
		// every time a panel is shown. Worse, its ProcessInput returns TopPanel.IsOverlay, so while an
		// overlay or dialog is up it swallows input for everything registered before it - which is
		// every dialog in the game. Re-registering here puts us back in front of it, and it has to
		// happen on the event: if we let PanelStack swallow our input first we would never run again
		// to notice we had been buried.
		private void OnPanelChanged()
		{
			ClearSelection();
			_pendingSubSection = null;
			_initialSelectionFrames = 0;
			_candidates.Clear();
			_scope = null;
			_reader.Reset();

			_inputService.RemoveInputProcessor(this);
			_inputService.AddInputProcessor(this);
		}

		public bool ProcessInput()
		{
			// A mod must never take the game down with it: this runs every frame and reaches into UI
			// Toolkit internals that can throw when the tree is mid-rebuild.
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
			var gamepad = Gamepad.current;
			if (gamepad == null)
			{
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

			RefreshHighlightIfMoved();
			TryInitialSelection();
			TrySubSectionJump();
			Scroll(gamepad);

			var direction = _reader.ReadMove(gamepad);
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
			var handled = false;

			if (gamepad.buttonSouth.wasPressedThisFrame)
			{
				handled |= Confirm();
			}

			if (gamepad.buttonEast.wasPressedThisFrame)
			{
				handled |= Cancel();
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
			_scope = scope;

			// The new scope may not have been laid out yet, in which case there are no candidates to
			// restore onto and the first push simply starts from the top - an acceptable miss.
			NavigationCandidates.Collect(_scope, _candidates);

			var restored = _memory.Restore(_scope, _candidates);

			// With no history to go on, start at the top of the panel. Waiting for the player to push the
			// stick first meant arriving in a scene with nothing highlighted while A would still press
			// whatever the panel considers its default - the cursor was there, just invisible.
			restored ??= SpatialNavigator.First(_candidates);

			if (restored != null)
			{
				Select(restored);
				ScrollIntoView(restored);
				return;
			}

			// Nothing to land on yet because the panel has not been laid out. Keep looking for a while.
			_initialSelectionFrames = InitialSelectionFrames;
		}

		private void Move(Vector2Int direction)
		{
			RefreshCandidates();
			if (_candidates.Count == 0)
			{
				ClearSelection();
				return;
			}

			// A push can land on a control that would rather absorb it: sideways on a slider changes its
			// value, up or down on a plain ListView moves its selected row. Re-applying the highlight afterwards matters because adjusting moves the
			// dragger, which changes what sits under the control's centre.
			if (_selected != null && ControlActivator.TryAdjust(_selected, direction))
			{
				_highlighter.Apply(_selected);
				return;
			}

			var next = SpatialNavigator.Next(_candidates, _selected, direction);
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

			var first = SpatialNavigator.First(_candidates);
			if (first == null)
			{
				return;
			}

			_initialSelectionFrames = 0;
			Select(first);
			ScrollIntoView(first);
		}

		// The right stick scrolls whatever list the player is in. It is free to use here: the camera
		// processor stands down while a panel is stacked, and in the main menu there is no camera at all.
		// Deliberately does not move the selection - this is the player looking around the list, and
		// yanking the cursor with the viewport would make it impossible to just read something.
		private void Scroll(Gamepad gamepad)
		{
			if (!_panelTracker.HasStackedPanel && !_dropdownTracker.IsOpen)
			{
				return;
			}

			var stick = gamepad.rightStick.ReadValue();
			if (stick.magnitude < ScrollDeadzone)
			{
				return;
			}

			var scrollView = FindScrollView();
			if (scrollView == null)
			{
				return;
			}

			// Capped frame time for the same reason the camera caps it: one long hitch should not fling
			// the list to the far end.
			var step = ScrollSpeed * Mathf.Min(Time.unscaledDeltaTime, MaxFrameTime);
			var offset = scrollView.scrollOffset;
			offset.x += stick.x * step;
			offset.y -= stick.y * step;

			// The setter runs both axes through their scrollers, which clamp, so no range check is needed.
			scrollView.scrollOffset = offset;
		}

		// The list the selection is actually sitting in, falling back to the scope itself - an open
		// dropdown *is* a ScrollView - and then to whatever list the panel holds.
		private ScrollView FindScrollView()
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

			return _scope as ScrollView ?? _scope?.Q<ScrollView>();
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
				return;
			}

			RefreshCandidates();

			var leftmost = BottomBarNavigation.Leftmost(_candidates, _pendingSubSection);
			if (leftmost == null)
			{
				return;
			}

			_pendingSubSection = null;
			Select(leftmost);
			ScrollIntoView(leftmost);
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
				_pendingSubSectionFrames = SubSectionSettleFrames;
			}

			return true;
		}

		// The old build synthesised an Escape key press on the real keyboard device and released it a
		// frame later by queueing a blank keyboard state - which also wiped whatever the player was
		// genuinely holding down. The panel's own cancel handler is public; calling it is both exact
		// and free of side effects.
		private bool Cancel()
		{
			// The drawer closes itself on the Cancel keybinding, which we deliberately no longer
			// synthesise - so closing it is on us before the press reaches the panel behind it.
			if (_dropdownTracker.IsOpen)
			{
				ClearSelection();
				_dropdownTracker.Close();
				_uiSoundController.PlayCancelSound();
				return true;
			}

			var controller = _panelTracker.TopController;
			if (controller == null)
			{
				return false;
			}

			ClearSelection();
			controller.OnUICancelled();
			_uiSoundController.PlayCancelSound();
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
			if (_selected == null || _selected.panel == null)
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
