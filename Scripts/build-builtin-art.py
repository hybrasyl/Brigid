"""Builds Brigid's built-in art (BRIG-123) from its SVG sources.

Each SVG in Brigid.Rendering/Resources/BuiltIn/src is rendered to a PNG of the same name in
Brigid.Rendering/Resources/BuiltIn, which the build embeds. The PNG is the role's size at density 4 (an 18 by 18
logical px icon is 72 by 72), so it stays sharp at large window sizes. Run it after a change to an SVG, and commit the
SVG and the PNG together.

    python -m pip install resvg-py
    python Scripts/build-builtin-art.py
"""

import os
import sys

import resvg_py

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILT_IN = os.path.join(ROOT, "Brigid.Rendering", "Resources", "BuiltIn")
SOURCES = os.path.join(BUILT_IN, "src")

# the size of each image in art px, by the start of its role name
SIZES = {"icon.status.": 72}


def size_for(role):
    for prefix, size in SIZES.items():
        if role.startswith(prefix):
            return size

    sys.exit(f"no size for {role}; add its role prefix to SIZES")


def main():
    for name in sorted(os.listdir(SOURCES)):
        if not name.endswith(".svg"):
            continue

        role = name[:-len(".svg")]
        size = size_for(role)
        png = bytes(resvg_py.svg_to_bytes(svg_path=os.path.join(SOURCES, name), width=size, height=size))

        with open(os.path.join(BUILT_IN, role + ".png"), "wb") as out:
            out.write(png)

        print(f"{role}.png ({size} by {size}, {len(png)} bytes)")


if __name__ == "__main__":
    main()
