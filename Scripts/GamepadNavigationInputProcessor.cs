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

			var handled = false;

			var direction = _reader.ReadMove(gamepad);
			if (direction != Vector2Int.zero)
			{
				handled = Move(direction);
			}

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
			_scope = scope;

			// The new scope may not have been laid out yet, in which case there are no candidates to
			// restore onto and the first push simply starts from the top - an acceptable miss.
			NavigationCandidates.Collect(_scope, _candidates);

			var restored = _memory.Restore(_scope, _candidates);

			// A freshly opened dropdown has no history, but landing on its first item beats making
			// the player push once just to enter the list they deliberately opened.
			if (restored == null && _dropdownTracker.IsOpen)
			{
				restored = SpatialNavigator.First(_candidates);
			}

			if (restored != null)
			{
				Select(restored);
				ScrollIntoView(restored);
			}
		}

		private bool Move(Vector2Int direction)
		{
			RefreshCandidates();
			if (_candidates.Count == 0)
			{
				ClearSelection();
				return false;
			}

			// A sideways push on a slider (or a buttons-only dropdown) changes its value rather than
			// moving off it. Re-applying the highlight afterwards matters because adjusting moves the
			// dragger, which changes what sits under the control's centre.
			if (direction.x != 0 && _selected != null && ControlActivator.TryAdjust(_selected, direction.x))
			{
				_highlighter.Apply(_selected);
				return true;
			}

			var next = SpatialNavigator.Next(_candidates, _selected, direction);
			if (next != null)
			{
				Select(next);
				ScrollIntoView(next);
			}

			// Consumed either way. There is nothing else in the chain that wants the stick, and
			// reporting "unhandled" at the edge of a panel would only invite another processor to act
			// on a push the player meant for us.
			return true;
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

			ControlActivator.Activate(_selected);
			_uiSoundController.PlayClickSound();
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
