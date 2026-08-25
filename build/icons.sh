#!/usr/bin/env bash
#
# Renders every icon file the platforms want from the one SVG.
#
# They used to be made by hand, which is why the volcano they held and the
# volcano the IDE drew had already drifted apart. Anything under Assets/ that
# is not the SVG is output: edit basalt.svg and run this.
#
#   build/icons.sh
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
assets="$root/src/Basalt.Shell/Assets"
source_svg="$assets/basalt.svg"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [[ ! -f "$source_svg" ]]; then
  echo "No $source_svg to render from." >&2
  exit 1
fi

# qlmanage is on every Mac; rsvg-convert and Inkscape are not. It writes
# <name>.png beside the output folder rather than to the name asked for, hence
# the rename.
render() {
  local size="$1" out="$2"
  qlmanage -t -s "$size" -o "$work" "$source_svg" >/dev/null 2>&1
  mv "$work/$(basename "$source_svg").png" "$out"
}

echo "==> Rendering from $(basename "$source_svg")"

# macOS wants a full iconset; the smaller entries matter most, because that is
# where an icon with too much in it turns to mush.
iconset="$work/basalt.iconset"
mkdir -p "$iconset"

for size in 16 32 128 256 512; do
  render "$size" "$iconset/icon_${size}x${size}.png"
  render "$((size * 2))" "$iconset/icon_${size}x${size}@2x.png"
done

# Windows asks for 48 and 64 as well, and macOS has no use for them: rendered
# beside the iconset rather than into it, since iconutil rejects a folder with
# names it does not recognise.
for size in 48 64; do
  render "$size" "$work/icon_${size}x${size}.png"
done

iconutil -c icns "$iconset" -o "$assets/basalt.icns"
echo "    basalt.icns"

# The window icon Avalonia loads, and what Linux desktops read.
render 512 "$assets/basalt.png"
echo "    basalt.png"

# Windows. sips cannot write .ico, so the frames are assembled by hand: an ICO
# is a small header, one directory entry per frame, then the PNGs themselves.
python3 - "$assets/basalt.ico" "$iconset" "$work" <<'PY'
import struct, sys, pathlib

target = sys.argv[1]
folders = [pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3])]

frames = []
for size in [16, 32, 48, 64, 128, 256]:
    for folder in folders:
        path = folder / f"icon_{size}x{size}.png"
        if path.exists():
            frames.append((size, path.read_bytes()))
            break
    else:
        raise SystemExit(f"no rendering for {size}x{size}")

header = struct.pack("<HHH", 0, 1, len(frames))
offset = 6 + 16 * len(frames)
directory, images = b"", b""

for size, data in frames:
    # 0 means 256 in an ICO directory: the field is one byte.
    directory += struct.pack(
        "<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
    images += data
    offset += len(data)

pathlib.Path(target).write_bytes(header + directory + images)
print(f"    {pathlib.Path(target).name} ({len(frames)} frames)")
PY

echo "==> Done. Nothing under Assets/ is edited by hand except the SVG."
