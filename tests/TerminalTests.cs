using System;
using System.Collections.Generic;

namespace OpenPaste
{
    public static class TerminalTests
    {
        private static int passed;
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static ConsoleKeyInfo Key(ConsoleKey key) { return new ConsoleKeyInfo('\0', key, false, false, false); }
        private static void Type(CommandEditor editor, string text)
        {
            foreach (char value in text) editor.Handle(new ConsoleKeyInfo(value, (ConsoleKey)0, false, false, false));
        }
        private static void Test(string name, Action action)
        { action(); passed++; Console.WriteLine("PASS " + name); }

        public static void Run()
        {
            Test("slash opens the command list and typing filters it", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/");
                Assert(editor.Matches.Count == 8, "slash menu did not open");
                Type(editor, "SP");
                Assert(editor.Matches.Count == 1 && editor.Matches[0].Name == "/speed", "case-insensitive filtering failed");
                Type(editor, "x");
                Assert(editor.Matches.Count == 0, "unknown prefix showed a suggestion");
                editor.Handle(Key(ConsoleKey.Backspace));
                Assert(editor.Matches.Count == 1, "editing did not restore matches");
            });
            Test("arrows wrap selection and Enter executes a selected command", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/");
                editor.Handle(Key(ConsoleKey.UpArrow));
                Assert(editor.Selected == 7, "up did not wrap");
                editor.Handle(Key(ConsoleKey.DownArrow));
                Assert(editor.Selected == 0, "down did not wrap");
                editor.Handle(Key(ConsoleKey.UpArrow));
                Assert(editor.Handle(Key(ConsoleKey.Enter)) == "/quit" && editor.Text == "", "selected command did not execute once");
            });
            Test("parameter selection waits for a user-supplied value", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/sp");
                Assert(editor.Handle(Key(ConsoleKey.Enter)) == null && editor.Text == "/speed ", "selection applied a default value");
                Assert(editor.Matches.Count == 0, "menu should close while entering arguments");
                Type(editor, "30");
                Assert(editor.Handle(Key(ConsoleKey.Enter)) == "/speed 30", "argument not submitted");
            });
            Test("Tab completes without executing a command", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/q");
                Assert(editor.Handle(Key(ConsoleKey.Tab)) == null && editor.Text == "/quit", "Tab executed or failed completion");
                Assert(editor.Matches.Count == 0, "Tab did not dismiss the menu");
                Assert(editor.Handle(Key(ConsoleKey.Enter)) == "/quit", "completed command not submitted");
            });
            Test("Esc dismisses suggestions while preserving the editable text", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/s");
                editor.Handle(Key(ConsoleKey.Escape));
                Assert(editor.Text == "/s" && editor.Matches.Count == 0, "Esc erased text or left the menu open");
                Type(editor, "p");
                Assert(editor.Matches.Count == 1 && editor.Matches[0].Name == "/speed", "editing did not reopen the menu");
            });
            Test("cursor editing supports insertion, delete, backspace, home and end", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/speed 31");
                editor.Handle(Key(ConsoleKey.LeftArrow));
                editor.Handle(Key(ConsoleKey.Delete));
                Type(editor, "0");
                Assert(editor.Text == "/speed 30", "in-place editing failed");
                editor.Handle(Key(ConsoleKey.Home));
                editor.Handle(Key(ConsoleKey.Delete));
                Type(editor, "/");
                editor.Handle(Key(ConsoleKey.End));
                editor.Handle(Key(ConsoleKey.Backspace));
                Assert(editor.Text == "/speed 3" && editor.Cursor == editor.Text.Length, "home/end editing failed");
            });
            Test("aliases and full parameter commands bypass completion", delegate
            {
                foreach (string value in new[] { "/status", "/exit", "/hotkey Ctrl+Alt+P", "/multiline off" })
                {
                    var editor = new CommandEditor();
                    Type(editor, value);
                    Assert(editor.Handle(Key(ConsoleKey.Enter)) == value, "changed command " + value);
                }
            });
            Test("Unicode cell sizing keeps Chinese and surrogate pairs intact", delegate
            {
                Assert(ScreenCells.Width("中😀A") == 5, "incorrect wide-cell count");
                Assert(ScreenCells.Clip("中😀A", 3) == "中", "cut a wide character");
                Assert(ScreenCells.Clip("中😀A", 4) == "中😀", "cut a surrogate pair");
                Assert(ScreenCells.Width("e\u0301") == 1, "combining mark occupied a cell");
                var editor = new CommandEditor();
                Type(editor, "中😀");
                editor.Handle(Key(ConsoleKey.Backspace));
                Assert(editor.Text == "中", "backspace left half an emoji");
            });
            Test("logo and menu frames fit narrow, short and wide consoles", delegate
            {
                foreach (int width in new[] { 20, 32, 54, 80, 120 })
                foreach (int height in new[] { 8, 12, 24, 32 })
                {
                    var editor = new CommandEditor();
                    Type(editor, "/");
                    editor.Handle(Key(ConsoleKey.UpArrow));
                    TerminalFrame frame = TerminalFrame.Build(editor, "Ctrl+Alt+Shift+F24", 30, true, true, "", width, height);
                    Assert(frame.Lines.Count < height, "frame taller than viewport");
                    Assert(frame.CursorRow < frame.Lines.Count && frame.CursorColumn < width, "cursor outside frame");
                    int selected = 0;
                    foreach (TerminalLine row in frame.Lines)
                    {
                        Assert(ScreenCells.Width(row.Text) <= width, "row overflow");
                        if (row.Tone == LineTone.Selected) { selected++; Assert(row.Text.Contains("/quit"), "selected option hidden"); }
                    }
                    Assert(selected == 1, "selection scrolled out of the menu");
                    if (width < 75) Assert(frame.Lines[1].Text == "openpaste", "compact logo missing");
                }
            });
            Test("long command lines scroll horizontally to keep the cursor visible", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/hotkey " + new string('A', 150));
                TerminalFrame frame = TerminalFrame.Build(editor, "Ctrl+Alt+V", 10, false, true, "", 32, 24);
                Assert(frame.CursorColumn < 32 && frame.Lines[frame.CursorRow].Text.EndsWith("A"), "long input overflow");
                editor.Handle(Key(ConsoleKey.Home));
                frame = TerminalFrame.Build(editor, "Ctrl+Alt+V", 10, false, true, "", 32, 24);
                Assert(frame.CursorColumn == 2 && frame.Lines[frame.CursorRow].Text.StartsWith("› /hotkey"), "home did not reveal the beginning");
            });
            Test("live status displays effective settings and pause state", delegate
            {
                TerminalFrame frame = TerminalFrame.Build(new CommandEditor(), "Ctrl+Alt+P", 75, true, false, "", 100, 30);
                string text = String.Join("\n", frame.Lines.ConvertAll(row => row.Text));
                Assert(text.Contains("Ctrl+Alt+P") && text.Contains("75 ms") && text.Contains("开启") && text.Contains("已暂停"), "stale status values");
                Assert(frame.Lines[1].Text == TerminalFrame.Logo[0], "wide logo missing");
            });
            Test("reports preserve a partially edited command and its menu selection", delegate
            {
                var editor = new CommandEditor();
                Type(editor, "/");
                editor.Handle(Key(ConsoleKey.DownArrow));
                TerminalFrame frame = TerminalFrame.Build(editor, "Ctrl+Alt+V", 10, false, true, "Input cancelled.", 80, 30);
                Assert(editor.Text == "/" && editor.Selected == 1, "report changed editor state");
                Assert(frame.Lines.Exists(row => row.Tone == LineTone.Selected && row.Text.Contains("/hotkey")), "selection was lost");
                Assert(frame.Lines.Exists(row => row.Text.Contains("Input cancelled.")), "menu hid the input result");
            });
            Console.WriteLine(passed + " terminal tests passed.");
        }
    }
}
