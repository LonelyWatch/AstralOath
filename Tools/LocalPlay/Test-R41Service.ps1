[CmdletBinding()]
param([switch]$LegacyPolicy)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$r38=Join-Path $repository '.artifacts/windows-r38/server'

if($LegacyPolicy){
    # Exercise the actual old ownership branch, without executing SQL, starting or stopping anything.
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $r38 'ensure-host.ps1'),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'LEGACY_SOURCE_PARSE_FAILED'}
    $guard=$ast.Find({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and
        $node.Extent.Text.StartsWith('if($owner)') -and $node.Extent.Text.Contains('R38_RUNTIME_REFUSED_UNOWNED_4_BEFORE_MYSQL')},$true)
    if(!$guard){throw 'LEGACY_GUARD_NOT_FOUND'}
    $check=[scriptblock]::Create('param($owner,$PSScriptRoot)'+[Environment]::NewLine+$guard.Extent.Text)
    try{& $check 35692 $r38}
    catch{throw "EXPECTED_RED_VALID_R36_REJECTED_BY_ACTUAL_OLD_GUARD: $($_.Exception.Message)"}
    throw 'LEGACY_RED_NOT_REPRODUCED'
}

. (Join-Path $PSScriptRoot 'R41Service.Identity.ps1')
. (Join-Path $PSScriptRoot 'Ensure-R41GameService.ps1')
$actualStop=${function:Stop-R41R36Host}
$script:checks=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "FAILED: $Name"};$script:checks++}
function Denied($Fixture,[string]$Code,[string]$Name){
    try{$null=Assert-R41R36Identity $Fixture $repository;throw "NOT_REJECTED: $Name"}
    catch{if($_.Exception.Message -ne $Code){throw "WRONG_REJECTION $Name : $($_.Exception.Message)"};$script:checks++}
}
function Fixture {
    [pscustomobject]@{pid=35692;startTicks='639313774413789114';
        executable=(Join-Path $repository '.tools/dotnet/dotnet.exe');
        commandLine=('"'+(Join-Path $repository '.tools/dotnet/dotnet.exe')+'" "'+(Join-Path $repository '.artifacts/windows-r36/server/host/AstralOath.Server.dll')+'"');
        dllSha256='1F2901919517DC94AFA89105997E222527893B2B273D59BA3110C30453CB2E63';
        state=[pscustomobject]@{pid=35692;startTicks='639313774413789114';address='127.0.0.4';port=7728;
            directory=(Join-Path $repository '.artifacts/windows-r36/server/host')}}
}
$f=Fixture;$identity=Assert-R41R36Identity $f $repository
Check ($identity.pid -eq 35692) 'valid owned R36 accepted'
$f=Fixture;$f.pid=35693;Denied $f 'R41_R36_STATE_PID_MISMATCH' 'different listener PID'
$f=Fixture;$f.state.startTicks='639313774413789115';Denied $f 'R41_R36_START_TIME_MISMATCH' 'reused PID'
$f=Fixture;$f.state.directory=Join-Path $repository '.artifacts/windows-r35/server/host';Denied $f 'R41_R36_STATE_TARGET_MISMATCH' 'other release path'
$f=Fixture;$f.state.address='127.0.0.6';Denied $f 'R41_R36_STATE_TARGET_MISMATCH' 'other endpoint'
$f=Fixture;$f.executable='D:\unknown\dotnet.exe';Denied $f 'R41_R36_EXECUTABLE_MISMATCH' 'foreign executable'
$f=Fixture;$f.dllSha256='0'*64;Denied $f 'R41_R36_DLL_HASH_MISMATCH' 'changed DLL'
$f=Fixture;$f.commandLine+=' --extra';Denied $f 'R41_R36_COMPLETE_COMMAND_MISMATCH' 'extra argument'
$f=Fixture;$f.commandLine=$f.commandLine.Substring(1);Denied $f 'R41_R36_COMPLETE_COMMAND_MISMATCH' 'unbalanced quoting'
$f=Fixture;$f.commandLine=$f.commandLine.Replace('windows-r36','windows-r35');Denied $f 'R41_R36_COMPLETE_COMMAND_MISMATCH' 'foreign DLL argv'
$f=Fixture;$f.state.pid=0;Denied $f 'R41_R36_STATE_PID_MISMATCH' 'unknown listener'
$f=Fixture;$f.commandLine=$f.commandLine.Replace('"','');$null=Assert-R41R36Identity $f $repository
Check $true 'complete unquoted command supported'

