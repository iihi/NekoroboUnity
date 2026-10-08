using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// ストーリー。JS版の talkOpen（会話）・tutBegin / tutStep / tutTarget / tutDone / tutTick（遊んでいる間の案内）・
    /// saveStory / loadStory（続きから）を写したもの。画面は StoryUi。
    ///
    /// 案内はステージの hints に1つずつ順に並べる。
    ///   { "say":"文", "arrow":"counter", "until":"pick", "max":12, "ok":"いいぞ！", "need":"items" }
    /// 出すのは、この画面で遊ぶ人が1人のときだけ（NPC のライバルは数えない）。
    /// </summary>
    public partial class Game
    {
        [Header("遊んでいる間の案内を出す（JS版 ?hints=0 で切れる物）")]
        public bool hints = true;

        // ================================================================ 会話
        public bool TalkOn { get { return hud != null && hud.Story.TalkOn; } }

        static List<string[]> Lines(JToken t)
        {
            var a = t as JArray;
            if (a == null) return null;
            var o = new List<string[]>();
            foreach (var x in a)
            {
                var r = x as JArray;
                if (r != null && r.Count >= 2) o.Add(new[] { (string)r[0], (string)r[1] });
            }
            return o.Count > 0 ? o : null;
        }

        /// <summary>会話を出す。読み終わるまでゲームは止まる。</summary>
        public void TalkOpen(JToken lines, System.Action done, string title)
        {
            var ls = Lines(lines);
            if (ls == null) { if (done != null) done(); return; }
            hud.Story.TutHide();
            hud.Story.TalkOpen(ls, () => { TutRender(false, false); if (done != null) done(); }, title);
        }

        // ================================================================ 案内
        class Tut
        {
            public JArray steps; public int i; public float t, okT; public Player P;
            public int del; public float dmg, hurt, jump; public string slot; public int items;
            public Dictionary<char, List<Vector3>> tiles;
        }
        Tut tut;
        Transform tutArrow;

        void TutBegin()
        {
            TutEnd();
            var humans = players.FindAll(P => P.src.kind == "key" || P.src.kind == "pad");
            if (!hints || stage.hints == null || stage.hints.Count == 0 || humans.Count != 1) return;
            tut = new Tut { steps = stage.hints, P = humans[0], tiles = new Dictionary<char, List<Vector3>>() };
            // 地形の文字ごとのマス（「凍りの床を指す」のように使う）
            foreach (var kv in map)
            {
                List<Vector3> l;
                if (!tut.tiles.TryGetValue(kv.Value, out l)) tut.tiles[kv.Value] = l = new List<Vector3>();
                l.Add(Coord.Cell(kv.Key.x, kv.Key.y));
            }
            // 矢印。下向きの四角錐を縁取りして、どこから見ても分かるようにする（何にも隠れない）
            var g = new GameObject("TutArrow").transform;
            g.SetParent(stageRoot, false);
            var sh = Resources.Load<Shader>("Shaders/Overlay");
            if (sh == null) sh = Shader.Find("Nekorobo/Overlay");
            System.Action<float, float, int, bool, int> mk = (r, h, col, back, q) =>
            {
                var m = new Material(sh); m.SetColor("_Color", Mats.Hex(col));
                m.SetFloat("_Cull", back ? 1 : 2); m.renderQueue = 4000 + q;
                var p = Part.Add(g, MeshGen.Cone(r, h, 4), m, Vector3.zero, shadow: false);
                p.transform.localRotation = Quaternion.Euler(180, 45, 0);
            };
            mk(0.42f, 0.84f, 0x2f2a22, true, 0);
            mk(0.34f, 0.68f, 0xffc83a, false, 1);
            g.gameObject.SetActive(false);
            tutArrow = g;
            TutStep();
        }

        void TutEnd()
        {
            if (tutArrow != null) Destroy(tutArrow.gameObject);
            tutArrow = null;
            tut = null;
            if (hud != null) hud.Story.TutHide();
        }

        int ItemTotal(Player P)
        {
            var W = WalletOf(P); int n = 0;
            foreach (var kv in W.items) n += kv.Value;
            foreach (var kv in W.ammo) n += kv.Value;
            return n;
        }

        void TutStep()
        {
            var P = tut.P; var W = WalletOf(P);
            // 前提が満たせない案内は飛ばす（アイテムを買わなかった人に「使ってみよう」は出さない）
            int kinds = 0;
            foreach (var it in ITEMS) if (W.Has(it.k) > 0 || (W.ammo.ContainsKey(it.k) && W.ammo[it.k] > 0)) kinds++;
            while (tut.i < tut.steps.Count)
            {
                var s = tut.steps[tut.i] as JObject;
                string need = s != null ? J.S(s, "need") : null;
                if (need == null) break;
                if (need == "items" && kinds >= 1) break;
                if (need == "items2" && kinds >= 2) break;
                tut.i++;
            }
            if (tut.i >= tut.steps.Count) { hud.Story.TutHide(); return; }
            tut.t = 0; tut.okT = 0;
            tut.del = P.delivered; tut.dmg = shopDmg; tut.hurt = P.botDmg; tut.jump = P.lastJump;
            tut.slot = P.slot; tut.items = ItemTotal(P);
            TutRender(false, true);
        }

        void TutRender(bool ok, bool pop)
        {
            if (tut == null || tut.i >= tut.steps.Count || TalkOn) { if (hud != null && tut == null) hud.Story.TutHide(); return; }
            var s = tut.steps[tut.i] as JObject;
            int n = tut.steps.Count;
            hud.Story.TutShow(ok ? "◎ " + (J.S(s, "ok") ?? "いいぞ！") : (J.S(s, "say") ?? ""),
                              n > 1 && !ok ? (tut.i + 1) + " / " + n : "", ok, pop);
        }

        /// <summary>
        /// 矢印で指す物の位置（上の高さ y も返す）。JS版 tutTarget。
        ///   counter … 受取台　order … 運んでいる料理の届け先（手ぶらなら受取台）　guest … 次に届ける客
        ///   bench … いちばん近いベンチ　rival … いちばん近いライバル　ramp / mover / raft / car / train … いちばん近いそれ
        ///   tile:X … 地形の文字 X のいちばん近いマス　zone:N … ステージの zones の N 番目のまん中　[i, j] … そのマス
        /// </summary>
        Vector3? TutTarget(JToken spec)
        {
            if (spec == null || spec.Type == JTokenType.Null) return null;
            var P = tut.P; var p = P.ent.rb.position;
            var arr = spec as JArray;
            if (arr != null && arr.Count >= 2) return Coord.Cell((int)arr[0], (int)arr[1]) + Vector3.up * 0.4f;
            var str = (string)spec;
            if (string.IsNullOrEmpty(str)) return null;
            int c0 = str.IndexOf(':');
            string k = c0 >= 0 ? str.Substring(0, c0) : str, a = c0 >= 0 ? str.Substring(c0 + 1) : null;
            System.Func<IEnumerable<Vector3>, Vector3?> near = list =>
            {
                Vector3? best = null; float bd = 1e9f;
                foreach (var q in list)
                {
                    float d = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                    if (d < bd) { bd = d; best = q; }
                }
                return best;
            };
            System.Func<Ent, Vector3> at = e => e.Pos + Vector3.up * (e.size.y > 0 ? e.size.y / 2 : 0.5f);
            System.Func<Ent, Vector3> guestAt = gu => gu.Pos + Vector3.up * (Mathf.Max(0.62f, gu.modelH - 0.52f + 0.22f) + 0.35f);
            System.Func<Vector3> counter = () => new Vector3(counterEnt != null ? counterEnt.Pos.x : counterPos.x, CounterH + 0.5f,
                                                             counterEnt != null ? counterEnt.Pos.z : counterPos.z);
            switch (k)
            {
                case "counter": return counter();
                case "order": return P.carried != null ? guestAt(P.carried.order.guest) : counter();
                case "guest":
                    {
                        var o = P.carried != null ? P.carried.order : (oi < orders.Count ? orders[oi] : null);
                        return o != null ? guestAt(o.guest) : (Vector3?)null;
                    }
                case "bench": return near(furni.FindAll(e => e.bench).ConvertAll(e => at(e)));
                case "rival": return near(players.FindAll(q => q != P && !q.down).ConvertAll(q => at(q.ent)));
                case "ramp": return near(ents.FindAll(e => e.kind == "ramp").ConvertAll(e => new Vector3(e.Pos.x, 0.9f, e.Pos.z)));
                case "mover": return near(movers.ConvertAll(m => at(m.ent)));
                case "raft": return near(rafts.ConvertAll(r => r.R != null ? new Vector3(r.R.pos.x, 0.4f, r.R.pos.z) : new Vector3(r.cx, 0.4f, r.cz)));
                case "car": return near(cars.FindAll(c => c.vk != "train").ConvertAll(c => at(c.ent)));
                case "train": return near(cars.FindAll(c => c.vk == "train").ConvertAll(c => at(c.ent)));
                case "tile":
                    {
                        List<Vector3> l;
                        if (string.IsNullOrEmpty(a) || !tut.tiles.TryGetValue(a[0], out l)) return null;
                        return near(l.ConvertAll(q => new Vector3(q.x, 0.3f, q.z)));
                    }
                case "zone":
                    {
                        int zi = 0; int.TryParse(a ?? "0", out zi);
                        if (zi < 0 || zi >= zones.Count) return null;
                        var Z = zones[zi];
                        return Coord.W((Z.i0 + Z.i1 + 1) / 2f, 0.3f, (Z.j0 + Z.j1 + 1) / 2f);
                    }
                case "me": return at(P.ent);
            }
            return null;
        }

        /// <summary>
        /// 次へ進む条件（JS版 tutDone）。
        ///   pick … 料理を受け取った　serve … 届けた（serve:3 なら通算3つ）　hit … 家具などを壊した　hurt … 自分が傷んだ
        ///   jump … ジャンプした　air … 高く飛んだ　ice / lava / water … その床・池に入った　sea … 海に落ちた
        ///   raft … いかだに乗った　near:… … そこへ近づいた　zone:N … その場所に入った　item … アイテムを使った
        ///   cycle … 持ち替えた　time:N … N 秒たった
        /// </summary>
        bool TutDone(JObject s)
        {
            var P = tut.P;
            var mx = J.FN(s, "max");
            if (mx != null && mx.Value > 0 && tut.t >= mx.Value) return true;
            string until = J.S(s, "until");
            if (until == null) return false;
            int c0 = until.IndexOf(':');
            string k = c0 >= 0 ? until.Substring(0, c0) : until, a = c0 >= 0 ? until.Substring(c0 + 1) : null;
            float nv; bool hasN = float.TryParse(a, out nv);
            var p = P.ent.rb.position;
            char ch = Tiles.At(under ?? map, p);
            switch (k)
            {
                case "time": return tut.t >= (hasN && nv > 0 ? nv : 3);
                case "pick": return P.carried != null;
                case "serve": return hasN ? P.delivered >= nv : P.delivered > tut.del;
                case "hit": return shopDmg > tut.dmg + 0.3f;
                case "hurt": return P.botDmg > tut.hurt + 0.5f;
                case "jump": return P.lastJump > tut.jump;
                case "air": return P.airborne && p.y > 0.95f;
                case "ice": return ch == 'i';
                case "lava": return ch == '^' && p.y < 0.3f;
                case "water": return (ch == '~' || ch == 'b') && p.y < 0.1f;
                case "sea": return P.revT > 0 || (ch == '@' && p.y < 0);
                case "raft": return rafts.Count > 0 && OverRaftNow(p) && p.y > -0.2f;
                case "near":
                    {
                        var t = TutTarget(a);
                        return t != null && new Vector2(t.Value.x - p.x, t.Value.z - p.z).magnitude < 2.2f;
                    }
                case "zone":
                    {
                        int zi = hasN ? (int)nv : 0;
                        if (zi < 0 || zi >= zones.Count) return false;
                        var Z = zones[zi]; float jz = -p.z;
                        return p.x >= Z.i0 && p.x < Z.i1 + 1 && jz >= Z.j0 && jz < Z.j1 + 1;
                    }
                case "item": return ItemTotal(P) < tut.items;
                case "cycle": return P.slot != tut.slot;
            }
            return false;
        }

        /// <summary>1/60 秒ごと（FixedUpdate）。できたら「いいぞ！」を少し出して次へ。</summary>
        void TutTick(float dt)
        {
            if (tut == null || tut.i >= tut.steps.Count) return;
            if (state != "play") return;
            if (tut.okT > 0)
            {
                tut.okT -= dt;
                if (tut.okT <= 0)
                {
                    tut.i++;
                    if (tut.i < tut.steps.Count) TutStep();
                    else hud.Story.TutHide();
                }
                return;
            }
            tut.t += dt;
            if (TutDone(tut.steps[tut.i] as JObject)) { tut.okT = 1.1f; TutRender(true, true); }
        }

        /// <summary>矢印を指す物の上で跳ねさせる（毎フレーム）。できた合図を出している間と結果では消す。</summary>
        void TutArrowTick()
        {
            if (tut == null || tutArrow == null) return;
            bool live = state != "result" && tut.i < tut.steps.Count;
            if (!live) { tutArrow.gameObject.SetActive(false); hud.Story.TutHide(); return; }
            var s = tut.steps[tut.i] as JObject;
            Vector3? tg = tut.okT > 0 || s == null ? null : TutTarget(s["arrow"]);
            tutArrow.gameObject.SetActive(tg != null);
            if (tg != null) tutArrow.position = tg.Value + Vector3.up * (0.75f + Mathf.Abs(Mathf.Sin(t * 4.2f)) * 0.32f);
        }

        // ================================================================ 続きから（ストーリーの途中経過）
        const string STORY_KEY = "nekorobo.story";

        /// <summary>面の進みとお金・強化をまるごと覚える（面が変わるたび）。</summary>
        void SaveStory()
        {
            if (entry == null || course == null) return;
            var ws = new JArray();
            foreach (var W in wallets)
                ws.Add(new JObject { ["cash"] = W.cash, ["up"] = JObject.FromObject(W.up), ["items"] = JObject.FromObject(W.items),
                                     ["ammo"] = JObject.FromObject(W.ammo), ["bought"] = JObject.FromObject(W.bought) });
            var o = new JObject { ["course"] = courseName, ["total"] = course.stages.Count, ["stage"] = courseIndex,
                                  ["done"] = runDone, ["wallets"] = ws, ["at"] = System.DateTime.Now.ToString("s") };
            PlayerPrefs.SetString(STORY_KEY, o.ToString(Newtonsoft.Json.Formatting.None));
            PlayerPrefs.Save();
        }

        public JObject LoadStory()
        {
            try
            {
                var s = PlayerPrefs.GetString(STORY_KEY, "");
                if (s.Length == 0) return null;
                var o = JObject.Parse(s);
                return o["stage"] != null && o["wallets"] is JArray ? o : null;
            }
            catch { return null; }
        }

        public void ClearStory() { PlayerPrefs.DeleteKey(STORY_KEY); PlayerPrefs.Save(); }

        /// <summary>覚えておいた財布を戻す（続きから）。</summary>
        void RestoreWallets(JArray ws)
        {
            NewWallets();
            for (int i = 0; i < wallets.Length && i < ws.Count; i++)
            {
                var o = ws[i] as JObject; if (o == null) continue;
                var W = wallets[i];
                W.cash = J.F(o, "cash", W.cash);
                foreach (var pair in new[] { new { d = W.up, k = "up" }, new { d = W.items, k = "items" }, new { d = W.ammo, k = "ammo" }, new { d = W.bought, k = "bought" } })
                {
                    var src = o[pair.k] as JObject; if (src == null) continue;
                    foreach (var p in src.Properties()) pair.d[p.Name] = (int)p.Value;
                }
            }
        }

        /// <summary>ストーリーを始める（cont なら続きから）。JS版 titleStart("story")。</summary>
        public void StartStory(bool cont)
        {
            runDone = 0; shopped = false;
            int at = 0;
            var sv = cont ? LoadStory() : null;
            if (sv != null)
            {
                RestoreWallets((JArray)sv["wallets"]);
                at = (int)sv["stage"]; runDone = J.I(sv, "done", at);
            }
            else NewWallets();
            LoadCourseStage(at);
        }
    }
}
