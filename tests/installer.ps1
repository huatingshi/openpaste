$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('openpaste-install-test-' + [guid]::NewGuid().ToString('N'))
$testSource = Join-Path $testRoot 'source'
$testProfile = Join-Path $testRoot 'profile'
New-Item -ItemType Directory -Path $testSource -Force | Out-Null
$files = @('openpaste.ps1', 'openpaste.cmd', 'OpenPasteHost.cs', 'TerminalUi.cs')
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $testSource }

# Redirect both installation and PATH access into this test's fresh sandbox.
$installer = Get-Content -LiteralPath (Join-Path $repo 'install.ps1') -Raw
$installer = $installer.Replace('$env:USERPROFILE', "'" + $testProfile.Replace("'", "''") + "'")
$installer = $installer.Replace("[Environment]::GetEnvironmentVariable('Path', 'User')", '$testState.UserPath')
$installer = $installer.Replace("[Environment]::SetEnvironmentVariable('Path', $" + "joined, 'User')", '$testState.UserPath = $joined')
if ($installer -match 'GetEnvironmentVariable|SetEnvironmentVariable|\$env:USERPROFILE') { throw 'Installer sandbox substitution failed' }
$testInstaller = Join-Path $testSource 'install.ps1'
Set-Content -LiteralPath $testInstaller -Value $installer -Encoding UTF8
$dest = Join-Path $testProfile 'bin'
$testState = @{ UserPath = $dest + '-tools' }
& $testInstaller | Out-Null
foreach ($file in $files) {
  $actual = Get-FileHash -LiteralPath (Join-Path $dest $file)
  $expected = Get-FileHash -LiteralPath (Join-Path $repo $file)
  if ($actual.Hash -ne $expected.Hash) { throw "Installer did not copy $file correctly" }
}
if ($testState.UserPath -ne ($dest + '-tools;' + $dest)) { throw 'PATH substring regression' }
& $testInstaller | Out-Null
if ($testState.UserPath -ne ($dest + '-tools;' + $dest)) { throw 'Repeated install duplicated PATH' }
Write-Output 'PASS sandboxed installation includes all runtime files and updates PATH exactly once'
