using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OpenPaste
{
    // Every input and close signal is confined to this helper's own console.
    public static class ConsoleSmoke
    {
        [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList(uint[] processes, uint count);
        [DllImport("kernel32.dll")] static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool WriteConsoleInput(IntPtr input, INPUT_RECORD[] records, uint count, out uint written);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool ReadConsoleOutput(IntPtr output, [Out] CHAR_INFO[] cells, COORD size, COORD origin, ref SMALL_RECT region);
        [StructLayout(LayoutKind.Sequential)] struct COORD { public short x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SMALL_RECT { public short left, top, right, bottom; }
        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)] struct CHAR_INFO
        {
            [FieldOffset(0)] public char character;
            [FieldOffset(2)] public ushort attributes;
        }
        [StructLayout(LayoutKind.Explicit, Size = 20)]
        struct INPUT_RECORD
        {
            [FieldOffset(0)] public ushort eventType;
            [FieldOffset(4)] public int down;
            [FieldOffset(8)] public ushort repeat;
            [FieldOffset(10)] public ushort key;
            [FieldOffset(14)] public char character;
        }

        private static void WriteKey(ushort key, char character)
        {
            var record = new INPUT_RECORD { eventType = 1, down = 1, repeat = 1, key = key, character = character };
            var release = record;
            release.down = 0;
            uint written;
            if (!WriteConsoleInput(GetStdHandle(-10), new[] { record, release }, 2, out written) || written != 2)
                throw new Exception("Cannot write to test console input.");
        }
        private static void Key(ConsoleKey key)
        {
            char character = key == ConsoleKey.Enter ? '\r' : key == ConsoleKey.Tab ? '\t' :
                key == ConsoleKey.Backspace ? '\b' : key == ConsoleKey.Escape ? (char)27 : '\0';
            WriteKey((ushort)key, character);
        }
        private static void Type(string text)
        {
            foreach (char character in text)
                if (character == '\r') Key(ConsoleKey.Enter);
                else WriteKey(0, character);
        }
        private static string Screen()
        {
            int width = Console.WindowWidth, height = Console.WindowHeight;
            var cells = new CHAR_INFO[width * height];
            var region = new SMALL_RECT { left = (short)Console.WindowLeft, top = (short)Console.WindowTop, right = (short)(Console.WindowLeft + width - 1),
                bottom = (short)(Console.WindowTop + height - 1) };
            if (!ReadConsoleOutput(GetStdHandle(-11), cells, new COORD { x = (short)width, y = (short)height },
                new COORD(), ref region)) throw new Exception("Cannot read test console.");
            var rows = new StringBuilder();
            for (int offset = 0; offset < cells.Length; offset += width)
            {
                var row = new StringBuilder();
                for (int column = 0; column < width; column++)
                    if ((cells[offset + column].attributes & 0x200) == 0) row.Append(cells[offset + column].character);
                rows.AppendLine(row.ToString().TrimEnd());
            }
            return rows.ToString();
        }
        private static string WaitScreen(Func<string, bool> expected, string description)
        {
            var timer = Stopwatch.StartNew();
            string screen = "";
            while (timer.ElapsedMilliseconds < 3000)
            {
                screen = Screen();
                if (expected(screen)) return screen;
                Thread.Sleep(20);
            }
            throw new Exception(description + "\n" + screen);
        }
        private static void MenuChecks(string directory)
        {
            File.WriteAllText(Path.Combine(directory, "startup.txt"),
                WaitScreen(x => x.Contains("Ctrl+Alt+Shift+F24") && x.Contains("|_|"), "Logo missing"));
            Type("/");
            File.WriteAllText(Path.Combine(directory, "menu.txt"),
                WaitScreen(x => x.Contains("/settings") && x.Contains("/quit") && x.Contains("/multiline"), "Menu did not open"));
            Key(ConsoleKey.DownArrow);
            Key(ConsoleKey.DownArrow);
            WaitScreen(x => x.Contains("› /speed"), "Arrow selection did not move");
            Key(ConsoleKey.Tab);
            WaitScreen(x => x.Contains("› /speed") && !x.Contains("/settings"), "Tab did not complete and erase menu");
            Type("37\r");
            WaitScreen(x => x.Contains("37 ms") && x.Contains("Saved."), "Speed did not apply and save");
            Type("/mu");
            File.WriteAllText(Path.Combine(directory, "filtered.txt"),
                WaitScreen(x => x.Contains("/multiline") && !x.Contains("/settings"), "Filtering left stale rows"));
            Key(ConsoleKey.Escape);
            File.WriteAllText(Path.Combine(directory, "dismissed.txt"),
                WaitScreen(x => x.Contains("› /mu") && !x.Contains("/multiline"), "Esc left menu content behind"));
            Console.SetWindowSize(32, 12);
            File.WriteAllText(Path.Combine(directory, "compact.txt"),
                WaitScreen(x => x.Contains("openpaste") && x.Contains("› /mu") && !x.Contains("__"), "Resize did not draw a clean compact layout"));
            Console.SetWindowSize(80, 30);
            WaitScreen(x => x.Contains("|_|") && x.Contains("37 ms") && x.Contains("› /mu"), "Expanded layout lost its state");
            Key(ConsoleKey.Backspace); Key(ConsoleKey.Backspace); Key(ConsoleKey.Backspace);
            Type("/q\r");
        }

        private static void HostChecks(string directory)
        {
            File.WriteAllText(Path.Combine(directory, "host-startup.txt"),
                WaitScreen(x => x.Contains("Ctrl+Alt+Shift+F24") && x.Contains("|_|"), "PowerShell entry did not show the wordmark"));
            Type("/");
            File.WriteAllText(Path.Combine(directory, "host-menu.txt"),
                WaitScreen(x => x.Contains("/settings") && x.Contains("/quit"), "PowerShell entry did not open the menu"));
            Type("q\r");
        }

        [STAThread]
        public static int Main(string[] args)
        {
            using (var file = new StreamWriter(args[1]))
            {
                file.AutoFlush = true;
                TextWriter log = TextWriter.Synchronized(file);
                try
                {
                    var processes = new uint[4];
                    if (GetConsoleProcessList(processes, 4) != 1)
                        throw new Exception("Refusing to signal a shared or missing console.");
                    IntPtr window = GetConsoleWindow();
                    if (window == IntPtr.Zero) throw new Exception("Missing dedicated console.");
                    Console.SetWindowSize(80, 30);
                    ConsoleColor foreground = Console.ForegroundColor, background = Console.BackgroundColor;
                    bool cursorVisible = Console.CursorVisible;
                    uint originalMode;
                    TerminalNative.GetConsoleMode(GetStdHandle(-11), out originalMode);
                    string settings = Path.Combine(Path.GetDirectoryName(args[1]), args[0] + "-settings.ini");
                    using (var timeout = new Timer(delegate { Environment.Exit(2); }, null, 15000, Timeout.Infinite))
                    using (var signal = new Timer(delegate
                    {
                        try
                        {
                            log.WriteLine("SIGNAL " + args[0]);
                            if (args[0] == "host") HostChecks(Path.GetDirectoryName(args[1]));
                            else if (args[0] == "menu") MenuChecks(Path.GetDirectoryName(args[1]));
                            else if (args[0] == "commands") Type("/speed 30\r/multiline on\r/settings\r/multiline off\r/quit\r");
                            else if (args[0] == "ctrlc")
                            {
                                if (!GenerateConsoleCtrlEvent(0, 0)) throw new Exception("Ctrl+C signal failed");
                            }
                            else if (!PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero)) throw new Exception("Close signal failed");
                        }
                        catch (Exception ex) { log.WriteLine(ex); Environment.Exit(1); }
                    }, null, 500, Timeout.Infinite))
                    {
                        if (args[0] == "host")
                        {
                            var start = new ProcessStartInfo();
                            start.FileName = args.Length > 3 && args[3].Length > 0 ? args[3] :
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                            string entry = args[2] == "openpaste" ? "openpaste" : "& '" + args[2].Replace("'", "''") + "'";
                            start.Arguments = "-STA -NoProfile -ExecutionPolicy Bypass -Command \"" + entry + " -Hotkey Ctrl+Alt+Shift+F24\"";
                            start.UseShellExecute = false;
                            using (Process child = Process.Start(start))
                            {
                                if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("PowerShell entry did not exit"); }
                                if (child.ExitCode != 0) throw new Exception("PowerShell entry failed");
                            }
                        }
                        else Program.RunConfiguredAt("Ctrl+Alt+Shift+F24", 10, 0, settings);
                        if (args[0] == "close") throw new Exception("Console close unexpectedly returned.");
                        uint restoredMode;
                        TerminalNative.GetConsoleMode(GetStdHandle(-11), out restoredMode);
                        // PowerShell itself changes console defaults during startup; direct host runs isolate our restoration.
                        if (args[0] != "host" && (restoredMode != originalMode || Console.ForegroundColor != foreground || Console.BackgroundColor != background || Console.CursorVisible != cursorVisible))
                            throw new Exception("Console appearance or output mode was not restored.");
                        Exception registrationError = null;
                        var check = new Thread(delegate()
                        {
                            try
                            {
                                var desktop = new Desktop();
                                desktop.Register(99, 7, 0x87);
                                desktop.Unregister(99);
                            }
                            catch (Exception ex) { registrationError = ex; }
                        });
                        check.Start(); check.Join();
                        if (registrationError != null) throw registrationError;
                        if (args[0] == "commands" && (Settings.Load(settings).Interval != 30 || Settings.Load(settings).Multiline))
                            throw new Exception("Command settings were not saved.");
                        if (args[0] == "menu" && Settings.Load(settings).Interval != 37) throw new Exception("Menu command was not saved.");
                        log.WriteLine(args[0].ToUpperInvariant() + "_EXITED_AND_HOTKEY_RELEASED");
                    }
                    return 0;
                }
                catch (Exception ex) { log.WriteLine(ex); return 1; }
            }
        }
    }
}
