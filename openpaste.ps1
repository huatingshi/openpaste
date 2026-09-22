param(
  [switch]$Manual,
  [string]$Hotkey = 'Ctrl+Alt+V',
  [ValidateRange(0, 1000)][int]$IntervalMs = 10,
  [ValidateRange(0, 60)][int]$DelaySeconds = 3,
  [switch]$Multiline,
  [switch]$Help
)

$ErrorActionPreference = 'Stop'
if ($Help) {
  Write-Output "openpaste [-Hotkey Ctrl+Alt+V] [-IntervalMs 10] [-Multiline]"
  Write-Output "openpaste -Manual [-DelaySeconds 3] [-IntervalMs 10] [-Multiline]"
  Write-Output "Copy text, focus the target field, then press the hotkey."
  Write-Output "Esc cancels input. Ctrl+C or closing this terminal ends the session."
  Write-Output "Line breaks and tabs become spaces; -Multiline sends Enter for line breaks."
  return
}

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
  throw 'An STA session is required. Run openpaste.cmd or powershell -STA -File openpaste.ps1.'
}
if (-not ('OpenPaste.Program' -as [type])) {
  Add-Type -Path (Join-Path $PSScriptRoot 'OpenPasteHost.cs') -ReferencedAssemblies System.Windows.Forms
}

if ($Manual) {
  Write-Host 'Paste text, then Enter. Empty line quits. Ctrl+C exits.'
  while ($true) {
    Write-Host 'Paste here:'
    $text = [Console]::ReadLine()
    if ($null -eq $text -or $text.Length -eq 0) { break }
    if (-not [OpenPaste.Program]::TypeManual($text, $DelaySeconds, $IntervalMs, $Multiline.IsPresent)) { break }
  }
} else {
  [OpenPaste.Program]::Run($Hotkey, $IntervalMs, $Multiline.IsPresent)
}
