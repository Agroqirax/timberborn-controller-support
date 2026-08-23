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
		private readonly UISoundController _uiSoundController;

		private readonly GamepadReader _reader = new GamepadReader();
		private readonly List<VisualElement> _candidates = new List<VisualElement>();

		private VisualElement _scope;
		private VisualElement _selected;
		private Vector2 _lastSelectionCentre;
		private float _nextFailureLogTime;

		public GamepadNavigationInputProcessor(InputService inputService, PanelTracker panelTracker,
			UISoundController uiSoundController)
		{
			_inputService = inputService;
			_panelTracker = panelTracker;
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

			var scope = _panelTracker.TopElement;
			if (scope == null)
			{
				ClearSelection();
				return false;
			}

			if (!ReferenceEquals(scope, _scope))
			{
				_scope = scope;
				ClearSelection();
			}

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

		private bool Move(Vector2Int direction)
		{
			RefreshCandidates();
			if (_candidates.Count == 0)
			{
				ClearSelection();
				return false;
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
			SelectionHighlighter.Remove(_selected);
			_selected = SpatialNavigator.NearestTo(_candidates, _lastSelectionCentre);
			SelectionHighlighter.Apply(_selected);
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

			Activate(_selected);
			_uiSoundController.PlayClickSound();
			return true;
		}

		private static void Activate(VisualElement element)
		{
			// A Toggle reacts to pointer events through its Clickable manipulator, not to ClickEvent,
			// so a synthesised click slides straight past it. Setting the value fires the ChangeEvent
			// the game is actually listening for.
			if (element is Toggle toggle)
			{
				toggle.value = !toggle.value;
				return;
			}

			// ClickEvent derives from PointerEventBase, whose dispatch routes to whatever sits under
			// the mouse unless a target is already set. Setting it is what makes the press land on the
			// selected element instead of the hovered one.
			using var clickEvent = ClickEvent.GetPooled();
			clickEvent.target = element;
			element.SendEvent(clickEvent);
		}

		// The old build synthesised an Escape key press on the real keyboard device and released it a
		// frame later by queueing a blank keyboard state - which also wiped whatever the player was
		// genuinely holding down. The panel's own cancel handler is public; calling it is both exact
		// and free of side effects.
		private bool Cancel()
		{
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

		private void Select(VisualElement element)
		{
			if (ReferenceEquals(_selected, element))
			{
				return;
			}

			ClearSelection();
			_selected = element;
			_lastSelectionCentre = element.worldBound.center;
			SelectionHighlighter.Apply(element);
		}

		private void ClearSelection()
		{
			if (_selected == null)
			{
				return;
			}

			SelectionHighlighter.Remove(_selected);
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
