# Offline safety contract only: never inspect listeners, run SQL, start or stop a real process.
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'R44Service.Identity.ps1')
. (Join-Path $PSScriptRoot 'R44Service.Common.ps1')
$checks=[Collections.Generic.List[object]]::new()
function Test-Case([string]$Name,[scriptblock]$Work){try{&$Work;$checks.Add(@{name=$Name;passed=$true})}catch{$checks.Add(@{name=$Name;passed=$false;code=$_.Exception.Message})}}
function Equal($Actual,$Expected){if($Actual -cne $Expected){throw 'OFFLINE_EXPECTATION_MISMATCH'}}
function Reject([scriptblock]$Work,[string]$Code){try{&$Work|Out-Null}catch{if($_.Exception.Message -ceq $Code){return};throw};throw 'OFFLINE_EXPECTED_REFUSAL'}
Test-Case 'legacy_primitives_survive_common_import_scope' {
 foreach($name in @('Assert-R38Tree','Ensure-R38RuntimeMySql','Read-R38RuntimeConnection','Read-R38MigrationConnection','New-R38SqlBackup','Start-R38OwnedHost')){
  if(!(Get-Command $name -CommandType Function -ErrorAction SilentlyContinue)){throw 'R44_REUSED_PRIMITIVE_NOT_IMPORTED'}
 }
}
$expected=[pscustomobject]@{pid=12;startTicks='123';directory='D:\fixture\host';executable='D:\fixture\dotnet.exe';dll='D:\fixture\host\AstralOath.Server.dll';serverSha256=('a'*64);address='127.0.0.4';port=7728}
function Observed { [pscustomobject]@{pid=12;startTicks='123';executable='D:\fixture\dotnet.exe';commandLine='"D:\fixture\dotnet.exe" "D:\fixture\host\AstralOath.Server.dll"';dllSha256=('a'*64);state=[pscustomobject]@{pid=12;startTicks='123';directory='D:\fixture\host';serverSha256=('a'*64);address='127.0.0.4';port=7728}} }
Test-Case 'exact_old_owner_accepted' { $null=Assert-R44ObservedHostIdentity (Observed) $expected }
Test-Case 'pid_reuse_refused' {$o=Observed;$o.startTicks='124';Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_START_TIME_MISMATCH'}
Test-Case 'unmatched_state_pid_refused' {$o=Observed;$o.state.pid=13;Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_STATE_PID_MISMATCH'}
Test-Case 'changed_executable_refused' {$o=Observed;$o.executable='D:\other\dotnet.exe';Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_EXECUTABLE_MISMATCH'}
Test-Case 'changed_dll_hash_refused' {$o=Observed;$o.dllSha256=('b'*64);Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_DLL_HASH_MISMATCH'}
Test-Case 'extra_command_argument_refused' {$o=Observed;$o.commandLine+=' --urls=http://0.0.0.0';Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_COMPLETE_COMMAND_MISMATCH'}
Test-Case 'unbalanced_command_quotes_refused' {$o=Observed;$o.commandLine='"D:\fixture\dotnet.exe D:\fixture\host\AstralOath.Server.dll';Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_COMPLETE_COMMAND_MISMATCH'}
Test-Case 'wrong_state_directory_refused' {$o=Observed;$o.state.directory='D:\other';Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_STATE_TARGET_MISMATCH'}
Test-Case 'wrong_endpoint_refused' {$o=Observed;$o.state.address='127.0.0.6';Reject {Assert-R44ObservedHostIdentity $o $expected} 'R44_HOST_STATE_TARGET_MISMATCH'}
Test-Case 'restore_database_main_refused' {Reject {Assert-R44RestoreDatabase 'astraloath_test'} 'R44_RESTORE_TARGET_REFUSED'}
Test-Case 'restore_database_other_schema_refused' {Reject {Assert-R44RestoreDatabase 'other_database'} 'R44_RESTORE_TARGET_REFUSED'}
Test-Case 'restore_database_isolated_accepted' {Assert-R44RestoreDatabase ('astraloath_restore_qa_r44_'+('a'*32))}
Test-Case 'dump_use_redirected_only_to_isolated_schema' {
 $target='astraloath_restore_qa_r44_'+('a'*32)
 $line=Convert-R44RestoreLine 'USE `astraloath_test`;' $target
 Equal $line ('USE '+[char]96+$target+[char]96+';')
 Equal (Convert-R44RestoreLine 'CREATE DATABASE /*!32312 IF NOT EXISTS*/ `astraloath_test` /*!40100 DEFAULT CHARACTER SET utf8mb4 */;' $target) $null
 Equal (Convert-R44RestoreLine "INSERT INTO x VALUES ('astraloath_test');" $target) "INSERT INTO x VALUES ('astraloath_test');"
}
Test-Case 'dump_unknown_schema_use_refused' {Reject {Convert-R44RestoreLine 'USE `other`;' ('astraloath_restore_qa_r44_'+('a'*32))} 'R44_DUMP_DATABASE_DIRECTIVE_REFUSED'}
Test-Case 'dump_drop_database_refused' {Reject {Convert-R44RestoreLine 'DROP DATABASE `astraloath_test`;' ('astraloath_restore_qa_r44_'+('a'*32))} 'R44_DUMP_DATABASE_DIRECTIVE_REFUSED'}
Test-Case 'dump_schema_qualified_write_refused' {Reject {Convert-R44RestoreLine 'ALTER TABLE `astraloath_test`.`x` ADD COLUMN y INT;' ('astraloath_restore_qa_r44_'+('a'*32))} 'R44_DUMP_MAIN_SCHEMA_REFERENCE_REFUSED'}

# Exercise state-machine ordering with all external operations replaced by strict fakes.
$script:ops=[Collections.Generic.List[string]]::new();$script:failAt='';$script:known=$true;$script:oldStopped=$false
function Op([string]$Name){$script:ops.Add($Name);if($script:failAt -ceq $Name){throw ('FAKE_'+$Name)}}
function Assert-R44Candidate($Context,[switch]$Sources){Op 'candidate'}
function Get-R44OwnedHost($Context,[int]$Owner,[switch]$Old){Op 'owner';if(!$script:known){throw 'R44_UNKNOWN_LISTENER_REFUSED'};[pscustomobject]@{Identity=$expected;Process=$null}}
function Find-R38Listener([string]$Address,[int]$Port){if(!$script:oldStopped){12}}
function Ensure-R44MySql($Context,[switch]$VerifyOnly,[string]$BootId){Op 'mysql';[pscustomobject]@{pid=22;startTicks='321'}}
function Get-R44MigrationState($Context,[switch]$RequireApplied){Op $(if($RequireApplied){'schema_after'}else{'schema_before'});@{applied=$RequireApplied.IsPresent}}
function Assert-R44RuntimePrivileges($Context){Op 'privileges'}
function Assert-R44Health([switch]$Old){Op 'health'}
function Assert-R44FreshPreflight($Context,[string]$Path){Op 'preflight'}
function Stop-R44OwnedHost($Context,$Expected,[switch]$Old){Op 'stop_old';$script:oldStopped=$true}
function New-R44Backup($Context,[string]$ResultId){Op 'backup';@{path='fake.sql';sha256=('a'*64)}}
function Test-R44BackupRestore($Context,$Backup,[string]$ResultId){Op 'isolated_restore';@{passed=$true}}
function Invoke-R44Migration($Context,[string]$ResultId){Op 'migrate'}
function Start-R44Host($Context,[string]$ResultId){Op 'start_new';@{pid=33}}
function Wait-R44Host($Context,$State){Op 'new_ready'}
function Complete-R44Activation($Context,$State,$Record){Op 'activate'}
function Get-R44Fingerprints($Context){@(@{path='offline-fixture';sha256=('a'*64)})}
$context=[pscustomobject]@{}
function Reset-Flow {$script:ops.Clear();$script:failAt='';$script:known=$true;$script:oldStopped=$false}
Test-Case 'readonly_preflight_never_stops_backups_restores_migrates_or_starts' {Reset-Flow;$null=Invoke-R44CutoverCore $context -ResultId ('a'*32);Equal ($script:ops -join ',') 'candidate,owner,mysql,schema_before,privileges,health'}
Test-Case 'unknown_listener_refuses_before_sql' {Reset-Flow;$script:known=$false;Reject {Invoke-R44CutoverCore $context -ResultId ('a'*32)} 'R44_UNKNOWN_LISTENER_REFUSED';Equal ($script:ops -join ',') 'candidate,owner'}
Test-Case 'cutover_orders_backup_and_isolated_restore_before_migration' {Reset-Flow;$null=Invoke-R44CutoverCore $context -Execute -PreflightReceipt fake -ResultId ('a'*32);Equal ($script:ops -join ',') 'candidate,owner,mysql,schema_before,privileges,health,preflight,candidate,stop_old,mysql,backup,isolated_restore,mysql,migrate,schema_after,start_new,new_ready,mysql,activate'}
Test-Case 'backup_failure_prevents_restore_migration_start' {Reset-Flow;$script:failAt='backup';Reject {Invoke-R44CutoverCore $context -Execute -PreflightReceipt fake -ResultId ('a'*32)} 'FAKE_backup';Equal ($script:ops -join ',') 'candidate,owner,mysql,schema_before,privileges,health,preflight,candidate,stop_old,mysql,backup'}
Test-Case 'isolated_restore_failure_prevents_migration_start' {Reset-Flow;$script:failAt='isolated_restore';Reject {Invoke-R44CutoverCore $context -Execute -PreflightReceipt fake -ResultId ('a'*32)} 'FAKE_isolated_restore';Equal ($script:ops.Contains('migrate')) $false}
Test-Case 'migration_failure_never_restores_main_database_or_starts_host' {Reset-Flow;$script:failAt='migrate';Reject {Invoke-R44CutoverCore $context -Execute -PreflightReceipt fake -ResultId ('a'*32)} 'FAKE_migrate';Equal ($script:ops.Contains('start_new')) $false;Equal $script:oldStopped $true}
Test-Case 'missing_preflight_refused_before_stop' {Reset-Flow;Reject {Invoke-R44CutoverCore $context -Execute -ResultId ('a'*32)} 'R44_EXECUTE_REQUIRES_PASSED_PREFLIGHT';Equal ($script:ops.Contains('stop_old')) $false}
$out=Join-Path $repository '.artifacts/home-profile-chat-review/server/r44-offline-tests.json'
@{status=if(@($checks|Where-Object {!$_.passed}).Count){'failed'}else{'passed'};checks=@($checks);processesStarted=$false;processesStopped=$false;sqlExecuted=$false}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $out -Encoding utf8
Write-Output ($checks|ConvertTo-Json -Depth 6 -Compress)
if(@($checks|Where-Object {!$_.passed}).Count){exit 1}
