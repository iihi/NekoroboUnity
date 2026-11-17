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
            new[] { "off", "なし" }, new[] { "npc-normal", "NPC" }, new[] { "npc-easy", "NPC・かんたん" },
        };

        /// <summary>つながり具合の1行。パッドしか無い人にも分かるように、状態を文で出す（JS版 netLine）。</summary>
        static string NetLine()
        {
            string where = "<i>" + Net.Label + "：" + Net.Where + "</i>";
            if (Net.State == "on") return "つながっています。" + (Net.Id >= 0 ? "（番号 " + Net.Id + "）" : "") + "\n" + where;
            if (Net.State == "connecting") return "つないでいます…\n" + where;
            string bad = "<color=#ff9e6b><b>つながっていません</b></color>" + (Net.Why.Length > 0 ? "（" + Net.Why + "）" : "") + "\n" + where;
            if (!Net.NeedsAddr) return bad + "\nネットにつながっているか確かめて、「もう一度つなぐ」を押してください。";
            return bad
                 + "\n部屋サーバー（HTML版の <b>node server.js</b> か <b>node rooms-server.js</b>）が動いているか確かめてください。"
                 + "別の場所で動かしているなら、下の<b>「サーバ」</b>にそのマシンを入れてください。";
        }

        void PageOnline(Stack st)
        {
            Net.Connect();
            bool on = Net.State == "on";
            Head(st, "オンライン");
            Note(st, NetLine());
            // つなぎ先の欄は、それが要る仕組み（部屋サーバー）のときだけ出す
            if (Net.NeedsAddr)
            Btn(st, "サーバ：" + (Net.Addr.Length > 0 ? Net.Addr : Net.AddrFromCfg ? "置いた先の設定（net.json）" : "localhost:8123（開発の置き方）") + "　▾", "sec",
                () => AskOpen("サーバを入れる",
                    "ふつうは空のままでよいです（<b>localhost:8123</b>＝このPCで動かした HTML版の node server.js につなぎます）。\n"
                    + "部屋サーバーを別のマシンで動かしているときは、<b>ホスト名:ポート</b>（例 <b>192.168.0.10:8124</b>）で入れてください。",
                    Net.Addr, "Online", v => { Net.SetAddr(v); Net.Close(); Net.Connect(); }),
                "つなぐ先の部屋サーバーを変えます（この端末で覚えます）。", false, "srv");
            // つなぎ方（部屋サーバー・EOS）。この端末で覚える。部屋は仕組みごとに別なので、相手と同じにすること
            // EOS の SDK を入れていない（NEKOROBO_EOS が無い）ときは選べる物が1つなので出さない
            if (new List<string>(Net.Kinds).Count > 1)
            Btn(st, "つなぎ方：" + Net.Label + "　▾", "sec", () =>
                {
                    var ks = new List<string>(Net.Kinds);
                    Net.SetKind(ks[(ks.IndexOf(Net.Kind) + 1) % ks.Count]);
                    Net.Connect();
                    Go("Online");
                },
                "部屋サーバー（HTML版とも遊べる）と EOS（サーバー要らず・Unity版どうし）を切り替えます。相手と同じにしてください。", false, "kind");
            // 自動マッチング（担当「ルーム番号を選んで入るのではなく、自動マッチングに。番号で入るプライベートは残す」）
            Tiles(st,
                Tile("オンラインマッチ", "red", "versus", () => Go("MatchLen"), "自動で対戦相手を探します。個人戦。遊び方を選んで、面はそろってから全員で投票します。", "match", !on),
                Tile("プライベート", "blue", "join", () => Go("Private"), "友達と遊びます。誰かが部屋を作って、ほかの人はルームIDを入れて参加します。", "priv", !on));
            Btn(st, on ? "つなぎ直す" : "もう一度つなぐ", "sec", () => { Net.Close(); Net.Connect(); Go("Online"); }, null, false, "redo");
            Btn(st, "戻る", "sec", () => Go("Multi"), null, true);
        }

        /// <summary>部屋を作っている所（作れたら Go が待機へ回す）。</summary>
        void PageMake(Stack st)
        {
            Head(st, "ルームを作る");
            Note(st, NetLine() + "\n作っています…");
            Btn(st, "戻る", "sec", () => Go("Private"), null, true);
        }

        void PageJoin(Stack st)
        {
            Head(st, "ルームに参加");
            Note(st, NetLine());
            if (Net.Why.Length > 0) Note(st, "<color=#ff9e6b><b>" + Net.Why + "</b></color>");
            // 公開の部屋の一覧は無くした（知らない人どうしはオンラインマッチで）
            Btn(st, "番号で入る：" + (Lobby.code.Length > 0 ? Lobby.code : "（未入力）") + "　▾", "sec",
                () => AskOpen("ルームIDを入れる", "12桁の番号です。相手に聞いて入れてください。", Lobby.code, "Join", v => Lobby.code = v, true),
                null, false, "code");
            var go = Btn(st, "この番号で入る", "", () => Net.JoinRoom(Lobby.code), null, false, "go");
            if (Lobby.code.Length == 0) Off(go);
            Btn(st, "戻る", "sec", () => Go("Private"), null, true);
        }

        static Font mono;
        /// <summary>等幅の字（ルームID）。</summary>
        static Font Mono { get { return mono != null ? mono : (mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "MS Gothic" }, 22)); } }

        void Off(Item it)
        {
            it.off = true;
            // Unity の部品は ?? で比べられない（無いときも null に見えない）ので、明示して比べる
            var cg = it.rt.GetComponent<CanvasGroup>();
            if (cg == null) cg = it.rt.gameObject.AddComponent<CanvasGroup>();
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
            // 番号は白い札に等幅の字で（JS版 .tCode）。「ルームID」の見出しは札の中へ（行を1つ減らす）
            var cc = st.Card(48);
            var ct = UiKit.Label(cc, string.Join(" ", System.Array.ConvertAll(((string)r["code"]).ToCharArray(), ch => ch.ToString())),
                                 22, Mats.Hex(0x3a352c), true, TextAnchor.MiddleCenter);
            ct.font = Mono;
            UiKit.Stretch(ct.rectTransform);
            {
                var lb = UiKit.Label(cc, Lang.Tr("ルームID"), 11, Mats.Hex(0x8a8274), true, TextAnchor.MiddleRight);
                var lr = lb.rectTransform; lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f); lr.pivot = new Vector2(1, 0.5f);
                lr.sizeDelta = new Vector2(200, 30); lr.anchoredPosition = new Vector2(-ct.preferredWidth / 2 - 18, 0);
            }
            // 席を1列（4行）にしたぶん、説明は1行にまとめる（HTML版と同じ）
            var pk = Lobby.rpick;
            Note(st, Lang.Tr(host ? "あなたがホストです。全員そろったら始めてください。" : "ホストが始めるのを待っています…") + "　"
                   + (g.InSession ? g.RoundLine() : Lang.Tr("遊び方：<b>{0}</b>", Game.LenName(Lobby.rounds))) + "　"
                   + Lang.Tr("遊ぶ面：<b>{0}</b>", pk != null ? Lang.Tr((string)pk["name"]) : Lang.Tr("<i>まだ選んでいません</i>"))
                   + (pk != null && pk["shopName"] != null ? "　<i>" + Lang.Tr((string)pk["shopName"]) + "</i>" : ""));
            bool team = Lobby.mode == "team";
            // ホストは「面を選ぶ」「ルール」「遊び方」を横1列に。参加者は字だけ
            if (host)
            {
                var acts = new List<System.Action<RectTransform, float, float>>();
                bool lenB = !g.InSession;
                int nb = lenB ? 3 : 2; float bw = 340, gap = 16, bx = (st.w - (bw * nb + gap * (nb - 1))) / 2;
                var row = st.Row(36);
                BtnAt(row, bx, 0, bw, 34, "面を選ぶ ▶", "sec", () => { from = "Wait"; next = false; Go("Free"); }, "部屋の全員で遊ぶ面を選びます。", false, "pickstage", 14);
                BtnAt(row, bx + (bw + gap), 0, bw, 34, Lang.Tr("ルール：{0}　▾", Lang.Tr(Game.ModeName(Lobby.mode))), "sec", () => PickOpen("ルールを選ぶ", null, new List<string[]>
                {
                    new[] { "versus", "個人戦（売上も修理費も個人ごと）", "versus" },
                    new[] { "team", "チーム戦（青と赤。売上も修理費もチームごと）", "team" },
                    new[] { "coop", "協力（売上も修理費も店舗ぜんぶ）", "coop" },
                }, Lobby.mode, "Wait", x => { Lobby.mode = x; Lobby.SendRule(); }), null, false, "rule", 14);
                if (lenB)
                {
                    var opts = new List<string[]> { new[] { "0", Game.LenName(0), "free" } };
                    foreach (var n in Game.MATCH_LENS) opts.Add(new[] { n.ToString(), Game.LenName(n), "cup" });
                    BtnAt(row, bx + (bw + gap) * 2, 0, bw, 34, Lang.Tr("遊び方：{0}　▾", Game.LenName(Lobby.rounds)), "sec", () => PickOpen(Lang.Tr("遊び方を選ぶ"), null, opts, Lobby.rounds.ToString(), "Wait",
                        x => { int v; Lobby.rounds = int.TryParse(x, out v) ? v : 0; Lobby.SendRule(); }), null, false, "len", 14);
                }
            }
            else Note(st, Lang.Tr("ルール：<b>{0}</b>", Lang.Tr(Game.ModeName(Lobby.mode))));
            // 席はローカルのユーザー選択と同じ行の形（担当の画面案。仮）。人が居る席は「OK」、空いている席は「接続中…」。
            // **人が来ない席は、ホストがNPCで埋められます**（右の「NPC」）。人が入ってきたら、その席は人のものになります
            int npcN = 0;
            for (int i = 0; i < Mathf.Min(4, max); i++) if (i >= players.Count && (Lobby.rnpc[i] ?? "off") != "off") npcN++;
            OlGrid(st, players, max, host, team, true);
            // チーム戦は青と赤に1人以上ずつ（チームは行の右の 青／赤）。NPC の人数と1行に
            bool rOne = false;
            var line = new List<string>();
            if (team)
            {
                var used = new List<int>();
                for (int i = 0; i < Mathf.Min(4, max); i++) if (i < players.Count || (Lobby.rnpc[i] ?? "off") != "off") used.Add(i);
                int n0 = used.FindAll(i => Lobby.rteam[i] == 0).Count, n1 = used.Count - n0;
                rOne = used.Count > 1 && (n0 == 0 || n1 == 0);
                line.Add(rOne ? Lang.Tr(host ? "チーム戦は<b>青と赤に1人以上ずつ</b>入れてください（右の 青／赤 を押すと入れ替わります）。" : "チーム戦は<b>青と赤に1人以上ずつ</b>入れてください。")
                              : Lang.Tr("青 <b>{0}人</b> 対 赤 <b>{1}人</b>", n0, n1));
            }
            if (npcN > 0) line.Add(Lang.Tr("<b>{0}人</b>で遊びます（うち<b>{1}人</b>はNPC）。", players.Count + npcN, npcN));
            if (line.Count > 0) Note(st, string.Join("　", line.ToArray()));
            // 戻ると始めるは横に並べる（ローカルの「キャンセル」「スタート」と同じ並び）。「ホストが全部計算して…」は「始める」の説明（下の帯）へ
            {
                var row = st.Row(52, 10);
                float bw = 400, gap = 22, bx = (st.w - bw * 2 - gap) / 2;
                BtnAt(row, bx, 9, bw, 36, g.InSession ? "部屋を出る" : "戻る", "sec", () =>
                {
                    System.Action leave = () => { Net.LeaveRoom(); g.NpEnd(); Go("Online"); };
                    if (!g.InSession) { leave(); return; }
                    // 続きの最中は、抜ける前に確かめる（押し間違えると、みんなの続きがその場で終わる）
                    ConfirmOpen("部屋を出ますか？", Lang.Tr("いま <b>{0}面</b> まで遊んだところです。<br>出ると、<b>みんなの続きがここで終わります</b>（お金・アイテム・強化もなくなります）。", g.runDone).Replace("<br>", "\n"),
                                "部屋を出る", "出ない（続ける）", "Wait", leave);
                }, null, true, "back", 14);
                var goB = BtnAt(row, bx + bw + gap, 0, bw, 48, host ? "始める" : "ホスト待ち", "go", RoomStart,
                                "ホストが全部計算して、みんなに配ります。始めると、部屋の人数ぶんのロボが出て、いっしょに遊べます。途中で入り直すことはできません。",
                                false, "go", 17);
                if (!host || Lobby.rpick == null || rOne) Off(goB);
            }
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
                ["rounds"] = Lobby.rounds, ["w"] = g.WalletsJson(),
                // ホストの調子の数値。全員この数値で遊ぶ（面ごとの上書きは各自の画面で乗せる）
                ["tune"] = g.TuneBaseJson(),
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
