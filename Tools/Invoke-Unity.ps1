[CmdletBinding()]
param(
    [ValidateSet('Prepare','Compile','ImportTmp','Generate','EditMode','PlayMode','Windows','AndroidAstc','AndroidEtc2','HotWindows','HotAndroidAstc','HotAndroidEtc2','InstallHybridCLR','CheckHybridCLR','CheckCompatibility')]
    [string]$Mode = 'Compile',
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe',
    [ValidatePattern('^Unity[A-Za-z0-9_-]*$')]
    [string]$ProjectName = 'UnityProject',
    [switch]$NoSync,
    [switch]$DevelopmentHttp,
    [switch]$Development,
    [string]$BaselineId,
    [string]$BaselinePath,
    [string]$AppVersion,
    [ValidateRange(1, 2147483647)][int]$AndroidVersionCode,
    [ValidateRange(1L, [long]::MaxValue)][long]$ContentVersion,
    [string]$OutputRoot,
    [string]$SigningKey,
    [string]$OfflinePath,
    [ValidateSet('Win64','Android')][string]$BuildTarget,
    [ValidateRange(1, 14400)][int]$TimeoutSeconds = 3600
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo ".artifacts/$ProjectName"
$reports = Join-Path $repo '.artifacts/unity'
New-Item -ItemType Directory -Force $project,$reports | Out-Null

# Only the explicitly listed source trees are mirrored. Library and user projects are never removed.
if (-not $NoSync) {
    foreach ($tree in @('Assets','Packages','ProjectSettings','Shared','Config')) {
        $source = Join-Path $repo $tree
        if (-not (Test-Path -LiteralPath $source)) { continue }
        $destination = [IO.Path]::GetFullPath((Join-Path $project $tree))
        if (-not $destination.StartsWith($project + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe mirror destination' }
        New-Item -ItemType Directory -Force $destination | Out-Null
        $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse -Force) {
            $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
            if ($relative -match '(^|[\\/])(bin|obj|\.git)([\\/]|$)') { continue }
            if ($tree -eq 'Assets' -and $relative.Replace('\','/') -match
                '^(Game/HotUpdate/Generated(?:[/.]|$)|HybridCLR/Generate(?:[/.]|$)|Game/Bootstrap/Resources/AstralOathPlayerBaseline\.json(?:\.meta)?$)') { continue }
            $expected.Add($relative) | Out-Null
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
            if (-not (Test-Path -LiteralPath $target) -or (Get-Item -LiteralPath $target).LastWriteTimeUtc -ne $file.LastWriteTimeUtc -or (Get-Item -LiteralPath $target).Length -ne $file.Length) {
                Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            }
        }
        foreach ($file in Get-ChildItem -LiteralPath $destination -File -Recurse -Force) {
            $relative = [IO.Path]::GetRelativePath($destination, $file.FullName)
            if (-not $expected.Contains($relative)) { Remove-Item -LiteralPath $file.FullName -Force }
        }
    }
}
if ($Mode -eq 'Prepare') { Write-Output $project; exit 0 }
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity 2022.3.62f3 not found: $Unity" }
$log = Join-Path $reports "$Mode.log"
if ($Mode -in 'EditMode','PlayMode') {
    $oldResult = Join-Path $reports "$Mode.xml"
    if (Test-Path -LiteralPath $oldResult) { Remove-Item -LiteralPath $oldResult -Force }
}
$arguments = @('-batchmode','-nographics','-projectPath',"`"$project`"",'-logFile',"`"$log`"")
if ($Mode -in 'Windows','HotWindows') { $BuildTarget = 'Win64' }
if ($Mode -in 'AndroidAstc','AndroidEtc2','HotAndroidAstc','HotAndroidEtc2') { $BuildTarget = 'Android' }
if ($BuildTarget) { $arguments += @('-buildTarget', $BuildTarget) }
if ($DevelopmentHttp) { $arguments += '-astral-dev-http' }
if ($Development) { $arguments += '-astral-development' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo '.artifacts/builds' }
$bindings = @{
    '-astral-baseline-id' = $BaselineId
    '-astral-baseline-path' = $BaselinePath
    '-astral-app-version' = $AppVersion
    '-astral-content-version' = $ContentVersion
    '-astral-android-version-code' = $AndroidVersionCode
    '-astral-output-root' = $OutputRoot
    '-astral-offline-path' = $OfflinePath
}
foreach ($name in $bindings.Keys) {
    $value = $bindings[$name]
    if (-not $value) { continue }
    if ($name -in '-astral-baseline-path','-astral-output-root','-astral-offline-path') {
        $value = [IO.Path]::GetFullPath($value, (Get-Location).ProviderPath)
    }
    if ("$value" -match '["\r\n]') { throw "Invalid value for $name." }
    $trailingBackslashes = "$value".Length - "$value".TrimEnd('\').Length
    $arguments += @($name, ('"' + $value + ('\' * $trailingBackslashes) + '"'))
}
switch ($Mode) {
    'Compile' { $arguments += '-quit' }
    { $_ -in 'EditMode','PlayMode' } { $arguments += @('-runTests','-testPlatform',$Mode,'-testResults',"`"$(Join-Path $reports "$Mode.xml")`"") }
    default {
        $legacyMethod = @{ImportTmp='ImportTmpEssentials';Generate='CreateDemo';Windows='BuildWindows';AndroidAstc='BuildAndroidAstc';AndroidEtc2='BuildAndroidEtc2'}[$Mode]
        $builderMethod = @{
            HotWindows='BuildHotWindows'
            HotAndroidAstc='BuildHotAndroidAstc'
            HotAndroidEtc2='BuildHotAndroidEtc2'
            InstallHybridCLR='Install'
            CheckHybridCLR='CheckEnvironment'
            CheckCompatibility='CheckCompatibility'
        }[$Mode]
        $execute = if ($legacyMethod) { "AstralOath.Editor.FoundationBuilder.$legacyMethod" }
            else { "AstralOath.Editor.HotUpdateBuilder.$builderMethod" }
        $arguments += @('-quit','-executeMethod', $execute)
    }
}
$oldToolRoot = $env:ASTRAL_BUILD_TOOL_ROOT
$oldSigningKey = $env:ASTRAL_SIGNING_KEY
try {
    $env:ASTRAL_BUILD_TOOL_ROOT = $repo
    if ($SigningKey) { $env:ASTRAL_SIGNING_KEY = [IO.Path]::GetFullPath($SigningKey, (Get-Location).ProviderPath) }
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
} finally {
    $env:ASTRAL_BUILD_TOOL_ROOT = $oldToolRoot
    $env:ASTRAL_SIGNING_KEY = $oldSigningKey
}
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    throw "Unity $Mode exceeded the timeout. See $log"
}
Write-Output "Unity $Mode exit=$($process.ExitCode); log=$log"
if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $log -Tail 70; exit $process.ExitCode }
if ($Mode -in 'EditMode','PlayMode') {
    $result = Join-Path $reports "$Mode.xml"
    if (-not (Test-Path -LiteralPath $result)) { throw 'Unity did not produce a test report' }
    [xml]$xml = Get-Content -LiteralPath $result -Raw
    if ($xml.'test-run'.result -ne 'Passed' -or [int]$xml.'test-run'.total -eq 0) { throw "Unity tests did not pass: $($xml.'test-run'.result)" }
    Write-Output "Tests: $($xml.'test-run'.total), passed=$($xml.'test-run'.passed), failed=$($xml.'test-run'.failed)"
}
