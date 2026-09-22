$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
foreach ($file in @('openpaste.ps1', 'install.ps1')) {
  $parseErrors = $null
  $tokens = $null
  [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo $file), [ref]$tokens, [ref]$parseErrors) | Out-Null
  if ($parseErrors.Count) { throw "$file has parse errors: $parseErrors" }
}
Add-Type -Path (Join-Path $repo 'OpenPasteHost.cs'), (Join-Path $PSScriptRoot 'SessionTests.cs') -ReferencedAssemblies System.Windows.Forms
[OpenPaste.SessionTests]::Run()
& (Join-Path $PSScriptRoot 'installer.ps1')
