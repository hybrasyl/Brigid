# item-dye-compare

A dev tool for BRIG-41. It checks the pack dye pass against the legacy dye on real item art, and it is not part of `Brigid.slnx`.

For each `item{id:D5}.png` below sprite 20001 in a folder (for example the Unity rip), the tool:

1. Detects whether the PNG is dyeable by the client's rule: it contains a pixel of the canonical purple ramp.
2. Renders the legacy item (EPF + palette) undyed, and compares it with the PNG, each cropped to its visible pixels. A difference here is a difference in the source art, not a dye error.
3. Dyes both for each colour (`--colors`, default `1,5,30`): the PNG through `ItemDyePass`, the legacy item through `ApplyDye`. Only the pixels that the dye adds to the art difference count as dye-pipeline errors.
4. Reports the disagreements with the legacy rule, in both directions. An item whose PNG has ramp pixels but whose legacy palette has no dye ramp is not dyed by the legacy client, so it belongs in the pack's `no_dye` list. An item that the legacy client dyes but whose PNG has no ramp pixel needs its art repainted with the ramp.
5. Times a cold decode, detect and dye of 60 dyeable icons (one inventory). The GPU upload is not in this number.

It also compares the hardcoded ramp with `color0.tbl` entry 0.

## Side effects

The tool starts the data layer as the client does, so it also scans the per-user assets folder for packs: it creates the folder when it does not exist, and on a first run it can copy legacy `{data}/hybrasyl-data/*.datf` packs into it. The registry's warnings go to stderr.

## Run

```bash
dotnet run --project Scripts/item-dye-compare -- "<game data folder>" "<png folder>" [--colors 1,5,30] [--first N]
```

The report is Markdown on stdout.

## Result

The result of each run that the team relies on is recorded on the card BRIG-41, not here.
