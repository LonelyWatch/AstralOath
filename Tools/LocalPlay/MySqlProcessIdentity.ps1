# Pure identity decisions are separate from CIM collection so offline fixtures never start MySQL.
# Windows MySQL 8.4 normally starts a monitor which creates the actual listening server child.
# https://dev.mysql.com/doc/refman/8.4/en/windows-installation.html
function ConvertFrom-AstralWindowsCommandLine([string]$CommandLine) {
    if([string]::IsNullOrWhiteSpace($CommandLine) -or $CommandLine.IndexOfAny([char[]]"`0`r`n") -ge 0){throw 'ASTRAL_MYSQL_COMMAND_LINE_INVALID'}
    $values=[Collections.Generic.List[string]]::new()
    $index=0
    while($index -lt $CommandLine.Length){
        while($index -lt $CommandLine.Length -and $CommandLine[$index] -in @(' ',"`t")){$index++}
        if($index -ge $CommandLine.Length){break}
        $value=[Text.StringBuilder]::new(); $quoted=$false
        while($index -lt $CommandLine.Length){
            $character=$CommandLine[$index]
            if(!$quoted -and $character -in @(' ',"`t")){break}
            if($character -eq '\'){
                $slashes=0
                while($index -lt $CommandLine.Length -and $CommandLine[$index] -eq '\'){$slashes++;$index++}
                if($index -lt $CommandLine.Length -and $CommandLine[$index] -eq '"'){
                    $null=$value.Append(('\' * [int][Math]::Floor($slashes/2)))
                    if($slashes % 2){$null=$value.Append('"');$index++;continue}
                    if($quoted -and $index+1 -lt $CommandLine.Length -and $CommandLine[$index+1] -eq '"'){
                        $null=$value.Append('"');$index+=2;continue
                    }
                    $quoted=!$quoted;$index++;continue
                }
                $null=$value.Append(('\' * $slashes));continue
            }
            if($character -eq '"'){
                if($quoted -and $index+1 -lt $CommandLine.Length -and $CommandLine[$index+1] -eq '"'){
                    $null=$value.Append('"');$index+=2;continue
                }
                $quoted=!$quoted;$index++;continue
            }
            $null=$value.Append($character);$index++
        }
        if($quoted){throw 'ASTRAL_MYSQL_COMMAND_LINE_INVALID'}
        $values.Add($value.ToString())
    }
    return $values.ToArray()
}

function Assert-AstralMySqlSnapshot($Snapshot,[string]$ExpectedExecutable,[string[]]$ExpectedArgv) {
    foreach($name in @('pid','parentPid','startTicks','executable','commandLine','running')){
        if($null -eq $Snapshot -or $null -eq $Snapshot.PSObject.Properties[$name]){throw 'ASTRAL_MYSQL_PROCESS_IDENTITY_INCOMPLETE'}
    }
    [long]$ticks=0;[int]$processId=0
    if(![int]::TryParse([string]$Snapshot.pid,[ref]$processId) -or $processId -le 0 -or
        ![long]::TryParse([string]$Snapshot.startTicks,[ref]$ticks) -or $ticks -le 0 -or
        [string]::IsNullOrWhiteSpace([string]$Snapshot.executable) -or [string]::IsNullOrWhiteSpace([string]$Snapshot.commandLine)){
        throw 'ASTRAL_MYSQL_PROCESS_IDENTITY_INCOMPLETE'
    }
    if($Snapshot.running -isnot [bool] -or !$Snapshot.running){throw 'ASTRAL_MYSQL_PROCESS_NOT_RUNNING'}
    if(![string]::Equals([string]$Snapshot.executable,$ExpectedExecutable,[StringComparison]::OrdinalIgnoreCase)){
        throw 'ASTRAL_MYSQL_PROCESS_EXECUTABLE_MISMATCH'
    }
    $actualArgv=@(ConvertFrom-AstralWindowsCommandLine ([string]$Snapshot.commandLine))
    if($actualArgv.Count -ne $ExpectedArgv.Count -or
        ![string]::Equals($actualArgv[0],$ExpectedArgv[0],[StringComparison]::OrdinalIgnoreCase)){
        throw 'ASTRAL_MYSQL_PROCESS_ARGUMENTS_MISMATCH'
    }
    for($index=1;$index -lt $ExpectedArgv.Count;$index++){
        if(![string]::Equals($actualArgv[$index],$ExpectedArgv[$index],[StringComparison]::Ordinal)){
            throw 'ASTRAL_MYSQL_PROCESS_ARGUMENTS_MISMATCH'
        }
    }
}

function Assert-AstralMySqlProcessIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$StartedMonitor,[Parameter(Mandatory)]$Monitor,[Parameter(Mandatory)]$Listener,
        [Parameter(Mandatory)][int]$ListenerOwner,[Parameter(Mandatory)][string]$ExpectedExecutable,
        [Parameter(Mandatory)][string]$ExpectedArguments)
    $expectedArgv=@(ConvertFrom-AstralWindowsCommandLine ('"'+$ExpectedExecutable+'" '+$ExpectedArguments))
    Assert-AstralMySqlSnapshot $StartedMonitor $ExpectedExecutable $expectedArgv
    Assert-AstralMySqlSnapshot $Monitor $ExpectedExecutable $expectedArgv
    Assert-AstralMySqlSnapshot $Listener $ExpectedExecutable $expectedArgv
    if([int]$Monitor.pid -ne [int]$StartedMonitor.pid -or [long]$Monitor.startTicks -ne [long]$StartedMonitor.startTicks){
        throw 'ASTRAL_MYSQL_MONITOR_IDENTITY_CHANGED'
    }
    if($ListenerOwner -le 0 -or [int]$Listener.pid -ne $ListenerOwner){throw 'ASTRAL_MYSQL_LISTENER_PID_MISMATCH'}
    if($ListenerOwner -eq [int]$StartedMonitor.pid){
        if([long]$Listener.startTicks -ne [long]$StartedMonitor.startTicks){throw 'ASTRAL_MYSQL_MONITOR_IDENTITY_CHANGED'}
        $ownership='started-process'
    }else{
        if([int]$Listener.parentPid -ne [int]$StartedMonitor.pid){throw 'ASTRAL_MYSQL_LISTENER_NOT_STARTED_CHILD'}
        if([long]$Listener.startTicks -le [long]$StartedMonitor.startTicks){throw 'ASTRAL_MYSQL_CHILD_CREATION_ORDER_MISMATCH'}
        $ownership='monitor-child'
    }
    [pscustomobject]@{ownership=$ownership;
        monitor=[pscustomobject]@{pid=[int]$Monitor.pid;startTicks=[string]$Monitor.startTicks;executable=$ExpectedExecutable};
        server=[pscustomobject]@{pid=$ListenerOwner;parentPid=[int]$Listener.parentPid;startTicks=[string]$Listener.startTicks;executable=$ExpectedExecutable}}
}

function Get-AstralMySqlProcessSnapshot([int]$Owner) {
    if($Owner -le 0){throw 'ASTRAL_MYSQL_PROCESS_IDENTITY_INCOMPLETE'}
    try {
        $process=Get-Process -Id $Owner -ErrorAction Stop
        $ticks=[string]$process.StartTime.ToUniversalTime().Ticks
        $native=Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$Owner" -ErrorAction Stop
        if($null -eq $native -or [int]$native.ProcessId -ne $Owner -or !$native.ExecutablePath -or !$native.CommandLine){
            throw 'ASTRAL_MYSQL_PROCESS_IDENTITY_UNREADABLE'
        }
        $process.Refresh()
        if($process.HasExited -or [string](Get-Process -Id $Owner -ErrorAction Stop).StartTime.ToUniversalTime().Ticks -ne $ticks){
            throw 'ASTRAL_MYSQL_PROCESS_CHANGED_DURING_SNAPSHOT'
        }
        [pscustomobject]@{pid=$Owner;parentPid=[int]$native.ParentProcessId;startTicks=$ticks;executable=[string]$native.ExecutablePath;
            commandLine=[string]$native.CommandLine;running=$true}
    }catch {
        # No raw CIM/command text or process environment is copied into diagnostics.
        if($_.Exception.Message -like 'ASTRAL_MYSQL_*'){throw}
        throw 'ASTRAL_MYSQL_PROCESS_IDENTITY_UNREADABLE'
    }
}
