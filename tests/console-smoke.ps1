# Run with Windows PowerShell 5.1 on an interactive Windows desktop.
param(
  [ValidateSet('ctrlc','close','commands','menu','host')][string[]]$Modes = @('ctrlc','close','commands','menu','host'),
  [string]$HostExecutable = '',
  [switch]$Installed
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$smokeDir = Join-Path ([IO.Path]::GetTempPath()) ('openpaste-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeDir | Out-Null
$smokeExe = Join-Path $smokeDir 'ConsoleSmoke.exe'
Add-Type -Path (Join-Path $repo 'OpenPasteHost.cs'), (Join-Path $repo 'TerminalUi.cs'), (Join-Path $PSScriptRoot 'ConsoleSmoke.cs') -ReferencedAssemblies System.Windows.Forms -OutputAssembly $smokeExe -OutputType ConsoleApplication
foreach ($mode in $Modes) {
  $log = Join-Path $smokeDir ($mode + '.log')
  $entry = if ($Installed) { 'openpaste' } else { Join-Path $repo 'openpaste.ps1' }
  $process = Start-Process -FilePath $smokeExe -ArgumentList @($mode, ('"' + $log + '"'), ('"' + $entry + '"'), ('"' + $HostExecutable + '"')) -WindowStyle Hidden -PassThru
  if (-not $process.WaitForExit(18000)) {
    $process.Kill()
    throw "$mode test timed out"
  }
  $output = Get-Content -LiteralPath $log -Raw -Encoding UTF8
  if ($mode -ne 'close') {
    if ($process.ExitCode -ne 0 -or $output -notmatch ($mode.ToUpperInvariant() + '_EXITED_AND_HOTKEY_RELEASED')) { throw $output }
  } elseif ($output -notmatch 'SIGNAL close' -or $process.ExitCode -in @(0, 1, 2, 3)) {
    throw "Console close did not terminate the helper as expected: $output"
  }
  Write-Output "PASS native $mode lifecycle (helper exited, no text input)"
}
Write-Output "Smoke logs: $smokeDir"
