using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ReadingFocus
{
    public sealed class DayRecord
    {
        public string Date;
        public long Milliseconds;
    }
    public sealed class PaperRecord
    {
        public int Number;
        public string[] MarkDates = new string[3];
    }
    public sealed class WordRecord : INotifyPropertyChanged
    {
        public string Word { get; set; }
        private string meaning = "";
        public string Meaning { get { return meaning; } set { meaning = value; Changed("Meaning"); } }
        public string Status { get; set; }
        public int Paper { get; set; }
        public DateTime CollectedAt { get; set; }
        public int Copies { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
    }
    public sealed class SessionRecord
    {
        public string Id = Guid.NewGuid().ToString("N");
        public DateTime StartedAt;
        public DateTime EndedAt;
        public long Milliseconds;
        public string Status = "进行中";
        public string Notes = "";
        public List<WordRecord> Words = new List<WordRecord>();
    }
    // One entry per word ever looked up, kept across sessions and restarts.
    public sealed class VocabularyRecord
    {
        public string Word { get; set; }
        public string Meaning { get; set; }
        public string Status { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Lookups { get; set; }
    }
    public sealed class ReadingData
    {
        public string DraftNotes = "";
        public List<DayRecord> Days = new List<DayRecord>();
        public List<PaperRecord> Papers = new List<PaperRecord>();
        public List<SessionRecord> Sessions = new List<SessionRecord>();
        public List<VocabularyRecord> Vocabulary = new List<VocabularyRecord>();
        public bool VocabularyImported;
        [XmlIgnore] public PaperRecord CurrentPaper
        {
            get { if (Papers.Count == 0) Papers.Add(new PaperRecord { Number = 1 }); return Papers[Papers.Count - 1]; }
        }
        public void AddTime(DateTime end, long milliseconds)
        {
            if (milliseconds <= 0) return;
            DateTime start = end.AddMilliseconds(-milliseconds);
            while (start < end)
            {
                DateTime next = start.Date.AddDays(1);
                DateTime until = next < end ? next : end;
                string date = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                DayRecord day = Days.Find(x => x.Date == date);
                if (day == null) { day = new DayRecord { Date = date }; Days.Add(day); }
                day.Milliseconds += (long)Math.Round((until - start).TotalMilliseconds);
                start = until;
            }
        }
        public long TodayTime(DateTime now)
        {
            string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DayRecord day = Days.Find(x => x.Date == date);
            return day == null ? 0 : day.Milliseconds;
        }
        public int TodayCount(int section, DateTime now)
        {
            string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Papers.Count(p => p.MarkDates != null && p.MarkDates.Length == 3 && p.MarkDates[section] == date);
        }
        public bool Mark(int section, bool value, DateTime now)
        {
            if (section < 0 || section > 2) return false;
            PaperRecord paper = CurrentPaper;
            if (paper.MarkDates == null || paper.MarkDates.Length != 3) paper.MarkDates = new string[3];
            bool was = !string.IsNullOrEmpty(paper.MarkDates[section]);
            if (was == value) return false;
            paper.MarkDates[section] = value ? now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
            return true;
        }
        public void NextPaper() { Papers.Add(new PaperRecord { Number = CurrentPaper.Number + 1 }); }
        public WordRecord AddWord(SessionRecord session, DictionaryHit hit, DateTime now)
        {
            Remember(hit, now, 1);
            WordRecord existing = session.Words.Find(w => w.Word == hit.Word);
            if (existing != null) { existing.Copies++; return null; }
            WordRecord item = new WordRecord { Word = hit.Word, Status = hit.Status, Meaning = hit.Meaning ?? "", Paper = CurrentPaper.Number, CollectedAt = now, Copies = 1 };
            session.Words.Add(item);
            return item;
        }
        private VocabularyRecord Remember(DictionaryHit hit, DateTime when, int lookups)
        {
            VocabularyRecord entry = Vocabulary.Find(v => v.Word == hit.Word);
            if (entry == null)
            {
                entry = new VocabularyRecord { Word = hit.Word, FirstSeen = when, LastSeen = when };
                Vocabulary.Add(entry);
            }
            if (when < entry.FirstSeen) entry.FirstSeen = when;
            if (when > entry.LastSeen) entry.LastSeen = when;
            entry.Lookups += lookups;
            if (entry.Status != "ok" || hit.Status == "ok") { entry.Status = hit.Status; entry.Meaning = hit.Meaning ?? ""; }
            return entry;
        }
        public bool Forget(string word) { return Vocabulary.RemoveAll(v => v.Word == word) > 0; }
        // Older records and words the dictionary did not know are looked up
        // again, so a larger dictionary fills them in. Words collected before
        // the permanent vocabulary existed are folded into it once.
        public void Resolve(AcademicDictionary dictionary)
        {
            foreach (SessionRecord s in Sessions)
                foreach (WordRecord w in s.Words)
                {
                    if (w.Status == "ok") continue;
                    DictionaryHit hit = dictionary.Describe(w.Word) ?? DictionaryHit.Unknown(w.Word);
                    w.Status = hit.Status;
                    w.Meaning = hit.Meaning ?? "";
                }
            foreach (VocabularyRecord v in Vocabulary)
            {
                if (v.Status == "ok") continue;
                DictionaryHit hit = dictionary.Describe(v.Word) ?? DictionaryHit.Unknown(v.Word);
                v.Status = hit.Status;
                v.Meaning = hit.Meaning ?? "";
            }
            if (VocabularyImported) return;
            foreach (IGrouping<string, WordRecord> group in Sessions.SelectMany(s => s.Words).GroupBy(w => w.Word))
            {
                WordRecord latest = group.OrderBy(w => w.CollectedAt).Last();
                DictionaryHit hit = new DictionaryHit { Word = group.Key, Meaning = latest.Status == "missing" ? null : latest.Meaning, Derived = latest.Status == "derived" };
                VocabularyRecord entry = Remember(hit, group.Min(w => w.CollectedAt), 0);
                if (latest.CollectedAt > entry.LastSeen) entry.LastSeen = latest.CollectedAt;
                entry.Lookups = Math.Max(entry.Lookups, group.Sum(w => Math.Max(1, w.Copies)));
            }
            VocabularyImported = true;
        }
    }
    internal sealed class DataStore
    {
        public ReadingData Data;
        public string Error;
        private readonly string path;
        public DataStore(string folder)
        {
            path = System.IO.Path.Combine(folder, "reading-data.xml");
            Data = new ReadingData();
            if (!File.Exists(path)) return;
            if (!TryLoad(path) && !TryLoad(path + ".bak"))
            {
                Data = new ReadingData();
                // Never overwrite an unreadable history without preserving its bytes.
                try { File.Copy(path, path + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); }
                catch { Error = "历史数据无法读取，且无法备份；本次不会覆盖原文件。"; return; }
                Error = "历史文件损坏，已备份；从新的记录继续。";
            }
            foreach (SessionRecord s in Data.Sessions)
                if (s.Status == "进行中") { s.Status = "意外退出"; s.EndedAt = DateTime.Now; }
            foreach (PaperRecord paper in Data.Papers)
                if (paper.MarkDates == null || paper.MarkDates.Length != 3) paper.MarkDates = new string[3];
        }
        private bool TryLoad(string file)
        {
            try
            {
                using (XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    Data = (ReadingData)new XmlSerializer(typeof(ReadingData)).Deserialize(reader);
                if (Data.Days == null || Data.Papers == null || Data.Sessions == null) return false;
                return true;
            }
            catch { return false; }
        }
        public bool Save()
        {
            if (Error != null && Error.Contains("不会覆盖")) return false;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                using (XmlWriter writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize }))
                    new XmlSerializer(typeof(ReadingData)).Serialize(writer, Data);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                if (Error == "记录暂时无法保存，请检查磁盘空间与文件夹权限。") Error = null;
                return true;
            }
            catch { Error = "记录暂时无法保存，请检查磁盘空间与文件夹权限。"; return false; }
        }
    }
    internal static class WordExport
    {
        private static string Flat(string value)
        {
            string s = (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
            return s;
        }
        private static string Csv(string value) { return "\"" + Flat(value).Replace("\"", "\"\"") + "\""; }
        public static string Export(SessionRecord session, string root)
        {
            string folder = System.IO.Path.Combine(root, session.StartedAt.ToString("yyyy-MM-dd"), session.StartedAt.ToString("HHmmss") + "-" + session.Id.Substring(0, 6));
            Directory.CreateDirectory(folder);
            StringBuilder csv = new StringBuilder("单词,中文释义,词典,论文序号,收集时间\r\n");
            StringBuilder tsv = new StringBuilder();
            StringBuilder words = new StringBuilder();
            StringBuilder summary = new StringBuilder("文献专注 · 本轮词表\r\n" + session.StartedAt.ToString("yyyy-MM-dd HH:mm") + "\r\n专注 " + (session.Milliseconds / 60000.0).ToString("0.0") + " 分钟\r\n\r\n");
            foreach (WordRecord w in session.Words)
            {
                bool found = w.Status != "missing";
                csv.AppendLine(Csv(w.Word) + "," + Csv(found ? w.Meaning : "") + "," + Csv(Source(w.Status)) + "," + w.Paper + "," + Csv(w.CollectedAt.ToString("s")));
                if (found) tsv.AppendLine(Flat(w.Word) + "\t" + Flat(w.Meaning));
                words.AppendLine(w.Word);
                summary.AppendLine(found ? w.Word + " — " + w.Meaning : w.Word);
            }
            AtomicText(System.IO.Path.Combine(folder, "中英词表.csv"), csv.ToString(), true);
            AtomicText(System.IO.Path.Combine(folder, "中英词表.tsv"), tsv.ToString(), false);
            AtomicText(System.IO.Path.Combine(folder, "纯单词-扇贝用.txt"), words.ToString(), false);
            if (!string.IsNullOrWhiteSpace(session.Notes))
                summary.Append("\r\n本轮笔记\r\n────────\r\n" + session.Notes + "\r\n");
            AtomicText(System.IO.Path.Combine(folder, "本轮笔记.txt"), session.Notes ?? "", true);
            AtomicText(System.IO.Path.Combine(folder, "本轮汇总.txt"), summary.ToString(), true);
            return folder;
        }
        // The permanent vocabulary as files next to the session folders,
        // most recent lookups first.
        public static void ExportVocabulary(IEnumerable<VocabularyRecord> vocabulary, string root)
        {
            Directory.CreateDirectory(root);
            StringBuilder csv = new StringBuilder("单词,中文释义,词典,查询次数,首次查询,最近查询\r\n");
            StringBuilder words = new StringBuilder();
            foreach (VocabularyRecord v in vocabulary.OrderByDescending(v => v.LastSeen))
            {
                csv.AppendLine(Csv(v.Word) + "," + Csv(v.Status == "missing" ? "" : v.Meaning) + "," + Csv(Source(v.Status)) + "," + v.Lookups + "," + Csv(v.FirstSeen.ToString("s")) + "," + Csv(v.LastSeen.ToString("s")));
                words.AppendLine(v.Word);
            }
            AtomicText(System.IO.Path.Combine(root, "生词本.csv"), csv.ToString(), true);
            AtomicText(System.IO.Path.Combine(root, "生词本-扇贝用.txt"), words.ToString(), false);
        }
        private static string Source(string status) { return status == "ok" ? "已收录" : status == "derived" ? "构词推测" : "未收录"; }
        private static void AtomicText(string file, string text, bool bom)
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(bom));
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        }
    }
}
