# Run with Windows PowerShell 5.1 on an interactive Windows desktop.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$smokeDir = Join-Path ([IO.Path]::GetTempPath()) ('openpaste-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeDir | Out-Null
$smokeExe = Join-Path $smokeDir 'ConsoleSmoke.exe'
Add-Type -Path (Join-Path $repo 'OpenPasteHost.cs'), (Join-Path $PSScriptRoot 'ConsoleSmoke.cs') -ReferencedAssemblies System.Windows.Forms -OutputAssembly $smokeExe -OutputType ConsoleApplication
foreach ($mode in @('ctrlc', 'close', 'commands')) {
  $log = Join-Path $smokeDir ($mode + '.log')
  $process = Start-Process -FilePath $smokeExe -ArgumentList @($mode, ('"' + $log + '"')) -WindowStyle Hidden -PassThru
  if (-not $process.WaitForExit(8000)) {
    $process.Kill()
    throw "$mode test timed out"
  }
  $output = Get-Content -LiteralPath $log -Raw
  if ($mode -eq 'ctrlc') {
    if ($process.ExitCode -ne 0 -or $output -notmatch 'CTRL_C_EXITED_AND_HOTKEY_RELEASED') { throw $output }
  } elseif ($mode -eq 'commands') {
    if ($process.ExitCode -ne 0 -or $output -notmatch 'COMMANDS_EXITED_AND_HOTKEY_RELEASED' -or $output -notmatch 'Speed: 30 ms \| Multiline: on') { throw $output }
  } elseif ($output -notmatch 'SIGNAL close' -or $process.ExitCode -in @(0, 1, 2, 3)) {
    throw "Console close did not terminate the helper as expected: $output"
  }
  Write-Output "PASS native $mode lifecycle (helper exited, no text input)"
}
Write-Output "Smoke logs: $smokeDir"
