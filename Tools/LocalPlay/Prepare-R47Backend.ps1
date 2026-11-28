#requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^[a-f0-9]{40}$')][string]$PinSourceCommit)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo|Out-Null
$release=Join-Path $repo '.artifacts/windows-r47'
$backend=Join-Path $release 'backend'
$metadata=Join-Path $release 'backend-preparation'
$manifestPath=Join-Path $release 'backend-release-manifest.json'
$dotnet=Join-Path $repo '.tools/dotnet/dotnet.exe'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()}
function MigrationChecksum([string]$Path){
    # Match DatabaseMigrator: StreamReader detects/removes BOM, then CRLF -> LF,
    # then hash UTF-8 text. Published file hashes remain raw-byte identities.
    $reader=[IO.StreamReader]::new($Path)
    try{$sql=$reader.ReadToEnd().Replace("`r`n","`n")}finally{$reader.Dispose()}
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql))).ToLowerInvariant()
}
function Sources {
    $found=@{}
    $projects=@{host='Server/Host/AstralOath.Server.csproj';shared='Shared/com.astraloath.shared/AstralOath.Shared.csproj';battle='Shared/com.astraloath.shared/Runtime/Battle/AstralOath.Battle.csproj'}
    foreach($name in @('host','shared','battle')){
        $raw=@(& $dotnet msbuild (Join-Path $repo $projects[$name]) -nologo '-getItem:Compile,EmbeddedResource,Content' '-p:Configuration=Release' "-p:TargetPlatformSdkPath=$repo/.tools" '-p:TargetPlatformDisplayName=Windows')
        if($LASTEXITCODE -ne 0){throw 'R47_MSBUILD_INPUT_DISCOVERY_FAILED'}
        $value=($raw -join "`n")|ConvertFrom-Json
        foreach($kind in @('Compile','EmbeddedResource','Content')){
            foreach($item in @($value.Items.$kind)){
                if(!$item){continue};$path=[IO.Path]::GetFullPath($item.FullPath)
                if(!(Test-Path -LiteralPath $path -PathType Leaf)){continue}
                if(!$path.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'R47_SOURCE_OUTSIDE_REPOSITORY'}
                $relative=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/')
                if(!$found.ContainsKey($relative)){$found[$relative]=[ordered]@{path=$relative;bytes=(Get-Item -LiteralPath $path).Length;sha256=Hash $path;uses=@()}}
                $found[$relative].uses+=($name+':'+$kind)
            }
        }
    }
    foreach($relative in @('global.json','NuGet.Config','Server/Host/AstralOath.Server.csproj','Server/Host/packages.lock.json','Shared/com.astraloath.shared/AstralOath.Shared.csproj','Shared/com.astraloath.shared/packages.lock.json','Shared/com.astraloath.shared/Directory.Build.props','Shared/com.astraloath.shared/Runtime/Battle/AstralOath.Battle.csproj')){
        $path=Join-Path $repo $relative
        if(Test-Path -LiteralPath $path -PathType Leaf){$found[$relative]=[ordered]@{path=$relative;bytes=(Get-Item -LiteralPath $path).Length;sha256=Hash $path;uses=@('project_or_lock')}}
    }
    @($found.Values|Sort-Object {$_.path})
}
function SourceDigest($Files){
    $text=(@($Files|ForEach-Object {$_.path+"`t"+$_.sha256+"`n"}) -join '')
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}
$helperPaths=@('Tools/LocalPlay/Ensure-R47GameService.ps1','Tools/LocalPlay/R47Service.Common.ps1','Tools/LocalPlay/R47Service.Identity.ps1','Tools/LocalPlay/R44Service.Common.ps1','Tools/LocalPlay/R44Service.Identity.ps1','Tools/LocalPlay/MySqlProcessIdentity.ps1','.artifacts/windows-r38/server/Cutover.Common.ps1','.artifacts/windows-r38/server/Runtime.Common.ps1','.artifacts/windows-r38/server/DetachedHostLauncher.cs','.artifacts/windows-r38/server/development.json','.artifacts/windows-r38/server/cutover-manifest.json')
if($PinSourceCommit){
    $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
    $sources=@(Sources)
    if((SourceDigest $sources) -cne $manifest.sourceDigest){throw 'R47_SOURCE_CHANGED_SINCE_PUBLISH_REPUBLISH_REQUIRED'}
    foreach($helper in $manifest.helpers){if((Hash (Join-Path $repo $helper.path)) -cne $helper.sha256){throw 'R47_HELPER_CHANGED_SINCE_PREPARATION'}}
    foreach($entry in $manifest.newHost.files){if((Hash (Join-Path $backend $entry.path)) -cne $entry.sha256){throw 'R47_PUBLISHED_FILE_CHANGED'}}
    if($manifest.readyForActivation){throw 'R47_SOURCE_ALREADY_PINNED_NO_OVERWRITE'}
    $manifest.sourceCommit=$PinSourceCommit;$manifest.sourceState='Root supplied final commit; evaluated source hashes unchanged from publish'
    $manifest.readyForActivation=$true
    $manifest|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $manifestPath -Encoding utf8
    [ordered]@{status='r47_source_pinned_prepared_only';sourceCommit=$PinSourceCommit;serverSha256=$manifest.newHost.sha256;servicesTouched=$false;sqlExecuted=$false}|ConvertTo-Json
    return
}
foreach($path in @($backend,$metadata,$manifestPath)){if(Test-Path -LiteralPath $path){throw 'R47_PREPARATION_OUTPUT_EXISTS_NO_OVERWRITE'}}
New-Item -ItemType Directory -Path $metadata -Force|Out-Null
$sources=@(Sources);$digest=SourceDigest $sources
[ordered]@{sourceState='uncommitted working-tree inputs; not a final source pin';sourceFiles=$sources;sourceDigest=$digest}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $metadata 'source-before-publish.json') -Encoding utf8
& $dotnet publish (Join-Path $repo 'Server/Host/AstralOath.Server.csproj') -c Release --no-restore -o $backend "-p:TargetPlatformSdkPath=$repo/.tools" '-p:TargetPlatformDisplayName=Windows' *> (Join-Path $metadata 'publish.log')
if($LASTEXITCODE -ne 0){throw 'R47_BACKEND_PUBLISH_FAILED_INSPECT_PUBLISH_LOG'}
if((SourceDigest @(Sources)) -cne $digest){throw 'R47_SOURCE_CHANGED_DURING_PUBLISH_OUTPUT_NOT_PINNABLE'}
$oldManifest=Get-Content -LiteralPath (Join-Path $repo '.artifacts/windows-r44/server/release-manifest.json') -Raw|ConvertFrom-Json
$oldState=Get-Content -LiteralPath (Join-Path $repo '.artifacts/windows-r44/server/qa-host.json') -Raw|ConvertFrom-Json
$files=@(Get-ChildItem -LiteralPath $backend -Recurse -File|Sort-Object FullName|ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($backend,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=Hash $_.FullName}})
$migrations=@(Get-ChildItem -LiteralPath (Join-Path $repo 'Server/Host/Foundation/Migrations') -Filter '*.sql' -File|Sort-Object Name|ForEach-Object {[ordered]@{version=$_.Name;checksum=MigrationChecksum $_.FullName}})
$manifest=[ordered]@{schemaVersion=1;release='R47';readyForActivation=$false;sourceCommit=$null;sourceState='uncommitted working-tree snapshot; Root final source pin required';sourceDigest=$digest;sourceFiles=$sources;
    endpoint='127.0.0.4';port=7728;database='astraloath_test';minimumAppVersion='0.1.27';realmId='undying-memory';dotnetSha256=Hash $dotnet;
    oldHost=[ordered]@{directory=$oldManifest.newHost.directory;sha256=$oldManifest.newHost.sha256;files=$oldManifest.newHost.files;pid=$oldState.pid;startTicks=$oldState.startTicks;statePath='.artifacts/windows-r44/server/qa-host.json';backupDirectory=$oldManifest.newHost.directory};
    newHost=[ordered]@{directory=$backend;sha256=Hash (Join-Path $backend 'AstralOath.Server.dll');files=$files};migrations=$migrations;
    helpers=@($helperPaths|ForEach-Object {[ordered]@{path=$_;sha256=Hash (Join-Path $repo $_)}});
    restoreRehearsal='not performed per explicit user waiver; fresh backup remains mandatory before migration';regressionTests='not run per explicit user request';preparedUtc=[datetime]::UtcNow.ToString('O');servicesStarted=$false;servicesStopped=$false;sqlExecuted=$false}
$manifest|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $manifestPath -Encoding utf8
[ordered]@{status='r47_backend_published_unpinned_prepared_only';sourceFiles=$sources.Count;outputFiles=$files.Count;serverSha256=$manifest.newHost.sha256;manifest=$manifestPath;servicesTouched=$false;sqlExecuted=$false}|ConvertTo-Json
