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
        public const float StartSec = 20;       // 2人そろったら、この秒数で始める（4人そろったらすぐ）
        public const float MergeSec = 5;        // 自分の部屋に誰も来ない間、この秒ごとにほかの部屋を探し直す
        public static readonly int[] Lens = { 1, 3, 5, 10 };   // 選べる遊び方（1＝フリープレイ。1回だけ遊んで終わる）。探す前に選び、同じ遊び方の部屋どうしで組む
        public const int AnyLen = 3;            // 「こだわらない」で自分が部屋を作るときの遊び方
        public const string Mode = "versus";    // 個人戦・空き席は NPC。面は、そろったら全員で投票（STAGE SELECT）
        public const string Npc = "npc-normal";
    }

    /// <summary>
    /// タイトルの自動マッチング（JS版 MM・TITLE_PAGE の Match / Private と同じ動き）。
    /// 探す → 入る（無ければ自分で作って待つ）→ 2人そろったら数えて始める。
    ///   phase … search（一覧を待っている）・join（入ろうとしている）・room（部屋に居る）
    /// 途中退出を数えて、多い人をマッチングしにくくする・ホストに参加の確認を出す仕組みもあったが、
    /// 担当の判断でやめた（2026-11-17）。前の端末に残っている数（PlayerPrefs の nekorobo.quit）は消す
    /// </summary>
    public partial class TitleWin
    {
        class MmState
        {
            public bool on, listing;
            public string phase = "";
            public float t0, listAt, nextList, startAt, bcT;
            public JArray rooms = new JArray();
            public int left = -1, drawSec = -1, len;   // len … 選んだ遊び方（1・3・5・10。0 はこだわらない）
        }
        readonly MmState mm = new MmState();
        static float Now { get { return Time.realtimeSinceStartup; } }
        /// <summary>前の版が覚えていた途中退出の数を消す（起動したときに1回）。</summary>
        static void MmClearOld() { if (PlayerPrefs.HasKey("nekorobo.quit")) { PlayerPrefs.DeleteKey("nekorobo.quit"); PlayerPrefs.Save(); } }
        static int MmN(JToken r) { return r["n"] != null ? (int)r["n"] : (r["players"] is JArray a ? a.Count : 0); }

        void MmStart() { MmStart(-1); }
        /// <summary>len … 選んだ遊び方（-1 なら前のまま）。</summary>
        void MmStart(int len)
        {
            if (len >= 0) mm.len = len;
            Net.Connect();
            mm.on = true; mm.phase = "search"; mm.t0 = Now; mm.rooms = new JArray(); mm.listing = false; mm.nextList = 0;
            mm.startAt = 0; mm.left = -1; mm.bcT = 0;
            MmList();
            Go("Match");
        }

        void MmStop()
        {
            if (!mm.on) return;
            mm.on = false; mm.phase = "";
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
            string mine = Net.Room != null ? (string)Net.Room["code"] : null;
            var seen = new HashSet<string>();
            var c = new List<JToken>();
            foreach (var r in mm.rooms)
            {
                string code = (string)r["code"];
                if (code == null || !seen.Add(code) || code == mine) continue;
                int n = MmN(r), max = (int?)r["max"] ?? 4;
                bool lenOk = mm.len == 0 || ((int?)r["len"] ?? 0) == mm.len;     // 同じ遊び方の部屋だけ（こだわらないなら全部）
                if (n > 0 && n < max && lenOk) c.Add(r);
            }
            c.Sort((a, b) =>
            {
                int d = MmN(b) - MmN(a);
                return d != 0 ? d : string.CompareOrdinal((string)a["code"], (string)b["code"]);
            });
            if (mm.phase == "search")
            {
                if (c.Count > 0) { mm.phase = "join"; Net.JoinRoom((string)c[0]["code"]); }
                else { mm.phase = "room"; Net.CreateRoom("オンラインマッチ", false, true, mm.len > 0 ? mm.len : MmCfg.AnyLen); }
                return;
            }
            // 自分の部屋に誰も来ない間：ほかに人の多い部屋（同じ人数なら番号の小さい部屋）があれば移る（同時に作った部屋をまとめる）
            var pl = Net.Players;
            if (mm.phase == "room" && pl != null && pl.Count == 1)
                foreach (var r in c)
                    if (MmN(r) > 1 || string.CompareOrdinal((string)r["code"], mine) < 0)
                    { Net.LeaveRoom(); mm.phase = "join"; Net.JoinRoom((string)r["code"]); break; }
        }

        /// <summary>部屋が変わった（入った・出た・人が増えた減った）。</summary>
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
            // 部屋から出た（入れなかった・ホストが居なくなった）→ 探し直す
            if (mm.phase == "room" || mm.phase == "join")
            {
                mm.phase = "search"; mm.startAt = 0; mm.left = -1;
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
            if (Open && !SS.open && page == "Match")
            {
                int sec = (int)now;
                if (sec != mm.drawSec) { mm.drawSec = sec; GoLater("Match"); }
            }
        }

        void MmHostTick(float now, JObject r)
        {
            int n = ((JArray)r["players"]).Count, max = (int?)r["max"] ?? 4;
            if (n >= 2) { if (mm.startAt <= 0) mm.startAt = now + MmCfg.StartSec; }
            else mm.startAt = 0;
            if (n >= max) mm.startAt = Mathf.Min(mm.startAt, now + 1.5f);   // 4人そろったらすぐ
            mm.left = mm.startAt > 0 ? Mathf.Max(0, Mathf.CeilToInt(mm.startAt - now)) : -1;
            if (now > mm.bcT) { mm.bcT = now + 1; Net.Send(new JObject { ["p"] = "mm", ["left"] = mm.left }); }
            if (mm.startAt > 0 && now >= mm.startAt) MmGo(r);
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
        public void MmOff() { mm.on = false; mm.phase = ""; }
        public bool MmOn { get { return mm.on; } }
        /// <summary>最終結果の「もう一度さがす」。</summary>
        public void MmAgain() { MmStart(); }

        // ---------------------------------------------------------------- 通信のできごと（TitleWin.NetHook から）
        bool MmOnRoom()
        {
            if (!mm.on) return false;
            MmRoom();
            if (Open) GoLater("Match");
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
            return true;
        }

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
