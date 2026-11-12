function Initialize-AstralProjectEnvironment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$RepositoryRoot
    )

    $windows = [IO.Path]::DirectorySeparatorChar -eq '\'
    $comparison = if ($windows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $systemDrive = if ($windows) { [IO.Path]::GetPathRoot([Environment]::SystemDirectory) }

    function Resolve-CacheDirectory([string]$Path, [int]$LinkDepth = 0) {
        if ([string]::IsNullOrWhiteSpace($Path) -or $LinkDepth -ge 40) {
            return $null
        }
        if ($windows) {
            # Windows PowerShell 5.1's IsPathRooted also accepts drive-relative paths.
            if ($Path -notmatch '^(?:[A-Za-z]:[\\/]|[\\/]{2}[^\\/]+[\\/][^\\/]+)' -or
                $Path -match '^[\\/]{2}[?.][\\/]') {
                return $null
            }
        }
        elseif (-not [IO.Path]::IsPathRooted($Path)) {
            return $null
        }

        try {
            $fullPath = [IO.Path]::GetFullPath($Path)
            $driveRoot = [IO.Path]::GetPathRoot($fullPath)
            if (-not [IO.Directory]::Exists($driveRoot) -or ($windows -and $fullPath.Substring($driveRoot.Length).Contains(':'))) {
                return $null
            }

            $parts = [Collections.Generic.Stack[string]]::new()
            $directory = [IO.DirectoryInfo]::new($fullPath)
            while ($null -ne $directory.Parent) {
                $parts.Push($directory.Name)
                $directory = $directory.Parent
            }
            while ($parts.Count -gt 0) {
                $directory = [IO.DirectoryInfo]::new([IO.Path]::Combine($directory.FullName, $parts.Pop()))
                if ([IO.File]::Exists($directory.FullName)) {
                    return $null
                }
                $attributes = $directory.Attributes
                if ($attributes -eq -1 -or -not ($attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    continue
                }

                # Target is provided by the filesystem provider in both PowerShell editions.
                $link = Get-Item -LiteralPath $directory.FullName -Force -ErrorAction SilentlyContinue
                $target = @($link.Target)[0]
                if (-not $target) {
                    return $null
                }
                if (-not [IO.Path]::IsPathRooted($target)) {
                    $target = [IO.Path]::Combine($directory.Parent.FullName, $target)
                }
                $resolved = Resolve-CacheDirectory $target ($LinkDepth + 1)
                if (-not $resolved) {
                    return $null
                }
                $directory = [IO.DirectoryInfo]::new($resolved)
            }
            return $directory.FullName
        }
        catch [ArgumentException], [IO.IOException], [UnauthorizedAccessException], [NotSupportedException] {
            return $null
        }
    }

    $repository = Resolve-CacheDirectory $RepositoryRoot
    if (-not $repository -or -not [IO.Directory]::Exists($repository)) {
        throw 'RepositoryRoot must name an existing absolute directory.'
    }
    $separators = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $repositoryPrefix = $repository.TrimEnd($separators) + [IO.Path]::DirectorySeparatorChar
    $avoidSystemDrive = $windows -and $systemDrive -and [IO.Path]::GetPathRoot($repository) -ine $systemDrive

    $defaults = [ordered]@{
        TEMP                  = '.artifacts/temp'
        TMP                   = '.artifacts/temp'
        TMPDIR                = '.artifacts/temp'
        UPM_CACHE_ROOT        = '.tools/unity-package-cache'
        NUGET_HTTP_CACHE_PATH = '.tools/nuget-http-cache'
        NUGET_PACKAGES        = '.tools/nuget'
        DOTNET_CLI_HOME       = '.artifacts/dotnet'
        GRADLE_USER_HOME      = '.tools/gradle'
        PIP_CACHE_DIR         = '.tools/pip-cache'
        PYTHONPYCACHEPREFIX   = '.artifacts/python-pycache'
        UNITY_MCP_LOG_DIR     = '.artifacts/mcp/logs'
        UV_CACHE_DIR          = '.tools/uv-cache'
        npm_config_cache      = '.tools/npm-cache'
    }
    $locations = [ordered]@{}
    $warnings = [Collections.Generic.List[string]]::new()
    foreach ($name in $defaults.Keys) {
        $value = [Environment]::GetEnvironmentVariable($name, 'Process')
        $candidate = Resolve-CacheDirectory $value
        $onSystemDrive = $candidate -and $avoidSystemDrive -and [IO.Path]::GetPathRoot($candidate) -ieq $systemDrive
        if ($candidate -and -not $onSystemDrive) {
            [IO.Directory]::CreateDirectory($candidate) | Out-Null
            $locations[$name] = $value
            continue
        }

        $destination = Resolve-CacheDirectory (Join-Path $repository $defaults[$name])
        if (-not $destination -or -not $destination.StartsWith($repositoryPrefix, $comparison)) {
            throw "The repository cache destination for $name resolves outside the repository or is not a directory."
        }
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        $locations[$name] = $destination
        if ($onSystemDrive) {
            $warnings.Add("$name points to the Windows system drive. Using $destination.")
        }
        elseif ($value) {
            $warnings.Add("$name does not name an absolute cache directory. Using $destination.")
        }
    }

    foreach ($name in $locations.Keys) {
        [Environment]::SetEnvironmentVariable($name, $locations[$name], 'Process')
    }
    foreach ($message in $warnings) {
        Write-Warning $message
    }
    [pscustomobject]$locations
}
