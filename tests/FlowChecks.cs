using System;
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
                check(widget.window.FindName("MiniStop") == null && widget.window.FindName("ExpandButton") == null, "direct stop and expand removed");
                check(((BitmapSource)widget.window.Icon).PixelWidth >= 128, "high resolution taskbar icon selected");
                widget.ToggleNotes();
                widget.F<TextBox>("NotesBox").Text = "待阅读：模型的假设";
                Thread.Sleep(500); widget.Tick();
                check(new DataStore(testData).Data.DraftNotes == "待阅读：模型的假设", "draft notes autosave before starting");
                widget.Toggle();
                check(widget.compact && widget.clock.State == TimerState.Running, "start automatically minimizes timer");
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
                Thread.Sleep(70); widget.Tick();
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "02-running.png"));
                widget.Toggle();
                check(!widget.compact && widget.clock.State == TimerState.Paused, "pause automatically expands timer");
                long time = widget.store.Data.TodayTime(DateTime.Now);
                Thread.Sleep(40); widget.Tick();
                check(time > 0 && widget.store.Data.TodayTime(DateTime.Now) == time, "paused time excluded");
                widget.Mark(0, true); widget.Mark(1, true); widget.Mark(0, true);
                check(widget.store.Data.TodayCount(0, DateTime.Now) == 1 && widget.store.Data.TodayCount(1, DateTime.Now) == 1, "checks count once");
                check(widget.F<CheckBox>("Section0").IsChecked == true && widget.F<CheckBox>("MiniSection0").IsChecked == true, "full and mini checks synchronize");
                check(widget.F<Canvas>("Confetti").Children.Count > 0, "check triggers local confetti");
                widget.NextPaper();
                check(widget.store.Data.CurrentPaper.Number == 2 && widget.F<CheckBox>("MiniSection0").IsChecked == false, "next paper clears controls");
                check(widget.store.Data.TodayCount(0, DateTime.Now) == 1, "next paper retains incomplete previous record");
                widget.Mark(2, true); widget.Mark(2, false);
                check(widget.store.Data.TodayCount(2, DateTime.Now) == 0, "undo mark updates statistics");
                widget.Toggle();
                check(widget.compact && widget.clock.State == TimerState.Running, "resume returns to mini");
                SessionRecord session = widget.activeSession;
                WordRecord first = widget.store.Data.AddWord(session, "research", DateTime.Now);
                widget.ApplyTranslation(session, first, new TranslationResult { Success = true, Text = "研究" });
                widget.Tick();
                check(widget.F<Border>("ToastPanel").Visibility == Visibility.Visible && widget.F<TextBlock>("ToastWord").Text == "research", "translation toast appears");
                check(widget.toastUntil - Native.Now <= 10000 && widget.toastUntil - Native.Now > 9700, "toast duration is ten seconds");
                widget.RenderOwnTree(System.IO.Path.Combine(folder, "03-word-toast.png"));
                WordRecord late = widget.store.Data.AddWord(session, "hypothesis", DateTime.Now);
                widget.Toggle();
                check(widget.clock.State == TimerState.Paused && widget.F<Button>("StopButton").IsEnabled, "ending becomes available after pause");
                widget.TryEndSession();
                check(widget.activeSession == null && !widget.compact && widget.showSummary, "stop expands and shows all words");
                check(widget.notesOpen && widget.F<TextBox>("NotesBox").IsReadOnly && widget.F<TextBox>("NotesBox").Text == note, "completed session automatically displays notes");
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "本轮笔记.txt")) == note, "export preserves complete note text");
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "本轮汇总.txt")).Contains(note), "combined summary includes notes");
                check(widget.F<DataGrid>("WordsGrid").Items.Count == 2, "summary contains successful and pending words");
                widget.ApplyTranslation(session, late, new TranslationResult { Success = true, Text = "假说" });
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "中英词表.csv")).Contains("假说"), "late translation updates automatic export");
                check(widget.F<Button>("RetryButton").Content.ToString() == "翻译已完成" && !widget.F<Button>("RetryButton").IsEnabled, "successful translations never show a failure label");
                widget.ApplyTranslation(session, late, new TranslationResult { Success = false, Text = "测试断网，请稍后重试" });
                check(widget.F<Button>("RetryButton").Content.ToString() == "重新翻译" && widget.F<Button>("RetryButton").IsEnabled, "failed word exposes clear retry action");
                int retryCalls = 0;
                widget.translateWord = delegate(string word) { Interlocked.Increment(ref retryCalls); return new TranslationResult { Success = true, Text = "假说（已重试）" }; };
                widget.RetryFailed(); widget.RetryFailed();
                check(widget.F<Button>("RetryButton").Content.ToString() == "翻译中…" && !widget.F<Button>("RetryButton").IsEnabled, "retry changes to pending state");
                PumpUntil(delegate { return late.Status == "ok"; }, 3000);
                check(retryCalls == 1 && late.Status == "ok" && widget.F<Button>("RetryButton").Content.ToString() == "翻译已完成", "double retry queues once and returns to completed state");
                check(File.ReadAllText(System.IO.Path.Combine(widget.exportFolder, "中英词表.csv")).Contains("已重试"), "retry after session end updates exported results");
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
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(root.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(file)) encoder.Save(stream);
        }
    }
}
