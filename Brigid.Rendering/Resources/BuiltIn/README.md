# Built-in art

Brigid's own default art (BRIG-123). `BuiltInArt` draws an image from this folder when the installed `ui_theme` pack has no part for the role. The built-in art is one step to run Brigid with no dat file.

- Give each image the name of the `ui_theme` role that it replaces, with `.png`: for example `icon.status.awake.png`.
- Only original art goes here. Purchased art stays out of the repository.
- Each image has an SVG source in `src/` with the same name. `Scripts/build-builtin-art.py` renders each source to its PNG at the role's size at density 4 (a status icon is 72 by 72); run it after a change to a source (`python -m pip install resvg-py`, then `python Scripts/build-builtin-art.py`), and commit the SVG and the PNG together.
- The build embeds each `.png` in `Brigid.Rendering`. A role with no image falls back to the legacy art.
- Brigid draws an image like a theme part with no slice: it scales the image to fit its rectangle, keeps the aspect ratio, and centres it. Draw the image large (for example 64 by 64 for an 18 by 18 icon), so that it stays sharp at large window sizes.

The social status icons are `icon.status.awake`, `icon.status.do_not_disturb`, `icon.status.daydreaming`, `icon.status.need_group`, `icon.status.grouped`, `icon.status.lone_hunter`, `icon.status.group_hunting` and `icon.status.need_help`. The set is single-colour line art in white on a 24-unit grid with a 2-unit stroke: a sun, a circle with a bar, a thought cloud, a person with a plus, two people, a sword, crossed swords, and an exclamation mark in a circle. A theme's `icon.status.*` part replaces an icon.

The top bar icons of the new HUD (BRIG-144) are drawn at 64 by 64 for a 13 by 13 icon. `icon.hud.latency.none`, `icon.hud.latency.good`, `icon.hud.latency.fair` and `icon.hud.latency.poor` are three rising bars: the filled bars are the reading (none, three, two, one), in blue grey, green, orange and red, so the shape and the colour both give the bucket. `icon.hud.secure` is a padlock, `icon.hud.gold` a coin in gold, `icon.hud.weight` a weight, `icon.hud.mail` an envelope and `icon.hud.parcel` a box, the last four in white line art like the status icons.

The window button icons of the new HUD (BRIG-144) are drawn at 72 by 72 for an 18 by 18 button, in white line art like the status icons: `icon.window.abilities` (a spark), `icon.window.inventory` (a chest), `icon.window.equipment` (a shield), `icon.window.mastery` (a star), `icon.window.quests` (a scroll), `icon.window.menu` (three lines), `icon.window.mail` (an envelope), `icon.window.community` (two people), `icon.window.map` (a folded map), `icon.window.group` (a flag) and `icon.window.screenshot` (a camera).

The buttons of the chat box of the new HUD (BRIG-145) are drawn at 64 by 64 for a 13 by 13 button (15 by 13 for hide), in white line art with a 2.5-unit stroke, so they stay clear at that size: `icon.chat.hide` (a chevron down), `icon.chat.show` (a chevron up) and `icon.chat.add` (a plus).

The gold bag of the Inventory window of the new HUD (BRIG-150), `icon.inventory.gold_bag`, is drawn at 64 by 64 for a 16 by 16 icon: a sack tied at the neck, with a coin ring, in the gold of `icon.hud.gold`. A drag of it drops or gives gold.

The swap button of a compact window panel of the new HUD (BRIG-150), `icon.panel.swap_sides`, is drawn at 56 by 56 for a 14 by 14 button, in white line art with a 2.5-unit stroke like the chat box buttons: an arrow to the right over an arrow to the left. The title link to the Legend window (BRIG-151), `icon.panel.legend`, has the same size and a 2.25-unit stroke: an open book. A panel shows it at the left end of its title row (today the Details window).

The empty-slot outlines of the Equipment window of the new HUD (BRIG-151), `icon.equipment.*`, one for each slot, are drawn at 128 by 128 for a 32 by 32 slot, in white line art with a 1.5-unit stroke. The slot draws them at 35% alpha, so they read as a faint outline of the item that the slot takes.

The arrows of the stat raise of the Details window of the new HUD (BRIG-151), `icon.details.raise` and `icon.details.lower`, are drawn at 56 by 56 for a 14 by 14 button, in white line art with a 3-unit stroke: a chevron up and a chevron down.

The page arrows of the hotbar of the new HUD (BRIG-146), `icon.hotbar.page_up` and `icon.hotbar.page_down`, are drawn at 56 by 56 for a 14 by 10 button (the icon area is 10 by 6), as solid white triangles that fill the width of the image; the button draws the square image in its 10 by 6 icon area at 6 by 6: up and down, as in the wireframe.
