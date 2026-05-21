# Dumps every prefab control's referenced image (filename stem + frame index) to
# output/prefab-dump.log so artists can find which legacy asset a given prefab
# control name maps to (e.g. "InventoryBackgroundExpanded -> _ninv5.spf[0]").
#
# Usage: .\Scripts\dump-prefab-images.ps1
#   Then exit the client once the title screen appears — the dump fires during
#   data-context init, before anything else happens. Inspect output\prefab-dump.log
#   afterward (Select-String for the prefab control name you're trying to override).
#
# The dump is gated on the CHAOS_PREFAB_DUMP env var; normal client runs are silent.

$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force -Path output | Out-Null

$env:CHAOS_PREFAB_DUMP = '1'
try {
    dotnet run --project Chaos.Client/Chaos.Client.csproj 2>&1 | Tee-Object -FilePath output/prefab-dump.log
} finally {
    Remove-Item Env:\CHAOS_PREFAB_DUMP -ErrorAction SilentlyContinue
}
