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
                        bool sent = args[0] == "ctrlc" ? GenerateConsoleCtrlEvent(0, 0) :
                            PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                        if (!sent) Environment.Exit(3);
                    }, null, 500, Timeout.Infinite))
                    {
                        Program.Run("Ctrl+Alt+Shift+F24", 10, false);
                        if (args[0] != "ctrlc") throw new Exception("Console-close test returned unexpectedly.");
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
                        Console.WriteLine("CTRL_C_EXITED_AND_HOTKEY_RELEASED");
                    }
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine(ex); return 1; }
            }
        }
    }
}
