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
        Text title, time, orders, sales, status, center, help;
        Image dmgBar, dishBar;
        GameObject resultPanel, menuPanel;
        Text resultText, menuText;
        readonly List<PopItem> pops = new List<PopItem>();
        Overhead over;
        Text toast; float toastT;

        class PopItem { public Text t; public Vector3 w; public float age, life; public bool big; }

        public bool MenuOpen { get { return menuPanel != null && menuPanel.activeSelf; } }

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
            over = new Overhead(root, game ?? GetComponent<Game>());

            // ---- 上の帯
            var bar = Panel(root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -54), Vector2.zero, new Color(0.1f, 0.12f, 0.16f, 0.72f));
            title = Txt(bar, "", 20, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0.45f, 1), new Vector2(16, 0), new Vector2(0, 0));
            time = Txt(bar, "0:00.00", 30, TextAnchor.MiddleCenter, new Vector2(0.4f, 0), new Vector2(0.6f, 1), Vector2.zero, Vector2.zero);
            orders = Txt(bar, "", 20, TextAnchor.MiddleRight, new Vector2(0.6f, 0), new Vector2(0.8f, 1), Vector2.zero, Vector2.zero);
            sales = Txt(bar, "", 20, TextAnchor.MiddleRight, new Vector2(0.8f, 0), new Vector2(1, 1), Vector2.zero, new Vector2(-16, 0));

            // ---- 左下：機体と料理
            var st = Panel(root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(12, 12), new Vector2(300, 112), new Color(0.1f, 0.12f, 0.16f, 0.72f));
            status = Txt(st, "", 17, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(12, 8), new Vector2(-12, -8));
            dmgBar = BarImg(st, new Vector2(110, -18), new Color(1f, 0.42f, 0.3f));
            dishBar = BarImg(st, new Vector2(110, -44), new Color(0.37f, 0.82f, 0.54f));

            // ---- 右下：操作
            help = Txt(root, "←→ 旋回　↑ 前進　↓ バック　Space ジャンプ　R やり直し　F2 調子　Esc 面を選ぶ", 14, TextAnchor.LowerRight,
                       new Vector2(0.4f, 0), new Vector2(1, 0), new Vector2(0, 8), new Vector2(-12, 30));
            help.color = new Color(0.15f, 0.17f, 0.2f, 0.9f);
            Object.Destroy(help.GetComponent<Outline>());

            // ---- まん中：3・2・1・スタート！
            center = Txt(root, "", 96, TextAnchor.MiddleCenter, new Vector2(0, 0.3f), new Vector2(1, 0.7f), Vector2.zero, Vector2.zero);
            center.color = new Color(1f, 0.85f, 0.2f);

            // ---- 結果
            resultPanel = Panel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260, -230), new Vector2(520, 460), new Color(1, 1, 1, 0.96f)).gameObject;
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

            // ---- お知らせ（調子を変えたときなど）
            toast = Txt(root, "", 22, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -110), new Vector2(0, -70));
        }

        public void Toast(string s) { toast.text = s; toastT = 2.2f; }

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

        Image BarImg(Transform parent, Vector2 pos, Color c)
        {
            var bg = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
            var r = bg.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f);
            r.anchoredPosition = pos; r.sizeDelta = new Vector2(170, 12);
            bg.GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);
            var fg = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var fr = fg.GetComponent<RectTransform>();
            fr.SetParent(r, false);
            fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0, 1); fr.pivot = new Vector2(0, 0.5f);
            fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
            var im = fg.GetComponent<Image>(); im.color = c;
            return im;
        }

        static void SetBar(Image im, float k)
        {
            var r = im.rectTransform;
            r.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
        }

        // ------------------------------------------------------------ 呼び出し口
        public void OnStage()
        {
            resultPanel.SetActive(false);
            foreach (var p in pops) Destroy(p.t.gameObject);
            pops.Clear();
            over.Clear();
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
            s.Append(R.cleared ? "＼(^o^)／\n\n" : "ダメージ100%で動けなくなった\n\n");
            if (R.cleared)
            {
                s.Append(Row("タイム", "<b>" + Game.FmtTime(g.frames) + "</b>" + (R.rec ? "　<color=#ff9500>NEW RECORD!</color>" : "")));
                int best = PlayerPrefs.GetInt(g.BestKey(), 0);
                if (best > 0 && !R.rec) s.Append(Row("ベスト", Game.FmtTime(best)));
            }
            int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
            s.Append(Row("配達できた料理", doneN + " / " + g.orders.Count));
            s.Append(Row("売上", "<color=#1a9e4b>" + Game.Yen(R.sales) + "</color>"));
            if (R.best >= 2)
                s.Append(Row("最高 " + R.best + "連鎖" + (R.wreck > 0 ? " → 大暴れボーナス" : "（" + g.T.wreckMin + "連鎖から付きます）"),
                             R.wreck > 0 ? "<color=#ffb300>" + Game.Yen(R.wreck) + "</color>" : "—"));
            s.Append(Row("店舗損壊率 " + Mathf.RoundToInt(R.dmg) + "% → 修理費", "<color=#e53935>" + Game.Yen(-R.repair) + "</color>"));
            s.Append(Row("救急車 " + R.amb + "台", "<color=#e53935>" + Game.Yen(-R.ambCost) + "</color>"));
            s.Append(Row("<b>合計</b>", "<b><color=" + (R.total >= 0 ? "#1a9e4b" : "#e53935") + ">" + Game.Yen(R.total) + "</color></b>"));
            if (R.bonus > 0) s.Append(Row("ステージ達成ボーナス", "<color=#ff9500>" + Game.Yen(R.bonus) + "</color>"));
            s.Append(Row("所持金", Game.Yen(g.cash)));
            s.Append("\n<size=16><color=#666>Enter：もう一度　N（パッドは A）：次の面　Esc：面を選ぶ</color></size>");
            resultText.text = s.ToString();
            resultPanel.SetActive(true);
        }

        static string Row(string k, string v) { return k + "　　" + v + "\n"; }

        // ------------------------------------------------------------ 毎フレーム
        void Update()
        {
            var g = game;
            if (g == null || g.stage == null) return;
            MenuTick();

            title.text = g.stageTitle;
            time.text = Game.FmtTime(g.frames);
            int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
            orders.text = "配達 " + doneN + " / " + g.orders.Count;
            var L = g.LedgerOf(g.me);
            sales.text = "売上 " + Game.Yen(L.sales) + "　損壊 " + Mathf.RoundToInt(g.shopDmg) + "%";
            var P = g.me;
            if (P != null)
            {
                string brk = P.broken != null ? "　<color=#ff6b6b>" + (P.broken == "left" ? "左" : "右") + "旋回 故障</color>" : "";
                if (P.brokenDrive) brk += "　<color=#ff6b6b>駆動 故障</color>";
                string dish = P.carried != null ? P.carried.dish.n + "　" + Mathf.RoundToInt(P.carried.integ) + "%" : "（運んでいない）";
                status.supportRichText = true;
                status.text = "機体　" + Mathf.RoundToInt(P.botDmg) + "%" + brk + "\n料理　\n" + dish
                            + "\n所持金 " + Game.Yen(g.cash);
                SetBar(dmgBar, P.botDmg / 100f);
                SetBar(dishBar, P.carried != null ? P.carried.integ / 100f : 0f);
            }
            // 3・2・1・スタート！
            // JS版と同じ：3.999→3 / 2.999→2 / 1.999→1 / 0.999→スタート！（スタートの間はまだ動けない）
            if (g.state == "ready") { int n = Mathf.CeilToInt(g.readyT) - 1; center.text = n > 0 ? n.ToString() : "スタート！"; }
            else center.text = "";

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
