using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>実績1つ（JS版 ACHS の1行）。c … 仕分け、s … ★の数（1〜3。0 は枠だけ）</summary>
    public class AchDef
    {
        public string id, n, d; public int c, s; public bool tbd;
        public AchDef(string id, int c, int s, string n, string d, bool tbd = false) { this.id = id; this.c = c; this.s = s; this.n = n; this.d = d; this.tbd = tbd; }
    }

    // ================================================================ 実績（JS版 ACHS / ach / achDeliver / achItem / achHurt / achBuy / achStageEnd / achMatchEnd）
    // **遊んでいる人（キーボード・パッド）だけ**が取れる。NPC と、オンラインの相手の機体は数えない。
    // 1台で何人かで遊んだときは、誰が取っても**この端末の実績**になる。
    // 記録はこの端末（PlayerPrefs の nekorobo.ach。JS版の localStorage と同じ形）。
    // EOS（Epic Online Services）の実績にも同じ ID で送る（EosAch。鍵が無い・つながらないときはこの端末だけ）。
    // ★1 は取る前から中身が見え、★2・★3 は取るまで「？？？」。最後の1つ（tbd）は枠だけ。
    public partial class Game
    {
        public static readonly string[] ACH_CATS = { "はじめて", "配膳の腕", "お金", "アイテム", "ドタバタ", "ステージ", "やり込み" };
        public static readonly AchDef[] ACHS =
        {
            // はじめて
            new AchDef("deliver", 0, 1, "はじめての配膳", "料理を1つ届ける"),
            new AchDef("clear", 0, 1, "ラウンドクリア", "面を1つクリアする"),
            new AchDef("buy", 0, 1, "お買い物デビュー", "ショップで何か買う"),
            new AchDef("item", 0, 1, "アイテムを使ってみた", "アイテムを1回使う"),
            new AchDef("jumpout", 0, 1, "ジャンプ！", "池や川からジャンプして岸へ上がる"),
            new AchDef("vip", 0, 1, "VIPのおもてなし", "VIPに料理を届ける"),
            // 配膳の腕
            new AchDef("perfect", 1, 1, "崩さず運ぶ", "料理を崩さずに届ける"),
            new AchDef("pickup", 1, 1, "拾ってセーフ", "床に落ちた料理を拾う"),
            new AchDef("rush", 1, 1, "速達便", "速達ボーナスを付けて届ける"),
            new AchDef("combo5", 1, 2, "5連続", "続けて届けて、5連続にする"),
            new AchDef("combo10", 1, 3, "10連続", "続けて届けて、10連続にする"),
            new AchDef("rich", 1, 2, "高級店の信頼", "高額オーダーを届ける"),
            new AchDef("nodmg", 1, 2, "ノーダメージ", "機体のダメージ 0% のまま面をクリアする（注文6品以上の面）"),
            new AchDef("noshop", 1, 2, "お店を守った", "店舗損壊率 0% で面をクリアする（注文6品以上の面）"),
            new AchDef("flawless", 1, 3, "完璧な仕事", "届けた料理を1つも崩さずに、面を配りきる（注文6品以上の面）"),
            // お金
            new AchDef("net10k", 2, 2, "1面で1万円", "1面の差引を ¥10,000 以上にする"),
            new AchDef("net30k", 2, 3, "1面で3万円", "1面の差引を ¥30,000 以上にする"),
            new AchDef("cash50k", 2, 2, "小金持ち", "所持金を ¥50,000 以上にする"),
            new AchDef("upmax", 2, 2, "能力を最大まで", "能力強化のどれか1つを最大Lvにする"),
            new AchDef("upall", 2, 3, "フル強化", "能力強化を4つとも最大Lvにする"),
            // アイテム
            new AchDef("banana", 3, 1, "バナナの皮", "置いたバナナで相手を転ばせる"),
            new AchDef("steal", 3, 2, "横取り成功", "ブーメランで相手の料理を奪う"),
            new AchDef("hit", 3, 2, "命中！", "レーザーかミサイルを相手に当てる"),
            new AchDef("star", 3, 2, "無敵モード", "無敵の間に体当たりする"),
            new AchDef("allitems", 3, 3, "アイテム全種", "8種類のアイテムをすべて使う（通算）"),
            // ドタバタ
            new AchDef("sea", 4, 1, "おぼれるにゃ～", "海に落ちる"),
            new AchDef("lava", 4, 1, "あつい！", "溶岩に入る"),
            new AchDef("amb", 4, 1, "救急車", "お客さんを昇天させる"),
            new AchDef("traffic", 4, 1, "交通事故", "車か電車にぶつかる"),
            new AchDef("wreck", 4, 2, "大暴れ", "大暴れボーナスをもらう"),
            new AchDef("allamb", 4, 3, "全員昇天", "1つの面で、店内のお客さんを全員昇天させる"),
            // ステージ
            new AchDef("ice", 5, 1, "氷上の配膳", "凍りの床がある面をクリアする"),
            new AchDef("raft", 5, 2, "船出", "いかだに乗ったまま料理を届ける"),
            new AchDef("air", 5, 2, "空中配達", "跳んでいる間に料理を届ける"),
            // やり込み
            new AchDef("kaikin", 6, 1, "皆勤", "10ラウンドのラウンドマッチを最後まで遊ぶ"),
            new AchDef("story", 6, 2, "研修修了", "ストーリーを最後までクリアする"),
            new AchDef("storyblack", 6, 2, "黒字で修了", "ストーリーを黒字で終える"),
            new AchDef("podium", 6, 2, "表彰台のてっぺん", "2人以上のラウンドマッチで1位になる"),
            new AchDef("allstage", 6, 3, "全ステージ制覇", "フリープレイの面をすべて遊ぶ"),
            new AchDef("tbd", 6, 0, "？？", "準備中", true),
        };
        const string ACH_KEY = "nekorobo.ach";
        const int ACH_MIN_ORDERS = 6;              // 「ノーダメージ」「お店を守った」「完璧な仕事」が取れる面の注文数

        /// <summary>取った実績（id → 取った時刻[ミリ秒]）・使ったことのあるアイテム・遊んだ面。JS版 ACH</summary>
        public static class AchStore
        {
            public static readonly Dictionary<string, long> got = new Dictionary<string, long>();
            public static readonly List<string> items = new List<string>(), stages = new List<string>();
            static bool loaded;
            public static void Load()
            {
                if (loaded) return;
                loaded = true;
                try
                {
                    var s = PlayerPrefs.GetString(ACH_KEY, "");
                    if (s == "") return;
                    var v = JObject.Parse(s);
                    if (v["got"] is JObject g) foreach (var p in g) got[p.Key] = (long?)p.Value ?? 1;
                    if (v["items"] is JArray a) foreach (var x in a) items.Add((string)x);
                    if (v["stages"] is JArray b) foreach (var x in b) stages.Add((string)x);
                }
                catch (System.Exception e) { Debug.LogWarning("[実績] 記録が読めません: " + e.Message); }
            }
            public static void Save()
            {
                var g = new JObject(); foreach (var kv in got) g[kv.Key] = kv.Value;
                var v = new JObject { ["got"] = g, ["items"] = new JArray(items.ToArray()), ["stages"] = new JArray(stages.ToArray()) };
                PlayerPrefs.SetString(ACH_KEY, v.ToString(Newtonsoft.Json.Formatting.None)); PlayerPrefs.Save();
            }
            public static bool Has(string id) { Load(); return got.ContainsKey(id); }
            /// <summary>この端末の記録を消す（デバッグの「実績の記録を消す」）。EOS 側は消えない</summary>
            public static void Clear()
            {
                Load();
                got.Clear(); items.Clear(); stages.Clear();
                PlayerPrefs.DeleteKey(ACH_KEY); PlayerPrefs.Save();
                EosAch.Forget();
            }
        }

        public static AchDef AchOf(string id) { foreach (var a in ACHS) if (a.id == id) return a; return null; }
        /// <summary>取った数（例 3 / 39）。タイトルの「実績」の説明と、取ったときの知らせに出す</summary>
        public static string AchCount()
        {
            AchStore.Load();
            int n = 0, all = 0;
            foreach (var a in ACHS) { if (a.tbd) continue; all++; if (AchStore.got.ContainsKey(a.id)) n++; }
            return n + " / " + all;
        }
        public string AchCountText() { return AchCount(); }

        static bool AchMine(Player P) { return P != null && P.src != null && (P.src.kind == "key" || P.src.kind == "pad"); }

        /// <summary>取る。P は取った人（遊んでいる人でなければ何もしない）</summary>
        public void Ach(Player P, string id) { if (AchMine(P)) AchGive(id); }
        /// <summary>人を問わずに取る（通算の記録など。JS版 ach(true, id)）</summary>
        public void AchGive(string id)
        {
            AchStore.Load();
            if (AchStore.got.ContainsKey(id)) return;
            var a = AchOf(id);
            if (a == null || a.tbd) return;
            AchStore.got[id] = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AchStore.Save();
            EosAch.Unlock(id);
            if (hud != null) hud.ShowAch(a);
        }

        // 届けたとき（VIP も含む）。崩れていた物を1つでも届けたら、その面の「完璧な仕事」は無し
        void AchDeliver(Player P, Order o, float integ)
        {
            if (!AchMine(P)) return;
            Ach(P, "deliver");
            if (Dishes.PriceTier(integ) >= 1) Ach(P, "perfect"); else P.achFlaw = true;
            if (o != null && o.vip) Ach(P, "vip");
            if (o != null && o.rich) Ach(P, "rich");
        }
        // アイテムを使った。8種類とも使ったら（通算）
        void AchItem(Player P, string k)
        {
            if (!AchMine(P)) return;
            Ach(P, "item");
            if (!AchStore.items.Contains(k)) { AchStore.items.Add(k); AchStore.Save(); }
            bool all = true; foreach (var it in ITEMS) if (!AchStore.items.Contains(it.k)) all = false;
            if (all) Ach(P, "allitems");
        }
        // 客を昇天させた。店内の客が全員そうなったら「全員昇天」
        void AchHurt(Player P)
        {
            Ach(P, "amb");
            bool all = guests.Count > 0; foreach (var gu in guests) if (gu.hp > 50) all = false;
            if (all) Ach(P, "allamb");
        }
        // ショップで買った
        public void AchBuy(Player P, Wallet W)
        {
            Ach(P, "buy");
            bool any = false, every = true;
            foreach (var u in UPGRADES) { if (W.Up(u.k) >= u.max) any = true; else every = false; }
            if (any) Ach(P, "upmax");
            if (every) Ach(P, "upall");
        }
        /// <summary>面の記号（遊んだ面を控える）。フリープレイの一覧の並びと同じ書き方</summary>
        public static string AchStageKey(StageCfg c) { return c == null ? null : !string.IsNullOrEmpty(c.file) ? "file:" + c.file : "builtin:" + c.n; }
        // 面が終わったとき（Finish から。所持金とボーナスを入れたあと）
        void AchStageEnd(bool cleared)
        {
            var key = AchStageKey(stage);
            if (key != null && !AchStore.stages.Contains(key)) { AchStore.stages.Add(key); AchStore.Save(); }
            if (!players.Exists(AchMine)) return;
            var all = TitleWin.FreeKeys();
            if (all.Count > 0 && all.TrueForAll(k => AchStore.stages.Contains(k))) AchGive("allstage");
            bool ice = false; foreach (var ch in map.Values) if (Tiles.IsSlippery(ch)) { ice = true; break; }
            foreach (var P in players)
            {
                if (!AchMine(P)) continue;
                if (WalletOf(P).cash >= 50000) Ach(P, "cash50k");
                if (!cleared) continue;
                Ach(P, "clear");
                if (ice) Ach(P, "ice");
                var L = LedgerOf(P);
                if (L.total >= 10000) Ach(P, "net10k");
                if (L.total >= 30000) Ach(P, "net30k");
                // 面の出来で取る物は、注文が少ない面（ストーリーの始めのほう）では取れないようにする
                if (P.delivered > 0 && orders.Count >= ACH_MIN_ORDERS)
                {
                    if (P.achDmg < 0.5f) Ach(P, "nodmg");       // 結果画面で 0% と出る範囲
                    if (shopDmg < 0.5f) Ach(P, "noshop");
                    if (!timeUp && !P.achFlaw) Ach(P, "flawless");
                }
            }
        }
        // ラウンドマッチの最終結果（FinalWin から）
        public void AchMatchEnd()
        {
            var H = matchHist; var PL = players;
            if (matchRounds >= 10 && H.Count >= 10) foreach (var P in PL) Ach(P, "kaikin");
            if (PL.Count < 2) return;
            System.Func<int, float> sumOf = i => { float n = 0; foreach (var r in H) if (i < r.per.Count) n += r.per[i].tot; return n; };
            if (mode == "team")
            {
                var ts = TeamsInPlay(); var tot = new List<float>();
                foreach (var t in ts) tot.Add(sumOf(PL.IndexOf(TeamLead(t))));
                float best = float.MinValue; foreach (var v in tot) best = Mathf.Max(best, v);
                int nb = 0; foreach (var v in tot) if (v == best) nb++;
                if (nb == 1) { int win = ts[tot.IndexOf(best)]; foreach (var P in PL) if (P.team == win) Ach(P, "podium"); }
            }
            else if (mode == "versus")
            {
                float best = float.MinValue; for (int i = 0; i < PL.Count; i++) best = Mathf.Max(best, sumOf(i));
                for (int i = 0; i < PL.Count; i++) if (sumOf(i) == best) Ach(PL[i], "podium");
            }
        }
        // ストーリーを最後まで（エンディングの前に）
        void AchStoryEnd()
        {
            Ach(me, "story");
            if (WalletOf(me).cash >= 0) Ach(me, "storyblack");
        }
    }
}
