# Built-in art

Brigid's own default art (BRIG-123). `BuiltInArt` draws an image from this folder when the installed `ui_theme` pack has no part for the role. The built-in art is one step to run Brigid with no dat file.

- Give each image the name of the `ui_theme` role that it replaces, with `.png`: for example `icon.status.awake.png`.
- Only original art goes here. Purchased art stays out of the repository.
- The build embeds each `.png` in `Brigid.Rendering`. A role with no image falls back to the legacy art.
- Brigid draws an image like a theme part with no slice: it scales the image to fit its rectangle, keeps the aspect ratio, and centres it. Draw the image large (for example 64 by 64 for an 18 by 18 icon), so that it stays sharp at large window sizes.

The social status icons are `icon.status.awake`, `icon.status.do_not_disturb`, `icon.status.daydreaming`, `icon.status.need_group`, `icon.status.grouped`, `icon.status.lone_hunter`, `icon.status.group_hunting` and `icon.status.need_help`.
