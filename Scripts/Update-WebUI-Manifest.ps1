$ErrorActionPreference = "Stop"

function Get-RepoRoot {
  $scriptDir = $PSScriptRoot
  if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent $PSCommandPath
  }
  if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = (Get-Location).Path
  }
  return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

$root = Get-RepoRoot
$projectRoot = Join-Path $root "Local-Network-Messenger"
$web = Join-Path $projectRoot "WebUI"
$out = Join-Path $projectRoot "WebUI.manifest.json"

if (!(Test-Path $web)) {
  Write-Error "WebUI klasoru bulunamadi: $web"
}

$files = Get-ChildItem -Path $web -Recurse -File | Sort-Object FullName
$map = [ordered]@{}
foreach ($f in $files) {
  $rel = $f.FullName.Substring($web.Length).TrimStart('\','/')
  $hash = (Get-FileHash -Algorithm SHA256 -Path $f.FullName).Hash.ToLowerInvariant()
  $map[$rel.Replace('\','/')] = $hash
}

$manifest = [ordered]@{
  version = 1
  files = $map
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $out -Encoding UTF8
Write-Host "Manifest guncellendi: $out"
