using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml;
using Path = System.Windows.Shapes.Path;

namespace ReadingFocus
{
    public enum TimerState { Ready, Running, Paused, Complete }

    // The clock is monotonic and includes Windows sleep time. UI refreshes never
    // subtract fixed intervals, so a busy UI cannot make the timer drift.
    public sealed class FocusClock
    {
        private readonly Func<long> now;
        private long startedAt;
        private long savedRemaining;
        public long Duration { get; private set; }
        public TimerState State { get; private set; }
        public FocusClock(long duration, Func<long> clock)
        {
            now = clock;
            SetDuration(duration);
        }
        public long Remaining
        {
            get
            {
                if (State != TimerState.Running) return savedRemaining;
                return Math.Max(0, savedRemaining - Math.Max(0, now() - startedAt));
            }
        }
        public void SetDuration(long duration)
        {
            if (duration <= 0) throw new ArgumentOutOfRangeException("duration");
            Duration = duration;
            Reset();
        }
        public void Reset()
        {
            savedRemaining = Duration;
            State = TimerState.Ready;
        }
        public void Toggle()
        {
            if (State == TimerState.Running)
            {
                savedRemaining = Remaining;
                State = savedRemaining == 0 ? TimerState.Complete : TimerState.Paused;
                return;
            }
            if (State == TimerState.Complete) savedRemaining = Duration;
            startedAt = now();
            State = TimerState.Running;
        }
        public bool PollCompletion()
        {
            if (State != TimerState.Running || Remaining > 0) return false;
            savedRemaining = 0;
            State = TimerState.Complete;
            return true;
        }
        public static string Display(long milliseconds)
        {
            long seconds = Math.Max(0, (long)Math.Ceiling(milliseconds / 1000.0));
            return (seconds / 60).ToString("00", CultureInfo.InvariantCulture)
                + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class Preferences
    {
        public int Minutes = 25;
        public bool Sound = true;
        public bool Compact;
        public double Left = double.NaN;
        public double Top = double.NaN;
        public static string Folder
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReadingFocusTimer"); }
        }
        private static string FilePath { get { return System.IO.Path.Combine(Folder, "settings.xml"); } }
        public static Preferences Load()
        {
            Preferences p = new Preferences();
            try
            {
                if (!File.Exists(FilePath)) return p;
                XmlDocument doc = new XmlDocument();
                doc.XmlResolver = null;
                using (XmlReader reader = XmlReader.Create(FilePath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
                XmlElement r = doc.DocumentElement;
                int minutes;
                bool b;
                double n;
                if (int.TryParse(r.GetAttribute("minutes"), out minutes) && minutes >= 1 && minutes <= 180) p.Minutes = minutes;
                if (bool.TryParse(r.GetAttribute("sound"), out b)) p.Sound = b;
                if (bool.TryParse(r.GetAttribute("compact"), out b)) p.Compact = b;
                if (double.TryParse(r.GetAttribute("left"), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n)) p.Left = n;
                if (double.TryParse(r.GetAttribute("top"), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n)) p.Top = n;
            }
            catch { /* A damaged or unavailable settings file must not block launch. */ }
            return p;
        }
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = FilePath + ".tmp";
                using (XmlWriter w = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                {
                    w.WriteStartElement("preferences");
                    w.WriteAttributeString("minutes", Minutes.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("sound", Sound.ToString());
                    w.WriteAttributeString("compact", Compact.ToString());
                    w.WriteAttributeString("left", Left.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("top", Top.ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                }
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            catch { /* Saving preferences is optional, including in read-only profiles. */ }
        }
    }

}
