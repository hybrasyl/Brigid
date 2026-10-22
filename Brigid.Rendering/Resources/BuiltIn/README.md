# Built-in art

Brigid's own default art (BRIG-123). `BuiltInArt` draws an image from this folder when the installed `ui_theme` pack has no part for the role. The built-in art is one step to run Brigid with no dat file.

- Give each image the name of the `ui_theme` role that it replaces, with `.png`: for example `icon.status.awake.png`.
- Only original art goes here. Purchased art stays out of the repository.
- Each image has an SVG source in `src/` with the same name. `Scripts/build-builtin-art.py` renders each source to its PNG at the role's size at density 4 (a status icon is 72 by 72); run it after a change to a source (`python -m pip install resvg-py`, then `python Scripts/build-builtin-art.py`), and commit the SVG and the PNG together.
- The build embeds each `.png` in `Brigid.Rendering`. A role with no image falls back to the legacy art.
- Brigid draws an image like a theme part with no slice: it scales the image to fit its rectangle, keeps the aspect ratio, and centres it. Draw the image large (for example 64 by 64 for an 18 by 18 icon), so that it stays sharp at large window sizes.

The social status icons are `icon.status.awake`, `icon.status.do_not_disturb`, `icon.status.daydreaming`, `icon.status.need_group`, `icon.status.grouped`, `icon.status.lone_hunter`, `icon.status.group_hunting` and `icon.status.need_help`. The set is single-colour line art in white on a 24-unit grid with a 2-unit stroke: a sun, a circle with a bar, a thought cloud, a person with a plus, two people, a sword, crossed swords, and an exclamation mark in a circle. A theme's `icon.status.*` part replaces an icon.
