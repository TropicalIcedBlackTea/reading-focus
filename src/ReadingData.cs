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
    public sealed class ReadingData
    {
        public string DraftNotes = "";
        public List<DayRecord> Days = new List<DayRecord>();
        public List<PaperRecord> Papers = new List<PaperRecord>();
        public List<SessionRecord> Sessions = new List<SessionRecord>();
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
        public WordRecord AddWord(SessionRecord session, string word, string meaning, DateTime now)
        {
            WordRecord existing = session.Words.Find(w => w.Word == word);
            if (existing != null) { existing.Copies++; return null; }
            WordRecord item = new WordRecord { Word = word, Status = meaning != null ? "ok" : "missing", Meaning = meaning ?? "", Paper = CurrentPaper.Number, CollectedAt = now, Copies = 1 };
            session.Words.Add(item);
            return item;
        }
        // Older records and words the dictionary did not know are looked up
        // again, so a larger dictionary fills them in.
        public void Resolve(AcademicDictionary dictionary)
        {
            foreach (SessionRecord s in Sessions)
                foreach (WordRecord w in s.Words)
                {
                    if (w.Status == "ok") continue;
                    DictionaryHit hit = dictionary.Lookup(w.Word);
                    w.Status = hit != null ? "ok" : "missing";
                    w.Meaning = hit != null ? hit.Meaning : "";
                }
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
                bool found = w.Status == "ok";
                csv.AppendLine(Csv(w.Word) + "," + Csv(found ? w.Meaning : "") + "," + Csv(found ? "已收录" : "未收录") + "," + w.Paper + "," + Csv(w.CollectedAt.ToString("s")));
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
        private static void AtomicText(string file, string text, bool bom)
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(bom));
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        }
    }
}
