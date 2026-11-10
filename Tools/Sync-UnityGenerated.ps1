[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo '.artifacts/UnityProject'
if (-not (Test-Path (Join-Path $project 'Assets/Game'))) { throw 'Generate the isolated Unity project first.' }
$generated = @(Get-ChildItem (Join-Path $project 'Assets/Game') -File -Recurse | Where-Object { $_.Extension -in @('.unity','.prefab','.asset','.mat','.png') })

# Write metadata BEFORE new assets: a live Editor must never invent a different GUID in between.
foreach ($file in $generated) {
    $relative = [IO.Path]::GetRelativePath($project, $file.FullName)
    $target = Join-Path $repo $relative
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    $sourceMeta = "$($file.FullName).meta"
    if (-not (Test-Path -LiteralPath $sourceMeta)) { throw "Generated asset has no metadata: $relative" }
    if (Test-Path -LiteralPath "$target.meta") {
        $sourceGuid = (Select-String '^guid: ' -LiteralPath $sourceMeta).Line
        $targetGuid = (Select-String '^guid: ' -LiteralPath "$target.meta").Line
        if ($sourceGuid -ne $targetGuid) { throw "GUID conflict for $relative. Sync canonical sources into the isolated project and regenerate; never overwrite an existing GUID." }
    } else { Copy-Item -LiteralPath $sourceMeta -Destination "$target.meta" }
}
foreach ($file in $generated) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $repo ([IO.Path]::GetRelativePath($project, $file.FullName))) -Force
}
foreach ($tree in @('Assets','Packages','Shared')) {
    foreach ($meta in Get-ChildItem (Join-Path $project $tree) -File -Recurse -Filter '*.meta') {
        $target = Join-Path $repo ([IO.Path]::GetRelativePath($project, $meta.FullName))
        if (-not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $target.Substring(0, $target.Length - 5))) {
            Copy-Item -LiteralPath $meta.FullName -Destination $target
        }
    }
}
Copy-Item (Join-Path $project 'ProjectSettings/EditorBuildSettings.asset') (Join-Path $repo 'ProjectSettings/EditorBuildSettings.asset') -Force
Write-Output "Synchronized $($generated.Count) generated Unity assets and missing metadata; source code was not overwritten."
