using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// ユーザー選択（HTML版 11/17。担当の画面案。仮で、画面の作りはあとで変わる）。
    ///   ローカル … 1P〜4P の行に「参加」「NPC」（チーム戦は 青／赤）。つながっているコントローラーごと（Steam 版のためにキーボードにも1つ）に
    ///              同じ指が出て、参加した指には「1P」などの札（席の色）。自分の指で「参加」を押すとその席で遊ぶ
    ///              （もう一度押すとやめる。人の居る席には入れない。ほかの席に居たら移る）。「NPC」はその席を NPC に（もう一度でやめる）。
    ///              B・Esc は、参加していればやめる、していなければ前の画面へ。「スタート」は1人以上参加で押せる（HTML版 LOC・locTick）
    ///   オンライン … 部屋の待機と自動マッチングも同じ行（HTML版 olGrid）。人が居る席・NPC の席は緑の「OK」、空いた席は灰色に回る印と「接続中…」。
    ///              部屋のホストは行の右の「NPC」で空いた席に NPC を入れる（チーム戦は、その右の 青／赤）
    /// </summary>
    public partial class TitleWin
    {
        // ================================================================ ローカル
        static bool LocHuman(string v) { return v == "key" || (v != null && v.Length == 4 && v.StartsWith("pad")); }

        class LocCur
        {
            public string dev; public RectTransform el; public Text tg; public Image tgBg; public Vector2 pos;   // pos … 板の左上からの px
            public bool pOk = true, pBack = true, pL = true, pR = true, pU = true, pD = true;               // 開く前から押していたボタンは効かせない
        }
        readonly List<LocCur> locCur = new List<LocCur>();
        RectTransform locLayer;

        void PageLocal(Stack st)
        {
            bool team = mode == "team";
            Head(st, Lang.Tr("ローカル　{0}", Lang.Tr(Game.ModeName(mode))));
            Note(st, Lang.Tr("コントローラーごとに指が出ます。自分の指で「参加」を押すと、その番号で遊びます（もう一度押すとやめます）。")
                   + (Gamepad.all.Count > 0 ? "" : Lang.Tr("パッドは未接続です（つないでボタンを1回押すと出ます）。")));
            // 1P〜4P の行。番号（席の色）・参加・NPC（・チーム）
            float W = 70 + 16 + 230 + 16 + 230 + (team ? 16 + 130 : 0), x0 = (st.w - W) / 2;
            for (int i = 0; i < 4; i++)
            {
                int ii = i; var v = slots[i]; bool human = LocHuman(v), npc = v != null && v.StartsWith("npc-");
                var row = st.Row(40, i == 0 ? 8 : 10);
                SeatNo(row, x0, i);
                float x = x0 + 70 + 16;
                var jb = BtnAt(row, x, 0, 230, 40, human ? Lang.Tr("参加") + " (" + Lang.Tr(SlotLabel(v)) + ")" : Lang.Tr("参加"), "sec",
                               () => LocJoin(ii, "key"), null, false, "j" + i, 15);
                jb.devFire = d => LocJoin(ii, d);
                if (human) OnRing(jb.rt, 230, 40, Mats.Hex(Player.PCOL[i]));
                x += 230 + 16;
                var nb = BtnAt(row, x, 0, 230, 40, "NPC", "sec", () => LocNpc(ii, "key"), null, false, "n" + i, 15);
                nb.devFire = d => LocNpc(ii, d);
                if (npc) OnRing(nb.rt, 230, 40, Color.white);
                x += 230 + 16;
                if (team && v != "off")
                {
                    int t = teams[i];
                    BtnAt(row, x, 0, 130, 40, Lang.Tr(t == 1 ? "赤" : "青"), t == 1 ? "tm1" : "tm0", () => { teams[ii] = 1 - t; Go("Local"); }, null, false, "t" + i, 15);
                }
            }
            var live = Live();
            int n0 = live.FindAll(s => s.team == 0).Count, n1 = live.FindAll(s => s.team == 1).Count;
            bool oneSide = team && live.Count > 0 && (n0 == 0 || n1 == 0);
            bool anyHuman = System.Array.Exists(slots, LocHuman);
            // ルールと遊び方（横に並べる）
            {
                var r = st.Row(36, 14);
                float bw = 340, gap = 22, bx = (st.w - bw * 2 - gap) / 2;
                BtnAt(r, bx, 0, bw, 34, Lang.Tr("ルール：{0}　▾", Lang.Tr(Game.ModeName(mode))), "sec", () => PickOpen("ルールを選ぶ", null, new List<string[]>
                {
                    new[] { "versus", "個人戦（売上も修理費も個人ごと）", "versus" },
                    new[] { "team", "チーム戦（青と赤。売上も修理費もチームごと）", "team" },
                    new[] { "coop", "協力（売上も修理費も店舗ぜんぶ）", "coop" },
                }, mode, "Local", x => mode = x), null, false, "mode", 14);
                var opts = new List<string[]> { new[] { "0", Game.LenName(0), "free" } };
                foreach (var n in Game.MATCH_LENS) opts.Add(new[] { n.ToString(), Game.LenName(n), "cup" });
                BtnAt(r, bx + bw + gap, 0, bw, 34, Lang.Tr("遊び方：{0}　▾", Game.LenName(rounds)), "sec",
                      () => PickOpen("遊び方を選ぶ", null, opts, rounds.ToString(), "Local", x => rounds = int.Parse(x)), null, false, "len", 14);
            }
            Note(st, !anyHuman ? "「参加」を押した人が居ません。1人以上参加してください。"
                   : oneSide ? "チーム戦は<b>青と赤に1人以上ずつ</b>入れてください（右の 青／赤 を押すと入れ替わります）。"
                   : team ? Lang.Tr("<b>{0}人</b>（{1}）　青 <b>{2}人</b> 対 赤 <b>{3}人</b>で遊びます。次の画面で面を選びます。",
                                    live.Count, string.Join(Lang.Sep, live.ConvertAll(s => Lang.Tr(s.Name)).ToArray()), n0, n1)
                   : Lang.Tr("<b>{0}人</b>（{1}）で遊びます。次の画面で面を選びます。", live.Count, string.Join(Lang.Sep, live.ConvertAll(s => Lang.Tr(s.Name)).ToArray())));
            BtnRow(st, new[] { "キャンセル", "スタート" }, new[] { "sec", "go" },
                   new System.Action[] { () => Go("Len"), () => { from = "Local"; next = false; Go("Free"); } },
                   new[] { false, !anyHuman || oneSide });
        }

        /// <summary>席の番号（席の色。右に寄せる）</summary>
        void SeatNo(RectTransform row, float x, int i)
        {
            var n = UiKit.Label(row, (i + 1) + "P", 28, Mats.Hex(Player.PCOL[i]), true, TextAnchor.MiddleRight);
            var nr = n.rectTransform; nr.anchorMin = nr.anchorMax = new Vector2(0, 1); nr.pivot = new Vector2(0, 1);
            nr.sizeDelta = new Vector2(70, 40); nr.anchoredPosition = new Vector2(x, 0);
            Shade(n, 0.35f);
        }

        /// <summary>選んである印（ボタンの外に、斜めの色の縁）</summary>
        static void OnRing(RectTransform hold, float w, float h, Color c)
        {
            var glow = c; glow.a = 0.45f;
            UiPoly.SkewRing(hold, glow, -6, -6, w + 12, h + 12, -6, 3);
            UiPoly.SkewRing(hold, c, -3, -3, w + 6, h + 6, -6, 4);
        }

        // 参加する・やめる（同じ指で同じ席を押したらやめる。ほかの人が居る席には入れない。ほかの席に居たらこちらへ移る）
        void LocJoin(int i, string dev)
        {
            var v = slots[i];
            if (v == dev) { slots[i] = "off"; Game.SeUi("back"); Go("Local"); return; }
            if (LocHuman(v)) { Game.SeUi("ng"); return; }
            for (int k = 0; k < 4; k++) if (slots[k] == dev) slots[k] = "off";
            slots[i] = dev;
            Game.SeUi("ok");
            Go("Local");
        }
        // その席を NPC にする・やめる（人が居る席は、その人の指でだけ NPC に替えられる）
        void LocNpc(int i, string dev)
        {
            var v = slots[i];
            if (LocHuman(v) && v != dev) { Game.SeUi("ng"); return; }
            slots[i] = v != null && v.StartsWith("npc-") ? "off" : "npc-normal";
            Game.SeUi(slots[i] == "off" ? "back" : "ok");
            Go("Local");
        }

        List<string> LocDevs()
        {
            var o = new List<string> { "key" };
            for (int n = 0; n < Gamepad.all.Count && n < 4; n++) o.Add("pad" + n);
            return o;
        }

        void LocClose()
        {
            foreach (var c in locCur) if (c.el != null) Object.Destroy(c.el.gameObject);
            locCur.Clear();
            if (locLayer != null) { Object.Destroy(locLayer.gameObject); locLayer = null; }
        }

        // つながっているコントローラーに合わせて指を足す・消す（つないだ・抜いた）
        void LocSync()
        {
            if (locLayer == null)
            {
                locLayer = UiKit.Rect(root, "LocFingers");
                locLayer.anchorMin = locLayer.anchorMax = new Vector2(0.5f, 0.5f); locLayer.pivot = new Vector2(0.5f, 0.5f);
                locLayer.sizeDelta = new Vector2(1280, 720);
            }
            locLayer.SetAsLastSibling();
            var devs = LocDevs();
            locCur.RemoveAll(c => { if (devs.Contains(c.dev)) return false; Object.Destroy(c.el.gameObject); return true; });
            for (int n = 0; n < devs.Count; n++)
            {
                string dev = devs[n];
                if (locCur.Exists(c => c.dev == dev)) continue;
                var c = new LocCur { dev = dev };
                c.el = UiBoard.Box(locLayer, 0, 0, 0, 0, "Fing_" + dev);
                var im = UiBoard.Img(c.el, "ui/ptr/normal.png", -24.5f, -4, 62, 69);
                c.tgBg = UiBoard.Fill(c.el, Color.white, 14, 58, 40, 22, 4, "Tg");
                var inner = UiBoard.Fill(c.tgBg.rectTransform, Color.white, 2, 2, 36, 18, 3, "In");
                c.tg = UiBoard.T(inner.rectTransform, "", 0, 1, 15, Color.white, 2);
                c.tgBg.gameObject.SetActive(false);
                Vector2? mp = dev == "key" ? MouseOnBox() : null;
                c.pos = mp ?? new Vector2(1280 * (0.42f + 0.08f * n), 720 * 0.62f);
                locCur.Add(c);
            }
        }

        Vector2? MouseOnBox()
        {
            var ms = Mouse.current;
            if (ms == null || locLayer == null || !Ptr.MouseUser) return null;
            Vector2 lp;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(locLayer, ms.position.ReadValue(), null, out lp)) return null;
            return new Vector2(lp.x + 640, 360 - lp.y);
        }

        Vector2 LocScreen(Vector2 p) { return (Vector2)locLayer.TransformPoint(new Vector3(p.x - 640, 360 - p.y, 0)); }
        Vector2 ItemBox(Item it)
        {
            var wc = new Vector3[4]; it.rt.GetWorldCorners(wc);
            Vector2 lp = locLayer.InverseTransformPoint((wc[0] + wc[2]) / 2);
            return new Vector2(lp.x + 640, 360 - lp.y);
        }
        int LocHit(Vector2 p)
        {
            var sp = LocScreen(p);
            for (int i = items.Count - 1; i >= 0; i--)
                if (!items[i].off && RectTransformUtility.RectangleContainsScreenPoint(items[i].rt, sp, null)) return i;
            return -1;
        }
        // 十字キー：その向きにあるいちばん近いボタンへ跳ぶ（向きから外れている物ほど遠い）
        void LocJump(LocCur c, int dx, int dy)
        {
            int best = -1; float bs = float.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].off) continue;
                var q = ItemBox(items[i]);
                float pr = (q.x - c.pos.x) * dx + (q.y - c.pos.y) * dy;
                if (pr <= 4) continue;
                float sc = pr + 2.5f * Mathf.Abs(dx != 0 ? q.y - c.pos.y : q.x - c.pos.x);
                if (sc < bs) { bs = sc; best = i; }
            }
            if (best >= 0) { c.pos = ItemBox(items[best]); Game.SeUi("cur", 0.6f); }
        }

        void LocTick(float dt)
        {
            LocSync();
            Ptr.NoCursor();                                    // 本物のマウスのカーソルは消す（キーボードの指がマウスの所に出る）
            if (cap != null) cap.text = "";
            if (lockT > 0) lockT -= dt;
            var kb = Keyboard.current; var ms = Mouse.current;
            float sp = Mathf.Max(420, 1280) / 1.15f;           // 画面を 1.15 秒で横断（ほかの指と同じ）
            foreach (var c in locCur)
            {
                bool l, r, u, d, ok, bk; Vector2 ax = Vector2.zero;
                if (c.dev == "key")
                {
                    l = kb != null && kb.leftArrowKey.isPressed; r = kb != null && kb.rightArrowKey.isPressed;
                    u = kb != null && kb.upArrowKey.isPressed; d = kb != null && kb.downArrowKey.isPressed;
                    ok = kb != null && (kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed);
                    bk = kb != null && (kb.escapeKey.isPressed || kb.backspaceKey.isPressed);
                    // マウスはキーボードの指を動かす。押すと、指の下のボタンをキーボードの人として押す
                    if (ms != null && ms.delta.ReadValue().sqrMagnitude > 0.01f) { var mp = MouseOnBox(); if (mp != null) c.pos = mp.Value; }
                    if (ms != null && ms.leftButton.wasPressedThisFrame && lockT <= 0)
                    {
                        var mp = MouseOnBox(); if (mp != null) c.pos = mp.Value;
                        int hi = LocHit(c.pos);
                        if (hi >= 0) { LocPress(items[hi], "key"); return; }
                    }
                }
                else
                {
                    int n = int.Parse(c.dev.Substring(3));
                    var gp = n < Gamepad.all.Count ? Gamepad.all[n] : null;
                    if (gp == null) continue;
                    l = gp.dpad.left.isPressed; r = gp.dpad.right.isPressed; u = gp.dpad.up.isPressed; d = gp.dpad.down.isPressed;
                    ok = gp.buttonSouth.isPressed; bk = gp.buttonEast.isPressed;
                    var s = gp.leftStick.ReadValue();
                    if (Mathf.Abs(s.x) > 0.22f) ax.x = s.x;
                    if (Mathf.Abs(s.y) > 0.22f) ax.y = -s.y;
                }
                if (ax != Vector2.zero) c.pos = new Vector2(Mathf.Clamp(c.pos.x + ax.x * sp * dt, 4, 1276), Mathf.Clamp(c.pos.y + ax.y * sp * dt, 4, 716));
                bool hl = l && !c.pL, hr = r && !c.pR, hu = u && !c.pU, hd = d && !c.pD, hok = ok && !c.pOk, hbk = bk && !c.pBack;
                c.pL = l; c.pR = r; c.pU = u; c.pD = d; c.pOk = ok; c.pBack = bk;
                if (hl) LocJump(c, -1, 0); if (hr) LocJump(c, 1, 0);
                if (hu) LocJump(c, 0, -1); if (hd) LocJump(c, 0, 1);
                if (lockT > 0) continue;
                if (hok) { int hi = LocHit(c.pos); if (hi >= 0) { LocPress(items[hi], c.dev); return; } }
                if (hbk)
                {
                    int at = System.Array.IndexOf(slots, c.dev);
                    if (at >= 0) { slots[at] = "off"; Game.SeUi("back"); Go("Local"); return; }
                    Game.SeUi("back"); Go("Len"); return;
                }
            }
            // 指を置き直す。参加した指には席の札（色も席の色）。指が乗っているボタンは少し明るく
            var hov = new HashSet<int>();
            foreach (var c in locCur)
            {
                c.el.anchoredPosition = new Vector2(c.pos.x, -c.pos.y);
                int at = System.Array.IndexOf(slots, c.dev);
                c.tgBg.gameObject.SetActive(at >= 0);
                if (at >= 0)
                {
                    string s = (at + 1) + "P";
                    if (c.tg.text != s) c.tg.text = s;
                    var col = Mats.Hex(Player.PCOL[at]);
                    ((Image)c.tg.transform.parent.GetComponent<Image>()).color = col;
                    float w = c.tg.preferredWidth + 16;
                    c.tgBg.rectTransform.sizeDelta = new Vector2(w + 4, 22);
                    var ir = (RectTransform)c.tg.transform.parent; ir.sizeDelta = new Vector2(w, 18);
                    c.tg.rectTransform.anchoredPosition = new Vector2(w / 2, -1 + 15 * 0.12f);
                }
                c.el.SetAsLastSibling();
                int hi = LocHit(c.pos);
                if (hi >= 0) hov.Add(hi);
            }
            for (int i = 0; i < items.Count; i++) if (items[i].look != null) items[i].look(hov.Contains(i));
        }

        void LocPress(Item it, string dev)
        {
            if (it.off) return;
            if (it.devFire != null) { it.devFire(dev); return; }
            Game.SeUi(it.back ? "back" : "ok");
            it.fire();
        }

        // ================================================================ オンライン（部屋の待機・自動マッチング）
        readonly List<RectTransform> olSpin = new List<RectTransform>();

        /// <summary>
        /// 1P〜4P の行（HTML版 olGrid）。players … 部屋の人（null なら、自分だけ 1P に居る形。自動マッチングで相手を探している間）。
        /// host … ホスト（空いた席に NPC を入れるボタンを出す）、team … チーム戦（右に 青／赤）、room … 部屋の待機（NPC を見る）
        /// </summary>
        void OlGrid(Stack st, JArray players, int max, bool host, bool team, bool room)
        {
            float W = 70 + 16 + 130 + 16 + 140 + (host ? 16 + 200 : 0) + (team ? 16 + 110 : 0), x0 = (st.w - W) / 2;
            for (int i = 0; i < Mathf.Min(4, max); i++)
            {
                int ii = i;
                var m = players != null ? (i < players.Count ? players[i] as JObject : null) : (i == 0 ? new JObject { ["id"] = Net.Id } : null);
                string v = m == null && room ? (Lobby.rnpc[i] ?? "off") : "off";
                bool bot = m == null && v != "off";
                var row = st.Row(34, i == 0 ? 8 : 7);
                SeatNo(row, x0, i);
                float x = x0 + 70 + 16;
                // 状態の札（緑の「OK」か、灰色に回る印）
                {
                    var bx = UiBoard.Box(row, x, 0, 130, 34, "St" + i);
                    UiPoly.Skew(bx, new Color(0, 0, 0, 0.35f), 0, 3, 130, 34, -6);
                    UiPoly.Skew(bx, Color.white, 0, 0, 130, 34, -6);
                    UiPoly.Skew(bx, m != null || bot ? Mats.Hex(0x10b65a) : Mats.Hex(0x7a7a7a), 3, 3, 124, 28, -6);
                    if (m != null || bot) { var t = UiBoard.T(bx, "OK", 65, 17 - 9, 18, Color.white, 2); Shade(t, 0.3f); }
                    else
                    {
                        var sp = UiBoard.Box(bx, 65, 17, 0, 0, "Spin");
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k / 8f * Mathf.PI * 2;
                            var d = UiBoard.Fill(sp, new Color(1, 0.9f, 0, 0.25f + 0.75f * k / 7f), Mathf.Sin(a) * 9 - 2.2f, -Mathf.Cos(a) * 9 - 2.2f, 4.4f, 4.4f, 2.2f, "D");
                        }
                        olSpin.Add(sp);
                    }
                }
                x += 130 + 16;
                // 右の字（あなた／参加者・ホストの札、NPC の名前、接続中…）
                {
                    bool mHost = m != null && (bool?)m["host"] == true;
                    string s = m != null ? Lang.Tr((int?)m["id"] == Net.Id ? "あなた" : "参加者") + (mHost && room ? "  <color=#ffc44d><size=12>" + Lang.Tr("ホスト") + "</size></color>" : "")
                             : bot ? (host ? "" : Lang.Tr(SlotLabel(v))) : Lang.Tr("接続中…");
                    var t = UiKit.Label(row, s, 16, Color.white, true, TextAnchor.MiddleLeft);
                    var tr = t.rectTransform; tr.anchorMin = tr.anchorMax = new Vector2(0, 1); tr.pivot = new Vector2(0, 1);
                    tr.sizeDelta = new Vector2(140, 34); tr.anchoredPosition = new Vector2(x, 0);
                    Shade(t, 0.35f);
                }
                x += 140 + 16;
                // 空いた席に NPC を入れる（ホストだけ）
                if (host)
                {
                    if (m == null)
                    {
                        var nb = BtnAt(row, x, 0, 200, 34, bot ? Lang.Tr(SlotLabel(v)) : "NPC", "sec",
                                       () => PickOpen(Lang.Tr("{0}P の席", ii + 1), "人が来ない席は<b>NPC</b>で埋められます。あとから人が入ったら、その人が入ります。",
                                                      SeatOpts(), v, "Wait", y => { Lobby.rnpc[ii] = y; Lobby.SendNpc(); }),
                                       bot ? Lang.Tr("{0}P は「{1}」です。押すと変えられます。", ii + 1, Lang.Tr(SlotLabel(v))) : Lang.Tr("{0}P は空きです。押すとNPCを入れられます。", ii + 1),
                                       false, "seat" + i, 14);
                        if (bot) OnRing(nb.rt, 200, 34, Color.white);
                    }
                    x += 200 + 16;
                }
                // チーム（ホストは押して入れ替える）
                if (team && (m != null || bot))
                {
                    int t = Lobby.rteam[i];
                    if (host) BtnAt(row, x, 0, 110, 34, Lang.Tr(t == 1 ? "赤" : "青"), t == 1 ? "tm1" : "tm0", () => { Lobby.rteam[ii] = 1 - t; Lobby.SendRule(); Go("Wait"); }, null, false, "rt" + i, 14);
                    else
                    {
                        var tl = UiKit.Label(row, Game.TEAM_NAME[t], 16, Color.white, true, TextAnchor.MiddleLeft);
                        var tr = tl.rectTransform; tr.anchorMin = tr.anchorMax = new Vector2(0, 1); tr.pivot = new Vector2(0, 1);
                        tr.sizeDelta = new Vector2(110, 34); tr.anchoredPosition = new Vector2(x, 0);
                    }
                }
            }
        }

        void OlSpinTick(float dt)
        {
            olSpin.RemoveAll(s => s == null);
            foreach (var s in olSpin) s.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime / 1.1f * 360);
        }
    }
}
