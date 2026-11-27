# State receipts alone never authorize terminating a process. Match the complete live identity.
function Assert-R44ObservedHostIdentity($Observed,$Expected) {
    $state=$Observed.state
    if(!$state -or !$Observed.pid -or $state.pid -ne $Observed.pid -or
       ($Expected.pid -and $Observed.pid -ne $Expected.pid)){throw 'R44_HOST_STATE_PID_MISMATCH'}
    if(!$Observed.startTicks -or [string]$state.startTicks -cne [string]$Observed.startTicks -or
       ($Expected.startTicks -and [string]$Expected.startTicks -cne [string]$Observed.startTicks)){throw 'R44_HOST_START_TIME_MISMATCH'}
    if($state.address -cne '127.0.0.4' -or $state.port -ne 7728 -or
       $state.directory -ine $Expected.directory -or $state.serverSha256 -ine $Expected.serverSha256){throw 'R44_HOST_STATE_TARGET_MISMATCH'}
    if($Observed.executable -ine $Expected.executable){throw 'R44_HOST_EXECUTABLE_MISMATCH'}
    if($Observed.dllSha256 -ine $Expected.serverSha256){throw 'R44_HOST_DLL_HASH_MISMATCH'}
    $exe='(?:"'+[regex]::Escape($Expected.executable)+'"|'+[regex]::Escape($Expected.executable)+')'
    $dll='(?:"'+[regex]::Escape($Expected.dll)+'"|'+[regex]::Escape($Expected.dll)+')'
    if(!$Observed.commandLine -or $Observed.commandLine -inotmatch ('^\s*'+$exe+'\s+'+$dll+'\s*$')){throw 'R44_HOST_COMPLETE_COMMAND_MISMATCH'}
    [pscustomobject]@{pid=[int]$Observed.pid;startTicks=[string]$Observed.startTicks;directory=$Expected.directory;
        executable=$Expected.executable;dll=$Expected.dll;serverSha256=$Expected.serverSha256;address='127.0.0.4';port=7728}
}

function Get-R44OwnedHost($Context,[int]$Owner,[switch]$Old) {
    $description=if($Old){$Context.Manifest.oldHost}else{$Context.Manifest.newHost}
    $directory=[IO.Path]::GetFullPath($description.directory)
    $dll=Join-Path $directory 'AstralOath.Server.dll'
    $statePath=if($Old){Join-Path $Context.Repository '.artifacts/windows-r38/server/qa-host.json'}else{Join-Path $Context.Output 'qa-host.json'}
    foreach($path in @($dll,$Context.Dotnet,$statePath)){Assert-R44SafePath $Context.Repository $path}
    if(!(Test-Path -LiteralPath $statePath)){throw 'R44_UNKNOWN_LISTENER_REFUSED'}
    $state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
    if($state.pid -ne $Owner){throw 'R44_UNKNOWN_LISTENER_REFUSED'}
    $expected=[pscustomobject]@{pid=if($Old){$description.pid}else{0};startTicks=if($Old){$description.startTicks}else{$null};
        directory=$directory;executable=$Context.Dotnet;dll=$dll;serverSha256=$description.sha256;address='127.0.0.4';port=7728}
    $process=Get-Process -Id $Owner -ErrorAction Stop
    try {
        $null=$process.Handle
        try{$native=Get-CimInstance Win32_Process -Filter "ProcessId=$Owner" -ErrorAction Stop}
        catch{throw 'R44_PROCESS_IDENTITY_UNREADABLE_USE_INTERACTIVE_PREFLIGHT'}
        if(!$native){throw 'R44_UNKNOWN_LISTENER_REFUSED'}
        $observed=[pscustomobject]@{pid=$Owner;startTicks=[string]$process.StartTime.ToUniversalTime().Ticks;
            executable=$native.ExecutablePath;commandLine=$native.CommandLine;dllSha256=(Get-FileHash -LiteralPath $dll).Hash;state=$state}
        $identity=Assert-R44ObservedHostIdentity $observed $expected
        if((Find-R38Listener '127.0.0.4' 7728) -ne $Owner -or $process.HasExited){throw 'R44_OWNER_CHANGED_DURING_VERIFICATION'}
        [pscustomobject]@{Identity=$identity;Process=$process}
    }catch{$process.Dispose();throw}
}

function Stop-R44OwnedHost($Context,$Expected,[switch]$Old) {
    $current=Get-R44OwnedHost $Context $Expected.pid -Old:$Old
    try {
        if($current.Identity.pid -ne $Expected.pid -or $current.Identity.startTicks -cne $Expected.startTicks -or
           $current.Identity.serverSha256 -ine $Expected.serverSha256){throw 'R44_OWNER_CHANGED_BEFORE_STOP'}
        $current.Process.Kill()
        if(!$current.Process.WaitForExit(10000)){throw 'R44_EXACT_HOST_STOP_TIMEOUT'}
        if(Find-R38Listener '127.0.0.4' 7728){throw 'R44_ENDPOINT_OCCUPIED_AFTER_OWNED_EXIT'}
    }finally{$current.Process.Dispose()}
}
