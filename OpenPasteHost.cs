using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
        private int interval;
        private bool multiline;
        private readonly Func<string> readCommand;
        private readonly string settingsPath;
        private readonly Action<string, int, bool, bool> updateView;
        private string hotkey;
        private int activeTriggerId = TriggerId;
        private bool triggerRegistered;
        private volatile bool stopping;
        private bool cancelled;

        internal Session(IDesktop desktop, Action<string> report, int interval, bool multiline,
            Func<string> readCommand = null, string settingsPath = null,
            Action<string, int, bool, bool> updateView = null)
        {
            this.desktop = desktop;
            this.report = report;
            this.interval = interval;
            this.multiline = multiline;
            this.readCommand = readCommand;
            this.settingsPath = settingsPath;
            this.updateView = updateView;
        }

        internal void Stop() { stopping = true; }
        internal bool IsStopping { get { return stopping; } }

        internal void Run(string hotkey)
        {
            this.hotkey = hotkey;
            uint modifiers, key;
            Keyboard.ParseHotkey(hotkey, out modifiers, out key);
            desktop.Register(TriggerId, modifiers, key);
            triggerRegistered = true;
            try
            {
                if (updateView != null) RefreshView();
                else
                {
                    report("OpenPaste active: " + hotkey + " types clipboard text. Esc cancels input. Ctrl+C exits.");
                    report(multiline ? "Multiline enabled: line breaks send Enter." : "Line breaks and tabs become spaces.");
                    report("Enter /help for settings or /quit to exit. Settings changes are saved automatically.");
                }
                while (!stopping)
                {
                    string command = readCommand == null ? null : readCommand();
                    if (command != null) ExecuteCommand(command);
                    if (stopping) break;
                    int id;
                    if (desktop.ReadHotkey(out id))
                    {
                        if (triggerRegistered && id == activeTriggerId) Type(null, 0);
                    }
                    else desktop.Sleep(10);
                }
            }
            finally
            {
                if (triggerRegistered) desktop.Unregister(activeTriggerId);
                triggerRegistered = false;
            }
        }

        internal void ExecuteCommand(string line)
        {
            string[] parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string argument = parts.Length == 2 ? parts[1].Trim() : "";
            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "/help":
                        report("/settings                 Show current settings\n" +
                            "/hotkey Ctrl+Alt+P        Change the trigger shortcut\n" +
                            "/speed 30                Milliseconds between characters (0-1000)\n" +
                            "/multiline on|off        Send Enter for line breaks, or use spaces\n" +
                            "/pause   /resume         Disable or enable the shortcut\n" +
                            "/quit                    End this session\n" +
                            "Shortcut, speed and multiline changes are saved automatically.");
                        break;
                    case "/settings":
                    case "/status":
                        report("Hotkey: " + hotkey + " | Speed: " + interval + " ms | Multiline: " +
                            (multiline ? "on" : "off") + " | " + (triggerRegistered ? "active" : "paused"));
                        break;
                    case "/hotkey":
                        ChangeHotkey(argument);
                        ReportSetting("Hotkey: " + hotkey);
                        break;
                    case "/speed":
                        int value;
                        if (!Int32.TryParse(argument, out value) || value < 0 || value > 1000)
                            throw new ArgumentException("Use /speed 0-1000 (milliseconds per character).");
                        interval = value;
                        ReportSetting("Speed: " + interval + " ms per character.");
                        break;
                    case "/multiline":
                        if (!argument.Equals("on", StringComparison.OrdinalIgnoreCase) &&
                            !argument.Equals("off", StringComparison.OrdinalIgnoreCase))
                            throw new ArgumentException("Use /multiline on or /multiline off.");
                        multiline = argument.Equals("on", StringComparison.OrdinalIgnoreCase);
                        ReportSetting(multiline ? "Multiline on: line breaks send Enter and may submit forms." : "Multiline off: line breaks become spaces.");
                        break;
                    case "/pause":
                        if (triggerRegistered) desktop.Unregister(activeTriggerId);
                        triggerRegistered = false;
                        RefreshView();
                        report("Paused. Enter /resume to enable the shortcut.");
                        break;
                    case "/resume":
                        if (!triggerRegistered)
                        {
                            uint modifiers, key;
                            Keyboard.ParseHotkey(hotkey, out modifiers, out key);
                            desktop.Register(activeTriggerId, modifiers, key);
                            triggerRegistered = true;
                        }
                        RefreshView();
                        report("Shortcut active: " + hotkey);
                        break;
                    case "/quit":
                    case "/exit": Stop(); break;
                    default: report("Unknown command. Enter /help for available commands."); break;
                }
            }
            catch (Exception ex) { report("Settings unchanged: " + ex.Message); }
        }

        private void ReportSetting(string message)
        {
            RefreshView();
            if (settingsPath != null)
            {
                try
                {
                    new Settings { Hotkey = hotkey, Interval = interval, Multiline = multiline }.Save(settingsPath);
                    message += " Saved.";
                }
                catch (Exception ex)
                {
                    message += " Applied for this session, but could not save: " + ex.Message;
                }
            }
            report(message);
        }

        private void RefreshView()
        {
            if (updateView != null) updateView(hotkey, interval, multiline, triggerRegistered);
        }

        private void ChangeHotkey(string value)
        {
            uint modifiers, key, oldModifiers, oldKey;
            Keyboard.ParseHotkey(value, out modifiers, out key);
            Keyboard.ParseHotkey(hotkey, out oldModifiers, out oldKey);
            if (modifiers == oldModifiers && key == oldKey) return;
            if (triggerRegistered)
            {
                // Register first: an occupied shortcut must not disable the working one.
                int replacementId = activeTriggerId == TriggerId ? 3 : TriggerId;
                desktop.Register(replacementId, modifiers, key);
                desktop.Unregister(activeTriggerId);
                activeTriggerId = replacementId;
            }
            hotkey = value;
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
                    id == Session.CancelId ? "Cannot register Esc for cancellation. Input was not started." :
                    "Cannot register hotkey. Close the other session or choose another shortcut.");
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

    internal sealed class Settings
    {
        internal string Hotkey = "Ctrl+Alt+V";
        internal int Interval = 10;
        internal bool Multiline;

        internal static Settings Load(string path)
        {
            var settings = new Settings();
            if (!File.Exists(path)) return settings;
            foreach (string line in File.ReadAllLines(path))
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                string[] pair = line.Split(new[] { '=' }, 2);
                if (pair.Length != 2) throw new FormatException("Invalid settings file.");
                switch (pair[0])
                {
                    case "hotkey": settings.Hotkey = pair[1]; break;
                    case "interval_ms": settings.Interval = Int32.Parse(pair[1]); break;
                    case "multiline": settings.Multiline = Boolean.Parse(pair[1]); break;
                    default: throw new FormatException("Unknown setting: " + pair[0]);
                }
            }
            settings.Validate();
            return settings;
        }

        private void Validate()
        {
            uint modifiers, key;
            Keyboard.ParseHotkey(Hotkey, out modifiers, out key);
            if (Hotkey.IndexOfAny(new[] { '\r', '\n' }) >= 0 || Interval < 0 || Interval > 1000)
                throw new FormatException("Invalid saved shortcut or speed.");
        }

        internal void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, new[] { "hotkey=" + Hotkey, "interval_ms=" + Interval,
                    "multiline=" + Multiline }, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static class Program
    {
        public static void Run(string hotkey, int interval, bool multiline)
        {
            var session = new Session(new Desktop(), Console.WriteLine, interval, multiline);
            WithConsoleHandler(session, delegate { session.Run(hotkey); });
        }

        public static void RunConfigured(string hotkey, int interval, int multiline)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenPaste", "settings.ini");
            RunConfiguredAt(hotkey, interval, multiline, path);
        }

        internal static void RunConfiguredAt(string hotkey, int interval, int multiline, string path)
        {
            Settings settings;
            try { settings = Settings.Load(path); }
            catch (Exception ex)
            {
                Console.WriteLine("Could not load saved settings; using defaults: " + ex.Message);
                settings = new Settings();
            }
            if (!String.IsNullOrEmpty(hotkey)) settings.Hotkey = hotkey;
            if (interval >= 0) settings.Interval = interval;
            if (multiline >= 0) settings.Multiline = multiline != 0;
            using (var commands = new ConsoleCommands())
            {
                var session = new Session(new Desktop(), commands.Report, settings.Interval, settings.Multiline,
                    commands.Poll, path, commands.UpdateStatus);
                WithConsoleHandler(session, delegate { session.Run(settings.Hotkey); });
            }
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
