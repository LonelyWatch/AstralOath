$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$source=Join-Path $PSScriptRoot 'Start-R41Trial.ps1.in'
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors){throw $errors}
$assignment=$ast.Find({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$friendly'},$true)
if(!$assignment){throw 'Startup diagnostic classification is missing'}
$classify=[scriptblock]::Create($assignment.Right.Extent.Text)
$cases=@(
 @('ASTRAL_MYSQL_PROCESS_EXECUTABLE_MISMATCH','数据库'),
 @('R38_RUNTIME_MYSQL_DATA_DIRECTORY_MISMATCH','数据库'),
 @('R38_MIGRATION_CHECKSUM_MISMATCH','数据库'),
 @('R41_SERVICE_UNOWNED_LISTENER','游戏端口'),
 @('R41_R36_DLL_HASH_MISMATCH','游戏端口'),
 @('R41_PACKAGE_FILES_MISSING','试玩未能启动')
)
foreach($case in $cases){$code=$case[0];$actual=& $classify;if(!$actual.Contains($case[1])){throw "Diagnostic category failed: $code"}}
[ordered]@{status='passed';cases=$cases.Count;servicesTouched=$false}|ConvertTo-Json -Compress
