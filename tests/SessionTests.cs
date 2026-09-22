using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenPaste
{
    public static class SessionTests
    {
        private sealed class FakeDesktop : IDesktop
        {
            internal readonly List<string> Sent = new List<string>();
            internal readonly Queue<int> Events = new Queue<int>();
            internal readonly HashSet<int> Registered = new HashSet<int>();
            internal readonly List<string> Messages = new List<string>();
            internal string ClipboardText = "abc";
            internal IntPtr Target = new IntPtr(123);
            internal bool Held;
            internal int Reads, Sleeps, BusyReads, FailedId;
            internal Action OnSend, OnSleep;
            internal bool FailSend;
            public IntPtr ForegroundWindow { get { return Target; } }
            public bool ModifiersDown { get { return Held; } }
            public void Register(int id, uint modifiers, uint key)
            {
                if (id == FailedId) throw new InvalidOperationException("Hotkey occupied.");
                Assert(Registered.Add(id), "duplicate registration");
            }
            public void Unregister(int id) { Assert(Registered.Remove(id), "unregistering someone else's hotkey"); }
            public bool ReadHotkey(out int id)
            {
                id = Events.Count > 0 ? Events.Dequeue() : 0;
                return id != 0;
            }
            public string ReadClipboard()
            {
                Reads++;
                if (Reads <= BusyReads) throw new ExternalException("Clipboard busy");
                return ClipboardText;
            }
            public void Send(string textElement)
            {
                if (FailSend) throw new InvalidOperationException("SendInput returned zero");
                Assert(!Held, "sent while a shortcut modifier was still down");
                Sent.Add(textElement);
                if (OnSend != null) OnSend();
            }
            public void Sleep(int milliseconds) { Sleeps++; if (OnSleep != null) OnSleep(); }
            internal Session Create(bool multiline = false) { return new Session(this, Messages.Add, 10, multiline); }
            internal string Text { get { return String.Concat(Sent); } }
            internal bool Succeeded { get { return Messages.Exists(x => x.StartsWith("Text sent.")); } }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Test(string name, Action action)
        {
            action();
            Console.WriteLine("PASS " + name);
        }

        public static void Run()
        {
            Test("clipboard is read once, whitespace preserved, newlines and tabs flattened", delegate
            {
                var d = new FakeDesktop { ClipboardText = "  A\r\nB\rC\n\tD  " };
                d.OnSend = delegate { d.ClipboardText = "changed"; };
                d.Create().Type(null, 0);
                Assert(d.Text == "  A B C  D  " && d.Reads == 1 && d.Succeeded, "incorrect snapshot or normalization");
                Assert(d.Registered.Count == 0, "Esc leaked");
            });
            Test("multiline, Chinese and emoji retain their input units", delegate
            {
                var d = new FakeDesktop { ClipboardText = "\u4e2d\ud83d\ude00\r\n\r\nX" };
                d.Create(true).Type(null, 0);
                Assert(d.Text == "\u4e2d\ud83d\ude00\n\nX" && d.Sent[1].Length == 2, "surrogate pair or line breaks split");
            });
            Test("Esc cancels after first character and releases its registration", delegate
            {
                var d = new FakeDesktop();
                d.OnSend = delegate { d.Events.Enqueue(Session.CancelId); };
                d.Create().Type(null, 0);
                Assert(d.Text == "a" && !d.Succeeded && d.Registered.Count == 0, "Esc did not stop input");
            });
            Test("window changes stop remaining text", delegate
            {
                var d = new FakeDesktop();
                d.OnSend = delegate { d.Target = new IntPtr(456); };
                d.Create().Type(null, 0);
                Assert(d.Text == "a" && !d.Succeeded, "typed into a different window");
            });
            Test("hotkey modifiers must be released before input", delegate
            {
                var d = new FakeDesktop { Held = true };
                d.OnSleep = delegate { d.Held = false; };
                d.Create().Type(null, 0);
                Assert(d.Sleeps > 0 && d.Text == "abc" && d.Succeeded, "release wait failed");
            });
            Test("pressing a modifier during input cancels", delegate
            {
                var d = new FakeDesktop();
                d.OnSend = delegate { d.Held = true; };
                d.Create().Type(null, 0);
                Assert(d.Text == "a" && !d.Succeeded, "input continued with a modifier down");
            });
            Test("repeated triggers during input are discarded", delegate
            {
                var d = new FakeDesktop();
                d.OnSend = delegate { d.Events.Enqueue(Session.TriggerId); };
                d.Create().Type(null, 0);
                Assert(d.Text == "abc" && d.Events.Count == 0 && d.Reads == 1, "duplicate trigger remained queued");
            });
            Test("temporary clipboard contention retries, persistent contention fails", delegate
            {
                var d = new FakeDesktop { BusyReads = 2 };
                d.Create().Type(null, 0);
                Assert(d.Reads == 3 && d.Text == "abc", "clipboard retry failed");
                d = new FakeDesktop { BusyReads = 99 };
                d.Create().Type(null, 0);
                Assert(d.Reads == 5 && d.Text == "" && !d.Succeeded && d.Registered.Count == 0, "retry was not bounded");
            });
            Test("empty clipboard and failed input never report success", delegate
            {
                var d = new FakeDesktop { ClipboardText = "" };
                d.Create().Type(null, 0);
                Assert(d.Text == "" && !d.Succeeded && d.Registered.Count == 0, "empty clipboard sent");
                d = new FakeDesktop { FailSend = true };
                d.Create().Type(null, 0);
                Assert(!d.Succeeded && d.Messages.Exists(x => x.StartsWith("Input failed:")) && d.Registered.Count == 0, "false success");
            });
            Test("Esc conflicts prevent typing without unregistering another owner", delegate
            {
                var d = new FakeDesktop { FailedId = Session.CancelId };
                d.Create().Type(null, 0);
                Assert(d.Reads == 0 && d.Text == "" && !d.Succeeded, "typed without cancellation available");
            });
            Test("session stop interrupts typing and releases both hotkeys", delegate
            {
                var d = new FakeDesktop();
                var s = d.Create();
                d.Events.Enqueue(Session.TriggerId);
                d.OnSend = s.Stop;
                s.Run("Ctrl+Alt+V");
                Assert(d.Text == "a" && !d.Succeeded && d.Registered.Count == 0, "stop did not release resources");
            });
            Test("idle session stop releases the trigger hotkey", delegate
            {
                var d = new FakeDesktop();
                var s = d.Create();
                d.OnSleep = s.Stop;
                s.Run("Ctrl+Alt+V");
                Assert(d.Registered.Count == 0 && d.Text == "", "idle listener leaked");
            });
            Test("manual countdown can be cancelled before any input", delegate
            {
                var d = new FakeDesktop();
                d.OnSleep = delegate { d.Events.Enqueue(Session.CancelId); };
                d.Create().Type("manual", 3);
                Assert(d.Text == "" && !d.Succeeded && d.Registered.Count == 0, "countdown could not cancel");
            });
            Test("hotkey validation rejects plain typing keys", delegate
            {
                uint modifiers, key;
                Keyboard.ParseHotkey("Ctrl+Alt+V", out modifiers, out key);
                Assert(modifiers == 3 && key == 0x56, "incorrect shortcut");
                Keyboard.ParseHotkey("Ctrl+Shift+F12", out modifiers, out key);
                Assert(modifiers == 6 && key == 0x7B, "incorrect function key");
                foreach (string invalid in new[] { "", "V", "Shift+V", "Ctrl+Esc", "Ctrl+F25", "Meta+V" })
                {
                    bool rejected = false;
                    try { Keyboard.ParseHotkey(invalid, out modifiers, out key); }
                    catch (ArgumentException) { rejected = true; }
                    Assert(rejected, "accepted " + invalid);
                }
            });
            Test("native event layout, Enter and supplementary Unicode batches", delegate
            {
                Assert(Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "invalid INPUT size");
                var enter = Keyboard.BuildInputs("\n");
                Assert(enter.Length == 2 && enter[0].u.ki.vk == 13 && enter[1].u.ki.flags == 2, "Enter is not a key pair");
                var emoji = Keyboard.BuildInputs("\ud83d\ude00");
                Assert(emoji.Length == 4 && emoji[0].u.ki.scan == 0xD83D && emoji[2].u.ki.scan == 0xDE00 &&
                    emoji[1].u.ki.flags == 6 && emoji[3].u.ki.flags == 6, "emoji batch is incomplete");
            });
            Test("slash settings apply immediately and quit releases the listener", delegate
            {
                var d = new FakeDesktop();
                var commands = new Queue<string>(new[] { "/speed 30", "/multiline on", "/settings", "/quit" });
                var s = new Session(d, d.Messages.Add, 10, false, delegate { return commands.Dequeue(); });
                s.Run("Ctrl+Alt+V");
                Assert(d.Messages.Exists(x => x.Contains("Speed: 30 ms | Multiline: on")) && d.Registered.Count == 0,
                    "settings were not updated or /quit leaked the hotkey");
            });
            Test("changed hotkey triggers input and stale old messages are ignored", delegate
            {
                var d = new FakeDesktop();
                int step = 0;
                d.Events.Enqueue(Session.TriggerId);
                var s = new Session(d, d.Messages.Add, 10, false, delegate
                {
                    step++;
                    if (step == 1) return "/hotkey Ctrl+Alt+P";
                    if (step == 2) { d.Events.Enqueue(3); return null; }
                    return "/quit";
                });
                s.Run("Ctrl+Alt+V");
                Assert(d.Reads == 1 && d.Text == "abc" && d.Registered.Count == 0, "hotkey switch failed");
            });
            Test("conflicting new shortcut preserves the working shortcut", delegate
            {
                var d = new FakeDesktop { FailedId = 3 };
                d.Events.Enqueue(Session.TriggerId);
                var commands = new Queue<string>(new[] { "/hotkey Ctrl+Alt+P", "/settings", "/quit" });
                var s = new Session(d, d.Messages.Add, 10, false, delegate { return commands.Dequeue(); });
                s.Run("Ctrl+Alt+V");
                Assert(d.Text == "abc" && d.Messages.Exists(x => x.StartsWith("Hotkey: Ctrl+Alt+V |")) &&
                    d.Registered.Count == 0, "conflict disabled or changed the previous shortcut");
            });
            Test("pause releases and resume reacquires the hotkey", delegate
            {
                var d = new FakeDesktop();
                int step = 0;
                var s = new Session(d, d.Messages.Add, 10, false, delegate
                {
                    step++;
                    if (step == 1) return "/pause";
                    if (step == 2) { Assert(d.Registered.Count == 0, "paused hotkey still owned"); return "/resume"; }
                    Assert(d.Registered.Contains(Session.TriggerId), "resume did not register");
                    return "/quit";
                });
                s.Run("Ctrl+Alt+V");
                Assert(d.Registered.Count == 0, "resume leaked registration");
            });
            Test("invalid commands preserve settings and never type terminal text", delegate
            {
                var d = new FakeDesktop();
                var commands = new Queue<string>(new[] { "/speed -1", "/speed 1001", "/multiline maybe",
                    "/hotkey V", "text in terminal", "/settings", "/quit" });
                var s = new Session(d, d.Messages.Add, 10, false, delegate { return commands.Dequeue(); });
                s.Run("Ctrl+Alt+V");
                Assert(d.Reads == 0 && d.Messages.Exists(x => x.Contains("Speed: 10 ms | Multiline: off")), "invalid command changed state");
            });
            Test("multiple shortcut changes keep exactly one registration", delegate
            {
                var d = new FakeDesktop();
                var commands = new Queue<string>(new[] { "/hotkey Ctrl+Alt+P", "/hotkey Ctrl+Alt+P",
                    "/hotkey Ctrl+Alt+Q", "/hotkey Ctrl+Alt+R", "/quit" });
                var s = new Session(d, d.Messages.Add, 10, false, delegate
                {
                    Assert(d.Registered.Count == 1, "shortcut swap lost or leaked a registration");
                    return commands.Dequeue();
                });
                s.Run("Ctrl+Alt+V");
                Assert(d.Registered.Count == 0, "last shortcut leaked");
            });
            Test("setting commands automatically persist only settings and replace existing files", delegate
            {
                string directory = Path.Combine(Path.GetTempPath(), "openpaste-settings-test-" + Guid.NewGuid().ToString("N"));
                string path = Path.Combine(directory, "settings.ini");
                var d = new FakeDesktop();
                var commands = new Queue<string>(new[] { "/speed 45", "/hotkey Ctrl+Alt+P", "/multiline on",
                    "/speed 60", "/quit" });
                var s = new Session(d, d.Messages.Add, 10, false, delegate { return commands.Dequeue(); }, path);
                s.Run("Ctrl+Alt+V");
                Settings saved = Settings.Load(path);
                Assert(saved.Hotkey == "Ctrl+Alt+P" && saved.Interval == 60 && saved.Multiline, "settings did not round trip");
                Assert(File.ReadAllLines(path).Length == 3 && !File.ReadAllText(path).Contains(d.ClipboardText), "unexpected data saved");
                File.WriteAllText(path, "interval_ms=-1");
                bool rejected = false;
                try { Settings.Load(path); } catch (FormatException) { rejected = true; }
                Assert(rejected, "invalid saved speed accepted");
            });
            Test("each successful change saves immediately and invalid changes do not overwrite it", delegate
            {
                string path = Path.Combine(Path.GetTempPath(), "openpaste-unsaved-" + Guid.NewGuid().ToString("N"), "settings.ini");
                var d = new FakeDesktop();
                int step = 0;
                new Session(d, d.Messages.Add, 10, false, delegate
                {
                    step++;
                    if (step == 1) return "/speed 99";
                    Assert(Settings.Load(path).Interval == 99, "change was not saved immediately");
                    return step == 2 ? "/speed -1" : "/quit";
                }, path).Run("Ctrl+Alt+V");
            });
            Test("save failures accurately report that the runtime setting still applies", delegate
            {
                string directory = Path.Combine(Path.GetTempPath(), "openpaste-save-error-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string blocker = Path.Combine(directory, "not-a-directory");
                File.WriteAllText(blocker, "block");
                var d = new FakeDesktop();
                var commands = new Queue<string>(new[] { "/speed 77", "/settings", "/quit" });
                new Session(d, d.Messages.Add, 10, false, delegate { return commands.Dequeue(); },
                    Path.Combine(blocker, "settings.ini")).Run("Ctrl+Alt+V");
                Assert(d.Messages.Exists(x => x.Contains("could not save:")) &&
                    d.Messages.Exists(x => x.Contains("Speed: 77 ms")), "save failure misreported runtime state");
            });
            Console.WriteLine("24 tests passed; no real clipboard access or input injection.");
        }
    }
}
