using System;
using System.Collections;
using System.Reflection;
using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Knows which panel is in front of the player.
	//
	// The previous approach walked GameObject.Find("PanelStack") and guessed at the last visible
	// child of the container. PanelStack already tracks this exactly, and posts PanelShownEvent /
	// PanelHiddenEvent whenever it changes, so we mirror its state instead of re-deriving it. The
	// stack field itself is private and StackedPanel is an internal struct, but IPanelController is
	// public - which is all we need to drive the panel and to key per-panel rules on later.
	internal class PanelTracker : ILoadableSingleton, IUnloadableSingleton
	{
		private static readonly FieldInfo StackField =
			typeof(PanelStack).GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly FieldInfo RootField =
			typeof(PanelStack).GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic);

		private readonly PanelStack _panelStack;
		private readonly EventBus _eventBus;

		private PropertyInfo _panelControllerProperty;
		private PropertyInfo _visualElementProperty;
		private bool _reflectionFailed;

		private bool _dirty = true;
		private IPanelController _topController;
		private VisualElement _topElement;

		public PanelTracker(PanelStack panelStack, EventBus eventBus)
		{
			_panelStack = panelStack;
			_eventBus = eventBus;
		}

		// Raised after the front panel changed. Subscribers must drop any element references they
		// were holding - the panel they belonged to is gone from the tree.
		public event Action PanelChanged;

		public IPanelController TopController
		{
			get
			{
				Refresh();
				return _topController;
			}
		}

		// The element PanelStack actually pushed. For overlay/dialog panels this is the overlay
		// wrapper rather than the panel itself, which is what we want: it is the whole of what the
		// player can currently interact with.
		//
		// With nothing pushed this falls back to the whole UI root, which is what makes the Game and
		// MapEditor scenes work at all: there, the HUD is not on the panel stack. UILayout calls
		// PanelStack.Initialize("Common/GameUI", "Panels") and then hangs the bottom bar, entity
		// panels, notifications and the rest off *sibling* containers of "Panels" via AddBottomBar /
		// AddTopLeft / AddAbsoluteItem. So an empty stack means "no dialog is up", not "nothing to
		// navigate", and the root is the only element covering both the HUD and the panel container.
		public VisualElement TopElement
		{
			get
			{
				Refresh();
				return _topElement;
			}
		}

		public void Load()
		{
			_eventBus.Register(this);
		}

		public void Unload()
		{
			_eventBus.Unregister(this);
		}

		[OnEvent]
		public void OnPanelShown(PanelShownEvent panelShownEvent)
		{
			Invalidate();
		}

		[OnEvent]
		public void OnPanelHidden(PanelHiddenEvent panelHiddenEvent)
		{
			Invalidate();
		}

		private void Invalidate()
		{
			_dirty = true;
			PanelChanged?.Invoke();
		}

		private void Refresh()
		{
			if (!_dirty)
			{
				return;
			}

			_dirty = false;
			_topController = null;
			_topElement = Root();

			if (_reflectionFailed || StackField == null)
			{
				ReportReflectionFailure("PanelStack._stack is no longer accessible");
				return;
			}

			try
			{
				// Stack<T> enumerates from the top down, so the first entry is the front panel.
				if (StackField.GetValue(_panelStack) is not IEnumerable stack)
				{
					return;
				}

				foreach (var stackedPanel in stack)
				{
					if (stackedPanel == null)
					{
						return;
					}

					_panelControllerProperty ??= stackedPanel.GetType().GetProperty("PanelController");
					_visualElementProperty ??= stackedPanel.GetType().GetProperty("VisualElement");
					if (_panelControllerProperty == null || _visualElementProperty == null)
					{
						ReportReflectionFailure("StackedPanel members are no longer accessible");
						return;
					}

					_topController = _panelControllerProperty.GetValue(stackedPanel) as IPanelController;
					_topElement = _visualElementProperty.GetValue(stackedPanel) as VisualElement ?? Root();
					return;
				}
			}
			catch (Exception e)
			{
				ReportReflectionFailure(e.Message);
			}
		}

		// Null until UILayout (Game/MapEditor) or TitleScreen (MainMenu) has run PanelStack.Initialize.
		private VisualElement Root()
		{
			if (RootField == null)
			{
				return null;
			}

			try
			{
				return RootField.GetValue(_panelStack) as VisualElement;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private void ReportReflectionFailure(string reason)
		{
			if (_reflectionFailed)
			{
				return;
			}

			_reflectionFailed = true;
			Debug.LogWarning($"[ControllerSupport] Gamepad navigation disabled: {reason}.");
		}
	}
}
