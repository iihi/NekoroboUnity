using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// タイトルのオンラインのページ（JS版 TITLE_PAGE の Online / Make / Join / Wait と titleAsk）。
    /// 部屋サーバーは HTML版と同じもの。部屋の中身（席・面・ルール）は Net.Room と Lobby にある。
    /// </summary>
    public partial class TitleWin
    {
        static readonly string[][] NPC_SEAT_OPTS =
        {
            new[] { "off", "なし" }, new[] { "npc-serious", "NPC・まじめ" }, new[] { "npc-normal", "NPC・普通" },
            new[] { "npc-wild", "NPC・暴走" }, new[] { "npc-easy", "NPC・かんたん" },
        };

        /// <summary>つながり具合の1行。パッドしか無い人にも分かるように、状態を文で出す（JS版 netLine）。</summary>
        static string NetLine()
        {
            string where = "<i>" + Net.Label + "：" + Net.Where + "</i>";
            if (Net.State == "on") return "つながっています。" + (Net.Id >= 0 ? "（番号 " + Net.Id + "）" : "") + "\n" + where;
            if (Net.State == "connecting") return "つないでいます…\n" + where;
            return "<color=#ff9e6b><b>つながっていません</b></color>" + (Net.Why.Length > 0 ? "（" + Net.Why + "）" : "") + "\n" + where
                 + "\n部屋サーバー（HTML版の <b>node server.js</b> か <b>node rooms-server.js</b>）が動いているか確かめてください。"
                 + "別の場所で動かしているなら、下の<b>「サーバ」</b>にそのマシンを入れてください。";
        }

        void PageOnline(Stack st)
        {
            Net.Connect();
            bool on = Net.State == "on";
            Head(st, "オンライン");
            Note(st, NetLine());
            Btn(st, "サーバ：" + (Net.Addr.Length > 0 ? Net.Addr : Net.AddrFromCfg ? "置いた先の設定（net.json）" : "localhost:8123（開発の置き方）") + "　▾", "sec",
                () => AskOpen("サーバを入れる",
                    "ふつうは空のままでよいです（<b>localhost:8123</b>＝このPCで動かした HTML版の node server.js につなぎます）。\n"
                    + "部屋サーバーを別のマシンで動かしているときは、<b>ホスト名:ポート</b>（例 <b>192.168.0.10:8124</b>）で入れてください。",
                    Net.Addr, "Online", v => { Net.SetAddr(v); Net.Close(); Net.Connect(); }),
                "つなぐ先の部屋サーバーを変えます（この端末で覚えます）。", false, "srv");
            Tiles(st,
                Tile("ルームを作る", "green", "make", () => { Lobby.ruleTo = "Make"; Go("Rule"); }, "あなたがホストになります。面を選ぶのもホストです。", "make", !on),
                Tile("ルームに参加", "blue", "join", () => { Net.ListRooms(); Go("Join"); }, "開いている部屋の一覧から選ぶか、ルームIDを入れて入ります。", "join", !on));
            Btn(st, on ? "つなぎ直す" : "もう一度つなぐ", "sec", () => { Net.Close(); Net.Connect(); Go("Online"); }, null, false, "redo");
            Btn(st, "戻る", "sec", () => Go("Multi"), null, true);
        }

        /// <summary>部屋を作っている所（作れたら Go が待機へ回す）。</summary>
        void PageMake(Stack st)
        {
            Head(st, "ルームを作る");
            Note(st, NetLine() + "\n作っています…");
            Btn(st, "戻る", "sec", () => Go("Online"), null, true);
        }

        void PageJoin(Stack st)
        {
            Head(st, "ルームに参加");
            Note(st, NetLine());
            if (Net.Why.Length > 0) Note(st, "<color=#ff9e6b><b>" + Net.Why + "</b></color>");
            var list = Lobby.rooms;
            if (list != null && list.Count > 0)
                foreach (var r in list)
                {
                    string code = (string)r["code"];
                    Btn(st, (string)r["name"] + "　" + r["n"] + "/" + r["max"] + "人　" + code, "", () => Net.JoinRoom(code), null, false, "r" + code);
                }
            else Note(st, "<i>開いている部屋がありません。</i>");
            Btn(st, "一覧を出し直す", "sec", () => Net.ListRooms(), null, false, "refresh");
            Btn(st, "番号で入る：" + (Lobby.code.Length > 0 ? Lobby.code : "（未入力）") + "　▾", "sec",
                () => AskOpen("ルームIDを入れる", "12桁の番号です。相手に聞いて入れてください。", Lobby.code, "Join", v => Lobby.code = v, true),
                null, false, "code");
            var go = Btn(st, "この番号で入る", "", () => Net.JoinRoom(Lobby.code), null, false, "go");
            if (Lobby.code.Length == 0) Off(go);
            Btn(st, "戻る", "sec", () => Go("Online"), null, true);
        }

        static Font mono;
        /// <summary>等幅の字（ルームID）。</summary>
        static Font Mono { get { return mono != null ? mono : (mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "MS Gothic" }, 22)); } }

        void Off(Item it)
        {
            it.off = true;
            var cg = it.rt.GetComponent<CanvasGroup>() ?? it.rt.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0.45f;
        }

        /// <summary>待機。全員そろったらホストが始める（JS版 TITLE_PAGE.Wait）。</summary>
        void PageWait(Stack st)
        {
            var r = Net.Room;
            if (r == null) { PageOnline(st); return; }
            bool host = Net.IsHost;
            var players = Net.Players ?? new JArray();
            int max = Net.Max;
            Head(st, (string)r["name"] + ((bool?)r["priv"] == true ? "（非公開）" : ""));
            Note(st, "ルームID");
            // 番号は白い札に等幅の字で（JS版 .tCode）
            var cc = st.Card(48);
            var ct = UiKit.Label(cc, string.Join(" ", System.Array.ConvertAll(((string)r["code"]).ToCharArray(), ch => ch.ToString())),
                                 22, Mats.Hex(0x3a352c), true, TextAnchor.MiddleCenter);
            ct.font = Mono;
            UiKit.Stretch(ct.rectTransform);
            Note(st, host ? "あなたがホストです。全員そろったら始めてください。" : "ホストが始めるのを待っています…");
            Note(st, g.RoundLine());
            var pk = Lobby.rpick;
            Note(st, "遊ぶ面：<b>" + (pk != null ? (string)pk["name"] : "<i>まだ選んでいません</i>") + "</b>"
                   + (pk != null && pk["shopName"] != null ? "　<i>" + (string)pk["shopName"] + "</i>" : ""));
            if (host) Btn(st, "面を選ぶ ▶", "sec", () => { from = "Wait"; next = false; Go("Free"); }, "部屋の全員で遊ぶ面を選びます。", false, "pickstage");
            // ルール。決めるのはホスト（面と同じ）。決めたそばから全員へ配る
            bool team = Lobby.mode == "team";
            if (host)
                Btn(st, "ルール：" + Game.ModeName(Lobby.mode) + "　▾", "sec", () => PickOpen("ルールを選ぶ", null, new List<string[]>
                {
                    new[] { "versus", "個人戦（売上も修理費も個人ごと）", "versus" },
                    new[] { "team", "チーム戦（青と赤。売上も修理費もチームごと）", "team" },
                    new[] { "coop", "協力（売上も修理費も店舗ぜんぶ）", "coop" },
                }, Lobby.mode, "Wait", x => { Lobby.mode = x; Lobby.SendRule(); }), null, false, "rule");
            else Note(st, "ルール：<b>" + Game.ModeName(Lobby.mode) + "</b>");
            // 席。誰がどの色のロボになるかが、始める前に分かるように（並びはそのまま枠の順）。
            // 人が来ない席は、ホストがNPCで埋められる。人が入ってきたら、その席は人のもの（NPCは押し出される）
            var row = st.Row(92);
            float sw = (1098 - 3 * 10) / 4f;
            int npcN = 0;
            for (int i = 0; i < Mathf.Min(4, max); i++)
            {
                int ii = i;
                var m = i < players.Count ? players[i] : null;
                string v = m != null ? null : Lobby.rnpc[i] ?? "off";
                bool bot = m == null && v != "off";
                if (bot) npcN++;
                bool mHost = m != null && (bool?)m["host"] == true;
                string ic = m != null ? (mHost ? "host" : "solo") : bot ? "npc" : "off";
                string nm = m != null ? ((int?)m["id"] == Net.Id ? "あなた" : "参加者")
                          : bot ? SlotLabel(v) : "（空き）";
                string col = m != null || bot ? (team ? (Lobby.rteam[i] == 1 ? "red" : "blue") : SEAT_COL[i]) : "grey";
                string pn = (i + 1) + "P" + (team && (m != null || bot) ? "・" + Game.TEAM_NAME[Lobby.rteam[i]] : "");
                System.Action fire = null;
                if (m == null && host)
                    fire = () => PickOpen((ii + 1) + "P の席", "人が来ない席は<b>NPC</b>で埋められます。あとから人が入ったら、その人が入ります。",
                                          SeatOpts(), v, "Wait", x => { Lobby.rnpc[ii] = x; Lobby.SendNpc(); });
                Seat(row, i * (sw + 10), sw, pn, ic, nm, col, fire,
                     bot ? (i + 1) + "P は「" + SlotLabel(v) + "」です。押すと変えられます。" : (i + 1) + "P は空きです。押すとNPCを入れられます。", "seat" + i,
                     mHost ? "ホスト" : null);
            }
            // チーム分け（ホストだけ押せる）。人が居る席とNPCの席だけ並べる
            bool rOne = false;
            if (team)
            {
                var used = new List<int>();
                for (int i = 0; i < Mathf.Min(4, max); i++) if (i < players.Count || Lobby.rnpc[i] != "off") used.Add(i);
                int n0 = used.FindAll(i => Lobby.rteam[i] == 0).Count, n1 = used.Count - n0;
                rOne = used.Count > 1 && (n0 == 0 || n1 == 0);
                if (host && used.Count > 0)
                {
                    var r2 = st.Row(36);
                    float bw = 160, gap = 13, x0 = (1098 - (used.Count * bw + (used.Count - 1) * gap)) / 2;
                    for (int k = 0; k < used.Count; k++)
                    {
                        int i = used[k], t = Lobby.rteam[i];
                        BtnAt(r2, x0 + k * (bw + gap), 0, bw, 34, (i + 1) + "P：" + (t == 1 ? "赤" : "青"), t == 1 ? "tm1" : "tm0",
                              () => { Lobby.rteam[i] = 1 - t; Lobby.SendRule(); Go("Wait"); }, null, false, "rt" + i, 14);
                    }
                }
                Note(st, rOne ? "チーム戦は<b>青と赤に1人以上ずつ</b>入れてください" + (host ? "（上の 1P：青 などを押すと入れ替わります）。" : "。")
                              : "青 <b>" + n0 + "人</b> 対 赤 <b>" + n1 + "人</b>");
            }
            if (npcN > 0) Note(st, "<b>" + (players.Count + npcN) + "人</b>で遊びます（うち<b>" + npcN + "人</b>はNPC）。");
            Note(st, "<b>ホストが全部計算して、みんなに配ります。</b>始めると、部屋の人数ぶんのロボが出て、いっしょに遊べます。\n<i>途中で入り直すことはできません。</i>");
            var goB = Btn(st, host ? "始める" : "ホスト待ち", "go", RoomStart, null, false, "go");
            if (!host || Lobby.rpick == null || rOne) Off(goB);
            Btn(st, g.InSession ? "部屋を出る" : "戻る", "sec", () =>
                   {
                       System.Action leave = () => { Net.LeaveRoom(); g.NpEnd(); Go("Online"); };
                       if (!g.InSession) { leave(); return; }
                       // 続きの最中は、抜ける前に確かめる（押し間違えると、みんなの続きがその場で終わる）
                       ConfirmOpen("部屋を出ますか？", "いま <b>" + g.runDone + "面</b> まで遊んだところです。\n"
                                   + "出ると、<b>みんなの続きがここで終わります</b>（お金・アイテム・強化もなくなります）。",
                                   "部屋を出る", "出ない（続ける）", "Wait", leave);
                   }, null, true);
        }

        List<string[]> SeatOpts()
        {
            var o = new List<string[]>();
            foreach (var s in NPC_SEAT_OPTS) o.Add(new[] { s[0], s[1], SlotIc(s[0]) });
            return o;
        }

        /// <summary>ホストが始める。ランダムはここで引いて、決まった面を配る（全員が同じ面になるように）。</summary>
        void RoomStart()
        {
            var pk = Lobby.rpick;
            if (pk == null) return;
            if ((bool?)pk["random"] == true)
            {
                var pool = FreeList().FindAll(x => !x.missing && x.c != null);
                if (pool.Count == 0) return;
                var w = pool[Random.Range(0, pool.Count)];
                int si = w.shop != null ? Mathf.Max(0, Shops.All.IndexOf(Shops.Find(w.shop))) : 0;
                pk = new JObject { ["ref"] = w.refKey, ["name"] = w.n, ["shop"] = si };
            }
            // gn … 何面目か。便りを捨てるかどうかの判断に使う番号なので、ゲスト側もこの数字に合わせる
            Net.StartRoom(new JObject
            {
                ["pick"] = pk, ["cont"] = g.np.stage > 0, ["gn"] = g.np.stage,
                ["npc"] = new JArray(Lobby.rnpc), ["mode"] = Lobby.mode, ["team"] = new JArray(Lobby.rteam),
                ["w"] = g.WalletsJson(),
            });
        }

        // ---------------------------------------------------------------- 文字を入れる
        string PageAsk(Stack st)
        {
            var A = ask;
            if (A == null) { PageTop(st); return null; }
            Head(st, A.title);
            if (A.note != null) Note(st, A.note);
            var r = st.Row(52);
            var bg = UiKit.Img(r, Color.white, 10, "Input");
            bg.raycastTarget = true;
            var br = bg.rectTransform; br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f); br.sizeDelta = new Vector2(520, 48);
            var txt = UiKit.Label(bg.transform, "", 22, INK, true, TextAnchor.MiddleLeft);
            txt.supportRichText = false; txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Stretch(txt.rectTransform); txt.rectTransform.offsetMin = new Vector2(16, 0); txt.rectTransform.offsetMax = new Vector2(-16, 0);
            var ph = UiKit.Label(bg.transform, A.digits ? "123456789012" : "ホスト名:ポート", 22, new Color(0, 0, 0, 0.25f), true, TextAnchor.MiddleLeft);
            UiKit.Stretch(ph.rectTransform); ph.rectTransform.offsetMin = new Vector2(16, 0); ph.rectTransform.offsetMax = new Vector2(-16, 0);
            var inp = bg.gameObject.AddComponent<InputField>();
            inp.textComponent = txt; inp.placeholder = ph;
            inp.characterLimit = A.digits ? 12 : 120;
            if (A.digits) inp.contentType = InputField.ContentType.IntegerNumber;
            inp.text = A.value;
            inp.caretColor = INK; inp.customCaretColor = true;
            A.inp = inp;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(bg.gameObject);
            inp.ActivateInputField();
            inp.MoveTextEnd(false);
            BtnRow(st, new[] { "決める", "やめる" }, new[] { "go", "sec" }, new System.Action[] { AskOk, () => Go(A.back) }, new[] { false, false });
            return null;
        }

        void AskOk()
        {
            var A = ask;
            if (A == null) return;
            string v = A.inp != null ? A.inp.text.Trim() : A.value;
            if (A.digits) v = System.Text.RegularExpressions.Regex.Replace(v, "\\D", "");
            A.onOk(v);
            Go(A.back);
        }
    }
}
