# R41 may retire only the published R36 development Host. State is necessary, never sufficient.
function Assert-R41R36Identity($Observed,[string]$Repository) {
    $directory=[IO.Path]::GetFullPath((Join-Path $Repository '.artifacts/windows-r36/server/host'))
    $executable=[IO.Path]::GetFullPath((Join-Path $Repository '.tools/dotnet/dotnet.exe'))
    $dll=Join-Path $directory 'AstralOath.Server.dll'
    $state=$Observed.state
    if(!$state -or !$Observed.pid -or $state.pid -ne $Observed.pid){throw 'R41_R36_STATE_PID_MISMATCH'}
    if(!$Observed.startTicks -or [string]$state.startTicks -cne [string]$Observed.startTicks){throw 'R41_R36_START_TIME_MISMATCH'}
    if($state.address -cne '127.0.0.4' -or $state.port -ne 7728 -or $state.directory -ine $directory){throw 'R41_R36_STATE_TARGET_MISMATCH'}
    if($Observed.executable -ine $executable){throw 'R41_R36_EXECUTABLE_MISMATCH'}
    if($Observed.dllSha256 -ine '1F2901919517DC94AFA89105997E222527893B2B273D59BA3110C30453CB2E63'){throw 'R41_R36_DLL_HASH_MISMATCH'}
    # Match the entire command, with balanced quoting and no extra options or arguments.
    $exePattern='(?:"'+[regex]::Escape($executable)+'"|'+[regex]::Escape($executable)+')'
    $dllPattern='(?:"'+[regex]::Escape($dll)+'"|'+[regex]::Escape($dll)+')'
    if($Observed.commandLine -inotmatch ('^\s*'+$exePattern+'\s+'+$dllPattern+'\s*$')){throw 'R41_R36_COMPLETE_COMMAND_MISMATCH'}
    [pscustomobject]@{pid=[int]$Observed.pid;startTicks=[string]$Observed.startTicks;directory=$directory;
        executable=$executable;dll=$dll;serverSha256=$Observed.dllSha256;address='127.0.0.4';port=7728}
}

function Get-R41R36Owner([string]$Repository,[int]$Owner) {
    $statePath=Join-Path $Repository '.artifacts/windows-r36/server/qa-host.json'
    $dll=Join-Path $Repository '.artifacts/windows-r36/server/host/AstralOath.Server.dll'
    foreach($path in @((Join-Path $Repository '.artifacts'),(Join-Path $Repository '.artifacts/windows-r36'),
        (Split-Path $statePath),(Split-Path $dll),$statePath,$dll,(Join-Path $Repository '.tools'),
        (Join-Path $Repository '.tools/dotnet'),(Join-Path $Repository '.tools/dotnet/dotnet.exe'))){
        if(!(Test-Path -LiteralPath $path)){throw 'R41_SERVICE_UNOWNED_LISTENER'}
        if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'R41_R36_REPARSE_REFUSED'}
    }
    $state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
    if($state.pid -ne $Owner){throw 'R41_SERVICE_UNOWNED_LISTENER'}
    $process=Get-Process -Id $Owner -ErrorAction Stop
    try {
        # Keep the native process handle open so a later PID reuse cannot turn this into another target.
        $null=$process.Handle
        try{$native=Get-CimInstance Win32_Process -Filter "ProcessId=$Owner" -ErrorAction Stop}
        catch{throw 'R41_PROCESS_IDENTITY_UNREADABLE_USE_INTERACTIVE_PREFLIGHT'}
        if(!$native){throw 'R41_SERVICE_UNOWNED_LISTENER'}
        $observed=[pscustomobject]@{pid=$Owner;startTicks=[string]$process.StartTime.ToUniversalTime().Ticks;
            executable=$native.ExecutablePath;commandLine=$native.CommandLine;
            dllSha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash;state=$state}
        $identity=Assert-R41R36Identity $observed $Repository
        if((Find-R38Listener '127.0.0.4' 7728) -ne $Owner -or $process.HasExited){throw 'R41_R36_OWNER_CHANGED_DURING_VERIFICATION'}
        return [pscustomobject]@{Identity=$identity;Process=$process}
    } catch {$process.Dispose();throw}
}

function Stop-R41R36Host([string]$Repository,$Expected) {
    # Re-read state, command, ticks, disk hash and listener immediately before the only stop operation.
    $current=Get-R41R36Owner $Repository $Expected.pid
    try{
        if($current.Identity.pid -ne $Expected.pid -or $current.Identity.startTicks -cne $Expected.startTicks){throw 'R41_R36_OWNER_CHANGED_BEFORE_STOP'}
        $current.Process.Kill()
        if(!$current.Process.WaitForExit(10000)){throw 'R41_R36_STOP_TIMEOUT_NO_OTHER_PROCESS_STOPPED'}
        if(Find-R38Listener '127.0.0.4' 7728){throw 'R41_PORT_OCCUPIED_AFTER_OWNED_R36_EXIT'}
    }finally{$current.Process.Dispose()}
}
