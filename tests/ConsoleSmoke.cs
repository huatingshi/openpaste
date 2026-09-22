using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenPaste
{
    // This executable owns a separate hidden console. It never triggers text input.
    public static class ConsoleSmoke
    {
        [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList(uint[] processes, uint count);
        [DllImport("kernel32.dll")] static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool WriteConsoleInput(IntPtr input, INPUT_RECORD[] records, uint count, out uint written);
        [StructLayout(LayoutKind.Explicit, Size = 20)]
        struct INPUT_RECORD
        {
            [FieldOffset(0)] public ushort eventType;
            [FieldOffset(4)] public int down;
            [FieldOffset(8)] public ushort repeat;
            [FieldOffset(10)] public ushort key;
            [FieldOffset(14)] public char character;
        }

        private static bool WriteCommands()
        {
            // This is the helper's own input buffer, never the foreground app's keyboard.
            string commands = "/speed 30\r/multiline on\r/settings\r/multiline off\r/quit\r";
            var records = new INPUT_RECORD[commands.Length];
            for (int index = 0; index < commands.Length; index++)
            {
                records[index].eventType = 1;
                records[index].down = 1;
                records[index].repeat = 1;
                records[index].character = commands[index];
                records[index].key = commands[index] == '\r' ? (ushort)13 : (ushort)0;
            }
            uint written;
            return WriteConsoleInput(GetStdHandle(-10), records, (uint)records.Length, out written) && written == records.Length;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            using (var log = new StreamWriter(args[1]))
            {
                log.AutoFlush = true;
                Console.SetOut(TextWriter.Synchronized(log));
                Console.SetError(Console.Out);
                try
                {
                    var processes = new uint[4];
                    if (GetConsoleProcessList(processes, 4) != 1)
                        throw new Exception("Refusing to signal a shared or missing console.");
                    IntPtr window = GetConsoleWindow();
                    if (window == IntPtr.Zero) throw new Exception("Missing dedicated console window.");
                    using (var timeout = new Timer(delegate { Environment.Exit(2); }, null, 5000, Timeout.Infinite))
                    using (var signal = new Timer(delegate
                    {
                        Console.WriteLine("SIGNAL " + args[0]);
                        bool sent = args[0] == "commands" ? WriteCommands() : args[0] == "ctrlc" ? GenerateConsoleCtrlEvent(0, 0) :
                            PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                        if (!sent) Environment.Exit(3);
                    }, null, 500, Timeout.Infinite))
                    {
                        if (args[0] == "commands") Program.RunConfiguredAt("Ctrl+Alt+Shift+F24", 10, 0,
                            Path.Combine(Path.GetDirectoryName(args[1]), "settings.ini"));
                        else Program.Run("Ctrl+Alt+Shift+F24", 10, false);
                        if (args[0] == "close") throw new Exception("Console-close test returned unexpectedly.");
                        // A different thread must be able to acquire the same shortcut before process exit.
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
                        check.Start();
                        check.Join();
                        if (registrationError != null) throw registrationError;
                        Console.WriteLine(args[0] == "commands" ? "COMMANDS_EXITED_AND_HOTKEY_RELEASED" : "CTRL_C_EXITED_AND_HOTKEY_RELEASED");
                    }
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine(ex); return 1; }
            }
        }
    }
}
