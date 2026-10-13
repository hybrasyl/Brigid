# items-test-001-002 — Stage 1 test pack scaffold

Hand-editable starting point for the Phase 1 / Stage 1 item-asset-pack test pack covering sheets 1–2 (item IDs 1–532).

## Building the test pack

1. Drop PNGs into this directory alongside `_manifest.json`. Naming convention: `item{id:D5}.png` — five-digit zero-padded, 1-based. Examples:
   - sprite ID 1 → `item00001.png`
   - sprite ID 95 → `item00095.png`
   - sprite ID 482 → `item00482.png`
2. Zip the directory contents (manifest + PNGs) at the **archive root** — not inside a subdirectory.
3. Rename `.zip` → `.datf`.
4. Put the `.datf` in the per-user assets folder (`%LOCALAPPDATA%\Erisco\Brigid\assets` on Windows; `AppPaths.AssetsDir`). The game data folder is not read for packs.
5. Restart the client.

## What the manifest declares

- `content_type: item_icons` registers this pack as an `item_icons` pack (`AssetPackRegistry.ItemPacks`).
- `covers.item_icons.dyeable` is an explicit list, and the client uses it as the **exact set** of dyeable items: it does not detect dyeability from the art for this pack. The 45 IDs listed are the dyeable items of sheets 1–2. They are the same 45 ids that ramp detection finds in the Unity rip.
- Items **not** in the `dyeable` list ignore the server's color byte and render as-is.
- Items in the `dyeable` list run the canonical-purple find-and-replace via [ItemDyePass.cs](../../../Brigid.Rendering/ItemDyePass.cs) when the server-sent color byte is non-zero.
- A pack without a `dyeable` list (the normal case, and what Taliesin writes) lets the client detect dyeability from the art, with `no_dye` for the exceptions. See `assetPackList.md`.

## Adjusting the dyeable list

If you discover an item that visually dyes but is missing from the array, add its ID. If you find an item that incorrectly tries to dye (e.g. an artist-painted purple that wasn't meant to be replaced), remove it from the list. A new pack does not need this list: remove `dyeable` to let the client detect the ramp, and list exceptions in `no_dye`.

## Note on hand-authoring vs Taliesin

This scaffold is older than Taliesin's item pipeline. Taliesin now authors item packs: it writes no `dyeable` list, so the client detects dyeability from the art, and its "No dye" box writes `covers.item_icons.no_dye` for an item that has ramp pixels but must not take the dye. Keep this scaffold only as an example of an explicit `dyeable` list.
