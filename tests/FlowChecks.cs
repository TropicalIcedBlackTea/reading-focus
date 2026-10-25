using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                StackPanel stack = widget.F<StackPanel>("ToastStack");
                check(widget.toasts.Count == 1 && stack.Children.Count == 1 && CardText(stack, 0).StartsWith("hypothesis") && CardText(stack, 0).Contains("假设"), "dictionary card appears immediately");
                check(widget.toasts[0].Until - Native.Now <= 10000 && widget.toasts[0].Until - Native.Now > 9700, "card stays for ten seconds");
                check(widget.F<TextBlock>("MiniNotice").Text == "1 词", "mini view shows word count");
                widget.Collect("Estimated,");
                widget.Collect("formidable");
                check(widget.toasts.Count == 3 && CardText(stack, 0).StartsWith("formidable") && CardText(stack, 2).StartsWith("hypothesis"), "several cards stay at once, newest on top");
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "03-word-toast.png"));
                long firstUntil = widget.toasts[2].Until;
                Thread.Sleep(30);
                widget.Collect("hypothesis");
                check(widget.toasts.Count == 3 && CardText(stack, 0).StartsWith("hypothesis") && widget.toasts[0].Until > firstUntil, "repeat lookup refreshes and raises its card");
                widget.Collect("vasoconstrictor");
                widget.Collect("two words");
                check(session.Words.Count == 4 && session.Words[0].Copies == 2, "separate words rejected and repeats deduplicated");
                check(session.Words[1].Word == "estimate" && session.Words[3].Status == "missing" && session.Words[3].Meaning == "", "inflections fold to headwords and unknown words are kept");
                check(CardText(stack, 0).Contains("词典未收录"), "unknown word card says so");
                for (int i = 0; i < 8; i++) widget.Collect(new[] { "apathy", "visceral", "resurrect", "charlatan", "overblown", "longstanding", "exegesis", "gravitate" }[i]);
                check(widget.toasts.Count == MaxToasts && stack.Children.Count == MaxToasts, "card stack is capped");
                widget.toasts[1].Until = Native.Now - 1; widget.Tick();
                check(widget.toasts.Count == MaxToasts - 1 && stack.Children.Count == MaxToasts - 1, "each card expires on its own");
                stack.Children[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                check(widget.toasts.Count == MaxToasts - 2, "clicking a card dismisses it");
                widget.Toggle();
                check(widget.clock.State == TimerState.Paused && widget.F<Button>("StopButton").IsEnabled, "ending becomes available after pause");
                widget.TryEndSession();
                check(widget.activeSession == null && !widget.compact && widget.showSummary && widget.toasts.Count == 0 && stack.Children.Count == 0, "stop expands, shows all words and clears cards");
                check(widget.notesOpen && widget.F<TextBox>("NotesBox").IsReadOnly && widget.F<TextBox>("NotesBox").Text == note, "completed session automatically displays notes");
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "本轮笔记.txt")) == note, "export preserves complete note text");
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "本轮汇总.txt")).Contains(note), "combined summary includes notes");
                check(widget.F<DataGrid>("WordsGrid").Items.Count == 12 && widget.F<TextBlock>("SummaryCount").Text == "12" && !widget.vocabularyView, "summary lists every collected word");
                check(File.ReadAllLines(System.IO.Path.Combine(widget.exportFolder, "中英词表.tsv")).Length == 11, "TSV lists dictionary words only");
                check(File.ReadAllLines(System.IO.Path.Combine(widget.exportFolder, "纯单词-扇贝用.txt")).Length == 12, "plain list includes unknown words");
                check(widget.F<TextBlock>("ExportStatus").Visibility == Visibility.Collapsed, "no export notice after a successful export");
                widget.F<Button>("CopyWords").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(widget.F<TextBlock>("ExportStatus").Visibility == Visibility.Visible, "copy reports its result");
                string book = System.IO.Path.Combine(testData, "词表", "生词本.csv");
                check(File.Exists(book) && File.ReadAllLines(book).Length == 13, "permanent vocabulary file written at session end");
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "04-session-summary.png"));
                widget.F<Button>("VocabularyView").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DataGrid grid = widget.F<DataGrid>("WordsGrid");
                check(widget.vocabularyView && grid.Items.Count == 12 && ((VocabularyRecord)grid.Items[0]).Lookups >= 1 && widget.F<Grid>("SearchRow").Visibility == Visibility.Visible, "vocabulary view lists every word ever looked up");
                check(widget.store.Data.Vocabulary.Find(v => v.Word == "hypothesis").Lookups == 2, "vocabulary counts repeat lookups");
                widget.F<TextBox>("SearchBox").Text = "假设";
                check(grid.Items.Count == 1 && ((VocabularyRecord)grid.Items[0]).Word == "hypothesis", "vocabulary search matches meanings");
                widget.F<TextBox>("SearchBox").Text = "vaso";
                grid.SelectedItem = grid.Items[0];
                widget.ForgetSelected();
                check(widget.store.Data.Vocabulary.Count == 11 && grid.Items.Count == 0 && session.Words.Count == 12, "removing from the vocabulary keeps the session list");
                check(File.ReadAllLines(book).Length == 12, "vocabulary file follows removals");
                widget.F<TextBox>("SearchBox").Text = "";
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "05-vocabulary.png"));
                widget.F<Button>("SessionView").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!widget.vocabularyView && grid.Items.Count == 12, "session view returns");
                FocusWindow reopened = new FocusWindow(true, testData);
                check(reopened.notesOpen && reopened.showSummary && reopened.F<TextBox>("NotesBox").Text == note && reopened.F<TextBox>("NotesBox").IsReadOnly, "relaunch displays the saved session notes");
                check(reopened.store.Data.Vocabulary.Count == 11 && reopened.store.Data.Vocabulary.Find(v => v.Word == "hypothesis").Lookups == 2, "vocabulary survives a restart without double counting");
                reopened.window.Close();
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
        private static string CardText(StackPanel stack, int index)
        {
            StackPanel text = ((Grid)((Border)stack.Children[index]).Child).Children.OfType<StackPanel>().First();
            return string.Join(" ", text.Children.OfType<TextBlock>().Select(t => t.Text));
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
