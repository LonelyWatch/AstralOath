[CmdletBinding()]
param([ValidateSet('Foundation','CharacterLab')][string]$Profile = 'Foundation')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo $(if ($Profile -eq 'CharacterLab') { '.artifacts/UnityCharacterLab' } else { '.artifacts/UnityProject' })
if (-not (Test-Path (Join-Path $project 'Assets/Game'))) { throw 'Generate the isolated Unity project first.' }
$generated = if ($Profile -eq 'CharacterLab') {
    @('Assets/Game/Modules/CharacterLab/Scenes/CharacterLab.unity',
      'Assets/Game/Modules/CharacterLab/Res/Prefab/CharacterLabWindow.prefab',
      'Assets/Game/Modules/CharacterPresentation/Res/Materials/CharacterSpriteBlur.mat',
      'Assets/Game/Modules/FoundationDemo/AssetBundleCollectorSetting.asset') | ForEach-Object {
        Get-Item -LiteralPath (Join-Path $project $_)
    }
} else { @(Get-ChildItem (Join-Path $project 'Assets/Game') -File -Recurse | Where-Object { $_.Extension -in @('.unity','.prefab','.asset','.mat','.png') }) }

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
if ($Profile -eq 'CharacterLab') {
    # Only these imported textures receive the explicit PMA validation profile.
    $manifest = Get-Content -LiteralPath (Join-Path $repo 'Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in $manifest.entries | Where-Object { $_.path.EndsWith('.png') }) {
        $relative = $entry.path
        if (-not $relative.StartsWith('Assets/Game/Modules/CharacterPresentation/Res/') -or $relative.Split('/') -contains '..' -or $relative.Contains('\')) { throw 'Unsafe character texture path.' }
        $sourceMeta = Join-Path $project ($relative + '.meta')
        $targetMeta = Join-Path $repo ($relative + '.meta')
        if ((Select-String '^guid: ' -LiteralPath $sourceMeta).Line -ne (Select-String '^guid: ' -LiteralPath $targetMeta).Line) { throw "Texture GUID conflict: $relative" }
        Copy-Item -LiteralPath $sourceMeta -Destination $targetMeta -Force
    }
} else {
    Copy-Item (Join-Path $project 'ProjectSettings/EditorBuildSettings.asset') (Join-Path $repo 'ProjectSettings/EditorBuildSettings.asset') -Force
}
Write-Output "Synchronized $($generated.Count) generated Unity assets and missing metadata; source code was not overwritten."
