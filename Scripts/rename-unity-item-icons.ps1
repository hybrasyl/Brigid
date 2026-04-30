[CmdletBinding()]
param(
    [string]$SourcePath = 'E:\Hybrasyl Dev\Client Assets\Unity Assets\Item Icons - Actual',
    [int]$SheetSize = 266,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
    throw "Source folder not found: $SourcePath"
}

$pattern = '^item(\d{3})_(\d{1,3})\.png$'
$files = Get-ChildItem -LiteralPath $SourcePath -Filter '*.png' -File

$plan = New-Object System.Collections.Generic.List[object]
$skipped = 0
$alreadyRenamed = 0

foreach ($file in $files) {
    if ($file.Name -match '^item\d{5}\.png$') {
        $alreadyRenamed++
        continue
    }
    if ($file.Name -notmatch $pattern) {
        Write-Warning "Skipping (does not match pattern): $($file.Name)"
        $skipped++
        continue
    }
    $sheet = [int]$Matches[1]
    $idx = [int]$Matches[2]
    if ($idx -ge $SheetSize) {
        Write-Warning "Skipping (index $idx >= sheet size $SheetSize): $($file.Name)"
        $skipped++
        continue
    }
    $legacyId = ($sheet - 1) * $SheetSize + $idx + 1
    $newName = 'item{0:D5}.png' -f $legacyId
    $plan.Add([PSCustomObject]@{
        Old = $file.Name
        New = $newName
        FullOld = $file.FullName
        FullNew = Join-Path $file.DirectoryName $newName
    })
}

$collisions = $plan | Group-Object New | Where-Object { $_.Count -gt 1 }
if ($collisions) {
    Write-Error 'Name collisions detected — aborting:'
    foreach ($c in $collisions) {
        Write-Error "  $($c.Name) <-  $($c.Group.Old -join ', ')"
    }
    throw 'Resolve collisions before running.'
}

$existingTargets = $plan | Where-Object {
    $_.FullOld -ne $_.FullNew -and (Test-Path -LiteralPath $_.FullNew)
}
if ($existingTargets) {
    Write-Error 'Target filenames already exist — aborting:'
    $existingTargets | Select-Object -First 10 | ForEach-Object {
        Write-Error "  $($_.New) (would overwrite, source: $($_.Old))"
    }
    throw 'Move or remove pre-existing targets before running.'
}

"Source:        $SourcePath"
"Sheet size:    $SheetSize"
"Files scanned: $($files.Count)"
"To rename:     $($plan.Count)"
"Already named: $alreadyRenamed"
"Skipped:       $skipped"

if ($DryRun) {
    'Dry run — first 5 mappings:'
    $plan | Select-Object -First 5 | ForEach-Object { "  $($_.Old) -> $($_.New)" }
    'Last 5 mappings:'
    $plan | Select-Object -Last 5 | ForEach-Object { "  $($_.Old) -> $($_.New)" }
    'No files modified.'
    return
}

$renamed = 0
foreach ($entry in $plan) {
    Rename-Item -LiteralPath $entry.FullOld -NewName $entry.New
    $renamed++
}
"Renamed $renamed file(s)."
