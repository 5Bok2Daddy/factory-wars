param(
  [string]$Message = "[gemini] Auto-sync progress, screenshots, and verification report",
  [string]$Agent = "gemini"
)

$ErrorActionPreference = "Stop"
$WorkspaceRoot = "C:\Users\MinGyu\FactoryWars"
$RepoDir = Join-Path $WorkspaceRoot "Game"

Write-Host "=== [sync_github.ps1] Starting GitHub Auto-Sync ==="

# 1. Remove stale Git lock if present
$lockFile = Join-Path $RepoDir ".git\index.lock"
if (Test-Path $lockFile) {
  Remove-Item -Force $lockFile -ErrorAction SilentlyContinue
  Write-Host "Removed stale .git/index.lock"
}

# 2. Ensure Git large push buffer
git -C $RepoDir config http.postBuffer 524288000

# 3. Guard against >50MB non-ignored files
$oversized = Get-ChildItem -Path $RepoDir -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
  $_.Length -gt 50MB -and $_.FullName -notmatch "\\(\.git|Library|Temp|Logs|Obj|Build|Builds|FactoryWars|ExternalAssets|FactoryWarsArt)\\" -and $_.Extension -notin @(".apk", ".aab", ".unitypackage", ".zip")
}
if ($oversized) {
  Write-Host "ERROR: Oversized (>50MB) tracked candidate files found:"
  $oversized | Select-Object FullName,@{N='MB';E={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
  exit 2
}

# 4. Enforce fixed-filename screenshot policy & verify case-sensitive status_images links in Game/README.md
$statusDir = Join-Path $RepoDir "Docs\status_images"
$allowedTopImages = @(
  "map_standard.png", "map_desert.png", "map_urban.png", "map_archipelago.png", "map_strait.png",
  "current_scale_overview.png", "current_factory_base.png", "current_factory_complex.png",
  "53_ultimate_flying_carrier.png", "54_ultimate_missile_silo_strike.png",
  "55_individual_units_formation.png", "56_mobile_thumb_hud.png", "57_persistent_battlefield_scars.png",
  "58_unit_scale_comparison.png", "59_no_overlap_mixed_group.png", "46_post_match_mvp_timeline.png",
  "kubold_anim_verify.png"
)
if (Test-Path $statusDir) {
  $stray = Get-ChildItem -Path $statusDir -File -Filter "*.png" | Where-Object { $allowedTopImages -notcontains $_.Name }
  if ($stray) {
    foreach ($s in $stray) {
      Remove-Item -Force $s.FullName
      Write-Host "Pruned obsolete screenshot: $($s.Name)"
    }
  }
}
$gameReadme = Join-Path $RepoDir "README.md"
if (Test-Path $gameReadme) {
  $content = Get-Content $gameReadme -Raw -Encoding UTF8
  $matches = [regex]::Matches($content, 'Docs/status_images/[^"\)\s]+')
  $broken = @()
  foreach ($m in $matches) {
    $rel = $m.Value.Replace('/', '\')
    $full = Join-Path $RepoDir $rel
    if (-not (Test-Path $full)) {
      $broken += $m.Value
    }
  }
  if ($broken.Count -gt 0) {
    Write-Host "ERROR: Broken image links in Game/README.md:"
    $broken | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" }
    exit 3
  }
  Write-Host "Verified $($matches.Count) image links in Game/README.md (0 broken, exact case match)."

  # Sync Game/README.md -> local root README.md with Game/ prefix
  $rootReadme = Join-Path $WorkspaceRoot "README.md"
  $rootContent = $content.Replace("Docs/status_images/", "Game/Docs/status_images/")
  [System.IO.File]::WriteAllText($rootReadme, $rootContent, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host "Synced Game/README.md -> root README.md"
}

# 5. Sync SETUP-REPORT.md across workspace root, Game/, and Game/Docs/
$rootReport = Join-Path $WorkspaceRoot "SETUP-REPORT.md"
if (Test-Path $rootReport) {
  $ts = Get-Date -Format "yyyy-MM-dd HH:mm"
  $reportText = Get-Content $rootReport -Raw -Encoding UTF8
  if ($reportText -match "(?m)^마지막 작업:.*$") {
    $reportText = [regex]::Replace($reportText, "(?m)^마지막 작업:.*$", "마지막 작업: [$Agent] $ts / GitHub 자동 동기화 완료")
    [System.IO.File]::WriteAllText($rootReport, $reportText, (New-Object System.Text.UTF8Encoding($false)))
  }
  Copy-Item -Force $rootReport (Join-Path $RepoDir "SETUP-REPORT.md")
  Copy-Item -Force $rootReport (Join-Path $RepoDir "Docs\SETUP-REPORT.md")
  Write-Host "Synced SETUP-REPORT.md -> Game/SETUP-REPORT.md & Game/Docs/SETUP-REPORT.md"
}

# 6. Stage, commit, pull --rebase, push
git -C $RepoDir add -A
$staged = git -C $RepoDir status --porcelain
if ($staged) {
  git -C $RepoDir commit -m $Message
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} else {
  Write-Host "No new changes to commit."
}

git -C $RepoDir pull --rebase origin main
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

git -C $RepoDir push origin main
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$headHash = (git -C $RepoDir rev-parse --short HEAD).Trim()
Write-Host "=== [sync_github.ps1] LIVE SYNC COMPLETE :: HEAD=$headHash ==="
git -C $RepoDir status -sb
