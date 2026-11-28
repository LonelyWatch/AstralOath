# R47 only specializes reviewed R44/R38 primitives. Preparation never invokes this file.
# Explicit cutover backs up then runs the entire migrate CLI, including UID initialization.
function Get-R47Context{
    $repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $legacy=Get-R38RuntimeContext
    $output=Join-Path $repository '.artifacts/windows-r47/server';$legacy.Output=$output
    $path=Join-Path $repository '.artifacts/windows-r47/backend-release-manifest.json'
    Assert-R44SafePath $repository $path
    $manifest=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    $newDirectory=Join-Path $repository '.artifacts/windows-r47/backend'
    $oldDirectory=Join-Path $repository '.artifacts/windows-r44/server/host'
    if($manifest.schemaVersion -ne 1 -or $manifest.release -cne 'R47' -or $manifest.endpoint -cne '127.0.0.4' -or
       $manifest.port -ne 7728 -or $manifest.database -cne 'astraloath_test' -or $manifest.minimumAppVersion -cne '0.1.27' -or
       $manifest.realmId -cne 'undying-memory' -or $manifest.newHost.directory -ine $newDirectory -or
       $manifest.oldHost.directory -ine $oldDirectory -or $manifest.oldHost.statePath -cne '.artifacts/windows-r44/server/qa-host.json' -or
       $manifest.oldHost.sha256 -cne '69664456f2245d7e3f8bf6e882fd467443c3db331440beec495e21804ef88b84' -or
       $manifest.migrations.Count -ne 21 -or $manifest.migrations[-1].version -cne '021_player_public_identity.sql'){
        throw 'R47_MANIFEST_TARGET_MISMATCH'
    }
    if(!$manifest.readyForActivation -or $manifest.sourceCommit -cnotmatch '^[a-f0-9]{40}$'){throw 'R47_FINAL_SOURCE_PIN_REQUIRED'}
    foreach($at in @($newDirectory,$oldDirectory,$legacy.Dotnet,$legacy.MySql,$legacy.AdminFile,$legacy.RuntimeFile,$output)){Assert-R44SafePath $repository $at}
    [pscustomobject]@{Repository=$repository;Manifest=$manifest;ManifestPath=$path;Output=$output;Legacy=$legacy;Dotnet=$legacy.Dotnet;MySql=$legacy.MySql;
        MySqlDump=Join-Path (Split-Path $legacy.MySql) 'mysqldump.exe';AdminFile=$legacy.AdminFile;RuntimeFile=$legacy.RuntimeFile;
        NewDirectory=$newDirectory;OldDirectory=$oldDirectory;NewDll=Join-Path $newDirectory 'AstralOath.Server.dll'}
}
function Assert-R47Candidate($Context,[switch]$Sources){
    Assert-R38Tree $Context.NewDirectory $Context.Manifest.newHost.files
    Assert-R38Tree $Context.OldDirectory $Context.Manifest.oldHost.files
    foreach($entry in $Context.Manifest.helpers){
        $path=Join-Path $Context.Repository $entry.path;Assert-R44SafePath $Context.Repository $path
        if((Get-FileHash -LiteralPath $path).Hash -ine $entry.sha256){throw 'R47_REUSED_OR_RELEASE_HELPER_CHANGED'}
    }
    if((Get-FileHash -LiteralPath $Context.Dotnet).Hash -ine $Context.Manifest.dotnetSha256){throw 'R47_DOTNET_HASH_CHANGED'}
    if($Sources){
        foreach($entry in $Context.Manifest.sourceFiles){
            $path=Join-Path $Context.Repository $entry.path;Assert-R44SafePath $Context.Repository $path
            if((Get-FileHash -LiteralPath $path).Hash -ine $entry.sha256){throw ('R47_PUBLISHED_SOURCE_CHANGED: '+$entry.path)}
        }
        $current=@(Get-ChildItem -LiteralPath (Join-Path $Context.Repository 'Server/Host') -Filter '*.cs' -Recurse -File|
            Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}|ForEach-Object {[IO.Path]::GetRelativePath($Context.Repository,$_.FullName).Replace('\','/')})
        $current+=@(Get-ChildItem -LiteralPath (Join-Path $Context.Repository 'Shared/com.astraloath.shared/Runtime') -Filter '*.cs' -Recurse -File|
            ForEach-Object {[IO.Path]::GetRelativePath($Context.Repository,$_.FullName).Replace('\','/')})
        $expected=@($Context.Manifest.sourceFiles|Where-Object {($_.uses -contains 'host:Compile') -or ($_.uses -contains 'shared:Compile') -or ($_.uses -contains 'battle:Compile')}|ForEach-Object {$_.path})
        if((@($current|Sort-Object -Unique)|ConvertTo-Json -Compress) -cne (@($expected|Sort-Object -Unique)|ConvertTo-Json -Compress)){throw 'R47_COMPILE_INPUT_SET_CHANGED'}
    }
}
function Get-R47MigrationState($Context,[switch]$RequireApplied){
    $rows=@(Invoke-R38Sql $Context 'SELECT version,HEX(checksum) FROM astraloath_test.foundation_migrations ORDER BY version;');$applied=@{}
    foreach($row in $rows){$parts=([string]$row).Split("`t");if($parts.Count -ne 2 -or $applied.ContainsKey($parts[0])){throw 'R47_MIGRATION_HISTORY_INVALID'};$applied[$parts[0]]=$parts[1]}
    foreach($migration in $Context.Manifest.migrations){
        if($applied.ContainsKey($migration.version)){if($applied[$migration.version] -ine $migration.checksum){throw 'R47_MIGRATION_CHECKSUM_MISMATCH'}}
        elseif($RequireApplied -or $migration.version -cne '021_player_public_identity.sql'){throw 'R47_REQUIRED_MIGRATION_ABSENT'}
    }
    foreach($version in $applied.Keys){if($version -notin @($Context.Manifest.migrations.version)){throw 'R47_UNREVIEWED_MIGRATION_PRESENT'}}
    $uidTable=@(Invoke-R38Sql $Context "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA='astraloath_test' AND TABLE_NAME='player_public_identity';")
    if($RequireApplied){
        if($applied.Count -ne 21 -or $uidTable.Count -ne 1){throw 'R47_UID_MIGRATION_INCOMPLETE'}
        $missing=@(Invoke-R38Sql $Context 'SELECT COUNT(*) FROM astraloath_test.arena_player p LEFT JOIN astraloath_test.player_public_identity u ON u.player_id=p.id WHERE u.player_id IS NULL;')
        $badReceipts=@(Invoke-R38Sql $Context "SELECT COUNT(*) FROM astraloath_test.player_profile_operation WHERE JSON_EXTRACT(receipt,'$.uid') IS NULL OR JSON_TYPE(JSON_EXTRACT(receipt,'$.uid'))<>'STRING' OR CHAR_LENGTH(JSON_UNQUOTE(JSON_EXTRACT(receipt,'$.uid')))<>10;")
        if($missing.Count -ne 1 -or $missing[0] -ne '0' -or $badReceipts.Count -ne 1 -or $badReceipts[0] -ne '0'){throw 'R47_UID_INITIALIZATION_INCOMPLETE_USE_FULL_MIGRATE_NOT_SQL_ONLY'}
    }elseif($applied.Count -ne 20 -or $uidTable.Count -ne 0){throw 'R47_UID_ALREADY_OR_PARTIALLY_APPLIED_MANUAL_REVIEW_REQUIRED'}
    $nonTransactional=@(Invoke-R38Sql $Context "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA='astraloath_test' AND TABLE_TYPE='BASE TABLE' AND ENGINE<>'InnoDB';")
    if($nonTransactional.Count){throw 'R47_NONTRANSACTIONAL_TABLE_PREVENTS_CONSISTENT_BACKUP'}
    [pscustomobject]@{appliedCount=$applied.Count;uidTables=$uidTable.Count;checksumsMatch=$true;complete=[bool]$RequireApplied}
}
function Get-R47Fingerprints($Context){
    @($Context.Manifest.helpers|ForEach-Object {[ordered]@{path=$_.path;sha256=(Get-FileHash -LiteralPath (Join-Path $Context.Repository $_.path)).Hash}})
    [ordered]@{path='.artifacts/windows-r47/backend-release-manifest.json';sha256=(Get-FileHash -LiteralPath $Context.ManifestPath).Hash}
}
function Assert-R47FreshPreflight($Context,[string]$Path){
    $full=[IO.Path]::GetFullPath($Path);$resultRoot=(Join-Path $Context.Output 'results')+[IO.Path]::DirectorySeparatorChar
    if(!$full.StartsWith($resultRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'R47_PREFLIGHT_OUTSIDE_RESULTS'}
    Assert-R44SafePath $Context.Repository $full;$receipt=Get-Content -LiteralPath $full -Raw|ConvertFrom-Json
    $completed=ConvertTo-R38UtcInstant $receipt.completedUtc
    if($receipt.release -cne 'R47' -or $receipt.status -cne 'preflight_passed_no_switch' -or $receipt.requestedExecute -isnot [bool] -or
       $receipt.requestedExecute -or $completed -lt [datetimeoffset]::UtcNow.AddMinutes(-15) -or $completed -gt [datetimeoffset]::UtcNow.AddSeconds(5) -or
       !(Test-R38FingerprintSet $receipt.scriptFingerprints @(Get-R47Fingerprints $Context))){throw 'R47_PREFLIGHT_STALE_OR_INPUTS_CHANGED'}
}
function Assert-R47SameMySql($Context,$Expected,[string]$ResultId){
    $current=Ensure-R38RuntimeMySql $Context.Legacy -VerifyOnly -BootId $ResultId
    if($current.pid -ne $Expected.pid -or $current.startTicks -cne $Expected.startTicks){throw 'R47_MYSQL_IDENTITY_CHANGED'}
}
function Start-R47Host($Context,[string]$ResultId){
    $state=Start-R38OwnedHost $Context $Context.NewDirectory $Context.Manifest.newHost.sha256 'r47' $ResultId
    $state|Add-Member -NotePropertyName sourceCommit -NotePropertyValue $Context.Manifest.sourceCommit
    $state|Add-Member -NotePropertyName sourceDigest -NotePropertyValue $Context.Manifest.sourceDigest
    $state
}
function Wait-R47Host($Context,$State){
    $until=[datetime]::UtcNow.AddSeconds(25)
    while($true){
        $process=Assert-R38Process $State.pid $State.startTicks $Context.Dotnet $Context.NewDll
        try{if($process.HasExited){throw 'R47_STARTED_HOST_EXITED'}}finally{$process.Dispose()}
        $owner=Find-R38Listener '127.0.0.4' 7728
        if($owner -and $owner -ne $State.pid){throw 'R47_OTHER_PROCESS_TOOK_ENDPOINT'}
        if($owner -eq $State.pid){try{Assert-R44Health;if((Get-R44Http '/api/profile/uids/1000000000').status -ne 401){throw 'R47_UID_ROUTE_AUTH_GATE_INVALID'};return}catch{if([datetime]::UtcNow -ge $until){throw}}}
        if([datetime]::UtcNow -ge $until){throw 'R47_START_HEALTH_TIMEOUT'}
        Start-Sleep -Milliseconds 200
    }
}
function Invoke-R47Cutover($Context,[switch]$Execute,[string]$PreflightReceipt,[string]$ResultId){
    $record=[ordered]@{release='R47';resultId=$ResultId;requestedExecute=[bool]$Execute;status='preflight';beganUtc=[datetime]::UtcNow.ToString('O');oldStopped=$false;mysqlStopped=$false;mainDatabaseRestored=$false;restoreRehearsal='waived by explicit user request; not run'}
    $Context|Add-Member -Force -NotePropertyName RunRecord -NotePropertyValue $record
    Assert-R47Candidate $Context -Sources
    $owner=Find-R38Listener '127.0.0.4' 7728;if(!$owner){throw 'R47_CUTOVER_REQUIRES_VERIFIED_RUNNING_R44'}
    $old=Get-R47OwnedHost $Context $owner -Old
    try{
        $mysql=Ensure-R38RuntimeMySql $Context.Legacy -VerifyOnly -BootId $ResultId;$record.mysql=$mysql;$record.oldIdentity=$old.Identity
        $record.schemaBefore=Get-R47MigrationState $Context
        $null=Assert-R38RuntimeSchemaPrivileges $Context;Assert-R44Health
        if(!$Execute){$record.status='preflight_passed_no_switch';$record.completedUtc=[datetime]::UtcNow.ToString('O');$record.scriptFingerprints=@(Get-R47Fingerprints $Context);return $record}
        if(!$PreflightReceipt){throw 'R47_EXECUTE_REQUIRES_PASSED_PREFLIGHT'}
        Assert-R47FreshPreflight $Context $PreflightReceipt;Assert-R47Candidate $Context -Sources
        Stop-R47OwnedHost $Context $old.Identity;$record.oldStopped=$true
        Assert-R47SameMySql $Context $mysql $ResultId
        $record.backup=New-R38SqlBackup $Context $ResultId
        Assert-R47SameMySql $Context $mysql $ResultId
        $record.migrationStarted=$true;$previous=Set-R38Environment $Context (Read-R38MigrationConnection $Context)
        try{$exit=Invoke-R44CapturedChild $Context.Dotnet @($Context.NewDll,'migrate') $Context.NewDirectory}
        finally{Restore-R38Environment $previous}
        $record.migrationExitCode=$exit
        if($exit -ne 0){throw 'R47_FULL_MIGRATE_FAILED_BACKUP_RETAINED_NO_AUTOMATIC_RESTORE'}
        $record.schemaAfter=Get-R47MigrationState $Context -RequireApplied
        # The existing local account must have schema-wide SELECT/INSERT/UPDATE;
        # those reviewed grants include the new UID table. No automatic grants/DDL here.
        $null=Assert-R38RuntimeSchemaPrivileges $Context
        $state=Start-R47Host $Context $ResultId;$record.newHost=$state;Wait-R47Host $Context $state
        Assert-R47SameMySql $Context $mysql $ResultId
        $state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $Context.Output 'qa-host.json') -Encoding utf8
        [ordered]@{release='R47';status='active';hostSha256=$Context.Manifest.newHost.sha256;sourceCommit=$Context.Manifest.sourceCommit;manifestSha256=(Get-FileHash -LiteralPath $Context.ManifestPath).Hash;activatedUtc=[datetime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Context.Output 'activation.json') -Encoding utf8
        $record.status='switched_to_r47';$record.completedUtc=[datetime]::UtcNow.ToString('O');$record
    }finally{if($old.Process){$old.Process.Dispose()}}
}
function Invoke-R47Runtime($Context,[switch]$VerifyOnly){
    Assert-R47Candidate $Context
    $activation=Get-Content -LiteralPath (Join-Path $Context.Output 'activation.json') -Raw|ConvertFrom-Json
    if($activation.release -cne 'R47' -or $activation.status -cne 'active' -or $activation.hostSha256 -ine $Context.Manifest.newHost.sha256 -or
       $activation.manifestSha256 -ine (Get-FileHash -LiteralPath $Context.ManifestPath).Hash){throw 'R47_NOT_ACTIVATED_OR_IDENTITY_CHANGED'}
    $owner=Find-R38Listener '127.0.0.4' 7728;$owned=$null
    if($owner){$owned=Get-R47OwnedHost $Context $owner}elseif($VerifyOnly){throw 'R47_VERIFY_ONLY_NO_HOST_RUNNING'}
    $id=[Guid]::NewGuid().ToString('N')
    try{
        New-Item -ItemType Directory -Force (Join-Path $Context.Output 'results')|Out-Null
        $mysql=Ensure-R38RuntimeMySql $Context.Legacy -VerifyOnly:$VerifyOnly -BootId $id
        $null=Get-R47MigrationState $Context -RequireApplied
        if($owned){$state=Get-Content -LiteralPath (Join-Path $Context.Output 'qa-host.json') -Raw|ConvertFrom-Json}
        else{$state=Start-R47Host $Context $id;$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $Context.Output 'qa-host.json') -Encoding utf8}
        Wait-R47Host $Context $state
        [ordered]@{status='r47_ready';address='127.0.0.4';port=7728;pid=$state.pid;minimumAppVersion='0.1.27';hostSha256=$state.serverSha256;mysql=$mysql;mysqlStopped=$false;mainDatabaseRestored=$false;verifyOnly=[bool]$VerifyOnly}
    }finally{if($owned -and $owned.Process){$owned.Process.Dispose()}}
}
function Invoke-R47GameService([switch]$VerifyOnly,[switch]$SwitchToR47,[switch]$Execute,[string]$PreflightReceipt,[string]$ResultId){
    if($ResultId -cnotmatch '^[a-f0-9]{32}$'){throw 'R47_RESULT_IDENTIFIER_INVALID'}
    $context=Get-R47Context;$mutex=[Threading.Mutex]::new($false,'Local\AstralOath.EquipmentQA.127-0-0-4.7728');$held=$false
    try{
        try{$held=$mutex.WaitOne([TimeSpan]::FromSeconds(55))}catch [Threading.AbandonedMutexException]{$held=$true}
        if(!$held){throw 'R47_STARTUP_MUTEX_TIMEOUT'}
        if(!$SwitchToR47){if($Execute -or $PreflightReceipt){throw 'R47_EXECUTE_REQUIRES_EXPLICIT_SWITCH_MODE'};return Invoke-R47Runtime $context -VerifyOnly:$VerifyOnly}
        if($VerifyOnly -and $Execute){throw 'R47_VERIFY_ONLY_CANNOT_EXECUTE'}
        New-Item -ItemType Directory -Force (Join-Path $context.Output 'results'),(Join-Path $context.Output 'private')|Out-Null
        $resultPath=Join-Path $context.Output "results/$ResultId.json"
        if(Test-Path -LiteralPath $resultPath){throw 'R47_RESULT_IDENTIFIER_ALREADY_USED'}
        try{$record=Invoke-R47Cutover $context -Execute:$Execute -PreflightReceipt $PreflightReceipt -ResultId $ResultId}
        catch{
            $record=$context.RunRecord
            if($record){$record.status=if($record.oldStopped){'cutover_failed_backup_and_main_data_retained_manual_review_required'}else{'preflight_failed_no_stop'};$record.failure=$_.Exception.Message;$record.completedUtc=[datetime]::UtcNow.ToString('O');Write-R38Result $resultPath $record}
            throw
        }
        Write-R38Result $resultPath $record;$record
    }finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
