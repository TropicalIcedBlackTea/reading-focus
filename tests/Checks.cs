using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ReadingFocus
{
    internal static class Checks
    {
        private static int count;
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            count++;
        }
        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            string root = System.IO.Path.Combine(output, "check-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string report = System.IO.Path.Combine(output, "timer-test-results.txt");
            try
            {
                long now = 100000;
                FocusClock c = new FocusClock(60000, delegate { return now; });
                Check(c.State == TimerState.Ready && c.Remaining == 60000, "ready");
                c.Toggle(); now += 12345;
                Check(c.Remaining == 47655, "elapsed real time");
                c.Toggle(); now += 1000000;
                Check(c.State == TimerState.Paused && c.Remaining == 47655, "pause freezes time");
                c.Toggle(); now += 47654;
                Check(c.Remaining == 1 && !c.PollCompletion(), "one millisecond remains");
                Check(FocusClock.Display(c.Remaining) == "00:01", "round display up");
                now++;
                Check(c.PollCompletion() && !c.PollCompletion(), "notify once");
                Check(c.State == TimerState.Complete && c.Remaining == 0, "finished");
                c.Toggle(); Check(c.State == TimerState.Running && c.Remaining == 60000, "restart");
                now += 9999999;
                Check(c.PollCompletion() && c.Remaining == 0, "sleep and delayed UI");
                c.Reset(); Check(c.State == TimerState.Ready && c.Remaining == 60000, "reset");
                c.SetDuration(180 * 60000L); Check(FocusClock.Display(c.Remaining) == "180:00", "long timer");
                Check(FocusClock.Display(0) == "00:00" && FocusClock.Display(-1) == "00:00", "no negatives");
                ReadingData data = new ReadingData();
                DateTime end = new DateTime(2026, 10, 24, 0, 0, 30);
                data.AddTime(end, 90000);
                Check(data.TodayTime(end) == 30000 && data.TodayTime(end.AddDays(-1)) == 60000, "split midnight");
                Check(data.Mark(0, true, end) && !data.Mark(0, true, end), "idempotent check");
                Check(data.TodayCount(0, end) == 1 && data.TodayCount(1, end) == 0, "section count");
                data.NextPaper();
                Check(data.CurrentPaper.Number == 2 && data.CurrentPaper.MarkDates.All(string.IsNullOrEmpty), "unfinished next paper resets");
                Check(data.TodayCount(0, end) == 1, "previous unfinished paper retained");
                data.Mark(0, true, end); data.Mark(1, true, end);
                Check(data.TodayCount(0, end) == 2 && data.TodayCount(1, end) == 1, "multiple papers");
                data.Mark(1, false, end);
                Check(data.TodayCount(1, end) == 0, "undo check");
                Check(data.TodayCount(0, end.AddDays(1)) == 0, "next day section rollover");
                Check(ReadingData.NormalizeWord(" Research, ") == "research", "normalize case and punctuation");
                Check(ReadingData.NormalizeWord("state-of-the-art") == "state-of-the-art", "hyphen word");
                Check(ReadingData.NormalizeWord("don't") == "don't", "apostrophe word");
                Check(ReadingData.NormalizeWord("two words") == null && ReadingData.NormalizeWord("line\nbreak") == null, "reject multiword clipboard");
                Check(ReadingData.NormalizeWord("123456") == null && ReadingData.NormalizeWord("https://example.com") == null, "reject numbers and URLs");
                Check(ReadingData.NormalizeWord("研究") == null && ReadingData.NormalizeWord(new string('a', 101)) == null, "reject other clipboard data");
                SessionRecord session = new SessionRecord { StartedAt = end, Status = "已完成", Milliseconds = 90000, Notes = "假设 A < B & C\r\n下一步验证" };
                data.DraftNotes = "下一轮的问题";
                data.Sessions.Add(session);
                WordRecord word = data.AddWord(session, "research", end);
                Check(word != null && data.AddWord(session, "research", end) == null && session.Words.Count == 1 && word.Copies == 2, "deduplicate session words");
                word.Meaning = "研究, \"科研\""; word.Status = "ok";
                Check(data.Cached("research") == word.Meaning, "reuse successful translation");
                WordRecord failed = data.AddWord(session, "hypothesis", end); failed.Status = "failed"; failed.Meaning = "网络暂不可用";
                string folder = WordExport.Export(session, System.IO.Path.Combine(root, "exports"));
                Check(File.ReadAllLines(System.IO.Path.Combine(folder, "纯单词-扇贝用.txt")).SequenceEqual(new[] { "research", "hypothesis" }), "plain words retain untranslated items");
                Check(File.ReadAllText(System.IO.Path.Combine(folder, "中英词表.csv")).Contains("\"研究, \"\"科研\"\"\""), "CSV escaping");
                Check(File.ReadAllLines(System.IO.Path.Combine(folder, "中英词表.tsv")).Length == 1, "TSV contains successful translations only");
                Check(File.ReadAllText(System.IO.Path.Combine(folder, "本轮笔记.txt")) == session.Notes, "notes export preserves newlines and special characters");
                TranslationResult parsed = Translator.Parse("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"假说\"}}");
                Check(parsed.Success && parsed.Text == "假说", "valid translation response");
                Check(!Translator.Parse("{\"responseStatus\":429}").Success, "quota error is not translation");
                Check(!Translator.Parse("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"PLEASE WAIT\"}}").Success, "reject non-Chinese service error");
                Check(!Translator.Parse("invalid json").Success, "malformed response");
                Check(WindowPolicy.IsReadingApp("zotero") && WindowPolicy.IsReadingApp("chrome") && WindowPolicy.IsReadingApp("msedge") && WindowPolicy.IsReadingApp("explorer"), "reading windows allowed");
                Check(WindowPolicy.ShouldBlock("notepad") && WindowPolicy.ShouldBlock("focusguardprobe"), "other app windows blocked");
                Check(!WindowPolicy.ShouldBlock("consent") && !WindowPolicy.ShouldBlock("lockapp"), "system security surfaces untouched");
                DataStore disk = new DataStore(System.IO.Path.Combine(root, "data")); disk.Data = data;
                Check(disk.Save(), "persist reading history");
                DataStore loaded = new DataStore(System.IO.Path.Combine(root, "data"));
                Check(loaded.Data.TodayTime(end) == 30000 && loaded.Data.TodayCount(0, end) == 2, "restore daily statistics");
                Check(loaded.Data.Sessions[0].Words.Count == 2 && loaded.Data.CurrentPaper.Number == 2, "restore words and current paper");
                Check(loaded.Data.Sessions[0].Notes == session.Notes && loaded.Data.DraftNotes == data.DraftNotes, "restore notes and draft");
                using (FileStream blocked = new FileStream(System.IO.Path.Combine(root, "data", "reading-data.xml.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    Check(!loaded.Save() && loaded.Error != null, "temporary storage failure is visible");
                Check(loaded.Save() && loaded.Error == null, "recovered storage clears the old failure state");
                loaded.Data.Sessions[0].Words[1].Status = "pending"; loaded.Data.Sessions[0].Status = "进行中"; loaded.Save();
                DataStore interrupted = new DataStore(System.IO.Path.Combine(root, "data"));
                Check(interrupted.Data.Sessions[0].Status == "意外退出" && interrupted.Data.Sessions[0].Words[1].Status == "failed", "recover interrupted session and pending word");
                File.WriteAllText(report, "PASS: " + count + " checks\r\n", Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL after " + count + " checks: " + ex, Encoding.UTF8); return 1; }
        }
    }
}
