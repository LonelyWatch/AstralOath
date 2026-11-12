$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$initializer = Join-Path $repository 'Tools/ProjectEnvironment.ps1'
if (-not [IO.File]::Exists($initializer)) {
    throw 'ProjectEnvironment.ps1 is missing. Build tools have no common cache initializer.'
}
. $initializer

$managed = @(
    'TEMP', 'TMP', 'TMPDIR', 'UPM_CACHE_ROOT', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PACKAGES', 'DOTNET_CLI_HOME',
    'GRADLE_USER_HOME', 'PIP_CACHE_DIR', 'PYTHONPYCACHEPREFIX', 'UNITY_MCP_LOG_DIR',
    'UV_CACHE_DIR', 'npm_config_cache'
)
$before = @{}
foreach ($name in $managed) {
    $before[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Assert-Equal($Actual, $Expected, [string]$Context) {
    if ($Actual -cne $Expected) {
        throw "$Context. Expected '$Expected', got '$Actual'."
    }
}

$fixture = Join-Path $repository '.artifacts/project-environment-test'
[IO.Directory]::CreateDirectory($fixture) | Out-Null
try {
    foreach ($name in $managed) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    $custom = Join-Path $fixture ('cache with spaces-' + [Guid]::NewGuid().ToString('N'))
    $env:GRADLE_USER_HOME = $custom
    $env:PIP_CACHE_DIR = 'relative-pip-cache'
    $env:DOTNET_CLI_HOME = Join-Path $fixture 'caller-dotnet'
    $env:NUGET_PACKAGES = Join-Path $fixture 'caller-nuget'
    $dotnet = $env:DOTNET_CLI_HOME
    $packages = $env:NUGET_PACKAGES

    $result = Initialize-AstralProjectEnvironment -RepositoryRoot $fixture
    $tempPath = Join-Path $fixture '.artifacts/temp'
    Assert-Equal $result.TEMP $tempPath 'Temporary output follows the repository'
    Assert-Equal $env:TMP $tempPath 'TMP is initialized in process scope'
    Assert-Equal $env:TMPDIR $tempPath 'TMPDIR is initialized in process scope'
    Assert-Equal $env:GRADLE_USER_HOME $custom 'An absolute custom cache path is preserved'
    if (-not [IO.Directory]::Exists($custom)) {
        throw 'The new custom cache directory was not created.'
    }
    Assert-Equal $env:DOTNET_CLI_HOME $dotnet 'The caller owns DOTNET_CLI_HOME'
    Assert-Equal $env:NUGET_PACKAGES $packages 'The caller owns NUGET_PACKAGES'
    Assert-Equal ($result.PSObject.Properties.Name -join ',') ($managed -join ',') 'Output contains only managed cache locations'
    foreach ($name in $managed | Where-Object { $_ -notin 'GRADLE_USER_HOME', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES' }) {
        $path = $result.$name
        Assert-Equal $path ([Environment]::GetEnvironmentVariable($name, 'Process')) "Process variable $name matches the result"
        if (-not [IO.Directory]::Exists($path)) {
            throw "Default $name was not created in the repository."
        }
        if (-not $path.StartsWith($fixture + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Default $name escaped the repository."
        }
    }

    $again = Initialize-AstralProjectEnvironment -RepositoryRoot $fixture
    Assert-Equal ($again | ConvertTo-Json -Compress) ($result | ConvertTo-Json -Compress) 'Repeated initialization is stable'

    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        $systemRoot = [IO.Path]::GetPathRoot([Environment]::SystemDirectory)
        if ($systemRoot -ine [IO.Path]::GetPathRoot($fixture)) {
            $env:TEMP = $systemRoot.ToLowerInvariant() + 'Windows\Temp'
            $env:NUGET_PACKAGES = $systemRoot + 'SharedPackages'
            $env:DOTNET_CLI_HOME = $systemRoot + 'SharedDotnet'
            $warnings = @()
            $redirected = Initialize-AstralProjectEnvironment -RepositoryRoot $fixture -WarningVariable warnings -WarningAction SilentlyContinue
            $warningText = $warnings -join [Environment]::NewLine
            Assert-Equal $redirected.TEMP $tempPath 'Windows system-drive overrides are redirected'
            Assert-Equal $redirected.NUGET_PACKAGES (Join-Path $fixture '.tools/nuget') 'Direct restores cannot use system-drive packages'
            Assert-Equal $redirected.DOTNET_CLI_HOME (Join-Path $fixture '.artifacts/dotnet') 'The .NET CLI home is redirected from the system drive'
            if ($warningText -notmatch 'TEMP' -or $warningText -match 'Windows[\\/]Temp') {
                throw 'The redirect reports the variable and new location only.'
            }

            $linkPath = Join-Path $fixture ('system-drive-link-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Junction -Path $linkPath -Target $systemRoot | Out-Null
            try {
                $env:TMP = Join-Path $linkPath 'Windows\Temp'
                $redirected = Initialize-AstralProjectEnvironment -RepositoryRoot $fixture -WarningAction SilentlyContinue
                Assert-Equal $redirected.TMP $tempPath 'Windows junctions cannot route cache output to the system drive'
            }
            finally {
                [IO.Directory]::Delete($linkPath)
            }
        }
    }
}
finally {
    foreach ($name in $managed) {
        [Environment]::SetEnvironmentVariable($name, $before[$name], 'Process')
    }
}
Write-Output 'Project cache environment tests passed.'
