using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 自動マッチングの数字（JS版 MM_CFG と同じ）。いったんお任せで入れてあり、あとで調整する。
    /// </summary>
    public static class MmCfg
    {
        public const int BadMin = 5;            // 途中退出が多い人：自動マッチングの対戦がこの回数以上で、
        public const float BadRate = 0.3f;      //   途中退出がこの割合以上
        public const float AskSec = 15;         // ホストへの参加の確認。答えなければ断る
        public const float StartSec = 20;       // 2人そろったら、この秒数で始める（4人そろったらすぐ）
        public const float StrictSec = 30;      // 探し始めてこの秒数までは、同じ扱いの人の部屋だけを探す
        public const float MergeSec = 5;        // 自分の部屋に誰も来ない間、この秒ごとにほかの部屋を探し直す
        public static readonly int[] Lens = { 1, 3, 5, 10 };   // 選べる遊び方（1＝フリープレイ。1回だけ遊んで終わる）。探す前に選び、同じ遊び方の部屋どうしで組む
        public const int AnyLen = 3;            // 「こだわらない」で自分が部屋を作るときの遊び方
        public const string Mode = "versus";    // 個人戦・空き席は NPC。面は、そろったら全員で投票（STAGE SELECT）
        public const string Npc = "npc-normal";
    }

    /// <summary>
    /// 途中退出の数（JS版 QST と同じ決まり）。自動マッチングの対戦だけ数える（友達どうしのプライベートは数えない）。この端末に覚える。
    ///   M … 対戦数（始まったら1つ増やす）、Q … 途中退出、cur … いま対戦中の印
    /// 始まったら cur を立て、最後の結果まで遊んだら（またはホストが抜けて終わったら）消す。
    /// 「ゲームから抜ける」・部屋を出る・自分の通信が切れたら、その場で途中退出を1つ。
    /// 対戦中にゲームを閉じた・落ちたときは cur が残るので、次に起動したときに途中退出を1つ。
    /// HP が0で動けなくなる（ゲームの中のリタイア）は数えない。のちに EOS の Stats（足し算だけの統計）へ移す予定
    /// </summary>
    public static class EarlyQuit
    {
        const string KEY = "nekorobo.quit";
        public static int M, Q;
        static int cur;
        static bool loaded;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            var s = PlayerPrefs.GetString(KEY, "");
            var p = s.Split(',');
            if (p.Length == 3) { int.TryParse(p[0], out M); int.TryParse(p[1], out Q); int.TryParse(p[2], out cur); }
            if (cur != 0) { Q++; cur = 0; Save(); }          // 前回、対戦の途中で閉じた・落ちた
            Net.StatM = M; Net.StatQ = Q;
        }
        static void Save() { PlayerPrefs.SetString(KEY, M + "," + Q + "," + cur); PlayerPrefs.Save(); }

        /// <summary>起動したときに呼ぶ（前回の対戦中の印を数え、名乗る成績を入れる）。</summary>
        public static void Init() { Load(); }
        public static void Pub() { Load(); Net.SetStats(M, Q); }
        public static bool Bad { get { Load(); return IsBad(M, Q); } }
        public static bool IsBad(int m, int q) { return m >= MmCfg.BadMin && q / (float)Mathf.Max(1, m) >= MmCfg.BadRate; }
        public static bool IsBad(JToken st) { return st is JObject o && IsBad((int?)o["m"] ?? 0, (int?)o["q"] ?? 0); }
        public static void Begin() { Load(); M++; cur = 1; Save(); Pub(); }
        public static void Done() { Load(); if (cur != 0) { cur = 0; Save(); } }
        public static void Quitted() { Load(); if (cur != 0) { cur = 0; Q++; Save(); Pub(); } }
    }

    /// <summary>
    /// タイトルの自動マッチング（JS版 MM・TITLE_PAGE の Match / MmAsk / Private と同じ動き）。
    /// 探す → 入る（無ければ自分で作って待つ）→ 2人そろったら数えて始める。
    ///   phase … search（一覧を待っている）・join（入ろうとしている）・room（部屋に居る）
    ///   ホストは、途中退出の多い人が入ってきたら確認を出し（自分も多い人なら出さない）、断ったら外す
    /// </summary>
    public partial class TitleWin
    {
        class MmState
        {
            public bool on, listing;
            public string phase = "";
            public float t0, listAt, nextList, askAt, startAt, bcT;
            public JArray rooms = new JArray();
            public HashSet<string> deny = new HashSet<string>();
            public HashSet<int> ok = new HashSet<int>(), asked = new HashSet<int>();
            public int ask = -1, left = -1, drawSec = -1, len;   // len … 選んだ遊び方（1・3・5・10。0 はこだわらない）
            public bool pendMe;
        }
        readonly MmState mm = new MmState();
        static float Now { get { return Time.realtimeSinceStartup; } }
        static int MmTier { get { return EarlyQuit.Bad ? 1 : 0; } }
        static int MmN(JToken r) { return r["n"] != null ? (int)r["n"] : (r["players"] is JArray a ? a.Count : 0); }

        void MmStart() { MmStart(-1); }
        /// <summary>len … 選んだ遊び方（-1 なら前のまま）。</summary>
        void MmStart(int len)
        {
            if (len >= 0) mm.len = len;
            Net.Connect();
            mm.on = true; mm.phase = "search"; mm.t0 = Now; mm.rooms = new JArray(); mm.listing = false; mm.nextList = 0;
            mm.deny.Clear(); mm.ok.Clear(); mm.asked.Clear(); mm.ask = -1; mm.startAt = 0; mm.left = -1; mm.pendMe = false; mm.bcT = 0;
            EarlyQuit.Pub();
            MmList();
            Go("Match");
        }

        void MmStop()
        {
            if (!mm.on) return;
            mm.on = false; mm.phase = ""; mm.ask = -1;
            if (Net.Room != null) Net.LeaveRoom();
        }

        /// <summary>一覧を頼んで、少し待って集めてから決める（JS版と同じ。EOS は1回で全部届く）。</summary>
        void MmList()
        {
            if (mm.listing || Net.State != "on") return;
            mm.rooms = new JArray(); mm.listing = true; mm.listAt = Now + 0.6f;
            Net.ListRooms(true);
        }

        void MmDecide()
        {
            mm.listing = false;
            if (!mm.on) return;
            bool strict = Now - mm.t0 < MmCfg.StrictSec;
            string mine = Net.Room != null ? (string)Net.Room["code"] : null;
            var seen = new HashSet<string>();
            var c = new List<JToken>();
            foreach (var r in mm.rooms)
            {
                string code = (string)r["code"];
                if (code == null || !seen.Add(code) || code == mine || mm.deny.Contains(code)) continue;
                int n = MmN(r), max = (int?)r["max"] ?? 4, tier = (int?)r["tier"] ?? 0;
                // ふつうの人は、途中退出の多い人の部屋には入らない。途中退出の多い人は、はじめは同じような人の部屋だけ、
                // しばらくしたらふつうの人の部屋にも入りに行く（そこでホストの確認を受ける）
                bool tierOk = tier == MmTier || (MmTier == 1 && !strict);
                bool lenOk = mm.len == 0 || ((int?)r["len"] ?? 0) == mm.len;     // 同じ遊び方の部屋だけ（こだわらないなら全部）
                if (n > 0 && n < max && tierOk && lenOk) c.Add(r);
            }
            c.Sort((a, b) =>
            {
                int d = ((int?)a["tier"] ?? 0) - ((int?)b["tier"] ?? 0);
                if (d != 0) return d;
                d = MmN(b) - MmN(a);
                return d != 0 ? d : string.CompareOrdinal((string)a["code"], (string)b["code"]);
            });
            if (mm.phase == "search")
            {
                if (c.Count > 0) { mm.phase = "join"; Net.JoinRoom((string)c[0]["code"]); }
                else { mm.phase = "room"; Net.CreateRoom("オンラインマッチ", false, true, MmTier, mm.len > 0 ? mm.len : MmCfg.AnyLen); }
                return;
            }
            // 自分の部屋に誰も来ない間：ほかに人の多い部屋（同じ人数なら番号の小さい部屋）があれば移る（同時に作った部屋をまとめる）。
            // 途中退出の多い人は、ふつうの人の部屋が見つかればそちらへ（ふつうの人は来ないので）
            var pl = Net.Players;
            if (mm.phase == "room" && pl != null && pl.Count == 1)
                foreach (var r in c)
                    if (MmN(r) > 1 || string.CompareOrdinal((string)r["code"], mine) < 0 || (MmTier == 1 && ((int?)r["tier"] ?? 0) == 0))
                    { Net.LeaveRoom(); mm.phase = "join"; Net.JoinRoom((string)r["code"]); break; }
        }

        /// <summary>部屋が変わった（入った・出た・人が増えた減った・外された）。</summary>
        void MmRoom()
        {
            if (!mm.on) return;
            if (Net.Room != null)
            {
                if ((bool?)Net.Room["auto"] != true) return;
                mm.phase = "room";
                if (!Net.IsHost) mm.startAt = 0;
                return;
            }
            // 部屋から出た（入れなかった・外された・ホストが居なくなった）→ 探し直す
            if (mm.phase == "room" || mm.phase == "join")
            {
                mm.phase = "search"; mm.ok.Clear(); mm.asked.Clear(); mm.ask = -1; mm.startAt = 0; mm.left = -1;
                mm.nextList = Now + 0.4f;
            }
        }

        /// <summary>毎フレーム（Tick から）。</summary>
        void MmTick()
        {
            if (!mm.on) return;
            float now = Now;
            if (mm.listing && now >= mm.listAt) MmDecide();
            var r = Net.Room;
            if (Net.State == "on" && mm.phase == "search" && !mm.listing && now > mm.nextList) { mm.nextList = now + 2; MmList(); }
            if (mm.phase == "room" && r != null && (bool?)r["auto"] == true)
            {
                var pl = Net.Players;
                if (pl != null && pl.Count == 1 && now > mm.nextList) { mm.nextList = now + MmCfg.MergeSec; MmList(); }
                if (Net.IsHost) MmHostTick(now, r);
            }
            // 画面の秒数を進める（1秒ごと）
            if (Open && !SS.open && (page == "Match" || page == "MmAsk"))
            {
                int sec = (int)now;
                if (sec != mm.drawSec) { mm.drawSec = sec; GoLater(mm.ask >= 0 && Net.IsHost ? "MmAsk" : "Match"); }
            }
        }

        void MmHostTick(float now, JObject r)
        {
            var others = new List<JObject>();
            foreach (JObject p in (JArray)r["players"]) if (((int?)p["id"] ?? -1) != Net.Id) others.Add(p);
            // 入ってきた人を確かめる。途中退出の多い人は確認を出す（自分も多い人なら、そのまま入れる）
            foreach (var p in others)
            {
                int id = (int?)p["id"] ?? -1;
                if (mm.ok.Contains(id) || mm.asked.Contains(id)) continue;
                if (EarlyQuit.IsBad(p["st"]) && !EarlyQuit.Bad)
                {
                    if (mm.ask < 0) { mm.ask = id; mm.askAt = now; mm.asked.Add(id); Game.SeUi("talk_open"); GoLater("MmAsk"); }
                }
                else mm.ok.Add(id);
            }
            if (mm.ask >= 0)
            {
                if (!others.Exists(p => ((int?)p["id"] ?? -1) == mm.ask)) { mm.ask = -1; GoLater("Match"); }
                else if (now - mm.askAt > MmCfg.AskSec) MmAnswer(false);
            }
            var pend = new JArray();
            foreach (var p in others) { int id = (int?)p["id"] ?? -1; if (!mm.ok.Contains(id)) pend.Add(id); }
            int ready = 1 + others.Count - pend.Count, max = (int?)r["max"] ?? 4;
            if (ready >= 2) { if (mm.startAt <= 0) mm.startAt = now + MmCfg.StartSec; }
            else mm.startAt = 0;
            if (ready >= max && pend.Count == 0) mm.startAt = Mathf.Min(mm.startAt, now + 1.5f);   // 4人そろったらすぐ
            mm.left = mm.startAt > 0 ? Mathf.Max(0, Mathf.CeilToInt(mm.startAt - now)) : -1;
            if (now > mm.bcT) { mm.bcT = now + 1; Net.Send(new JObject { ["p"] = "mm", ["left"] = mm.left, ["pend"] = pend }); }
            if (mm.startAt > 0 && now >= mm.startAt && mm.ask < 0 && pend.Count == 0) MmGo(r);
        }

        /// <summary>ホストの答え。断ったら部屋から外す（外された人は、ほかの部屋を探し直す）。</summary>
        void MmAnswer(bool ok)
        {
            int id = mm.ask;
            mm.ask = -1;
            if (id >= 0) { if (ok) mm.ok.Add(id); else Net.Kick(id); }
            if (Open) GoLater("Match");
        }

        /// <summary>
        /// ホスト：人がそろった。部屋を締め切って（もう入れない）、面を全員で投票する（STAGE SELECT）。決まったら始まる（SsGo）。
        /// 遊び方は部屋の遊び方（len）、個人戦・空き席は NPC
        /// </summary>
        void MmGo(JObject r)
        {
            int n = ((JArray)r["players"]).Count, ln = (int?)r["len"] ?? 0;
            Lobby.mode = MmCfg.Mode; Lobby.rounds = ln > 0 ? ln : MmCfg.AnyLen;
            for (int i = 0; i < 4; i++) { Lobby.rteam[i] = i % 2; Lobby.rnpc[i] = i < n ? "off" : MmCfg.Npc; }
            Net.LockRoom();
            Lobby.SendRule(); Lobby.SendNpc();
            mm.on = false; mm.phase = "";
            from = "Wait"; next = false;
            Go("Free");                                  // 面選び（部屋の全員で投票。参加者の画面にも開く）
        }

        /// <summary>始まったとき（Game.NetStart から）。自動マッチングは終わり。</summary>
        public void MmOff() { mm.on = false; mm.phase = ""; mm.ask = -1; }
        public bool MmOn { get { return mm.on; } }
        /// <summary>最終結果の「もう一度さがす」。</summary>
        public void MmAgain() { MmStart(); }

        // ---------------------------------------------------------------- 通信のできごと（TitleWin.NetHook から）
        bool MmOnRoom()
        {
            if (!mm.on) return false;
            MmRoom();
            if (Open && page != "MmAsk") GoLater("Match");
            return true;
        }
        bool MmOnRooms(JArray a)
        {
            if (!mm.listing) return false;
            foreach (var r in a) mm.rooms.Add(r);
            return true;
        }
        bool MmOnErr()
        {
            // 入れなかった（いっぱい・始まった）→ 探し直す
            if (!mm.on || mm.phase != "join") return false;
            mm.phase = "search"; mm.nextList = Now + 0.4f;
            return true;
        }
        bool MmOnFrom(JObject d)
        {
            if ((string)d["p"] != "mm") return false;
            if (!mm.on || Net.IsHost) return true;
            mm.left = (int?)d["left"] ?? -1;
            mm.pendMe = false;
            if (d["pend"] is JArray pa) foreach (var x in pa) if (((int?)x ?? -2) == Net.Id) mm.pendMe = true;
            return true;
        }
        void MmOnKicked(string code) { if (mm.on && !string.IsNullOrEmpty(code)) mm.deny.Add(code); }

        // ---------------------------------------------------------------- 画面
        static string Mmss(float s) { int t = Mathf.Max(0, (int)s); return t / 60 + ":" + (t % 60).ToString("00"); }

        /// <summary>自動マッチング。探している間は経過時間、2人そろったら開始までの残り秒数を大きく出す。</summary>
        void PageMatch(Stack st)
        {
            var r = Net.Room;
            Head(st, "オンラインマッチ");
            string line, big = "";
            if (Net.State != "on") line = "つないでいます…";
            else if (r == null || mm.phase != "room") { line = "対戦相手を探しています…"; big = Mmss(Now - mm.t0); }
            else if (!Net.IsHost && mm.pendMe) line = "ホストの確認を待っています…";
            else if (mm.left >= 0) { line = "まもなく始まります"; big = mm.left.ToString(); }
            else { line = "対戦相手を探しています…"; big = Mmss(Now - mm.t0); }
            Note(st, "<b>" + line + "</b>");
            if (big.Length > 0)
            {
                var cc = st.Card(48);
                var ct = UiKit.Label(cc, big, 28, Mats.Hex(0x3a352c), true, TextAnchor.MiddleCenter);
                ct.font = Mono;
                UiKit.Stretch(ct.rectTransform);
            }
            if (r != null && mm.phase == "room")
                Note(st, "いま <b>" + Net.Players.Count + " / " + Net.Max + "人</b>。そろわない席は NPC が入ります。");
            int ln = r != null && mm.phase == "room" ? (int?)r["len"] ?? 0 : mm.len;
            Note(st, "個人戦・" + (ln > 0 ? Game.LenName(ln) : "遊び方はこだわらない") + "。面はそろってから全員で投票します。");
            if (EarlyQuit.Bad) Note(st, "<b>途中退出が多いため</b>、相手が見つかりにくくなっています（" + EarlyQuit.M + "回中" + EarlyQuit.Q + "回）。");
            Btn(st, "やめる", "sec", () => { MmStop(); Go("MatchLen"); }, null, true);
        }

        /// <summary>自動マッチング：遊び方を選ぶ（同じ遊び方を選んだ人どうしで組む）。面はそろってから全員で投票。</summary>
        string PageMatchLen(Stack st)
        {
            bool on = Net.State == "on";
            Head(st, "遊び方を選ぶ");
            Note(st, "個人戦です。面は、そろってから<b>全員で投票</b>して決めます。");
            var col = new Dictionary<int, string> { [1] = "blue", [3] = "green", [5] = "red", [10] = "gold" };
            var ts = new List<TileSpec>();
            foreach (var n in MmCfg.Lens)
            {
                int nn = n;
                ts.Add(Tile(n == 1 ? "フリープレイ" : n + "ラウンド", col.ContainsKey(n) ? col[n] : "blue", n == 1 ? "free" : n.ToString(), () => MmStart(nn),
                            n == 1 ? "1回だけ遊んで終わります。" : n + "ラウンド遊んで、最後に最終結果を出して終わります。", "l" + n, !on));
            }
            Tiles(st, ts.ToArray());
            Btn(st, "こだわらない", "sec", () => MmStart(0), "どの遊び方の部屋にも入ります（相手が見つかりやすくなります）。", false, "l0");
            Btn(st, "戻る", "sec", () => Go("Online"), null, true);
            return mm.len > 0 ? "l" + mm.len : null;
        }

        /// <summary>ホスト：途中退出の多い人が入ってきた。許すか（答えなければ断る）。はじめは「断る」を選んでおく。</summary>
        string PageMmAsk(Stack st)
        {
            JObject p = null;
            var pl = Net.Players;
            if (pl != null) foreach (JObject x in pl) if (((int?)x["id"] ?? -1) == mm.ask) p = x;
            if (p == null) { mm.ask = -1; PageMatch(st); return null; }
            int left = Mathf.Max(0, Mathf.CeilToInt(MmCfg.AskSec - (Now - mm.askAt)));
            var s = p["st"] as JObject;
            int m = s != null ? (int?)s["m"] ?? 0 : 0, q = s != null ? (int?)s["q"] ?? 0 : 0;
            Head(st, "参加の確認");
            Note(st, "<b>途中退出の多いプレイヤー</b>が参加しようとしています（" + m + "回中" + q + "回 途中退出）。\n参加を許可しますか？");
            Note(st, "<b>" + left + "秒</b>たつと断ります。");
            BtnRow(st, new[] { "許可する", "断る" }, new[] { "go", "sec" }, new System.Action[] { () => MmAnswer(true), () => MmAnswer(false) }, new[] { false, false });
            return "back";
        }

        /// <summary>プライベート：部屋を作るか、番号で入る（友達どうし。一覧には出さない）。</summary>
        void PagePrivate(Stack st)
        {
            bool on = Net.State == "on";
            Head(st, "プライベート");
            Note(st, NetLine());
            Tiles(st,
                Tile("ルームを作る", "green", "make", () => { Lobby.ruleTo = "Make"; Go("Rule"); },
                     "あなたがホストになります。ルールと面を選ぶのもホストです。ルームIDを友達に伝えてください。", "make", !on),
                Tile("ルームに参加", "blue", "join", () => { Net.Why = ""; Go("Join"); }, "友達から聞いたルームIDを入れて入ります。", "join", !on));
            Btn(st, "戻る", "sec", () => Go("Online"), null, true);
        }
    }
}
