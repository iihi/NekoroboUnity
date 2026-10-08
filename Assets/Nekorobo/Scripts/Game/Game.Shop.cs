using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// ショップと面の送り（JS版 afterResult / openShop / shopDone / nextStage / buyUpgrade / buyItem / npcShop）。
    /// 画面は ShopWin。
    ///
    /// 流れ：結果 →（次へ）→ ショップ →（全員が購入完了・時間切れ）→ 次の面
    ///   ストーリーで失敗した面はもう一度、ショップの無い面（noShop）はそのまま次の面へ。
    ///   面を直接選んで遊んだとき（フリープレイ）は、買い物のあと面を選ぶ。
    /// </summary>
    public partial class Game
    {
        /// <summary>このステージのあとでショップへ寄ったか（寄らずに次へ進むときだけ、NPC に裏で買わせる）。</summary>
        [System.NonSerialized] public bool shopped;

        // ================================================================ 値段と買う
        public static UpgradeDef UpgradeOf(string k) { foreach (var u in UPGRADES) if (u.k == k) return u; return null; }

        /// <summary>強化の値段。買うほど上がる（JS版 upCost）。</summary>
        public static int UpCost(UpgradeDef u, Wallet W) { return Mathf.RoundToInt(u.bas * Mathf.Pow(u.rate > 0 ? u.rate : 1.6f, W.Up(u.k))); }
        /// <summary>アイテムは固定額（買うたびに上がる形だと、持っている人が勝ち続けた）。</summary>
        public static int ItemCost(ItemDef it) { return Mathf.RoundToInt(it.price); }
        /// <summary>もう持てないか。パック式は「持っていて、弾が満タン」のとき。</summary>
        public static bool ItemFull(ItemDef it, Wallet W)
        {
            return it.ammo > 0 ? (W.Has(it.k) > 0 && W.ammo[it.k] >= it.ammo) : W.Has(it.k) >= it.max;
        }

        /// <summary>持っているぶんでしか買えない（借金はやめた）。</summary>
        public void BuyUpgrade(string k, Wallet W)
        {
            var u = UpgradeOf(k);
            if (u == null || W.Up(k) >= u.max) return;
            int cost = UpCost(u, W);
            if (W.cash < cost) return;
            W.cash -= cost;
            W.up[k] = W.Up(k) + 1;
        }

        public void BuyItem(string k, Wallet W)
        {
            var it = ItemOf(k);
            if (it == null || ItemFull(it, W)) return;
            int cost = ItemCost(it);
            if (W.cash < cost) return;
            W.cash -= cost;
            // パック式は満タンまで入れ直す（残りが半端でも買い直せる）
            if (it.ammo > 0) { W.items[k] = 1; W.ammo[k] = it.ammo; }
            else W.items[k] = W.Has(k) + 1;
            W.bought[k] = (W.bought.ContainsKey(k) ? W.bought[k] : 0) + 1;
            foreach (var P in players) if (WalletOf(P) == W) FixSlot(P);
        }

        /// <summary>NPC が買い物をしてよいか。人と同じ財布を使うときは買わせない（人の金を使わせない）。</summary>
        public bool NpcMayShop(Player P)
        {
            return mode == "versus"
                || (mode == "team" && !players.Exists(q => q.team == P.team && q.src.kind != "npc"));
        }

        /// <summary>
        /// NPC の裏の買い物（ショップへ寄らずに次へ進むとき。JS版 npcShop）。
        /// 3回に1回はアイテム、ほかは買える強化のうちいちばん高い物から。
        /// </summary>
        void NpcShop()
        {
            if (mode == "coop") return;
            foreach (var P in players)
            {
                if (P.src.kind != "npc" || !NpcMayShop(P)) continue;
                var W = WalletOf(P);
                for (int guard = 0; guard < 8; guard++)
                {
                    bool wantItem = guard % 3 == 1;
                    var items = new List<ItemDef>();
                    foreach (var it in ITEMS) if (W.Has(it.k) < it.max && ItemCost(it) <= W.cash) items.Add(it);
                    if (wantItem && items.Count > 0) { BuyItem(items[(int)(Rnd() * items.Count) % items.Count].k, W); continue; }
                    var buy = new List<UpgradeDef>();
                    foreach (var u in UPGRADES) if (W.Up(u.k) < u.max && UpCost(u, W) <= W.cash) buy.Add(u);
                    buy.Sort((a, b) => UpCost(b, W).CompareTo(UpCost(a, W)));
                    if (buy.Count == 0)
                    {
                        if (items.Count > 0) { BuyItem(items[0].k, W); continue; }
                        break;
                    }
                    BuyUpgrade(buy[0].k, W);
                }
            }
        }

        // ================================================================ 面の送り
        /// <summary>結果の画面のあと（JS版 afterResult）。ふつうはショップへ。</summary>
        public void AfterResult()
        {
            if (result == null) return;
            if (entry != null)
            {
                // ストーリー：失敗した面はもう一度（チュートリアルなので、できるまで）。ショップの無い面は次へ
                if (!result.cleared) { shopped = false; LoadCourseStage(courseIndex); return; }
                if (entry.noShop) { shopped = true; NextStage(); return; }   // ライバルにも裏で買わせない
            }
            OpenShop();
        }

        public void OpenShop()
        {
            shopped = true;                      // NPC はショップの画面で買うので、あとで裏買いさせない
            hud.OpenShop();
            if (entry != null && entry.raw != null && entry.raw["shopTalk"] != null)
                TalkOpen(entry.raw["shopTalk"], () => hud.ShopResetLocks(), "ショップ");
        }

        /// <summary>全員が購入完了・時間切れ（JS版 shopDone）。</summary>
        public void ShopDone() { NextStage(); }

        /// <summary>次の面へ（JS版 nextStage）。フリープレイは面を選ぶ。</summary>
        public void NextStage()
        {
            if (!shopped) NpcShop();
            shopped = false;
            hud.CloseShop();
            if (entry != null && course != null)
            {
                if (courseIndex + 1 < course.stages.Count) { LoadCourseStage(courseIndex + 1); return; }
                // 最後の面のあとはエンディング。通したので途中経過の記録は消す
                ClearStory();
                hud.ShowEnding();
                return;
            }
            // フリープレイには終わりが無い。お金・強化・アイテムを持ったまま、次に遊ぶ面を選ぶ
            hud.OpenTitle("Free", true);
        }

        /// <summary>タイトルへ戻る（ショップの T。JS版 backToTitle）。走っている途中のものは畳んでから開く。</summary>
        public void BackToTitle()
        {
            shopped = false;
            TutEnd();
            hud.OpenTitle("Top");
        }

        /// <summary>最初から（ショップの R。JS版 resetRun）。お金と強化も戻す。</summary>
        public void ResetRun()
        {
            hud.CloseShop();
            shopped = false; runDone = 0; runTimes.Clear();
            NewWallets();
            if (entry != null) LoadCourseStage(0); else Rebuild();
        }
    }
}
