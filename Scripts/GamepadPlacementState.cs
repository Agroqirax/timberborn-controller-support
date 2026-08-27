using Timberborn.Rendering;
using UnityEngine;

namespace ControllerSupport
{
	// Bridges GamepadBuildingPlacementController / GamepadAreaSelectionController (writers, once per
	// frame) to InputServicePlacementPatch / CameraServicePlacementPatch / the invalid-preview patches
	// (readers). Static because Harmony patch classes are static and cannot take constructor DI.
	internal static class GamepadPlacementState
	{
		public static bool Active;

		// Distinct from Active: Active means "the Harmony patches should synthesize gamepad-driven
		// values this frame" and now flips false on a frame the real mouse is driving the cursor
		// instead (see GamepadMouseHandoff). ToolEngaged means "one of the three cursor controllers
		// is genuinely running its tool right now", regardless of which device drives that frame -
		// GamepadNavigationInputProcessor reads this one to decide whether to stand down, since
		// standing down only on Active would let it treat every mouse-driven placement frame as
		// "nothing engaged" and highlight the bare HUD out from under an idle stick.
		public static bool ToolEngaged;

		public static Vector3Int GridCursor;
		public static bool MainMouseButtonDown;
		public static bool MainMouseButtonHeld;
		public static bool MainMouseButtonUp;

		// Set once by GamepadAreaSelectionController.Load and left alone thereafter - unlike the fields
		// above this isn't per-frame state, it's a standing reference to a drawer PlantingPreviewPatch
		// shares so it doesn't have to create - and leak - its own MeshDrawer. Null until Load runs;
		// every reader treats that as "nothing to draw yet" rather than throwing, since a patch can fire
		// before singleton loading order gets there.
		public static MeshDrawer InvalidTileDrawer;

		// Not our own colour - copied from TreeCuttingColorsSpec.ToolNoActionTile (the tile
		// TreeCuttingAreaUnselectionTool already draws for a cell with nothing to remove), read via
		// reflection in GamepadAreaSelectionController.Load since that spec type is internal to
		// Timberborn.ForestryUI. An invented shade of our own looked out of place next to the game's
		// existing UI language; reusing a colour the game already uses for "nothing happens here"
		// doesn't. Height matches where that same tile draws, so it looks identical, not just
		// same-coloured.
		public static Color InvalidColor;
		public const float InvalidTileHeight = 0.02f;

		public static void Clear()
		{
			Active = false;
			ToolEngaged = false;
			MainMouseButtonDown = false;
			MainMouseButtonHeld = false;
			MainMouseButtonUp = false;
		}
	}
}
