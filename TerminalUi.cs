using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OpenPaste
{
    internal sealed class CommandItem
    {
        internal readonly string Name, Description;
        internal readonly bool NeedsArgument;
        internal CommandItem(string name, string description, bool needsArgument = false)
        { Name = name; Description = description; NeedsArgument = needsArgument; }
    }

    // Input editing and selection are independent of the console and never execute commands.
    internal sealed class CommandEditor
    {
        internal static readonly CommandItem[] Commands = {
            new CommandItem("/settings", "查看当前设置"),
            new CommandItem("/hotkey", "修改快捷键，例如 Ctrl+Alt+P", true),
            new CommandItem("/speed", "字符间隔，0–1000 毫秒", true),
            new CommandItem("/multiline", "多行输入：on / off", true),
            new CommandItem("/pause", "暂停快捷键"),
            new CommandItem("/resume", "恢复快捷键"),
            new CommandItem("/help", "查看命令说明"),
            new CommandItem("/quit", "退出当前会话")
        };
        internal string Text = "";
        internal int Cursor, Selected;
        private bool dismissed;
        internal List<CommandItem> Matches
        {
            get
            {
                var matches = new List<CommandItem>();
                if (dismissed || !Text.StartsWith("/") || Text.IndexOfAny(new[] { ' ', '\t' }) >= 0) return matches;
                foreach (CommandItem item in Commands)
                    if (item.Name.StartsWith(Text, StringComparison.OrdinalIgnoreCase)) matches.Add(item);
                return matches;
            }
        }

        internal void Clear() { Text = ""; Cursor = Selected = 0; dismissed = false; }

        internal string Handle(ConsoleKeyInfo key)
        {
            List<CommandItem> matches = Matches;
            if (Selected >= matches.Count) Selected = 0;
            if (key.Key == ConsoleKey.Escape) { dismissed = true; return null; }
            if (matches.Count > 0 && (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow))
            {
                Selected = (Selected + (key.Key == ConsoleKey.DownArrow ? 1 : matches.Count - 1)) % matches.Count;
                return null;
            }
            if (matches.Count > 0 && (key.Key == ConsoleKey.Tab || key.Key == ConsoleKey.Enter))
            {
                CommandItem item = matches[Selected];
                if (key.Key == ConsoleKey.Enter && !item.NeedsArgument) { Clear(); return item.Name; }
                Text = item.Name + (item.NeedsArgument ? " " : "");
                Cursor = Text.Length;
                dismissed = true;
                return null;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                string command = Text.Trim();
                Clear();
                return command.Length == 0 ? null : command;
            }
            if (key.Key == ConsoleKey.LeftArrow) { Cursor = Previous(Text, Cursor); return null; }
            if (key.Key == ConsoleKey.RightArrow) { Cursor = Next(Text, Cursor); return null; }
            if (key.Key == ConsoleKey.Home) { Cursor = 0; return null; }
            if (key.Key == ConsoleKey.End) { Cursor = Text.Length; return null; }
            if (key.Key == ConsoleKey.Backspace && Cursor > 0)
            {
                int previous = Previous(Text, Cursor);
                Text = Text.Remove(previous, Cursor - previous);
                Cursor = previous;
            }
            else if (key.Key == ConsoleKey.Delete && Cursor < Text.Length)
                Text = Text.Remove(Cursor, Next(Text, Cursor) - Cursor);
            else if (!Char.IsControl(key.KeyChar) && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0 && Text.Length < 512)
            {
                Text = Text.Insert(Cursor, key.KeyChar.ToString());
                Cursor++;
            }
            else return null;
            Selected = 0;
            dismissed = false;
            return null;
        }

        internal static int Next(string text, int index)
        {
            if (index >= text.Length) return text.Length;
            return index + (Char.IsHighSurrogate(text[index]) && index + 1 < text.Length && Char.IsLowSurrogate(text[index + 1]) ? 2 : 1);
        }
        private static int Previous(string text, int index)
        {
            if (index <= 0) return 0;
            return index - (index > 1 && Char.IsLowSurrogate(text[index - 1]) && Char.IsHighSurrogate(text[index - 2]) ? 2 : 1);
        }
    }

    internal static class ScreenCells
    {
        private static int CharacterWidth(string text, int index)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, index);
            if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark || Char.IsControl(text[index])) return 0;
            int code = Char.IsHighSurrogate(text[index]) && index + 1 < text.Length && Char.IsLowSurrogate(text[index + 1])
                ? Char.ConvertToUtf32(text, index) : text[index];
            return code >= 0x1100 && (code <= 0x115F || code >= 0x2E80 && code <= 0xA4CF ||
                code >= 0xAC00 && code <= 0xD7A3 || code >= 0xF900 && code <= 0xFAFF ||
                code >= 0xFE10 && code <= 0xFE6F || code >= 0xFF00 && code <= 0xFF60 ||
                code >= 0xFFE0 && code <= 0xFFE6 || code >= 0x1F300 && code <= 0x1FAFF || code >= 0x20000) ? 2 : 1;
        }
        internal static int Width(string text)
        {
            int width = 0;
            for (int index = 0; index < text.Length; index = CommandEditor.Next(text, index)) width += CharacterWidth(text, index);
            return width;
        }
        internal static string Clip(string text, int width)
        {
            int index = 0, cells = 0;
            while (index < text.Length)
            {
                int next = CharacterWidth(text, index);
                if (cells + next > width) break;
                cells += next;
                index = CommandEditor.Next(text, index);
            }
            return text.Substring(0, index);
        }
    }

    internal enum LineTone { Normal, Accent, Muted, Selected }
    internal sealed class TerminalLine
    {
        internal readonly string Text;
        internal readonly LineTone Tone;
        internal TerminalLine(string text, LineTone tone) { Text = text; Tone = tone; }
    }
    internal sealed class TerminalFrame
    {
        internal static readonly string[] Logo = {
            @"                                      _",
            @"  ___  _ __   ___ _ __  _ __   __ _ ___| |_ ___",
            @" / _ \| '_ \ / _ \ '_ \| '_ \ / _" + (char)96 + @" / __| __/ _ \",
            @"| (_) | |_) |  __/ | | | |_) | (_| \__ \ ||  __/",
            @" \___/| .__/ \___|_| |_| .__/ \__,_|___/\__\___|",
            @"      |_|             |_|"
        };
        internal readonly List<TerminalLine> Lines = new List<TerminalLine>();
        internal int CursorRow, CursorColumn;

        internal static TerminalFrame Build(CommandEditor editor, string hotkey, int interval,
            bool multiline, bool active, string feedback, int width, int height)
        {
            width = Math.Max(4, width);
            int limit = Math.Max(6, height - 1);
            var frame = new TerminalFrame();
            Action<string, LineTone> add = delegate(string text, LineTone tone)
                { frame.Lines.Add(new TerminalLine(ScreenCells.Clip(text, width), tone)); };
            if (width >= 54 && limit >= 22)
                foreach (string row in Logo) add(row, LineTone.Accent);
            else add("openpaste", LineTone.Accent);
            if (limit >= 12)
            {
                add("", LineTone.Normal);
                add("把复制的文字，直接打进去。", LineTone.Normal);
            }
            if (width >= 66)
                add("快捷键 " + hotkey + "   间隔 " + interval + " ms   多行 " + (multiline ? "开启" : "关闭") + (active ? "" : "   已暂停"), LineTone.Muted);
            else
            {
                add(hotkey + (active ? "" : " · 已暂停"), LineTone.Muted);
                if (limit >= 12) add("间隔 " + interval + " ms · 多行" + (multiline ? "开启" : "关闭"), LineTone.Muted);
            }
            add(new string('─', Math.Min(width, 70)), LineTone.Muted);
            frame.CursorRow = frame.Lines.Count;
            int start = 0;
            while (ScreenCells.Width(editor.Text.Substring(start, editor.Cursor - start)) >= width - 2)
                start = CommandEditor.Next(editor.Text, start);
            frame.CursorColumn = 2 + ScreenCells.Width(editor.Text.Substring(start, editor.Cursor - start));
            add("› " + (editor.Text.Length == 0 ? "输入 / 查看命令" : editor.Text.Substring(start)),
                editor.Text.Length == 0 ? LineTone.Muted : LineTone.Normal);

            List<CommandItem> matches = editor.Matches;
            int available = Math.Max(0, limit - frame.Lines.Count - 1);
            int feedbackSpace = matches.Count > 0 ? Math.Min(2, Math.Max(0, available - 1)) : available;
            if (!String.IsNullOrEmpty(feedback))
            {
                foreach (string row in feedback.Replace("\r", "").Split('\n'))
                {
                    string remaining = row;
                    do
                    {
                        if (feedbackSpace <= 0) break;
                        string part = ScreenCells.Clip(remaining, width);
                        add(part, LineTone.Normal);
                        feedbackSpace--; available--;
                        remaining = remaining.Substring(part.Length);
                    } while (remaining.Length > 0);
                    if (feedbackSpace <= 0) break;
                }
            }
            if (matches.Count > 0 && available > 0)
            {
                int selected = Math.Min(editor.Selected, matches.Count - 1);
                int first = Math.Max(0, selected - available + 1);
                for (int index = first; index < Math.Min(matches.Count, first + available); index++)
                {
                    string text = (index == selected ? "› " : "  ") + matches[index].Name.PadRight(12);
                    if (width >= 30) text += " " + matches[index].Description;
                    add(text, index == selected ? LineTone.Selected : LineTone.Normal);
                }
            }
            add(matches.Count > 0 ? "↑↓ 选择 · Tab 补全 · Enter 确认 · Esc 收起" :
                "设置自动保存 · Esc 取消输入 · Ctrl+C 退出", LineTone.Muted);
            return frame;
        }
    }

    internal sealed class ConsoleCommands : IDisposable
    {
        private readonly CommandEditor editor = new CommandEditor();
        private readonly ConcurrentQueue<string> redirected = new ConcurrentQueue<string>();
        private readonly bool redirectedInput = Console.IsInputRedirected;
        private readonly bool rich;
        private readonly ConsoleColor foreground, background;
        private readonly Encoding originalEncoding;
        private readonly bool originalCursor;
        private readonly IntPtr outputHandle;
        private readonly uint originalMode;
        private readonly bool vt;
        private string hotkey = "Ctrl+Alt+V", feedback = "";
        private int interval = 10, anchor = -1, renderedRows, lastWidth, lastHeight;
        private bool multiline, active = true, started;

        internal ConsoleCommands()
        {
            rich = !redirectedInput && !Console.IsOutputRedirected;
            if (rich)
            {
                foreground = Console.ForegroundColor;
                background = Console.BackgroundColor;
                originalCursor = Console.CursorVisible;
                originalEncoding = Console.OutputEncoding;
                Console.OutputEncoding = new UTF8Encoding(false);
                outputHandle = TerminalNative.GetStdHandle(-11);
                uint mode;
                if (TerminalNative.GetConsoleMode(outputHandle, out mode))
                {
                    originalMode = mode;
                    vt = TerminalNative.SetConsoleMode(outputHandle, mode | 4);
                }
            }
            if (redirectedInput)
            {
                var reader = new Thread(delegate()
                {
                    try
                    {
                        string value;
                        while ((value = Console.ReadLine()) != null) redirected.Enqueue(value);
                    }
                    catch (IOException) { }
                    finally { redirected.Enqueue("/quit"); }
                });
                reader.IsBackground = true;
                reader.Start();
            }
        }

        internal void UpdateStatus(string currentHotkey, int currentInterval, bool currentMultiline, bool currentActive)
        {
            hotkey = currentHotkey; interval = currentInterval; multiline = currentMultiline; active = currentActive;
            if (!started && !rich)
            {
                Console.WriteLine("openpaste");
                Console.WriteLine("Hotkey: " + hotkey + " | Speed: " + interval + " ms | Multiline: " + (multiline ? "on" : "off"));
                Console.WriteLine("Type / for commands. Settings changes are saved automatically. Ctrl+C exits.");
            }
            started = true;
            Render();
        }

        internal string Poll()
        {
            if (redirectedInput)
            {
                string value;
                return redirected.TryDequeue(out value) ? value : null;
            }
            // Limit each pass so a pasted command stream cannot starve the hotkey loop.
            for (int count = 0; count < 64 && Console.KeyAvailable; count++)
            {
                string command = editor.Handle(Console.ReadKey(true));
                feedback = "";
                Render();
                if (command != null) return command;
            }
            if (rich && (Math.Min(Console.BufferWidth, Console.WindowWidth) - 1 != lastWidth || Console.WindowHeight != lastHeight)) Render();
            return null;
        }

        internal void Report(string message)
        {
            if (!rich) { Console.WriteLine(message); return; }
            feedback = message;
            Render();
        }

        private void ResetColors()
        {
            if (vt) Console.Write("\x1b[0m");
            Console.ForegroundColor = foreground;
            Console.BackgroundColor = background;
        }

        private void Render()
        {
            if (!rich || !started) return;
            try
            {
                int width = Math.Max(4, Math.Min(Console.BufferWidth, Console.WindowWidth) - 1);
                int height = Console.WindowHeight;
                if (Console.BufferWidth < 5 || height < 7) return;
                TerminalFrame frame = TerminalFrame.Build(editor, hotkey, interval, multiline, active, feedback, width, height);
                int clearRows = Math.Min(Math.Max(renderedRows, frame.Lines.Count), Console.BufferHeight - 1);
                int clearWidth = Math.Min(Math.Max(lastWidth, width) + 1, Console.BufferWidth);
                if (anchor < 0) anchor = Console.CursorTop;
                int overflow = Math.Max(0, anchor + clearRows + 1 - Console.BufferHeight);
                if (overflow > 0)
                {
                    Console.SetCursorPosition(0, Console.BufferHeight - 1);
                    Console.Write(new string('\n', overflow));
                    anchor = Math.Max(0, anchor - overflow);
                }
                Console.CursorVisible = false;
                // Move down first so the complete frame becomes visible in a scrollback buffer.
                Console.SetCursorPosition(0, anchor + clearRows - 1);
                for (int row = 0; row < clearRows; row++)
                {
                    Console.SetCursorPosition(0, anchor + row);
                    ResetColors();
                    TerminalNative.ClearLine(outputHandle, anchor + row, clearWidth,
                        (ushort)((int)foreground | ((int)background << 4)));
                    string text = "";
                    if (row < frame.Lines.Count)
                    {
                        TerminalLine line = frame.Lines[row];
                        text = line.Text;
                        if (line.Tone == LineTone.Accent)
                        {
                            if (vt) Console.Write("\x1b[38;2;113;221;176m");
                            else Console.ForegroundColor = ConsoleColor.Cyan;
                        }
                        else if (line.Tone == LineTone.Muted) Console.ForegroundColor = ConsoleColor.DarkGray;
                        else if (line.Tone == LineTone.Selected)
                        {
                            if (vt) Console.Write("\x1b[48;2;32;57;44m\x1b[38;2;224;233;228m");
                            else { Console.BackgroundColor = ConsoleColor.DarkCyan; Console.ForegroundColor = ConsoleColor.White; }
                        }
                    }
                    if (row < frame.Lines.Count && frame.Lines[row].Tone == LineTone.Selected)
                        text += new string(' ', Math.Max(0, width - ScreenCells.Width(text)));
                    Console.Write(text);
                }
                ResetColors();
                Console.SetCursorPosition(frame.CursorColumn, anchor + frame.CursorRow);
                renderedRows = frame.Lines.Count;
                lastWidth = width; lastHeight = height;
            }
            catch (ArgumentOutOfRangeException) { anchor = -1; renderedRows = 0; }
            catch (IOException) { anchor = -1; renderedRows = 0; }
            finally { Console.CursorVisible = originalCursor; }
        }

        public void Dispose()
        {
            if (!rich) return;
            try
            {
                editor.Clear();
                feedback = "";
                Render();
                ResetColors();
                if (anchor >= 0)
                {
                    Console.SetCursorPosition(0, Math.Min(anchor + renderedRows, Console.BufferHeight - 1));
                    Console.WriteLine();
                }
            }
            finally
            {
                if (vt) TerminalNative.SetConsoleMode(outputHandle, originalMode);
                Console.CursorVisible = originalCursor;
                Console.OutputEncoding = originalEncoding;
            }
        }
    }

    internal static class TerminalNative
    {
        [StructLayout(LayoutKind.Sequential)] private struct COORD { public short x, y; }
        [DllImport("kernel32.dll")] internal static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll")] internal static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        [DllImport("kernel32.dll")] internal static extern bool SetConsoleMode(IntPtr handle, uint mode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool FillConsoleOutputCharacter(IntPtr handle, char value, uint count, COORD position, out uint written);
        [DllImport("kernel32.dll")]
        private static extern bool FillConsoleOutputAttribute(IntPtr handle, ushort attribute, uint count, COORD position, out uint written);
        internal static void ClearLine(IntPtr handle, int row, int width, ushort attribute)
        {
            uint written;
            var position = new COORD { x = 0, y = (short)row };
            FillConsoleOutputCharacter(handle, ' ', (uint)width, position, out written);
            FillConsoleOutputAttribute(handle, attribute, (uint)width, position, out written);
        }
    }
}
