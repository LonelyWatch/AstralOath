[CmdletBinding()]
param([string]$LegacyRuntimePath, [string]$RuntimeFlowPath, [string]$ReportPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$cases = [Collections.Generic.List[object]]::new()
$runtimeFixtures = [Collections.Generic.List[object]]::new()
$fixtureExe = 'D:\Fixture MySql\bin\mysqld.exe'
$fixtureArguments = '--no-defaults --basedir="D:\Fixture MySql" --datadir="D:\Fixture Data" --bind-address=127.0.0.1 --port=3307 --mysqlx=OFF --log-error="D:\Fixture Logs\engine.err"'
function New-FixtureSnapshot([int]$ProcessId = 100, [int]$ParentId = 90, [long]$Ticks = 1000) {
    [pscustomobject]@{ pid=$ProcessId; parentPid=$ParentId; startTicks=[string]$Ticks; executable=$fixtureExe;
        commandLine=('"' + $fixtureExe + '" ' + $fixtureArguments); running=$true }
}
function Copy-Fixture($Value) { $copy=[ordered]@{}; foreach($property in $Value.PSObject.Properties){$copy[$property.Name]=$property.Value}; [pscustomobject]$copy }
function Invoke-Fixture($Started = (New-FixtureSnapshot), $Monitor = (New-FixtureSnapshot), $Listener = (New-FixtureSnapshot 200 100 1100), [int]$Owner = 200) {
    Assert-AstralMySqlProcessIdentity -StartedMonitor $Started -Monitor $Monitor -Listener $Listener -ListenerOwner $Owner `
        -ExpectedExecutable $fixtureExe -ExpectedArguments $fixtureArguments
}
function Check-Case([string]$Name, [scriptblock]$Action, [string]$ExpectedError = '') {
    $failure=$null
    try { $null=& $Action; if($ExpectedError){$failure='Expected rejection: '+$ExpectedError} }
    catch { if(!$ExpectedError -or $_.Exception.Message -ne $ExpectedError){$failure=$_.Exception.Message} }
    $cases.Add([pscustomobject]@{ name=$Name; passed=($null -eq $failure); failure=$failure })
}
function Invoke-RuntimeFixture([string]$Scenario) {
    # Import the actual Runtime and immutable writer. Only OS process/SQL boundaries are replaced.
    $repository=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    . (Join-Path $repository '.artifacts/windows-r38/server/Cutover.Common.ps1')
    . ([scriptblock]::Create([IO.File]::ReadAllText((Resolve-Path -LiteralPath $RuntimeFlowPath).Path)))
    $fixtureRoot=Join-Path $repository ('.artifacts/startup-r41-fix/receipt-regression/runs/'+[Guid]::NewGuid().ToString('N'))
    $data=Join-Path $fixtureRoot 'data';$output=Join-Path $fixtureRoot 'output'
    New-Item -ItemType Directory -Path $data,$output,(Join-Path $data 'astraloath_test') -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $data 'auto.cnf'),'fixture, not a database')
    [IO.File]::WriteAllText((Join-Path $data 'mysql.ibd'),'fixture, not a database')
    $context=[pscustomobject]@{Repository=$repository;Output=$output;MySqlDataDirectory=$data;
        MySqlExecutable=(Resolve-Path (Join-Path $repository '.tools/mysql/mysql-8.4.13-winx64/bin/mysqld.exe')).Path}
    $state=[pscustomobject]@{started=$false;nativeCalls=0;sqlCalls=0;listenerChecks=0;arguments='';argumentsExact=$false;processChecks=0}
    function Get-Process([int]$Id,[object]$ErrorAction) {
        if($Id -notin @(100,200,999)){throw 'Fixture unexpected process ID.'}
        $ticks=if($Id -eq 100){1000}else{1100}
        $process=[pscustomobject]@{Id=$Id;StartTime=[datetime]::new($ticks,[DateTimeKind]::Utc);HasExited=$false}
        $process|Add-Member -MemberType ScriptMethod -Name Refresh -Value {}
        return $process
    }
    function Get-CimInstance([string]$ClassName,[string]$Filter,[object]$ErrorAction) {
        if($ClassName -ne 'Win32_Process' -or $Filter -notmatch '^ProcessId=(100|200|999)$'){throw 'Fixture unexpected CIM query.'}
        $processId=[int]$Matches[1]
        $parentId=if($processId -eq 100){90}elseif($processId -eq 200){100}else{90}
        [pscustomobject]@{ProcessId=$processId;ParentProcessId=$parentId;ExecutablePath=$context.MySqlExecutable;
            CommandLine=('"'+$context.MySqlExecutable+'" '+$state.arguments)}
    }
    function Find-R38Listener([string]$Address,[int]$Port) {
        if($Address -ne '127.0.0.1' -or $Port -ne 3307){throw 'Fixture unexpected listener query.'}
        $state.listenerChecks++
        if($Scenario -eq 'existing'){return 200}
        if(!$state.started){return}
        if($Scenario -eq 'stranger'){return 999}
        return 200
    }
    function Start-R38RuntimeMySqlProcess($Context,[string]$Arguments,[string]$Base,[string]$BootId) {
        $expected='--no-defaults --basedir="'+$Base+'" --datadir="'+$Context.MySqlDataDirectory+'" --bind-address=127.0.0.1 --port=3307 --mysqlx=OFF --log-error="'+(Join-Path $Context.Output "results/$BootId-mysql-engine.err")+'"'
        if($Arguments -cne $expected){throw 'Fixture production MySQL argument string changed.'}
        $state.argumentsExact=$true;$state.arguments=$Arguments;$state.started=$true;$state.nativeCalls++
        return 100
    }
    function Assert-R38Process([int]$Owner,[string]$Ticks,[string]$Executable,[string]$Dll='') {
        $state.processChecks++
        $process=Get-Process -Id $Owner
        if($Executable -cne $context.MySqlExecutable -or $Ticks -ne [string]$process.StartTime.ToUniversalTime().Ticks -or $Dll){throw 'Fixture process identity mismatch.'}
        return $process
    }
    function Invoke-R38Sql($Context,[string]$Sql) {
        if($Sql -cne 'SELECT @@datadir;'){throw 'Fixture unexpected SQL boundary.'}
        $state.sqlCalls++
        return $Context.MySqlDataDirectory
    }
    $failure=$null
    try {
        $result=Ensure-R38RuntimeMySql $context -VerifyOnly:($Scenario -eq 'verify-only') -BootId 'fixture'
        [pscustomobject]@{result=$result;state=$state;childPath=(Join-Path $output 'results/fixture-mysql-child.json');readyPath=(Join-Path $output 'results/fixture-mysql-ready.json')}
    }catch {$failure=$_.Exception.Message;throw}
    finally {
        $runtimeFixtures.Add([pscustomobject]@{scenario=$Scenario;root=$fixtureRoot;failure=$failure;fakeNativeCalls=$state.nativeCalls;
            fakeSqlCalls=$state.sqlCalls;listenerChecks=$state.listenerChecks;productionArgumentsExact=$state.argumentsExact;
            receipts=@(Get-ChildItem -LiteralPath $output -Filter '*.json' -File -Recurse|ForEach-Object FullName)})
    }
}
if($LegacyRuntimePath -and $RuntimeFlowPath){throw 'Choose one regression mode.'}
if($RuntimeFlowPath) {
    Check-Case '完整实际Ensure冷启分支保留两份不可变回执并返回monitor/server' {
        $flow=Invoke-RuntimeFixture 'cold-start'
        if($flow.result.pid -ne 200 -or $flow.result.ownership -ne 'monitor-child' -or $flow.result.monitor.pid -ne 100 -or $flow.result.server.pid -ne 200){throw 'Missing returned process identities.'}
        $child=Get-Content -LiteralPath $flow.childPath -Raw|ConvertFrom-Json
        $ready=Get-Content -LiteralPath $flow.readyPath -Raw|ConvertFrom-Json
        if($child.ownership -ne 'awaiting-listener' -or $null -ne $child.server -or $child.monitor.pid -ne 100){throw 'Initial immutable receipt was changed.'}
        if($ready.ownership -ne 'monitor-child' -or $ready.server.pid -ne 200 -or $ready.monitor.pid -ne 100){throw 'Ready receipt lost either process identity.'}
        if($flow.state.nativeCalls -ne 1 -or $flow.state.sqlCalls -ne 1 -or $flow.state.listenerChecks -lt 5 -or !$flow.state.argumentsExact){throw 'Whole cold-start boundary assertions incomplete.'}
    }
    Check-Case '完整实际Ensure拒绝陌生listener且不接SQL' {
        try{$null=Invoke-RuntimeFixture 'stranger'}finally{
            $last=$runtimeFixtures[$runtimeFixtures.Count-1]
            if($last.fakeNativeCalls -ne 1 -or $last.fakeSqlCalls -ne 0 -or $last.receipts.Count -ne 1){throw 'Stranger listener crossed a protected boundary.'}
        }
    } 'ASTRAL_MYSQL_LISTENER_NOT_STARTED_CHILD'
    Check-Case '完整实际Ensure现有数据库仅核对而不新启动或写回执' {
        $flow=Invoke-RuntimeFixture 'existing'
        if($flow.result.pid -ne 200 -or $flow.state.nativeCalls -ne 0 -or $flow.state.sqlCalls -ne 1 -or (Test-Path -LiteralPath $flow.childPath) -or (Test-Path -LiteralPath $flow.readyPath)){throw 'Existing process path unexpectedly started or wrote a receipt.'}
    }
    Check-Case '完整实际Ensure VerifyOnly无listener时不启动或接SQL' {
        try{$null=Invoke-RuntimeFixture 'verify-only'}finally{
            $last=$runtimeFixtures[$runtimeFixtures.Count-1]
            if($last.fakeNativeCalls -ne 0 -or $last.fakeSqlCalls -ne 0 -or $last.receipts.Count -ne 0){throw 'VerifyOnly crossed a protected boundary.'}
        }
    } 'R38_RUNTIME_VERIFY_ONLY_MYSQL_NOT_RUNNING'
} elseif($LegacyRuntimePath) {
    # Execute the exact former guard from the saved product Runtime, not a rewritten imitation.
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $LegacyRuntimePath).Path,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Legacy Runtime syntax invalid.'}
    $guards=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and
        $node.Extent.Text -match '\$listener -and \$listener -ne \$childId'},$true))
    if($guards.Count -ne 1){throw 'Expected exactly one original PID equality guard.'}
    $legacyGuard=[scriptblock]::Create($guards[0].Extent.Text)
    Check-Case '合法Windows monitor父子链必须接受（原Runtime实际守卫）' {
        $childId=100; $listener=200
        . $legacyGuard
    }
} else {
    . (Join-Path $PSScriptRoot 'MySqlProcessIdentity.ps1')
    Check-Case '接受本次monitor的直接server子进程并记录双身份' {
        $result=Invoke-Fixture
        if($result.ownership -ne 'monitor-child' -or $result.monitor.pid -ne 100 -or $result.server.pid -ne 200){throw 'Lost monitor/server identity.'}
    }
    Check-Case '接受启动进程自身为监听者' { $value=New-FixtureSnapshot; $result=Invoke-Fixture -Listener $value -Owner 100; if($result.ownership -ne 'started-process'){throw 'Wrong direct ownership.'} }
    Check-Case '完整argv允许等义引号而不改变参数' {
        $value=New-FixtureSnapshot 200 100 1100
        $value.commandLine=$value.commandLine.Replace('--port=3307','"--port=3307"')
        Invoke-Fixture -Listener $value
    }
    Check-Case '拒绝并发无关listener而不是仅凭同exe接受' { $value=New-FixtureSnapshot 200 999 1100; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_LISTENER_NOT_STARTED_CHILD'
    Check-Case '拒绝孙进程及兄弟链' { $value=New-FixtureSnapshot 200 150 1100; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_LISTENER_NOT_STARTED_CHILD'
    Check-Case '拒绝listener归属与快照PID不一致' { Invoke-Fixture -Owner 201 } 'ASTRAL_MYSQL_LISTENER_PID_MISMATCH'
    Check-Case '拒绝未知listener零PID' { Invoke-Fixture -Owner 0 } 'ASTRAL_MYSQL_LISTENER_PID_MISMATCH'
    Check-Case '拒绝monitor PID变化' { $value=New-FixtureSnapshot 101 90 1000; Invoke-Fixture -Monitor $value } 'ASTRAL_MYSQL_MONITOR_IDENTITY_CHANGED'
    Check-Case '拒绝monitor PID复用及ticks变化' { $value=New-FixtureSnapshot 100 90 1200; Invoke-Fixture -Monitor $value } 'ASTRAL_MYSQL_MONITOR_IDENTITY_CHANGED'
    Check-Case '拒绝已退出monitor' { $value=New-FixtureSnapshot; $value.running=$false; Invoke-Fixture -Monitor $value } 'ASTRAL_MYSQL_PROCESS_NOT_RUNNING'
    Check-Case '拒绝启动PID无效' { $value=New-FixtureSnapshot 0; Invoke-Fixture -Started $value } 'ASTRAL_MYSQL_PROCESS_IDENTITY_INCOMPLETE'
    Check-Case '拒绝启动ticks无效' { $value=New-FixtureSnapshot 100 90 0; Invoke-Fixture -Started $value } 'ASTRAL_MYSQL_PROCESS_IDENTITY_INCOMPLETE'
    Check-Case '拒绝monitor映像不符' { $value=New-FixtureSnapshot; $value.executable='D:\other\mysqld.exe'; Invoke-Fixture -Monitor $value } 'ASTRAL_MYSQL_PROCESS_EXECUTABLE_MISMATCH'
    Check-Case '拒绝monitor完整argv多出参数' { $value=New-FixtureSnapshot; $value.commandLine+=' --skip-grant-tables'; Invoke-Fixture -Monitor $value } 'ASTRAL_MYSQL_PROCESS_ARGUMENTS_MISMATCH'
    Check-Case '拒绝monitor完整argv缺少参数' { $value=New-FixtureSnapshot; $value.commandLine=$value.commandLine.Replace(' --mysqlx=OFF',''); Invoke-Fixture -Monitor $value } 'ASTRAL_MYSQL_PROCESS_ARGUMENTS_MISMATCH'
    Check-Case '拒绝server映像不符' { $value=New-FixtureSnapshot 200 100 1100; $value.executable='D:\other\mysqld.exe'; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_PROCESS_EXECUTABLE_MISMATCH'
    Check-Case '拒绝server完整argv多出参数' { $value=New-FixtureSnapshot 200 100 1100; $value.commandLine+=' --skip-networking'; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_PROCESS_ARGUMENTS_MISMATCH'
    Check-Case '拒绝server完整argv缺少参数' { $value=New-FixtureSnapshot 200 100 1100; $value.commandLine=$value.commandLine.Replace(' --port=3307',''); Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_PROCESS_ARGUMENTS_MISMATCH'
    Check-Case '拒绝server命令行伪装其它可执行文件' { $value=New-FixtureSnapshot 200 100 1100; $value.commandLine=$value.commandLine.Replace($fixtureExe,'D:\other\mysqld.exe'); Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_PROCESS_ARGUMENTS_MISMATCH'
    Check-Case '拒绝早于monitor出生的旧进程' { $value=New-FixtureSnapshot 200 100 900; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_CHILD_CREATION_ORDER_MISMATCH'
    Check-Case '拒绝同ticks的伪子身份' { $value=New-FixtureSnapshot 200 100 1000; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_CHILD_CREATION_ORDER_MISMATCH'
    Check-Case '拒绝同PID但不同ticks的server快照' { $value=New-FixtureSnapshot 100 90 1100; Invoke-Fixture -Listener $value -Owner 100 } 'ASTRAL_MYSQL_MONITOR_IDENTITY_CHANGED'
    Check-Case '拒绝已退出server' { $value=New-FixtureSnapshot 200 100 1100; $value.running=$false; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_PROCESS_NOT_RUNNING'
    Check-Case '拒绝不完整快照' { $value=[pscustomobject]@{pid=200}; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_PROCESS_IDENTITY_INCOMPLETE'
    Check-Case '拒绝未闭合命令行引号' { $value=New-FixtureSnapshot 200 100 1100; $value.commandLine+=' "'; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_COMMAND_LINE_INVALID'
    Check-Case '拒绝命令行换行' { $value=New-FixtureSnapshot 200 100 1100; $value.commandLine+="`n--port=3308"; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_COMMAND_LINE_INVALID'
    Check-Case '拒绝空命令行' { $value=New-FixtureSnapshot 200 100 1100; $value.commandLine=''; Invoke-Fixture -Listener $value } 'ASTRAL_MYSQL_PROCESS_IDENTITY_INCOMPLETE'
    Check-Case 'Windows引号解析保留反斜线及空参数' {
        $value=@(ConvertFrom-AstralWindowsCommandLine '"C:\Program Files\mysql.exe" "" "D:\folder\\" --value="a b"')
        if($value.Count -ne 4 -or $value[1] -ne '' -or $value[2] -ne 'D:\folder\' -or $value[3] -ne '--value=a b'){throw 'Windows argument parser mismatch.'}
    }
}
$failed=@($cases | Where-Object { !$_.passed }).Count
$report=[ordered]@{mode=$(if($RuntimeFlowPath){'actual_runtime_flow_fake_os_sql'}elseif($LegacyRuntimePath){'actual_legacy_guard_red'}else{'strict_monitor_child_identity'});passed=($cases.Count-$failed);failed=$failed;total=$cases.Count;processesStarted=0;processesStopped=0;databaseAccessed=$false;cases=@($cases.ToArray());runtimeFixtures=@($runtimeFixtures.ToArray())}
$json=$report|ConvertTo-Json -Depth 7
if($ReportPath){$parent=Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath)); New-Item -ItemType Directory -Force -Path $parent|Out-Null; [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath),$json,[Text.UTF8Encoding]::new($false))}
$json
if($failed){exit 1}