# Replace only OS/SQL boundaries. The real transition function and identity policy execute offline.
$script:events=[Collections.Generic.List[string]]::new()
$script:owner=35692;$script:route='r36';$script:failPreflight=$false;$script:changed=$false;$script:failStart=$false;$script:changeAfterPreflight=$false
function Find-R38Listener {param($Address,$Port) return $script:owner}
function Test-R41R38Owner {param($Directory,$Owner) return $script:route -eq 'r38'}
function Get-R41R36Owner {param($Repository,$Owner) $script:events.Add('identity');if($script:changed){throw 'R41_R36_START_TIME_MISMATCH'};return [pscustomobject]@{Identity=(Assert-R41R36Identity (Fixture) $repository)}}
function Get-R38RuntimeContext {$script:events.Add('context');return [pscustomobject]@{NewDirectory='offline';Manifest=[pscustomobject]@{newHost=[pscustomobject]@{files=@()}}}}
function Assert-R38Tree {param($Directory,$Files) $script:events.Add('tree');if($script:failPreflight){throw 'OFFLINE_PREFLIGHT_FAILED'}}
function Read-R38RuntimeActivation {param($Context) $script:events.Add('activation')}
function Ensure-R38RuntimeMySql {param($Context,[switch]$VerifyOnly,$BootId) if(!$VerifyOnly){throw 'MYSQL_MUTATION_FORBIDDEN'};$script:events.Add('mysql-verify')}
function Get-R38MigrationState {param($Context,[switch]$RequireApplied) if(!$RequireApplied){throw 'MIGRATION_NOT_REQUIRED'};$script:events.Add('migration-017');if($script:changeAfterPreflight){$script:changed=$true}}
function Stop-R41R36Host {param($Repository,$Expected) $script:events.Add('stop-recheck');$null=Get-R41R36Owner $Repository $Expected.pid;$script:events.Add('stop-exact-r36');$script:owner=$null}
function Invoke-R41ExistingHost {param($Directory,[switch]$VerifyOnly) $script:events.Add("existing:$([bool]$VerifyOnly)");if($script:failStart){throw 'OFFLINE_NEW_START_FAILED'};return '{"status":"r38_ready"}'}
function Reset { $script:events.Clear();$script:owner=35692;$script:route='r36';$script:failPreflight=$false;$script:changed=$false;$script:failStart=$false;$script:changeAfterPreflight=$false }
Reset;$result=Invoke-R41ServiceCore $repository $r38 -VerifyOnly
Check (($result|ConvertFrom-Json).status -eq 'r41_switch_required') 'verify reports switch required, not ready'
Check (($script:events -join ',') -eq 'identity,context,tree,activation,mysql-verify,migration-017') 'verify never starts/stops or writes'
Reset;$null=Invoke-R41ServiceCore $repository $r38
Check (($script:events -join ',') -eq 'identity,context,tree,activation,mysql-verify,migration-017,stop-recheck,identity,stop-exact-r36,existing:False') 'preflight before exact stop then existing ensure'
Reset;$script:failPreflight=$true
try{$null=Invoke-R41ServiceCore $repository $r38;throw 'EXPECTED_PREFLIGHT_FAILURE'}catch{Check ($_.Exception.Message -eq 'OFFLINE_PREFLIGHT_FAILED') 'preflight error preserved'}
Check (!$script:events.Contains('stop-recheck')) 'preflight failure leaves host untouched'
Reset;$script:changeAfterPreflight=$true
try{$null=Invoke-R41ServiceCore $repository $r38;throw 'EXPECTED_IDENTITY_FAILURE'}catch{Check ($_.Exception.Message -eq 'R41_R36_START_TIME_MISMATCH') 'changed identity rejected'}
Check (!$script:events.Contains('stop-exact-r36')) 'changed process never stopped'
Reset;$script:owner=$null;$null=Invoke-R41ServiceCore $repository $r38 -VerifyOnly
Check (($script:events -join ',') -eq 'existing:True') 'no owner delegates unchanged verify policy'
Reset;$script:route='r38';$null=Invoke-R41ServiceCore $repository $r38
Check (($script:events -join ',') -eq 'existing:False') 'R38 owner delegates unchanged strict guard'
Reset;$script:failStart=$true
try{$null=Invoke-R41ServiceCore $repository $r38;throw 'EXPECTED_START_FAILURE'}catch{Check ($_.Exception.Message -eq 'OFFLINE_NEW_START_FAILED') 'start failure stays failure'}
Check (!$script:events.Contains('restore-database')) 'failure does not restore database'

