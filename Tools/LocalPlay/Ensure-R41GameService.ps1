[CmdletBinding()]
param([switch]$VerifyOnly)

function Test-R41R38Owner([string]$Directory,[int]$Owner) {
    $path=Join-Path $Directory 'qa-host.json'
    if(!(Test-Path -LiteralPath $path)){return $false}
    $state=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    # This is routing only. The unchanged R38 helper validates every ownership field again.
    return $state.pid -eq $Owner
}

function Invoke-R41ExistingHost([string]$Directory,[switch]$VerifyOnly) {
    & (Join-Path $Directory 'ensure-host.ps1') -VerifyOnly:$VerifyOnly
}

function Invoke-R41ServiceCore([string]$Repository,[string]$R38Directory,[switch]$VerifyOnly) {
    $owner=Find-R38Listener '127.0.0.4' 7728
    if(!$owner -or (Test-R41R38Owner $R38Directory $owner)){
        return Invoke-R41ExistingHost $R38Directory -VerifyOnly:$VerifyOnly
    }
    $old=Get-R41R36Owner $Repository $owner
    try{
        $context=Get-R38RuntimeContext
        Assert-R38Tree $context.NewDirectory $context.Manifest.newHost.files
        $null=Read-R38RuntimeActivation $context
        # A historical Host is never retired to bootstrap or migrate the database.
        $null=Ensure-R38RuntimeMySql $context -VerifyOnly -BootId 'r41-preflight'
        $null=Get-R38MigrationState $context -RequireApplied
        if($VerifyOnly){
            return [ordered]@{status='r41_switch_required';code='R41_R36_OWNED_PREFLIGHT_OK';
                oldRelease='R36';oldPid=$old.Identity.pid;targetRelease='R38';
                verifyOnly=$true;hostStopped=$false;mysqlStopped=$false;databaseRestored=$false}|ConvertTo-Json -Compress
        }
        Stop-R41R36Host $Repository $old.Identity
        # Same thread: the existing helper re-enters the shared named mutex and owns new-child cleanup.
        $ready=@(Invoke-R41ExistingHost $R38Directory)
        return [ordered]@{status='r41_ready';code='R41_OWNED_R36_REPLACED';oldRelease='R36';oldPid=$old.Identity.pid;
            targetRelease='R38';service=($ready -join [Environment]::NewLine|ConvertFrom-Json);
            mysqlStopped=$false;databaseRestored=$false;verifyOnly=$false}|ConvertTo-Json -Depth 8 -Compress
    }finally{if($old.Process){$old.Process.Dispose()}}
}

function Invoke-R41GameService([switch]$VerifyOnly) {
    $ErrorActionPreference='Stop'
    $repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $r38=Join-Path $repository '.artifacts/windows-r38/server'
    . (Join-Path $r38 'Cutover.Common.ps1')
    . (Join-Path $r38 'Runtime.Common.ps1')
    . (Join-Path $PSScriptRoot 'R41Service.Identity.ps1')
    $mutex=[Threading.Mutex]::new($false,'Local\AstralOath.EquipmentQA.127-0-0-4.7728')
    $held=$false
    try{
        try{$held=$mutex.WaitOne([TimeSpan]::FromSeconds(55))}catch [Threading.AbandonedMutexException]{$held=$true}
        if(!$held){throw 'R41_STARTUP_MUTEX_TIMEOUT'}
        Invoke-R41ServiceCore $repository $r38 -VerifyOnly:$VerifyOnly
    }finally{
        if($held){$mutex.ReleaseMutex()}
        $mutex.Dispose()
    }
}

if($MyInvocation.InvocationName -ne '.') {Invoke-R41GameService -VerifyOnly:$VerifyOnly}
