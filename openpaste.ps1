param(
  [switch]$Manual,
  [string]$Hotkey = 'Ctrl+Alt+V',
  [ValidateRange(0, 1000)][int]$IntervalMs = 10,
  [ValidateRange(0, 60)][int]$DelaySeconds = 3,
  [switch]$Multiline,
  [switch]$Help,
  [switch]$HostProcess
)

$ErrorActionPreference = 'Stop'
if ($Help) {
  Write-Output "openpaste [-Hotkey Ctrl+Alt+V] [-IntervalMs 10] [-Multiline]"
  Write-Output "openpaste -Manual [-DelaySeconds 3] [-IntervalMs 10] [-Multiline]"
  Write-Output "Copy text, focus the target field, then press the hotkey."
  Write-Output "Esc cancels input. Ctrl+C or closing this terminal ends the session."
  Write-Output "Line breaks and tabs become spaces; -Multiline sends Enter for line breaks."
  Write-Output "In-session commands: /help /settings /hotkey /speed /multiline /pause /resume /quit"
  Write-Output "Shortcut, speed and multiline changes are saved automatically."
  return
}

if (-not $HostProcess) {
  # PowerShell resolves openpaste.ps1 before openpaste.cmd. Use the same isolated
  # Windows PowerShell host from either entry, including when launched from pwsh.
  $launchCommand = "& '" + $PSCommandPath.Replace("'", "''") + "' -HostProcess"
  foreach ($parameter in $PSBoundParameters.GetEnumerator()) {
    if ($parameter.Value -is [Management.Automation.SwitchParameter]) {
      $launchCommand += ' -' + $parameter.Key + ':$' + $parameter.Value.IsPresent.ToString().ToLowerInvariant()
    } else {
      $launchCommand += ' -' + $parameter.Key + " '" + ([string]$parameter.Value).Replace("'", "''") + "'"
    }
  }
  $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($launchCommand))
  & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -STA -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand
  return
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
  $savedHotkey = if ($PSBoundParameters.ContainsKey('Hotkey')) { $Hotkey } else { $null }
  $savedInterval = if ($PSBoundParameters.ContainsKey('IntervalMs')) { $IntervalMs } else { -1 }
  $savedMultiline = if ($PSBoundParameters.ContainsKey('Multiline')) { [int]$Multiline.IsPresent } else { -1 }
  [OpenPaste.Program]::RunConfigured($savedHotkey, $savedInterval, $savedMultiline)
}
