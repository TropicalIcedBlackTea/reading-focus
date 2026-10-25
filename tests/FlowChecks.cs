using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ReadingFocus
{
    internal sealed partial class FocusWindow
    {
        // Component tests render only our own WPF visual tree, without displaying
        // a desktop window, reading the clipboard, or acting on other applications.
        private static int FlowTest(string folder)
        {
            Directory.CreateDirectory(folder);
            string report = System.IO.Path.Combine(folder, "flow-test-results.txt");
            FocusWindow widget = null;
            int count = 0;
            Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new Exception(name); count++; };
            try
            {
                string testData = System.IO.Path.Combine(folder, "data-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                widget = new FocusWindow(true, testData);
                widget.prefs.Sound = false;
                widget.SetMinutes(1);
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "01-ready.png"));
                check(!widget.compact && widget.F<Border>("StatsPanel").Visibility == Visibility.Visible, "idle statistics visible");
                check(widget.window.FindName("MiniStop") == null && widget.window.FindName("ExpandButton") == null && widget.window.FindName("RetryButton") == null, "direct stop, expand and retry removed");
                check(((BitmapSource)widget.window.Icon).PixelWidth >= 128 && widget.F<Image>("AppMark").Source != null, "high resolution icons selected");
                check(widget.F<TextBlock>("FooterLabel").Text == "" && widget.F<TextBlock>("NotesSaveState").Text == "", "no save hints while storage works");
                widget.ToggleNotes();
                widget.F<TextBox>("NotesBox").Text = "待阅读：模型的假设";
                check(widget.F<TextBlock>("NotesSaveState").Text == "", "typing shows no save indicator");
                Thread.Sleep(500); widget.Tick();
                check(new DataStore(testData).Data.DraftNotes == "待阅读：模型的假设", "draft notes autosave before starting");
                widget.Toggle();
                check(widget.compact && widget.clock.State == TimerState.Running, "start automatically minimizes timer");
                check(widget.F<Grid>("PaperRow").Visibility == Visibility.Visible && widget.F<StackPanel>("FullPanel").Visibility == Visibility.Collapsed, "paper row stays in mini mode");
                check(widget.activeSession.Notes == "待阅读：模型的假设" && widget.store.Data.DraftNotes == "", "draft moves into new session");
                check(!widget.F<Button>("StopButton").IsEnabled && widget.window.FindName("ResetButton") == null && !widget.F<Button>("CloseButton").IsEnabled, "running session cannot end by buttons");
                SessionRecord protectedSession = widget.activeSession;
                widget.F<Button>("StopButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(widget.activeSession == protectedSession && widget.clock.State == TimerState.Running, "direct stop handler refuses active timer");
                widget.window.Close();
                check(!widget.closing && widget.activeSession == protectedSession, "window close refuses active timer");
                widget.ToggleNotes();
                string note = "待阅读：模型的假设\r\n机制：需求 < 供给 & 异质性\r\n模型与发现需要对照。";
                widget.F<TextBox>("NotesBox").Text = note;
                check(widget.activeSession.Notes == note && widget.compact && widget.clock.State == TimerState.Running, "write multiline notes without pausing or expanding");
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "02b-running-notes.png"));
                Thread.Sleep(500); widget.Tick();
                check(new DataStore(testData).Data.Sessions.Last().Notes == note, "active notes autosave to disk");
                widget.ToggleNotes(); widget.ToggleNotes();
                check(widget.F<TextBox>("NotesBox").Text == note, "folding notes preserves text");
                widget.ToggleNotes();
                Thread.Sleep(70); widget.Tick();
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "02-running.png"));
                widget.Toggle();
                check(!widget.compact && widget.clock.State == TimerState.Paused, "pause automatically expands timer");
                long time = widget.store.Data.TodayTime(DateTime.Now);
                Thread.Sleep(40); widget.Tick();
                check(time > 0 && widget.store.Data.TodayTime(DateTime.Now) == time, "paused time excluded");
                widget.Mark(0, true); widget.Mark(1, true); widget.Mark(0, true);
                check(widget.store.Data.TodayCount(0, DateTime.Now) == 1 && widget.store.Data.TodayCount(1, DateTime.Now) == 1, "checks count once");
                check(widget.F<CheckBox>("Section0").IsChecked == true && widget.F<CheckBox>("Section1").IsChecked == true, "checkboxes reflect marks");
                check(widget.F<Canvas>("Confetti").Children.Count > 0, "check triggers local confetti");
                widget.NextPaper();
                check(widget.store.Data.CurrentPaper.Number == 2 && widget.F<CheckBox>("Section0").IsChecked == false, "next paper clears controls");
                check(widget.store.Data.TodayCount(0, DateTime.Now) == 1, "next paper retains incomplete previous record");
                widget.Mark(2, true); widget.Mark(2, false);
                check(widget.store.Data.TodayCount(2, DateTime.Now) == 0, "undo mark updates statistics");
                widget.Toggle();
                check(widget.compact && widget.clock.State == TimerState.Running, "resume returns to mini");
                SessionRecord session = widget.activeSession;
                widget.Collect("hypo-\r\nthesis");
                check(session.Words.Count == 1 && session.Words[0].Word == "hypothesis" && session.Words[0].Status == "ok", "line-broken word resolved by the built-in dictionary");
                widget.Tick();
                check(widget.F<Border>("ToastPanel").Visibility == Visibility.Visible && widget.F<TextBlock>("ToastWord").Text == "hypothesis" && widget.F<TextBlock>("ToastMeaning").Text.Contains("假设"), "dictionary toast appears immediately");
                check(widget.toastUntil - Native.Now <= 10000 && widget.toastUntil - Native.Now > 9700, "toast duration is ten seconds");
                check(widget.F<TextBlock>("MiniNotice").Text == "1 词", "mini view shows word count");
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "03-word-toast.png"));
                widget.Collect("Estimated,");
                widget.Collect("vasoconstrictor");
                widget.Collect("two words");
                widget.Collect("hypothesis");
                check(session.Words.Count == 3 && session.Words[0].Copies == 2, "separate words rejected and repeats deduplicated");
                check(session.Words[1].Word == "estimate" && session.Words[2].Status == "missing" && session.Words[2].Meaning == "", "inflections fold to headwords and unknown words are kept");
                widget.Toggle();
                check(widget.clock.State == TimerState.Paused && widget.F<Button>("StopButton").IsEnabled, "ending becomes available after pause");
                widget.TryEndSession();
                check(widget.activeSession == null && !widget.compact && widget.showSummary, "stop expands and shows all words");
                check(widget.notesOpen && widget.F<TextBox>("NotesBox").IsReadOnly && widget.F<TextBox>("NotesBox").Text == note, "completed session automatically displays notes");
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "本轮笔记.txt")) == note, "export preserves complete note text");
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "本轮汇总.txt")).Contains(note), "combined summary includes notes");
                check(widget.F<DataGrid>("WordsGrid").Items.Count == 3 && widget.F<TextBlock>("SummaryCount").Text == "3", "summary lists every collected word");
                check(File.ReadAllLines(System.IO.Path.Combine(widget.exportFolder, "中英词表.tsv")).Length == 2, "TSV lists dictionary words only");
                check(File.ReadAllLines(System.IO.Path.Combine(widget.exportFolder, "纯单词-扇贝用.txt")).Length == 3, "plain list includes unknown words");
                check(widget.F<TextBlock>("ExportStatus").Visibility == Visibility.Collapsed, "no export notice after a successful export");
                widget.F<Button>("CopyWords").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(widget.F<TextBlock>("ExportStatus").Visibility == Visibility.Visible, "copy reports its result");
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "04-session-summary.png"));
                FocusWindow reopened = new FocusWindow(true, testData);
                check(reopened.notesOpen && reopened.showSummary && reopened.F<TextBox>("NotesBox").Text == note && reopened.F<TextBox>("NotesBox").IsReadOnly, "relaunch displays the saved session notes");
                reopened.window.Close();
                widget.toastUntil = Native.Now - 1; widget.Tick();
                check(widget.F<Border>("ToastPanel").Visibility == Visibility.Collapsed, "expired toast is hidden");
                check(session.Milliseconds > 0 && widget.store.Data.TodayTime(DateTime.Now) == session.Milliseconds, "statistics equal actual session time");
                widget.clock.SetDuration(120);
                widget.Toggle();
                SessionRecord completion = widget.activeSession;
                check(completion.Notes == "" && widget.F<TextBox>("NotesBox").Text == "", "next session starts with fresh notes");
                Thread.Sleep(160); widget.Tick();
                check(widget.clock.State == TimerState.Complete && !widget.compact && widget.activeSession == null && completion.Milliseconds == 120, "natural completion expands and caps statistics");
                widget.Toggle();
                check(widget.compact && widget.activeSession.Id != completion.Id && widget.activeSession.Words.Count == 0, "next session starts with an independent word list");
                widget.EndSession("测试结束", false);
                File.WriteAllText(report, "PASS: " + count + " component flow checks\r\n", Encoding.UTF8);
                widget.window.Close();
                return 0;
            }
            catch (Exception ex)
            {
                if (widget != null) try { widget.window.Close(); } catch { }
                File.WriteAllText(report, "FAIL after " + count + ": " + ex, Encoding.UTF8); return 1;
            }
        }

        // Starts a disposable probe window and checks that starting and resuming
        // the timer minimize it. QA mode never acts on any other process.
        private static int SweepTest(string folder, string probePath)
        {
            Directory.CreateDirectory(folder);
            string report = System.IO.Path.Combine(folder, "sweep-test-results.txt");
            FocusWindow widget = null;
            Process probe = null;
            int count = 0;
            Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new Exception(name); count++; };
            try
            {
                probe = Process.Start(new ProcessStartInfo(probePath) { UseShellExecute = false });
                IntPtr handle = IntPtr.Zero;
                PumpUntil(delegate { probe.Refresh(); handle = probe.MainWindowHandle; return handle != IntPtr.Zero && Native.IsWindowVisible(handle); }, 10000);
                check(handle != IntPtr.Zero && !Native.IsIconic(handle), "probe window is open");
                check(Native.AppWindows().Contains(handle), "probe counts as an app window");
                widget = new FocusWindow(true, System.IO.Path.Combine(folder, "data-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
                widget.prefs.Sound = false;
                widget.SetMinutes(1);
                widget.Toggle();
                PumpUntil(delegate { return Native.IsIconic(handle); }, 3000);
                check(widget.clock.State == TimerState.Running && Native.IsIconic(handle), "start minimizes other app windows");
                widget.Toggle();
                Native.ShowWindowAsync(handle, 9);
                PumpUntil(delegate { return !Native.IsIconic(handle); }, 3000);
                check(widget.clock.State == TimerState.Paused && !Native.IsIconic(handle), "paused timer leaves windows alone");
                widget.Toggle();
                PumpUntil(delegate { return Native.IsIconic(handle); }, 3000);
                check(Native.IsIconic(handle), "resume minimizes again");
                widget.Toggle();
                widget.TryEndSession();
                File.WriteAllText(report, "PASS: " + count + " sweep checks\r\n", Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL after " + count + ": " + ex, Encoding.UTF8); return 1; }
            finally
            {
                if (widget != null) try { widget.window.Close(); } catch { }
                if (probe != null) try { if (!probe.HasExited) probe.Kill(); probe.Dispose(); } catch { }
            }
        }
        private static void PumpUntil(Func<bool> complete, int timeout)
        {
            DispatcherFrame frame = new DispatcherFrame();
            long deadline = Native.Now + timeout;
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += delegate { if (complete() || Native.Now >= deadline) { timer.Stop(); frame.Continue = false; } };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        private void RenderOwnTree(string file)
        {
            FrameworkElement root = (FrameworkElement)window.Content;
            root.Measure(new Size(window.Width, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, window.Width, root.DesiredSize.Height));
            root.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width * 2), (int)Math.Ceiling(root.DesiredSize.Height * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(root);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(file)) encoder.Save(stream);
        }
    }
}
