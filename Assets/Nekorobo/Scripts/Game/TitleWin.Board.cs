using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 設定（CONFIG）・実績（ACHIEVEMENT）の画面。清書1111（HTML版 setDraw・achDraw）。
    /// 1280×720 の板（UiBoard）に、PSD から切り出した絵（HTML版の assets/ui/cfg・ach）を置く。字はゲームが書く（訳すため）。
    /// 位置は PSD（2560×1440）の座標の半分（HTML版と同じ数値）。緑の枠の中の説明の字は黄色。
    /// 選ぶ・決めるはタイトルと同じ items（上下左右は画面の上の位置で隣のボタンへ。BoardNav）。
    /// 札（タブ）には選択枠を当てず、LB / RB で替える（マウスは札を押す）。替えたら中身の「いま選ばれている物」に枠が入る
    /// （サウンドは BGM、言語は今の言語、操作説明は今のコントローラー／キーボード、著作権表示は一覧。担当）。開くたびにサウンドの札から。
    ///   サウンド   … BGM・効果音の棒。← → で 10% ずつ（マウスは押した所。押したまま左右に動かせる）。いちばん左（0%）が音なし
    ///   言語       … 5つの言語のボタン。決定で切り替える。名前はその言語の書体で書く
    ///   操作説明   … コントローラー（Xbox の並び）／キーボードの絵と、何をするか
    ///   著作権表示 … ゲームの名前・バージョン・コピーライト（動かない）と、使っているライブラリ・書体の一覧（上下に送る箱。
    ///                 箱に乗って ↑↓・ホイール。マウスは字の所を押したまま上下に動かすと字が付いてくる、右の棒（白）は押した高さへ送り、
    ///                 押したまま動かすと付いてくる。担当「増えても大丈夫なように、上下のスクロールに」）
    /// 中身で B を押すと、すぐ閉じずに下の段の「もどる」へ枠が抜ける。そこでもう一度 B で閉じる。
    /// 下の段（CREDITS・もどる）から ↑ は、抜ける前に選んでいた中身へ戻る（札ごとに覚える setLast。開き直すと忘れる）。
    /// 左下の「CREDITS」（もどると同じ絵。札を替えても常に出す）で、黒い幕の上にスタッフロールを流す（credits.json。HTML版 creditsShow）。
    /// 実績：左の札で仕分けを選び（決定で一覧へ）、右に一覧（一度に4行。▲▼・ホイール・一覧に乗って↑↓で送る。B で札へ戻る）。
    /// 取った行は金で日付つき。まだの行は灰色。★1 は取る前から中身が見え、★2・★3 は取るまで「？？？」
    /// </summary>
    public partial class TitleWin
    {
        const string CFGU = "ui/cfg/", ACHU = "ui/ach/";
        static readonly Color YEL = Mats.Hex(0xffe45a);
        UiBoard cfgB;
        string boardPage;
        // 設定の札。off … 選んでいない札の絵の位置、on … 選んでいる札の絵の位置（どちらも左・上・幅・高さ）
        static readonly string[] SET_TAB_K = { "snd", "lang", "ctrl", "lic" };
        static readonly string[] SET_TAB_N = { "サウンド", "言語", "操作説明", "著作権表示" };
        static readonly float[][] SET_TAB_OFF = { new[] { 449.5f, 85, 210, 62.5f }, new[] { 641.5f, 85, 211, 62.5f }, new[] { 834f, 85, 210.5f, 62.5f }, new[] { 1026f, 85, 211, 62.5f } };
        static readonly float[][] SET_TAB_ON = { new[] { 447f, 81, 213, 63.5f }, new[] { 639f, 81, 213, 63.5f }, new[] { 831.5f, 81, 213, 63.5f }, new[] { 1023.5f, 81, 213, 63.5f } };
        // 操作説明の字。[字, x, y（字の真ん中）, 寄せ（l・r・c）, 収める幅]
        static readonly object[][] SET_PAD_TXT =
        {
            new object[] { "エモート(ポジティブ)", 315.5f, 275.75f, "r", 250f }, new object[] { "前のアイテム", 316f, 318f, "r", 250f },
            new object[] { "方向転換/押し込みでダッシュ", 315.5f, 383.25f, "r", 250f }, new object[] { "↑前進 ↓後退 ←→方向転換", 316f, 429.75f, "r", 250f },
            new object[] { "エモート(ネガティブ)", 964.5f, 194.75f, "l", 255f }, new object[] { "次のアイテム", 964.5f, 237f, "l", 255f },
            new object[] { "アイテムを使う", 964.5f, 283f, "l", 255f }, new object[] { "ジャンプ", 965f, 323.25f, "l", 255f },
            new object[] { "後退/もどる", 964.5f, 363.75f, "l", 255f }, new object[] { "前進/決定", 964.5f, 403.25f, "l", 255f },
            new object[] { "エモート選択/押し込みで料理を捨てる", 964.5f, 454.25f, "l", 255f }, new object[] { "ポーズメニュー", 697.5f, 194f, "l", 200f },
            new object[] { "LB＋LT＋RB＋RT を長押し：クレーンで出現位置へ戻る(－10,000)", 639.75f, 513f, "c", 1100f },
        };
        static readonly object[][] SET_KEY_TXT =
        {
            new object[] { "前進", 657.5f, 234.75f, "l", 240f }, new object[] { "後退", 657f, 277f, "l", 240f }, new object[] { "方向転換", 693.5f, 317.75f, "l", 205f },
            new object[] { "ジャンプ", 694f, 359.25f, "l", 205f }, new object[] { "料理を捨てる", 656.5f, 440.75f, "l", 240f },
            new object[] { "アイテムを使う", 948f, 235f, "l", 265f }, new object[] { "次/前のアイテム", 983.5f, 277f, "l", 232f }, new object[] { "決定", 984f, 316.5f, "l", 232f },
            new object[] { "もどる/ポーズメニュー", 984f, 360.25f, "l", 232f }, new object[] { "エモート(押している間)", 984f, 400.75f, "l", 232f },
            new object[] { "エモート選択(次/前)", 1075f, 440.25f, "l", 142f },
            new object[] { "Q＋E＋X＋C を長押し：クレーンで出現位置へ戻る(－10,000)", 107.5f, 491.5f, "l", 520f },
        };
        // 実績の仕分けのアイコンの大きさ（一覧の札用。PSD の半分）。仕分けの札の段（上から）。一覧の行の間隔。一度に見える行
        static readonly float[][] ACH_IC = { new[] { 26f, 38.5f }, new[] { 48f, 36 }, new[] { 40f, 40 }, new[] { 44f, 44 }, new[] { 47f, 45 }, new[] { 45f, 38 }, new[] { 47f, 30.5f } };
        static readonly float[] ACH_CAT_Y = { 131.5f, 209, 287, 364.5f, 442, 520.5f, 597.5f };
        const float ACH_ROW_H = 82; const int ACH_VIS = 4;
        static string boardSetTab = "snd", boardCtrl = "pad";
        // 札ごとに、B・↓で抜ける前に選んでいた中身（下の段から ↑ で戻る先。HTML版 SET.last）
        static readonly Dictionary<string, string> setLast = new Dictionary<string, string>();
        static int achTop;
        RectTransform achIn; RawImage achUp, achDn; float achY;

        static bool IsBoard(string pg) { return pg == "Settings" || pg == "Ach"; }

        // ---------------------------------------------------------------- 板を出す・描く
        /// <summary>focus … そのボタンへ（札を替えたときなど）。無ければ同じボタンに残る。開いたばかりなら既定へ</summary>
        void BoardDraw(string pg, string focus = null)
        {
            bool first = boardPage != pg || cfgB == null || !cfgB.Active;
            string keep = !first && idx < items.Count ? items[idx].key : null;
            boardPage = pg;
            if (first && pg == "Settings") { boardSetTab = "snd"; setLast.Clear(); }   // 設定は開くたびにサウンドの札から（担当）
            if (cfgB == null) cfgB = new UiBoard(root, "CfgBoard", true, CFGU + "bg.jpg");
            cfgB.Active = true;
            cfgB.Clear();
            items.Clear(); gridItems.Clear(); freeDesc = null; cap = null; hov = -1;
            achIn = null; achUp = achDn = null;
            crRoot = null; crOutT = -1; licIn = null; licThumb = null;
            string def = pg == "Settings" ? SetDraw() : AchDraw();
            if (first) { cfgB.StartIn(); lockT = g.T.menuLock; armed = false; }
            string want = focus == "tab" ? def : focus ?? keep ?? def;     // "tab" … 札を替えた（中身の「いま選ばれている物」へ）
            int d = want != null ? items.FindIndex(x => x.key == want) : -1;
            if (d < 0) d = items.FindIndex(x => x.key == def);
            idx = Mathf.Max(0, d);
            FocusQuiet(idx);
        }

        void BoardHide()
        {
            if (cfgB != null) cfgB.Active = false;
            boardPage = null;
        }

        /// <summary>音を鳴らさず、乗ったときの動き（onFocus）もせずに立て直す</summary>
        void FocusQuiet(int i)
        {
            if (items.Count == 0) return;
            idx = Mathf.Clamp(i, 0, items.Count - 1);
            Looks();
        }

        // 字。y は字の真ん中（HTML版 .t.l / .t.r / .t.c は translateY(-50%)）。al … l（x から右へ）・r（x で終わる）・c（真ん中）。
        // mw … 収める幅（長い訳は字を小さくする。HTML版 fitWidth）。s は訳したあとの字
        static Text BT(RectTransform p, string s, float x, float y, float size, Color c, string al = "l", float mw = 0, Font font = null)
        {
            var t = UiBoard.T(p, "", x, y, size, c, al == "r" ? 1 : al == "c" ? 2 : 0);
            var r = t.rectTransform;
            r.pivot = new Vector2(al == "r" ? 1 : al == "c" ? 0.5f : 0, 0.5f);
            r.anchoredPosition = new Vector2(x, -y);
            t.alignment = al == "r" ? TextAnchor.MiddleRight : al == "c" ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            if (font != null) t.font = font;
            t.text = s;
            Fit(t, mw);
            return t;
        }
        static void Fit(Text t, float mw)
        {
            if (mw <= 0) return;
            float w = t.preferredWidth;
            if (w > mw) t.fontSize = Mathf.Max(7, Mathf.FloorToInt(t.fontSize * mw / w));
        }

        /// <summary>板の上のボタン（絵を重ねた箱）。frame … 選択枠の形（"skew"＝板の絵の斜めのボタン。PSD の札と枠の位置の差で置く、
        /// "box"＝四角）。parent … 置く所（無ければ板。クレジットの幕の「もどる」は幕の中）</summary>
        Item BBtn(string key, float x, float y, float w, float h, System.Action fire, string frame = "skew", RectTransform parent = null)
        {
            var r = UiBoard.Box(parent ?? cfgB.box, x, y, w, h, "B_" + key);
            var it = new Item { rt = r, key = key, fire = fire, frameTan = frame == "skew" ? -0.35f : 0, frameBox = frame == "box", frameImg = frame == "skew" };
            // 選んでいるボタンは少し明るく（HTML版 filter:brightness(1.12)）。選択枠が本命
            var ims = new List<RawImage>(); var orig = new Dictionary<RawImage, Texture>();
            it.look = on =>
            {
                foreach (var im in r.GetComponentsInChildren<RawImage>(true))
                {
                    if (!orig.ContainsKey(im)) orig[im] = im.texture;
                    var o = orig[im];
                    if (o != null) im.texture = on ? UiBoard.Bright(o, 1.12f) : o;
                }
            };
            items.Add(it);
            return it;
        }

        /// <summary>もどると同じ絵のボタン（左下の「CREDITS」・右下の「もどる」）。押したら押した絵にしてから fire</summary>
        Item BBackLike(string key, float x, string label, System.Action fire, RectTransform parent = null)
        {
            Item it = null;
            it = BBtn(key, x, 591, 260, 83, () =>
            {
                var b0 = it.rt.Find("b0"); var b1 = it.rt.Find("b1");
                if (b0 != null) b0.gameObject.SetActive(false);
                if (b1 != null) b1.gameObject.SetActive(true);
                fire();
            }, "skew", parent);
            UiBoard.Img(it.rt, CFGU + "back.png", 0, 0, 260, 83, "b0");
            var on = UiBoard.Img(it.rt, CFGU + "back_on.png", 0, 0, 260, 83, "b1"); on.gameObject.SetActive(false);
            BT(it.rt, label, 130 - 1, 41.5f - 4, 30, Color.white, "c");
            return it;
        }
        /// <summary>もどる（押した絵を少し見せてから戻る）</summary>
        void BBack(System.Func<string> up = null)
        {
            var it = BBackLike("back", 976.5f, Lang.Tr("もどる"), () => backLater = 0.11f);
            it.back = true; it.upFn = up;
            cfgB.In(it.rt, "fade", 3);
        }
        /// <summary>左下の「CREDITS」（設定の札を替えても常に出す。名前は英語で全言語同じ）</summary>
        void BCredit(System.Func<string> up = null)
        {
            var it = BBackLike("credit", 43.5f, "CREDITS", CreditsOpen);
            it.upFn = up;
            cfgB.In(it.rt, "fade", 3);
        }
        float backLater = -1;

        // ---------------------------------------------------------------- 設定
        void SetTabStep(int dir)
        {
            int i = System.Array.IndexOf(SET_TAB_K, boardSetTab);
            boardSetTab = SET_TAB_K[(i + dir + SET_TAB_K.Length) % SET_TAB_K.Length];
            Game.SeUi("cur");
            BoardDraw("Settings", "tab");
        }
        static float VolOf(string k) { return k == "Bgm" ? (Sound.BgmMuted ? 0 : Sound.BgmVolume) : (Sound.Muted ? 0 : Sound.Volume); }
        RawImage barBgm, barSe;
        void VolTo(string k, float v, bool live)
        {
            v = Mathf.Clamp01(Mathf.Round(v * 20) / 20);
            if (live && Mathf.Approximately(v, VolOf(k))) return;            // 5% の刻みが変わったときだけ
            if (k == "Bgm") { Sound.SetBgmVolume(v); if (Sound.BgmMuted) Sound.SetBgmMute(false); }
            else { Sound.SetVolume(v); if (Sound.Muted) Sound.SetMute(false); Game.SeUi("tick"); }
            BarFill(k == "Bgm" ? barBgm : barSe, VolOf(k));
        }
        static void BarFill(RawImage full, float v)
        {
            if (full == null) return;
            float w = 35.5f + 889.5f * v;
            full.rectTransform.sizeDelta = new Vector2(w, 33.5f);
            full.uvRect = new Rect(0, 0, w / 933f, 1);
        }

        string SetDraw()
        {
            var b = cfgB.box;
            string tab = boardSetTab;
            var ttl = UiBoard.Img(b, CFGU + "title.png", 48.5f, 45.5f, 277, 56.5f); cfgB.In(ttl, "slam", 0);
            // 札：選んでいない札 → 緑の枠 → 選んでいる札（枠の上に重なる）
            for (int i = 0; i < SET_TAB_K.Length; i++) if (SET_TAB_K[i] != tab) { var o = SET_TAB_OFF[i]; UiBoard.Img(b, CFGU + "tab_" + SET_TAB_K[i] + ".png", o[0], o[1], o[2], o[3]); }
            UiBoard.Img(b, CFGU + "panel.png", 50, 140, 1180, 412.5f);
            { int ti = System.Array.IndexOf(SET_TAB_K, tab); var o = SET_TAB_ON[ti]; UiBoard.Img(b, CFGU + "tab_" + tab + "_on.png", o[0], o[1], o[2], o[3]); }
            for (int i = 0; i < SET_TAB_K.Length; i++)
            {
                string k = SET_TAB_K[i]; var o = SET_TAB_OFF[i];
                // 札にはカーソル（選択枠）を当てない。LB/RB で替える（マウスは押せる）。替えたら中身の「いま選ばれている物」へ（担当）
                System.Action go = () => { if (boardSetTab != k) { boardSetTab = k; Game.SeUi("cur"); BoardDraw("Settings", "tab"); } };
                var it = BBtn("tab_" + k, o[0], o[1], o[2], o[3], go);
                it.nofocus = true; it.grp = "tab"; it.on = k == tab;
                BT(it.rt, Lang.Tr(SET_TAB_N[i]), o[2] / 2 - 2, o[3] / 2, 19, Color.white, "c");
            }
            string def = null;
            barBgm = barSe = null;
            if (tab == "snd")
            {
                foreach (var row in new[] { new object[] { "Bgm", "BGM", 292f, 114.5f, 308.5f }, new object[] { "Se", "SE", 369f, 113f, 385.5f } })
                {
                    string k = (string)row[0]; float y = (float)row[2];
                    BT(b, (string)row[1], (float)row[3], (float)row[4], 28, YEL);
                    var it = BBtn("sl" + k, 238, y, 933, 33.5f, null);
                    it.frameImg = false; it.framePad = 7;
                    UiBoard.Img(it.rt, CFGU + "bar.png", 0, 0, 933, 33.5f);
                    var full = UiBoard.Img(it.rt, CFGU + "bar_full.png", 0, 0, 933, 33.5f);
                    if (k == "Bgm") barBgm = full; else barSe = full;
                    BarFill(full, VolOf(k));
                    it.adj = dir => VolTo(k, VolOf(k) + dir * 0.1f, false);
                    // マウス：押した所の大きさにして、押したまま左右に動かすと付いてくる（棒の外へ出ても離すまでは効く）
                    var bar = it.rt;
                    it.hold = sp =>
                    {
                        Vector2 lp;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(bar, sp, null, out lp);
                        VolTo(k, (lp.x - bar.rect.xMin - 35.5f) / 889.5f, true);
                    };
                }
                def = "slBgm";
            }
            else if (tab == "lang")
            {
                // 5つ。上に3つ、下に2つ（真ん中に寄せる）。絵は PSD の 0.75 倍
                const float S = 0.75f, W = 466.5f * S, H = 83 * S, G = 22;
                int nl = Lang.LANGS.Length;
                for (int i = 0; i < nl; i++)
                {
                    int row = i < 3 ? 0 : 1, n = row == 1 ? nl - 3 : 3, col = row == 1 ? i - 3 : i;
                    float x = 640 - (n * W + (n - 1) * G) / 2 + col * (W + G), y = row == 1 ? 362 : 262;
                    string lk = Lang.LANGS[i][0]; bool cur = lk == Lang.Cur;
                    var it = BBtn("lang" + i, x, y, W, H, null);
                    int li = i;
                    it.fire = () => { if (!cur) { Lang.Set(lk); BoardDraw("Settings", "lang" + li); } };
                    if (cur) UiBoard.Img(it.rt, CFGU + "lang_on.png", -3 * S, -4 * S, 469.5f * S, 87 * S);
                    else UiBoard.Img(it.rt, CFGU + "lang.png", 0, 0, W, H);
                    BT(it.rt, Lang.LANGS[i][1], 231.5f * S, 38.5f * S, 22, Color.white, "c", 0, Fonts.OfLang(lk));
                    if (cur) def = "lang" + i;
                }
            }
            else if (tab == "ctrl")
            {
                bool pad = boardCtrl == "pad";
                foreach (var row in new[] { new object[] { "cPad", "pad", 86.5f, "コントローラー", 31.25f }, new object[] { "cKey", "key", 289f, "キーボード", 30f } })
                {
                    string k = (string)row[0], v = (string)row[1];
                    System.Action go = () => { if (boardCtrl != v) { boardCtrl = v; BoardDraw("Settings", k); } };
                    var it = BBtn(k, (float)row[2], 170, 213, 69, go);
                    it.onFocus = go; it.grp = "sub"; it.on = boardCtrl == v;
                    UiBoard.Img(it.rt, CFGU + (boardCtrl == v ? "sub_on" : "sub") + ".png", 0, 0, 213, 69);
                    BT(it.rt, Lang.Tr((string)row[3]), 105.5f - 2, (float)row[4], 18, Color.white, "c");
                }
                if (pad) UiBoard.Img(b, CFGU + "pad.png", 323.5f, 179, 632.5f, 316);
                else UiBoard.Img(b, CFGU + "key.png", 88.5f, 220.5f, 974.5f, 237.5f);
                foreach (var t in pad ? SET_PAD_TXT : SET_KEY_TXT)
                    BT(b, Lang.Tr((string)t[0]), (float)t[1], (float)t[2], 13, YEL, (string)t[3], (float)t[4]);
                if (!pad)
                {
                    // 2行（下は小さい字）。2行まとめて y=399.5 が真ん中
                    var t1 = BT(b, Lang.Tr("向いている方向へダッシュ"), 693, 393.25f, 13, YEL, "l");
                    var t2 = BT(b, Lang.Tr("(5秒に1回。ぶつけると自分も相手も大ダメージ)"), 693, 407.5f, 9.5f, YEL, "l");
                    float w = Mathf.Max(t1.preferredWidth, t2.preferredWidth);
                    if (w > 235) { t1.fontSize = Mathf.Max(7, Mathf.FloorToInt(13 * 235 / w)); t2.fontSize = Mathf.Max(6, Mathf.FloorToInt(9.5f * 235 / w)); }
                }
                def = pad ? "cPad" : "cKey";
            }
            else { LicDraw(b); def = "licList"; }
            // 中身のボタン：B で下の段の「もどる」へ抜ける（すぐ設定を閉じない。担当）。抜けたボタンは ↑ で戻る先として覚える。
            // 下の段（CREDITS・もどる）：↑ で覚えた中身へ（無ければ「いま選ばれている物」）。そこで B を押すと「もどる」
            string tabNow = tab, defNow = def;
            foreach (var it in items)
            {
                if (it.grp == "tab") continue;
                string k = it.key;
                it.leave = () => setLast[tabNow] = k;
                it.backFn = () =>
                {
                    setLast[tabNow] = k;
                    int bi = items.FindIndex(x => x.key == "back");
                    if (bi >= 0) Focus(bi);
                };
            }
            System.Func<string> up = () => { string s; return setLast.TryGetValue(tabNow, out s) ? s : defNow; };
            BCredit(up);                       // 左下の「CREDITS」（札を替えても常に出す）
            BBack(up);
            return def;
        }

        // 著作権表示。上にゲームの名前・バージョン・コピーライト（動かない）。その下は1列の一覧を上下に送る箱（HTML版 .cb-licv）。
        // 箱に乗って ↑↓（56px ずつ。端まで送ったら隣のボタンへ）・ホイール・右の棒（押した所へ）で送る
        const float LIC_STEP = 56, LIC_VH = 290;
        static float licTop;                     // 送った量（札を替えても覚えておく。HTML版 SET.licTop）
        RectTransform licIn; RawImage licThumb, licBarBg; float licY, licMx, licH;
        void LicDraw(RectTransform b)
        {
            const float L = 96, T0 = 172, LH = 1.35f, W = 1050;
            BT(b, Lang.Tr(GAME_TITLE), L, T0 + 22 * LH / 2, 22, YEL);
            BT(b, "Ver. " + GAME_VER + "　" + Lang.Tr(GAME_COPY), L, T0 + 22 * LH + 13 * LH / 2, 13, YEL);
            var v = UiBoard.Box(b, 96, 240, W, LIC_VH, "LicView");
            v.gameObject.AddComponent<RectMask2D>();
            licIn = UiBoard.Box(v, 0, 0, W, 10, "In");
            float y = 0;
            foreach (var l in LICENSES)
            {
                // 字の箱にも幅を持たせる（幅 0 の箱は、送る箱（RectMask2D）の外とみなされて消える）
                BT(licIn, Lang.Tr(l[0]), 0, y + 17 * LH / 2, 17, YEL, "l", W).rectTransform.sizeDelta = new Vector2(W, 17 * LH); y += 17 * LH;
                BT(licIn, l[1], 0, y + 13 * LH / 2, 13, YEL, "l", W).rectTransform.sizeDelta = new Vector2(W, 13 * LH); y += 13 * LH;
                var c = UiBoard.T(licIn, "", 0, y, 12, new Color(YEL.r, YEL.g, YEL.b, 0.78f));
                c.text = l[2];
                c.horizontalOverflow = HorizontalWrapMode.Wrap; c.lineSpacing = 1.1f;
                var cr = c.rectTransform;
                cr.sizeDelta = new Vector2(W, 200);
                float ph = c.preferredHeight;                                    // 折り返した行の数を測る
                int lines = Mathf.Max(1, c.cachedTextGeneratorForLayout.lineCount);
                cr.sizeDelta = new Vector2(W, Mathf.Max(ph, lines * 13 * LH));
                y += lines * 13 * LH + 14;
            }
            licH = Mathf.Max(0, y - 14);                                         // 最後の段の下の空きは入れない
            licIn.sizeDelta = new Vector2(W, licH);
            licMx = Mathf.Max(0, licH - LIC_VH);
            // 右の棒（送れるときだけ出す）
            var bar = UiBoard.Box(b, 1166, 240, 7, LIC_VH, "LicBar");
            // 右の棒はいったん白（担当「黄色だと分かりにくい」）
            licBarBg = bar.gameObject.AddComponent<RawImage>(); licBarBg.color = new Color(1, 1, 1, 0.25f); licBarBg.raycastTarget = false;
            var th = UiBoard.Box(bar, 0, 0, 7, 28, "Thumb");
            licThumb = th.gameObject.AddComponent<RawImage>(); licThumb.color = Color.white; licThumb.raycastTarget = false;
            licY = licTop = Mathf.Clamp(licTop, 0, licMx);
            LicScroll(0, true);
            // 一覧の箱（選ぶ所）。マウスは押したまま上下に動かすと送る（担当）。
            //   字の所 … 字が指に付いてくる（スマホと同じ向き）
            //   右の棒 … 押した高さへ送り、そのまま動かすと付いてくる
            // 枠の外へ出ても離すまでは効く（BoardMouse の hold）
            var it = BBtn("licList", 86, 230, 1106, 306, null, "box");
            it.vadj = d => LicScroll(d);
            it.wheel = d => LicScroll(d);
            it.hold = LicDrag;
        }
        bool licDrag, licDragBar; float licDragY0, licDragT0;
        void LicDrag(Vector2 sp)
        {
            if (licIn == null || licMx <= 0) return;
            var ms = Mouse.current;
            var view = (RectTransform)licIn.parent;
            Vector2 lp;
            if (!licDrag || (ms != null && ms.leftButton.wasPressedThisFrame))
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(licBarBg.rectTransform, sp, null, out lp);
                licDragBar = lp.x >= -14 && lp.x <= 7 + 14;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(view, sp, null, out lp);
                licDragY0 = lp.y; licDragT0 = licTop; licDrag = true;
            }
            float t;
            if (licDragBar)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(licBarBg.rectTransform, sp, null, out lp);
                t = Mathf.Clamp01(-lp.y / LIC_VH) * licMx;
            }
            else
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(view, sp, null, out lp);
                t = licDragT0 + (lp.y - licDragY0);
            }
            LicScroll(0, true, Mathf.Round(t));
        }
        /// <summary>一覧を送る（dir … 1 下へ・-1 上へ・0 今の位置を合わせ直すだけ）。送れたら true（送れないときは ↑↓ で隣のボタンへ）</summary>
        bool LicScroll(int dir, bool quiet = false, float? to = null)
        {
            if (licIn == null) return false;
            float t = Mathf.Clamp(to ?? licTop + dir * LIC_STEP, 0, licMx);     // to … 送った量をじかに（マウスで押したまま動かすとき）
            bool moved = !Mathf.Approximately(t, licTop);
            licTop = t;
            if (licBarBg != null)
            {
                licBarBg.gameObject.SetActive(licMx > 0);
                float H = LIC_VH, thh = Mathf.Max(28, H * LIC_VH / Mathf.Max(1, licH));
                var tr = licThumb.rectTransform;
                tr.sizeDelta = new Vector2(7, thh);
                tr.anchoredPosition = new Vector2(0, -(licMx > 0 ? (H - thh) * t / licMx : 0));
            }
            if (moved && dir != 0 && !quiet) Game.SeUi("cur");
            return moved;
        }

        // ---------------------------------------------------------------- クレジット（スタッフロール）
        // 押すとフワッと真っ黒になって、映画のスタッフロールのように下から上へ字を流す。最後の「開発・販売」から下は画面の真ん中で止まる（担当）。
        // 右下の「もどる」（と B・Esc）でいつでも止めて設定へ戻る。中身は credits.json（HTML版と同じファイル）。
        // 字は訳さない（日本語と英語を併記してある）。止まったときに、その上（パブリッシングチームなど）が画面の上へ抜けきって、
        // 「開発・販売」から下だけが真ん中に残るように、最後の固まりの前を空ける（担当「パブリッシングチームの所が見えなくなって」）
        const float CRED_SPEED = 60;           // 流れる速さ[px/秒]（板の 1280×720 の px）
        const float CRED_WAIT = 0.6f;          // 黒くなってから流し始めるまで[秒]
        RectTransform crRoot, crRoll; CanvasGroup crCg;
        float crY, crStop, crT, crInT, crOutT = -1;
        bool CredOn { get { return crRoot != null; } }

        void CreditsOpen()
        {
            var d = DataRoot.ReadJson("credits.json");
            if (d == null) { Game.SeUi("ng"); return; }
            var box = cfgB.box;
            // 板の中に、画面いっぱいの黒い幕を重ねる（板の外の余白も黒く）。中の字とボタンは板の 1280×720 の位置
            crRoot = UiBoard.Box(box, -3000, -3000, 1280 + 6000, 720 + 6000, "Credits");
            var bg = crRoot.gameObject.AddComponent<RawImage>(); bg.color = Color.black; bg.raycastTarget = true;
            crCg = crRoot.gameObject.AddComponent<CanvasGroup>(); crCg.alpha = 0;
            var inn = UiBoard.Box(crRoot, 3000, 3000, 1280, 720, "In");
            crRoll = UiBoard.Box(inn, 0, 0, 1280, 10, "Roll");
            const float LH = 1.35f;
            float y = 0;
            System.Action<string, float, float, Color> line = (t, size, mt, col) =>
            {
                y += mt;
                BT(crRoll, t ?? "", 640, y + size * LH / 2, size, col, "c");
                y += size * LH;
            };
            Color gray = Mats.Hex(0xcfcfcf);
            line((string)d["title"], 38, 0, Color.white);
            if (d["sub"] != null) line((string)d["sub"], 16, 8, Mats.Hex(0xd8d8d8));
            foreach (var g in (d["groups"] as JArray) ?? new JArray())
            {
                line((string)g["head"], 26, 70, YEL);
                foreach (var r in (g["roles"] as JArray) ?? new JArray())
                {
                    line((string)r["role"], 17, 22, Color.white);
                    foreach (var n in (r["names"] as JArray) ?? new JArray()) line((string)n, 19, 6, gray);
                }
            }
            // 最後の固まり（開発・販売）。先に高さを測って、前の空きを決める（HTML版は margin-top ＝ 360 ＋ 高さ/2 ＋ 40）
            var E = d["end"] as JObject ?? new JObject();
            var lines = (E["lines"] as JArray) ?? new JArray();
            float eh = 17 * LH;
            for (int i = 0; i < lines.Count; i++) eh += 6 + (i == 0 ? 22 : 16) * LH;
            if (E["copy"] != null) eh += 22 + 14 * LH;
            float endTop = y + Mathf.Round(360 + eh / 2 + 40);
            y = endTop;
            line((string)E["role"], 17, 0, Color.white);
            for (int i = 0; i < lines.Count; i++) line((string)lines[i], i == 0 ? 22 : 16, 6, i == 0 ? Color.white : gray);
            if (E["copy"] != null) line((string)E["copy"], 14, 22, Mats.Hex(0xa8a8a8));
            crStop = 360 - (endTop + eh / 2);                   // 最後の固まりの真ん中が、画面の真ん中に来たら止まる
            crY = 720 + 10; crT = -CRED_WAIT; crInT = 0; crOutT = -1;
            crRoll.anchoredPosition = new Vector2(0, -crY);
            // ボタンは右下の「もどる」だけ
            items.Clear();
            var bk = BBackLike("crBack", 976.5f, Lang.Tr("もどる"), CreditsClose, inn);
            bk.back = true;
            idx = 0; FocusQuiet(0);
        }
        // 止めて設定へ戻る（フワッと消える）
        void CreditsClose()
        {
            if (crRoot == null || crOutT >= 0) return;
            crOutT = 0;
            items.Clear(); idx = 0;
        }
        void CreditsTick(float dt)
        {
            if (crRoot == null) return;
            if (crOutT >= 0)
            {
                crOutT += dt;
                float k = Mathf.Clamp01(crOutT / 0.35f);
                crCg.alpha = 1 - k * k;                           // ease-in
                if (crOutT >= 0.35f) { crRoot = null; BoardDraw("Settings", "credit"); return; }
            }
            else
            {
                crInT += dt;
                float k = Mathf.Clamp01(crInT / 0.5f);
                crCg.alpha = 1 - (1 - k) * (1 - k);               // ease-out
                crT += Mathf.Min(dt, 0.05f);
                if (crT > 0) crY = Mathf.Max(crStop, crY - CRED_SPEED * Mathf.Min(dt, 0.05f));
            }
            crRoll.anchoredPosition = new Vector2(0, -crY);
        }

        // ---------------------------------------------------------------- 実績
        void AchTabStep(int dir)
        {
            int nc = Game.ACH_CATS.Length;
            achTab = (achTab + dir + nc) % nc; achTop = 0; Game.SeUi("cur");
            BoardDraw("Ach", "cat" + achTab);
        }
        List<AchDef> AchListOf(int cat) { var l = new List<AchDef>(); foreach (var a in Game.ACHS) if (a.c == cat) l.Add(a); return l; }
        bool AchScroll(int dy)
        {
            int n = AchListOf(achTab).Count, mx = Mathf.Max(0, n - ACH_VIS);
            int t = Mathf.Clamp(achTop + dy, 0, mx);
            if (t == achTop) return false;
            achTop = t; Game.SeUi("cur");
            AchArrows(mx);
            return true;
        }
        void AchArrows(int mx)
        {
            if (achUp != null) achUp.color = new Color(1, 1, 1, achTop > 0 ? 1 : 0.25f);
            if (achDn != null) achDn.color = new Color(1, 1, 1, achTop < mx ? 1 : 0.25f);
        }
        static string AchDate(long ms)
        {
            var d = System.DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            return d.Year + "/" + d.Month.ToString("00") + "/" + d.Day.ToString("00");
        }

        string AchDraw()
        {
            Game.AchStore.Load();
            var got = Game.AchStore.got;
            var b = cfgB.box;
            int cat = achTab;
            var list = AchListOf(cat);
            int mx = Mathf.Max(0, list.Count - ACH_VIS);
            achTop = Mathf.Clamp(achTop, 0, mx);
            var ttl = UiBoard.Img(b, ACHU + "title.png", 46, 45.5f, 528, 56.5f); cfgB.In(ttl, "slam", 0);
            // 仕分けの札
            for (int i = 0; i < Game.ACH_CATS.Length; i++)
            {
                int ci = i; float y = ACH_CAT_Y[i]; var ic = ACH_IC[i];
                System.Action go = () => { if (achTab != ci) { achTab = ci; achTop = 0; BoardDraw("Ach", "cat" + ci); } };
                // 札を選んで動かしている間は中身を替えて見せる（onFocus）。決定（A・クリック）で一覧へ移る（担当）
                System.Action pick = () => { if (achTab != ci) { achTab = ci; achTop = 0; } BoardDraw("Ach", "list"); };
                var it = BBtn("cat" + i, 53.5f, y, 245, 69, pick, "box");
                it.onFocus = go; it.grp = "cat"; it.on = i == cat;
                it.frameSub = new Rect(-2, -1.5f, 69, 69);
                UiBoard.Img(it.rt, ACHU + "cat.png", 0, 0, 245, 69);
                UiBoard.Img(it.rt, ACHU + "ic" + i + ".png", 32.5f - ic[0] / 2, 33 - ic[1] / 2, ic[0], ic[1]);
                if (i == cat) UiBoard.Img(it.rt, ACHU + "cat_sel.png", -2, -1.5f, 69, 69);
                BT(it.rt, Lang.Tr(Game.ACH_CATS[i]), 82.5f, 34.75f, 20, Color.white, "l", 150);
                cfgB.In(it.rt, "left", 1 + i * 0.25f);
            }
            // 一覧
            UiBoard.Img(b, ACHU + "panel.png", 313.5f, 140, 916.5f, 412.5f);
            var clip = UiBoard.Box(b, 318, 145, 906, 404, "AchList");
            clip.gameObject.AddComponent<RectMask2D>();
            achIn = UiBoard.Box(clip, 0, 0, 906, 404, "In");
            achY = -achTop * ACH_ROW_H; achIn.anchoredPosition = new Vector2(0, -achY);
            for (int k = 0; k < list.Count; k++)
            {
                var a = list[k];
                bool has = got.ContainsKey(a.id), open = !a.tbd && (has || a.s <= 1);
                var row = UiBoard.Box(achIn, 15, 15 + k * ACH_ROW_H, 876, 83.5f, "Row_" + a.id);
                UiBoard.Img(row, ACHU + (has ? "row_on" : "row") + ".png", 0, 0, 876, 83.5f);
                var sts = UiBoard.Img(row, ACHU + "stars.png", 624.5f, 5.5f, 81.5f, 25.5f);
                if (!has) sts.color = new Color(0.5f, 0.5f, 0.5f, 1);
                for (int i = 0; i < a.s; i++)
                {
                    var s = UiBoard.Img(row, ACHU + (has ? "star_on" : "star_dim") + ".png", 626.5f + i * 27.25f, 7.5f, 23.5f, 21.5f);
                    if (!has) s.color = new Color(0.5f, 0.5f, 0.5f, 1);
                }
                BT(row, open ? Lang.Tr(a.n) : "？？？", 47, 18, 20, Mats.Hex(has ? 0x402801 : 0x201400), "l", 560);
                if (has) BT(row, AchDate(got[a.id]), 760, 16, 14, Color.white);
                BT(row, open ? Lang.Tr(a.d) : "？？？", 26, 54.75f, 13, has ? Color.white : Mats.Hex(0x5c5c5c), "l", 800);
            }
            {
                var it = BBtn("list", 318, 145, 906, 404, null, "box");
                it.vadj = AchScroll;
                it.backFn = () => { Game.SeUi("back"); BoardDraw("Ach", "cat" + achTab); };
                it.wheel = dy => AchScroll(dy);
            }
            achUp = UiBoard.Img(b, ACHU + "up.png", 734.5f, 107.5f, 76, 59);
            achDn = UiBoard.Img(b, ACHU + "down.png", 734.5f, 527, 76, 59.5f);
            AchArrows(mx);
            BBack();
            return "cat" + cat;
        }

        // ---------------------------------------------------------------- 毎フレーム（板の画面の入力）
        /// <summary>板の上での上下左右。いま選んでいるボタンの真ん中から、その向きにあるいちばん近いボタンへ（HTML版 boardNav）。
        /// 札の並び（grp）へ入るときは、いま選んである札へ。← → で値を変えるボタンは値を変える（adj）。↑↓で中を送るもの（実績・著作権表示の一覧）は、送れる間は送る（vadj）。
        /// 札から ↓ は、その札の中身の最初のボタンへ（down）</summary>
        void BoardNav(int dx, int dy)
        {
            if (idx >= items.Count) return;
            var cur = items[idx];
            if (dx != 0 && cur.adj != null) { cur.adj(dx); return; }
            if (dy != 0 && cur.vadj != null && cur.vadj(dy)) return;
            if (dy > 0 && cur.down != null)
            {
                int di = items.FindIndex(x => x.key == cur.down);
                if (di >= 0) { Focus(di); return; }
            }
            // 下の段（設定の「CREDITS」「もどる」）から ↑ は、抜ける前に選んでいた中身へ
            if (dy < 0 && cur.upFn != null)
            {
                int ui = items.FindIndex(x => x.key == cur.upFn());
                if (ui >= 0) { Focus(ui); return; }
            }
            if (cur.leave != null) cur.leave();             // 中身から動く（設定：↑ で戻る先として覚える）
            Rect r0 = BoxRect(cur.rt);
            float cx = r0.center.x, cy = r0.center.y;
            int best = -1; float bs = float.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                if (i == idx || items[i].off || items[i].nofocus) continue;
                var r = BoxRect(items[i].rt); var c = r.center;
                float p = (c.x - cx) * dx + (c.y - cy) * dy;
                if (p <= 4) continue;
                // 横（縦）のずれは、ボタンの真ん中ではなくボタンの端まで（幅の広い一覧は、真下にあれば近いとみなす）
                float side = dx != 0 ? Mathf.Max(0, Mathf.Max(r.yMin - cy, cy - r.yMax)) : Mathf.Max(0, Mathf.Max(r.xMin - cx, cx - r.xMax));
                // ← → は、同じ段（高さが重なる物）の中だけで動く（「CREDITS」から → で「もどる」へ。いちばん右から上の札へ跳ばない）
                if (dx != 0 && side > 0) continue;
                float sc = p + 2.5f * side;
                if (sc < bs) { bs = sc; best = i; }
            }
            // ↓ の向きに何も無いとき（いちばん下の「CREDITS」「もどる」）は、同じ段のいちばん近いボタンへ（札の並びへは跳ばない）
            if (best < 0 && dy > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (i == idx || items[i].off || items[i].nofocus || items[i].grp != null) continue;
                    var r = BoxRect(items[i].rt);
                    if (r.yMax < r0.yMin || r.yMin > r0.yMax) continue;
                    float d = Mathf.Abs(r.center.x - cx);
                    if (d < bs) { bs = d; best = i; }
                }
            }
            if (best < 0) return;
            var gp = items[best].grp;
            if (gp != null && cur.grp != gp)
            {
                int on = items.FindIndex(x => x.grp == gp && x.on);
                if (on >= 0) best = on;
            }
            Focus(best);
        }
        /// <summary>板の上の箱（左上が原点、y は下向き。板の px。yMin が上の端）</summary>
        Rect BoxRect(RectTransform r)
        {
            var wc = new Vector3[4]; r.GetWorldCorners(wc);
            Vector2 a = cfgB.box.InverseTransformPoint(wc[1]), b = cfgB.box.InverseTransformPoint(wc[3]);   // 左上・右下
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(-a.y, -b.y), Mathf.Max(a.x, b.x), Mathf.Max(-a.y, -b.y));
        }

        void BoardTick(float dt)
        {
            if (backLater >= 0) { backLater -= dt; if (backLater < 0) { Go("Top"); return; } }
            CreditsTick(dt);
            if (page == null || !IsBoard(page)) return;
            // 著作権表示の一覧の送り（0.15 秒でなめらかに）
            if (licDrag && (Mouse.current == null || !Mouse.current.leftButton.isPressed)) licDrag = false;
            if (licIn != null)
            {
                licY = licDrag ? licTop : Mathf.Lerp(licY, licTop, 1 - Mathf.Exp(-dt * 22));     // 押したまま動かしている間は遅れずに付いてくる
                if (Mathf.Abs(licY - licTop) < 0.3f) licY = licTop;
                licIn.anchoredPosition = new Vector2(0, licY);
            }
            // 一覧の送り（0.18 秒でなめらかに）
            if (achIn != null)
            {
                float to = -achTop * ACH_ROW_H;
                achY = Mathf.Lerp(achY, to, 1 - Mathf.Exp(-dt * 18));
                if (Mathf.Abs(achY - to) < 0.3f) achY = to;
                achIn.anchoredPosition = new Vector2(0, -achY);
                float bob = Mathf.PingPong(Time.unscaledTime / 0.9f, 1) * 5;
                if (achUp != null) achUp.rectTransform.anchoredPosition = new Vector2(734.5f, -107.5f + bob * (achTop > 0 ? 1 : 0));
                if (achDn != null) achDn.rectTransform.anchoredPosition = new Vector2(734.5f, -527 - bob * (achDn.color.a > 0.5f ? 1 : 0));
            }
            cfgB.Tick(dt);
        }

        /// <summary>板の画面のマウス（▲▼・ホイール・棒のドラッグ）。true なら、このフレームの入力はここで使った</summary>
        bool BoardMouse()
        {
            var ms = Mouse.current;
            if (ms == null) return false;
            var mp = ms.position.ReadValue();
            if (holdItem != null)
            {
                if (ms.leftButton.isPressed) { holdItem.hold(mp); return true; }
                holdItem = null; return true;
            }
            if (ms.leftButton.wasPressedThisFrame)
            {
                if (achUp != null && RectTransformUtility.RectangleContainsScreenPoint(achUp.rectTransform, mp, null)) { AchScroll(-1); return true; }
                if (achDn != null && RectTransformUtility.RectangleContainsScreenPoint(achDn.rectTransform, mp, null)) { AchScroll(1); return true; }
                int hi = HitAt(mp);
                if (hi >= 0 && items[hi].hold != null) { Focus(hi, true); holdItem = items[hi]; licDrag = false; holdItem.hold(mp); return true; }
            }
            float wy = ms.scroll.ReadValue().y;
            if (Mathf.Abs(wy) > 0.01f)
            {
                int hi = HitAt(mp);
                if (hi >= 0 && items[hi].wheel != null) { items[hi].wheel(wy > 0 ? -1 : 1); return true; }
            }
            return false;
        }
        Item holdItem;
    }
}
