# R44 runtime and one-shot Social cutover share reviewed R38 primitives, never its Host startup.
# Import definitions in the caller's script scope. Importing inside Get-R44Context would
# discard them when that function returns, so the first pinned startup could not verify files.
$script:R44Repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $script:R44Repository '.artifacts/windows-r38/server/Cutover.Common.ps1')
. (Join-Path $script:R44Repository '.artifacts/windows-r38/server/Runtime.Common.ps1')

function Assert-R44SafePath([string]$Repository,[string]$Path) {
    $base=[IO.Path]::GetFullPath($Repository).TrimEnd('\','/')
    $at=[IO.Path]::GetFullPath($Path)
    if(!$at.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'R44_PATH_OUTSIDE_REPOSITORY'}
    while($at.Length -ge $base.Length){
        if((Test-Path -LiteralPath $at) -and ((Get-Item -LiteralPath $at -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'R44_REPARSE_PATH_REFUSED'}
        if($at -ieq $base){break};$at=Split-Path $at
    }
}

function Get-R44Context {
    $repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    . (Join-Path $repository 'Tools/ProjectEnvironment.ps1')
    Initialize-AstralProjectEnvironment -RepositoryRoot $repository|Out-Null
    $old=Join-Path $repository '.artifacts/windows-r38/server'
    $legacy=Get-R38RuntimeContext
    $output=Join-Path $repository '.artifacts/windows-r44/server'
    $legacy.Output=$output
    $manifestPath=Join-Path $output 'release-manifest.json'
    Assert-R44SafePath $repository $manifestPath
    $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
    $newDirectory=Join-Path $output 'host'
    $oldDirectory=Join-Path $old 'host'
    if($manifest.schemaVersion -ne 1 -or $manifest.release -cne 'R44' -or
       $manifest.endpoint -cne '127.0.0.4' -or $manifest.port -ne 7728 -or $manifest.database -cne 'astraloath_test' -or
       $manifest.minimumAppVersion -cne '0.1.27' -or $manifest.realmId -cne 'undying-memory' -or
       $manifest.newHost.directory -ine $newDirectory -or $manifest.oldHost.directory -ine $oldDirectory -or
       $manifest.newHost.sha256 -cne '69664456f2245d7e3f8bf6e882fd467443c3db331440beec495e21804ef88b84' -or
       $manifest.oldHost.sha256 -ine 'ECC7FF4BADBEF6F6B85D28E637F5E044755420E489727C1CCA56DF97C97F614A' -or
       $manifest.oldHost.pid -ne 40884 -or $manifest.oldHost.startTicks -cne '639313872582813218' -or
       $manifest.sourceFiles.Count -ne 124 -or $manifest.newHost.files.Count -ne 18 -or $manifest.migrations.Count -ne 20){throw 'R44_MANIFEST_IDENTITY_MISMATCH'}
    if(!$manifest.readyForActivation -or $manifest.sourceCommit -cnotmatch '^[a-f0-9]{40}$'){throw 'R44_FINAL_SOURCE_PIN_REQUIRED'}
    foreach($path in @($newDirectory,$oldDirectory,$legacy.Dotnet,$legacy.MySql,$legacy.AdminFile,$legacy.RuntimeFile,$output)){
        Assert-R44SafePath $repository $path
    }
    [pscustomobject]@{Repository=$repository;Manifest=$manifest;ManifestPath=$manifestPath;Output=$output;Legacy=$legacy;
        Dotnet=$legacy.Dotnet;MySql=$legacy.MySql;MySqlDump=Join-Path (Split-Path $legacy.MySql) 'mysqldump.exe';
        AdminFile=$legacy.AdminFile;RuntimeFile=$legacy.RuntimeFile;NewDirectory=$newDirectory;OldDirectory=$oldDirectory;
        NewDll=Join-Path $newDirectory 'AstralOath.Server.dll'}
}

function Get-R44Fingerprints($Context) {
    $paths=@('Tools/LocalPlay/Ensure-R44GameService.ps1','Tools/LocalPlay/R44Service.Identity.ps1','Tools/LocalPlay/R44Service.Common.ps1',
        'Tools/LocalPlay/MySqlProcessIdentity.ps1','.artifacts/windows-r38/server/Cutover.Common.ps1',
        '.artifacts/windows-r38/server/Runtime.Common.ps1','.artifacts/windows-r38/server/DetachedHostLauncher.cs',
        '.artifacts/windows-r38/server/development.json','.artifacts/windows-r38/server/cutover-manifest.json',
        '.artifacts/windows-r44/server/release-manifest.json')
    @($paths|ForEach-Object {
        $file=Join-Path $Context.Repository $_;Assert-R44SafePath $Context.Repository $file
        [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $file).Hash}
    })
}

function Assert-R44Candidate($Context,[switch]$Sources) {
    Assert-R38Tree $Context.NewDirectory $Context.Manifest.newHost.files
    Assert-R38Tree $Context.OldDirectory $Context.Manifest.oldHost.files
    foreach($entry in $Context.Manifest.legacyTools){
        $path=Join-Path $Context.Repository $entry.path;Assert-R44SafePath $Context.Repository $path
        if((Get-FileHash -LiteralPath $path).Hash -ine $entry.sha256){throw 'R44_REUSED_HELPER_CHANGED'}
    }
    if((Get-FileHash -LiteralPath $Context.Dotnet).Hash -ine $Context.Manifest.dotnetSha256){throw 'R44_DOTNET_HASH_CHANGED'}
    if($Sources){
        foreach($entry in $Context.Manifest.sourceFiles){
            $path=Join-Path $Context.Repository $entry.path;Assert-R44SafePath $Context.Repository $path
            if((Get-FileHash -LiteralPath $path).Hash -ine $entry.sha256){throw ('R44_PUBLISHED_SOURCE_CHANGED: '+$entry.path)}
        }
        $current=@(Get-ChildItem -LiteralPath (Join-Path $Context.Repository 'Server/Host') -Filter '*.cs' -Recurse -File|
            Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}|ForEach-Object {[IO.Path]::GetRelativePath($Context.Repository,$_.FullName).Replace('\','/')})
        $current+=@(Get-ChildItem -LiteralPath (Join-Path $Context.Repository 'Shared/com.astraloath.shared/Runtime') -Filter '*.cs' -Recurse -File|
            ForEach-Object {[IO.Path]::GetRelativePath($Context.Repository,$_.FullName).Replace('\','/')})
        $expected=@($Context.Manifest.sourceFiles|Where-Object {($_.uses -contains 'host:Compile') -or ($_.uses -contains 'shared:Compile') -or ($_.uses -contains 'battle:Compile')}|ForEach-Object {$_.path})
        if((@($current|Sort-Object -Unique)|ConvertTo-Json -Compress) -cne (@($expected|Sort-Object -Unique)|ConvertTo-Json -Compress)){throw 'R44_COMPILE_INPUT_SET_CHANGED'}
    }
}

function Ensure-R44MySql($Context,[switch]$VerifyOnly,[string]$BootId) {
    # This reviewed function only verifies/reopens the existing datadir; no initialize/restore/stop.
    Ensure-R38RuntimeMySql $Context.Legacy -VerifyOnly:$VerifyOnly -BootId $BootId
}

function Get-R44MigrationState($Context,[switch]$RequireApplied) {
    $rows=@(Invoke-R38Sql $Context 'SELECT version,HEX(checksum) FROM astraloath_test.foundation_migrations ORDER BY version;')
    $applied=@{}
    foreach($row in $rows){$parts=([string]$row).Split("`t");if($parts.Count -ne 2 -or $applied.ContainsKey($parts[0])){throw 'R44_MIGRATION_HISTORY_INVALID'};$applied[$parts[0]]=$parts[1]}
    foreach($migration in $Context.Manifest.migrations){
        if($applied.ContainsKey($migration.version)){
            if($applied[$migration.version] -ine $migration.checksum){throw 'R44_MIGRATION_CHECKSUM_MISMATCH'}
        }elseif($RequireApplied -or $migration.version -notmatch '^0(?:18|19|20)_'){throw 'R44_REQUIRED_MIGRATION_ABSENT'}
    }
    foreach($name in $applied.Keys){if($name -notin @($Context.Manifest.migrations.version)){throw 'R44_UNREVIEWED_MIGRATION_PRESENT'}}
    $names=@('player_profile','entry_nickname_key','player_profile_operation','channel_chat_preferences','channel_chat_message','channel_chat_operation','channel_preferences_operation','channel_chat_order')
    $tables=@(Invoke-R38Sql $Context ("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA='astraloath_test' AND TABLE_NAME IN ('"+($names -join "','")+"') ORDER BY TABLE_NAME;"))
    if($RequireApplied){if($applied.Count -ne 20 -or $tables.Count -ne 8){throw 'R44_SOCIAL_MIGRATION_INCOMPLETE'}}
    elseif($applied.Count -ne 17 -or $tables.Count -ne 0){throw 'R44_SOCIAL_ALREADY_OR_PARTIALLY_APPLIED_REVIEW_REQUIRED'}
    $nonTransactional=@(Invoke-R38Sql $Context "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA='astraloath_test' AND TABLE_TYPE='BASE TABLE' AND ENGINE<>'InnoDB';")
    if($nonTransactional.Count){throw 'R44_NONTRANSACTIONAL_TABLE_PREVENTS_CONSISTENT_BACKUP'}
    [pscustomobject]@{appliedCount=$applied.Count;socialTables=$tables.Count;checksumsMatch=$true;complete=[bool]$RequireApplied}
}

function Assert-R44RuntimePrivileges($Context) { Assert-R38RuntimeSchemaPrivileges $Context|Out-Null }

function Get-R44Http([string]$Path) {
    $request=[Net.HttpWebRequest]::Create('http://127.0.0.4:7728'+$Path)
    $request.Proxy=$null;$request.Timeout=2000;$response=$null
    try {
        try{$response=$request.GetResponse()}catch [Net.WebException]{if($_.Exception.Response){$response=$_.Exception.Response}else{throw 'R44_HTTP_UNAVAILABLE'}}
        $reader=[IO.StreamReader]::new($response.GetResponseStream())
        try{$body=$reader.ReadToEnd()}finally{$reader.Dispose()}
        [pscustomobject]@{status=[int]$response.StatusCode;body=$body}
    }finally{if($response){$response.Dispose()}}
}

function Assert-R44Health([switch]$Old) {
    if((Get-R44Http '/health/ready').status -ne 200){throw 'R44_HEALTH_NOT_READY'}
    $reply=Get-R44Http '/api/entry/version'
    if($reply.status -ne 200){throw 'R44_ENTRY_POLICY_UNAVAILABLE'}
    $policy=$reply.body|ConvertFrom-Json
    if($policy.minimumAppVersion -cne '0.1.27' -or $policy.realmId -cne 'undying-memory'){throw 'R44_ENTRY_POLICY_CHANGED'}
    $routes=@('/api/mail/summary')
    if(!$Old){$routes+=@('/api/profile/me','/api/chat/preferences','/api/chat/channels/1/messages')}
    foreach($route in $routes){if((Get-R44Http $route).status -ne 401){throw 'R44_AUTHENTICATED_ROUTE_MISSING_OR_UNPROTECTED'}}
}

function Assert-R44FreshPreflight($Context,[string]$Path) {
    $full=[IO.Path]::GetFullPath($Path);$resultRoot=(Join-Path $Context.Output 'results')+[IO.Path]::DirectorySeparatorChar
    if(!$full.StartsWith($resultRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'R44_PREFLIGHT_OUTSIDE_RESULTS'}
    Assert-R44SafePath $Context.Repository $full
    $receipt=Get-Content -LiteralPath $full -Raw|ConvertFrom-Json
    $completed=ConvertTo-R38UtcInstant $receipt.completedUtc
    if($receipt.release -cne 'R44' -or $receipt.status -cne 'preflight_passed_no_switch' -or
       $receipt.requestedExecute -isnot [bool] -or $receipt.requestedExecute -or
       $completed -lt [datetimeoffset]::UtcNow.AddMinutes(-15) -or $completed -gt [datetimeoffset]::UtcNow.AddSeconds(5) -or
       !(Test-R38FingerprintSet $receipt.scriptFingerprints (Get-R44Fingerprints $Context))){throw 'R44_PREFLIGHT_STALE_OR_INPUTS_CHANGED'}
}

function New-R44Backup($Context,[string]$ResultId) {
    # Exact old writer has already stopped; the original backup stays immutable and restorable.
    New-R38SqlBackup $Context $ResultId
}

function Assert-R44RestoreDatabase([string]$Database) {
    if($Database -cnotmatch '^astraloath_restore_qa_r44_[a-f0-9]{32}$'){throw 'R44_RESTORE_TARGET_REFUSED'}
}

function Convert-R44RestoreLine([string]$Line,[string]$Database) {
    Assert-R44RestoreDatabase $Database
    if($Line -match '^\s*DROP\s+DATABASE\b'){throw 'R44_DUMP_DATABASE_DIRECTIVE_REFUSED'}
    if($Line -match '^\s*CREATE\s+DATABASE\b'){
        if($Line -cnotmatch '^CREATE DATABASE /\*!32312 IF NOT EXISTS\*/ `astraloath_test`(?: /\*![^\r\n]*\*/)*;$'){throw 'R44_DUMP_DATABASE_DIRECTIVE_REFUSED'}
        return $null
    }
    if($Line -match '^\s*USE\s+'){
        if($Line -cne 'USE `astraloath_test`;'){throw 'R44_DUMP_DATABASE_DIRECTIVE_REFUSED'}
        return ('USE '+[char]96+$Database+[char]96+';')
    }
    # Never rewrite user data. Refuse schema-qualified executable definitions needing manual review.
    if($Line -notmatch '^\s*(?:--|#|INSERT\s+INTO\b|REPLACE\s+INTO\b)' -and
       $Line -match '(?:`astraloath_test`|\bastraloath_test)\s*\.'){throw 'R44_DUMP_MAIN_SCHEMA_REFERENCE_REFUSED'}
    return $Line
}

function Invoke-R44CapturedChild([string]$Executable,[string[]]$Arguments,[string]$Directory,[string]$InputFile='',[int]$TimeoutSeconds=120) {
    $process=[Diagnostics.Process]::new()
    $process.StartInfo=[Diagnostics.ProcessStartInfo]::new($Executable)
    foreach($argument in $Arguments){$process.StartInfo.ArgumentList.Add($argument)}
    $process.StartInfo.WorkingDirectory=$Directory;$process.StartInfo.UseShellExecute=$false;$process.StartInfo.CreateNoWindow=$true
    $process.StartInfo.RedirectStandardOutput=$true;$process.StartInfo.RedirectStandardError=$true
    $process.StartInfo.RedirectStandardInput=[bool]$InputFile
    $null=$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    $clock=[Diagnostics.Stopwatch]::StartNew()
    try {
        if($InputFile){
            $inputStream=[IO.File]::OpenRead($InputFile)
            try{
                $copy=$inputStream.CopyToAsync($process.StandardInput.BaseStream)
                if(!$copy.Wait($TimeoutSeconds*1000)){$process.Kill();$null=$process.WaitForExit(10000);throw 'R44_OWNED_INPUT_TIMEOUT_NO_MAIN_RESTORE_OR_MYSQL_STOP'}
                # PowerShell exposes CopyToAsync's runtime Task<VoidTaskResult>;
                # discard its completion value so callers receive only the exit code.
                $null=$copy.GetAwaiter().GetResult()
            }finally{$inputStream.Dispose();$process.StandardInput.Close()}
        }
        $remaining=[Math]::Max(0,($TimeoutSeconds*1000)-[int]$clock.ElapsedMilliseconds)
        if(!$process.WaitForExit($remaining)){$process.Kill();$null=$process.WaitForExit(10000);throw 'R44_OWNED_COMMAND_TIMEOUT_NO_DATABASE_RESTORE_OR_MYSQL_STOP'}
        # Deliberately discard raw command output: a driver error may echo credentials or row text.
        $null=$stdout.GetAwaiter().GetResult();$null=$stderr.GetAwaiter().GetResult()
        return $process.ExitCode
    }finally{$process.Dispose()}
}

function Get-R44TableProof($Context,[string]$Database) {
    if($Database -cne 'astraloath_test'){Assert-R44RestoreDatabase $Database}
    $tables=@(Invoke-R38Sql $Context ("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA='"+$Database+"' AND TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;"))
    $proof=@()
    foreach($table in $tables){
        if([string]$table -cnotmatch '^[A-Za-z0-9_]+$'){throw 'R44_RESTORE_TABLE_NAME_UNSUPPORTED'}
        $quoted=([char]96+$Database+[char]96+'.'+[char]96+$table+[char]96)
        $count=@(Invoke-R38Sql $Context ('SELECT COUNT(*) FROM '+$quoted+';'))
        $checksum=@(Invoke-R38Sql $Context ('CHECKSUM TABLE '+$quoted+' EXTENDED;'))
        if($count.Count -ne 1 -or $checksum.Count -ne 1 -or ([string]$checksum[0]).Split("`t").Count -ne 2){throw 'R44_RESTORE_ROW_PROOF_UNAVAILABLE'}
        $sum=([string]$checksum[0]).Split("`t")[1]
        if($sum -notmatch '^\d+$'){throw 'R44_RESTORE_ROW_CHECKSUM_UNAVAILABLE'}
        $proof+=[ordered]@{table=[string]$table;rows=[string]$count[0];checksum=$sum}
    }
    return @($proof)
}

function Test-R44BackupRestore($Context,$Backup,[string]$ResultId) {
    $database='astraloath_restore_qa_r44_'+$ResultId;Assert-R44RestoreDatabase $database
    Assert-R44SafePath $Context.Repository $Backup.path
    if((Get-FileHash -LiteralPath $Backup.path).Hash -ine $Backup.sha256){throw 'R44_BACKUP_CHANGED_BEFORE_ISOLATED_RESTORE'}
    if(@(Invoke-R38Sql $Context ("SELECT SCHEMA_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='"+$database+"';")).Count){throw 'R44_RESTORE_SCHEMA_ALREADY_EXISTS'}
    $restoreFile=Join-Path (Split-Path $Backup.path) 'isolated-restore.sql'
    if(Test-Path -LiteralPath $restoreFile){throw 'R44_RESTORE_INPUT_ALREADY_EXISTS'}
    $reader=[IO.StreamReader]::new($Backup.path);$writer=[IO.StreamWriter]::new($restoreFile,$false,[Text.UTF8Encoding]::new($false));$useCount=0
    try{while($null -ne ($line=$reader.ReadLine())){
        $converted=Convert-R44RestoreLine $line $database
        if($line -ceq 'USE `astraloath_test`;'){$useCount++}
        if($null -ne $converted){$writer.WriteLine($converted)}
    }}finally{$reader.Dispose();$writer.Dispose()}
    if($useCount -ne 1){throw 'R44_DUMP_SINGLE_DATABASE_SELECTION_REQUIRED'}
    $before=@(Get-R44TableProof $Context 'astraloath_test')
    $null=Invoke-R38Sql $Context ('CREATE DATABASE '+[char]96+$database+[char]96+' CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;')
    $mysqlArguments=@("--defaults-file=$($Context.AdminFile)",'--no-login-paths','--host=127.0.0.1','--port=3307','--protocol=tcp',"--database=$database",'--binary-mode')
    $exit=Invoke-R44CapturedChild $Context.MySql $mysqlArguments $Context.Output $restoreFile 120
    if($exit -ne 0){throw 'R44_ISOLATED_BACKUP_RESTORE_FAILED_NO_MAIN_RESTORE'}
    $after=@(Get-R44TableProof $Context $database)
    if(!$before.Count -or ($before|ConvertTo-Json -Depth 4 -Compress) -cne ($after|ConvertTo-Json -Depth 4 -Compress)){throw 'R44_ISOLATED_RESTORE_ROW_PROOF_MISMATCH'}
    $receipt=[ordered]@{passed=$true;sourceDatabase='astraloath_test';restoredDatabase=$database;backupSha256=$Backup.sha256;
        restoredInputSha256=(Get-FileHash -LiteralPath $restoreFile).Hash;tableCount=$after.Count;tables=$after;mainDatabaseRestored=$false;
        isolatedSchemaRetained=$true;completedUtc=[datetime]::UtcNow.ToString('O')}
    Write-R38Result (Join-Path (Split-Path $Backup.path) 'restore-rehearsal.json') $receipt
    return [pscustomobject]$receipt
}

function Invoke-R44Migration($Context,[string]$ResultId) {
    $previous=Set-R38Environment $Context (Read-R38MigrationConnection $Context)
    try{$exit=Invoke-R44CapturedChild $Context.Dotnet @($Context.NewDll,'migrate') $Context.NewDirectory}
    finally{Restore-R38Environment $previous}
    Write-R38Result (Join-Path $Context.Output "private/$ResultId-migration.json") @{
        exitCode=$exit;sourceCommit=$Context.Manifest.sourceCommit;hostSha256=$Context.Manifest.newHost.sha256;
        migrations=@('018_player_profile.sql','019_channel_chat.sql','020_channel_chat_order.sql');mainDatabaseRestored=$false;mysqlStopped=$false}
    if($exit -ne 0){throw 'R44_MIGRATION_FAILED_MAIN_DATA_AND_BACKUP_RETAINED_NO_AUTOMATIC_ROLLBACK'}
}

function Start-R44Host($Context,[string]$ResultId) {
    $state=Start-R38OwnedHost $Context $Context.NewDirectory $Context.Manifest.newHost.sha256 'r44' $ResultId
    $state|Add-Member -NotePropertyName sourceCommit -NotePropertyValue $Context.Manifest.sourceCommit
    $state|Add-Member -NotePropertyName sourceDigest -NotePropertyValue $Context.Manifest.sourceDigest
    return $state
}

function Wait-R44Host($Context,$State) {
    $until=[datetime]::UtcNow.AddSeconds(25)
    while($true){
        $process=Assert-R38Process $State.pid $State.startTicks $Context.Dotnet $Context.NewDll
        if($process.HasExited){throw 'R44_STARTED_HOST_EXITED'}
        $owner=Find-R38Listener '127.0.0.4' 7728
        if($owner -and $owner -ne $State.pid){throw 'R44_OTHER_PROCESS_TOOK_ENDPOINT'}
        if($owner -eq $State.pid){try{Assert-R44Health;return}catch{if([datetime]::UtcNow -ge $until){throw}}}
        if([datetime]::UtcNow -ge $until){throw 'R44_START_HEALTH_TIMEOUT'}
        Start-Sleep -Milliseconds 200
    }
}

function Complete-R44Activation($Context,$State,$Record) {
    $Record.status='switched_to_r44';$Record.completedUtc=[datetime]::UtcNow.ToString('O')
    $State|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $Context.Output 'qa-host.json') -Encoding utf8
    [ordered]@{release='R44';status='active';hostSha256=$Context.Manifest.newHost.sha256;sourceCommit=$Context.Manifest.sourceCommit;
        manifestSha256=(Get-FileHash -LiteralPath $Context.ManifestPath).Hash;minimumAppVersion='0.1.27';database='astraloath_test';
        resultId=$Record.resultId;activatedUtc=[datetime]::UtcNow.ToString('O');mainDatabaseRestored=$false;mysqlStopped=$false}|
        ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $Context.Output 'activation.json') -Encoding utf8
}

function Invoke-R44CutoverCore($Context,[switch]$Execute,[string]$PreflightReceipt,[string]$ResultId) {
    if($ResultId -cnotmatch '^[a-f0-9]{32}$'){throw 'R44_RESULT_IDENTIFIER_INVALID'}
    $record=[ordered]@{release='R44';resultId=$ResultId;requestedExecute=[bool]$Execute;status='preflight';
        beganUtc=[datetime]::UtcNow.ToString('O');oldStopped=$false;mysqlStopped=$false;mainDatabaseRestored=$false}
    $Context|Add-Member -Force -NotePropertyName RunRecord -NotePropertyValue $record
    Assert-R44Candidate $Context -Sources
    $owner=Find-R38Listener '127.0.0.4' 7728
    if(!$owner){throw 'R44_CUTOVER_REQUIRES_VERIFIED_RUNNING_R38'}
    $old=Get-R44OwnedHost $Context $owner -Old
    try {
        $mysql=Ensure-R44MySql $Context -VerifyOnly -BootId $ResultId
        $record.mysql=$mysql;$record.oldIdentity=$old.Identity
        $record.schemaBefore=Get-R44MigrationState $Context
        Assert-R44RuntimePrivileges $Context
        Assert-R44Health -Old
        if(!$Execute){$record.status='preflight_passed_no_switch';$record.completedUtc=[datetime]::UtcNow.ToString('O');$record.scriptFingerprints=@(Get-R44Fingerprints $Context);return $record}
        if(!$PreflightReceipt){throw 'R44_EXECUTE_REQUIRES_PASSED_PREFLIGHT'}
        Assert-R44FreshPreflight $Context $PreflightReceipt
        Assert-R44Candidate $Context -Sources
        Stop-R44OwnedHost $Context $old.Identity -Old
        $record.oldStopped=$true
        $current=Ensure-R44MySql $Context -VerifyOnly -BootId $ResultId
        if($current.pid -ne $mysql.pid -or $current.startTicks -cne $mysql.startTicks){throw 'R44_MYSQL_CHANGED_BEFORE_BACKUP'}
        $record.backup=New-R44Backup $Context $ResultId
        $record.restoreRehearsal=Test-R44BackupRestore $Context $record.backup $ResultId
        $current=Ensure-R44MySql $Context -VerifyOnly -BootId $ResultId
        if($current.pid -ne $mysql.pid -or $current.startTicks -cne $mysql.startTicks){throw 'R44_MYSQL_CHANGED_BEFORE_MIGRATION'}
        $record.migrationStarted=$true;Invoke-R44Migration $Context $ResultId
        $record.schemaAfter=Get-R44MigrationState $Context -RequireApplied
        $new=Start-R44Host $Context $ResultId;$record.newHost=$new
        Wait-R44Host $Context $new
        $current=Ensure-R44MySql $Context -VerifyOnly -BootId $ResultId
        if($current.pid -ne $mysql.pid -or $current.startTicks -cne $mysql.startTicks){throw 'R44_MYSQL_CHANGED_AFTER_MIGRATION'}
        Complete-R44Activation $Context $new $record
        return $record
    }finally{if($old.Process){$old.Process.Dispose()}}
}

function Invoke-R44Runtime($Context,[switch]$VerifyOnly) {
    Assert-R44Candidate $Context
    $activationPath=Join-Path $Context.Output 'activation.json'
    if(!(Test-Path -LiteralPath $activationPath)){throw 'R44_NOT_ACTIVATED_USE_EXPLICIT_CUTOVER'}
    $activation=Get-Content -LiteralPath $activationPath -Raw|ConvertFrom-Json
    if($activation.release -cne 'R44' -or $activation.status -cne 'active' -or
       $activation.hostSha256 -ine $Context.Manifest.newHost.sha256 -or
       $activation.manifestSha256 -ine (Get-FileHash -LiteralPath $Context.ManifestPath).Hash){throw 'R44_ACTIVATION_IDENTITY_CHANGED'}
    $owner=Find-R38Listener '127.0.0.4' 7728;$owned=$null
    if($owner){$owned=Get-R44OwnedHost $Context $owner}
    elseif($VerifyOnly){throw 'R44_VERIFY_ONLY_NO_HOST_RUNNING'}
    $id=[Guid]::NewGuid().ToString('N')
    try {
        $mysql=Ensure-R44MySql $Context -VerifyOnly:$VerifyOnly -BootId $id
        $null=Get-R44MigrationState $Context -RequireApplied
        if($owned){$state=Get-Content -LiteralPath (Join-Path $Context.Output 'qa-host.json') -Raw|ConvertFrom-Json}
        else{$state=Start-R44Host $Context $id;$state|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $Context.Output 'qa-host.json') -Encoding utf8}
        Wait-R44Host $Context $state
        [ordered]@{status='r44_ready';address='127.0.0.4';port=7728;pid=$state.pid;minimumAppVersion='0.1.27';
            hostSha256=$state.serverSha256;mysql=$mysql;mysqlStopped=$false;mainDatabaseRestored=$false;verifyOnly=[bool]$VerifyOnly}
    }finally{if($owned -and $owned.Process){$owned.Process.Dispose()}}
}

function Invoke-R44GameService([switch]$VerifyOnly,[switch]$SwitchToR44,[switch]$Execute,[string]$PreflightReceipt,[string]$ResultId=([Guid]::NewGuid().ToString('N'))) {
    $ErrorActionPreference='Stop';$context=Get-R44Context
    $mutex=[Threading.Mutex]::new($false,'Local\AstralOath.EquipmentQA.127-0-0-4.7728');$held=$false
    try {
        try{$held=$mutex.WaitOne([TimeSpan]::FromSeconds(55))}catch [Threading.AbandonedMutexException]{$held=$true}
        if(!$held){throw 'R44_STARTUP_MUTEX_TIMEOUT'}
        if(!$SwitchToR44){if($Execute -or $PreflightReceipt){throw 'R44_EXECUTE_REQUIRES_EXPLICIT_SWITCH_MODE'};return Invoke-R44Runtime $context -VerifyOnly:$VerifyOnly}
        if($VerifyOnly -and $Execute){throw 'R44_VERIFY_ONLY_CANNOT_EXECUTE'}
        New-Item -ItemType Directory -Force (Join-Path $context.Output 'results'),(Join-Path $context.Output 'private')|Out-Null
        $resultPath=Join-Path $context.Output "results/$ResultId.json"
        if(Test-Path -LiteralPath $resultPath){throw 'R44_RESULT_IDENTIFIER_ALREADY_USED'}
        try{$record=Invoke-R44CutoverCore $context -Execute:$Execute -PreflightReceipt $PreflightReceipt -ResultId $ResultId}
        catch {
            $record=$context.RunRecord
            if($record){$record.status=if($record.oldStopped){'cutover_failed_main_data_retained_manual_review_required'}else{'preflight_failed_no_stop'};
                $record.failure=$_.Exception.Message;$record.completedUtc=[datetime]::UtcNow.ToString('O');Write-R38Result $resultPath $record}
            throw
        }
        Write-R38Result $resultPath $record
        return $record
    }finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
