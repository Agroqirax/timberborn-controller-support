using Timberborn.AreaSelectionSystem;
using Timberborn.BlueprintSystem;
using Timberborn.Rendering;
using Timberborn.TerrainQueryingSystem;
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

		// Set once by GamepadAreaSelectionController.Load and shared with any Harmony patch that needs to
		// draw its own "here's the cursor" box but has no BlockObjectSelectionDrawer of its own to route
		// through (see BuildingBlueprintsIntegration.DemolishToolPostfix) - a flat MeshDrawer tile
		// (InvalidTileDrawer, above) was tried for that exact purpose once and wasn't reliably visible in
		// real play, see GamepadSelectionController's own comment on _cursorBoundsDrawer, so
		// RectangleBoundsDrawer (degenerate start==end for a single cell) is the mechanism that's proven
		// to work. Published as the raw factory, not a single ready-made drawer, since a demolish-flavoured
		// cursor needs the game's own destruction red/white, not the tree-cutting yellow InvalidColor is -
		// each caller builds its own drawer in whatever colour actually matches what it's marking.
		public static RectangleBoundsDrawerFactory BoundsDrawerFactory;

		// Same reasoning: ISpecService is the other constructor dependency GetTreeCuttingNoActionColor
		// below already needed to look up an internal ComponentSpec by reflection - published here so
		// other Harmony patches on optional mods can look up their own matching spec colours (e.g.
		// DemolishingColorsSpec for BuildingBlueprintsIntegration) the same way, without each one needing
		// its own constructor-injected copy.
		public static ISpecService SpecService;

		// The straight-down ray CameraServicePlacementPatch already builds from GridCursor.x/y - exposed
		// so a Harmony patch that needs the *current* terrain height under the cursor (not GridCursor.z,
		// which is only ever set once at Activate() and never re-derived as x/y change - irrelevant to
		// every existing consumer since CameraServicePlacementPatch's own ray construction never reads it
		// either) can query TerrainPicker.PickTerrainCoordinates itself instead of trusting a stale value.
		public static TerrainPicker TerrainPicker;

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
