# Keep R44's complete command/PID/start/DLL identity rules; only state locations differ.
function Get-R47OwnedHost($Context,[int]$Owner,[switch]$Old){
    $description=if($Old){$Context.Manifest.oldHost}else{$Context.Manifest.newHost}
    $directory=[IO.Path]::GetFullPath($description.directory);$dll=Join-Path $directory 'AstralOath.Server.dll'
    $statePath=if($Old){Join-Path $Context.Repository $Context.Manifest.oldHost.statePath}else{Join-Path $Context.Output 'qa-host.json'}
    foreach($path in @($dll,$Context.Dotnet,$statePath)){Assert-R44SafePath $Context.Repository $path}
    if(!(Test-Path -LiteralPath $statePath)){throw 'R47_UNKNOWN_LISTENER_REFUSED'}
    $state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
    $expected=[pscustomobject]@{pid=if($Old){$description.pid}else{0};startTicks=if($Old){$description.startTicks}else{$null};directory=$directory;executable=$Context.Dotnet;dll=$dll;serverSha256=$description.sha256}
    $process=Get-Process -Id $Owner -ErrorAction Stop
    try{
        $null=$process.Handle;$native=Get-CimInstance Win32_Process -Filter "ProcessId=$Owner" -ErrorAction Stop
        $observed=[pscustomobject]@{pid=$Owner;startTicks=[string]$process.StartTime.ToUniversalTime().Ticks;executable=$native.ExecutablePath;commandLine=$native.CommandLine;dllSha256=(Get-FileHash -LiteralPath $dll).Hash;state=$state}
        $identity=Assert-R44ObservedHostIdentity $observed $expected
        if((Find-R38Listener '127.0.0.4' 7728) -ne $Owner -or $process.HasExited){throw 'R47_OWNER_CHANGED_DURING_VERIFICATION'}
        [pscustomobject]@{Identity=$identity;Process=$process}
    }catch{$process.Dispose();throw}
}
function Stop-R47OwnedHost($Context,$Expected){
    $current=Get-R47OwnedHost $Context $Expected.pid -Old
    try{
        if($current.Identity.startTicks -cne $Expected.startTicks -or $current.Identity.serverSha256 -ine $Expected.serverSha256){throw 'R47_OWNER_CHANGED_BEFORE_STOP'}
        $current.Process.Kill();if(!$current.Process.WaitForExit(10000)){throw 'R47_EXACT_HOST_STOP_TIMEOUT'}
        if(Find-R38Listener '127.0.0.4' 7728){throw 'R47_ENDPOINT_OCCUPIED_AFTER_OWNED_EXIT'}
    }finally{$current.Process.Dispose()}
}
