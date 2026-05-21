# items-test-001-002 — Stage 1 test pack scaffold

Hand-editable starting point for the Phase 1 / Stage 1 item-asset-pack test pack covering sheets 1–2 (item IDs 1–532).

## Building the test pack

1. Drop PNGs into this directory alongside `_manifest.json`. Naming convention: `item{id:D5}.png` — five-digit zero-padded, 1-based. Examples:
   - sprite ID 1 → `item00001.png`
   - sprite ID 95 → `item00095.png`
   - sprite ID 482 → `item00482.png`
2. Zip the directory contents (manifest + PNGs) at the **archive root** — not inside a subdirectory.
3. Rename `.zip` → `.datf`.
4. Drop the `.datf` into the Dark Ages data directory (the folder `GlobalSettings.DataPath` points at).
5. Restart the client.

## What the manifest declares

- `content_type: item_icons` registers this pack with `AssetPackRegistry.GetItemPack()`.
- `covers.item_icons.dyeable` is the **opt-in list** of item IDs that participate in the runtime find-and-replace dye pass. The 45 IDs listed are the rule-passing dyeable items from sheets 1–2 per `Scripts/dump-item-palette/items-dyeable.csv` (`dyeable_loose=True`).
- Items **not** in the `dyeable` list ignore the server's color byte and render as-is. The renderer also collapses the cache key to `color = 0` for them so a single decode is shared across every incoming color.
- Items in the `dyeable` list run the canonical-purple find-and-replace via [ItemDyePass.cs](../../../Chaos.Client.Rendering/ItemDyePass.cs) when the server-sent color byte is non-zero.

## Adjusting the dyeable list

If you discover an item that visually dyes but is missing from the array, add its ID. If you find an item that incorrectly tries to dye (e.g. an artist-painted purple that wasn't meant to be replaced), remove it from the list. The dump-tool CSV is a starting heuristic, not ground truth.

## Note on hand-authoring vs Taliesin

This scaffold exists so the Stage 1 review gate (pixel-for-pixel diff against legacy) can run before Taliesin's `.datf` build pipeline is ready. Taliesin will own authoring going forward — artists will toggle dyeability per-item in the Taliesin UI with live preview, and the manifest will be generated from those flags. This sample is a hand-editable bridge.
