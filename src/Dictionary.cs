using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ReadingFocus
{
    public sealed class DictionaryHit
    {
        public string Word;
        public string Meaning;
        public bool Derived;
        public bool Found { get { return Meaning != null; } }
        public string Status { get { return Meaning == null ? "missing" : Derived ? "derived" : "ok"; } }
        public static DictionaryHit Unknown(string word) { return new DictionaryHit { Word = word }; }
    }

    // Offline English-Chinese dictionary embedded in the executable: the
    // academic glossary first, then general vocabulary for everything else.
    // Word lookups ignore case and fold regular inflections and British
    // spellings back to the headword. Acronyms (OLS, ATE) match only in
    // capitals, so they never shadow an ordinary word.
    public sealed class AcademicDictionary
    {
        private static AcademicDictionary shared;
        private static readonly string[] Resources = { "academic-words.tsv", "general-words.tsv" };
        private readonly Dictionary<string, string[]> entries = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> acronyms = new Dictionary<string, string[]>(StringComparer.Ordinal);

        public AcademicDictionary(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0 || line.StartsWith("#")) continue;
                string word = line.Substring(0, tab).Trim(), meaning = line.Substring(tab + 1).Trim();
                if (word.Length == 0 || meaning.Length == 0) continue;
                Dictionary<string, string[]> target = word.Any(char.IsUpper) ? acronyms : entries;
                if (!target.ContainsKey(word)) target[word] = new[] { word, meaning };
            }
        }
        public int Count { get { return entries.Count + acronyms.Count; } }
        public static AcademicDictionary Shared
        {
            get
            {
                if (shared != null) return shared;
                List<string> lines = new List<string>();
                foreach (string name in Resources)
                    using (Stream stream = typeof(AcademicDictionary).Assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null) continue;
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            lines.AddRange(reader.ReadToEnd().Split('\n'));
                    }
                return shared = new AcademicDictionary(lines);
            }
        }

        // Dictionary entry, or a labeled word-formation guess for long-tail words.
        public DictionaryHit Describe(string word)
        {
            return Lookup(word) ?? Derive(word);
        }

        public DictionaryHit Lookup(string word)
        {
            if (string.IsNullOrEmpty(word)) return null;
            string[] entry;
            if (acronyms.TryGetValue(word, out entry) || (word.Length > 2 && word.EndsWith("s") && acronyms.TryGetValue(word.Substring(0, word.Length - 1), out entry)))
                return new DictionaryHit { Word = entry[0], Meaning = entry[1] };
            foreach (string[] form in Forms(word.ToLowerInvariant()))
            {
                entry = Find(form[0], form[1]);
                if (entry == null && form[0].IndexOf('-') > 0) entry = Find(form[0].Replace("-", ""), form[1]);
                if (entry != null) return new DictionaryHit { Word = entry[0], Meaning = entry[1] };
            }
            return null;
        }
        private string[] Find(string word, string pos)
        {
            if (word.Length < 2) return null;
            foreach (string spelling in Spellings(word))
            {
                string[] entry;
                if (entries.TryGetValue(spelling, out entry) && (pos == null || HasPos(entry[1], pos))) return entry;
            }
            return null;
        }
        private static bool HasPos(string meaning, string pos)
        {
            foreach (string tag in pos.Split('|'))
                if (meaning.StartsWith(tag + ".") || meaning.Contains(" " + tag + ".")) return true;
            return false;
        }

        // Candidate base forms, most literal first. Each carries the part of
        // speech the headword needs for that suffix rule to apply.
        private static IEnumerable<string[]> Forms(string w)
        {
            yield return new[] { w, null };
            if (w.EndsWith("'s")) yield return Form(w, 2, "", null);
            if (w.EndsWith("s'")) yield return Form(w, 1, "", null);
            int n = w.Length;
            if (n < 4) yield break;
            if (w.EndsWith("ses")) yield return Form(w, 2, "is", "n");
            if (w.EndsWith("ices")) { yield return Form(w, 4, "ex", "n"); yield return Form(w, 4, "ix", "n"); }
            if (w.EndsWith("ae")) yield return Form(w, 1, "", "n");
            else if (w.EndsWith("a")) { yield return Form(w, 1, "um", "n"); yield return Form(w, 1, "on", "n"); }
            if (w.EndsWith("i")) yield return Form(w, 1, "us", "n");
            if (w.EndsWith("ies")) yield return Form(w, 3, "y", "n|v");
            if (w.EndsWith("es")) yield return Form(w, 2, "", "n|v");
            if (w.EndsWith("s") && !w.EndsWith("ss")) yield return Form(w, 1, "", "n|v");
            if (w.EndsWith("ied")) yield return Form(w, 3, "y", "v");
            if (w.EndsWith("ed")) foreach (string[] f in Stems(w.Substring(0, n - 2), "v")) yield return f;
            if (w.EndsWith("ing") && n > 5) foreach (string[] f in Stems(w.Substring(0, n - 3), "v")) yield return f;
            if (w.EndsWith("ier")) yield return Form(w, 3, "y", "adj");
            if (w.EndsWith("iest")) yield return Form(w, 4, "y", "adj");
            if (w.EndsWith("er")) foreach (string[] f in Stems(w.Substring(0, n - 2), "adj")) yield return f;
            if (w.EndsWith("est")) foreach (string[] f in Stems(w.Substring(0, n - 3), "adj")) yield return f;
            if (w.EndsWith("ically")) yield return Form(w, 4, "", "adj");
            if (w.EndsWith("ily")) yield return Form(w, 3, "y", "adj");
            if (w.EndsWith("ly")) { yield return Form(w, 2, "", "adj"); yield return Form(w, 1, "e", "adj"); }
        }
        private static string[] Form(string w, int cut, string add, string pos) { return new[] { w.Substring(0, w.Length - cut) + add, pos }; }
        private static IEnumerable<string[]> Stems(string b, string pos)
        {
            yield return new[] { b, pos };
            yield return new[] { b + "e", pos };
            int n = b.Length;
            if (n > 2 && b[n - 1] == b[n - 2] && "aeiou".IndexOf(b[n - 1]) < 0) yield return new[] { b.Substring(0, n - 1), pos };
        }
        private static IEnumerable<string> Spellings(string w)
        {
            yield return w;
            string us = w.Replace("isation", "ization").Replace("our", "or").Replace("ae", "e").Replace("oe", "e");
            us = Regex.Replace(us, "ise$", "ize");
            us = Regex.Replace(us, "yse$", "yze");
            us = Regex.Replace(us, "tre$", "ter");
            us = Regex.Replace(us, "ogue$", "og");
            us = Regex.Replace(us, "mme$", "m");
            us = Regex.Replace(us, "([fc])ence$", "$1ense");
            if (us != w) yield return us;
        }

        // Productive prefixes whose meaning carries over to any known base word;
        // longest first so "under" wins over "un".
        private static readonly string[][] Prefixes =
        {
            new[] { "counter", "反/逆" }, new[] { "pseudo", "伪" }, new[] { "hyper", "超/过度" }, new[] { "inter", "…之间/相互" },
            new[] { "intra", "…内" }, new[] { "macro", "宏观" }, new[] { "micro", "微" }, new[] { "multi", "多" }, new[] { "quasi", "准/类" },
            new[] { "super", "超" }, new[] { "ultra", "极/超" }, new[] { "under", "不足/低于" }, new[] { "cross", "跨" }, new[] { "over", "过度" },
            new[] { "anti", "反" }, new[] { "post", "…后" }, new[] { "self", "自我" }, new[] { "semi", "半" }, new[] { "dis", "不/反" },
            new[] { "mis", "错误地" }, new[] { "non", "非" }, new[] { "pre", "预先/…前" }, new[] { "sub", "次/子" }, new[] { "un", "不/未" },
            new[] { "re", "再/重新" }
        };
        private static readonly HashSet<string> Glue = new HashSet<string> { "a", "an", "and", "as", "at", "by", "for", "in", "of", "on", "or", "per", "the", "to", "vs" };

        // Words missing from the dictionary but built from known parts get a
        // guess marked 〔构词〕: hyphenated compounds, prefixes, -ness and -less.
        private DictionaryHit Derive(string word)
        {
            string w = word.ToLowerInvariant();
            if (w.IndexOf('-') > 0)
            {
                List<string> parts = new List<string>();
                foreach (string part in w.Split('-'))
                {
                    DictionaryHit hit = Glue.Contains(part) ? null : Lookup(part);
                    if (hit == null && !Glue.Contains(part)) { parts = null; break; }
                    parts.Add(hit == null ? part : part + "（" + FirstSense(hit.Meaning) + "）");
                }
                if (parts != null && parts.Count > 1) return Guess(w, string.Join(" + ", parts).Replace("） + ", "）+ "));
            }
            foreach (string[] prefix in Prefixes)
            {
                if (!w.StartsWith(prefix[0])) continue;
                string rest = w.Substring(prefix[0].Length).TrimStart('-');
                DictionaryHit hit = rest.Length >= 4 ? Lookup(rest) : null;
                if (hit != null) return Guess(w, prefix[0] + "-（" + prefix[1] + "）+ " + rest + "（" + FirstSense(hit.Meaning) + "）");
            }
            if (w.Length > 7 && (w.EndsWith("ness") || w.EndsWith("less")))
            {
                string stem = w.Substring(0, w.Length - 4);
                if (w.EndsWith("iness")) stem = stem.Substring(0, stem.Length - 1) + "y";
                DictionaryHit hit = Lookup(stem);
                if (hit != null) return Guess(w, stem + "（" + FirstSense(hit.Meaning) + "）+ " + (w.EndsWith("ness") ? "-ness（…性/状态）" : "-less（无…的）"));
            }
            return null;
        }
        private static DictionaryHit Guess(string word, string parts) { return new DictionaryHit { Word = word, Meaning = "〔构词〕" + parts, Derived = true }; }
        private static string FirstSense(string meaning)
        {
            string m = meaning;
            int dot = m.IndexOf(". ");
            if (dot >= 0 && dot < 6) m = m.Substring(dot + 2);
            int end = m.IndexOfAny(new[] { '；', ' ' });
            return end > 0 ? m.Substring(0, end) : m;
        }
    }

    // Turns clipboard text into a single word. PDF selections that run across
    // a line break arrive as "hypo-\r\nthesis", "hypoth esis", or with
    // ligatures and soft hyphens; the pieces are rejoined before lookup.
    public static class WordReader
    {
        private static readonly Regex Shape = new Regex("^[A-Za-z]+(?:['-][A-Za-z]+)*$");
        private static readonly HashSet<string> CompoundPrefixes = new HashSet<string>
        {
            "self", "non", "co", "cross", "well", "quasi", "semi", "anti", "socio", "long", "short", "high", "low", "full", "part", "half"
        };

        public static DictionaryHit Read(string text, AcademicDictionary dictionary)
        {
            if (text == null || text.Length > 120) return null;
            string s = text.Normalize(NormalizationForm.FormKC);
            s = Regex.Replace(s, @"[\u00AD\u00AC]\s*", "");
            s = Regex.Replace(s, "[\u200B-\u200D\uFEFF]", "");
            s = Regex.Replace(s, "[\u2010\u2011\u2012\u2212\uFE63]", "-");
            s = Regex.Replace(s, "[\u2018\u2019\u02BC\u2032`]", "'");
            s = Regex.Replace(s, @"^[\s\p{P}\p{S}]+|[\s\p{P}\p{S}]+$", "");
            if (s.Length == 0) return null;
            string[] parts = Regex.Split(s, @"\s+");
            if (parts.Length > 4) return null;
            return parts.Length == 1 ? Single(parts[0], dictionary) : Joined(parts, dictionary);
        }

        private static DictionaryHit Single(string word, AcademicDictionary dictionary)
        {
            if (Shape.IsMatch(word)) return Resolve(word, dictionary);
            // Footnote markers stick to the word when copied from a PDF.
            string bare = Regex.Replace(word, "[0-9]+$", "");
            return bare != word && Shape.IsMatch(bare) ? dictionary.Lookup(bare) : null;
        }

        private static DictionaryHit Joined(string[] parts, AcademicDictionary dictionary)
        {
            int gaps = parts.Length - 1, hyphenated = 0;
            string[] pieces = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                pieces[i] = parts[i];
                if (i < gaps && parts[i].Length > 1 && parts[i].EndsWith("-"))
                {
                    hyphenated |= 1 << i;
                    pieces[i] = parts[i].Substring(0, parts[i].Length - 1);
                }
            }
            // A line-break hyphen may be typesetting or part of a compound, so try
            // every keep/drop combination; plain breaks always join.
            for (int keep = 0; keep < 1 << gaps; keep++)
            {
                if ((keep & ~hyphenated) != 0) continue;
                string candidate = Build(pieces, keep);
                DictionaryHit hit = candidate.Length <= 48 && Shape.IsMatch(candidate) ? dictionary.Lookup(candidate) : null;
                if (hit != null) return hit;
            }
            // Separate words unless every break was hyphenated.
            if (hyphenated != (1 << gaps) - 1) return null;
            int compounds = 0;
            for (int i = 0; i < gaps; i++)
                if (IsCompound(pieces[i].Split('-').Last(), pieces[i + 1].Split('-').First(), dictionary)) compounds |= 1 << i;
            string word = Build(pieces, compounds);
            return Shape.IsMatch(word) ? Resolve(word, dictionary) : null;
        }

        private static bool IsCompound(string left, string right, AcademicDictionary dictionary)
        {
            if (right.Length == 0) return false;
            return CompoundPrefixes.Contains(left.ToLowerInvariant()) || (left.Length >= 4 && dictionary.Lookup(left) != null);
        }

        private static string Build(string[] pieces, int hyphens)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < pieces.Length; i++)
            {
                b.Append(pieces[i]);
                if ((hyphens & (1 << i)) != 0) b.Append('-');
            }
            return b.ToString();
        }

        private static DictionaryHit Resolve(string word, AcademicDictionary dictionary)
        {
            if (word.Length > 48) return null;
            if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) && dictionary.Lookup(word) == null) word = word.Substring(0, word.Length - 2);
            return dictionary.Describe(word) ?? DictionaryHit.Unknown(word.ToLowerInvariant());
        }
    }
}
