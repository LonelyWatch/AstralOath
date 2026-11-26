#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Position=0)][ValidateSet('interactive','preview','confirm','status','retry')][string]$Action = 'interactive',
    [string]$ServerDll,
    [string]$Realm = 'undying-memory',
    [string[]]$To,
    [string]$AllowFile,
    [string]$DenyFile,
    [switch]$AllInRealm,
    [string]$Template,
    [string]$PreviewId,
    [string]$BatchId,
    [switch]$Json,
    [ValidateRange(5,600)][int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo -WarningAction SilentlyContinue | Out-Null

function Assert-MailArguments {
    if ($Action -eq 'interactive') { return }
    $scopeCount = [int]($To.Count -gt 0) + [int]([bool]$AllowFile) + [int]$AllInRealm.IsPresent
    if ($Action -eq 'preview') {
        if ($scopeCount -ne 1) { throw '必须明确且仅选择一个收件范围：-To、-AllowFile 或 -AllInRealm。黑名单不能单独作为范围。' }
        if (-not $Template) { throw '预览必须指定模板 JSON：-Template。' }
        if ($PreviewId -or $BatchId) { throw '预览不能混用已有预览或批次号。' }
    } else {
        if ($scopeCount -gt 0 -or $DenyFile -or $Template) { throw '确认、状态和重试只接受已冻结的预览或批次，不接受新名单或模板。' }
        if ($Action -eq 'confirm' -and (-not $PreviewId -or $BatchId)) { throw '确认必须仅指定 -PreviewId。' }
        if ($Action -in @('status','retry') -and (-not $BatchId -or $PreviewId)) { throw '查询或重试必须仅指定 -BatchId。' }
    }
}

function Resolve-MailFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw '指定的输入文件不存在或不可读。' }
    (Resolve-Path -LiteralPath $Path).ProviderPath
}

function Invoke-MailHost([string[]]$CommandArguments) {
    if (-not $ServerDll) { throw '请用 -ServerDll 显式选择目标环境的已发布游戏服程序集。' }
    $dll = Resolve-MailFile $ServerDll
    if ([IO.Path]::GetExtension($dll) -ne '.dll') { throw '目标必须是已发布的服务端 .dll 文件。' }
    $dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $dotnet
    $start.WorkingDirectory = Split-Path -Parent $dll
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $start.ArgumentList.Add($dll)
    $start.ArgumentList.Add('mail')
    foreach ($argument in $CommandArguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw '无法启动邮件工具。' }
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw '邮件命令超时；请使用相同预览号确认或原批次号查询，不要重建发送。'
        }
        $output = $outputTask.GetAwaiter().GetResult()
        $diagnostic = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            $code = 'mail_host_failed'
            try {
                $parsed = $diagnostic | ConvertFrom-Json -ErrorAction Stop
                if ($parsed.error -cmatch '^[a-z0-9_]{1,80}$') { $code = $parsed.error }
            } catch { }
            throw "邮件命令失败：$code。原始数据库或模板内容不会输出。"
        }
        try { $output | ConvertFrom-Json -ErrorAction Stop }
        catch { throw '邮件工具未返回有效 JSON；请核对程序集版本。' }
    } finally { $process.Dispose() }
}

function Show-MailPreview($Preview) {
    Write-Host "预览号：$($Preview.previewId)"
    Write-Host "区服：$($Preview.realmId)；实际收件：$($Preview.recipientCount)；重复：$($Preview.duplicateCount)；排除：$($Preview.excludedCount)"
    Write-Host "预览确认截止 UTC：$($Preview.previewExpiresAt)；确认后全批固定 $($Preview.expiresInDays) 天到期，重试不续期。"
    Write-Host "类型：$($Preview.mail.letterType)；发件人：$($Preview.mail.senderName)；模板：$($Preview.mail.templateId)"
    Write-Host "标题：$($Preview.mail.title)"
    Write-Host "正文：`n$($Preview.mail.body)"
    Write-Host '每人实际奖励：'
    foreach ($reward in $Preview.mail.rewards) { Write-Host "  $($reward.name) [$($reward.kind)/$($reward.key)] × $($reward.quantity)" }
    Write-Host '全批合计奖励：'
    foreach ($reward in $Preview.totalRewards) { Write-Host "  $($reward.name) × $($reward.quantity)" }
    if ($Preview.recipientCount -eq 0) { Write-Host '本次为零人预览，不可确认发送。' }
}

function Show-MailBatch($Batch) {
    Write-Host "批次：$($Batch.batchId)；预览：$($Batch.previewId)；固定到期 UTC：$($Batch.expiresAt)"
    Write-Host "目标 $($Batch.targetCount)；已投递 $($Batch.deliveredCount)；待投递 $($Batch.pendingCount)；失败 $($Batch.failedCount)；已领取 $($Batch.claimedCount)"
    foreach ($failure in $Batch.failures) { Write-Host "  $($failure.reason)：$($failure.count)" }
    Write-Host '容量失败可用原批次 retry；新目标或新内容必须重新 preview。'
}

function New-PreviewArguments {
    $parts = [Collections.Generic.List[string]]::new()
    $parts.Add('preview'); $parts.Add('--realm'); $parts.Add($Realm)
    $parts.Add('--template'); $parts.Add((Resolve-MailFile $Template))
    if ($To.Count -gt 0) { $parts.Add('--to'); $parts.Add(($To -join ',')) }
    elseif ($AllowFile) { $parts.Add('--allow-file'); $parts.Add((Resolve-MailFile $AllowFile)) }
    elseif ($AllInRealm) { $parts.Add('--all-in-realm') }
    if ($DenyFile) { $parts.Add('--deny-file'); $parts.Add((Resolve-MailFile $DenyFile)) }
    $parts.ToArray()
}

