using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 画面の表示（仮）。上の帯・機体と料理の状態・カウントダウン・数字の吹き出し・セリフ・結果・面選び。
    /// **見た目は仮**（デザインの絵が来たら作り直す前提）。中身は JS版と同じものを出す。
    /// コードだけで組んでいるので、シーンに何も置かなくてよい。
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public Game game;
        Canvas canvas;
        RectTransform root;
        Text center, help;
        HudTop top;
        SettingsPanel settings;
        ShopWin shopWin;
        GameObject gearBtn;
        float readyAnim; string readyShown = "";
        GameObject resultPanel, menuPanel;
        Text resultText, menuText;
        readonly List<PopItem> pops = new List<PopItem>();
        Overhead over;
        Text toast; float toastT;

        class PopItem { public Text t; public Vector3 w; public float age, life; public bool big; }

        public bool MenuOpen { get { return menuPanel != null && menuPanel.activeSelf; } }
        public bool ShopOpen { get { return shopWin != null && shopWin.Open; } }
        public void OpenShop() { resultPanel.SetActive(false); if (MenuOpen) menuPanel.SetActive(false); shopWin.OpenNow(); }
        public void CloseShop() { if (shopWin != null) shopWin.Close(); }

        void Awake()
        {
            var cg = new GameObject("HUD");
            canvas = cg.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var sc = cg.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1280, 720);
            sc.matchWidthOrHeight = 0.5f;
            root = cg.GetComponent<RectTransform>();
            cg.AddComponent<GraphicRaycaster>();             // パネルのつまみをマウスで触れるように
            over = new Overhead(root, GetComponent<Game>());

            // ---- 上の帯と右の人ごとの札（JS版 #topbar / #pcards）
            top = new HudTop(root, GetComponent<Game>());

            // ---- 右下：操作
            help = Txt(root, "←→ 旋回　↑ 前進　↓ バック　Space ジャンプ　R やり直し　F2 調子　Esc 面を選ぶ", 14, TextAnchor.LowerRight,
                       new Vector2(0.4f, 0), new Vector2(1, 0), new Vector2(0, 8), new Vector2(-12, 30));
            help.color = new Color(0.15f, 0.17f, 0.2f, 0.9f);
            Object.Destroy(help.GetComponent<Outline>());

            // ---- まん中：3・2・1・スタート！
            // JS版の #ready：白い太字 110px（スタート！は金色 74px）。出るたびに大きい所から縮んで止まる
            center = Txt(root, "", 110, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, -100), new Vector2(400, 100));
            center.fontStyle = FontStyle.Bold;
            var co = center.GetComponent<Outline>(); co.effectColor = new Color(0, 0, 0, 0.45f); co.effectDistance = new Vector2(3, -5);

            // ---- 結果
            resultPanel = Panel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-330, -280), new Vector2(660, 560), new Color(1, 1, 1, 0.96f)).gameObject;
            resultText = Txt(resultPanel.transform, "", 20, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(28, 20), new Vector2(-28, -20));
            resultText.color = new Color(0.11f, 0.14f, 0.2f);
            Object.Destroy(resultText.GetComponent<Outline>());
            resultText.supportRichText = true;
            resultPanel.SetActive(false);

            // ---- 面を選ぶ
            menuPanel = Panel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-330, -300), new Vector2(660, 600), new Color(0.08f, 0.1f, 0.14f, 0.95f)).gameObject;
            menuText = Txt(menuPanel.transform, "", 17, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(24, 18), new Vector2(-24, -18));
            menuText.supportRichText = true;
            menuPanel.SetActive(false);

            // ---- 検証用パネル（Tab / ⚙）
            // ---- 強化ショップ（全画面。検証用パネルより下に重ねる）
            shopWin = new ShopWin(root, GetComponent<Game>());

            settings = new SettingsPanel(root, GetComponent<Game>());
            {
                var im = UiKit.Img(root, new Color(0.16f, 0.19f, 0.24f, 0.9f), 10, "Gear");
                im.raycastTarget = true;
                var r = im.rectTransform; r.anchorMin = r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(1, 1);
                r.sizeDelta = new Vector2(36, 36); r.anchoredPosition = new Vector2(-12, -88);
                var b = im.gameObject.AddComponent<Button>(); b.targetGraphic = im;
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.onClick.AddListener(() => { settings.Toggle(); UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null); });
                var t = UiKit.Label(im.transform, "⚙", 22, Color.white, false, TextAnchor.MiddleCenter);
                UiKit.Stretch(t.rectTransform);
                gearBtn = im.gameObject;
            }

            // ---- お知らせ（調子を変えたときなど）
            toast = Txt(root, "", 22, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -110), new Vector2(0, -70));
        }

        public void Toast(string s) { toast.text = s; toastT = 2.2f; }
        public void RefreshSettings() { if (settings != null) settings.Refresh(); }
        public void OpenStageMenu() { if (!MenuOpen) MenuOpenNow(); }

        // ------------------------------------------------------------ 部品
        RectTransform Panel(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Color c)
        {
            var g = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var r = g.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = aMin; r.anchorMax = aMax;
            if (aMin == aMax) { r.pivot = Vector2.zero; r.anchoredPosition = pos; r.sizeDelta = size; }
            else { r.offsetMin = new Vector2(0, pos.y); r.offsetMax = new Vector2(0, 0); }
            g.GetComponent<Image>().color = c;
            return r;
        }

        Text Txt(Transform parent, string s, int size, TextAnchor al, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var g = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var r = g.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = offMin; r.offsetMax = offMax;
            var t = g.GetComponent<Text>();
            t.font = Fonts.UI; t.fontSize = size; t.alignment = al; t.text = s; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var o = g.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.6f); o.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        // ------------------------------------------------------------ 呼び出し口
        public void OnStage()
        {
            resultPanel.SetActive(false);
            foreach (var p in pops) Destroy(p.t.gameObject);
            pops.Clear();
            over.Clear();
            top.Clear();
            readyShown = "";
        }

        /// <summary>数字の吹き出し（売上・故障・コンボ）。JS版 pop。</summary>
        public void Pop(Vector3 world, string text, int col, bool big)
        {
            // JS版の .pop：色つきの太字に白いふち（明るい床の上でも読める）。1秒で上へ流れて消える
            var t = Txt(root, text, big ? 25 : 19, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            t.color = Mats.Hex(col);
            t.fontStyle = FontStyle.Bold;
            var o = t.GetComponent<Outline>(); o.effectColor = new Color(1, 1, 1, 0.9f); o.effectDistance = new Vector2(2, -2);
            t.gameObject.AddComponent<Outline>().effectColor = new Color(1, 1, 1, 0.9f);
            pops.Add(new PopItem { t = t, w = world, life = 1.0f, big = big });
        }

        /// <summary>戻ってきた所の印（仮。JS版は光の柱）。</summary>
        public void Mark(Vector3 world, int col)
        {
            Pop(world + Vector3.up * 1.6f, "▼", col, true);
        }

        public void ShowResult()
        {
            var g = game; var R = g.result;
            var s = new System.Text.StringBuilder();
            s.Append("<size=40><b>").Append(R.cleared ? "<color=#1a9e4b>STAGE CLEAR</color>" : "<color=#e53935>FAILED…</color>").Append("</b></size>\n");
            bool many = g.players.Count > 1;
            s.Append(R.cleared ? "＼(^o^)／\n\n" : (many ? "全員が動けなくなった\n\n" : "ダメージ100%で動けなくなった\n\n"));
            resultText.fontSize = many && g.mode != "coop" ? 16 : 20;
            if (R.cleared)
            {
                s.Append(Row("タイム", "<b>" + Game.FmtTime(g.frames) + "</b>" + (R.rec ? "　<color=#ff9500>NEW RECORD!</color>" : "")));
                int best = PlayerPrefs.GetInt(g.BestKey(), 0);
                if (best > 0 && !R.rec) s.Append(Row("ベスト", Game.FmtTime(best)));
            }
            int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
            s.Append(Row("配達できた料理", doneN + " / " + g.orders.Count));
            if (many && g.mode != "coop")
            {
                RankRows(s);
                s.Append("\n<size=14><color=#666>Enter・スペース（パッドは A）：次へ　R：もう一度　Esc：面を選ぶ</color></size>");
                resultText.text = s.ToString();
                resultPanel.SetActive(true);
                return;
            }
            if (many)
            {
                // 協力：財布はひとつ。お店ひとつぶんの成績にして、誰がいくら売ったかを内訳に出す
                foreach (var P in g.players)
                    s.Append(Row(Dot(P) + Nm(P) + Out(P), P.delivered + "品　" + Game.Yen(P.sales)));
            }
            s.Append(Row("売上", "<color=#1a9e4b>" + Game.Yen(R.sales) + "</color>"));
            if (R.best >= 2)
                s.Append(Row("最高 " + R.best + "連鎖" + (R.wreck > 0 ? " → 大暴れボーナス" : "（" + g.T.wreckMin + "連鎖から付きます）"),
                             R.wreck > 0 ? "<color=#ffb300>" + Game.Yen(R.wreck) + "</color>" : "—"));
            s.Append(Row("店舗損壊率 " + Mathf.RoundToInt(R.dmg) + "% → 修理費", "<color=#e53935>" + Game.Yen(-R.repair) + "</color>"));
            s.Append(Row("救急車 " + R.amb + "台", "<color=#e53935>" + Game.Yen(-R.ambCost) + "</color>"));
            s.Append(Row("<b>合計</b>", "<b><color=" + (R.total >= 0 ? "#1a9e4b" : "#e53935") + ">" + Game.Yen(R.total) + "</color></b>"));
            if (R.bonus > 0) s.Append(Row("ステージ達成ボーナス", "<color=#ff9500>" + Game.Yen(R.bonus) + "</color>"));
            s.Append(Row("所持金", Game.Yen(g.cash)));
            s.Append("\n<size=16><color=#666>Enter・スペース（パッドは A）：次へ　R：もう一度　Esc：面を選ぶ</color></size>");
            resultText.text = s.ToString();
            resultPanel.SetActive(true);
        }

        static string Row(string k, string v) { return k + "　　" + v + "\n"; }
        static string Dot(Player P) { return "<color=" + UiKit.Hex(Mats.Hex(P.col)) + ">●</color> "; }
        static string Nm(Player P) { return P.src.kind == "npc" ? "NPC（" + NpcLevels.Get(P.src.lv).name + "）" : P.name; }
        static string Out(Player P) { return P.down ? "<color=#e53935>　" + (P.revT > 0 ? "復帰 " + Mathf.CeilToInt(P.revT) : "リタイア") + "</color>" : ""; }
        static string YenC(float v) { return "<color=" + (v >= 0 ? "#1a9e4b" : "#e53935") + ">" + Game.Yen(v) + "</color>"; }

        /// <summary>個人戦・チーム戦の順位（JS版 finish の順位表。見た目はあとで結果の画面ごと移す）。</summary>
        void RankRows(System.Text.StringBuilder s)
        {
            var g = game;
            if (g.mode == "team")
            {
                s.Append("<b>チーム順位</b>（差引＝チームの売上 − チームが壊したぶんの修理費・救急車）\n");
                var ts = g.TeamsInPlay();
                var tot = new Dictionary<int, Game.Ledger>();
                foreach (var t in ts) tot[t] = g.LedgerOf(g.TeamLead(t));
                ts.Sort((a, b) => tot[b].total.CompareTo(tot[a].total));
                for (int i = 0; i < ts.Count; i++)
                {
                    var L = tot[ts[i]];
                    var ms = g.players.FindAll(q => q.team == ts[i]);
                    int dl = 0; foreach (var q in ms) dl += q.delivered;
                    s.Append("<b>" + (i + 1) + "位 <color=" + UiKit.Hex(Mats.Hex(Game.TEAM_HEX[ts[i]])) + ">" + Game.TEAM_NAME[ts[i]] + "</color></b> "
                             + ms.Count + "人　<b>" + YenC(L.total) + "</b>\n");
                    s.Append("<size=13>　" + dl + "品 " + Game.Yen(L.sales)
                             + (L.wreck > 0 ? " ／ 大暴れ" + L.best + "連鎖 " + Game.Yen(L.wreck) : "")
                             + " ／ 損壊" + Mathf.RoundToInt(L.dmg) + "% " + Game.Yen(-L.repair)
                             + (L.amb > 0 ? " ／ 救急車" + L.amb + "台 " + Game.Yen(-L.ambCost) : "") + "</size>\n");
                    foreach (var P in ms)
                        s.Append("<size=13>　" + Dot(P) + Nm(P) + "　" + P.delivered + "品 " + Game.Yen(P.sales)
                                 + " ／ 壊" + Mathf.RoundToInt(P.shopDmg) + "%" + Out(P) + "</size>\n");
                    s.Append("<size=13>　チームの所持金 " + YenC(g.wallets[ts[i]].cash) + "</size>\n");
                }
            }
            else
            {
                s.Append("<b>順位</b>（差引＝売上 −（自分が壊したぶんの）修理費・救急車）\n");
                var order = g.RankByTotal();
                for (int i = 0; i < order.Count; i++)
                {
                    var P = order[i]; var L = g.LedgerOf(P);
                    s.Append("<b>" + (i + 1) + "位</b> " + Dot(P) + Nm(P) + Out(P) + "　<b>" + YenC(L.total) + "</b>\n");
                    s.Append("<size=13>　" + P.delivered + "品 " + Game.Yen(L.sales)
                             + (L.wreck > 0 ? " ／ 大暴れ" + P.bestCombo + "連鎖 " + Game.Yen(L.wreck) : "")
                             + " ／ 損壊" + Mathf.RoundToInt(L.dmg) + "% " + Game.Yen(-L.repair)
                             + (L.amb > 0 ? " ／ 救急車" + L.amb + "台 " + Game.Yen(-L.ambCost) : "")
                             + " ／ 機体" + Mathf.RoundToInt(P.botDmg) + "%　所持金 " + YenC(g.WalletOf(P).cash) + "</size>\n");
                }
            }
            s.Append("<size=13>お店ぜんたい：損壊 " + Mathf.RoundToInt(g.shopDmg) + "%　倒れた客 " + g.guests.FindAll(x => x.hp <= 50).Count + "人</size>\n");
            var R = g.result;
            if (R.bonuses.Count > 0)
            {
                s.Append("<color=#ff9500><b>ステージ達成ボーナス</b></color>　");
                foreach (var b in R.bonuses)
                    s.Append((b.team != null ? b.rank + "位 " + Game.TEAM_NAME[b.team.Value] : b.P != null ? b.rank + "位 " + Dot(b.P) + Nm(b.P) : "")
                             + " " + Game.Yen(b.amt) + "　");
                s.Append("\n");
            }
        }

        // ------------------------------------------------------------ 毎フレーム
        void Update()
        {
            var g = game;
            if (g == null || g.stage == null) return;
            if (shopWin.Open) shopWin.Tick(Time.deltaTime); else MenuTick();

            top.Tick();
            // 3・2・1・スタート！
            // JS版と同じ：3.999→3 / 2.999→2 / 1.999→1 / 0.999→スタート！（スタートの間はまだ動けない）
            string label = "";
            if (g.state == "ready") { int n = Mathf.CeilToInt(g.readyT) - 1; label = n > 0 ? n.ToString() : "スタート！"; }
            if (label != readyShown)
            {
                readyShown = label; readyAnim = 0;
                center.text = label;
                bool go = label == "スタート！";
                center.fontSize = go ? 74 : 110;
                center.color = go ? Mats.Hex(0xffd24a) : Color.white;
            }
            // 拡大 2.1 → 1（少し行き過ぎて戻る）、0.55秒
            readyAnim += Time.deltaTime;
            {
                float k = Mathf.Clamp01(readyAnim / 0.55f);
                float e = 1 + 2.7f * Mathf.Pow(k - 1, 3) + 1.7f * Mathf.Pow(k - 1, 2);   // 行き過ぎて戻る
                center.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(2.1f, 1f, e);
                var cc = center.color; cc.a = Mathf.Clamp01(k * 2.5f); center.color = cc;
            }

            var cam = g.mainCam;
            // 数字の吹き出し
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i];
                p.age += Time.deltaTime;
                if (p.age >= p.life) { Destroy(p.t.gameObject); pops.RemoveAt(i); continue; }
                var sp = cam.WorldToScreenPoint(p.w + Vector3.up * p.age * 0.7f);
                p.t.rectTransform.position = sp;
                var c = p.t.color; float k = p.age / p.life; c.a = 1 - k * k; p.t.color = c;
                p.t.gameObject.SetActive(sp.z > 0);
            }
            over.Tick();
            settings.Tick();
            var kbd = Keyboard.current;
            if (kbd != null && kbd.tabKey.wasPressedThisFrame) settings.Toggle();
            gearBtn.SetActive(!settings.Open);
            if (toastT > 0)
            {
                toastT -= Time.deltaTime;
                var tc = toast.color; tc.a = Mathf.Clamp01(toastT / 0.5f); toast.color = tc;
                if (toastT <= 0) toast.text = "";
            }
        }

        // ------------------------------------------------------------ 面を選ぶ（Esc）
        readonly List<System.Action> menuAct = new List<System.Action>();
        readonly List<string> menuLabel = new List<string>();
        int menuCur, menuTop;

        void MenuOpenNow()
        {
            menuAct.Clear(); menuLabel.Clear();
            var g = game;
            if (g.course != null)
                for (int i = 0; i < g.course.stages.Count; i++)
                {
                    var e = g.course.stages[i]; int ii = i;
                    menuLabel.Add("STAGE " + (i + 1) + "　" + (e.title ?? e.cfg) + "　<color=#8b93a0>" + e.shop + " / " + e.cfg + "</color>");
                    menuAct.Add(() => g.LoadCourseStage(ii));
                }
            foreach (var f in StageCfg.List())
            {
                string ff = f;
                menuLabel.Add("<color=#9fd0ff>" + f + "</color>　<color=#8b93a0>（カフェ）</color>");
                menuAct.Add(() => g.LoadFile(ff, "カフェ"));
            }
            menuCur = Mathf.Clamp(menuCur, 0, Mathf.Max(0, menuAct.Count - 1));
            menuPanel.SetActive(true);
        }

        void MenuTick()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame)
            {
                if (MenuOpen) menuPanel.SetActive(false); else MenuOpenNow();
            }
            if (!MenuOpen) return;
            if (kb.downArrowKey.wasPressedThisFrame) menuCur = Mathf.Min(menuAct.Count - 1, menuCur + 1);
            if (kb.upArrowKey.wasPressedThisFrame) menuCur = Mathf.Max(0, menuCur - 1);
            if (kb.pageDownKey.wasPressedThisFrame) menuCur = Mathf.Min(menuAct.Count - 1, menuCur + 10);
            if (kb.pageUpKey.wasPressedThisFrame) menuCur = Mathf.Max(0, menuCur - 10);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                menuPanel.SetActive(false);
                if (menuCur < menuAct.Count) menuAct[menuCur]();
                return;
            }
            const int rows = 24;
            if (menuCur < menuTop) menuTop = menuCur;
            if (menuCur >= menuTop + rows) menuTop = menuCur - rows + 1;
            var s = new System.Text.StringBuilder("<b>面を選ぶ</b>　<color=#8b93a0>↑↓ で選んで Enter／Esc で閉じる</color>\n\n");
            for (int i = menuTop; i < Mathf.Min(menuAct.Count, menuTop + rows); i++)
                s.Append(i == menuCur ? "<color=#ffc44d>▶ </color>" : "　 ").Append(menuLabel[i]).Append('\n');
            menuText.text = s.ToString();
        }
    }
}
