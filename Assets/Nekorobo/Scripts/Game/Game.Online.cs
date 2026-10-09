using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 部屋の待機画面の状態（JS版 TITLE の rpick / rnpc / rteam / rooms / code など）。
    /// 面・空き席のNPC・ルールとチームを決めるのはホストだけで、決めたそばから部屋の全員へ配る。
    /// </summary>
    public static class Lobby
    {
        public static string rname = "";                       // 作る部屋の名前（空ならサーバが「へや」）
        public static bool rpriv;                              // 一覧に出さない
        public static JObject rpick;                           // 部屋で遊ぶ面 { ref, name, shop, shopName } か { random:true, name }
        public static string[] rnpc = { "off", "off", "off", "off" };
        public static int[] rteam = { 0, 1, 0, 1 };
        public static string mode = "versus";
        public static JArray rooms = new JArray();             // 開いている部屋の一覧
        public static string code = "";                        // 番号で入るときの番号
        public static string ruleTo = "Local";                 // ルールを選んだあとの行き先（Local / Make）

        public static void SendPick() { if (Net.IsHost && rpick != null) Net.Send(new JObject { ["p"] = "pick", ["pick"] = rpick }); }
        public static void SendNpc() { if (Net.IsHost) Net.Send(new JObject { ["p"] = "npc", ["npc"] = new JArray(rnpc) }); }
        public static void SendRule() { if (Net.IsHost) Net.Send(new JObject { ["p"] = "rule", ["mode"] = mode, ["team"] = new JArray(rteam) }); }
    }

    /// <summary>
    /// オンラインの同期（JS版 NP）。**ホストが全部計算して、結果を配る**方式。
    ///   ホスト … いままでどおり物理を回す。他の人のロボは「届いた入力」で動かす（枠の種類 "net"）
    ///   ゲスト … 物理を回さない。自分の入力を送り、届いた位置を当てて描くだけ
    /// 各自が同じ計算をする方式にしないのは、物理がカオスなこのゲームだと、機械の違いで結果がズレて数秒で別世界になるため。
    /// </summary>
    public partial class Game
    {
        public class NetPlay
        {
            public string role = "off";        // off / host / guest
            public int me;                     // 自分が何番目のロボか
            public List<int> ids = new List<int>();   // 枠の順番 → サーバの番号
            public int stage;                  // オンラインで何面目か（便りの gn。前の面の便りを捨てるのに使う）
            public Dictionary<int, bool> shopped = new Dictionary<int, bool>();
            public Dictionary<int, bool> rdy = new Dictionary<int, bool>();
            public bool waitHost, waitShop;
        }
        [System.NonSerialized] public NetPlay np = new NetPlay();
        public bool NpOn { get { return np.role != "off"; } }
        /// <summary>続けて遊んでいる最中か（2面目以降）。この間に抜けると、みんなの続きが終わる。</summary>
        public bool InSession { get { return NpOn && np.stage > 0; } }
        static readonly string[] PNAME = { "1P", "2P", "3P", "4P" };

        /// <summary>部屋の人を席の番号（1P・2P…）で呼ぶ（名乗る名前はやめた）。</summary>
        public string SeatName(int id) { int i = np.ids.IndexOf(id); return i >= 0 ? (i < 4 ? PNAME[i] : "P" + (i + 1)) : "？"; }

        /// <summary>オンラインの相手の入力（届いたもの）。ゲーム中の同期はこの次の段で入れる。</summary>
        BotInput NetInputOf(Player P) { return new BotInput(); }

        void NetHook()
        {
            Net.OnStart += NetStart;
            Net.OnState += () =>
            {
                if (Net.State != "on") NpEnd();          // 切れたら同期もやめる
            };
        }

        /// <summary>部屋の並びから枠を作る。**両方で同じ順番**にしないと、誰がどのロボか食い違う（JS版 npBegin）。</summary>
        void NpBegin(JArray members, JArray npc, JObject cfg)
        {
            np.ids.Clear();
            foreach (var m in members) np.ids.Add((int?)m["id"] ?? -1);
            np.me = Mathf.Max(0, np.ids.IndexOf(Net.Id));
            np.role = Net.IsHost ? "host" : "guest";
            // 自分の枠だけキーボード、他はネット越し。チームは席の番号で決まる（ホストが配った team）
            var tm = cfg != null && cfg["team"] is JArray ta ? ta : new JArray(0, 1, 0, 1);
            int Tm(int i) { return i < tm.Count && ((int?)tm[i] ?? 0) != 0 ? 1 : 0; }
            var sl = new List<PlayerSrc>();
            for (int i = 0; i < np.ids.Count; i++)
                sl.Add(i == np.me ? new PlayerSrc { kind = "key", team = Tm(i) }
                                  : new PlayerSrc { kind = "net", netId = np.ids[i], team = Tm(i) });
            // 人が集まらないときは、空いた席をNPCで埋める（ホストが決めて配るので、どの画面でも同じ並び）
            if (npc != null)
                for (int i = sl.Count; i < 4; i++)
                {
                    var got = PlayerSrc.Parse(i < npc.Count ? (string)npc[i] ?? "off" : "off");
                    if (got.Count == 0 || got[0].kind != "npc") continue;
                    got[0].team = Tm(i);
                    sl.Add(got[0]);
                }
            slots = sl;
            // ルールはホストが選んだもの（古い版のホストは送ってこないので個人戦）
            string md = cfg != null ? (string)cfg["mode"] : null;
            mode = md == "versus" || md == "coop" || md == "team" ? md : "versus";
            Lobby.mode = mode;
        }

        public void NpEnd()
        {
            np.role = "off"; np.shopped.Clear(); np.rdy.Clear();
            np.waitHost = false; np.waitShop = false; np.stage = 0;
        }

        /// <summary>
        /// ホストが始めたら、みんなの画面でも始める（JS版 NETMOD.on("start")）。
        /// 2面目以降は、枠と財布をそのまま使う（お金・アイテム・強化を持ち越す）。続きかどうかはホストが教える。
        /// </summary>
        void NetStart(JObject m)
        {
            var room = m["room"] as JObject ?? Net.Room;
            var cfg = m["cfg"] as JObject;
            var pick = cfg != null && cfg["pick"] is JObject pj ? pj : Lobby.rpick;
            bool again = NpOn && cfg != null && (bool?)cfg["cont"] == true;
            // 何面目かはホストの数字をそのまま使う（便りを捨てるかどうかの判断に使う番号）
            if (cfg != null && cfg["gn"] != null) np.stage = (int)cfg["gn"];
            else if (again) np.stage = Mathf.Max(1, np.stage);
            // 財布はホストのものに揃える。**ホストが正**（買ったものは全員ぶん集めてある）
            if (again && cfg["w"] is JArray wa)
                for (int i = 0; i < wa.Count && i < wallets.Length; i++) WalletFromJson(wallets[i], wa[i] as JObject);
            if (!again && room != null) NpBegin(room["players"] as JArray ?? new JArray(), cfg != null ? cfg["npc"] as JArray : null, cfg);
            np.shopped.Clear(); np.rdy.Clear(); np.waitHost = false; np.waitShop = false;
            // 面。ランダムはホストが引いて、決まった面を配ってある（ref）
            string rf = pick != null ? (string)pick["ref"] : null;
            var c = rf != null ? StageCfg.Ref(rf, true) : null;
            if (c == null) { hud.Toast("部屋の面が見つかりません（" + (rf ?? "なし") + "）。stages をそろえてください"); return; }
            int si = pick["shop"] != null && pick["shop"].Type == JTokenType.Integer ? (int)pick["shop"] : 0;
            string shopN = si >= 0 && si < Shops.All.Count ? Shops.All[si].n : Shops.All[0].n;
            hud.CloseForGame();
            StartFree(c, shopN, slots, mode, again);
        }

        // ---------------------------------------------------------------- 財布を JSON で（start の w・買い物の便り）
        public static JObject WalletJson(Wallet W)
        {
            return new JObject
            {
                ["cash"] = W.cash,
                ["up"] = JObject.FromObject(W.up), ["items"] = JObject.FromObject(W.items),
                ["ammo"] = JObject.FromObject(W.ammo), ["bought"] = JObject.FromObject(W.bought),
            };
        }
        public static void WalletFromJson(Wallet W, JObject v)
        {
            if (W == null || v == null) return;
            if (v["cash"] != null) W.cash = (float)v["cash"];
            void Copy(Dictionary<string, int> d, JToken t) { if (t is JObject o) foreach (var p in o) d[p.Key] = (int?)p.Value ?? 0; }
            Copy(W.up, v["up"]); Copy(W.items, v["items"]); Copy(W.ammo, v["ammo"]); Copy(W.bought, v["bought"]);
        }

        /// <summary>start で配る財布（全員ぶん）。</summary>
        public JArray WalletsJson()
        {
            var a = new JArray();
            foreach (var W in wallets) a.Add(WalletJson(W));
            return a;
        }

        /// <summary>何面目か、と、ステージボーナスまであと何面か（JS版 roundLine）。</summary>
        public string RoundLine()
        {
            int next = runDone + 1;
            string t = "<b>" + next + "面目</b>";
            if (T.bonusEvery > 0 && T.stageBonus > 0)
            {
                int left = Mathf.RoundToInt(T.bonusEvery) - (runDone % Mathf.Max(1, Mathf.RoundToInt(T.bonusEvery)));
                float lo = Mathf.Round(T.stageBonus * (1 - T.bonusSpread)), hi = Mathf.Round(T.stageBonus * (1 + T.bonusSpread));
                string amt = Lobby.mode != "coop" && T.bonusSpread > 0 ? Yen(lo) + "〜" + Yen(hi) + "・順位で変わります" : Yen(T.stageBonus);
                t += left == 1 ? "　🎁 <b>この面を終えるとステージボーナス</b> " + amt
                               : "　あと <b>" + left + "面</b> でステージボーナス（" + amt + "）";
            }
            return t;
        }
    }
}
