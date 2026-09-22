$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:USERPROFILE 'bin'
$base = 'https://raw.githubusercontent.com/huatingshi/openpaste/main'
New-Item -ItemType Directory -Force -Path $dest | Out-Null

foreach ($file in @('openpaste.ps1', 'openpaste.cmd', 'OpenPasteHost.cs', 'TerminalUi.cs')) {
  if ($PSScriptRoot) {
    Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $dest $file) -Force
  } else {
    Invoke-WebRequest "$base/$file" -UseBasicParsing -OutFile (Join-Path $dest $file)
  }
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ([string]::IsNullOrEmpty($userPath)) { $userPath = '' }
$pathEntries = @($userPath -split ';' | ForEach-Object { $_.Trim().TrimEnd('\') })
if ($pathEntries -notcontains $dest.TrimEnd('\')) {
  $joined = if ($userPath.Trim().Length -eq 0) { $dest } else { $userPath.TrimEnd(';') + ';' + $dest }
  [Environment]::SetEnvironmentVariable('Path', $joined, 'User')
}

Write-Host 'Installed. Open a new terminal and run: openpaste'
