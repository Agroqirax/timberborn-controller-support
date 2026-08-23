using System;
using System.Reflection;
using Timberborn.DropdownSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// An open dropdown list is not inside the panel that opened it. DropdownListDrawer builds its own
	// UIDocument root ("DropdownListDrawer", sorting order 2) and moves the item elements into it, so
	// a walk over the front panel never sees them and the list stayed unreachable.
	//
	// While a list is open it becomes the navigation scope outright - which is also what the player
	// expects, since nothing behind the dropdown should be reachable until it closes.
	internal class DropdownTracker
	{
		private static readonly FieldInfo ItemsField =
			typeof(DropdownListDrawer).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);

		private readonly DropdownListDrawer _dropdownListDrawer;
		private bool _reflectionFailed;

		public DropdownTracker(DropdownListDrawer dropdownListDrawer)
		{
			_dropdownListDrawer = dropdownListDrawer;
		}

		public bool IsOpen
		{
			get
			{
				try
				{
					return _dropdownListDrawer.DropdownVisible;
				}
				catch (Exception)
				{
					// DropdownVisible dereferences a root that only exists after the drawer's Load().
					return false;
				}
			}
		}

		// The ScrollView holding the item elements, or null when nothing is open.
		public VisualElement Scope
		{
			get
			{
				if (!IsOpen)
				{
					return null;
				}

				if (ItemsField == null)
				{
					ReportReflectionFailure();
					return null;
				}

				try
				{
					return ItemsField.GetValue(_dropdownListDrawer) as VisualElement;
				}
				catch (Exception)
				{
					ReportReflectionFailure();
					return null;
				}
			}
		}

		public void Close()
		{
			_dropdownListDrawer.HideDropdown();
		}

		private void ReportReflectionFailure()
		{
			if (_reflectionFailed)
			{
				return;
			}

			_reflectionFailed = true;
			Debug.LogWarning("[ControllerSupport] DropdownListDrawer._items is no longer accessible; "
				+ "dropdown lists will not be navigable.");
		}
	}
}
