using System;
using System.Collections.Generic;
using System.Reflection;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// UI Toolkit keeps the pieces we need for controller navigation internal: the :hover pseudo-state
	// that Timberborn's stylesheets already style, and the per-element callback registry that tells us
	// which elements actually respond to a click. Everything here degrades gracefully - if a future
	// Unity version renames a member, the probe reports "unsupported" and the caller falls back.
	internal static class VisualElementProbe
	{
		private const int HoverPseudoState = 2;

		private static readonly PropertyInfo PseudoStatesProperty =
			typeof(VisualElement).GetProperty("pseudoStates", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly FieldInfo CallbackRegistryField =
			typeof(VisualElement).GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly long ClickEventTypeId = EventBase<ClickEvent>.TypeId();

		// Timberborn's own controls always wire clicks through RegisterCallback<ClickEvent> (see
		// HasClickHandler above), but a Button is stock UI Toolkit underneath, and plenty of UI-framework
		// mods (BuildingBlueprints' dialogs go through TimberUi's DialogBoxElement.AddCloseButton, for
		// one) use the native `Button.clicked` event instead - wired through the Clickable manipulator's
		// own pointer-down/up handling, which never touches ClickEvent at all. HasClickHandler alone
		// therefore reads such a button as inert, NavigationCandidates.IsInertButton skips it, and it's
		// never reachable - the mod's own dialog close ("X") buttons specifically. `clicked` is a
		// field-like event, so its backing field is `private` regardless of the event's own `public`
		// accessibility - BindingFlags.NonPublic is required even though Clickable itself is public.
		private static readonly FieldInfo ClickableClickedField =
			typeof(Clickable).GetField("clicked", BindingFlags.Instance | BindingFlags.NonPublic);

		private static FieldInfo _bubbleUpCallbacksField;
		private static FieldInfo _callbackListField;
		private static FieldInfo _callbackArrayField;
		private static FieldInfo _callbackCountField;
		private static FieldInfo _eventTypeIdField;
		private static bool _clickProbeUnavailable;

		// m_Callback is declared on each generic functor instantiation, so its FieldInfo differs per
		// closed type and cannot be cached in a single field the way the shared base's members can.
		private static readonly Dictionary<Type, FieldInfo> CallbackFields = new Dictionary<Type, FieldInfo>();

		public static bool SupportsHoverState => PseudoStatesProperty != null;

		public static void SetHoverState(VisualElement element, bool hovered)
		{
			if (PseudoStatesProperty == null)
			{
				return;
			}

			try
			{
				var enumType = PseudoStatesProperty.PropertyType;
				var current = Convert.ToInt32(PseudoStatesProperty.GetValue(element));
				var updated = hovered ? current | HoverPseudoState : current & ~HoverPseudoState;
				if (updated != current)
				{
					PseudoStatesProperty.SetValue(element, Enum.ToObject(enumType, updated));
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[ControllerSupport] Could not set hover state: {e.Message}");
			}
		}

		// True only for the duration of a call this class made itself into SendEvent, never for a real
		// mouse event - GamepadTooltipDelayPatch reads this from inside Tooltip.Enable (invoked
		// synchronously off MouseEnterEvent, so it is still true at that point) to tell a gamepad-driven
		// hover apart from a real one, since both funnel through the exact same handler.
		internal static bool IsSyntheticDispatch { get; private set; }

		// The element the in-flight SendEvent call was targeted at. Read by GamepadTooltipDelayPatch
		// only while IsSyntheticDispatch is true, so it always reflects the element whose hover just
		// caused whatever handler is currently running - not any wider "current selection" state, which
		// would be wrong the moment a real mouse tooltip is what's actually on screen (this mod supports
		// mixing mouse/keyboard and gamepad at once, e.g. a Steam Deck trackpad mapped to mouse).
		internal static VisualElement DispatchTarget { get; private set; }

		// The pseudo-state above only makes :hover USS rules match - it never touches UI Toolkit's
		// event dispatcher. Real hover-triggered behaviour (Tooltip's show-delay timer, ToolButton's
		// bottom-bar description card via ToolService.SetTemporaryTool) is wired entirely off genuine
		// MouseEnterEvent/MouseLeaveEvent/MouseOverEvent/MouseOutEvent callbacks, so without this a
		// gamepad selection can rest on an element forever and neither will ever appear. Sending all
		// four - Enter/Leave are targeted, non-bubbling events (Tooltip.RegisterTooltip and
		// ToolButton.PostLoad both listen for those directly on their own root), Over/Out bubble (used
		// by ToolButton's own small tooltip init) - covers both patterns without needing to know which
		// one a given element actually uses.
		public static void DispatchHover(VisualElement element, bool entering)
		{
			IsSyntheticDispatch = true;
			DispatchTarget = element;
			try
			{
				if (entering)
				{
					SendMouseEnter(element);
					SendMouseOver(element);
				}
				else
				{
					SendMouseOut(element);
					SendMouseLeave(element);
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[ControllerSupport] Could not dispatch hover event: {e.Message}");
			}
			finally
			{
				IsSyntheticDispatch = false;
				DispatchTarget = null;
			}
		}

		private static void SendMouseEnter(VisualElement element)
		{
			using (var evt = MouseEnterEvent.GetPooled())
			{
				evt.target = element;
				element.SendEvent(evt);
			}
		}

		private static void SendMouseLeave(VisualElement element)
		{
			using (var evt = MouseLeaveEvent.GetPooled())
			{
				evt.target = element;
				element.SendEvent(evt);
			}
		}

		private static void SendMouseOver(VisualElement element)
		{
			using (var evt = MouseOverEvent.GetPooled())
			{
				evt.target = element;
				element.SendEvent(evt);
			}
		}

		private static void SendMouseOut(VisualElement element)
		{
			using (var evt = MouseOutEvent.GetPooled())
			{
				evt.target = element;
				element.SendEvent(evt);
			}
		}

		// True when the element has a ClickEvent callback that actually does something.
		//
		// This is what makes non-Button controls reachable - list rows, for example, are plain
		// VisualElements that call RegisterCallback<ClickEvent>(...) rather than deriving from Button.
		//
		// The catch: UISoundInitializer is an IVisualElementInitializer, and it registers a ClickEvent
		// handler on *every element in the game* to play the click sound named by its `--click-sound`
		// custom style. So a naive "has a ClickEvent callback" test answers yes for nearly everything,
		// including section headers and plain labels. Skipping that one callback is what puts the test
		// back to meaning "a player can click this".
		public static bool HasClickHandler(VisualElement element)
		{
			if (_clickProbeUnavailable || CallbackRegistryField == null)
			{
				return false;
			}

			try
			{
				var registry = CallbackRegistryField.GetValue(element);
				if (registry == null)
				{
					return false;
				}

				var callbackList = GetBubbleUpCallbackList(registry);
				if (callbackList == null)
				{
					return false;
				}

				var array = (Array)GetCallbackArrayField(callbackList).GetValue(callbackList);
				var count = (int)GetCallbackCountField(callbackList).GetValue(callbackList);
				for (var i = 0; i < count && i < array.Length; i++)
				{
					var functor = array.GetValue(i);
					if (functor == null)
					{
						continue;
					}

					var typeIdField = GetEventTypeIdField(functor);
					if (typeIdField == null || (long)typeIdField.GetValue(functor) != ClickEventTypeId)
					{
						continue;
					}

					if (!IsUiSoundCallback(functor))
					{
						return true;
					}
				}
			}
			catch (Exception e)
			{
				_clickProbeUnavailable = true;
				Debug.LogWarning($"[ControllerSupport] Click-handler probe unavailable, falling back to Button matching: {e.Message}");
			}

			return false;
		}

		// True when a Button has at least one subscriber on its native `clicked` event - the sibling
		// check to HasClickHandler for buttons that skip ClickEvent entirely (see ClickableClickedField).
		private static Clickable GetClickable(VisualElement element)
		{
			return (element as Button)?.clickable;
		}

		public static bool HasClickableDelegate(VisualElement element)
		{
			if (ClickableClickedField == null)
			{
				return false;
			}

			var clickable = GetClickable(element);
			return clickable != null && ClickableClickedField.GetValue(clickable) != null;
		}

		// Fires a Button's native `clicked` delegate directly - SendEvent(ClickEvent) never reaches it,
		// since Clickable reacts to pointer-down/up events, not ClickEvent. Safe to call unconditionally
		// alongside a ClickEvent dispatch: a button with no `clicked` subscriber (Timberborn's own,
		// RegisterCallback<ClickEvent>-based) just has a null delegate here and nothing happens.
		public static void InvokeClickedDelegate(VisualElement element)
		{
			var clickable = GetClickable(element);
			if (clickable == null || ClickableClickedField == null)
			{
				return;
			}

			(ClickableClickedField.GetValue(clickable) as Action)?.Invoke();
		}

		// The sound handler is a private instance method on UISoundInitializer, so the delegate it was
		// registered through carries that instance as its target.
		private static bool IsUiSoundCallback(object functor)
		{
			var field = GetCallbackField(functor.GetType());
			return field?.GetValue(functor) is Delegate callback && callback.Target is UISoundInitializer;
		}

		private static FieldInfo GetCallbackField(Type functorType)
		{
			if (CallbackFields.TryGetValue(functorType, out var field))
			{
				return field;
			}

			field = functorType.GetField("m_Callback", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			CallbackFields[functorType] = field;
			return field;
		}

		private static object GetBubbleUpCallbackList(object registry)
		{
			_bubbleUpCallbacksField ??= registry.GetType()
				.GetField("m_BubbleUpCallbacks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			var dynamicList = _bubbleUpCallbacksField?.GetValue(registry);
			if (dynamicList == null)
			{
				return null;
			}

			_callbackListField ??= dynamicList.GetType()
				.GetField("m_Callbacks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			return _callbackListField?.GetValue(dynamicList);
		}

		private static FieldInfo GetCallbackArrayField(object callbackList)
		{
			return _callbackArrayField ??= callbackList.GetType()
				.GetField("m_Array", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		}

		private static FieldInfo GetCallbackCountField(object callbackList)
		{
			return _callbackCountField ??= callbackList.GetType()
				.GetField("m_Count", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		}

		private static FieldInfo GetEventTypeIdField(object functor)
		{
			return _eventTypeIdField ??= functor.GetType()
				.GetField("eventTypeId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		}
	}
}
