function Resolve-ContentSourceFile([string]$Root, [string]$Path) {
    # Provenance records local file names, not portable payload names.
    if ([string]::IsNullOrEmpty($Path) -or [IO.Path]::IsPathRooted($Path)) {
        throw "Invalid source path '$Path'."
    }
    foreach ($segment in $Path.Split('/')) {
        if (-not $segment -or $segment -match '[\p{Cc}\\:*?"<>|]' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
            $segment -match '\A(?:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|(?:COM|LPT)[0-9\u00b9\u00b2\u00b3]) *(?:\.|\z)') {
            throw "Invalid source path '$Path'."
        }
    }

    $file = [IO.Path]::GetFullPath($Path, $Root)
    $relative = [IO.Path]::GetRelativePath($Root, $file)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -in '.','..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar)) {
        throw "Source path '$Path' is outside its source root."
    }

    # Check ancestors before touching a child, so a linked directory is never followed.
    $volume = [IO.Path]::GetPathRoot($file)
    $current = ''
    foreach ($segment in @($volume) + $file.Substring($volume.Length).Split([IO.Path]::DirectorySeparatorChar)) {
        $current = [IO.Path]::Combine($current, $segment)
        $attributes = [IO.File]::GetAttributes($current)
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "A symbolic link or reparse point is not allowed at '$current'."
        }
    }
    if ($attributes -band [IO.FileAttributes]::Directory) { throw "Expected a source file at '$file'." }
    return $file
}
