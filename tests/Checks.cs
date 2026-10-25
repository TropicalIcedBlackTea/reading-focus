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
        private static void Reads(AcademicDictionary dictionary, string clipboard, string word, string label)
        {
            DictionaryHit hit = WordReader.Read(clipboard, dictionary);
            Check(hit != null && hit.Word == word, label + " (got " + (hit == null ? "null" : hit.Word) + ")");
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

                AcademicDictionary dict = new AcademicDictionary(new[]
                {
                    "# comment", "hypothesis\tn. 假设；假说", "hypotheses\tn. 假设（hypothesis 的复数）", "significant\tadj. 显著的；重要的",
                    "estimate\tv. 估计；估算 n. 估计值", "heterogeneity\tn. 异质性", "analyze\tv. 分析", "behavior\tn. 行为", "behavioral\tadj. 行为的",
                    "model\tn. 模型 v. 建模", "study\tn. 研究 v. 研究", "author\tn. 作者", "cross-sectional\tadj. 横截面的", "OLS\tabbr. 普通最小二乘法",
                    "large\tadj. 大的", "criterion\tn. 标准", "index\tn. 指数", "basic\tadj. 基本的", "considerable\tadj. 相当大的", "organization\tn. 组织",
                    "data\tn. 数据", "trade-off\tn. 权衡", "nonlinear\tadj. 非线性的", "apply\tv. 应用", "control\tv. 控制 n. 控制", "endogeneity\tn. 内生性", "ATE\tabbr. 平均处理效应"
                });
                Check(dict.Count == 26, "dictionary skips comments");
                Reads(dict, "Hypothesis", "hypothesis", "case-insensitive lookup");
                Reads(dict, "hypotheses", "hypotheses", "irregular plural entry");
                Reads(dict, "estimated", "estimate", "past tense folds to headword");
                Reads(dict, "estimating", "estimate", "present participle folds to headword");
                Reads(dict, "studies", "study", "plural -ies");
                Reads(dict, "models", "model", "plural -s");
                Reads(dict, "modelling", "model", "British doubled consonant");
                Reads(dict, "controlled", "control", "doubled consonant past tense");
                Reads(dict, "applied", "apply", "-ied past tense");
                Reads(dict, "analysed", "analyze", "British -yse spelling");
                Reads(dict, "behaviour", "behavior", "British -our spelling");
                Reads(dict, "behavioural", "behavioral", "British -our adjective");
                Reads(dict, "organisations", "organization", "British -isation plural");
                Reads(dict, "significantly", "significant", "adverb folds to adjective");
                Reads(dict, "basically", "basic", "-ically adverb");
                Reads(dict, "considerably", "considerable", "-ably adverb");
                Reads(dict, "larger", "large", "comparative");
                Reads(dict, "criteria", "criterion", "Greek plural");
                Reads(dict, "indices", "index", "Latin plural");
                Reads(dict, "trade-offs", "trade-off", "hyphenated plural");
                Reads(dict, "non-linear", "nonlinear", "hyphen variant");
                Reads(dict, "model's", "model", "possessive");
                Reads(dict, "OLS", "OLS", "acronym");
                Reads(dict, "ATEs", "ATE", "acronym plural");
                Check(dict.Lookup("ate") == null && dict.Lookup("Ols") == null, "acronyms never match ordinary words");
                Check(dict.Lookup("large").Meaning == "adj. 大的" && dict.Lookup("controlled").Meaning.StartsWith("v."), "lookup returns meaning");
                Check(dict.Lookup("datas") == null || dict.Lookup("datas").Word == "data", "plural lookup stays on headword");
                Check(dict.Lookup("analyst") == null, "unknown word has no entry");
                Reads(dict, "hypo-\r\nthesis", "hypothesis", "hyphenated line break");
                Reads(dict, "hypo-\nthesis", "hypothesis", "hyphenated line break with LF");
                Reads(dict, "hypo- thesis", "hypothesis", "hyphenated break joined by a space");
                Reads(dict, "hypoth\r\nesis", "hypothesis", "line break without hyphen");
                Reads(dict, "signi\uFB01cant", "significant", "fi ligature");
                Reads(dict, "signi fi cant", "significant", "ligature split by spaces");
                Reads(dict, "hetero\u00ADgeneity", "heterogeneity", "soft hyphen");
                Reads(dict, "hetero\u00AD\r\ngeneity", "heterogeneity", "soft hyphen at line end");
                Reads(dict, "endo\u2010\r\ngeneity", "endogeneity", "Unicode hyphen at line end");
                Reads(dict, "cross-\r\nsectional", "cross-sectional", "compound hyphen at line end");
                Reads(dict, "hypo-thesis", "hypothesis", "line-break hyphen kept without newline");
                Reads(dict, "hypothesis\u00B9", "hypothesis", "superscript footnote marker");
                Reads(dict, "hypothesis2", "hypothesis", "digit footnote marker");
                Reads(dict, "(hypothesis),", "hypothesis", "surrounding punctuation");
                Reads(dict, "\u201Chypothesis\u201D", "hypothesis", "curly quotes");
                Reads(dict, "\uFF48\uFF59\uFF50\uFF4F\uFF54\uFF48\uFF45\uFF53\uFF49\uFF53", "hypothesis", "full-width letters");
                Reads(dict, "  hypothesis\r\n", "hypothesis", "trailing newline");
                Reads(dict, "co-\r\nauthor", "co-author", "known compound prefix keeps hyphen");
                Reads(dict, "vaso-\r\nconstriction", "vasoconstriction", "unknown word drops line-break hyphen");
                Reads(dict, " Research, ", "research", "unknown single word is still collected");
                Reads(dict, "state-of-the-art", "state-of-the-art", "hyphenated word");
                Reads(dict, "don\u2019t", "don't", "curly apostrophe");
                Check(WordReader.Read(" Research, ", dict).Meaning == null && !WordReader.Read("research", dict).Found, "unknown word has no meaning");
                Check(WordReader.Read("two words", dict) == null && WordReader.Read("line\nbreak", dict) == null, "reject separate words");
                Check(WordReader.Read("the hypo-\nthesis", dict) == null, "reject a phrase around a broken word");
                Check(WordReader.Read("a b c d e", dict) == null, "reject long selections");
                Check(WordReader.Read("123456", dict) == null && WordReader.Read("https://example.com", dict) == null, "reject numbers and URLs");
                Check(WordReader.Read("研究", dict) == null && WordReader.Read(new string('a', 101), dict) == null && WordReader.Read(null, dict) == null, "reject other clipboard data");
                Check(WordReader.Read("Hypothesis 1", dict) == null, "reject numbered labels");

                AcademicDictionary embedded = AcademicDictionary.Shared;
                Check(embedded.Count >= 3000, "embedded dictionary loads (" + embedded.Count + " entries)");
                foreach (string w in new[] { "hypothesis", "heterogeneity", "endogeneity", "regression", "coefficient", "elasticity", "robust", "significantly", "empirical", "methodology", "variance", "spillover" })
                    Check(embedded.Lookup(w) != null, "embedded dictionary contains " + w);
                Check(embedded.Lookup("estimated") != null && embedded.Lookup("regressions") != null && embedded.Lookup("analysed") != null, "embedded dictionary folds inflections");

                SessionRecord session = new SessionRecord { StartedAt = end, Status = "已完成", Milliseconds = 90000, Notes = "假设 A < B & C\r\n下一步验证" };
                data.DraftNotes = "下一轮的问题";
                data.Sessions.Add(session);
                WordRecord word = data.AddWord(session, "research", "研究, \"科研\"", end);
                Check(word != null && word.Status == "ok" && data.AddWord(session, "research", "研究", end) == null && session.Words.Count == 1 && word.Copies == 2, "deduplicate session words");
                WordRecord missing = data.AddWord(session, "vasoconstriction", null, end);
                Check(missing.Status == "missing" && missing.Meaning == "", "unknown word recorded without meaning");
                string folder = WordExport.Export(session, System.IO.Path.Combine(root, "exports"));
                Check(File.ReadAllLines(System.IO.Path.Combine(folder, "纯单词-扇贝用.txt")).SequenceEqual(new[] { "research", "vasoconstriction" }), "plain words include unknown words");
                string csv = File.ReadAllText(System.IO.Path.Combine(folder, "中英词表.csv"));
                Check(csv.Contains("\"研究, \"\"科研\"\"\"") && csv.Contains("\"已收录\"") && csv.Contains("\"未收录\""), "CSV escaping and dictionary column");
                Check(File.ReadAllLines(System.IO.Path.Combine(folder, "中英词表.tsv")).Length == 1, "TSV contains words with meanings only");
                Check(File.ReadAllText(System.IO.Path.Combine(folder, "本轮笔记.txt")) == session.Notes, "notes export preserves newlines and special characters");
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
                WordRecord legacy = loaded.Data.Sessions[0].Words[1];
                legacy.Word = "hypotheses"; legacy.Status = "failed"; legacy.Meaning = "网络暂不可用，点击重试";
                loaded.Data.Sessions[0].Status = "进行中"; loaded.Save();
                DataStore interrupted = new DataStore(System.IO.Path.Combine(root, "data"));
                Check(interrupted.Data.Sessions[0].Status == "意外退出", "recover interrupted session");
                interrupted.Data.Resolve(dict);
                WordRecord resolved = interrupted.Data.Sessions[0].Words[1];
                Check(resolved.Status == "ok" && resolved.Meaning.StartsWith("n. 假设") && interrupted.Data.Sessions[0].Words[0].Meaning == word.Meaning, "legacy online words fall back to the dictionary");
                File.WriteAllText(report, "PASS: " + count + " checks\r\n", Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL after " + count + " checks: " + ex, Encoding.UTF8); return 1; }
        }
    }
}