function Read-MailNumber([string]$Prompt, [int]$Default, [int]$Minimum, [int]$Maximum) {
    $text = Read-Host "$Prompt（默认 $Default）"
    if (-not $text) { return $Default }
    $number = 0
    if (-not [int]::TryParse($text, [ref]$number) -or $number -lt $Minimum -or $number -gt $Maximum) { throw '输入数量超出允许范围。' }
    $number
}

try {
    Assert-MailArguments
    if ($Action -eq 'interactive') {
        Write-Host 'AstralOath 邮件运营：本机 Host 命令，不启动 HTTP 监听。连接凭据沿目标环境配置读取。'
        if (-not $ServerDll) { $ServerDll = Read-Host '目标环境服务端 DLL 的完整路径' }
        $chosenRealm = Read-Host "目标区服（默认 $Realm）"
        if ($chosenRealm) { $Realm = $chosenRealm }
        $operation = Read-Host '操作：1 新邮件预览；2 确认已有预览；3 批次状态；4 原批次重试（默认 1）'
        if ($operation -in @('2','3','4')) {
            if ($operation -eq '2') { $Action = 'confirm'; $PreviewId = Read-Host '预览号' }
            elseif ($operation -eq '3') { $Action = 'status'; $BatchId = Read-Host '批次号' }
            else { $Action = 'retry'; $BatchId = Read-Host '批次号' }
        } else {
            $Action = 'preview'
            $Template = Read-Host '模板 JSON 路径（留空可交互填写）'
            if (-not $Template) {
                $kind = Read-Host '邮件类型 standard 或 gm（默认 standard）'
                if (-not $kind) { $kind = 'standard' }
                if ($kind -notin @('standard','gm')) { throw '邮件类型必须为 standard 或 gm。' }
                $sender = Read-Host '发件人目录 ID（默认 gm_yufine）'
                if (-not $sender) { $sender = 'gm_yufine' }
                $title = Read-Host '标题'
                Write-Host '逐行输入正文，单独一行 . 结束：'
                $lines = [Collections.Generic.List[string]]::new()
                do { $line = Read-Host; if ($line -ne '.') { $lines.Add($line) } } while ($line -ne '.')
                $days = Read-MailNumber '有效天数' 7 1 90
                $rewards = [Collections.Generic.List[object]]::new()
                do {
                    $rewardKind = Read-Host '附件类型 currency/material/energy/hero/artifact/equipment/bundle（留空结束）'
                    if (-not $rewardKind) { break }
                    if ($rewardKind -notin @('currency','material','energy','hero','artifact','equipment','bundle')) { throw '附件类型不受支持。' }
                    $rewardKey = Read-Host '附件目录 key（由服务端校验，不接受自定义规格）'
                    $quantity = Read-MailNumber '数量' 1 1 1000000
                    $rewards.Add(@{kind=$rewardKind; key=$rewardKey; quantity=$quantity})
                } while ($rewards.Count -lt 20)
                $inputDirectory = Join-Path $repo '.artifacts/mail-ops'
                [IO.Directory]::CreateDirectory($inputDirectory) | Out-Null
                $temporaryTemplate = Join-Path $inputDirectory ("input-$([guid]::NewGuid().ToString('N')).json")
                @{letterType=$kind; senderId=$sender; title=$title; body=($lines -join "`n"); expiresInDays=$days; attachments=$rewards.ToArray()} |
                    ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $temporaryTemplate -Encoding utf8NoBOM
                $Template = $temporaryTemplate
            }
            $scope = Read-Host '收件范围：1 指定一人或多人；2 白名单文件；3 显式整区（无默认）'
            switch ($scope) {
                '1' { $To = (Read-Host '玩家 ID，多个用英文逗号分隔') -split ',' }
                '2' { $AllowFile = Read-Host 'UTF-8 白名单路径，每行一个玩家 ID' }
                '3' {
                    if ((Read-Host "请输入区服 $Realm 以明确选择整区") -cne $Realm) { throw '整区选择未确认。' }
                    $AllInRealm = [switch]::Present
                }
                default { throw '未明确选择收件范围，已停止。' }
            }
            $DenyFile = Read-Host 'UTF-8 黑名单路径（可留空；只从以上范围排除）'
        }
        Assert-MailArguments
        if ($Action -eq 'preview') {
            $preview = Invoke-MailHost (New-PreviewArguments)
            Show-MailPreview $preview
            if ($preview.recipientCount -gt 0 -and (Read-Host '输入“发送”确认以上冻结预览，其他输入保留预览并退出') -ceq '发送') {
                Show-MailBatch (Invoke-MailHost @('confirm','--preview-id',$preview.previewId))
            }
            return
        }
    }
    $result = switch ($Action) {
        'preview' { Invoke-MailHost (New-PreviewArguments) }
        'confirm' { Invoke-MailHost @('confirm','--preview-id',$PreviewId) }
        'status' { Invoke-MailHost @('status','--batch-id',$BatchId) }
        'retry' { Invoke-MailHost @('retry','--batch-id',$BatchId) }
    }
    if ($Json) { $result | ConvertTo-Json -Depth 32 }
    elseif ($Action -eq 'preview') { Show-MailPreview $result }
    else { Show-MailBatch $result }
} finally {
    if ($temporaryTemplate -and (Test-Path -LiteralPath $temporaryTemplate -PathType Leaf)) {
        Remove-Item -LiteralPath $temporaryTemplate -Force
    }
}
