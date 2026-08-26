using System;
using System.Reflection;
using HarmonyLib;
using Timberborn.CameraSystem;
using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using UnityEngine;

namespace ControllerSupport
{
	// KeyboardCameraController is entirely digital (IsKeyHeld booleans, direction vectors with no
	// magnitude) and sealed with no extension point, so there is no way to get analog speed out of it
	// without replacing its private update methods outright. These three patches do that.
	//
	// The right stick and triggers are real, rebindable Secondary bindings on the base game's own
	// MoveCameraUp/Down/Left/Right, RotateCameraUp/Down/Left/Right and ZoomIn/ZoomOut keybindings (see
	// Root/KeyBindings/Camera) - not a separate custom keybinding surface. The trick that makes that
	// analog instead of the usual on/off IsHeld: read each keybinding's Primary and Secondary
	// InputBinding *separately* via KeyBindingRegistry.Get(id) and combine with Mathf.Max, rather than
	// going through the aggregate IsKeyHeld/GetRawValue (which ORs Primary+Secondary into one boolean
	// and would make a light stick push register as a full digital press). Keyboard therefore still
	// reads as a clean 0/1 through its Primary binding; the stick/trigger's Secondary binding is a true
	// continuous value (the Input System's own per-direction deadzone processing on the stick's
	// synthetic up/down/left/right sub-controls already applies, so no extra deadzone math is needed
	// here).
	//
	// Speed comes from the controller's own private MovementSpeed/RotationSpeed/ZoomSpeed properties via
	// reflection on the live instance, so it always matches the real "camera movement/rotation/zoom
	// speed" settings, including the MoveCameraFast 2x multiplier and the zoom spec's BaseZoomSpeed - no
	// duplicated formula constants.
	//
	// Holding a modifier button switches the stick from panning to rotating, same trade
	// MouseCameraController makes for RMB-held-and-dragging. That modifier is its own rebindable
	// keybind (RotateModifierKey, see Root/KeyBindings/GamePad/KeyBinding.GamepadCameraRotateModifier)
	// rather than Timberborn's own KeyBindingSystem modifier concept: InputModifiers is a closed 4-bit
	// Ctrl/Alt/Shift/Cmd enum read only from Keyboard.current (Timberborn.InputSystem.InputModifiers),
	// so a gamepad button can never satisfy it - there is no blueprint-level way to make a stick-press
	// act as a modifier for another gamepad control. Reading RotateModifierKey through InputService
	// instead is what makes "which button arms rotate" itself remappable in the controls menu, even
	// though it composes with the stick axes in code rather than through a single chorded binding path.
	// Both axes are negated relative to the mouse-drag version - the expected stick convention is "the
	// stick points where the camera looks" (flight-stick / third-person-camera style), not "drag the
	// world".
	internal static class CameraKeyBindingAxes
	{
		// This mod's own keybind (Root/KeyBindings/GamePad/KeyBinding.GamepadCameraRotateModifier),
		// primary-bound to <Gamepad>/rightStickPress by default - rebindable to any control, including
		// the left stick's press, the same as every other keybind in this mod.
		public const string RotateModifierKey = "GamepadCameraRotateModifier";

		public static readonly GamepadAxisKeys Move =
			new GamepadAxisKeys("MoveCameraUp", "MoveCameraDown", "MoveCameraLeft", "MoveCameraRight");

		public static readonly GamepadAxisKeys Rotate =
			new GamepadAxisKeys("RotateCameraUp", "RotateCameraDown", "RotateCameraLeft", "RotateCameraRight");

		private static readonly FieldInfo KeyBindingRegistryField =
			AccessTools.Field(typeof(InputService), "_keyBindingRegistry");

		private const float MaxFrameTime = 0.2f;
		private const float FailureLogInterval = 30f;

		private static float _nextFailureLogTime;

		public static float CappedDeltaTime => Mathf.Min(Time.unscaledDeltaTime, MaxFrameTime);

		public static KeyBindingRegistry ResolveRegistry(InputService inputService)
		{
			return (KeyBindingRegistry)KeyBindingRegistryField.GetValue(inputService);
		}

		// Primary alone (keyboard/mouse, digital) maxed with Secondary alone (stick/trigger, analog) -
		// never the aggregate IsHeld/GetRawValue, which would flatten the analog side to on/off.
		public static float AxisValue(KeyBindingRegistry registry, string id, bool includeSecondary)
		{
			var keyBinding = registry.Get(id);
			var primary = keyBinding.PrimaryInputBinding.GetRawValue();
			var secondary = includeSecondary ? keyBinding.SecondaryInputBinding.GetRawValue() : 0f;
			return Mathf.Max(primary, secondary);
		}

		public static Vector2 ReadAxes(KeyBindingRegistry registry, GamepadAxisKeys keys, bool includeSecondary)
		{
			return new Vector2(
				AxisValue(registry, keys.Right, includeSecondary) - AxisValue(registry, keys.Left, includeSecondary),
				AxisValue(registry, keys.Up, includeSecondary) - AxisValue(registry, keys.Down, includeSecondary));
		}

		// Secondary only, no keyboard mixed in - used to let the right stick scroll UI lists with the
		// same physical binding it pans the camera with (see GamepadNavigationInputProcessor.Scroll).
		public static Vector2 ReadSecondaryAxes(KeyBindingRegistry registry, GamepadAxisKeys keys)
		{
			return new Vector2(
				registry.Get(keys.Right).SecondaryInputBinding.GetRawValue()
					- registry.Get(keys.Left).SecondaryInputBinding.GetRawValue(),
				registry.Get(keys.Up).SecondaryInputBinding.GetRawValue()
					- registry.Get(keys.Down).SecondaryInputBinding.GetRawValue());
		}

