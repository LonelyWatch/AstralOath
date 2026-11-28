# Starts only short, owned fixture children; no network, SQL or service operations.
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'R44Service.Identity.ps1')
. (Join-Path $PSScriptRoot 'R44Service.Common.ps1')
$out=Join-Path $repo ('.artifacts/localplay-captured-process/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out|Out-Null
$child=Join-Path $out 'fixture.ps1';$inputFile=Join-Path $out 'input.txt'
[IO.File]::WriteAllText($child,'param([int]$Expected,[int]$Code) $text=[Console]::In.ReadToEnd(); if($text.Length -ne $Expected){exit 13}; exit $Code',[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($inputFile,('x'*524288),[Text.UTF8Encoding]::new($false))
$executable=Join-Path $repo '.tools/powershell/pwsh.exe'
$checks=@()
foreach($code in @(0,7)){
    $values=@(Invoke-R44CapturedChild $executable @('-NoProfile','-File',$child,'-Expected','524288','-Code',[string]$code) $out $inputFile 30)
    $checks+=@{case="streamed_input_exit_$code";passed=($values.Count -eq 1 -and $values[0] -is [int] -and $values[0] -eq $code);returnedValues=$values.Count;types=@($values|ForEach-Object {$_.GetType().FullName})}
}
$values=@(Invoke-R44CapturedChild $executable @('-NoProfile','-Command','exit 0') $out)
$checks+=@{case='no_input_exit_zero';passed=($values.Count -eq 1 -and $values[0] -is [int] -and $values[0] -eq 0);returnedValues=$values.Count;types=@($values|ForEach-Object {$_.GetType().FullName})}
$result=@{passed=@($checks|Where-Object passed).Count;total=$checks.Count;checks=$checks;serviceOrSqlTouched=$false;output=$out}
$result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $out 'receipt.json') -Encoding utf8
$result|ConvertTo-Json -Depth 7
if($result.passed -ne $result.total){throw 'Captured process result contract failed'}
