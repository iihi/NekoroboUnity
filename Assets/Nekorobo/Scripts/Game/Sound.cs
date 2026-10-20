using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Nekorobo
{
    /// <summary>
    /// 効果音（SE）と BGM。JS版 se.js を写したもの。
    ///
    /// ---- 効果音
    /// **音のファイルは使わず、HTML版の se.json のレシピから合成する**（作り方は se.js と同じ式）。
    /// 起動時に裏の糸で全部の音を作っておき（響きのたたみ込みが重いため）、鳴らすときは作った音を流すだけ。
    /// レシピに "file" を書いた音は、そのファイル（assets/se/xxx.wav など）に差し替わる。
    /// JS版は鳴らすたびに高さを変えて合成し直すが、こちらは出来た音の再生の速さで高さを変える（短い音なので違いは小さい）。
    /// JS版の後ろの圧縮（Web Audio の DynamicsCompressor）は、Chrome で測った効き方に合わせて音ごとに掛けておく（Compress）。
    /// 響きは Web Audio と同じく正規化する。この2つで、聞こえる大きさが Chrome と1dB 以内でそろう（2026-10-20 に測った）。
    ///
    /// ---- BGM
    /// ファイル（mp3）を読んで、くり返し鳴らす。**ファイルが無ければ鳴らさない。**切り替えるときは、前の曲を下げながら次の曲を上げる。
    ///
    /// 大きさ・消す・ON/OFF はこの端末で覚える（PlayerPrefs。名前は JS版の localStorage と同じ）。
    /// </summary>
    public class Sound : MonoBehaviour
    {
        const int SR = 44100;
        const int MAX_VOICES = 24;                      // 同時に鳴らす数の上限。超えたぶんは鳴らさない
        const float BGM_BASE = 0.6f;                    // BGM の土台の大きさ（「BGM が大きい」と言われて 1→0.6）
        const float FADE = 0.8f;                        // BGM の切り替えにかける秒数
        const float BAKED = 0.7f;                       // 作るときに掛けておく全体の大きさ（JS版の既定。圧縮の前に掛かるため）

        static Sound I;
        static JObject R;                                // レシピ（se.json）
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, float> last = new Dictionary<string, float>();
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static readonly Dictionary<string, AudioClip> files = new Dictionary<string, AudioClip>();
        static List<KeyValuePair<string, float[]>> made;     // 裏の糸で作った音（主の糸で AudioClip にする）
        static System.Threading.Tasks.Task makeJob;

        static float vol = 0.7f, bgmVol = 0.5f;
        static bool mute, bgmMute, off;

        public static bool Off { get { return off; } }
        public static float Volume { get { return vol; } }
        public static bool Muted { get { return mute; } }
        public static float BgmVolume { get { return bgmVol; } }
        public static bool BgmMuted { get { return bgmMute; } }

        // ================================================================ 起動
        /// <summary>はじめに1回（Game.Start）。se.json を読んで、裏で音を作り始める。</summary>
        public static void Init()
        {
            if (R != null) return;                      // もう読んである（調整パネルが先に呼ぶこともある）
            if (I == null)
            {
                var go = new GameObject("Sound");
                DontDestroyOnLoad(go);
                I = go.AddComponent<Sound>();
                for (int i = 0; i < MAX_VOICES; i++)
                {
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false; s.spatialBlend = 0; pool.Add(s);
                }
                for (int i = 0; i < 2; i++)
                {
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false; s.spatialBlend = 0; s.loop = true; s.volume = 0;
                    bgmSrc[i] = s;
                }
            }
            vol = PlayerPrefs.GetFloat("nekorobo.seVol", 0.7f);
            mute = PlayerPrefs.GetInt("nekorobo.seMute", 0) != 0;
            bgmVol = PlayerPrefs.GetFloat("nekorobo.bgmVol", 0.5f);
            bgmMute = PlayerPrefs.GetInt("nekorobo.bgmMute", 0) != 0;
            off = PlayerPrefs.GetInt("nekorobo.soundOff", 0) != 0;
            AudioListener.pause = off;
            R = DataRoot.ReadJson("se.json");
            if (R == null) { Debug.LogWarning("[Sound] se.json が読めません"); return; }
            // ファイルで差し替える音は、先に読んでおく（読めなければ合成のまま）
            foreach (var p in R.Properties())
                if (p.Value is JObject o && o["file"] != null) I.StartCoroutine(LoadFile((string)o["file"], null));
            var recipes = new List<KeyValuePair<string, JObject>>();
            foreach (var p in R.Properties()) if (p.Name != "_" && p.Value is JObject o2) recipes.Add(new KeyValuePair<string, JObject>(p.Name, o2));
            makeJob = System.Threading.Tasks.Task.Run(() =>
            {
                var outL = new List<KeyValuePair<string, float[]>>();
                foreach (var kv in recipes)
                    try { outL.Add(new KeyValuePair<string, float[]>(kv.Key, Make(kv.Value))); }
                    catch (System.Exception e) { Debug.LogWarning("[Sound] 作れない音: " + kv.Key + " " + e.Message); }
                made = outL;
            });
        }

        void Update()
        {
            // 裏で作った音を AudioClip にする
            if (made != null)
            {
                foreach (var kv in made)
                {
                    var c = AudioClip.Create("se_" + kv.Key, kv.Value.Length / 2, 2, SR, false);
                    c.SetData(kv.Value, 0);
                    clips[kv.Key] = c;
                }
                Debug.Log("[Sound] 効果音 " + made.Count + " 個を作りました");
                made = null;
            }
            BgmUpdate(Time.unscaledDeltaTime);
        }

        public static IEnumerable<string> Names { get { if (R != null) foreach (var p in R.Properties()) if (p.Name != "_") yield return p.Name; } }
        public static string Label(string k) { var o = R != null ? R[k] as JObject : null; return o != null && o["n"] != null ? (string)o["n"] : k; }

        public static void SetVolume(float v) { vol = Mathf.Clamp01(v); PlayerPrefs.SetFloat("nekorobo.seVol", vol); }
        public static void SetMute(bool m) { mute = m; PlayerPrefs.SetInt("nekorobo.seMute", m ? 1 : 0); }
        public static void SetBgmVolume(float v) { bgmVol = Mathf.Clamp01(v); PlayerPrefs.SetFloat("nekorobo.bgmVol", bgmVol); }
        public static void SetBgmMute(bool m) { bgmMute = m; PlayerPrefs.SetInt("nekorobo.bgmMute", m ? 1 : 0); }
        /// <summary>音をまるごと止める（画面の ON/OFF ボタン）。BGM も効果音も止まり、ON に戻すと続きから鳴る。</summary>
        public static void SetOff(bool b)
        {
            off = b; PlayerPrefs.SetInt("nekorobo.soundOff", b ? 1 : 0); PlayerPrefs.Save();
            AudioListener.pause = off;
        }

        // ================================================================ 鳴らす
        /// <summary>鳴らす。pitch … 高さの倍率、v … 大きさの倍率、pan … 左右（-1〜1）。</summary>
        public static void Play(string name, float pitch = 1, float v = 1, float pan = 0)
        {
            if (I == null || R == null || mute || off || vol <= 0) return;
            var r = R[name] as JObject;
            if (r == null) return;
            AudioClip clip = null;
            var file = (string)r["file"];
            if (file != null) files.TryGetValue(file, out clip);
            bool fromFile = clip != null;
            if (clip == null && !clips.TryGetValue(name, out clip)) return;       // まだ作っている途中
            float now = Time.realtimeSinceStartup;
            float gap = J.F(r, "gap", 0.04f);
            float lt;
            if (last.TryGetValue(name, out lt) && now - lt < gap) return;
            AudioSource src = null;
            foreach (var s in pool) if (!s.isPlaying) { src = s; break; }
            if (src == null) return;
            last[name] = now;
            float rnd = J.F(r, "rnd", 0);
            src.clip = clip;
            src.pitch = pitch * (rnd > 0 ? 1 + (Random.value * 2 - 1) * rnd : 1);
            // 作った音には全体の大きさ（v）を入れてある。ファイルの音はここで掛ける
            src.volume = Mathf.Clamp01(fromFile ? vol * v * J.F(r, "v", 1) : vol / BAKED * v);
            src.panStereo = Mathf.Clamp(pan, -1, 1);
            src.Play();
        }

        static IEnumerator LoadFile(string path, System.Action<AudioClip> done)
        {
            AudioClip c;
            if (files.TryGetValue(path, out c)) { if (done != null) done(c); yield break; }
            files[path] = null;
            var full = DataRoot.File_(path.StartsWith("assets/") || path.StartsWith("./") ? path.Replace("./", "") : path);
            if (!System.IO.File.Exists(full)) { if (done != null) done(null); yield break; }
            var type = full.EndsWith(".mp3") ? AudioType.MPEG : full.EndsWith(".ogg") ? AudioType.OGGVORBIS : AudioType.WAV;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(full).AbsoluteUri, type))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) { c = DownloadHandlerAudioClip.GetContent(req); files[path] = c; }
                else Debug.LogWarning("[Sound] 音のファイルが読めません: " + full + " " + req.error);
            }
            if (done != null) done(files[path]);
        }

        // ================================================================ BGM
        static readonly AudioSource[] bgmSrc = new AudioSource[2];
        static readonly float[] bgmFade = new float[2], bgmTo = new float[2];
        static readonly string[] bgmPath = new string[2];
        static string bgmWant;
        static float bgmLv = 1;

        /// <summary>鳴らす曲を決める（assets からの場所。null＝鳴らさない）。毎フレーム呼んでよい（同じ曲なら何もしない）。</summary>
        public static void Bgm(string path)
        {
            if (I == null || path == bgmWant) return;
            bgmWant = path;
            // いまの曲を下げて止める
            for (int i = 0; i < 2; i++) if (bgmPath[i] != null && bgmPath[i] != path) bgmTo[i] = 0;
            if (path == null) return;
            for (int i = 0; i < 2; i++) if (bgmPath[i] == path) { bgmTo[i] = 1; return; }
            I.StartCoroutine(LoadFile(path, c =>
            {
                if (c == null || path != bgmWant) return;
                // 使っていない口（下げている途中なら、より小さくなっている方）に入れる
                int k = bgmTo[0] <= 0 && (bgmTo[1] > 0 || bgmFade[0] <= bgmFade[1]) ? 0 : 1;
                var s = bgmSrc[k];
                s.Stop(); s.clip = c; s.loop = true; s.Play();
                bgmPath[k] = path; bgmFade[k] = 0; bgmTo[k] = 1;
            }));
        }
        /// <summary>一時的に下げる（1＝そのまま）。止めている間などに。</summary>
        public static void BgmLevel(float k) { bgmLv = k; }

        static float lvNow = 1;
        static void BgmUpdate(float dt)
        {
            lvNow = Mathf.Lerp(lvNow, bgmLv, 1 - Mathf.Exp(-dt / 0.15f));        // 一時的な下げはなめらかに
            for (int i = 0; i < 2; i++)
            {
                var s = bgmSrc[i]; if (s == null) continue;
                bgmFade[i] = Mathf.MoveTowards(bgmFade[i], bgmTo[i], dt / FADE);
                if (bgmFade[i] <= 0 && bgmTo[i] <= 0 && s.isPlaying) { s.Stop(); bgmPath[i] = null; }
                s.volume = (bgmMute ? 0 : bgmVol * BGM_BASE) * bgmFade[i] * lvNow;
            }
        }

        // ================================================================ 合成（JS版 layer をサンプルごとに計算する）
        /// <summary>決まった式の乱数（-1〜1。JS版と同じく丸めが入る）。</summary>
        class Lcg2
        {
            int s; public Lcg2(int seed) { s = seed; }
            public float Next()
            {
                double d = (double)s * 1103515245.0 + 12345.0;
                long l = (long)d; int v = (int)(l & 0xffffffffL); s = v & 0x7fffffff;
                return (float)(s / (double)0x3fffffff - 1);
            }
        }

        static float[] noise, irL, irR;
        static void Bases()
        {
            if (noise != null) return;
            // ザーという音の元（1秒ぶん。くり返して使う）
            noise = new float[SR];
            var r1 = new Lcg2(12345);
            for (int i = 0; i < SR; i++) noise[i] = r1.Next();
            // 響き。部屋で鳴らしたような残り（1.4秒で消える）
            int len = (int)(SR * 1.4f);
            irL = new float[len]; irR = new float[len];
            for (int ch = 0; ch < 2; ch++)
            {
                var d = ch == 0 ? irL : irR; var r2 = new Lcg2(777 + ch * 31);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SR;
                    d[i] = r2.Next() * Mathf.Exp(-t * 4.2f) * (t < 0.008f ? t / 0.008f : 1);
                }
            }
            // Web Audio の ConvolverNode は響きの元を正規化する（normalize＝true が既定。仕様の calculateNormalizationScale）。
            // しないと響きが何倍も大きくなる（実際、全体が2倍ほど大きく出ていた）
            double pw = 0;
            for (int i = 0; i < len; i++) pw += irL[i] * irL[i] + irR[i] * irR[i];
            pw = System.Math.Sqrt(pw / (2.0 * len));
            if (pw < 0.000125) pw = 0.000125;
            float scale = (float)(0.00125 / pw);
            for (int i = 0; i < len; i++) { irL[i] *= scale; irR[i] *= scale; }
        }

        static float[] Arr(JToken t, float def)
        {
            if (t is JArray a) { var o = new float[a.Count]; for (int i = 0; i < a.Count; i++) o[i] = a[i].Value<float>(); return o; }
            if (t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)) return new[] { t.Value<float>() };
            return new[] { def };
        }

        /// <summary>1つの音を作る。返すのは左右が交互に並んだ音（響き込み）。</summary>
        static float[] Make(JObject r)
        {
            Bases();
            var layers = r["layers"] as JArray ?? new JArray();
            float end = 0;
            foreach (JObject L in layers)
            {
                var rep = Arr(L["rep"], 1);
                int n = rep.Length >= 2 ? (int)rep[0] : 1; float every = rep.Length >= 2 ? rep[1] : 0;
                end = Mathf.Max(end, J.F(L, "at", 0) + (n - 1) * every + J.F(L, "d", 0.1f));
            }
            int len = (int)((end + 0.06f) * SR) + 1;
            var dry = new float[len];
            foreach (JObject L in layers) Layer(dry, L);
            float v = J.F(r, "v", 1), rev = J.F(r, "rev", 0);
            for (int i = 0; i < len; i++) dry[i] *= v;
            int outLen = rev > 0 ? len + irL.Length : len;
            var outS = new float[outLen * 2];
            for (int i = 0; i < len; i++) { outS[i * 2] = dry[i]; outS[i * 2 + 1] = dry[i]; }
            if (rev > 0) Reverb(dry, outS, rev * 0.5f);
            // JS版は全体の大きさ（既定 0.7）を圧縮の前に掛けている。既定の大きさで同じになるよう、ここで掛けておき、鳴らすときに戻す
            for (int i = 0; i < outS.Length; i++) outS[i] *= BAKED;
            Compress(outS);
            return outS;
        }

        /// <summary>
        /// JS版の後ろの圧縮（Web Audio の DynamicsCompressor：-6dB・knee 6dB・6:1・attack 3ms・release 150ms）の代わり。
        /// Chrome で一定の大きさの音を通して測った入力と出力の関係（静かな音は +1.8dB 持ち上がり、0dB で -1.05dB、その上は 1/5.6）に合わせ、
        /// 大きさの流れ（包絡）を追って音量を変える（1つずつ形を曲げると音が濁るため）。
        /// </summary>
        static void Compress(float[] s)
        {
            int n = s.Length / 2;
            // 大きさの流れ。Chrome と同じく 6ms 先読みして、音の頭が来る前に下げ始める
            var g = new float[n];
            float env = 0;
            float rel = 1 - Mathf.Exp(-1f / (0.15f * SR));
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Max(Mathf.Abs(s[i * 2]), Mathf.Abs(s[i * 2 + 1]));
                env = a > env ? a : env + (a - env) * rel;          // 上がるときは即（先読みのぶん、頭の前から下がる）
                g[i] = CompGain(env);
            }
            int L = (int)(0.006f * SR);
            for (int i = 0; i < n; i++)
            {
                float k = g[Mathf.Min(n - 1, i + L)];
                for (int j = i + 1; j <= Mathf.Min(n - 1, i + L); j += 8) k = Mathf.Min(k, g[j]);   // 先の6ms のうち、いちばん下げる所
                s[i * 2] *= k; s[i * 2 + 1] *= k;
            }
        }

        /// <summary>入力の大きさ → 掛ける倍率（Chrome の圧縮の静かな所の持ち上げ込み）。</summary>
        static float CompGain(float level)
        {
            if (level < 1e-5f) return 1.23f;
            float x = 20 * Mathf.Log10(level), y;
            if (x <= -9) y = x + 1.8f;
            else if (x >= 0) y = -1.05f + x / 5.6f;
            else
            {
                // -9dB（傾き1）と 0dB（傾き 1/5.6）をなめらかにつなぐ（エルミート曲線）
                float h = 9, t = (x + 9) / h;
                float t2 = t * t, t3 = t2 * t;
                y = (2 * t3 - 3 * t2 + 1) * -7.2f + (t3 - 2 * t2 + t) * h * 1 + (-2 * t3 + 3 * t2) * -1.05f + (t3 - t2) * h * (1 / 5.6f);
            }
            return Mathf.Pow(10, (y - x) / 20);
        }

        static float Ramp(float a, float b, float u, string c)
        {
            if (c == "lin") return a + (b - a) * u;
            b = Mathf.Max(1, b);
            return a * Mathf.Pow(b / a, u);
        }

        static void Layer(float[] buf, JObject L)
        {
            var rep = Arr(L["rep"], 1);
            int count = rep.Length >= 2 ? (int)rep[0] : 1; float every = rep.Length >= 2 ? rep[1] : 0;
            string w = (string)L["w"] ?? "sine", c = (string)L["c"];
            for (int k = 0; k < count; k++)
            {
                float st = J.F(L, "at", 0) + k * every;
                float d = J.F(L, "d", 0.1f);
                float a = Mathf.Min(L["a"] != null ? J.F(L, "a", 0.004f) : 0.004f, d * 0.5f);
                float pk = J.F(L, "v", 0.5f) * Mathf.Pow(J.F(L, "fall", 1), k);
                float mul = Mathf.Pow(J.F(L, "step", 1), k);
                var f = Arr(L["f"], 440);
                float f0 = f[0] * mul, f1 = (f.Length > 1 ? f[1] : f[0]) * mul;
                float s = J.F(L, "s", 0), holdEnd = st + a + (d - a) * s;
                // 削る（noise は f を「通す帯」として使う）
                string flt = (string)L["flt"] ?? (w == "noise" ? "bandpass" : null);
                float q = J.F(L, "q", 1);
                var ff = L["ff"] != null ? Arr(L["ff"], 440) : f;
                float b0 = ff[0] * mul, b1 = (ff.Length > 1 ? ff[1] : ff[0]) * mul;
                var dets = L["det"] != null ? new[] { -J.F(L, "det", 0), J.F(L, "det", 0) } : new[] { 0f };
                float each = L["det"] != null ? 0.6f : 1f;
                float[] fm = L["fm"] != null ? Arr(L["fm"], 0) : null;
                float[] vib = L["vib"] != null ? Arr(L["vib"], 0) : null;
                var ph = new double[dets.Length]; double phm = 0, phv = 0;
                // 直接形の2次の削り（WebAudio の BiquadFilter と同じ式）
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0, cb0 = 1, cb1 = 0, cb2 = 0, ca1 = 0, ca2 = 0;
                int i0 = Mathf.Max(0, (int)(st * SR)), i1 = Mathf.Min(buf.Length, (int)((st + d + 0.05f) * SR));
                for (int i = i0; i < i1; i++)
                {
                    float t = i / (float)SR, u = Mathf.Clamp01((t - st) / d);
                    // 大きさ：すっと立ち上げて、s の割合だけ保ってから、消えるまで減らす
                    float env;
                    if (t < st + a) env = Mathf.Lerp(0.0001f, pk, (t - st) / a);
                    else if (t < holdEnd) env = pk;
                    else if (t < st + d) env = pk * Mathf.Pow(0.0001f / pk, (t - holdEnd) / Mathf.Max(1e-5f, st + d - holdEnd));
                    else env = 0.0001f;
                    float fr = f1 != f0 ? Ramp(f0, f1, u, c) : f0;
                    float x;
                    if (w == "noise") x = noise[(i - i0) % SR];
                    else
                    {
                        float add = 0;
                        if (fm != null && fm.Length >= 2)
                        {
                            float mf = fr * fm[0];
                            float dep0 = fm[1] * fm[0] * f0, dep1 = Mathf.Max(0.01f, fm[1] * fm[0] * f1 * 0.1f);
                            float dep = dep0 * Mathf.Pow(dep1 / dep0, u);
                            phm += 2 * System.Math.PI * mf / SR;
                            add += (float)System.Math.Sin(phm) * dep;
                        }
                        if (vib != null && vib.Length >= 2)
                        {
                            phv += 2 * System.Math.PI * vib[0] / SR;
                            add += (float)System.Math.Sin(phv) * vib[1] * f0;
                        }
                        x = 0;
                        for (int j = 0; j < dets.Length; j++)
                        {
                            double fi = (fr + add) * System.Math.Pow(2, dets[j] / 1200.0);
                            ph[j] += 2 * System.Math.PI * fi / SR;
                            if (ph[j] > 2 * System.Math.PI) ph[j] -= 2 * System.Math.PI * System.Math.Floor(ph[j] / (2 * System.Math.PI));
                            double p = ph[j];
                            float y;
                            switch (w)
                            {
                                case "square": y = p < System.Math.PI ? 1 : -1; break;
                                case "saw": y = (float)(p / System.Math.PI - 1); break;
                                case "triangle": y = (float)(p < System.Math.PI ? p / System.Math.PI * 2 - 1 : 3 - p / System.Math.PI * 2); break;
                                default: y = (float)System.Math.Sin(p); break;
                            }
                            x += y * each;
                        }
                    }
                    if (flt != null)
                    {
                        if (((i - i0) & 15) == 0)
                        {
                            float bf = b1 != b0 ? Ramp(b0, b1, u, c) : b0;
                            double w0 = 2 * System.Math.PI * Mathf.Min(bf, SR * 0.49f) / SR, cs = System.Math.Cos(w0), sn = System.Math.Sin(w0);
                            double alpha, nb0, nb1, nb2;
                            if (flt == "bandpass") { alpha = sn / (2 * q); nb0 = alpha; nb1 = 0; nb2 = -alpha; }
                            else
                            {
                                alpha = sn / (2 * System.Math.Pow(10, q / 20.0));      // lowpass / highpass の Q は dB
                                if (flt == "highpass") { nb0 = (1 + cs) / 2; nb1 = -(1 + cs); nb2 = (1 + cs) / 2; }
                                else { nb0 = (1 - cs) / 2; nb1 = 1 - cs; nb2 = (1 - cs) / 2; }
                            }
                            double a0 = 1 + alpha;
                            cb0 = nb0 / a0; cb1 = nb1 / a0; cb2 = nb2 / a0; ca1 = -2 * cs / a0; ca2 = (1 - alpha) / a0;
                        }
                        double yv = cb0 * x + cb1 * x1 + cb2 * x2 - ca1 * y1 - ca2 * y2;
                        x2 = x1; x1 = x; y2 = y1; y1 = yv;
                        x = (float)yv;
                    }
                    buf[i] += x * env;
                }
            }
        }

        // ---------------------------------------------------------------- 響き（たたみ込み。FFT で）
        static void Reverb(float[] dry, float[] outS, float send)
        {
            int n = 1; while (n < dry.Length + irL.Length) n <<= 1;
            var re = new double[n]; var im = new double[n];
            for (int i = 0; i < dry.Length; i++) re[i] = dry[i];
            FFT(re, im, false);
            // 左右の響きの元を、実部と虚部に入れて一度に変換する
            var hr = new double[n]; var hi = new double[n];
            for (int i = 0; i < irL.Length; i++) { hr[i] = irL[i]; hi[i] = irR[i]; }
            FFT(hr, hi, false);
            // H = HL + i·HR から HL・HR を取り出し、Y = X·HL + i·X·HR（逆変換の実部が左、虚部が右）
            var yr = new double[n]; var yi = new double[n];
            for (int k = 0; k < n; k++)
            {
                int m = (n - k) & (n - 1);
                double lr = (hr[k] + hr[m]) / 2, li = (hi[k] - hi[m]) / 2;          // HL
                double rr = (hi[k] + hi[m]) / 2, ri = -(hr[k] - hr[m]) / 2;         // HR
                double xr = re[k], xi = im[k];
                double ar = xr * lr - xi * li, ai = xr * li + xi * lr;                // X·HL
                double br = xr * rr - xi * ri, bi = xr * ri + xi * rr;                // X·HR
                yr[k] = ar - bi; yi[k] = ai + br;                                     // X·HL + i·X·HR
            }
            FFT(yr, yi, true);
            int len = outS.Length / 2;
            for (int i = 0; i < len && i < n; i++) { outS[i * 2] += (float)(yr[i] * send); outS[i * 2 + 1] += (float)(yi[i] * send); }
        }

        static void FFT(double[] re, double[] im, bool inv)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { var t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int size = 2; size <= n; size <<= 1)
            {
                double ang = 2 * System.Math.PI / size * (inv ? 1 : -1);
                double wr = System.Math.Cos(ang), wi = System.Math.Sin(ang);
                for (int i = 0; i < n; i += size)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < size / 2; k++)
                    {
                        int a = i + k, b = a + size / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
            if (inv) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
        }
    }
}
