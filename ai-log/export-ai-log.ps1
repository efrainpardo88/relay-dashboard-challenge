<#
.SYNOPSIS
  Exports raw Claude Code session transcripts (.jsonl) to readable Markdown.

.DESCRIPTION
  Claude Code stores every session verbatim as JSONL under
  ~/.claude/projects/<slugified-cwd>/<session-id>.jsonl
  This script renders those to Markdown so the raw log is reviewable
  without a JSON viewer. Both the .jsonl and the .md are committed:
  the .jsonl is the unedited source of truth, the .md is for humans.

  Nothing is edited or omitted except that oversized tool results are
  truncated (and explicitly marked as such).

.EXAMPLE
  ./ai-log/export-ai-log.ps1
  ./ai-log/export-ai-log.ps1 -MaxToolResultChars 4000
#>
[CmdletBinding()]
param(
    # Directory holding the session .jsonl files. Defaults to this repo's project dir.
    [string]$SessionDir = "$env:USERPROFILE\.claude\projects\c--repos-personal-qualitara-home-challenge",

    # Where to write the exports. Defaults to ./raw next to this script.
    [string]$OutDir = (Join-Path $PSScriptRoot 'raw'),

    # Tool results longer than this are truncated in the Markdown render.
    [int]$MaxToolResultChars = 2000,

    # Session ids to skip. Used for a side session that explored hosting/DB options
    # and did not feed the implementation.
    [string[]]$ExcludeSessions = @('c34afa98-acea-4977-88f8-e2930f791882')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SessionDir)) { throw "Session dir not found: $SessionDir" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$OutDir = (Resolve-Path $OutDir).Path

function Format-Block {
    param($block)

    switch ($block.type) {
        'text' { return $block.text }

        'thinking' {
            return "<details><summary>[model reasoning]</summary>`n`n``````" +
                   "`n$($block.thinking)`n```````n`n</details>"
        }

        'tool_use' {
            $json = $block.input | ConvertTo-Json -Depth 6 -Compress:$false
            return "**-> tool call: ``$($block.name)``**`n`n``````json`n$json`n``````"
        }

        'tool_result' {
            $c = $block.content
            if ($c -is [array]) { $c = ($c | ForEach-Object { $_.text }) -join "`n" }
            $c = [string]$c
            $note = ''
            if ($c.Length -gt $MaxToolResultChars) {
                $note = "`n... [truncated by export-ai-log.ps1: $($c.Length) chars total]"
                $c = $c.Substring(0, $MaxToolResultChars)
            }
            return "**<- tool result**`n`n``````text`n$c$note`n``````"
        }

        default { return "_[unrendered block type: $($block.type)]_" }
    }
}

Get-ChildItem -Path $SessionDir -Filter *.jsonl | ForEach-Object {
    $sessionFile = $_

    if ($ExcludeSessions -contains $sessionFile.BaseName) {
        Write-Host "Skipped  $($sessionFile.BaseName)  (excluded)"
        return
    }

    $lines = Get-Content -LiteralPath $sessionFile.FullName

    # Keep the unedited source next to the render.
    Copy-Item -LiteralPath $sessionFile.FullName -Destination (Join-Path $OutDir $sessionFile.Name) -Force

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Raw session transcript — ``$($sessionFile.BaseName)``")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Verbatim export of a Claude Code session. Source of truth: ``$($sessionFile.Name)`` (committed alongside).")
    [void]$sb.AppendLine("Rendered by ``ai-log/export-ai-log.ps1``. Unedited except for truncation of long tool results, which is marked inline.")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine()

    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $e = $line | ConvertFrom-Json } catch { continue }
        if ($e.type -notin @('user', 'assistant')) { continue }

        $role = $e.message.role
        $ts = $e.timestamp
        $content = $e.message.content

        $rendered =
            if ($content -is [string]) { $content }
            else { ($content | ForEach-Object { Format-Block $_ }) -join "`n`n" }

        if ([string]::IsNullOrWhiteSpace($rendered)) { continue }

        $label = if ($role -eq 'user') { 'USER' } else { 'ASSISTANT' }
        [void]$sb.AppendLine("## $label  <sub>$ts</sub>")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine($rendered)
        [void]$sb.AppendLine()
        [void]$sb.AppendLine('---')
        [void]$sb.AppendLine()
    }

    $outFile = Join-Path $OutDir "$($sessionFile.BaseName).md"
    [System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
    Write-Host "Exported $($lines.Count) events -> $outFile"
}
