using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// タイトルとメニュー。JS版の #title（TITLE_PAGE の Top / Solo / Story / Free / Multi / Rule / Local / Pick / Debug）を写したもの。
    ///
    ///   タイトル ─ 1人プレイ ─ ストーリー（続きから／初めから）
    ///            │           └ フリープレイ（面を選ぶ）
    ///            ├ マルチプレイ ─ ローカル（ルール → 1P〜4Pの枠 → 面を選ぶ）
    ///            │              └ オンライン ─ ルームを作る（ルール → 待機）／ルームに参加（一覧か番号 → 待機）
    ///            │                             待機：ルームID・面（ホストが選ぶ）・ルール・席（空きはNPC）・始める
    ///            └ デバッグ
    ///
    /// カーソルは JS版の MENU と同じ動き：並び順に1つずつ（端で折り返さない）、面の一覧の中だけ ↑↓ で1行ぶん。
    /// 決定は Enter・スペース・パッドの A、戻るは Esc・Backspace・パッドの B。マウスは触ると選び、押すと決定。
    /// 位置と大きさは 1280×720 の画面で CSS と同じ数値（vh はここで px に直してある）。
    /// </summary>
    public partial class TitleWin
    {
        readonly Game g;
        readonly RectTransform root;
        RectTransform box;
        public bool Open { get; private set; }

        // ---- タイトルの状態（JS版 TITLE）
        public string page = "Top";
        public string[] slots = { "key", "off", "off", "off" };
        public string mode = "versus";
        public int[] teams = { 0, 1, 0, 1 };
        public int pick;                      // フリープレイで選んでいる面
        public string from = "Solo";          // フリープレイへ来た元（Solo / Local）
        public bool next;                     // 買い物のあとの「次の面選び」か
        Picker picker;

        class Picker { public string title, note, cur, back; public bool confirm; public List<string[]> opts; public System.Action<string> onPick; }
        // 文字を入れる画面（ルームID・サーバ）。パッドだけでは打てないので、キーボード前提（JS版 titleAsk）
        class Ask { public string title, note, value, back; public bool digits; public System.Action<string> onOk; public InputField inp; }
        Ask ask;

        // ---- カーソル
        class Item
        {
            public RectTransform rt; public System.Action fire; public bool back, off; public string desc, key;
            public System.Action<bool> look;            // 選んでいる見た目に切り替える
            public RectTransform anim; public float bob, scale = 1; public int grid = -1;
        }
        readonly List<Item> items = new List<Item>();
        int idx;
        float lockT; bool armed;
        bool pUp, pDown, pLeft, pRight, pOk, pBack;
        Text cap;
        Text freeDesc;
        readonly List<Item> gridItems = new List<Item>();

        const int FREE_SLOTS = 31;
        static readonly Color GOLD = Mats.Hex(0xf2b52c), GOLD_D = Mats.Hex(0xd18f10), INK = Mats.Hex(0x3a352c), SUB = Mats.Hex(0x8a8274);
        // 板の色（tcL / tcD / tcS）
        static readonly Dictionary<string, int[]> TC = new Dictionary<string, int[]>
        {
            { "red", new[] { 0xf0483c, 0xc01a12, 0x7d0f0a } }, { "blue", new[] { 0x3aa3ee, 0x1160bd, 0x0a3f80 } },
            { "green", new[] { 0x4cc463, 0x158c31, 0x0c5d21 } }, { "gold", new[] { 0xf7bf2e, 0xd68c06, 0x8f5c02 } },
            { "grey", new[] { 0xb9c0c9, 0x8b939d, 0x626a74 } },
        };
        static readonly string[] SEAT_COL = { "blue", "red", "green", "gold" };
        static readonly string[][] SLOT_OPTS =
        {
            new[] { "off", "なし" }, new[] { "key", "キーボード" },
            new[] { "pad0", "パッド1" }, new[] { "pad1", "パッド2" }, new[] { "pad2", "パッド3" }, new[] { "pad3", "パッド4" },
            new[] { "npc-serious", "NPC・まじめ" }, new[] { "npc-normal", "NPC・普通" },
            new[] { "npc-wild", "NPC・暴走" }, new[] { "npc-easy", "NPC・かんたん" },
        };

        public TitleWin(RectTransform parent, Game game)
        {
            g = game;
            root = UiKit.Rect(parent, "Title");
            UiKit.Stretch(root);
            var bg = root.gameObject.AddComponent<RawImage>();
            bg.texture = BgTex(); bg.raycastTarget = true;
            root.gameObject.SetActive(false);
            NetHook();
        }

        // ---- サーバから何か来たら、開いている画面を描き直す（JS版 NETMOD.on(...)）
        static bool NetPage(string p) { return p == "Online" || p == "Make" || p == "Join" || p == "Wait"; }
        void NetHook()
        {
            // 描き直しはその場でせず、次のコマで（画面を組んでいる途中に知らせが来ても、組み直さない）
            Net.OnState += () => { if (Open && NetPage(page)) GoLater(page); };
            Net.OnRoom += m =>
            {
                // 入ってきた人にも、いま選んでいる面と空き席のNPCとルールを教える
                Lobby.SendPick(); Lobby.SendNpc(); Lobby.SendRule();
                if (Open) GoLater(Net.Room != null ? "Wait" : "Online");
            };
            Net.OnFrom += (id, d) =>
            {
                string p = (string)d["p"];
                if (p == "pick") Lobby.rpick = d["pick"] as Newtonsoft.Json.Linq.JObject;
                else if (p == "npc" && d["npc"] is Newtonsoft.Json.Linq.JArray na)
                    for (int i = 0; i < 4; i++) Lobby.rnpc[i] = i < na.Count ? (string)na[i] ?? "off" : "off";
                else if (p == "rule")
                {
                    string md = (string)d["mode"];
                    if (md == "versus" || md == "coop" || md == "team") Lobby.mode = md;
                    if (d["team"] is Newtonsoft.Json.Linq.JArray ta)
                        for (int i = 0; i < 4 && i < ta.Count; i++) Lobby.rteam[i] = ((int?)ta[i] ?? 0) != 0 ? 1 : 0;
                }
                else return;
                if (Open && page == "Wait") GoLater("Wait");
            };
            Net.OnRooms += a => { Lobby.rooms = a; if (Open && page == "Join") GoLater("Join"); };
            Net.OnErr += m => { if (Open && (page == "Join" || page == "Online")) GoLater(page); };
        }
        string goLater;
        void GoLater(string pg) { goLater = pg; }

        public void OpenPage(string pg)
        {
            Open = true;
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            Go(pg ?? "Top");
        }

        public void Close() { Open = false; root.gameObject.SetActive(false); }

        // ================================================================ ページ
        void Go(string pg)
        {
            goLater = null;
            if (pg == "Make" && Net.Room != null) pg = "Wait";            // 作れていれば待機へ
            // 同じ画面の描き直し（部屋サーバーの知らせ）なら、カーソルを同じボタンへ戻す
            bool redraw = pg == page && Open && items.Count > 0;
            string keepKey = redraw && idx < items.Count ? items[idx].key : null;
            page = pg;
            if (box != null) Object.Destroy(box.gameObject);
            items.Clear(); gridItems.Clear(); freeDesc = null;
            box = UiKit.Rect(root, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f); box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(1280, 720);
            var st = new Stack(box, pg == "Free" ? 1159 : 1098);
            Logo(st);
            string def = null;
            switch (pg)
            {
                case "Top": PageTop(st); break;
                case "Solo": PageSolo(st); break;
                case "Story": PageStory(st); break;
                case "Free": def = PageFree(st); break;
                case "Multi": PageMulti(st); break;
                case "Rule": def = PageRule(st); break;
                case "Local": PageLocal(st); break;
                case "Pick": def = PagePick(st); break;
                case "Debug": PageDebug(st); break;
                case "Online": PageOnline(st); break;
                case "Make": PageMake(st); break;
                case "Join": PageJoin(st); break;
                case "Wait": PageWait(st); break;
                case "Ask": def = PageAsk(st); break;
                default: PageTop(st); break;
            }
            // 選んでいるものの説明（下の帯）と、操作の案内
            st.Gap(10);
            cap = st.Text("", 14, Color.white, TextAnchor.MiddleCenter, 20);
            Shade(cap, 0.6f);
            st.Gap(16);
            var ft = st.Text(pg == "Ask" ? "文字を入れて Enter で決定　Esc でやめる" : "↑↓←→ で選ぶ　Enter・スペース・A で決定　Esc・B で戻る",
                             12, new Color(1, 1, 1, 0.92f), TextAnchor.MiddleCenter, 20);
            Shade(ft, 0.5f);
            st.Finish();
            // カーソル（開いたときは少しのあいだ決定を受け付けない。描き直しは待ちを引き継ぐ）
            if (!redraw) { lockT = g.T.menuLock; armed = false; }
            if (keepKey != null) def = keepKey;
            int d = def != null ? items.FindIndex(x => x.key == def) : -1;
            if (d < 0) d = items.FindIndex(x => !x.off);
            Focus(Mathf.Max(0, d));
        }

        void PageTop(Stack st)
        {
            Tiles(st,
                Tile("1人プレイ", "red", "solo", () => Go("Solo"), "ストーリーと、好きな面で遊ぶフリープレイ。", "solo"),
                Tile("マルチプレイ", "blue", "multi", () => Go("Multi"), "1台で最大4人、または通信で。個人戦・チーム戦・協力。", "multi"),
                Tile("デバッグ", "grey", "debug", () => Go("Debug"), "ステージを作る・コースの並びを変える（開発用）。", "debug"));
        }

        void PageSolo(Stack st)
        {
            Head(st, "1人プレイ");
            Tiles(st,
                Tile("ストーリー", "red", "story", () => Go("Story"), "屋台の店主に教わりながら進む12面。遊び方を覚えるほうです。", "story"),
                Tile("フリープレイ", "blue", "free", () => { from = "Solo"; next = false; Go("Free"); }, "好きな面を選んで遊ぶほうです。", "free"));
            Btn(st, "戻る", "sec", () => Go("Top"), null, true);
        }

        void PageStory(Stack st)
        {
            var sv = g.LoadStory();
            int total = g.course != null ? g.course.stages.Count : 0;
            bool ok = sv != null && (int)sv["stage"] < total;
            Head(st, "ストーリー");
            Note(st, "屋台の店主に教わりながら進む<b>1人用</b>のチュートリアルです。\nいまのコース：<b>" + (g.course != null ? g.course.n : "") + "</b>（" + total + "ステージ）。"
                   + "並びは HTML版の「デバッグ → ステージの並び」で変えられます。");
            Tiles(st,
                Tile("続きから", "blue", "cont", () => Start(() => g.StartStory(true)),
                     ok ? "STAGE " + ((int)sv["stage"] + 1) + " / " + total + " から続けます。お金・強化もそのままです。"
                        : "途中の記録がありません。「初めから」を選んでください。", "cont", !ok),
                Tile("初めから", "red", "fresh", () => Start(() => g.StartStory(false)), "STAGE 1 から始めます。途中の記録は上書きされます。", "new"));
            Btn(st, "戻る", "sec", () => Go("Solo"), null, true);
        }

        void PageMulti(Stack st)
        {
            Head(st, "マルチプレイ");
            Tiles(st,
                Tile("ローカル", "green", "local", () => { Lobby.ruleTo = "Local"; Go("Rule"); }, "1台の画面で最大4人。枠ごとにキーボード・パッド・NPCを決めます。", "local"),
                Tile("オンライン", "gold", "online", () => Go("Online"), "通信で遊びます。ルールと面を選ぶのはホストです。", "online"));
            Note(st, "マルチは<b>フリープレイだけ</b>です（ストーリーは1人用）。");
            Btn(st, "戻る", "sec", () => Go("Top"), null, true);
        }

        string PageRule(Stack st)
        {
            bool toMake = Lobby.ruleTo == "Make";
            System.Action<string> pk = m =>
            {
                if (toMake)
                {
                    Lobby.mode = m;
                    for (int i = 0; i < 4; i++) { Lobby.rnpc[i] = "off"; Lobby.rteam[i] = i % 2; }
                    Net.CreateRoom(Lobby.rname, Lobby.rpriv);
                    Go("Make");
                }
                else { mode = m; Go("Local"); }
            };
            Head(st, "ルールを選ぶ");
            Tiles(st,
                Tile("個人戦", "red", "versus", () => pk("versus"), "売上も修理費も強化も、ひとりずつ。いちばん稼いだ人の勝ち。", "versus"),
                Tile("チーム戦", "blue", "team", () => pk("team"), "青と赤の2チームで競います。お金と強化はチームでひとつ。人数はそろえなくても遊べます。", "team"),
                Tile("協力", "green", "coop", () => pk("coop"), "全員でお店ひとつ。みんなで利益を上げます。", "coop"));
            Btn(st, "戻る", "sec", () => Go(toMake ? "Online" : "Multi"), null, true);
            return toMake ? Lobby.mode : mode;
        }

        void PageLocal(Stack st)
        {
            bool team = mode == "team";
            int pads = Gamepad.all.Count;
            Head(st, "ローカル　" + Game.ModeName(mode));
            Note(st, "枠を押すと、キーボード・パッド・NPCを選べます。"
                   + (pads > 0 ? "パッドは <b>" + pads + "台</b> つながっています。" : "パッドは未接続です（つないでボタンを1回押すと出ます）。"));
            // 1P〜4Pの札（色は足元のリングと同じ並び）
            var row = st.Row(92);
            float sw = (1098 - 3 * 10) / 4f;
            for (int i = 0; i < 4; i++)
            {
                int ii = i; var v = slots[i];
                string col = v == "off" ? "grey" : team ? (teams[i] == 1 ? "red" : "blue") : SEAT_COL[i];
                Seat(row, i * (sw + 10), sw, (i + 1) + "P" + (team && v != "off" ? "・" + Game.TEAM_NAME[teams[i]] : ""),
                     SlotIc(v), SlotLabel(v) + PadHint(v), col,
                     () => PickOpen((ii + 1) + "P を選ぶ", "同じパッドを2人に割り当てたときは、あとの枠が外れます。",
                                    SlotOptsWithHint(), v, "Local", x => slots[ii] = x),
                     (i + 1) + "P は「" + SlotLabel(v) + "」です。押すと、キーボード・パッド・NPCから選べます。", "s" + i);
            }
            var live = Live();
            int n0 = live.FindAll(s => s.team == 0).Count, n1 = live.FindAll(s => s.team == 1).Count;
            bool oneSide = team && live.Count > 0 && (n0 == 0 || n1 == 0);
            // チーム分け。使う枠だけ並べる。押すと青と赤が入れ替わる
            if (team)
            {
                var bs = new List<System.Action<RectTransform, float, float>>();
                var tb = new List<int>();
                for (int i = 0; i < 4; i++) if (slots[i] != "off") tb.Add(i);
                if (tb.Count > 0)
                {
                    var r2 = st.Row(36);
                    float bw = 160, gap = 13, x0 = (1098 - (tb.Count * bw + (tb.Count - 1) * gap)) / 2;
                    for (int k = 0; k < tb.Count; k++)
                    {
                        int i = tb[k], t = teams[i];
                        BtnAt(r2, x0 + k * (bw + gap), 0, bw, 34, (i + 1) + "P：" + (t == 1 ? "赤" : "青"), t == 1 ? "tm1" : "tm0",
                              () => { teams[i] = 1 - t; Go("Local"); }, null, false, "t" + i, 14);
                    }
                }
            }
            Btn(st, "ルール：" + Game.ModeName(mode) + "　▾", "sec", () => PickOpen("ルールを選ぶ", null, new List<string[]>
            {
                new[] { "versus", "個人戦（売上も修理費も個人ごと）", "versus" },
                new[] { "team", "チーム戦（青と赤。売上も修理費もチームごと）", "team" },
                new[] { "coop", "協力（売上も修理費も店舗ぜんぶ）", "coop" },
            }, mode, "Local", x => mode = x), null, false, "mode");
            Note(st, live.Count == 0 ? "枠が全部「なし」です。1つ以上選んでください。"
                   : oneSide ? "チーム戦は<b>青と赤に1人以上ずつ</b>入れてください（上の 1P：青 などを押すと入れ替わります）。"
                   : "<b>" + live.Count + "人</b>（" + string.Join("・", live.ConvertAll(s => s.Name).ToArray()) + "）"
                     + (team ? "　青 <b>" + n0 + "人</b> 対 赤 <b>" + n1 + "人</b>" : "") + "で遊びます。次の画面で面を選びます。");
            BtnRow(st, new[] { "面を選ぶ ▶", "戻る" }, new[] { "go", "sec" },
                   new System.Action[] { () => { from = "Local"; next = false; Go("Free"); }, () => Go("Rule") },
                   new[] { live.Count == 0 || oneSide, false });
        }

        string PagePick(Stack st)
        {
            var P = picker;
            if (P == null) { PageTop(st); return null; }
            Head(st, P.title);
            if (P.note != null) Note(st, P.note);
            int cols = P.opts.Count > 8 ? 4 : P.opts.Count > 4 ? 3 : 2;
            int rows = (P.opts.Count + cols - 1) / cols;
            const float gap = 8, bh = 64;
            float bw = (1098 - (cols - 1) * gap) / cols;
            var grid = st.Row(rows * bh + (rows - 1) * gap);
            string def = null;
            for (int k = 0; k < P.opts.Count; k++)
            {
                var o = P.opts[k];
                int c = k % cols, r = k / cols;
                string v = o[0];
                bool cur = v == P.cur;
                var it = BtnAt(grid, c * (bw + gap), r * (bh + gap), bw, bh, "", cur ? "pk" : "dark", () => { P.onPick(v); if (!P.confirm) Go(P.back); }, null, false, "o" + k, 14);
                it.grid = cols;
                // 絵を上に大きく、名前を下に。いま選ばれている物には金の札
                var holder = it.rt;
                var lb = holder.Find("Lb").GetComponent<Text>();
                lb.text = o[1];
                lb.rectTransform.anchoredPosition = new Vector2(0, o.Length > 2 && o[2] != null ? -10 : 0);
                if (o.Length > 2 && o[2] != null) Icon(holder, o[2], 26, new Vector2(0, 12), 1f);
                if (cur)
                {
                    def = it.key;
                    var tag = UiKit.Img(holder, GOLD, 8, "Cur");
                    var tr = tag.rectTransform; tr.anchorMin = tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(1, 1);
                    tr.sizeDelta = new Vector2(36, 16); tr.anchoredPosition = new Vector2(-12, -5);
                    var tt = UiKit.Label(tag.transform, "いま", 10, Mats.Hex(0x4a3406), true, TextAnchor.MiddleCenter);
                    UiKit.Stretch(tt.rectTransform);
                }
            }
            Btn(st, "戻る", "sec", () => Go(P.back), null, true);
            return def;
        }

        void PageDebug(Stack st)
        {
            bool itemsOn = PlayerPrefs.GetString(Game.ITEMS_KEY, "") == "all";
            Head(st, "デバッグ");
            Tiles(st,
                Tile("ステージエディタ", "blue", "edit", () => Application.OpenURL("http://localhost:8123/editor.html"),
                     "HTML版のエディタをブラウザで開きます（面を作る・直す。HTML版のサーバーが動いているとき）。", "ed"),
                Tile("ステージの並び", "green", "free", () => Application.OpenURL("http://localhost:8123/debug.html"),
                     "HTML版の「ステージの並び」をブラウザで開きます（コースとフリープレイの並び・説明）。", "order"),
                Tile("ストーリーの記録を消す", "grey", "trash", () => { g.ClearStory(); g.Toast("ストーリーの記録を消しました"); Go("Debug"); },
                     "途中まで進んだ記録を消します。", "clr"));
            Btn(st, "最初からアイテムを持つ：" + (itemsOn ? "オン" : "オフ") + "　▾", "sec", () =>
            {
                if (itemsOn) PlayerPrefs.DeleteKey(Game.ITEMS_KEY); else PlayerPrefs.SetString(Game.ITEMS_KEY, "all");
                PlayerPrefs.Save();
                g.ResetWallets();
                Go("Debug");
            }, "オンにすると、全部のアイテムを上限まで持った状態で始まります（この端末で覚えます）。", false, "items");
            Btn(st, "戻る", "sec", () => Go("Top"), null, true);
        }

        // ---- フリープレイ（面を選ぶ）
        class FreeE { public StageCfg c; public string n, desc, shop, refKey; public bool missing; }
        List<FreeE> flist;

        List<FreeE> FreeList()
        {
            var o = new List<FreeE>();
            var fr = Course.Load("free");
            if (fr.stages.Count > 0)
            {
                foreach (var e in fr.stages)
                {
                    var c = StageCfg.Ref(e.cfg, true);
                    string nm = e.cfg != null ? (e.cfg.StartsWith("file:") ? e.cfg.Substring(5) : e.cfg.StartsWith("builtin:") ? e.cfg.Substring(8) : e.cfg) : "";
                    o.Add(new FreeE { c = c, n = c != null ? c.n : nm, desc = e.desc ?? (c != null ? c.desc : null), shop = e.shop, refKey = e.cfg, missing = c == null });
                }
                return o;
            }
            // 並びのファイルが無ければ「内蔵の構成 ＋ stages/*.json を全部」
            foreach (var t in StageCfg.Builtins())
            {
                var c = StageCfg.Parse((Newtonsoft.Json.Linq.JObject)t, null);
                o.Add(new FreeE { c = c, n = c.n, desc = c.desc, refKey = "builtin:" + c.n });
            }
            foreach (var f in StageCfg.List())
            {
                var c = StageCfg.LoadQuiet(f);
                o.Add(new FreeE { c = c, n = c != null ? c.n : f, desc = c != null ? c.desc : null, refKey = "file:" + f, missing = c == null });
            }
            return o;
        }

        FreeE FreePick(int i) { return flist != null && i >= 0 && i < flist.Count && !flist[i].missing ? flist[i] : null; }

        string PageFree(Stack st)
        {
            flist = FreeList();
            if (pick > FREE_SLOTS) pick = 0;
            bool forRoom = from == "Wait";                    // 部屋の面をホストが決めている
            bool multi = from == "Local";
            var live = multi ? Live() : new List<PlayerSrc> { new PlayerSrc() };
            Head(st, next ? "次のステージを選ぶ" : "フリープレイ");
            // 8列×4行＝32マス（最後がランダム）
            const float W = 1159, gap = 5, th = 68;
            float tw = (W - 7 * gap) / 8;
            var grid = st.Row(4 * th + 3 * gap);
            for (int k = 0; k <= FREE_SLOTS; k++)
            {
                int kk = k;
                float x = (k % 8) * (tw + gap), y = (k / 8) * (th + gap);
                var e = k < FREE_SLOTS ? FreePick(k) : null;
                bool rnd = k == FREE_SLOTS;
                var gone = !rnd && flist != null && k < flist.Count && flist[k].missing ? flist[k] : null;
                var it = ThumbBtn(grid, x, y, tw, th, rnd ? null : e, rnd ? "？ ランダム" : e != null ? (e.c != null && e.c.file != null ? "★" : "") + e.n : gone != null ? "× " + gone.n : "準備中",
                                  k == pick, () => { pick = kk; Go("Free"); }, "fst" + k, !rnd && e == null);
                it.grid = 8;
                gridItems.Add(it);
            }
            // 選んでいる面の説明（カーソルが当たるたびに、ここだけ書き替える）
            var desc = st.Card(78);
            freeDesc = UiKit.Label(desc, "", 12.5f, INK, true, TextAnchor.UpperLeft);
            freeDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            var fr = freeDesc.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(16, 8); fr.offsetMax = new Vector2(-16, -9);
            freeDesc.lineSpacing = 1.1f;
            FreeDescSet(pick, false);
            Note(st, forRoom ? "この面を<b>部屋の全員</b>で遊びます。決めると待機画面へ戻ります。"
                   : next ? "買い物はここまでです。<b>次に遊ぶ面</b>を選んでください。お金・アイテム・強化はそのまま持ち越します。"
                         + (multi ? "\n<b>" + live.Count + "人</b>（" + string.Join("・", live.ConvertAll(s => s.Name).ToArray()) + "）／" + Game.ModeName(mode) : "")
                   : multi ? "<b>" + live.Count + "人</b>（" + string.Join("・", live.ConvertAll(s => s.Name).ToArray()) + "）／" + Game.ModeName(mode)
                           : "1人で遊びます。");
            var pickE = pick == FREE_SLOTS ? null : FreePick(pick);
            BtnRow(st, new[] { forRoom ? "この面にする" : next ? "この面で続ける ▶" : "スタート", next && !forRoom ? "やめてタイトルへ" : "戻る" }, new[] { "go", "sec" },
                   new System.Action[] { () => { if (forRoom) RoomPick(); else FreeStart(multi, live); },
                                         () => { if (next && !forRoom) { next = false; Go("Top"); } else Go(from); } },
                   new[] { pick != FREE_SLOTS && pickE == null, false });
            return "fst" + pick;
        }

        /// <summary>部屋で遊ぶ面を決めて、部屋の全員へ配る（JS版 roomPick）。</summary>
        void RoomPick()
        {
            bool rand = pick == FREE_SLOTS;
            var e = rand ? null : FreePick(pick);
            if (!rand && e == null) return;
            if (rand) Lobby.rpick = new Newtonsoft.Json.Linq.JObject { ["random"] = true, ["name"] = "？ ランダム" };
            else
            {
                int si = e.shop != null ? Mathf.Max(0, Shops.All.IndexOf(Shops.Find(e.shop))) : 0;
                Lobby.rpick = new Newtonsoft.Json.Linq.JObject { ["ref"] = e.refKey, ["name"] = e.n, ["shop"] = si, ["shopName"] = Shops.All[si].n };
            }
            Lobby.SendPick();
            Go("Wait");
        }

        void FreeStart(bool multi, List<PlayerSrc> live)
        {
            bool rand = pick == FREE_SLOTS;
            var e = rand ? null : FreePick(pick);
            if (rand)
            {
                // ランダム。並んでいる面から1つ引く（始まるまで分からない）
                var pool = flist.FindAll(x => !x.missing);
                if (pool.Count == 0) return;
                e = pool[Random.Range(0, pool.Count)];
            }
            if (e == null) return;
            var c = StageCfg.Ref(e.refKey);
            if (c == null) return;
            bool keep = next;
            var sl = multi ? live : new List<PlayerSrc> { new PlayerSrc() };
            string md = multi ? mode : "versus";
            next = false;
            Start(() => g.StartFree(c, e.shop, sl, md, keep));
        }

        void FreeDescSet(int i, bool preview)
        {
            if (freeDesc == null) return;
            string tag = preview ? "<color=#39414d>［見ています］</color> " : "<color=#b07a06>［選んでいます］</color> ";
            string body;
            if (i == FREE_SLOTS) body = "<b>？ ランダム</b>\nどの面になるかは、始まってからのお楽しみ。";
            else
            {
                var e = FreePick(i);
                if (e == null)
                {
                    var gone = flist != null && i < flist.Count ? flist[i] : null;
                    body = gone != null && gone.missing ? "<b>× " + gone.n + "</b>\n<color=#8a8274>この面が見つかりません（消したか、名前が変わっています）。</color>"
                                                        : "<b>準備中</b>\n<color=#8a8274>このマスのステージはまだありません。</color>";
                }
                else
                {
                    var sh = Shops.Find(e.shop) ?? Shops.All[0];
                    body = "<b>" + e.n + "</b>" + (e.c != null && e.c.file != null ? " <color=#8a8274>（stages/" + e.c.file + ".json）</color>" : "")
                         + "\n<color=#8a8274>" + sh.n + "　床：" + sh.fricLabel + "　単価 ×" + sh.price.ToString("0.0") + "　修理 ×" + sh.repair.ToString("0.0") + "</color>\n"
                         + (string.IsNullOrEmpty(e.desc) ? "<color=#8a8274>説明はまだありません。</color>" : e.desc);
                }
            }
            freeDesc.text = tag + body;
        }

        /// <summary>
        /// **本当にやめるか確かめる。**押し間違えるとすぐ終わってしまう所に使う（JS版 titleConfirm）。
        /// カーソルは**やめない方**から始める。B / Esc も「やめない」（元の画面へ）。
        /// </summary>
        void ConfirmOpen(string title, string note, string yesN, string noN, string back, System.Action onYes)
        {
            picker = new Picker { title = title, note = note, cur = "no", back = back, confirm = true,
                opts = new List<string[]> { new[] { "no", noN, "cont" }, new[] { "yes", yesN, "off" } },
                onPick = v => { if (v == "yes") onYes(); else Go(back); } };
            Go("Pick");
        }

        /// <summary>文字を入れる画面を開く（ルームID・サーバ）。</summary>
        void AskOpen(string title, string note, string value, string back, System.Action<string> onOk, bool digits = false)
        {
            ask = new Ask { title = title, note = note, value = value ?? "", back = back, onOk = onOk, digits = digits };
            Go("Ask");
        }

        // ---- 一覧から1つ選ばせる（選んだら元の画面へ戻る）
        void PickOpen(string title, string note, List<string[]> opts, string cur, string back, System.Action<string> onPick)
        {
            picker = new Picker { title = title, note = note, opts = opts, cur = cur, back = back, onPick = onPick };
            Go("Pick");
        }

        List<string[]> SlotOptsWithHint()
        {
            var o = new List<string[]>();
            foreach (var s in SLOT_OPTS) o.Add(new[] { s[0], s[1] + PadHint(s[0]), SlotIc(s[0]) });
            return o;
        }

        static string SlotLabel(string v) { foreach (var s in SLOT_OPTS) if (s[0] == v) return s[1]; return "なし"; }
        static string SlotIc(string v) { return v == "off" ? "off" : v == "key" ? "key" : v.StartsWith("pad") ? "pad" : "npc"; }
        static string PadHint(string v)
        {
            if (v == null || !v.StartsWith("pad") || v.Length < 4) return "";
            int i = v[3] - '0';
            return i < Gamepad.all.Count ? "" : "（未接続）";
        }

        /// <summary>枠と、枠ごとのチームを合わせる（JS版 slotsWithTeams）。落ちた枠にはチームを付けない。</summary>
        List<PlayerSrc> Live()
        {
            var o = new List<PlayerSrc>();
            for (int i = 0; i < slots.Length; i++)
            {
                var got = PlayerSrc.Parse(string.Join(",", slots, 0, i + 1));
                if (got.Count > o.Count) { var sc = got[got.Count - 1]; sc.team = teams[i]; o.Add(sc); }
            }
            return o;
        }

        void Start(System.Action act)
        {
            Close();
            act();
        }

        // ================================================================ 毎フレーム
        public void Tick(float dt)
        {
            if (!Open) return;
            if (goLater != null) { var pg = goLater; goLater = null; Go(pg); }
            if (lockT > 0) lockT -= dt;
            var kb = Keyboard.current;
            bool up = false, down = false, left = false, right = false, ok = false, back = false;
            if (kb != null)
            {
                up = kb.upArrowKey.isPressed; down = kb.downArrowKey.isPressed; left = kb.leftArrowKey.isPressed; right = kb.rightArrowKey.isPressed;
                ok = kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed;
                back = kb.escapeKey.isPressed || kb.backspaceKey.isPressed;
            }
            if (page == "Ask" && ask != null)
            {
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { AskOk(); return; }
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { Go(ask.back); return; }
                if (ask.inp != null && !ask.inp.isFocused) ask.inp.ActivateInputField();
                up = down = left = right = ok = back = false;            // 打っている字をカーソルの操作にしない
            }
            foreach (var gp in Gamepad.all)
            {
                // 十字キーだけ（スティックは JS版では指カーソル用）
                up |= gp.dpad.up.isPressed; down |= gp.dpad.down.isPressed; left |= gp.dpad.left.isPressed; right |= gp.dpad.right.isPressed;
                ok |= gp.buttonSouth.isPressed; back |= gp.buttonEast.isPressed;
            }
            if (back && !pBack)
            {
                var bk = items.Find(x => x.back && !x.off);
                if (bk != null) { Remember(up, down, left, right, ok, back); bk.fire(); return; }
            }
            // 十字キー・矢印キーを使ったら指は引っ込める（両方出ていると、どちらが効いているのか分からない）
            if ((up && !pUp) || (down && !pDown) || (left && !pLeft) || (right && !pRight)) Ptr.Hide();
            if (up && !pUp) Step(-1);
            if (down && !pDown) Step(1);
            if (left && !pLeft) Focus(idx - 1);
            if (right && !pRight) Focus(idx + 1);
            if (!ok) armed = true;                          // 開いてから一度離した後だけ
            bool fire = ok && !pOk && armed && lockT <= 0;
            Remember(up, down, left, right, ok, back);
            // 指カーソル（マウスかスティック）：動かしたら指の下の物を選ぶ。決定は A・Enter・クリック
            Ptr.Tick(dt, () => idx < items.Count ? Ptr.Center(items[idx].rt) : null);
            if (Ptr.Moved)
            {
                int hi = HitAt(Ptr.Pos);
                if (hi >= 0 && hi != idx && !items[hi].off) Focus(hi);
            }
            var ms = Mouse.current;
            if (ms != null)
            {
                var mp = ms.position.ReadValue();
                if (ms.leftButton.wasPressedThisFrame)
                {
                    int hi = HitAt(mp);
                    if (hi >= 0) { Focus(hi); if (!items[hi].off) { items[hi].fire(); return; } }
                }
            }
            if (fire && idx < items.Count && !items[idx].off) { items[idx].fire(); return; }
            // 選んでいる物を、ふわっと上下させる（板は 1.15 秒、ボタンは 1.1 秒）
            float tt = Time.unscaledTime;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.anim == null) continue;
                bool on = i == idx;
                float bob = on ? -Mathf.Abs(Mathf.Sin(tt * Mathf.PI / (it.bob > 4 ? 1.15f : 1.1f))) * it.bob : 0;
                it.anim.anchoredPosition = new Vector2(0, -bob);
                it.anim.localScale = Vector3.one * (on ? it.scale : 1f);
            }
        }

        void Remember(bool up, bool down, bool left, bool right, bool ok, bool back)
        {
            pUp = up; pDown = down; pLeft = left; pRight = right; pOk = ok; pBack = back;
        }

        int HitAt(Vector2 mp)
        {
            for (int i = items.Count - 1; i >= 0; i--)
                if (RectTransformUtility.RectangleContainsScreenPoint(items[i].rt, mp, null)) return i;
            return -1;
        }

        /// <summary>↑↓。面の一覧の中は1行ぶん、それ以外は1つずつ（JS版 menuStep）。</summary>
        void Step(int dir)
        {
            var it = idx < items.Count ? items[idx] : null;
            int n = it != null && it.grid > 1 ? it.grid : 1;
            if (n <= 1) { Focus(idx + dir); return; }
            var inG = items.FindAll(x => x.grid == n);
            int at = inG.IndexOf(it), nx = at + dir * n;
            if (nx >= 0 && nx < inG.Count) { Focus(items.IndexOf(inG[nx])); return; }
            if (dir > 0 && at < inG.Count - 1) { Focus(items.IndexOf(inG[inG.Count - 1])); return; }
            if (dir < 0 && at > 0) { Focus(items.IndexOf(inG[0])); return; }
            Focus(idx + (dir > 0 ? (inG.Count - at) : -(at + 1)));
        }

        void Focus(int i)
        {
            if (items.Count == 0) return;
            // 押せない物は飛ばす（JS版は disabled のボタンを並びに入れない）
            int dir = i >= idx ? 1 : -1;
            i = Mathf.Clamp(i, 0, items.Count - 1);
            while (i >= 0 && i < items.Count && items[i].off) i += dir;
            if (i < 0 || i >= items.Count) return;
            idx = i;
            for (int k = 0; k < items.Count; k++) if (items[k].look != null) items[k].look(k == idx);
            if (cap != null) cap.text = items[idx].desc ?? "";
            // 面選びの説明。一覧から外れたら、いま選んである面に戻す
            if (page == "Free")
            {
                var key = items[idx].key ?? "";
                int at = key.StartsWith("fst") ? int.Parse(key.Substring(3)) : -1;
                if (at < 0) FreeDescSet(pick, false); else FreeDescSet(at, at != pick);
            }
        }

        // ================================================================ 部品
        /// <summary>縦に積む（中身の高さを足して、画面のまん中に置く）。</summary>
        class Stack
        {
            public readonly RectTransform box; public readonly float w; float y;
            readonly List<RectTransform> rows = new List<RectTransform>();
            public Stack(RectTransform b, float width) { box = b; w = width; }
            public RectTransform Row(float h, float gapBefore = 8)
            {
                if (rows.Count > 0) y += gapBefore;
                var r = UiKit.Rect(box, "Row");
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1); r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(w, h); r.anchoredPosition = new Vector2(0, -y);
                y += h; rows.Add(r);
                return r;
            }
            public void Gap(float h) { y += h; }
            public Text Text(string s, float size, Color c, TextAnchor al, float h)
            {
                var r = Row(h, 0);
                var t = UiKit.Label(r, s, size, c, true, al);
                UiKit.Stretch(t.rectTransform);
                return t;
            }
            public RectTransform Card(float h)
            {
                var r = Row(h);
                var c = UiKit.Img(r, Mats.Hex(0xfffdf6), 15, "Card"); UiKit.Stretch(c.rectTransform);
                Ring(c.transform, Color.white, 4, 15);
                var sh = c.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.12f); sh.effectDistance = new Vector2(0, -4);
                return c.rectTransform;
            }
            /// <summary>積み終わったら、全体を縦のまん中へ寄せる（titleBox の max-height:96% の中で中央）。</summary>
            public void Finish()
            {
                float top = Mathf.Max(14, (720 - y) / 2);
                foreach (var r in rows) r.anchoredPosition += new Vector2(0, -top);
            }
        }

        /// <summary>題名（ネコ配（仮））と副題。</summary>
        void Logo(Stack st)
        {
            var r = st.Row(47, 0);
            var t = UiKit.Label(r, "ネコ配<size=20>（仮）</size>", 45, Color.white, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(t.rectTransform);
            var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = Mats.Hex(0x2f6ea8); s.effectDistance = new Vector2(0, -4);
            st.Gap(3);
            var sub = st.Text("～ただいま配膳中！～", 12, Color.white, TextAnchor.MiddleCenter, 16);
            Shade(sub, 0.45f);
            st.Gap(11);
        }

        /// <summary>見出し（斜めの金の帯）。</summary>
        void Head(Stack st, string s)
        {
            var r = st.Row(40, 0);
            var t = UiKit.Label(r, s, 18, Color.white, true, TextAnchor.MiddleCenter);
            float w = t.preferredWidth + 60 + 8, h = 38;
            var img = Panel(r, w, h, -9, CLIP_RECT, Mats.Hex(0xf6c95a), GOLD, 180, 0, Color.white, 4, GOLD_D, 4, "H2", out var holder);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 1); holder.pivot = new Vector2(0.5f, 1); holder.anchoredPosition = Vector2.zero;
            t.transform.SetParent(holder, false);
            UiKit.Stretch(t.rectTransform);
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = GOLD_D; sh.effectDistance = new Vector2(0, -2);
            st.Gap(7);
        }

        void Note(Stack st, string s)
        {
            var t = UiKit.Label(box, s.Replace("<b>", "<b><size=13>").Replace("</b>", "</size></b>"), 12.5f, Color.white, true, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.lineSpacing = 1.15f;
            // 行の数で高さを決める（line-height 1.55）
            var set = t.GetGenerationSettings(new Vector2(st.w, 0));
            var gen = t.cachedTextGeneratorForLayout;
            gen.Populate(t.text, set);
            float h = Mathf.Max(1, gen.lineCount) * 12.5f * 1.55f + 2;
            var r = st.Row(h);
            t.transform.SetParent(r, false);
            UiKit.Stretch(t.rectTransform);
            Shade(t, 0.55f);
        }

        static void Shade(Text t, float a)
        {
            var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, a); s.effectDistance = new Vector2(0, -2);
        }

        // ---- 色板（斜めの大きな板）
        class TileSpec { public string label, col, ic, desc, key; public System.Action fire; public bool off; }
        TileSpec Tile(string label, string col, string ic, System.Action fire, string desc, string key, bool off = false)
        {
            return new TileSpec { label = label, col = col, ic = ic, fire = fire, desc = desc, key = key, off = off };
        }

        /// <summary>2枚を横に（左が大きい 1.32:1）。3枚目は下に細い板（wide）。</summary>
        void Tiles(Stack st, params TileSpec[] ts)
        {
            const float H = 202, gap = 4;
            float wl = (1098 - gap) * 1.32f / 2.32f, wr = 1098 - gap - wl;
            var row = st.Row(H);
            for (int i = 0; i < ts.Length && i < 2; i++)
                BigTile(row, i == 0 ? 0 : wl + gap, i == 0 ? wl : wr, H, ts[i], i);
            st.Gap(10);
            if (ts.Length > 2)
            {
                var r2 = st.Row(46, 11);
                WideTile(r2, (1098 - 400) / 2f, 400, 46, ts[2]);
                st.Gap(4);
            }
        }

        static readonly float[] CLIP_RECT = { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1, 0 };
        // 点は (ax, bx, ay, by)：x = ax×幅 + bx、y = ay×高さ + by（CSS の polygon の % と px）
        static readonly float[] CLIP_TILE_L = { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0.09f, 0, 1, 0, 0, 0, 0.72f, 0 };
        static readonly float[] CLIP_TILE_R = { 0, 0, 0, 0, 0.91f, 0, 0, 0, 1, 0, 0.28f, 0, 1, 0, 1, 0, 0, 0, 1, 0 };
        static readonly float[] CLIP_WIDE = { 0, 0, 0, 0, 1, 0, 0, 0, 0.96f, 0, 1, 0, 0.04f, 0, 1, 0 };
        static readonly float[] CLIP_BTN = { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 13, 1, 0, 0, 0, 1, -13 };
        static readonly float[] CLIP_SEC = { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 10, 1, 0, 0, 0, 1, -10 };
        static readonly float[] CLIP_GO = { 0, 0, 0, 0, 1, -13, 0, 0, 1, 0, 0, 13, 1, 0, 1, 0, 0, 0, 1, 0 };
        static readonly float[] CLIP_PICK = { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 11, 1, 0, 0, 0, 1, -11 };

        void BigTile(RectTransform row, float x, float w, float h, TileSpec t, int n)
        {
            var c = TC[t.col];
            var it = new Item { fire = t.fire, desc = t.desc, key = t.key, off = t.off, bob = 5, scale = 1.04f };
            var clip = n == 0 ? CLIP_TILE_L : CLIP_TILE_R;
            var hold = UiKit.Rect(row, "Tile_" + t.key);
            hold.anchorMin = hold.anchorMax = new Vector2(0, 1); hold.pivot = new Vector2(0, 1);
            hold.anchoredPosition = new Vector2(x, 0); hold.sizeDelta = new Vector2(w, h);
            var anim = UiKit.Rect(hold, "Anim"); UiKit.Stretch(anim);
            RectTransform pr;
            var img = Panel(anim, w, h, -9, clip, Mats.Hex(c[0]), Mats.Hex(c[1]), 152, 0.13f, new Color(1, 1, 1, 0.95f), 3, Mats.Hex(c[2]), 7, "Bg", out pr);
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.pivot = new Vector2(0.5f, 0.5f); pr.anchoredPosition = new Vector2(0, -3.5f);
            var foc = Panel(anim, w, h, -9, clip, Brighten(Mats.Hex(c[0])), Brighten(Mats.Hex(c[1])), 152, 0.13f, GOLD, 3, Mats.Hex(c[2]), 7, "Foc", out var fr);
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f); fr.pivot = new Vector2(0.5f, 0.5f); fr.anchoredPosition = new Vector2(0, -3.5f);
            float t9 = Mathf.Tan(9 * Mathf.Deg2Rad);
            var fl = FocusLine(anim, w, h, -9, 5);
            // 絵は右のまん中に大きく薄く
            Icon(anim, t.ic, 86, new Vector2(w / 2 - w * 0.07f - 43, 0), 0.5f);
            // 字は左下（傾けたぶん、下ほど左へずれる）
            float ly = h - 11.5f - 18;
            var lb = UiKit.Label(anim, t.label, 26, Color.white, true, TextAnchor.MiddleLeft);
            var lr = lb.rectTransform; lr.anchorMin = lr.anchorMax = new Vector2(0, 1); lr.pivot = new Vector2(0, 0.5f);
            lr.sizeDelta = new Vector2(w, 40); lr.anchoredPosition = new Vector2(58 - t9 * (ly - h / 2), -ly);
            var sh = lb.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.4f); sh.effectDistance = new Vector2(0, -3);
            it.rt = hold; it.anim = anim;
            var cg = hold.gameObject.AddComponent<CanvasGroup>(); cg.alpha = t.off ? 0.45f : 1f;
            it.look = on => { foc.gameObject.SetActive(on); img.gameObject.SetActive(!on); fl.SetActive(on); if (on) hold.SetAsLastSibling(); };
            items.Add(it);
        }

        void WideTile(RectTransform row, float x, float w, float h, TileSpec t)
        {
            var c = TC[t.col];
            var it = new Item { fire = t.fire, desc = t.desc, key = t.key, off = t.off, bob = 5, scale = 1.04f };
            var hold = UiKit.Rect(row, "Wide_" + t.key);
            hold.anchorMin = hold.anchorMax = new Vector2(0, 1); hold.pivot = new Vector2(0, 1);
            hold.anchoredPosition = new Vector2(x, 0); hold.sizeDelta = new Vector2(w, h);
            var anim = UiKit.Rect(hold, "Anim"); UiKit.Stretch(anim);
            var img = Panel(anim, w, h, -9, CLIP_WIDE, Mats.Hex(c[0]), Mats.Hex(c[1]), 152, 0.13f, new Color(1, 1, 1, 0.95f), 3, Mats.Hex(c[2]), 7, "Bg", out var pr);
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.anchoredPosition = new Vector2(0, -3.5f);
            var foc = Panel(anim, w, h, -9, CLIP_WIDE, Brighten(Mats.Hex(c[0])), Brighten(Mats.Hex(c[1])), 152, 0.13f, GOLD, 3, Mats.Hex(c[2]), 7, "Foc", out var fr);
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f); fr.anchoredPosition = new Vector2(0, -3.5f);
            var fl = FocusLine(anim, w, h, -9, 5);
            // 絵と字を横に並べてまん中へ
            var lb = UiKit.Label(anim, t.label, 14.4f, Color.white, true, TextAnchor.MiddleCenter);
            float lw = lb.preferredWidth, iw = 22, total = iw + 10 + lw;
            UiKit.Stretch(lb.rectTransform); lb.rectTransform.anchoredPosition = new Vector2((iw + 10) / 2, 0);
            Icon(anim, t.ic, iw, new Vector2(-total / 2 + iw / 2, 0), 1f);
            var sh = lb.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.4f); sh.effectDistance = new Vector2(0, -3);
            it.rt = hold; it.anim = anim;
            var cg = hold.gameObject.AddComponent<CanvasGroup>(); cg.alpha = t.off ? 0.45f : 1f;
            it.look = on => { foc.gameObject.SetActive(on); img.gameObject.SetActive(!on); fl.SetActive(on); if (on) hold.SetAsLastSibling(); };
            items.Add(it);
        }

        void Icon(RectTransform parent, string ic, float size, Vector2 pos, float alpha)
        {
            var tex = Resources.Load<Texture2D>("Ui/Title/" + ic);
            if (tex == null) return;
            var ri = UiKit.Rect(parent, "Ic").gameObject.AddComponent<RawImage>();
            ri.texture = tex; ri.raycastTarget = false; ri.color = new Color(1, 1, 1, alpha);
            var r = ri.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(size, size); r.anchoredPosition = pos;
            var sh = ri.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.3f); sh.effectDistance = new Vector2(0, -3);
        }

        // ---- ボタン（斜め。default＝暗い、sec＝灰、go＝緑、tm0/tm1＝チームの色、pk＝いま選ばれている物）
        Item Btn(Stack st, string label, string kind, System.Action fire, string desc, bool back = false, string key = null)
        {
            float w = kind == "sec" ? 320 : 520, h = kind == "sec" ? 34 : 44;
            var r = st.Row(h + 4);
            return BtnAt(r, (st.w - w) / 2, 0, w, h, label, kind, fire, desc, back, key ?? (back ? "back" : label), kind == "sec" ? 14 : 17);
        }

        void BtnRow(Stack st, string[] labels, string[] kinds, System.Action[] fires, bool[] offs)
        {
            const float gap = 13, h = 48;
            float w = (st.w - gap * (labels.Length - 1)) / labels.Length;
            var r = st.Row(h + 4);
            for (int i = 0; i < labels.Length; i++)
            {
                bool back = kinds[i] == "sec";
                var it = BtnAt(r, i * (w + gap), kinds[i] == "sec" ? 7 : 0, w, kinds[i] == "sec" ? 36 : h, labels[i], kinds[i], fires[i], null, back, back ? "back" : "go", kinds[i] == "sec" ? 14 : 17);
                it.off = offs[i];
                if (it.off) it.rt.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f;
            }
        }

        Item BtnAt(RectTransform row, float x, float y, float w, float h, string label, string kind, System.Action fire, string desc, bool back, string key, float fs)
        {
            Color top, bot, edge; float[] clip = CLIP_BTN;
            switch (kind)
            {
                case "sec": top = Mats.Hex(0x8d8779); bot = Mats.Hex(0x6a655b); edge = Mats.Hex(0x3f3b34); clip = CLIP_SEC; break;
                case "go": top = Mats.Hex(0x63d47f); bot = Mats.Hex(0x4cbf68); edge = Mats.Hex(0x2f9448); clip = CLIP_GO; break;
                case "tm0": top = Mats.Hex(0x3aa3ee); bot = Mats.Hex(0x1160bd); edge = Mats.Hex(0x0a3f80); clip = CLIP_SEC; break;
                case "tm1": top = Mats.Hex(0xf0483c); bot = Mats.Hex(0xc01a12); edge = Mats.Hex(0x7d0f0a); clip = CLIP_SEC; break;
                case "pk": top = Mats.Hex(0x6a5a2a); bot = Mats.Hex(0x463a16); edge = Mats.Hex(0x241d09); clip = CLIP_PICK; break;
                default: top = Mats.Hex(0x39445a); bot = Mats.Hex(0x222a3a); edge = Mats.Hex(0x0d1220); if (key != null && key.StartsWith("o")) clip = CLIP_PICK; break;
            }
            var it = new Item { fire = fire, desc = desc, back = back, key = key, bob = 3, scale = 1f };
            var hold = UiKit.Rect(row, "Btn_" + key);
            hold.anchorMin = hold.anchorMax = new Vector2(0, 1); hold.pivot = new Vector2(0, 1);
            hold.anchoredPosition = new Vector2(x, -y); hold.sizeDelta = new Vector2(w, h);
            var anim = UiKit.Rect(hold, "Anim"); UiKit.Stretch(anim);
            var img = Panel(anim, w, h, -6, clip, top, bot, 180, 0.10f, new Color(1, 1, 1, 0.95f), 3, edge, 4, "Bg", out var pr);
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.anchoredPosition = new Vector2(0, -2);
            var foc = Panel(anim, w, h, -6, clip, Brighten(top), Brighten(bot), 180, 0.10f, GOLD, 3, edge, 4, "Foc", out var fr);
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f); fr.anchoredPosition = new Vector2(0, -2);
            var fl = FocusLine(anim, w, h, -6, 4);
            var lb = UiKit.Label(anim, label, fs, Color.white, true, TextAnchor.MiddleCenter);
            lb.name = "Lb";
            UiKit.Stretch(lb.rectTransform);
            var sh = lb.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.3f); sh.effectDistance = new Vector2(0, -2);
            it.rt = hold; it.anim = anim;
            it.look = on => { foc.gameObject.SetActive(on); img.gameObject.SetActive(!on); fl.SetActive(on); if (on) hold.SetAsLastSibling(); };
            // ラベルを Find で引けるように、ボタンの直下にも名前を置く
            lb.transform.SetParent(hold, true);
            items.Add(it);
            return it;
        }

        /// <summary>1P〜4P の札（角の丸い色板。縁は白、選ぶと金）。</summary>
        void Seat(RectTransform row, float x, float w, string pn, string ic, string label, string col, System.Action fire, string desc, string key, string tag = null)
        {
            var c = TC[col];
            var it = new Item { fire = fire, desc = desc, key = key, bob = 3, scale = 1f };
            var hold = UiKit.Rect(row, "Seat_" + key);
            hold.anchorMin = hold.anchorMax = new Vector2(0, 1); hold.pivot = new Vector2(0, 1);
            hold.anchoredPosition = new Vector2(x, 0); hold.sizeDelta = new Vector2(w, 92);
            var anim = UiKit.Rect(hold, "Anim"); UiKit.Stretch(anim);
            var sh = UiKit.Img(anim, Mats.Hex(c[2]), 18, "Shadow"); UiKit.Stretch(sh.rectTransform); sh.rectTransform.anchoredPosition = new Vector2(0, -5);
            var bg = UiKit.Img(anim, Color.white, 0, "Bg"); UiKit.Stretch(bg.rectTransform);
            bg.sprite = UiKit.Grad(Mathf.RoundToInt(w), 92, 18, Mats.Hex(c[0]), Mats.Hex(c[1]));
            var ring = Ring(anim, Color.white, 5, 18);
            var pnT = UiKit.Label(anim, pn, 11, new Color(1, 1, 1, 0.85f), true, TextAnchor.MiddleCenter);
            var r1 = pnT.rectTransform; r1.anchorMin = r1.anchorMax = new Vector2(0.5f, 1); r1.pivot = new Vector2(0.5f, 1); r1.sizeDelta = new Vector2(w, 16); r1.anchoredPosition = new Vector2(0, -13);
            Icon(anim, ic, 26, new Vector2(0, 2), 1f);
            var lb = UiKit.Label(anim, label, 13, Color.white, true, TextAnchor.MiddleCenter);
            var r2 = lb.rectTransform; r2.anchorMin = r2.anchorMax = new Vector2(0.5f, 0); r2.pivot = new Vector2(0.5f, 0); r2.sizeDelta = new Vector2(w, 18); r2.anchoredPosition = new Vector2(0, 13);
            Shade(lb, 0.28f);
            if (tag != null)
            {
                // 名前の下に小さく（部屋の「ホスト」）。そのぶん絵と名前を上へ
                r2.anchoredPosition = new Vector2(0, 21);
                var tg = UiKit.Label(anim, tag, 11, new Color(1, 1, 1, 0.85f), true, TextAnchor.MiddleCenter);
                var r3 = tg.rectTransform; r3.anchorMin = r3.anchorMax = new Vector2(0.5f, 0); r3.pivot = new Vector2(0.5f, 0); r3.sizeDelta = new Vector2(w, 14); r3.anchoredPosition = new Vector2(0, 8);
                var icn = anim.Find("Ic"); if (icn != null) ((RectTransform)icn).anchoredPosition = new Vector2(0, 8);
                pnT.rectTransform.anchoredPosition = new Vector2(0, -9);
            }
            if (col == "grey") hold.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;
            it.rt = hold; it.anim = anim;
            it.look = on => { ring.color = on ? GOLD : Color.white; if (on) hold.SetAsLastSibling(); };
            if (fire != null) items.Add(it);                    // 押せない札（部屋の人が居る席）はカーソルに入れない
        }

        /// <summary>面のマス（見取り図の上に名前）。選んである面は金、指が乗っている面は白で囲う。</summary>
        Item ThumbBtn(RectTransform row, float x, float y, float w, float h, FreeE e, string name, bool picked, System.Action fire, string key, bool off)
        {
            var it = new Item { fire = fire, key = key, off = off };
            var hold = UiKit.Rect(row, "Thumb_" + key);
            hold.anchorMin = hold.anchorMax = new Vector2(0, 1); hold.pivot = new Vector2(0, 1);
            hold.anchoredPosition = new Vector2(x, -y); hold.sizeDelta = new Vector2(w, h);
            var outline = UiKit.Img(hold, GOLD, 12, "Outline");
            var orr = outline.rectTransform; UiKit.Stretch(orr, -5);
            outline.sprite = UiKit.Ring(Mathf.RoundToInt(4 * 15.5f / 12)); outline.type = Image.Type.Sliced; outline.pixelsPerUnitMultiplier = 15.5f / 12;
            outline.gameObject.SetActive(picked);
            var bg = UiKit.Img(hold, Color.white, 10, "Bg"); UiKit.Stretch(bg.rectTransform);
            var inner = UiKit.Img(hold, Mats.Hex(e == null && name.StartsWith("？") ? 0x2b3446 : 0x12161d), 8, "Inner"); UiKit.Stretch(inner.rectTransform, 3);
            inner.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            if (e != null && e.c != null)
            {
                var ri = UiKit.Rect(inner.transform, "Pic").gameObject.AddComponent<RawImage>();
                ri.texture = Thumb.Of(e.refKey, e.c); ri.raycastTarget = false;
                // object-fit:cover（16:9 の絵を、マスの形に合わせて切る）
                float aw = w - 6, ah = h - 6, k = Mathf.Max(aw / 192f, ah / 108f);
                ri.rectTransform.sizeDelta = new Vector2(192 * k, 108 * k);
            }
            if (e == null && name.StartsWith("？"))
            {
                var t = UiKit.Label(inner.transform, name, 13, Color.white, true, TextAnchor.MiddleCenter);
                UiKit.Stretch(t.rectTransform);
            }
            else
            {
                var cb = UiKit.Img(inner.transform, picked ? GOLD : new Color(10 / 255f, 13 / 255f, 18 / 255f, 0.78f), 0, "Cap");
                var cr = cb.rectTransform; cr.anchorMin = new Vector2(0, 0); cr.anchorMax = new Vector2(1, 0); cr.pivot = new Vector2(0.5f, 0);
                cr.sizeDelta = new Vector2(0, 16); cr.anchoredPosition = Vector2.zero;
                var t = UiKit.Label(cb.transform, name, 10, picked ? Mats.Hex(0x3b2a05) : Color.white, picked, TextAnchor.MiddleCenter);
                UiKit.Stretch(t.rectTransform);
            }
            var hov = UiKit.Img(hold, Color.white, 12, "Hover");
            UiKit.Stretch(hov.rectTransform, -4);
            hov.sprite = UiKit.Ring(Mathf.RoundToInt(3 * 15.5f / 12)); hov.type = Image.Type.Sliced; hov.pixelsPerUnitMultiplier = 15.5f / 12;
            hov.gameObject.SetActive(false);
            if (off) hold.gameObject.AddComponent<CanvasGroup>().alpha = 0.32f;
            it.rt = hold;
            it.look = on =>
            {
                hov.gameObject.SetActive(on && !picked);
                hov.color = Color.white;
                if (on && picked) outline.color = Mats.Hex(0xffd56a);
                else outline.color = GOLD;
                if (on || picked) hold.SetAsLastSibling();
            };
            items.Add(it);
            return it;
        }

        /// <summary>選んでいる物の外側の金の線（JS版 .mfoc の outline。角は落とさず、斜めの四角）。</summary>
        static GameObject FocusLine(RectTransform parent, float w, float h, float skew, float off)
        {
            var im = Panel(parent, w + off * 2, h + off * 2, skew, CLIP_RECT, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 180, 0,
                           GOLD, 3, new Color(0, 0, 0, 0), 0, "FocLine", out var r);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            im.gameObject.SetActive(false);
            return im.gameObject;
        }

        static Color Brighten(Color c) { return new Color(Mathf.Min(1, c.r * 1.1f), Mathf.Min(1, c.g * 1.1f), Mathf.Min(1, c.b * 1.1f), c.a); }

        static Image Ring(Transform parent, Color c, int th, float radius)
        {
            var b = UiKit.Img(parent, c, 0, "Ring");
            b.sprite = UiKit.Ring(Mathf.Max(1, Mathf.RoundToInt(th * 15.5f / radius)));
            b.type = Image.Type.Sliced; b.pixelsPerUnitMultiplier = 15.5f / radius;
            UiKit.Stretch(b.rectTransform);
            return b;
        }

        // ================================================================ 斜めの板の絵
        static readonly Dictionary<string, Sprite> panels = new Dictionary<string, Sprite>();

        /// <summary>
        /// 斜めの板を1枚の絵にして置く（CSS の skewX と clip-path の2枚重ね、縞、厚みの影）。
        ///   外側の1枚＝縁の色、3px 内側の1枚＝地（グラデーション＋斜めの縞）、下へずらした同じ形＝厚みの影。
        /// 戻り値は Image。holder はその RectTransform（絵の大きさは斜めのぶん横に広い）。
        /// </summary>
        static Image Panel(RectTransform parent, float w, float h, float skewDeg, float[] clip, Color fill0, Color fill1, float gradDeg,
                           float stripeA, Color border, int bw, Color edge, int edgeOff, string name, out RectTransform holder)
        {
            var sp = PanelSprite(Mathf.RoundToInt(w), Mathf.RoundToInt(h), skewDeg, clip, fill0, fill1, gradDeg, stripeA, border, bw, edge, edgeOff);
            var im = UiKit.Img(parent, Color.white, 0, name);
            im.sprite = sp; im.raycastTarget = false;
            holder = im.rectTransform;
            holder.sizeDelta = new Vector2(sp.rect.width, sp.rect.height);
            return im;
        }

        static Sprite PanelSprite(int W, int H, float skewDeg, float[] clip, Color f0, Color f1, float gradDeg, float stripeA, Color border, int bw, Color edge, int edgeOff)
        {
            string key = W + "x" + H + "s" + skewDeg + "c" + string.Join(",", System.Array.ConvertAll(clip, v => v.ToString())) + ColorUtility.ToHtmlStringRGBA(f0)
                       + ColorUtility.ToHtmlStringRGBA(f1) + gradDeg + stripeA + ColorUtility.ToHtmlStringRGBA(border) + bw + ColorUtility.ToHtmlStringRGBA(edge) + edgeOff;
            Sprite s;
            if (panels.TryGetValue(key, out s)) return s;
            float t = Mathf.Tan(skewDeg * Mathf.Deg2Rad), shift = Mathf.Abs(t) * H / 2;
            int TW = Mathf.CeilToInt(W + Mathf.Abs(t) * H) + 2, TH = H + edgeOff;
            var outer = Poly(clip, 0, 0, W, H);
            var inner = Poly(clip, bw, bw, W - bw * 2, H - bw * 2);
            float ga = gradDeg * Mathf.Deg2Rad, gdx = Mathf.Sin(ga), gdy = -Mathf.Cos(ga);
            float glen = Mathf.Abs(W * gdx) + Mathf.Abs(H * gdy);
            float sa = 118 * Mathf.Deg2Rad, sdx = Mathf.Sin(sa), sdy = -Mathf.Cos(sa);
            float sw = stripeA > 0.11f ? 12 : 10, sper = stripeA > 0.11f ? 30 : 26;
            var px = new Color32[TW * TH];
            const int SS = 3;
            for (int ty = 0; ty < TH; ty++)
                for (int tx = 0; tx < TW; tx++)
                {
                    float aE = 0, aO = 0, aI = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float X = tx + (sx + 0.5f) / SS, Y = ty + (sy + 0.5f) / SS;      // Y は上から
                            float x = X - 1 - shift - t * (Y - H / 2f);
                            if (Inside(outer, x, Y)) aO++;
                            if (Inside(inner, x, Y)) aI++;
                            float xe = X - 1 - shift - t * (Y - edgeOff - H / 2f);
                            if (Inside(outer, xe, Y - edgeOff)) aE++;
                        }
                    aE /= SS * SS; aO /= SS * SS; aI /= SS * SS;
                    float cx = tx + 0.5f, cy = ty + 0.5f;
                    float lx = cx - 1 - shift - t * (cy - H / 2f);
                    float gt = Mathf.Clamp01(((lx - W / 2f) * gdx + (cy - H / 2f) * gdy) / glen + 0.5f);
                    var fill = Color.Lerp(f0, f1, gt);
                    float ph = Mathf.Repeat(lx * sdx + cy * sdy, sper);
                    float fa = fill.a;                                    // 地が透明（外側の線だけの絵）なら縞も描かない
                    if (stripeA > 0 && ph < sw && fa > 0) fill = Color.Lerp(fill, Color.white, stripeA);
                    fill.a = fa;
                    // 下から：厚みの影 → 縁 → 地
                    var c = new Color(edge.r, edge.g, edge.b, aE * edge.a);
                    c = Over(c, new Color(border.r, border.g, border.b, Mathf.Max(0, aO - aI) * border.a));   // 縁は内側を除いた輪だけ
                    c = Over(c, new Color(fill.r, fill.g, fill.b, aI * fill.a));
                    px[(TH - 1 - ty) * TW + tx] = c;
                }
            var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(false);
            s = Sprite.Create(tex, new Rect(0, 0, TW, TH), new Vector2(0.5f, 0.5f), 100);
            panels[key] = s;
            return s;
        }

        static Color Over(Color dst, Color src)
        {
            float a = src.a + dst.a * (1 - src.a);
            if (a <= 0) return new Color(0, 0, 0, 0);
            return new Color((src.r * src.a + dst.r * dst.a * (1 - src.a)) / a, (src.g * src.a + dst.g * dst.a * (1 - src.a)) / a,
                             (src.b * src.a + dst.b * dst.a * (1 - src.a)) / a, a);
        }

        static Vector2[] Poly(float[] c, float ox, float oy, float w, float h)
        {
            var p = new Vector2[c.Length / 4];
            for (int i = 0; i < p.Length; i++)
                p[i] = new Vector2(ox + c[i * 4] * w + c[i * 4 + 1], oy + c[i * 4 + 2] * h + c[i * 4 + 3]);
            return p;
        }

        static bool Inside(Vector2[] p, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if (((p[i].y > y) != (p[j].y > y)) && (x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)) inside = !inside;
            return inside;
        }

        /// <summary>背景（4色の空と芝生、2つの白い光、ごく薄い斜めの縞）。JS版 #title の background。</summary>
        static Texture2D BgTex()
        {
            const int W = 320, H = 180;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color c0 = Mats.Hex(0x3f8fd0), c1 = Mats.Hex(0x74c7ea), c2 = Mats.Hex(0xa8dcf0), c3 = Mats.Hex(0xa9d478);
            float sa = 112 * Mathf.Deg2Rad, sdx = Mathf.Sin(sa), sdy = -Mathf.Cos(sa);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float X = (x + 0.5f) / W * 1280, Y = (1 - (y + 0.5f) / H) * 720, v = Y / 720f;
                    var col = v < 0.42f ? Color.Lerp(c0, c1, v / 0.42f) : v < 0.62f ? Color.Lerp(c1, c2, (v - 0.42f) / 0.2f) : Color.Lerp(c2, c3, (v - 0.62f) / 0.38f);
                    float d1 = Mathf.Sqrt(Mathf.Pow((X - 256) / 900f, 2) + Mathf.Pow((Y - 86) / 300f, 2));
                    col = Color.Lerp(col, Color.white, 0.35f * Mathf.Clamp01(1 - d1 / 0.7f));
                    float d2 = Mathf.Sqrt(Mathf.Pow((X - 1024) / 700f, 2) + Mathf.Pow((Y - 158) / 240f, 2));
                    col = Color.Lerp(col, Color.white, 0.25f * Mathf.Clamp01(1 - d2 / 0.7f));
                    if (Mathf.Repeat(X * sdx + Y * sdy, 74) < 30) col = Color.Lerp(col, Color.white, 0.03f);
                    tex.SetPixel(x, y, col);
                }
            tex.Apply(false);
            return tex;
        }
    }
}
