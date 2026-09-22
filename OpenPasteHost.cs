using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OpenPaste
{
    internal interface IDesktop
    {
        IntPtr ForegroundWindow { get; }
        bool ModifiersDown { get; }
        void Register(int id, uint modifiers, uint key);
        void Unregister(int id);
        bool ReadHotkey(out int id);
        string ReadClipboard();
        void Send(string textElement);
        void Sleep(int milliseconds);
    }

    internal sealed class Session
    {
        internal const int TriggerId = 1, CancelId = 2;
        private readonly IDesktop desktop;
        private readonly Action<string> report;
        private readonly int interval;
        private readonly bool multiline;
        private volatile bool stopping;
        private bool cancelled;

        internal Session(IDesktop desktop, Action<string> report, int interval, bool multiline)
        {
            this.desktop = desktop;
            this.report = report;
            this.interval = interval;
            this.multiline = multiline;
        }

        internal void Stop() { stopping = true; }
        internal bool IsStopping { get { return stopping; } }

        internal void Run(string hotkey)
        {
            uint modifiers, key;
            Keyboard.ParseHotkey(hotkey, out modifiers, out key);
            desktop.Register(TriggerId, modifiers, key);
            try
            {
                report("OpenPaste active: " + hotkey + " types clipboard text. Esc cancels input. Ctrl+C exits.");
                report(multiline ? "Multiline enabled: line breaks send Enter." : "Line breaks and tabs become spaces.");
                while (!stopping)
                {
                    int id;
                    if (desktop.ReadHotkey(out id))
                    {
                        if (id == TriggerId) Type(null, 0);
                    }
                    else desktop.Sleep(10);
                }
            }
            finally { desktop.Unregister(TriggerId); }
        }

        // null text means a single clipboard snapshot; manual text comes from the terminal prompt.
        internal void Type(string text, int countdownSeconds)
        {
            bool registered = false;
            cancelled = false;
            try
            {
                IntPtr target = desktop.ForegroundWindow;
                desktop.Register(CancelId, 0, 0x1B);
                registered = true;
                if (text == null)
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        Check(target);
                        try { text = desktop.ReadClipboard(); break; }
                        catch (ExternalException)
                        {
                            if (attempt == 4) throw new InvalidOperationException("Clipboard is busy. Try again.");
                            Wait(50, target);
                        }
                    }
                }
                else
                {
                    for (int left = countdownSeconds; left > 0; left--)
                    {
                        report("Click the target field: " + left + "...");
                        Wait(1000, IntPtr.Zero);
                    }
                    target = desktop.ForegroundWindow;
                }

                if (String.IsNullOrEmpty(text)) { report("Clipboard has no text."); return; }
                text = Normalize(text, multiline);
                var releaseTimer = Stopwatch.StartNew();
                while (desktop.ModifiersDown)
                {
                    if (releaseTimer.ElapsedMilliseconds >= 3000)
                        throw new InvalidOperationException("Release Ctrl, Alt, Shift and Windows keys, then try again.");
                    Wait(10, target);
                }
                if (target == IntPtr.Zero) throw new InvalidOperationException("No target window is active.");

                for (int index = 0; index < text.Length; index++)
                {
                    Check(target);
                    if (desktop.ModifiersDown)
                        throw new OperationCanceledException("Stopped: a modifier key was pressed.");
                    int length = Char.IsHighSurrogate(text[index]) && index + 1 < text.Length &&
                        Char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
                    desktop.Send(text.Substring(index, length));
                    index += length - 1;
                    Wait(interval, target);
                }
                report("Text sent. Ready for the next input.");
            }
            catch (OperationCanceledException ex) { report(ex.Message); }
            catch (Exception ex) { report("Input failed: " + ex.Message); }
            finally
            {
                if (registered)
                {
                    // Discard triggers received while typing, including a final queued repeat.
                    int ignored;
                    while (desktop.ReadHotkey(out ignored)) { }
                    desktop.Unregister(CancelId);
                }
            }
        }

        private void Check(IntPtr target)
        {
            int id;
            while (desktop.ReadHotkey(out id))
                if (id == CancelId) cancelled = true;
            if (stopping) throw new OperationCanceledException("Session stopped.");
            if (cancelled) throw new OperationCanceledException("Input cancelled.");
            if (target != IntPtr.Zero && desktop.ForegroundWindow != target)
                throw new OperationCanceledException("Stopped: the foreground window changed.");
        }

        private void Wait(int milliseconds, IntPtr target)
        {
            Check(target);
            while (milliseconds > 0)
            {
                int step = Math.Min(milliseconds, 10);
                desktop.Sleep(step);
                milliseconds -= step;
                Check(target);
            }
        }

        internal static string Normalize(string text, bool multiline)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
            return multiline ? text : text.Replace('\n', ' ');
        }
    }

    internal sealed class Desktop : IDesktop
    {
        public IntPtr ForegroundWindow { get { return Native.GetForegroundWindow(); } }
        public bool ModifiersDown
        {
            get
            {
                foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                    if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) return true;
                return false;
            }
        }
        public void Register(int id, uint modifiers, uint key)
        {
            if (!Native.RegisterHotKey(IntPtr.Zero, id, modifiers | 0x4000, key))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    id == Session.TriggerId ? "Cannot register hotkey. Close the other session or use -Hotkey Ctrl+Alt+P." :
                    "Cannot register Esc for cancellation. Input was not started.");
        }
        public void Unregister(int id) { Native.UnregisterHotKey(IntPtr.Zero, id); }
        public bool ReadHotkey(out int id)
        {
            Native.MSG message;
            bool found = Native.PeekMessage(out message, IntPtr.Zero, 0x312, 0x312, 1);
            id = found ? (int)message.wParam.ToUInt32() : 0;
            return found;
        }
        public string ReadClipboard() { return Clipboard.GetText(TextDataFormat.UnicodeText); }
        public void Send(string textElement) { Keyboard.Send(textElement); }
        public void Sleep(int milliseconds) { Thread.Sleep(milliseconds); }
    }

    internal static class Keyboard
    {
        internal static void ParseHotkey(string hotkey, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;
            if (String.IsNullOrWhiteSpace(hotkey)) throw new ArgumentException("Hotkey cannot be empty.");
            string[] parts = hotkey.ToUpperInvariant().Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim())
                {
                    case "CTRL": modifiers |= 2; break;
                    case "ALT": modifiers |= 1; break;
                    case "SHIFT": modifiers |= 4; break;
                    case "WIN": modifiers |= 8; break;
                    default: throw new ArgumentException("Use modifiers Ctrl, Alt, Shift or Win, for example Ctrl+Alt+V.");
                }
            }
            string last = parts[parts.Length - 1].Trim();
            int function;
            if (last.Length == 1 && ((last[0] >= 'A' && last[0] <= 'Z') || (last[0] >= '0' && last[0] <= '9')))
                key = last[0];
            else if (last.StartsWith("F") && Int32.TryParse(last.Substring(1), out function) && function >= 1 && function <= 24)
                key = (uint)(0x70 + function - 1);
            else throw new ArgumentException("Hotkey must end with A-Z, 0-9 or F1-F24.");
            if ((modifiers & 11) == 0) throw new ArgumentException("Include Ctrl, Alt or Win in the hotkey.");
        }

        internal static Native.INPUT[] BuildInputs(string textElement)
        {
            var inputs = new Native.INPUT[textElement.Length * 2];
            for (int i = 0; i < textElement.Length; i++)
            {
                int offset = i * 2;
                inputs[offset].type = inputs[offset + 1].type = 1;
                if (textElement[i] == '\n')
                {
                    inputs[offset].u.ki.vk = inputs[offset + 1].u.ki.vk = 13;
                    inputs[offset + 1].u.ki.flags = 2;
                }
                else
                {
                    inputs[offset].u.ki.scan = inputs[offset + 1].u.ki.scan = textElement[i];
                    inputs[offset].u.ki.flags = 4;
                    inputs[offset + 1].u.ki.flags = 6;
                }
            }
            return inputs;
        }

        internal static void Send(string textElement)
        {
            Native.INPUT[] inputs = BuildInputs(textElement);
            uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
            if (sent != inputs.Length)
            {
                int error = Marshal.GetLastWin32Error();
                // A partial batch may leave a key down. Release only keys from this batch.
                if (sent > 0)
                {
                    var releases = new Native.INPUT[inputs.Length / 2];
                    for (int i = 0; i < releases.Length; i++) releases[i] = inputs[i * 2 + 1];
                    Native.SendInput((uint)releases.Length, releases, Marshal.SizeOf(typeof(Native.INPUT)));
                }
                throw new InvalidOperationException("Windows accepted " + sent + "/" + inputs.Length +
                    " input events (error " + error + "). Check the target app's permissions and input support.");
            }
        }
    }

    public static class Program
    {
        public static void Run(string hotkey, int interval, bool multiline)
        {
            var session = new Session(new Desktop(), Console.WriteLine, interval, multiline);
            WithConsoleHandler(session, delegate { session.Run(hotkey); });
        }

        public static bool TypeManual(string text, int delaySeconds, int interval, bool multiline)
        {
            var session = new Session(new Desktop(), Console.WriteLine, interval, multiline);
            WithConsoleHandler(session, delegate { session.Type(text, delaySeconds); });
            return !session.IsStopping;
        }

        private static void WithConsoleHandler(Session session, Action action)
        {
            Native.ControlHandler handler = delegate(uint signal)
            {
                if (signal == 0 || signal == 1) { session.Stop(); return true; }
                return false; // Closing the terminal uses Windows' normal process termination.
            };
            if (!Native.SetConsoleCtrlHandler(handler, true)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { action(); }
            finally
            {
                Native.SetConsoleCtrlHandler(handler, false);
                GC.KeepAlive(handler);
            }
        }
    }

    internal static class Native
    {
        internal delegate bool ControlHandler(uint signal);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetConsoleCtrlHandler(ControlHandler handler, bool add);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        internal static extern bool PeekMessage(out MSG message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x, y;
            public uint lPrivate;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT { public uint type; public InputUnion u; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT { public int dx, dy; public uint mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT { public uint message; public ushort low, high; }
    }
}
