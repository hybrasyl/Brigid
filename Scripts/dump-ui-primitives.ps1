# Regenerates the authored-UI-primitives reskin report — the inventory of Brigid's panels/popups
# and the named child controls a datf pack would address (e.g. "bankshoppanel.titlelabel").
#
# Usage: .\Scripts\dump-ui-primitives.ps1
#   Writes Scripts\output\primitivesdump<date>-<time>.{md,json}. Pure source analysis (Roslyn) —
#   no game data or running client required.

$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')

dotnet run --project Tools/Brigid.UiPrimitivesReport/Brigid.UiPrimitivesReport.csproj
