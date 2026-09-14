if (-not ('OpenPasteKbd' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class OpenPasteKbd {
  [DllImport("user32.dll", SetLastError = true)]
  static extern uint SendInput(uint n, INPUT[] i, int s);
  [StructLayout(LayoutKind.Sequential)]
  struct INPUT { public uint type; public InputUnion u; }
  [StructLayout(LayoutKind.Explicit)]
  struct InputUnion {
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct MOUSEINPUT {
    public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct KEYBDINPUT {
    public ushort vk, scan; public uint flags, time; public IntPtr extra;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct HARDWAREINPUT { public uint msg; public ushort l, h; }
  const uint KEYUP = 2, UNICODE = 4;
  public static void Type(string text) {
    foreach (char c in text) {
      if (c == '\r') continue;
      INPUT[] a = new INPUT[2];
      a[0].type = a[1].type = 1;
      if (c == '\n') {
        a[0].u.ki.vk = a[1].u.ki.vk = 13;
        a[1].u.ki.flags = KEYUP;
      } else {
        a[0].u.ki.scan = a[1].u.ki.scan = c;
        a[0].u.ki.flags = UNICODE;
        a[1].u.ki.flags = UNICODE | KEYUP;
      }
      SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
    }
  }
}
"@
}

Write-Host "Paste text here, then press Enter:"
$text = [Console]::ReadLine()
if ([string]::IsNullOrWhiteSpace($text)) {
  Write-Host "Cancelled."
  exit 0
}

Write-Host "Click the target box in 3 seconds..."
3..1 | ForEach-Object {
  Write-Host $_
  Start-Sleep -Seconds 1
}
[OpenPasteKbd]::Type($text)
Write-Host "Done."
