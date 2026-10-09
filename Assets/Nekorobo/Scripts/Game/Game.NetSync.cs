using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// オンラインのゲーム中の同期（JS版 npSendInput / npPayload / npHostTick / npApply / npFx と NETMOD.on("from") の "in" "s" "hb"）。
    ///
    ///   ホスト … いままでどおり物理を回す。参加者のロボは**届いた入力**で動かす（NetInputOf）。
    ///            30回/秒、動く物の位置と画面に出す数字を配る（変わらない項目は変わったときだけ。1秒ごとに全部）
    ///   ゲスト … 物理を回さない（Physics.simulationMode を Script にして、物は全部 kinematic）。
    ///            自分の入力を送り、届いた位置を**少し過去を描いて補間**して当てる
    ///
    /// **便りの中身と座標は HTML版と同じ**（three.js の向き：Unity の z を反転、回転は (−x, −y, z, w)、向きの角は符号を反転）。
    /// 物を指す番号（nid）は、面を組んだ順の通し番号。HTML版と同じ順で組むので、HTML版とも番号がそろう。
    /// </summary>
    public partial class Game
    {
        // ---------------------------------------------------------------- 物の通し番号
        [System.NonSerialized] public int nidSeq, entsBase;
        public readonly Dictionary<int, Ent> byNid = new Dictionary<int, Ent>();
        void NidAdd(Ent e, int nid = 0)
        {
            e.nid = nid > 0 ? nid : ++nidSeq;
            if (nidSeq < e.nid) nidSeq = e.nid;
            byNid[e.nid] = e;
        }

        // ---------------------------------------------------------------- 決まり（JS版と同じ数）
        const int NP_HZ = 30, NP_IN_HZ = 30;
        const float NP_LAG = 2f / NP_HZ;             // 過去を描く分は「配る間隔の2つぶん」
        const float NP_IN_STALE = 0.7f;              // 参加者の入力が届かなくなったら捨てる[秒]
        static readonly string[] NP_KEYS = { "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", " ", "use", "cycle", "cycleBack", "ok",
                                             "navUp", "navDown", "navLeft", "navRight" };

        class NetIn { public JObject k; public Vector2? am; public float at; }
        class Snap { public JObject d; public float at; public Dictionary<int, float[]> e; public JArray ev; public bool evDone; }
        class NetFxObj { public Transform mesh, fire; public Transform load; public Material food; public int kind; }

        // 同期の控え
        readonly Dictionary<int, NetIn> npInputs = new Dictionary<int, NetIn>();
        readonly List<Snap> npBuf = new List<Snap>();
        readonly Dictionary<string, string> npLast = new Dictionary<string, string>();
        readonly Dictionary<int, NetFxObj> netFx = new Dictionary<int, NetFxObj>();
        JArray npEv = new JArray();
        bool npReplay;
        float npClock, npSendT, npSnapT, npFullT, npLastRecv, npHbT;
        int npLastGn = -1;
        string npLastIn = "";
        bool npHostHidden;

        public bool NetGuest { get { return np.role == "guest"; } }
        public bool NetHost { get { return np.role == "host"; } }

        // ---------------------------------------------------------------- 座標（HTML版の向きへ）
        public static float R2(float v) { return Mathf.Round(v * 100) / 100; }
        static float R3(float v) { return Mathf.Round(v * 1000) / 1000; }
        static float R1(float v) { return Mathf.Round(v * 10) / 10; }
        static Vector3 FromJs(float x, float y, float z) { return new Vector3(x, y, -z); }
        /// <summary>向きの角（three.js の rotation.y・ラジアン）→ Unity の y 回転（度）。</summary>
        static float YawFromJs(float ry) { return -ry * Mathf.Rad2Deg; }
        static float YawToJs(Transform t) { return R2(-t.eulerAngles.y * Mathf.Deg2Rad); }
        public static string Hex6(int c) { return "#" + c.ToString("x6"); }
        /// <summary>数字の吹き出しを出来事として控える（Hud.Pop から）。</summary>
        public void NetEvPop(Vector3 w, string text, int col, bool big) { NetEv("pop", R2(w.x), R2(w.y), R2(-w.z), text, Hex6(col), big ? 1 : 0); }
        static int HexOf(string s, int dflt)
        {
            if (string.IsNullOrEmpty(s)) return dflt;
            s = s.TrimStart('#');
            int v; return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v) ? v : dflt;
        }

        /// <summary>
        /// 一度きりの出来事を控える（ホストだけ。受けて再生している間は控えない）。JS版 npEv。
        /// 破片や吹き出しは状態ではなく出来事なので、位置の一覧とは別に配る。
        /// </summary>
        public void NetEv(params object[] a)
        {
            if (np.role != "host" || npReplay) return;
            var arr = new JArray();
            foreach (var x in a) arr.Add(x == null ? JValue.CreateNull() : JToken.FromObject(x));
            npEv.Add(arr);
        }

        // ---------------------------------------------------------------- 始まりと終わり
        /// <summary>面を組み終えたら呼ぶ。ゲストは物理を止めて、物を全部「置くだけ」にする。</summary>
        void NetAfterBuild()
        {
            entsBase = ents.Count;
            npBuf.Clear(); npClock = 0; npLast.Clear(); npLastGn = -1; npFullT = 0; npLastIn = "";
            foreach (var o in netFx.Values) if (o.mesh != null) Destroy(o.mesh.gameObject);
            netFx.Clear();
            npLastRecv = Time.realtimeSinceStartup;
            bool guest = NetGuest;
            Physics.simulationMode = guest ? SimulationMode.Script : SimulationMode.FixedUpdate;
            if (!guest) return;
            foreach (var e in ents) GuestFreeze(e);
        }
        static void GuestFreeze(Ent e)
        {
            if (e.rb == null) return;
            e.rb.isKinematic = true;
            e.rb.interpolation = RigidbodyInterpolation.None;
        }
        void NetStop()
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
            npInputs.Clear(); npBuf.Clear();
        }

        // ---------------------------------------------------------------- 入力
        /// <summary>オンラインの相手の入力（ホストが読む）。0.7秒届かなければ止まったとみなして捨てる。</summary>
        BotInput NetInputOf(Player P)
        {
            var i = new BotInput();
            NetIn v;
            if (!npInputs.TryGetValue(P.src.netId, out v) || v.k == null) { P.netAim = null; return i; }
            if (Time.realtimeSinceStartup - v.at > NP_IN_STALE) { P.netAim = null; return i; }
            System.Func<string, bool> k = key => v.k[key] != null && (int?)v.k[key] != 0;
            i.up = k("ArrowUp"); i.down = k("ArrowDown"); i.left = k("ArrowLeft"); i.right = k("ArrowRight");
            i.jump = k(" "); i.use = k("use"); i.cycle = k("cycle"); i.cycleBack = k("cycleBack"); i.ok = k("ok");
            i.navUp = k("navUp"); i.navDown = k("navDown"); i.navLeft = k("navLeft"); i.navRight = k("navRight");
            // 参加者が出している狙いの位置。向こうで出した値をそのまま使う（ホストで積み直すと食い違ってカクつく）
            P.netAim = v.am.HasValue ? (Vector3?)FromJs(v.am.Value.x, 0.06f, v.am.Value.y) : null;
            return i;
        }

        /// <summary>ゲスト：自分の入力を送る（30回/秒。押した／離した瞬間は間隔を待たずに）。</summary>
        void NpSendInput(float dt)
        {
            npSendT -= dt;
            var P = me; if (P == null) return;
            var inp = P.input;
            var k = new JObject();
            void Put(string key, bool on) { if (on) k[key] = 1; }
            Put("ArrowUp", inp.up); Put("ArrowDown", inp.down); Put("ArrowLeft", inp.left); Put("ArrowRight", inp.right);
            Put(" ", inp.jump); Put("use", inp.use); Put("cycle", inp.cycle); Put("cycleBack", inp.cycleBack); Put("ok", inp.ok);
            Put("navUp", inp.navUp); Put("navDown", inp.navDown); Put("navLeft", inp.navLeft); Put("navRight", inp.navRight);
            JToken am = P.aim.HasValue ? new JArray(R2(P.aim.Value.x), R2(-P.aim.Value.z)) : (JToken)JValue.CreateNull();
            string now = k.ToString(Newtonsoft.Json.Formatting.None) + "|" + am.ToString(Newtonsoft.Json.Formatting.None);
            bool changed = now != npLastIn;
            if (npSendT > 0 && !changed) return;
            npSendT = 1f / NP_IN_HZ;
            npLastIn = now;
            Net.Send(new JObject { ["p"] = "in", ["k"] = k, ["am"] = am });
        }

        /// <summary>ゲスト：手元の照準を動かす（待たずに動くように手元で描く。離して撃つのはホスト）。</summary>
        void GuestAim(float dt)
        {
            var P = me; if (P == null) return;
            var it = AimItemOf(P);
            if (it == null || P.down) { if (P.aim != null) ClearAim(P); return; }
            if (P.input.use) { if (P.aim == null) StartAimQuiet(P); else MoveAim(P, dt); }
            else if (P.aim != null) ClearAim(P);
            P.prevUse = P.input.use;
        }

        // ---------------------------------------------------------------- ホスト：配る
        JArray NpSnap()
        {
            var o = new JArray();
            foreach (var e in ents)
            {
                if (e == null || e.rb == null || e.isFixed || e.nid == 0) continue;
                var t = e.rb.position; var q = e.rb.rotation;
                // three.js の向きへ（z を反転、回転は (−x, −y, z, w)）
                float x = t.x, y = t.y, z = -t.z, qx = -q.x, qy = -q.y, qz = q.z, qw = q.w;
                var l = e.netLast;
                if (l != null && Mathf.Abs(l[0] - x) < 0.002f && Mathf.Abs(l[1] - y) < 0.002f && Mathf.Abs(l[2] - z) < 0.002f && Mathf.Abs(l[6] - qw) < 0.002f) continue;
                e.netLast = new[] { x, y, z, qx, qy, qz, qw };
                o.Add(new JArray(e.nid, R3(x), R3(y), R3(z), R3(qx), R3(qy), R3(qz), R3(qw)));
            }
            return o;
        }

        /// <summary>アイテムで出た物（1=バナナ 2=ドローン 3=ビーム 4=ミサイル 5=ブーメラン）。見た目だけの物なので別で配る。</summary>
        JArray NpFxList()
        {
            var o = new JArray();
            foreach (var b in bananas) o.Add(new JArray(b.nid, 1, R2(b.p.x), 0.07f, R2(-b.p.z), 0));
            foreach (var d in drones) { var p = d.mesh.position; o.Add(new JArray(d.nid, 2, R2(p.x), R2(p.y), R2(-p.z), YawToJs(d.mesh), Dishes.All.IndexOf(d.dish))); }
            foreach (var s in shots) { var p = s.mesh.position; o.Add(new JArray(s.nid, s.homing ? 4 : 3, R2(p.x), R2(p.y), R2(-p.z), YawToJs(s.mesh))); }
            foreach (var b in booms) { var p = b.mesh.position; o.Add(new JArray(b.nid, 5, R2(p.x), R2(p.y), R2(-p.z), YawToJs(b.mesh), b.dish != null ? Dishes.All.IndexOf(b.dish.dish) : -1)); }
            return o;
        }

        /// <summary>変わったときだけ送る項目（変わっていなければ null を返して、便りに入れない）。</summary>
        JToken NpSame(string key, JToken val, bool full)
        {
            string s = val.ToString(Newtonsoft.Json.Formatting.None);
            string old;
            if (!full && npLast.TryGetValue(key, out old) && old == s) return null;
            npLast[key] = s;
            return val;
        }

        JObject NpPayload(bool full)
        {
            var ev = npEv; npEv = new JArray();
            var d = new JObject
            {
                ["p"] = "s", ["gn"] = np.stage, ["t"] = R3(t), ["fr"] = frames, ["st"] = state, ["rt"] = R2(readyT),
                ["oi"] = oi, ["sd"] = R1(shopDmg),
            };
            var ol = new JArray();
            foreach (var o in orders) ol.Add(new JArray(Dishes.All.IndexOf(o.dish), guests.IndexOf(o.guest), o.done ? 1 : 0));
            var v = NpSame("ol", ol, full); if (v != null) d["ol"] = v;
            var am = new JArray();
            foreach (var P in players) am.Add(P.aim.HasValue ? (JToken)new JArray(R2(P.aim.Value.x), R2(-P.aim.Value.z)) : JValue.CreateNull());
            v = NpSame("am", am, full); if (v != null) d["am"] = v;
            d["cl"] = result != null ? (JToken)(result.cleared ? 1 : 0) : JValue.CreateNull();
            var gh = new JArray(); foreach (var g in guests) gh.Add(Mathf.RoundToInt(g.hp));
            v = NpSame("gh", gh, full); if (v != null) d["gh"] = v;
            if (full) d["sig"] = NpSig();
            d["fx"] = NpFxList();
            var rp = new JArray(); foreach (var R in routed) rp.Add(new JArray(R3(R.pos.x), R3(-R.pos.z)));
            d["rp"] = rp;
            d["ev"] = ev.Count > 0 ? (JToken)ev : JValue.CreateNull();
            var it = new JArray();
            foreach (var P in players) { var W = WalletOf(P); it.Add(new JArray(JObject.FromObject(W.items), JObject.FromObject(W.ammo))); }
            v = NpSame("it", it, full); if (v != null) d["it"] = v;
            var ca = new JArray();
            foreach (var P in players)
                ca.Add(P.carried != null ? (JToken)new JArray(Dishes.All.IndexOf(P.carried.dish), Mathf.RoundToInt(P.carried.integ), orders.IndexOf(P.carried.order))
                                         : JValue.CreateNull());
            d["ca"] = ca;
            var pl = new JArray();
            foreach (var P in players)
                pl.Add(new JArray(Mathf.RoundToInt(P.sales), R1(P.botDmg), R1(P.shopDmg), P.down ? 1 : 0, P.carried != null ? 1 : 0,
                                  P.delivered, P.hurt, Mathf.RoundToInt(P.wreck), P.bestCombo, P.slot ?? "", R1(P.revT), R1(P.invT)));
            d["pl"] = pl;
            d["e"] = NpSnap();
            return d;
        }

        void NpHostTick(float dt)
        {
            npSnapT -= dt;
            if (npSnapT > 0) return;
            npSnapT = 1f / NP_HZ;
            // 面が変わったら、控えを捨てて全部送り直す（前の面の値を引き継がせない）
            if (npLastGn != np.stage) { npLastGn = np.stage; npLast.Clear(); npFullT = 0; }
            npFullT -= 1f / NP_HZ;
            bool full = npFullT <= 0;
            if (full) npFullT = 1;
            Net.Send(NpPayload(full));
        }

        /// <summary>ホストの「生きています」の知らせ（1秒ごと）。位置の便りはゲームが止まっている間は出ないため。</summary>
        void NpHeartbeat(float dt)
        {
            npHbT -= dt;
            if (npHbT > 0) return;
            npHbT = 1;
            Net.Send(new JObject { ["p"] = "hb", ["hid"] = Application.isFocused ? 0 : 1 });
        }

        /// <summary>
        /// ステージの指紋。**両方が同じデータで組み立てたか**を確かめる（JS版 npSig と同じ計算）。
        /// 物の番号は組んだ順なので、片方だけ物の数が違うと全部ズレる。黙ってズレないよう、気づけるようにする。
        /// </summary>
        JObject NpSig()
        {
            var C = stage;
            string tt = string.Join("|", C.terrain) + "#" + (C.raw != null && C.raw["origin"] is JArray oa ? JsJoin(oa) : C.origin.x + "," + C.origin.y)
                      + "#" + JsLen(C.raw != null ? C.raw["objects"] : null);
            int h = 5381;
            foreach (char ch in tt) h = unchecked((h * 33) ^ ch);
            return new JObject { ["h"] = h, ["n"] = entsBase, ["p"] = players.Count };
        }
        static string JsJoin(JArray a)
        {
            var s = new List<string>();
            foreach (var v in a) s.Add(JsNum(v));
            return string.Join(",", s.ToArray());
        }
        static string JsNum(JToken v)
        {
            if (v.Type == JTokenType.Integer) return v.ToString();
            if (v.Type == JTokenType.Float)
            {
                double d = (double)v;
                if (d == System.Math.Floor(d) && System.Math.Abs(d) < 1e15) return ((long)d).ToString();
                return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            return v.ToString();
        }
        /// <summary>JSON.stringify(objects).length を、JS と同じ書き方で数える（数は整数なら小数点を付けない）。</summary>
        static int JsLen(JToken t)
        {
            if (t == null) return 2;                                  // "[]"
            var sb = new System.Text.StringBuilder();
            JsWrite(t, sb);
            return sb.Length;
        }
        static void JsWrite(JToken t, System.Text.StringBuilder sb)
        {
            switch (t.Type)
            {
                case JTokenType.Array:
                    sb.Append('['); bool f = true;
                    foreach (var v in (JArray)t) { if (!f) sb.Append(','); f = false; JsWrite(v, sb); }
                    sb.Append(']'); break;
                case JTokenType.Object:
                    sb.Append('{'); bool g = true;
                    foreach (var p in (JObject)t) { if (!g) sb.Append(','); g = false; sb.Append(Newtonsoft.Json.JsonConvert.ToString(p.Key)).Append(':'); JsWrite(p.Value, sb); }
                    sb.Append('}'); break;
                case JTokenType.Integer: case JTokenType.Float: sb.Append(JsNum(t)); break;
                case JTokenType.String: sb.Append(Newtonsoft.Json.JsonConvert.ToString((string)t)); break;
                case JTokenType.Boolean: sb.Append((bool)t ? "true" : "false"); break;
                default: sb.Append("null"); break;
            }
        }

        // ---------------------------------------------------------------- 受ける（"in" "s" "hb"）
        void NetSyncHook()
        {
            Net.OnFrom += (id, d) =>
            {
                if (!NpOn) return;
                string p = (string)d["p"];
                // ホストから届いたものなら、何であれ「生きている」印にする
                if (NetGuest && Net.Room != null && id == (int?)Net.Room["host"]) npLastRecv = Time.realtimeSinceStartup;
                if (p == "hb")
                {
                    if (!NetGuest) return;
                    bool hid = (int?)d["hid"] == 1;
                    if (hid != npHostHidden) { npHostHidden = hid; hud.NetWarn(hid ? "ホストの画面が裏に回っています。ホストが戻るまで止まります" : "", true); }
                    return;
                }
                if (p == "in" && NetHost)
                {
                    Vector2? am = null;
                    if (d["am"] is JArray a && a.Count >= 2) am = new Vector2((float)a[0], (float)a[1]);
                    npInputs[id] = new NetIn { k = d["k"] as JObject, am = am, at = Time.realtimeSinceStartup };
                    return;
                }
                if (p == "s" && NetGuest)
                {
                    if (((int?)d["gn"] ?? 0) != np.stage) return;               // 前の面の便りは捨てる
                    var e = new Dictionary<int, float[]>();
                    if (d["e"] is JArray ea)
                        foreach (JArray row in ea)
                        {
                            var v = new float[7];
                            for (int i = 0; i < 7; i++) v[i] = (float)row[i + 1];
                            e[(int)row[0]] = v;
                        }
                    // 前の控えから引き継ぐ（動かなかった物・変わらなかった項目は送られてこない）
                    var last = npBuf.Count > 0 ? npBuf[npBuf.Count - 1] : null;
                    if (last != null)
                    {
                        foreach (var kv in last.e) if (!e.ContainsKey(kv.Key)) e[kv.Key] = kv.Value;
                        foreach (var kv in last.d) if (d[kv.Key] == null && kv.Key != "ev") d[kv.Key] = kv.Value;
                    }
                    var ev = d["ev"] as JArray;
                    npBuf.Add(new Snap { d = d, at = npClock, e = e, ev = ev, evDone = ev == null });
                    if (npBuf.Count > 8) npBuf.RemoveAt(0);
                    npLastRecv = Time.realtimeSinceStartup;
                }
            };
        }

        // ---------------------------------------------------------------- ゲスト：当てる
        /// <summary>届いた位置を当てる。届いた瞬間に当てるとカクつくので、少し過去を描いて間を補間する。</summary>
        void NpApply(float dt)
        {
            npClock += dt;
            // 便りが止まったら見つける（通信が閉じないまま止まることがある）。30回/秒で届くので、6秒も空いたらもう来ない
            if (npLastRecv > 0 && Time.realtimeSinceStartup - npLastRecv > 6f) { NpLostHost("ホストからの通信が6秒以上とだえました。"); return; }
            if (npBuf.Count == 0) return;
            float show = npClock - NP_LAG;
            Snap a = npBuf[0], b = npBuf[0];
            for (int i = 0; i < npBuf.Count; i++)
                if (npBuf[i].at <= show) { a = npBuf[i]; b = i + 1 < npBuf.Count ? npBuf[i + 1] : npBuf[i]; }
            float span = b.at - a.at;
            float u = span > 1e-4f ? Mathf.Clamp01((show - a.at) / span) : 1;
            // 出来事は、時が来た便りを1つ残らず拾う（間の便りの出来事を落とさないように）
            var due = new List<JArray>();
            foreach (var q in npBuf)
            {
                if (q.ev == null || q.evDone || q.at > show) continue;
                q.evDone = true; due.Add(q.ev);
            }
            while (npBuf.Count > 2 && npBuf[1].at <= show) npBuf.RemoveAt(0);

            var A = a.d; var B = b.d;
            // ---- 位置
            foreach (var kv in a.e)
            {
                Ent e;
                if (!byNid.TryGetValue(kv.Key, out e) || e == null || e.rb == null) continue;
                var p0 = kv.Value; float[] p1;
                if (!b.e.TryGetValue(kv.Key, out p1)) p1 = p0;
                bool jump = Mathf.Abs(p1[0] - p0[0]) > 3 || Mathf.Abs(p1[2] - p0[2]) > 3;   // 端から反対側へ出る物は飛ぶ
                float uu = jump ? 1 : u;
                float x = p0[0] + (p1[0] - p0[0]) * uu, y = p0[1] + (p1[1] - p0[1]) * uu, z = p0[2] + (p1[2] - p0[2]) * uu;
                if (!jump && e.kind == "car")
                {
                    // 自動で走る物は、遅れのぶん先を読んで描く（速さはホストの時計で出す）
                    float dtH = (float)B["t"] - (float)A["t"];
                    float spin = Mathf.Abs(p1[3] - p0[3]) + Mathf.Abs(p1[4] - p0[4]) + Mathf.Abs(p1[5] - p0[5]) + Mathf.Abs(p1[6] - p0[6]);
                    if (dtH > 1e-4f && spin < 0.02f) { x += (p1[0] - p0[0]) / dtH * NP_LAG; z += (p1[2] - p0[2]) / dtH * NP_LAG; }
                }
                float qx = p0[3] + (p1[3] - p0[3]) * uu, qy = p0[4] + (p1[4] - p0[4]) * uu, qz = p0[5] + (p1[5] - p0[5]) * uu, qw = p0[6] + (p1[6] - p0[6]) * uu;
                float n = Mathf.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw); if (n < 1e-6f) n = 1;
                var pos = new Vector3(x, y, -z);
                var rot = new Quaternion(-qx / n, -qy / n, qz / n, qw / n);
                e.transform.SetPositionAndRotation(pos, rot);
                e.rb.position = pos; e.rb.rotation = rot;
            }
            // ---- 状態と時計（step を回さないので、3・2・1 もここで進める）
            if (A["st"] != null)
            {
                state = (string)A["st"]; readyT = (float?)A["rt"] ?? 0; frames = (int?)A["fr"] ?? 0;
                var nw = npBuf[npBuf.Count - 1];
                t = nw.d["t"] != null ? (float)nw.d["t"] + Mathf.Max(0, npClock - nw.at) : (float?)A["t"] ?? t;
            }
            // ---- 注文（物の入れ替えではなく中身を上書き。運んでいる料理がこの注文の物を指しているため）
            if (A["ol"] is JArray ol && ol.Count == orders.Count)
            {
                bool ch = false;
                for (int i = 0; i < ol.Count; i++)
                {
                    var v = (JArray)ol[i]; var o = orders[i];
                    int di = (int)v[0], gi = (int)v[1];
                    var dish = di >= 0 && di < Dishes.All.Count ? Dishes.All[di] : o.dish;
                    if (dish != o.dish) { o.dish = dish; ch = true; }
                    if (gi >= 0 && gi < guests.Count) o.guest = guests[gi];
                    o.done = (int)v[2] != 0;
                }
                if (ch) SyncCounterPlates();
            }
            if (A["oi"] != null) { int noi = (int)A["oi"]; if (noi != oi) { oi = noi; SyncCounterPlates(); } }
            if (A["sd"] != null) shopDmg = (float)A["sd"];
            // ---- ルートで動く物（見た目と、上に乗せた物）
            if (A["rp"] is JArray rp)
                for (int i = 0; i < rp.Count && i < routed.Count; i++)
                {
                    var v = (JArray)rp[i];
                    var w = B["rp"] is JArray brp && i < brp.Count ? (JArray)brp[i] : v;
                    var R = routed[i];
                    R.pos.x = (float)v[0] + ((float)w[0] - (float)v[0]) * u;
                    R.pos.z = -((float)v[1] + ((float)w[1] - (float)v[1]) * u);
                    SetRoutePos(R, 0, 0);
                }
            if (A["gh"] is JArray gh) for (int i = 0; i < gh.Count && i < guests.Count; i++) guests[i].hp = (float)gh[i];
            // ---- 手持ちのアイテムの数（ショップを開いている間は写さない。買い物の間は財布を各自が持つ）
            if (A["it"] is JArray ita && !hud.ShopOpen)
                for (int i = 0; i < ita.Count && i < players.Count; i++)
                {
                    var W = WalletOf(players[i]);
                    if (ita[i] is JArray pair)
                    {
                        if (pair.Count > 0 && pair[0] is JObject its) foreach (var p in its) W.items[p.Key] = (int?)p.Value ?? 0;
                        if (pair.Count > 1 && pair[1] is JObject am) foreach (var p in am) W.ammo[p.Key] = (int?)p.Value ?? 0;
                    }
                }
            // ---- 運んでいる料理（皿の見た目と崩れ具合。配膳先の目印と画面の表示がこれを見る）
            if (A["ca"] is JArray ca)
                for (int i = 0; i < ca.Count && i < players.Count; i++)
                {
                    var P = players[i];
                    if (!(ca[i] is JArray v)) { if (P.carried != null || P.netDish != -1) { P.netDish = -1; P.carried = null; P.look.ShowDish(null); } continue; }
                    int di = (int)v[0];
                    var dish = di >= 0 && di < Dishes.All.Count ? Dishes.All[di] : null;
                    if (dish == null) continue;
                    if (P.netDish != di) { P.netDish = di; P.look.ShowDish(dish); }
                    int oix = (int)v[2];
                    var od = oix >= 0 && oix < orders.Count ? orders[oix] : null;
                    if (od != null) P.carried = new Carried { order = od, dish = dish, integ = (float)v[1] };
                }
            if (A["pl"] is JArray pl)
                for (int i = 0; i < pl.Count && i < players.Count; i++)
                {
                    var v = (JArray)pl[i]; var P = players[i];
                    P.sales = (float)v[0]; P.botDmg = (float)v[1]; P.shopDmg = (float)v[2];
                    P.down = (int)v[3] != 0; P.delivered = (int)v[5]; P.hurt = (int)v[6];
                    P.wreck = v.Count > 7 ? (float)v[7] : 0; P.bestCombo = v.Count > 8 ? (int)v[8] : 0;
                    P.slot = v.Count > 9 && (string)v[9] != "" ? (string)v[9] : null;
                    P.revT = v.Count > 10 ? (float)v[10] : 0; P.invT = v.Count > 11 ? (float)v[11] : 0;
                }
            // ---- 照準の輪。自分のぶんは手元で描く（ホストの値へは、手を止めている間だけ寄せる）。ほかの人のぶんはここで作って動かす
            if (A["am"] is JArray ama)
                for (int i = 0; i < ama.Count && i < players.Count; i++)
                {
                    var P = players[i];
                    var v = ama[i] as JArray;
                    if (P == me)
                    {
                        bool moving = P.input.navUp || P.input.navDown || P.input.navLeft || P.input.navRight;
                        if (v != null && P.aim != null && !moving)
                        {
                            var want = FromJs((float)v[0], 0.06f, (float)v[1]);
                            P.aim = Vector3.Lerp(P.aim.Value, want, Mathf.Min(1, dt * 6));
                            if (P.aimMesh != null) P.aimMesh.position = P.aim.Value;
                        }
                        continue;
                    }
                    if (v == null) { if (P.aim != null) ClearAim(P); continue; }
                    P.aim = FromJs((float)v[0], 0.06f, (float)v[1]);
                    if (P.aimMesh == null) P.aimMesh = AimMesh(P.col);
                    P.aimMesh.position = P.aim.Value;
                }
            // ---- 食い違いの見張り。物の数か指紋が違ったら、黙ってズレる前に知らせる
            if (A["sig"] is JObject sg && a.d == npBuf[0].d)
            {
                var mine = NpSig();
                string bad = (int?)sg["n"] != (int)mine["n"] ? "物の数が違います（" + sg["n"] + " と " + mine["n"] + "）"
                           : (int?)sg["h"] != (int)mine["h"] ? "ステージのデータが違います"
                           : (int?)sg["p"] != (int)mine["p"] ? "人数が違います" : "";
                if (bad.Length > 0) hud.NetWarn("オンラインの食い違い：" + bad + "／ステージのファイルと assets をそろえてください", false);
                else if (!npHostHidden) hud.NetWarn("", false);
            }
            // ---- アイテムで出た物。届いた一覧に合わせて、作る・動かす・消す
            if (A["fx"] is JArray fx)
            {
                var live = new HashSet<int>();
                foreach (JArray v in fx)
                {
                    int id = (int)v[0], kind = (int)v[1];
                    live.Add(id);
                    NetFxObj o;
                    if (!netFx.TryGetValue(id, out o))
                    {
                        o = new NetFxObj { kind = kind };
                        if (kind == 1) o.mesh = BananaMesh(Vector3.zero);
                        else if (kind == 2) { int di = v.Count > 6 ? (int)v[6] : 0; o.mesh = DroneMesh(di >= 0 && di < Dishes.All.Count ? Dishes.All[di].col : 0xffffff); }
                        else if (kind == 5) { o.mesh = BoomMesh(out o.load, out o.food); }
                        else o.mesh = ShotMesh(kind == 4, out o.fire);
                        netFx[id] = o;
                    }
                    if (kind == 5)
                    {
                        int di = v.Count > 6 ? (int)v[6] : -1;
                        o.load.gameObject.SetActive(di >= 0);
                        if (di >= 0 && di < Dishes.All.Count) o.food.SetColor("_BaseColor", Mats.Hex(Dishes.All[di].col));
                    }
                    o.mesh.position = FromJs((float)v[2], (float)v[3], (float)v[4]);
                    var ea = o.mesh.eulerAngles; o.mesh.eulerAngles = new Vector3(ea.x, YawFromJs((float)v[5]), ea.z);
                    if (o.fire != null) o.fire.localScale = Vector3.one * (0.7f + Random.value * 0.6f);    // 炎のゆらぎは見た目だけ
                }
                var gone = new List<int>();
                foreach (var kv in netFx) if (!live.Contains(kv.Key)) { if (kv.Value.mesh != null) Destroy(kv.Value.mesh.gameObject); gone.Add(kv.Key); }
                foreach (var id in gone) netFx.Remove(id);
            }
            // ---- 出来事の再生（届いた回だけ1度）
            if (due.Count > 0)
            {
                npReplay = true;
                foreach (var list in due) foreach (JArray e in list) try { NpReplayOne(e); } catch (System.Exception ex) { Debug.LogWarning("[Net] 出来事を再生できません: " + e + " " + ex.Message); }
                npReplay = false;
            }
            // 吹き出しと表情の消し込み（step を回さないので、ここで進める）
            foreach (var P in players)
            {
                if (P.msgT > 0) { P.msgT -= dt; if (P.msgT <= 0) P.msg = ""; }
                if (P.faceT > 0) { P.faceT -= dt; if (P.faceT <= 0 && P.face != "dead" && P.face != "happy") { P.face = "norm"; P.faceT = 99; } }
            }
            // ---- 結果。ホストが終わったら、こちらでも同じ数字で出す（成績は上で写してあるので、同じ計算で同じ結果）
            if ((string)A["st"] == "result" && result == null) Finish(A["cl"] != null && A["cl"].Type == JTokenType.Integer && (int)A["cl"] == 1);
        }

        void NpReplayOne(JArray e)
        {
            string k = (string)e[0];
            switch (k)
            {
                case "rs": RespawnMark(FromJs((float)e[1], 0, (float)e[2]), (int)e[3], (int)e[4] != 0); break;
                case "bm": BallFly(null, FromJs((float)e[1], 0, (float)e[2]), FromJs((float)e[3], 0.06f, (float)e[4]), (int)e[5]); break;
                case "bl": ExplodeFx(FromJs((float)e[1], (float)e[2], (float)e[3]), (int)e[4] != 0); break;
                case "ar": { Ent en; if (byNid.TryGetValue((int)e[1], out en)) RespawnArrow(en, (int)e[2]); break; }
                case "sp": SpawnSplash(FromJs((float)e[1], (float)e[2], (float)e[3]), (int)e[4], (int)e[5]); break;
                case "pop": hud.Pop(FromJs((float)e[1], (float)e[2], (float)e[3]), (string)e[4], HexOf((string)e[5], 0xff3b30), (int)e[6] != 0); break;
                case "say": { int i = (int)e[1]; if (i >= 0 && i < players.Count) Say(players[i], (string)e[2], (float)e[3]); break; }
                case "fc": { int i = (int)e[1]; if (i >= 0 && i < players.Count) SetFace(players[i], (string)e[2], (float)e[3]); break; }
                case "dd":
                    if (!byNid.ContainsKey((int)e[1]))
                    {
                        int di = (int)e[2], oix = (int)e[3];
                        var dish = di >= 0 && di < Dishes.All.Count ? Dishes.All[di] : Dishes.All[0];
                        var ord = oix >= 0 && oix < orders.Count ? orders[oix] : null;
                        MakeDropped((int)e[1], dish, ord, FromJs((float)e[4], (float)e[5], (float)e[6]), (float)e[7]);
                    }
                    break;
                case "ddx": { var d = dropped.Find(x => x.ent != null && x.ent.nid == (int)e[1]); if (d != null) KillDropped(d); break; }
            }
        }

        /// <summary>
        /// **ホストが居なくなった。**ゲストは自分では物理を回さないので、便りが止まると画面がそのまま止まる。
        /// 黙って固まると「操作ができなくなった」としか分からないので、その場で止めて、何が起きたかを出してからタイトルへ戻る。
        /// </summary>
        public void NpLostHost(string why)
        {
            if (np.role == "off") return;
            np.role = "off";
            NetStop();
            hud.NetWarn("", false);
            hud.NetWait("ホストとの接続が切れました", why + "\nタイトルへ戻ります…");
            lostT = 2.2f;
        }
        float lostT;

        /// <summary>毎フレーム（Update から）。ホストは配り、ゲストは当てて入力を送る。</summary>
        void NetTick(float dt)
        {
            if (lostT > 0)
            {
                lostT -= Time.unscaledDeltaTime;
                if (lostT <= 0) { hud.NetWaitHide(); Net.LeaveRoom(); NpEnd(); BackToTitle(); }
                return;
            }
            if (!NpOn || stage == null) return;
            if (NetHost)
            {
                NpHeartbeat(Time.unscaledDeltaTime);
                if (!hud.ShopOpen && !hud.TitleOpen) NpHostTick(dt);
            }
            else if (NetGuest)
            {
                GuestAim(dt);
                NpSendInput(dt);
                NpApply(dt);
            }
        }
    }
}
