$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:USERPROFILE 'bin'
$base = 'https://raw.githubusercontent.com/huatingshi/openpaste/main'
New-Item -ItemType Directory -Force -Path $dest | Out-Null

if ($PSScriptRoot) {
  Copy-Item (Join-Path $PSScriptRoot 'openpaste.ps1') (Join-Path $dest 'openpaste.ps1') -Force
  Copy-Item (Join-Path $PSScriptRoot 'openpaste.cmd') (Join-Path $dest 'openpaste.cmd') -Force
} else {
  Invoke-WebRequest "$base/openpaste.ps1" -UseBasicParsing -OutFile (Join-Path $dest 'openpaste.ps1')
  Invoke-WebRequest "$base/openpaste.cmd" -UseBasicParsing -OutFile (Join-Path $dest 'openpaste.cmd')
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ([string]::IsNullOrEmpty($userPath)) { $userPath = '' }
if ($userPath -notlike "*$dest*") {
  $joined = if ($userPath.Trim().Length -eq 0) { $dest } else { $userPath.TrimEnd(';') + ';' + $dest }
  [Environment]::SetEnvironmentVariable('Path', $joined, 'User')
}

Write-Host 'Installed. Open a new terminal and run: openpaste'
