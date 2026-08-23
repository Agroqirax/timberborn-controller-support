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