# Execute the real stop boundary against an inert process object, including handle cleanup on failure.
Set-Item -Path function:Stop-R41R36Host -Value $actualStop
$script:kills=0;$script:disposals=0;$script:exitSucceeded=$true;$script:foreignAfterExit=$false;$script:replaceTicks=$false
$fakeProcess=[pscustomobject]@{}
$fakeProcess|Add-Member ScriptMethod Kill {$script:kills++;if($script:foreignAfterExit){$script:owner=99999}else{$script:owner=$null}}
$fakeProcess|Add-Member ScriptMethod WaitForExit {param($Milliseconds) if($Milliseconds -ne 10000){throw 'STOP_TIMEOUT_NOT_BOUNDED'};return $script:exitSucceeded}
$fakeProcess|Add-Member ScriptMethod Dispose {$script:disposals++}
function Get-R41R36Owner {param($Repository,$Owner)
    $identity=Assert-R41R36Identity (Fixture) $repository
    if($script:replaceTicks){$identity.startTicks='639313774413789115'}
    return [pscustomobject]@{Identity=$identity;Process=$fakeProcess}
}
$expected=Assert-R41R36Identity (Fixture) $repository
Stop-R41R36Host $repository $expected
Check ($script:kills -eq 1 -and $script:disposals -eq 1) 'actual stop closes verified process handle'
$script:replaceTicks=$true
try{Stop-R41R36Host $repository $expected;throw 'EXPECTED_REUSED_PID_REJECTION'}catch{Check ($_.Exception.Message -eq 'R41_R36_OWNER_CHANGED_BEFORE_STOP') 'actual stop rejects replacement ticks'}
Check ($script:kills -eq 1 -and $script:disposals -eq 2) 'replacement not killed and handle closed'
$script:replaceTicks=$false;$script:exitSucceeded=$false
try{Stop-R41R36Host $repository $expected;throw 'EXPECTED_STOP_TIMEOUT'}catch{Check ($_.Exception.Message -eq 'R41_R36_STOP_TIMEOUT_NO_OTHER_PROCESS_STOPPED') 'actual stop has bounded timeout'}
Check ($script:kills -eq 2 -and $script:disposals -eq 3) 'timeout closes handle without stopping another process'
$script:exitSucceeded=$true;$script:foreignAfterExit=$true
try{Stop-R41R36Host $repository $expected;throw 'EXPECTED_PORT_RACE_REJECTION'}catch{Check ($_.Exception.Message -eq 'R41_PORT_OCCUPIED_AFTER_OWNED_R36_EXIT') 'foreign listener after exit rejected'}
Check ($script:kills -eq 3 -and $script:owner -eq 99999 -and $script:disposals -eq 4) 'foreign listener survives and handle closed'
[ordered]@{status='R41_SERVICE_OFFLINE_PASS';checks=$script:checks;realServicesTouched=$false}|ConvertTo-Json -Compress