		public static void ReportFailure(string context, Exception e)
		{
			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] {context} failed: {e}");
		}
	}

	[HarmonyPatch(typeof(KeyboardCameraController), "MovementUpdate")]
	internal static class CameraMovementAnalogPatch
	{
		private static readonly FieldInfo InputServiceField =
			AccessTools.Field(typeof(KeyboardCameraController), "_inputService");

		private static readonly FieldInfo CameraServiceField =
			AccessTools.Field(typeof(KeyboardCameraController), "_cameraService");

		private static readonly MethodInfo MovementSpeedGetter =
			AccessTools.PropertyGetter(typeof(KeyboardCameraController), "MovementSpeed");

		[HarmonyPrefix]
		private static bool Prefix(KeyboardCameraController __instance)
		{
			if (!Application.isFocused)
			{
				return true;
			}

			try
			{
				var inputService = (InputService)InputServiceField.GetValue(__instance);
				var cameraService = (CameraService)CameraServiceField.GetValue(__instance);
				var registry = CameraKeyBindingAxes.ResolveRegistry(inputService);

				var rotateModifierHeld = inputService.IsKeyHeld(CameraKeyBindingAxes.RotateModifierKey);
				var raw = CameraKeyBindingAxes.ReadAxes(registry, CameraKeyBindingAxes.Move, includeSecondary: !rotateModifierHeld);

				var magnitude = raw.magnitude;
				if (magnitude <= 0f)
				{
					return false;
				}

				var direction = raw / magnitude;
				var throttle = Mathf.Min(magnitude, 1f);
				var movementSpeed = (float)MovementSpeedGetter.Invoke(__instance, null);
				var speed = movementSpeed * cameraService.ZoomSpeedScale * CameraKeyBindingAxes.CappedDeltaTime;

				var delta = new Vector3(direction.x, 0f, direction.y) * (throttle * speed);
				cameraService.MoveCameraBy(delta);
				return false;
			}
			catch (Exception e)
			{
				CameraKeyBindingAxes.ReportFailure("Camera panning", e);
				return true;
			}
		}
	}

	[HarmonyPatch(typeof(KeyboardCameraController), "SmoothRotationUpdate")]
	internal static class CameraRotationAnalogPatch
	{
		private static readonly FieldInfo InputServiceField =
			AccessTools.Field(typeof(KeyboardCameraController), "_inputService");

		private static readonly FieldInfo CameraServiceField =
			AccessTools.Field(typeof(KeyboardCameraController), "_cameraService");

		[HarmonyPrefix]
		private static bool Prefix(KeyboardCameraController __instance, float rotationSpeed)
		{
			if (!Application.isFocused)
			{
				return true;
			}

			try
			{
				var inputService = (InputService)InputServiceField.GetValue(__instance);
				var cameraService = (CameraService)CameraServiceField.GetValue(__instance);
				var registry = CameraKeyBindingAxes.ResolveRegistry(inputService);

				var rotateModifierHeld = inputService.IsKeyHeld(CameraKeyBindingAxes.RotateModifierKey);
				var raw = CameraKeyBindingAxes.ReadAxes(registry, CameraKeyBindingAxes.Rotate, includeSecondary: rotateModifierHeld);

				var magnitude = raw.magnitude;
				if (magnitude <= 0f)
				{
					return false;
				}

				var direction = raw / magnitude;
				var throttle = Mathf.Min(magnitude, 1f);

				cameraService.ModifyHorizontalAngle(-direction.x * throttle * rotationSpeed);
				cameraService.ModifyVerticalAngle(direction.y * throttle * rotationSpeed);
				return false;
			}
			catch (Exception e)
			{
				CameraKeyBindingAxes.ReportFailure("Camera rotation", e);
				return true;
			}
		}
	}

	[HarmonyPatch(typeof(KeyboardCameraController), "ZoomUpdate")]
	internal static class CameraZoomAnalogPatch
	{
		private const string ZoomInKey = "ZoomIn";
		private const string ZoomOutKey = "ZoomOut";

		private static readonly FieldInfo InputServiceField =
			AccessTools.Field(typeof(KeyboardCameraController), "_inputService");

		private static readonly FieldInfo CameraServiceField =
			AccessTools.Field(typeof(KeyboardCameraController), "_cameraService");

		private static readonly MethodInfo ZoomSpeedGetter =
			AccessTools.PropertyGetter(typeof(KeyboardCameraController), "ZoomSpeed");

		[HarmonyPrefix]
		private static bool Prefix(KeyboardCameraController __instance)
		{
			if (!Application.isFocused)
			{
				return true;
			}

			try
			{
				var inputService = (InputService)InputServiceField.GetValue(__instance);
				var cameraService = (CameraService)CameraServiceField.GetValue(__instance);
				var registry = CameraKeyBindingAxes.ResolveRegistry(inputService);

				var zoomIn = CameraKeyBindingAxes.AxisValue(registry, ZoomInKey, includeSecondary: true);
				var zoomOut = CameraKeyBindingAxes.AxisValue(registry, ZoomOutKey, includeSecondary: true);
				var amount = zoomIn - zoomOut;
				if (amount == 0f)
				{
					return false;
				}

				var zoomSpeed = (float)ZoomSpeedGetter.Invoke(__instance, null);
				cameraService.ModifyZoomLevel(amount * zoomSpeed * CameraKeyBindingAxes.CappedDeltaTime);
				return false;
			}
			catch (Exception e)
			{
				CameraKeyBindingAxes.ReportFailure("Camera zoom", e);
				return true;
			}
		}
	}
}
