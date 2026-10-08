using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// 検証用パネル（右に出す。Tab か ⚙ で開け閉め）。JS版の右パネル（#side）を写したもの。
    ///   遊びの調子・カメラ・客の見た目・店舗・今の収支・数値のつまみ・既定として保存
    /// つまみの名前・幅・刻みは JS版の TUNE_SPEC と同じ。
    /// まだ移していない物（仕上げの処理・AO・アイテム・NPC）のつまみは、移したときに足す。
    /// </summary>
    public class SettingsPanel
    {
        readonly Game g;
        readonly RectTransform panel, content;
        readonly List<System.Action> refresh = new List<System.Action>();
        Text ledger, msg, presetInfo, shopName;
        readonly Dictionary<Preset, Image> presetBtn = new Dictionary<Preset, Image>();

        static readonly Color BG = new Color(0.10f, 0.12f, 0.15f, 0.98f);
        static readonly Color SEC = Mats.Hex(0x9fd0ff), NOTE = Mats.Hex(0x8b93a0), INK = Mats.Hex(0xe6e9ee);

        public bool Open { get { return panel.gameObject.activeSelf; } }

        // ---- つまみの表（JS版 TUNE_SPEC と同じ。アイテムの欄は移したら足す）
        struct Spec { public string k, n, sec; public float min, max, step; public int dec; public bool chk; }
        static Spec Sec(string s) { return new Spec { sec = s }; }
        static Spec S(string k, string n, float min, float max, float step, int dec = 0) { return new Spec { k = k, n = n, min = min, max = max, step = step, dec = dec }; }
        static Spec C(string k, string n) { return new Spec { k = k, n = n, chk = true }; }

        static readonly Spec[] TUNE_SPEC =
        {
            Sec("操作フィール"),
            S("thrust","加速力",3,40,0.5f,1), S("linDamp","前後の減衰",0.5f,8,0.1f,1), S("reverseRatio","バック出力比",0.2f,1,0.05f,2),
            S("steerAccel","旋回の立ち上がり",2,34,0.5f,1), S("steerMax","最大旋回速度",1,8,0.1f,1), S("lateralGrip","横グリップ(小=滑る)",0,9,0.1f,1),
            Sec("ジャンプ"),
            S("jumpSpeed","踏み切り速度",0,8,0.1f,1), S("jumpCooldown","連射間隔(秒)",0.1f,1.5f,0.05f,2), S("airControl","空中での効き",0,1,0.05f,2),
            Sec("吹っ飛び（雑な物理演算）"),
            S("knockback","客を飛ばす強さ",0,10,0.1f,1), S("knockUp","客を浮かせる強さ",0,10,0.1f,1),
            Sec("ダメージ係数（衝撃 kg・m/s あたり）"),
            S("hitThreshold","衝撃のしきい値",0,80,1), S("hitCooldown","再ヒット間隔(秒)",0,1,0.05f,2),
            S("botTough","耐久値(大=壊れにくい)",0.25f,12,0.05f,2), S("botDmg","└ Bot損壊率",0,0.2f,0.002f,3),
            S("shopDmg","店舗損壊率",0,0.2f,0.002f,3), S("chairWeight","└ イスの倍率",0,1.5f,0.05f,2), S("chainWeight","└ 連鎖の倍率",0,1.5f,0.05f,2),
            S("dishDmg","料理の乱れ",0,1.5f,0.01f,2),
            S("guestTough","客の丈夫さ(大=倒れにくい)",0.25f,12,0.05f,2), S("guestDmg","└ 客のHP減少",0,2,0.01f,2), S("guestOut","└ 場外へ飛んだとき%",0,100,5),
            Sec("池（水・溶岩）"),
            S("seaPenalty","海から戻るまで 秒(0=終わり)",0,30,1), S("waterDrag","水の抵抗",0,10,0.2f,1),
            S("waterDmg","水のダメージ %/秒",0,20,0.5f,1), S("lavaBurn","溶岩のダメージ %/秒",0,40,0.5f,1),
            Sec("凍りの床・ジャンプ台"),
            S("iceGrip","氷の横グリップ倍率",0,1,0.05f,2), S("iceDrag","氷の減衰倍率",0.1f,1,0.05f,2), S("icePush","氷の加速倍率",0.1f,1,0.05f,2),
            S("rampRise","ジャンプ台の高さ(m)",0.2f,1.6f,0.05f,2), S("wallLow","低い壁の高さ(m)",0.2f,1.5f,0.05f,2),
            S("wallHigh","高い壁の高さ(m)",1,8,0.1f,1), S("wallPhys","壁の見えない高さ(m)",2,40,1),
            Sec("故障とコンボ"),
            S("breakSteer","旋回が壊れる%",10,100,5), S("breakDrive","駆動が壊れる%",10,100,5), S("repairHeal","リペアの回復%",10,100,5),
            S("wreckMin","大暴れが付く連鎖数",2,12,1), S("wreckBack","└ そのときの戻し率",0,1,0.05f,2),
            S("wreckStep","└ 1連鎖ごとの上乗せ",0,0.5f,0.02f,2), S("wreckMax","└ 戻し率の上限",0,1,0.05f,2),
            S("comboWindow","コンボの猶予(秒)",3,40,1), S("comboBonus","コンボ1回の上乗せ",0,1,0.05f,2), S("comboMax","コンボの上限回数",0,12,1),
            Sec("収支"),
            S("startCash","始めの所持金",0,100000,1000), S("repairPerPct","修理費/損壊1%",0,600,10), S("ambulance","救急車1台",0,20000,500),
            C("countAmb","救急車費用を合計に含める（企画書未定）"),
            S("orderCount","オーダー数(R後)",1,12,1), S("repairShare","人数ぶんの修理費割引",0,1,0.05f,2), S("orderAddPer","└ 1人増ごとに+",0,4,1),
        };

        static readonly Spec[] CAM_SPEC =
        {
            S("elev","俯角（度）",15,88,1), S("azim","方位角（度）",-60,60,1), S("dist","距離（m）",8,60,0.5f,1), S("fov","画角（度）",16,70,1),
            C("autoFit","部屋に合わせて自動で引く"),
            S("fitPad","└ 左右と下の余白",0,0.35f,0.01f,2), S("fitTop","└ 上の余白（帯のぶん）",0,0.35f,0.01f,2),
            C("fitWalls","└ 高い壁の上まで収める"), C("fitRoomWalls","└ まわりの壁も収める（R で反映）"),
            S("fitHead","└ 床の上を収める高さ(m)",0,6,0.1f,1),
            C("shadow","影を出す（奥行きの手がかり）"),
        };

        public SettingsPanel(RectTransform root, Game game)
        {
            g = game;
            EnsureEventSystem();
            // ---- 右の板（全部の高さ）。中はスクロール
            var p = UiKit.Img(root, BG, 0, "Settings");
            panel = p.rectTransform;
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1); panel.pivot = new Vector2(1, 0.5f);
            panel.sizeDelta = new Vector2(360, 0); panel.anchoredPosition = Vector2.zero;
            p.raycastTarget = true;
            var sr = panel.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;
            var vp = UiKit.Rect(panel, "Viewport"); UiKit.Stretch(vp, 0);
            vp.gameObject.AddComponent<RectMask2D>();
            var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0); vpImg.raycastTarget = true;
            content = UiKit.Rect(vp, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var v = UiKit.VCol(content.gameObject, 4, UiKit.Pad(14, 14, 12, 18));
            v.childForceExpandWidth = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = content;

            var title = Label("ネコ配（仮）", 18, Color.white);
            Label("Unity 版 / クォータービュー検証用（Tab で開け閉め）", 11, NOTE, false);

            // ---- 遊びの調子
            Section("遊びの調子（まとめて切り替え）");
            var row = HRow();
            foreach (Preset pr in System.Enum.GetValues(typeof(Preset)))
            {
                var pp = pr;
                presetBtn[pr] = Button(row, pr.ToString(), () => { g.SetPreset(pp); Toast(pp + "の調子にしました"); }).GetComponent<Image>();
            }
            presetInfo = Label("", 11, NOTE, false);
            Label("普通・爽快は固定値、カスタムは保存した自分の数値（HTML版の tune.json）です。カメラと客の見た目は切り替えても動きません。", 11, NOTE, false);

            // ---- カメラ
            Section("カメラ（見え方の検証）");
            foreach (var s in CAM_SPEC) Control(s, g.cam, k =>
            {
                if (k == "dist" && g.cam.autoFit) g.cam.autoFit = false;          // 手で動かしたら自動を切る
                if (k == "autoFit" || k.StartsWith("fit")) g.FitCamera();
                g.ApplyShadows();
            });
            Section("客の見た目（R で反映）");
            Control(S("heads", "客の頭身", 2, 4, 0.1f, 1), g.TF, null);
            Control(S("bodyTint", "機体に乗せる色の濃さ", 0, 1, 0.05f, 2), g.TF, null);

            // ---- 店舗
            Section("店舗（種類＝単価・修理費・摩擦）");
            var srow = HRow();
            Button(srow, "◀", () => g.CycleShop(-1));
            shopName = UiKit.Label(srow, "", 13, INK, true, TextAnchor.MiddleCenter);
            UiKit.Size(shopName, -1, 26, 1);
            Button(srow, "▶", () => g.CycleShop(1));
            var row2 = HRow();
            Button(row2, "面を選ぶ（Esc）", () => g.OpenStageMenu());
            Button(row2, "やり直し（R）", () => g.Rebuild());

            // ---- 今の収支
            Section("今の収支（ライブ検算）");
            ledger = Label("", 12, INK, false);

            // ---- 数値のつまみ
            foreach (var s in TUNE_SPEC)
            {
                if (s.sec != null) { Section(s.sec); continue; }
                Control(s, g.T, k => g.preset = (Preset)(-1));     // 動かしたら、どの調子でもない
            }

            // ---- 既定として保存
            Section("設定（数値の既定値）");
            Button(HRow(), "この数値を既定として保存", Save);
            Button(HRow(), "この数値をJSONでコピー", () => { GUIUtility.systemCopyBuffer = g.TuneJson().ToString(); Toast("コピーしました"); });
            msg = Label("", 11, Mats.Hex(0x7be39a), false);
            Label("既定値は HTML版の nekorobo3d/tune.json＝上の「カスタム」です。HTML版と同じファイルなので、" +
                  "ここで保存すると HTML版にも効きます。Unity がまだ知らない項目（仕上げの処理など）はそのまま残します。", 11, NOTE, false);

            panel.gameObject.SetActive(false);
        }

        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ------------------------------------------------------------ 部品
        Text Label(string s, float size, Color c, bool bold = true)
        {
            var t = UiKit.Label(content, s, size, c, bold, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        void Section(string s)
        {
            var t = Label(s, 13, SEC);
            var le = UiKit.Size(t, -1, 26);
            t.alignment = TextAnchor.LowerLeft;
        }

        RectTransform HRow()
        {
            var r = UiKit.Rect(content, "Row");
            var h = UiKit.HRow(r.gameObject, 6);
            h.childForceExpandWidth = true;
            UiKit.Size(r, -1, 28);
            return r;
        }

        GameObject Button(Transform parent, string label, System.Action onClick)
        {
            var im = UiKit.Img(parent, Mats.Hex(0x39404c), 6, "Btn");
            im.raycastTarget = true;
            var b = im.gameObject.AddComponent<Button>();
            b.targetGraphic = im;
            var cb = b.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = cb;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => { onClick(); EventSystem.current.SetSelectedGameObject(null); });
            var t = UiKit.Label(im.transform, label, 12, INK, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(t.rectTransform);
            UiKit.Size(im, -1, 26, 1);
            return im.gameObject;
        }

        /// <summary>つまみ（スライダー）か、チェック。obj のフィールドを直接書き換える（JS版 mkSliders）。</summary>
        void Control(Spec s, object obj, System.Action<string> onChange)
        {
            var f = obj.GetType().GetField(s.k, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return;
            var r = UiKit.Rect(content, "Ctl_" + s.k);
            UiKit.HRow(r.gameObject, 6);
            UiKit.Size(r, -1, 24);
            if (s.chk)
            {
                var box = UiKit.Img(r, Mats.Hex(0x2b313c), 3, "Box");
                box.raycastTarget = true;
                UiKit.Size(box, 18, 18);
                var mark = UiKit.Img(box.transform, Mats.Hex(0x4aa8e0), 2, "Mark");
                UiKit.Stretch(mark.rectTransform, 3);
                var tg = box.gameObject.AddComponent<Toggle>();
                tg.targetGraphic = box; tg.graphic = mark;
                tg.navigation = new Navigation { mode = Navigation.Mode.None };
                tg.isOn = (bool)f.GetValue(obj);
                tg.onValueChanged.AddListener(on => { f.SetValue(obj, on); if (onChange != null) onChange(s.k); });
                var lb = UiKit.Label(r, s.n, 12, INK, false);
                UiKit.Size(lb, -1, -1, 1);
                refresh.Add(() => tg.SetIsOnWithoutNotify((bool)f.GetValue(obj)));
                return;
            }
            var lbl = UiKit.Label(r, s.n, 11, INK, false);
            UiKit.Size(lbl, 138, -1);
            lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
            // スライダー（地・中身・つまみ）
            var sl = UiKit.Rect(r, "Slider");
            UiKit.Size(sl, -1, 16, 1);
            var bg = UiKit.Img(sl, Mats.Hex(0x2b313c), 4, "Bg"); UiKit.Stretch(bg.rectTransform);
            bg.rectTransform.offsetMin = new Vector2(0, 5); bg.rectTransform.offsetMax = new Vector2(0, -5);
            var fa = UiKit.Rect(sl, "FillArea"); UiKit.Stretch(fa); fa.offsetMin = new Vector2(0, 5); fa.offsetMax = new Vector2(0, -5);
            var fill = UiKit.Img(fa, Mats.Hex(0x4aa8e0), 4, "Fill"); fill.rectTransform.sizeDelta = Vector2.zero;
            var ha = UiKit.Rect(sl, "HandleArea"); UiKit.Stretch(ha); ha.offsetMin = new Vector2(6, 0); ha.offsetMax = new Vector2(-6, 0);
            var handle = UiKit.Img(ha, Color.white, 7, "Handle"); handle.rectTransform.sizeDelta = new Vector2(12, 0);
            handle.raycastTarget = true;
            var slider = sl.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            // 刻みは step（整数の段に直して持つ）
            int steps = Mathf.Max(1, Mathf.RoundToInt((s.max - s.min) / s.step));
            slider.minValue = 0; slider.maxValue = steps; slider.wholeNumbers = true;
            var val = UiKit.Label(r, "", 11, INK, true, TextAnchor.MiddleRight);
            UiKit.Size(val, 46, -1);
            System.Func<float> get = () => System.Convert.ToSingle(f.GetValue(obj));
            System.Action show = () => val.text = s.dec > 0 ? get().ToString("F" + s.dec) : Mathf.Round(get()).ToString();
            slider.SetValueWithoutNotify(Mathf.Round((get() - s.min) / s.step));
            show();
            slider.onValueChanged.AddListener(x =>
            {
                float v = s.min + x * s.step;
                v = (float)System.Math.Round(v, 6);
                f.SetValue(obj, v);
                show();
                if (onChange != null) onChange(s.k);
            });
            refresh.Add(() => { slider.SetValueWithoutNotify(Mathf.Round((get() - s.min) / s.step)); show(); });
        }

        void Toast(string s) { if (msg != null) msg.text = s; }

        void Save()
        {
            string path;
            string err = g.SaveTune(out path);
            Toast(err == null ? "保存しました → " + path : "保存できません: " + err);
            if (err == null) g.preset = Preset.カスタム;
        }

        // ------------------------------------------------------------ 開け閉め・毎フレーム
        public void Toggle()
        {
            panel.gameObject.SetActive(!Open);
            if (Open) Refresh();
        }

        /// <summary>つまみを今の値に合わせ直す（調子を変えた・面を組み直した）。</summary>
        public void Refresh()
        {
            foreach (var a in refresh) a();
        }

        public void Tick()
        {
            if (!Open) return;
            var L = g.LedgerOf(g.me);
            ledger.text = "売上　" + Game.Yen(L.sales)
                        + (L.wreck > 0 ? "\n大暴れ " + L.best + "連鎖　" + Game.Yen(L.wreck) : "")
                        + "\n店舗損壊 " + Mathf.RoundToInt(g.shopDmg) + "%　" + Game.Yen(-L.repair)
                        + "\n救急車 " + L.amb + "台　" + (g.T.countAmb ? Game.Yen(-L.ambCost) : "—")
                        + "\n<b>合計　" + Game.Yen(L.total) + "</b>"
                        + "\nBot損壊　" + (g.me != null ? Mathf.RoundToInt(g.me.botDmg) : 0) + "%";
            shopName.text = g.shop.n + "（" + g.shop.fricLabel + "）";
            foreach (var kv in presetBtn) kv.Value.color = kv.Key == g.preset ? Mats.Hex(0x2f80b8) : Mats.Hex(0x39404c);
            presetInfo.text = g.preset == Preset.普通 ? "コードに書いてある内蔵の数値。企画書の想定に近い、詰めて遊ぶ調子です。"
                            : g.preset == Preset.爽快 ? "速くて壊れにくく、客がよく飛びます。見ている人も楽しい大味な調子です。"
                            : g.preset == Preset.カスタム ? "保存してある自分の数値（tune.json）。つまみで作って「既定として保存」を押すとここに入ります。"
                            : "つまみを動かしたので、どの調子でもない設定になっています（残すなら「既定として保存」）。";
        }
    }
}
