using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
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
        private string meaning = "翻译中…";
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
        public WordRecord AddWord(SessionRecord session, string word, DateTime now)
        {
            WordRecord existing = session.Words.Find(w => w.Word == word);
            if (existing != null) { existing.Copies++; return null; }
            WordRecord item = new WordRecord { Word = word, Status = "pending", Paper = CurrentPaper.Number, CollectedAt = now, Copies = 1 };
            session.Words.Add(item);
            return item;
        }
        public string Cached(string word)
        {
            foreach (SessionRecord s in Sessions.AsEnumerable().Reverse())
            {
                WordRecord w = s.Words.Find(x => x.Word == word && x.Status == "ok");
                if (w != null) return w.Meaning;
            }
            return null;
        }
        public static string NormalizeWord(string text)
        {
            if (text == null || text.Length > 100) return null;
            string word = text.Trim().Trim('"', '“', '”', '‘', '’', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}');
            word = word.Replace('’', '\'').Replace('‐', '-').Replace('‑', '-');
            if (word.Length < 1 || word.Length > 48 || !Regex.IsMatch(word, "^[A-Za-z]+(?:['-][A-Za-z]+)*$")) return null;
            return word.ToLowerInvariant();
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
            {
                if (s.Status == "进行中") { s.Status = "意外退出"; s.EndedAt = DateTime.Now; }
                foreach (WordRecord w in s.Words)
                    if (w.Status == "pending") { w.Status = "failed"; w.Meaning = "上次翻译中断，可重试"; }
            }
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
            StringBuilder csv = new StringBuilder("单词,中文释义,状态,论文序号,收集时间\r\n");
            StringBuilder tsv = new StringBuilder();
            StringBuilder words = new StringBuilder();
            StringBuilder summary = new StringBuilder("文献专注 · 本轮词表\r\n" + session.StartedAt.ToString("yyyy-MM-dd HH:mm") + "\r\n专注 " + (session.Milliseconds / 60000.0).ToString("0.0") + " 分钟\r\n\r\n");
            foreach (WordRecord w in session.Words)
            {
                string status = w.Status == "ok" ? "已翻译" : w.Status == "pending" ? "翻译中" : "待重试";
                csv.AppendLine(Csv(w.Word) + "," + Csv(w.Meaning) + "," + Csv(status) + "," + w.Paper + "," + Csv(w.CollectedAt.ToString("s")));
                if (w.Status == "ok") tsv.AppendLine(Flat(w.Word) + "\t" + Flat(w.Meaning));
                words.AppendLine(w.Word);
                summary.AppendLine(w.Word + " — " + w.Meaning + "  [" + status + "]");
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
    internal sealed class TranslationResult
    {
        public bool Success;
        public string Text;
    }
    internal static class Translator
    {
        public static TranslationResult Translate(string word)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(word) + "&langpair=en%7Czh-CN");
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.UserAgent = "ReadingFocusTimer/2.0";
                request.AllowAutoRedirect = false;
                string json;
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
                return Parse(json);
            }
            catch { return new TranslationResult { Text = "网络暂不可用，点击重试" }; }
        }
        internal static TranslationResult Parse(string json)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("responseStatus") || Convert.ToInt32(root["responseStatus"]) != 200)
                    return new TranslationResult { Text = "翻译服务限额或繁忙，可稍后重试" };
                var data = root["responseData"] as Dictionary<string, object>;
                string text = WebUtility.HtmlDecode(Convert.ToString(data["translatedText"])).Trim();
                if (text.Length < 1 || text.Length > 500 || !Regex.IsMatch(text, "[\u3400-\u9fff]"))
                    return new TranslationResult { Text = "未取得中文释义，可重试" };
                return new TranslationResult { Success = true, Text = text };
            }
            catch { return new TranslationResult { Text = "翻译响应异常，可重试" }; }
        }
    }
    internal sealed class TranslationQueue
    {
        private readonly Queue<Action> queue = new Queue<Action>();
        private bool working;
        public void Add(Action work)
        {
            lock (queue)
            {
                queue.Enqueue(work);
                if (working) return;
                working = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    Action next;
                    lock (queue)
                    {
                        if (queue.Count == 0) { working = false; return; }
                        next = queue.Dequeue();
                    }
                    try { next(); } catch { }
                }
            });
        }
    }
}
