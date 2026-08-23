using UnityEngine;

namespace ControllerSupport
{
	// Bridges GamepadBuildingPlacementController (writer, once per frame) to
	// InputServicePlacementPatch / CameraServicePlacementPatch (readers). Static because Harmony
	// patch classes are static and cannot take constructor DI.
	internal static class GamepadPlacementState
	{
		public static bool Active;
		public static Vector3Int GridCursor;
		public static bool MainMouseButtonDown;
		public static bool MainMouseButtonHeld;
		public static bool MainMouseButtonUp;

		public static void Clear()
		{
			Active = false;
			MainMouseButtonDown = false;
			MainMouseButtonHeld = false;
			MainMouseButtonUp = false;
		}
	}
}
