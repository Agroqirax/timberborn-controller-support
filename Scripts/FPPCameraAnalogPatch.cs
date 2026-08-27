using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.CameraSystem;
using Timberborn.InputSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// FPPCamera (kulesz.FPPCamera, optional workshop mod) ships mouse-only look and four
	// IsKeyHeld-driven movement booleans, with no extension point - FPPCameraController is
	// internal and its update methods are private. Movement's gamepad Secondary bindings already
	// exist (Root/KeyBindings/FPPCamera/KeyBinding.Move*.optional.blueprint.json, bound to the
	// left stick's synthetic up/down/left/right sub-controls) but UpdateMovement only ever reads
	// them through IsKeyHeld, so today the stick acts as four digital buttons - full speed the
	// instant the deadzone is crossed, diagonals limited to whatever angle two adjacent buttons
	// happen to combine into. MovementPatch replaces UpdateMovement with a version that reads the
	// same keybind ids' raw analog value instead, the same Primary-max-Secondary pattern
	// CameraKeyBindingAxes/GamepadAxis already establish elsewhere in this mod - so a keyboard
	// press still reads as a clean 0/1 (unchanged feel) while the stick contributes real magnitude
	// and any in-between angle, not just eight fixed ones.
	//
	// Look (YawPatch/PitchPatch) adds the right stick on top of the mouse rather than replacing
	// it, through the base game's own RotateCameraUp/Down/Left/Right keybindings - this mod
	// already binds the right stick's Secondary slot to those for the RTS camera (see
	// CameraKeyBindingAxes.Rotate in CameraAnalogPatch.cs), and reusing them means FPP look rides
	// the exact same rebindable control players already see as "Rotate Camera" in the Controls
	// menu, instead of a second bespoke keybind. ReadSecondaryAxes gives the stick's raw per-axis
	// value (no keyboard mixed in, no modifier gate needed here - FPP has no pan/rotate ambiguity
	// to disambiguate), so a light push turns slower than a full one: analog, same as movement.
	//
	// FPPCamera is optional and this mod must stay fully inert without it. FPPCamera.dll is
	// therefore never referenced at compile time (no precompiledReferences entry, no typeof()
	// anywhere in this file) - a [HarmonyPatch(typeof(FPPCameraController), ...)] attribute would
	// embed a type token that PatchAll()'s attribute scan resolves eagerly for every type in this
	// assembly, throwing the moment the mod isn't installed even though nothing here would
	// otherwise touch it. Everything below is applied manually from TryApply, only after
	// confirming the FPPCamera assembly is actually loaded; every field/method access on
	// FPPCameraController and its settings object goes through reflection, and every prefix falls
	// back to `return true` (run the original) on any failure, so a future FPPCamera update that
	// renames something degrades to "gamepad loses the analog upgrade", never a crash.
	internal static class FPPCameraIntegration
	{
		// Not derived from anything in FPPCamera - the mouse-delta formula it augments has no
		// natural stick-throttle equivalent to convert from, so this is a plain judgment-call
		// default (typical FPS gamepad look speed), not a reflected constant.
		private const float LookTurnRateDegPerSec = 150f;

		private static readonly GamepadAxisKeys MoveKeys = new GamepadAxisKeys(
			"FPPCamera.MoveForward", "FPPCamera.MoveBackward", "FPPCamera.MoveLeft", "FPPCamera.MoveRight");

		private const string RunKeyId = "FPPCamera.Run";
		private const string JumpKeyId = "FPPCamera.Jump";

		public static void TryApply(Harmony harmony)
		{
			var assembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name == "FPPCamera");
			if (assembly == null)
			{
				return;
			}

			var controllerType = assembly.GetType("FPPCamera.FPPCameraController");
			if (controllerType == null)
			{
				return;
			}

			try
			{
				Fields.Bind(controllerType);

				harmony.Patch(
					AccessTools.Method(controllerType, "UpdateMovement"),
					prefix: new HarmonyMethod(typeof(MovementPatch), nameof(MovementPatch.Prefix)));
				harmony.Patch(
					AccessTools.Method(controllerType, "UpdateSingleton"),
					prefix: new HarmonyMethod(typeof(YawPatch), nameof(YawPatch.Prefix)));
				harmony.Patch(
					AccessTools.Method(controllerType, "UpdateCameraRotation"),
					prefix: new HarmonyMethod(typeof(PitchPatch), nameof(PitchPatch.Prefix)));
			}
			catch (Exception e)
			{
				CameraKeyBindingAxes.ReportFailure("FPPCamera integration setup", e);
			}
		}

		// Cached reflection handles for FPPCameraController's private members, bound once we know
		// the type exists. Field/property names and static constants come straight off the
		// decompiled FPPCameraController/CameraSettings (kulesz.FPPCamera 1.1.2).
		private static class Fields
		{
			public static FieldInfo InputService;
			public static FieldInfo Root;
			public static FieldInfo Controller;
			public static FieldInfo CameraService;
			public static FieldInfo CameraSettings;
			public static FieldInfo VerticalVelocity;
			public static FieldInfo IsMoving;

			public static FieldInfo JumpHeight;
			public static FieldInfo Gravity;
			public static FieldInfo RunningSpeed;
			public static FieldInfo MovementSpeed;
			public static FieldInfo RotationLimit;

			public static MethodInfo HorizontalSensitivityGetter;
			public static MethodInfo VerticalSensitivityGetter;

			public static void Bind(Type controllerType)
			{
				InputService = AccessTools.Field(controllerType, "_inputService");
				Root = AccessTools.Field(controllerType, "_root");
				Controller = AccessTools.Field(controllerType, "_controller");
				CameraService = AccessTools.Field(controllerType, "_cameraService");
				CameraSettings = AccessTools.Field(controllerType, "_cameraSettings");
				VerticalVelocity = AccessTools.Field(controllerType, "_verticalVelocity");
				IsMoving = AccessTools.Field(controllerType, "_isMoving");

				JumpHeight = AccessTools.Field(controllerType, "JumpHeight");
				Gravity = AccessTools.Field(controllerType, "Gravity");
				RunningSpeed = AccessTools.Field(controllerType, "RunningSpeed");
				MovementSpeed = AccessTools.Field(controllerType, "MovementSpeed");
				RotationLimit = AccessTools.Field(controllerType, "RotationLimit");

				var cameraSettingsType = CameraSettings.FieldType;
				HorizontalSensitivityGetter = AccessTools.PropertyGetter(cameraSettingsType, "HorizontalSensitivity");
				VerticalSensitivityGetter = AccessTools.PropertyGetter(cameraSettingsType, "VerticalSensitivity");
			}
		}

		private static class MovementPatch
		{
			public static bool Prefix(object __instance, ref bool __result)
			{
				try
				{
					var root = (Transform)Fields.Root.GetValue(__instance);
					var controller = (CharacterController)Fields.Controller.GetValue(__instance);
					if (root == null || controller == null)
					{
						__result = false;
						return false;
					}

					var inputService = (InputService)Fields.InputService.GetValue(__instance);
					var registry = CameraKeyBindingAxes.ResolveRegistry(inputService);

					var raw = CameraKeyBindingAxes.ReadAxes(registry, MoveKeys, includeSecondary: true);
					var magnitude = raw.magnitude;
					var throttle = Mathf.Min(magnitude, 1f);
					var direction = magnitude > 0f ? raw / magnitude : Vector2.zero;

					var runHeld = inputService.IsKeyHeld(RunKeyId);
					var jumpHeld = inputService.IsKeyHeld(JumpKeyId);

					var jumpHeight = (float)Fields.JumpHeight.GetValue(null);
					var gravity = (float)Fields.Gravity.GetValue(null);
					var runningSpeed = (float)Fields.RunningSpeed.GetValue(null);
					var movementSpeed = (float)Fields.MovementSpeed.GetValue(null);

					var verticalVelocity = (float)Fields.VerticalVelocity.GetValue(__instance);
					var isGrounded = controller.isGrounded;
					if (isGrounded && verticalVelocity < 0f)
					{
						verticalVelocity = 0f;
					}
					if (jumpHeld && isGrounded)
					{
						verticalVelocity += Mathf.Sqrt(2f * jumpHeight * gravity);
					}
					verticalVelocity -= gravity * Time.unscaledDeltaTime;

					var speed = runHeld ? runningSpeed : movementSpeed;
					var move = (root.forward * direction.y + root.right * direction.x) * (throttle * speed);
					move.y = verticalVelocity;
					move *= Time.unscaledDeltaTime;
					controller.Move(move);

					Fields.VerticalVelocity.SetValue(__instance, verticalVelocity);
					var isMoving = magnitude > 0f;
					Fields.IsMoving.SetValue(__instance, isMoving);

					__result = isMoving | jumpHeld | runHeld;
					return false;
				}
				catch (Exception e)
				{
					CameraKeyBindingAxes.ReportFailure("FPP camera movement", e);
					return true;
				}
			}
		}

		private static class YawPatch
		{
			public static bool Prefix(object __instance)
			{
				try
				{
					var root = (Transform)Fields.Root.GetValue(__instance);
					var controller = (CharacterController)Fields.Controller.GetValue(__instance);
					if (root == null || controller == null)
					{
						return false;
					}

					var inputService = (InputService)Fields.InputService.GetValue(__instance);
					var registry = CameraKeyBindingAxes.ResolveRegistry(inputService);
					var cameraSettings = Fields.CameraSettings.GetValue(__instance);
					var horizontalSensitivity = (int)Fields.HorizontalSensitivityGetter.Invoke(cameraSettings, null);

					var mouseDelta = Mouse.current.delta.ReadValue();
					var stick = CameraKeyBindingAxes.ReadSecondaryAxes(registry, CameraKeyBindingAxes.Rotate);

					var mouseYaw = mouseDelta.x * horizontalSensitivity * Time.unscaledDeltaTime * 2f;
					var stickYaw = stick.x * LookTurnRateDegPerSec * Time.unscaledDeltaTime;
					root.Rotate(Vector3.up, mouseYaw + stickYaw, Space.World);
					return false;
				}
				catch (Exception e)
				{
					CameraKeyBindingAxes.ReportFailure("FPP camera yaw", e);
					return true;
				}
			}
		}

		private static class PitchPatch
		{
			public static bool Prefix(object __instance)
			{
				try
				{
					var root = (Transform)Fields.Root.GetValue(__instance);
					var controller = (CharacterController)Fields.Controller.GetValue(__instance);
					if (root == null || controller == null)
					{
						return false;
					}

					var inputService = (InputService)Fields.InputService.GetValue(__instance);
					var registry = CameraKeyBindingAxes.ResolveRegistry(inputService);
					var cameraSettings = Fields.CameraSettings.GetValue(__instance);
					var verticalSensitivity = (int)Fields.VerticalSensitivityGetter.Invoke(cameraSettings, null);
					var rotationLimit = (float)Fields.RotationLimit.GetValue(null);
					var cameraService = (CameraService)Fields.CameraService.GetValue(__instance);

					var mouseDelta = Mouse.current.delta.ReadValue();
					var stick = CameraKeyBindingAxes.ReadSecondaryAxes(registry, CameraKeyBindingAxes.Rotate);

					var mousePitch = (0f - mouseDelta.y) * verticalSensitivity * Time.unscaledDeltaTime * 2f;
					var stickPitch = (0f - stick.y) * LookTurnRateDegPerSec * Time.unscaledDeltaTime;

					var transform = cameraService.Transform;
					var pitch = transform.rotation.eulerAngles.x + mousePitch + stickPitch;
					pitch = pitch > 180f ? pitch - 360f : pitch;
					pitch = Mathf.Clamp(pitch, -rotationLimit, rotationLimit);

					var yaw = root.rotation.eulerAngles.y;
					var roll = transform.rotation.eulerAngles.z;
					transform.rotation = Quaternion.Euler(pitch, yaw, roll);
					return false;
				}
				catch (Exception e)
				{
					CameraKeyBindingAxes.ReportFailure("FPP camera pitch", e);
					return true;
				}
			}
		}
	}
}
