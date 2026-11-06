using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// ことば（多言語）。JS版と同じく**日本語の文をそのまま鍵にして、HTML版の lang/&lt;言語&gt;.json から訳を引く**。
    ///   Lang.Tr("ローカル")            … 訳。無ければ英語、それも無ければ日本語のまま
    ///   Lang.Tr("あと {0} 人", left)   … {0} {1} … に値を入れる（日本語のときも）
    /// 選んだ言語は PlayerPrefs（nekorobo.lang）。初めては OS の言語（日本語でなければ英語）。
    /// </summary>
    public static class Lang
    {
        public static readonly string[][] LANGS = { new[] { "ja", "日本語" }, new[] { "en", "English" } };
        const string KEY = "nekorobo.lang";
        static readonly Dictionary<string, Dictionary<string, string>> dict = new Dictionary<string, Dictionary<string, string>>();
        public static readonly HashSet<string> Miss = new HashSet<string>();
        static readonly Regex JP = new Regex("[぀-ヿ一-鿿]");
        static readonly Regex ARG = new Regex(@"\{(\d+)\}");
        static string cur;
        public static event System.Action Changed;

        public static string Cur
        {
            get
            {
                if (cur == null)
                {
                    cur = PlayerPrefs.GetString(KEY, "");
                    if (cur == "") cur = Application.systemLanguage == SystemLanguage.Japanese ? "ja" : "en";
                    bool ok = false; foreach (var l in LANGS) if (l[0] == cur) ok = true;
                    if (!ok) cur = "en";
                }
                return cur;
            }
        }
        public static bool IsJa { get { return Cur == "ja"; } }

        public static void Set(string k)
        {
            cur = k;
            PlayerPrefs.SetString(KEY, k); PlayerPrefs.Save();
            if (Changed != null) Changed();
        }

        static Dictionary<string, string> Load(string k)
        {
            Dictionary<string, string> d;
            if (dict.TryGetValue(k, out d)) return d;
            d = new Dictionary<string, string>();
            var j = DataRoot.ReadJson("lang/" + k + ".json");
            if (j != null) foreach (var p in j.Properties()) d[p.Name] = (string)p.Value;
            dict[k] = d;
            return d;
        }

        public static string Tr(string s, params object[] a)
        {
            string t = s;
            if (!IsJa && !string.IsNullOrEmpty(s))
            {
                string v;
                if (Load(Cur).TryGetValue(s, out v) && v != null) t = v;
                else if (Load("en").TryGetValue(s, out v) && v != null) t = v;
                if (t == s && JP.IsMatch(s)) Miss.Add(s);
            }
            if (a == null || a.Length == 0) return t;
            return ARG.Replace(t, m => { int i = int.Parse(m.Groups[1].Value); return i < a.Length && a[i] != null ? a[i].ToString() : ""; });
        }
        /// <summary>並べるときの区切り（日本語は「・」、ほかは「, 」）。</summary>
        public static string Sep { get { return IsJa ? "・" : ", "; } }
    }
}
