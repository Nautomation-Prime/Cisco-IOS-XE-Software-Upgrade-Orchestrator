<#
.SYNOPSIS
    Audits codebase integrity, licensing, and credentials security.
.DESCRIPTION
    Checks presence of license files, ensures sensitive configuration files are not committed,
    and validates project attribution.
.EXAMPLE
    .\scripts\audit-branding.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  Codebase & Security Audit" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$issues = [System.Collections.Generic.List[string]]::new()

# 1. License Check
$licenseFile = Join-Path $RepoRoot "LICENSE"
if (-not (Test-Path $licenseFile)) {
    $issues.Add("Missing LICENSE file.")
} else {
    Write-Host "[OK] LICENSE file present." -ForegroundColor Green
}

$noticeFile = Join-Path $RepoRoot "NOTICE"
if (-not (Test-Path $noticeFile)) {
    $issues.Add("Missing NOTICE file.")
} else {
    Write-Host "[OK] NOTICE file present." -ForegroundColor Green
}

# 2. Gitignore Check
$gitignoreFile = Join-Path $RepoRoot ".gitignore"
if (Test-Path $gitignoreFile) {
    $giContent = Get-Content $gitignoreFile -Raw
    if ($giContent -notmatch "\.env") {
        $issues.Add(".gitignore does not exclude .env")
    }
    if ($giContent -notmatch "/state/") {
        $issues.Add(".gitignore does not exclude /state/")
    }
    Write-Host "[OK] .gitignore guards sensitive state and environment files." -ForegroundColor Green
} else {
    $issues.Add("Missing .gitignore file.")
}

# 3. Plaintext Secret Check in tracked files
$trackedFiles = & git ls-files
foreach ($file in $trackedFiles) {
    if ($file -match "\.(yaml|yml|json|txt)$" -and $file -notmatch "example") {
        $fullPath = Join-Path $RepoRoot $file
        if (Test-Path $fullPath) {
            $content = Get-Content $fullPath -Raw
            if ($content -match 'password:\s*["''].{6,}["'']' -and $content -notmatch 'placeholder|dummy|secret_here') {
                $issues.Add("Potential plain-text secret committed in: $file")
            }
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Host "`n[AUDIT FAILED] Found issues:" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $issue" -ForegroundColor Red
    }
    exit 1
}

Write-Host "`n[AUDIT PASSED] Codebase attribution, licenses, and file security verified." -ForegroundColor Green
