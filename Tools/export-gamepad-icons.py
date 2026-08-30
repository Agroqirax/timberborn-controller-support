#!/usr/bin/env python3
"""Exports the gamepad button icons from Assets/controleler-icons.svg into
Root/Sprites/GamepadButtons/<label>.png, one PNG per Inkscape-labelled group.

Each icon's glyph is scaled so its shorter edge is exactly SHORT_EDGE -
2*PADDING pixels (aspect ratio preserved for non-square icons), then padded
with PADDING transparent pixels on all four sides - so the shorter edge of
the final PNG is exactly SHORT_EDGE, and the longer edge is the scaled
glyph's long edge plus 2*PADDING. Requires rsvg-convert on PATH.

Re-run this whenever controleler-icons.svg changes.
"""

import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SVG_PATH = REPO_ROOT / "Assets" / "controleler-icons.svg"
OUTPUT_DIR = REPO_ROOT / "Root" / "Sprites" / "GamepadButtons"
SHORT_EDGE = 48
PADDING = 4
CONTENT_SHORT_EDGE = SHORT_EDGE - 2 * PADDING

# Colour/outline letter variants - not real button glyphs, excluded per design.
EXCLUDED_LABELS = {
    "a_c", "a_o", "b_c", "b_o", "x_c", "x_o", "y_c", "y_o",
}

LABEL_RE = re.compile(
    r'<g\s+id="(?P<id>[a-zA-Z0-9]+)"\s+inkscape:label="(?P<label>[^"]+)"'
)


def find_labelled_groups(svg_text: str) -> dict[str, str]:
    groups = {}
    for match in LABEL_RE.finditer(svg_text):
        label = match.group("label")
        if label in EXCLUDED_LABELS:
            continue
        groups[label] = match.group("id")
    return groups


def native_size(object_id: str) -> tuple[int, int]:
    # Render at a large fixed width so the auto-scaled height reveals the
    # native aspect ratio with good precision.
    probe = subprocess.run(
        [
            "rsvg-convert", "-i", object_id, "-w", "2000",
            str(SVG_PATH),
        ],
        capture_output=True, check=True,
    )
    return png_dimensions(probe.stdout)


def png_dimensions(png_bytes: bytes) -> tuple[int, int]:
    # PNG IHDR chunk: width/height are the 4-byte big-endian ints right after
    # the 8-byte signature + 4-byte length + 4-byte "IHDR" tag.
    width = int.from_bytes(png_bytes[16:20], "big")
    height = int.from_bytes(png_bytes[20:24], "big")
    return width, height


def export_icon(label: str, object_id: str) -> None:
    native_w, native_h = native_size(object_id)
    if native_w <= native_h:
        content_w = CONTENT_SHORT_EDGE
        content_h = round(CONTENT_SHORT_EDGE * native_h / native_w)
    else:
        content_h = CONTENT_SHORT_EDGE
        content_w = round(CONTENT_SHORT_EDGE * native_w / native_h)

    page_w = content_w + 2 * PADDING
    page_h = content_h + 2 * PADDING

    out_path = OUTPUT_DIR / f"{label}.png"
    subprocess.run(
        [
            "rsvg-convert", "-i", object_id,
            "-w", str(content_w), "-h", str(content_h),
            "--left", str(PADDING), "--top", str(PADDING),
            "--page-width", str(page_w), "--page-height", str(page_h),
            str(SVG_PATH), "-o", str(out_path),
        ],
        check=True,
    )
    print(f"{label}: {native_w}x{native_h} -> {page_w}x{page_h} (content {content_w}x{content_h}, {out_path.name})")


def main() -> int:
    if not SVG_PATH.exists():
        print(f"Missing {SVG_PATH}", file=sys.stderr)
        return 1

    svg_text = SVG_PATH.read_text()
    groups = find_labelled_groups(svg_text)
    if not groups:
        print("No labelled groups found", file=sys.stderr)
        return 1

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for label in sorted(groups):
        export_icon(label, groups[label])

    print(f"\nExported {len(groups)} icons to {OUTPUT_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
