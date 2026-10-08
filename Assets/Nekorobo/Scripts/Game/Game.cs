using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nekorobo
{
    /// <summary>
    /// ゲーム本体。JS版 index.html の G（ゲームの状態）と step()（1/60秒ごとの更新）にあたる。
    ///
    /// シーンにはこれを1つ置くだけ。床もロボも、遊び始めたときにステージのファイルから作る。
    /// 物理は 1/60 秒刻み（JS版と同じ。刻みが違うと減衰の効き方が変わって最高速がずれる）。
    ///
    /// 遊べる範囲（1面ぶん）：受け取り → 配達 → 結果。ぶつかり（客の吹っ飛び・家具の損壊・
    /// 機体の故障・料理の乱れ）、池・溶岩・海・穴、凍りの床、ジャンプ台、場所ごとの床。
    /// まだの物：アイテム・NPC・対戦・ショップ・会話と案内・車・動く床・いかだ・モデル・音。
    /// </summary>
    public partial class Game : MonoBehaviour
    {
        public static Game I;

        [Header("遊ぶ面。コース（stages/course.json）の何面目か")]
        public string courseName = "course";
        public int courseIndex = 0;
        [Header("コースを使わずに面を直接選ぶとき（stages/ のファイル名。空ならコース）")]
        public string stageFile = "";
        public string shopName = "カフェ";

        [Header("遊びの調子（JS版の右パネルと同じ3つ。F2 で切り替え）")]
        public Preset preset = Preset.普通;
        [Header("いま効いている数値（遊びながら触ってよい。R で作り直すと調子の値に戻る）")]
        public Tune T;
        public CamTune cam;
        public TuneFile TF;

        // ---- いまの面
        [System.NonSerialized] public StageCfg stage;
        [System.NonSerialized] public ShopDef shop;
        [System.NonSerialized] public CourseEntry entry;
        [System.NonSerialized] public Course course;
        public string stageTitle = "";

        // ---- 状態（JS版の G）
        public float t;
        public string state = "ready";     // ready / play / result
        public float readyT;
        public int frames;
        public float shopDmg;
        public int oi, done;
        [System.NonSerialized] public List<Ent> ents = new List<Ent>();
        [System.NonSerialized] public List<Ent> guests = new List<Ent>();
        [System.NonSerialized] public List<Ent> furni = new List<Ent>();
        [System.NonSerialized] public List<Player> players = new List<Player>();
        [System.NonSerialized] public List<Order> orders = new List<Order>();
        [System.NonSerialized] public Player me;
        [System.NonSerialized] public Dictionary<Vector2Int, char> map;
        [System.NonSerialized] public Result result;
        public float cash;                  // 所持金（1人ぶん）
        readonly Dictionary<long, float> hits = new Dictionary<long, float>();
        List<StageObj> spawnObjs = new List<StageObj>();
        Transform stageRoot;
        public Camera mainCam;
        Light sun;
        Hud hud;
        public float shake;
        int buildGen;
        Tune tuneBack;                      // ステージの上書きを当てる前の値                       // 面を組み直した回数（読み込みが終わる前に組み直したかを見る）

        public class Result
        {
            public bool cleared, rec;
            public float sales, repair, ambCost, dmg, wreck, total, bonus;
            public int amb, best, delivered;
        }

        // ================================================================ 起動
        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            // エディタが前に出ていないと、遊んでいる途中でも1フレームも進まなくなる（試作で踏んだ）。
            // 確かめるときにエディタを裏へ回すことが多いので、裏でも動かす
            Application.runInBackground = true;
            Time.fixedDeltaTime = 1f / 60f;            // JS版と同じ刻み
            Physics.gravity = new Vector3(0, -9.81f, 0);
            TF = TuneFile.Load();
            T = BaseTune();
            cam = TF.cam;
            cash = T.startCash;
            var mj = DataRoot.ReadJson("assets/models.json");
            if (mj != null) Designs.Register(mj["designs"] as Newtonsoft.Json.Linq.JObject);
            Debug.Log("[Nekorobo] データ: " + DataRoot.Path + "（" + TF.log + "）");
            SetupView();
            hud = gameObject.AddComponent<Hud>();
            hud.game = this;
        }

        async void Start()
        {
            // モデル（glb）は遊ぶ前に全部読む（JS版 loadAssets と同じ）。客は18人ぶん
            hud.Toast("モデルを読み込み中…");
            Props.LoadCatalog();                           // 置き物のカタログ（catalog.json・models.json の props）
            await ModelStore.LoadAll();
            await Scenery.LoadModels();                    // まわりの飾りのモデル（木・建物…）
            if (this == null) return;                      // 読んでいる間に止められた
            Debug.Log("[Nekorobo] モデル: " + string.Join(" ／ ", ModelStore.Log));
            course = Course.Load(courseName);
            if (string.IsNullOrEmpty(stageFile)) LoadCourseStage(courseIndex);
            else LoadFile(stageFile, shopName);
        }

        void OnDestroy() { if (I == this) I = null; }

        void SetupView()
        {
            mainCam = Camera.main;
            if (mainCam == null)
            {
                var cg = new GameObject("Main Camera");
                cg.tag = "MainCamera";
                mainCam = cg.AddComponent<Camera>();
                cg.AddComponent<AudioListener>();
            }
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = Mats.Hex(0xdfe6ee);
            mainCam.nearClipPlane = 0.5f;
            // 光。JS版：半球光（空 白／地面 灰青）＋ 太陽（右上手前から）
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
            if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Color.white;
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.LookRotation(-Coord.W(7, 16, 6).normalized);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.95f, 0.95f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.80f, 0.82f, 0.85f);
            RenderSettings.ambientGroundColor = Mats.Hex(0x9aa2ad) * 0.85f;
        }

        // ================================================================ 面の読み込み
        public void LoadCourseStage(int i)
        {
            if (course == null || course.stages.Count == 0)
            {
                Debug.LogWarning("[Nekorobo] コースが読めないので stage1 を遊びます");
                LoadFile("stage1", "カフェ");
                return;
            }
            courseIndex = Mathf.Clamp(i, 0, course.stages.Count - 1);
            entry = course.stages[courseIndex];
            var fn = entry.FileName;
            if (fn == null)
            {
                Debug.LogWarning("[Nekorobo] 内蔵の構成（configs.js）はまだ読めません: " + entry.cfg);
                return;
            }
            var c = StageCfg.Load(fn);
            if (c == null) return;
            stage = c;
            shop = Shops.Find(entry.shop) ?? Shops.All[0];
            stageTitle = "STAGE " + (courseIndex + 1) + "　" + (entry.title ?? c.n);
            Rebuild();
        }

        public void LoadFile(string file, string shopN)
        {
            var c = StageCfg.Load(file);
            if (c == null) return;
            entry = null;
            stage = c;
            shop = Shops.Find(shopN) ?? Shops.All[0];
            stageTitle = c.n + "（" + shop.n + "）";
            stageFile = file;
            Rebuild();
        }

        /// <summary>面を作り直す（R でやり直すときもここ）。</summary>
        public void Rebuild()
        {
            if (stageRoot != null) DestroyImmediate(stageRoot.gameObject);
            stageRoot = new GameObject("Stage").transform;
            ents.Clear(); guests.Clear(); furni.Clear(); players.Clear(); orders.Clear(); hits.Clear();
            cars.Clear(); movers.Clear(); routed.Clear(); walkers.Clear(); rafts.Clear(); objEnt.Clear(); routeMesh.Clear();
            routeT = 0; buildGen++; tailRoot = null; flowMats.Clear();
            warned.Clear();
            // ステージごとの数値の上書き（その面だけ）。前の面の上書きは必ず戻してから当てる（JS版 TUNE_BACK）
            if (tuneBack != null) { T.CopyFrom(tuneBack); tuneBack = null; }
            if (stage.tune != null) { tuneBack = T.Clone(); T.Apply(stage.tune); }
            t = 0; frames = 0; shopDmg = 0; oi = 0; done = 0; result = null; shake = 0;
            state = "ready"; readyT = 3.999f;
            ModelStore.ResetSeq(stage.n);                  // 人違いのモデルを配る順番を、面ごとに数え直す
            BuildStage();
            me = players.Count > 0 ? players[0] : null;
            foreach (var P in players) Say(P, "ガンバルにゃ！", 2.2f);
            SyncCounterPlates();
            FitCamera();
            hud.OnStage();
        }

        // ================================================================ ロボ
        void BuildPlayers(float fr)
        {
            // いまは1人（キーボード＋1台目のパッド）。NPC と多人数はあとで
            var P = new Player(0);
            Vector3 pos; float rot;
            SpawnAt(0, out pos, out rot);
            P.spawn = pos; P.spawnRot = rot;
            const float RH = 0.52f;
            var R = MakeBody("robot", pos + new Vector3(0, RH, 0), new Vector3(0.28f, RH, 0.24f), Coord.RotFace(rot),
                             false, 40, 0.02f, 0.12f, 0.2f, minFric: true);
            R.rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            R.player = P;
            P.ent = R;
            P.look = RobotLook.Build(R.transform, P.col, TF.bodyTint);
            // 足元のリング（向きの矢印つき）
            var ringG = new GameObject("Ring_" + P.name);
            ringG.transform.SetParent(stageRoot, false);
            var rm = Mats.Basic(new Color(Mats.Hex(P.col).r, Mats.Hex(P.col).g, Mats.Hex(P.col).b, 0.8f), true, true, false);
            var ring = Part.Add(ringG.transform, MeshGen.Ring(0.44f, 0.55f, 28), rm, Vector3.zero, shadow: false);
            ring.transform.localRotation = Part.Euler3(-Mathf.PI / 2, 0, 0);
            var arrow = Part.Add(ringG.transform, MeshGen.Circle(0.17f, 3), rm, Coord.W(0, 0, -0.62f), shadow: false);
            arrow.transform.localRotation = Part.Euler3(-Mathf.PI / 2, 0, Mathf.PI / 2);
            P.ring = ringG;
            players.Add(P);
        }

        static readonly Vector2[] SPAWN_SPREAD = { new Vector2(0, 0), new Vector2(0, 2), new Vector2(2, 0), new Vector2(2, 2) };

        void SpawnAt(int n, out Vector3 pos, out float rot)
        {
            if (n < spawnObjs.Count)
            {
                var o = spawnObjs[n];
                pos = Coord.Cell(o.i, o.j);
                rot = o.raw["rot"] != null ? o.rot : 270;
                return;
            }
            var b = spawnObjs.Count > 0 ? spawnObjs[0] : null;
            var bp = b != null ? Coord.Cell(b.i, b.j) : Coord.W(-4.5f, 0, -3.5f);
            rot = b != null && b.raw["rot"] != null ? b.rot : 270;
            var off = SPAWN_SPREAD[n % 4];
            var want = bp + Coord.W(off.x, 0, off.y);
            pos = Tiles.NearestFloor(map, want) ?? bp;
        }

        // ================================================================ 入力
        static BotInput ReadInput()
        {
            var i = new BotInput();
            var kb = Keyboard.current;
            if (kb != null)
            {
                i.up = kb.upArrowKey.isPressed;
                i.down = kb.downArrowKey.isPressed;
                i.left = kb.leftArrowKey.isPressed;
                i.right = kb.rightArrowKey.isPressed;
                i.jump = kb.spaceKey.isPressed;
                i.use = kb.zKey.isPressed;
                i.cycle = kb.xKey.isPressed;
                i.cycleBack = kb.cKey.isPressed;
            }
            // パッド（1台目）。A/R2＝加速、B/L2＝バック、X＝ジャンプ、Y＝アイテム（JS版と同じ割り当て）
            var gp = Gamepad.current;
            if (gp != null)
            {
                const float dz = 0.35f;
                var st = gp.leftStick.ReadValue();
                if (st.y > dz || gp.dpad.up.isPressed) i.up = true;
                if (st.y < -dz || gp.dpad.down.isPressed) i.down = true;
                if (st.x < -dz || gp.dpad.left.isPressed) i.left = true;
                if (st.x > dz || gp.dpad.right.isPressed) i.right = true;
                if (gp.buttonSouth.isPressed || gp.rightTrigger.ReadValue() > dz) i.up = true;
                if (gp.buttonEast.isPressed || gp.leftTrigger.ReadValue() > dz) i.down = true;
                if (gp.buttonWest.isPressed) i.jump = true;
                if (gp.buttonNorth.isPressed) i.use = true;
                if (gp.rightShoulder.isPressed) i.cycle = true;
                if (gp.leftShoulder.isPressed) i.cycleBack = true;
            }
            return i;
        }

        void Update()
        {
            if (stage == null || players.Count == 0) return;
            // 入力は毎フレーム拾う（FixedUpdate だと押した瞬間を取りこぼす）
            var inp = ReadInput();
            if (autoPlay && me != null) autoTarget = NextGoal();
            if (autoTarget != null && me != null) inp = AutoDrive(me, autoTarget.Value);
            foreach (var P in players) P.input = inp;

            var kb = Keyboard.current;
            if (kb != null && !hud.MenuOpen)
            {
                if (kb.rKey.wasPressedThisFrame) Rebuild();
                if (kb.f2Key.wasPressedThisFrame)
                {
                    SetPreset((Preset)((((int)preset) + 1 + 3) % 3));
                    hud.Toast(preset + "の調子にしました");
                }
                if (state == "result")
                {
                    if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Rebuild();
                    if (kb.nKey.wasPressedThisFrame) NextStage();
                }
            }
            var g = Gamepad.current;
            if (g != null && state == "result" && !hud.MenuOpen && g.buttonSouth.wasPressedThisFrame) NextStage();
        }

        // ---- 確かめる用の自動運転。外から（エディタのコマンドで）行き先を入れると、そこへ向かって走る。
        // 遊ぶときは使わない。NPC を移すまでのつなぎ。
        [System.NonSerialized] public Vector3? autoTarget;
        [System.NonSerialized] public bool autoPlay;       // 受け取り → 配達 を自動でくり返す

        /// <summary>受取位置（カウンターの正面）。</summary>
        public Vector3 PickupPoint() { return counterPos + counterRot * new Vector3(PickupOffset, 0, 0); }

        /// <summary>いま運んでいる料理の届け先（無ければ受取位置）。</summary>
        public Vector3 NextGoal()
        {
            if (me != null && me.carried != null) return me.carried.order.guest.rb.position;
            return PickupPoint();
        }

        BotInput AutoDrive(Player P, Vector3 to)
        {
            var i = new BotInput();
            var p = P.ent.rb.position;
            var d = to - p; d.y = 0;
            if (d.magnitude < 0.3f) return i;
            var f = P.ent.rb.rotation * Vector3.forward; f.y = 0;
            float ang = Vector3.SignedAngle(f, d, Vector3.up);   // 正 = 右回り
            if (ang > 8) i.right = true; else if (ang < -8) i.left = true;
            i.up = Mathf.Abs(ang) < 50;
            i.jump = P.ent.wet > 0.4f;                         // 池に落ちたら跳んで上がる
            return i;
        }

        /// <summary>調子を切り替える（数値を入れ直して、面を作り直す）。</summary>
        public void SetPreset(Preset p)
        {
            preset = p;
            tuneBack = null;
            T.CopyFrom(BaseTune());
            Rebuild();
            hud.RefreshSettings();
        }

        /// <summary>店舗を切り替える（右パネル。その場で作り直す）。</summary>
        public void CycleShop(int d)
        {
            int n = Shops.All.Count, i = Shops.All.IndexOf(shop);
            shop = Shops.All[((i + d) % n + n) % n];
            if (entry == null) stageTitle = stage.n + "（" + shop.n + "）";
            Rebuild();
        }

        public void OpenStageMenu() { hud.OpenStageMenu(); }

        public void ApplyShadows() { if (sun != null) sun.shadows = cam.shadow ? LightShadows.Soft : LightShadows.None; }

        /// <summary>
        /// tune.json の中身（HTML版の「既定として保存」と同じ形）。
        /// **Unity がまだ知らない項目は残す**（ファイルにある値を読んで、知っている項目だけ書き換える）。
        /// </summary>
        public Newtonsoft.Json.Linq.JObject TuneJson()
        {
            var j = DataRoot.ReadJson("tune.json") ?? new Newtonsoft.Json.Linq.JObject();
            if (j["_comment"] == null)
                j["_comment"] = new Newtonsoft.Json.Linq.JArray(
                    "右パネルの数値の既定値。ゲームは起動時にこれを読みます。",
                    "ゲーム画面の右パネル「この数値を既定として保存」で書き出せます。",
                    "知らないキーは無視されるので、要らない項目は消しても構いません。",
                    "このファイルを消すと、コードに書いてある内蔵の数値に戻ります。");
            var tj = j["tune"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            foreach (var p in T.ToJson().Properties()) tj[p.Name] = p.Value;
            j["tune"] = tj;
            var cj = j["camera"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            foreach (var p in cam.ToJson().Properties()) cj[p.Name] = p.Value;
            j["camera"] = cj;
            var gj = j["guest"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            gj["heads"] = (double)System.Math.Round(TF.heads, 4); gj["bodyTint"] = (double)System.Math.Round(TF.bodyTint, 4);
            j["guest"] = gj;
            return j;
        }

        /// <summary>tune.json へ書く（HTML版と同じファイル）。失敗したら理由を返す。</summary>
        public string SaveTune(out string path)
        {
            path = DataRoot.File_("tune.json");
            try
            {
                var text = TuneJson().ToString(Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
                TF.tune = T.Clone();
                return null;
            }
            catch (System.Exception e) { return e.Message; }
        }

        /// <summary>調子の数値（JS版 applyPreset：内蔵の数値へ戻してから、その調子のぶんを乗せる）。</summary>
        Tune BaseTune()
        {
            switch (preset)
            {
                case Preset.爽快: return Tune.Wild();
                case Preset.カスタム: return TF.tune.Clone();
                case Preset.普通: return new Tune();
                default: return T.Clone();                 // つまみを動かした後（どの調子でもない）
            }
        }

        public void NextStage()
        {
            if (entry != null && course != null && courseIndex + 1 < course.stages.Count) LoadCourseStage(courseIndex + 1);
            else Rebuild();
        }

        void LateUpdate()
        {
            if (stage == null) return;
            UpdateFlow(Time.deltaTime);
            SyncLooks();
            ApplyCamera();
        }

        // ================================================================ 1/60秒ごとの更新（JS版 step）
        void FixedUpdate()
        {
            if (stage == null || hud.MenuOpen) return;
            float dt = Time.fixedDeltaTime;
            t += dt;
            if (state == "ready")
            {
                readyT -= dt;
                if (readyT <= 0) state = "play";
            }
            if (state == "play") frames++;

            // ぶつかる直前の速度を控える（衝突の強さを相対速度で測るため）
            foreach (var e in ents) if (e.rb != null) e.prevV = e.rb.isKinematic ? e.prevV : e.rb.linearVelocity;

            if (state == "play") foreach (var P in players) Drive(P, dt);

            UpdateHazards(dt);
            // ここで Unity の物理が1歩進む（FixedUpdate のあと）。ぶつかりは OnContact へ届く。
            // 旋回の入れ直し（JS版の steerHold）は、次の Drive で「前の値から」数えて同じ効き方にしている。

            foreach (var P in players)
            {
                P.comboT -= dt;
                if (P.comboT <= 0) { P.combo = 0; P.comboDmg = 0; P.comboPaid = 0; }
                if (P.dcomboT > 0) { P.dcomboT -= dt; if (P.dcomboT <= 0) P.dcombo = 0; }
                P.msgT -= dt;
                if (P.msgT <= 0) P.msg = "";
                P.faceT -= dt;
                if (P.faceT <= 0 && P.face != "dead" && P.face != "happy") SetFace(P, "norm", 99);
            }

            if (state == "play")
            {
                foreach (var P in players)
                {
                    var R = P.ent;
                    var v = R.rb.linearVelocity;
                    float spd = new Vector2(v.x, v.z).magnitude;
                    var p = R.rb.position;
                    if (spd > 7 && FricAt(p, shop.fric) < 0.8f && P.msgT <= 0) Say(P, "止まらないにゃ～！！", 1.0f);
                    if (spd > 5) SetFace(P, "fast", 0.25f);
                    if (spd > 3 && P.msgT <= 0 && Tiles.IsSlippery(Tiles.At(map, p))) Say(P, "つるつるにゃ！！", 1.0f);
                    if (!P.down && p.y < -3)
                    {
                        MarkFell(R);
                        var sp = RescueSpot(R, p);
                        DropBack(R, sp, 0.6f);
                        hud.Mark(sp, P.col);
                    }
                }
                // 客が店の外まで吹っ飛んだら、店内へ引き戻してダメージ
                foreach (var gu in guests)
                {
                    if (gu.sunk) continue;
                    var gp = gu.rb.position;
                    var B = bounds;
                    if (gp.x < B.x0 - 1 || gp.x > B.x1 + 1 || gp.z < B.z0 - 1 || gp.z > B.z1 + 1)
                    {
                        gu.rb.position = new Vector3(Mathf.Clamp(gp.x, B.x0 + 1, B.x1 - 1), 0.6f, Mathf.Clamp(gp.z, B.z0 + 1, B.z1 - 1));
                        gu.rb.linearVelocity = Vector3.zero; gu.rb.angularVelocity = Vector3.zero;
                        if (gu.hp > 0)
                        {
                            gu.hp = Mathf.Max(0, gu.hp - GuestHurt(T.guestOut));
                            hud.Pop(gu.rb.position + Vector3.up * 0.8f, "場外！", gu.hp <= 0 ? 0xe53935 : 0xff8a3d, true);
                            if (gu.hp <= 0) GuestDown(gu);
                        }
                    }
                }
                TickRevive(dt);
                foreach (var P in players) if (!P.down) CheckPickupDelivery(P);
            }
        }

        // ---- 強化（ショップを移すまでは全部 Lv0）
        float EffThrust(Player P) { return T.thrust; }
        float EffReverse(Player P) { return Mathf.Min(1f, T.reverseRatio); }
        float EffBotDmg(Player P) { return T.botDmg / Mathf.Max(0.05f, T.botTough); }
        float EffJumpSpd(Player P) { return T.jumpSpeed; }
        float EffJumpCd(Player P) { return Mathf.Max(0.18f, T.jumpCooldown); }
        float EffLight(Player P) { return 1f; }
        float GuestHurt(float v) { return v / Mathf.Max(0.05f, T.guestTough); }
        float Toughed(float v) { return v / Mathf.Max(0.05f, T.botTough); }
        static bool InvOn(Player P) { return P != null && P.invT > 0; }

        bool IsGrounded(Player P)
        {
            // JS版は「足元の少し下」から撃つ。Unity は物の内側から撃ったレイが当たらないので、
            // 機体の中心から下へ撃つ（自分の当たり判定は内側なので拾わない）
            return Physics.Raycast(P.ent.rb.position, Vector3.down, 0.52f + 0.16f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>1台ぶんの操作（JS版 step の中の「台数ぶん操作する」）。</summary>
        void Drive(Player P, float dt)
        {
            var R = P.ent; var rb = R.rb;
            if (P.down) { P.steerHold = null; return; }
            var inp = P.input;
            // ---- 旋回。JS版は 左 = +1（上から見て反時計回り）。Unity の y 回転は向きが逆なので、
            //      計算は JS版の向きのまま行い、入れるときだけ符号を変える
            bool slipping = P.slipT > 0;
            if (slipping) P.slipT -= dt;
            int steer = 0;
            if (!slipping)
            {
                if (inp.left && P.broken != "left") steer += 1;
                if (inp.right && P.broken != "right") steer -= 1;
            }
            // 物理演算のあとに入れ直していた値があれば、そこから数える（JS版の steerHold と同じ効き方）
            float av = P.steerHold ?? -rb.angularVelocity.y;
            float wy = av + steer * T.steerAccel * dt;
            if (steer == 0) wy *= Mathf.Exp(-4.5f * dt);
            wy = Mathf.Clamp(wy, -T.steerMax, T.steerMax);
            if (slipping) wy = P.slipSpin;
            var a3 = rb.angularVelocity;
            rb.angularVelocity = new Vector3(a3.x, -wy, a3.z);
            P.steerHold = steer != 0 ? wy : (float?)null;

            // ---- 前方向（Unity の +Z）
            var fwd = rb.rotation * Vector3.forward; fwd.y = 0; fwd.Normalize();
            // ---- 接地・ジャンプ・着地
            bool grounded = IsGrounded(P);
            if (inp.jump && grounded && t - P.lastJump > EffJumpCd(P))
            {
                var jv = rb.linearVelocity;
                rb.linearVelocity = new Vector3(jv.x, EffJumpSpd(P), jv.z);
                P.lastJump = t; P.airborne = true; SetFace(P, "fast", 0.4f);
            }
            if (P.airborne && grounded && t - P.lastJump > 0.12f)
            {
                P.airborne = false;
                float fall = Mathf.Max(0, -P.prevVy);
                if (fall > 1.5f)
                {
                    if (P.carried != null && !InvOn(P))
                        P.carried.integ = Mathf.Max(0, P.carried.integ - fall * 2.2f * P.carried.dish.frag);
                    shake = Mathf.Min(12, shake + fall * 1.2f);
                }
            }
            var tp = rb.position;
            bool onIce = grounded && Tiles.IsSlippery(Tiles.At(map, tp));

            float th = 0;
            if (inp.up) th = 1; else if (inp.down) th = -EffReverse(P);
            // 駆動系の故障。短い間隔で火が入ったり入らなかったりして、ガクガク進む
            if (P.brokenDrive)
            {
                P.misT -= dt;
                if (P.misT <= 0)
                {
                    P.misfire = !P.misfire;
                    P.misT = P.misfire ? 0.08f + Random.value * 0.10f : 0.16f + Random.value * 0.26f;
                }
                if (P.misfire) { th = 0; if (P == me) shake = Mathf.Min(6, shake + 22 * dt); }
            }
            float eff = (1 - 0.35f * (P.botDmg / 100f)) * (grounded ? 1 : T.airControl);
            var v = rb.linearVelocity;
            float push = EffThrust(P) * (onIce ? T.icePush : 1) * DragAt(tp);
            float nvx = v.x + fwd.x * th * push * eff * dt;
            float nvz = v.z + fwd.z * th * push * eff * dt;
            // ---- 前後と横に分けて、別々に減衰させる
            var right = new Vector3(fwd.z, 0, -fwd.x);
            float vf = nvx * fwd.x + nvz * fwd.z;
            float vl = nvx * right.x + nvz * right.z;
            if (grounded)
            {
                if (!slipping) vl *= Mathf.Exp(-T.lateralGrip * FricAt(tp, shop.fric) * (onIce ? T.iceGrip : 1) * dt);
                vf *= Mathf.Exp(-T.linDamp * (onIce ? T.iceDrag : 1) * DragAt(tp) * dt);
            }
            rb.linearVelocity = new Vector3(fwd.x * vf + right.x * vl, v.y, fwd.z * vf + right.z * vl);
            P.prevVy = v.y;
        }

        // ================================================================ 受け取り・配達
        void CheckPickupDelivery(Player P)
        {
            var p = P.ent.rb.position;
            if (P.carried == null && oi < orders.Count)
            {
                var lp = Quaternion.Inverse(counterRot) * (p - counterPos);
                if (Mathf.Abs(lp.x - PickupOffset) < PickupW / 2 + 0.7f && Mathf.Abs(lp.z) < PickupD / 2 + 0.7f)
                {
                    var ord = orders[oi];
                    ord.taker = P;
                    P.carried = new Carried { dish = ord.dish, integ = 100, order = ord };
                    P.look.ShowDish(ord.dish);
                    Say(P, "ピックアップにゃ！", 1.1f);
                    oi++;
                    SyncCounterPlates();
                }
            }
            if (P.carried != null)
            {
                var tg = P.carried.order.guest.rb.position;
                if (new Vector2(tg.x - p.x, tg.z - p.z).magnitude < 1.15f)
                {
                    DeliverDish(P, P.carried.order, P.carried.integ, tg);
                    P.carried = null;
                    P.look.ShowDish(null);
                }
            }
        }

        void DeliverDish(Player P, Order order, float integ, Vector3 at)
        {
            if (order.done) return;
            float tier = Dishes.PriceTier(integ);
            P.dcombo = P.dcomboT > 0 ? P.dcombo + 1 : 1;
            P.dcomboT = T.comboWindow;
            int steps = (int)Mathf.Min(P.dcombo - 1, T.comboMax);
            float mult = 1 + T.comboBonus * steps;
            float sale = order.dish.price * shop.price * tier * mult;
            P.sales += sale; P.delivered++; order.done = true;
            done++;
            hud.Pop(at + Vector3.up * 0.7f, Yen(sale) + " (" + Mathf.RoundToInt(tier * 100) + "%)", 0x1a9e4b, true);
            if (steps > 0)
                hud.Pop(at + Vector3.up * 1.15f, P.dcombo + " 連続 +" + Mathf.RoundToInt(T.comboBonus * steps * 100) + "%", 0xff9500, true);
            Say(P, steps > 0 ? P.dcombo + "連続にゃ！" : (tier >= 1 ? "完璧にゃ！" : (tier <= 0.5f ? "ごめんにゃ…" : "おまちにゃ！")), 1.3f);
            if (done >= orders.Count) Finish(true);
        }

        // ================================================================ ぶつかり
        /// <summary>ぶつかった瞬間（Ent.OnCollisionEnter から）。JS版 handleCollisions。</summary>
        public void OnContact(Ent a, Ent b)
        {
            if (state != "play") return;
            if (a.kind == "floor" || b.kind == "floor") return;
            if (a.kind == "pit" || b.kind == "pit") return;
            if (a.kind == "fence" || b.kind == "fence") return;
            float m1 = a.infMass ? 0 : a.Mass, m2 = b.infMass ? 0 : b.Mass;
            float mr = (m1 > 0 && m2 > 0) ? (m1 * m2) / (m1 + m2) : (m1 > 0 ? m1 : (m2 > 0 ? m2 : 1));
            float j = mr * (a.prevV - b.prevV).magnitude;
            OnImpact(a, b, j);
        }

        void OnImpact(Ent a, Ent b, float j)
        {
            if (j < T.hitThreshold) return;
            long key = a.id < b.id ? ((long)a.id << 32) | (uint)b.id : ((long)b.id << 32) | (uint)a.id;
            float last;
            if (hits.TryGetValue(key, out last) && t - last < T.hitCooldown) return;
            hits[key] = t;

            var hitP = new List<Player>();
            if (a.player != null) hitP.Add(a.player);
            if (b.player != null) hitP.Add(b.player);
            bool isRobot = hitP.Count > 0;
            var blame = isRobot ? hitP[0] : (a.blame ?? b.blame);
            if (blame != null) { a.blame = blame; b.blame = blame; }
            var guest = a.kind == "guest" ? a : (b.kind == "guest" ? b : null);
            var fur = (a.kind == "table" || a.kind == "chair") ? a : ((b.kind == "table" || b.kind == "chair") ? b : null);
            var rp = isRobot ? hitP[0].ent.rb.position : a.Pos;
            bool noisy = false;
            float lw = isRobot ? EffLight(hitP[0]) : 1;
            bool harmless = InvOn(blame);

            foreach (var P in hitP)
            {
                var pp = P.ent.rb.position;
                if (InvOn(P)) continue;
                float d = j * EffBotDmg(P);
                if (d > 0.15f)
                {
                    P.botDmg = Mathf.Min(100, P.botDmg + d);
                    SetFace(P, "hit", 0.6f); noisy = true;
                    if (d > 1.2f && P.msgT <= 0) Say(P, "痛いにゃ！", 0.9f);
                    if (P.broken == null && P.botDmg >= T.breakSteer)
                    {
                        P.broken = Random.value < 0.5f ? "left" : "right";
                        Say(P, (P.broken == "left" ? "左" : "右") + "に曲がれないにゃ！！", 2.4f);
                        hud.Pop(pp + Vector3.up * 1.0f, "旋回系 故障！", 0xff3b30, true);
                    }
                    if (!P.brokenDrive && P.botDmg >= T.breakDrive)
                    {
                        P.brokenDrive = true; P.misT = 0;
                        Say(P, "エンジンが変にゃ！！", 2.4f);
                        hud.Pop(pp + Vector3.up * 1.2f, "駆動系 故障！", 0xff3b30, true);
                    }
                    if (P.botDmg >= 100) DownPlayer(P, 0);
                }
                if (P.carried != null)
                {
                    float dd = j * T.dishDmg * P.carried.dish.frag;
                    if (dd > 0.2f)
                    {
                        P.carried.integ = Mathf.Max(0, P.carried.integ - dd);
                        noisy = true;
                    }
                }
            }
            if (guest != null)
            {
                // 吹っ飛び：物理まかせだとほぼ動かないので、衝撃に比例した力を明示的に加える
                if (isRobot)
                {
                    var gp = guest.rb.position;
                    float dx = gp.x - rp.x, dz = gp.z - rp.z;
                    float len = Mathf.Sqrt(dx * dx + dz * dz); if (len < 1e-6f) len = 1;
                    dx /= len; dz /= len;
                    guest.rb.AddForce(new Vector3(dx * j * T.knockback * lw, j * T.knockUp * lw, dz * j * T.knockback * lw), ForceMode.Impulse);
                    guest.rb.AddTorque(new Vector3((Random.value - 0.5f) * j * 0.5f, (Random.value - 0.5f) * j * 0.3f,
                                                   (Random.value - 0.5f) * j * 0.5f), ForceMode.Impulse);
                }
                float d = harmless ? 0 : GuestHurt(j * T.guestDmg) * lw;
                if (d > 0.5f)
                {
                    float before = guest.hp;
                    guest.hp = Mathf.Max(0, guest.hp - d);
                    guest.walkStop = true;          // 巡回している客は、ぶつかられたらそれまで
                    if (before > 50 && guest.hp <= 50 && blame != null) blame.hurt++;
                    if (before > 0 && guest.hp <= 0) GuestDown(guest);
                    noisy = true;
                }
            }
            if (fur != null)
            {
                float wKind = fur.kind == "chair" ? T.chairWeight : 1.0f;
                float wChain = isRobot ? 1.0f : T.chainWeight;
                float d = harmless ? 0 : j * T.shopDmg * wKind * wChain * lw;
                if (d > 0.05f)
                {
                    float gGain = Mathf.Min(100, shopDmg + d) - shopDmg;
                    float pGain = blame != null ? Mathf.Min(100, blame.shopDmg + d) - blame.shopDmg : 0;
                    shopDmg += gGain;
                    if (blame != null) { blame.shopDmg += pGain; blame.comboDmg += Mathf.Min(gGain, pGain); }
                    noisy = true;
                }
            }
            if (noisy && j > T.hitThreshold * 1.8f && hitP.Count > 0)
            {
                var P = hitP[0];
                P.comboT = 1.2f; P.combo++;
                if (P.combo > P.bestCombo) P.bestCombo = P.combo;
                if (P.combo >= 2)
                {
                    var cp = a.player == P ? b.Pos : a.Pos;
                    hud.Pop(cp + Vector3.up * 0.6f, P.combo + " combo", 0xff3b30, P.combo >= 4);
                }
                PayWreck(P);
            }
        }

        /// <summary>大暴れボーナス。連鎖が wreckMin 以上つながったら、その連鎖の修理費の一部が戻る。</summary>
        void PayWreck(Player P)
        {
            if (P.combo < T.wreckMin || P.comboDmg <= 0) return;
            float rate = Mathf.Min(T.wreckMax, T.wreckBack + T.wreckStep * (P.combo - T.wreckMin));
            float want = P.comboDmg * T.repairPerPct * shop.repair * rate;
            float add = want - P.comboPaid;
            if (add < 1) return;
            P.wreck += add; P.comboPaid = want;
            hud.Pop(P.ent.rb.position + Vector3.up * 1.3f, "大暴れ！ +" + Yen(add), 0xffb300, true);
            if (P.combo == (int)T.wreckMin) Say(P, "やっちゃったにゃ〜！", 1.2f);
        }

        void GuestDown(Ent e)
        {
            if (e.ring != null) return;
            hud.Pop(e.Pos + Vector3.up * 0.8f, "☆", 0xe8b400, true);
            e.ring = Part.Add(stageRoot, MeshGen.Torus(0.2f, 0.035f, 8, 20), Mats.Get(0xe8b400), e.Pos, shadow: false, name: "DownRing");
            e.ring.transform.rotation = Part.Euler3(Mathf.PI / 2, 0, 0);
        }

        void DownPlayer(Player P, float rev)
        {
            if (P.down) return;
            P.down = true; P.downT = t;
            P.revT = rev > 0 ? rev : 0;
            if (P.revT <= 0) P.botDmg = 100;
            if (P.carried != null) { P.carried = null; P.look.ShowDish(null); }   // 落とした料理（拾い直し）はまだ
            SetFace(P, "dead", 99);
            Say(P, P.revT > 0 ? "たすけてにゃ～！" : "もうダメにゃ…", 3.0f);
            hud.Pop(P.ent.rb.position + Vector3.up * 1.2f, P.revT > 0 ? "おちた！ " + Mathf.RoundToInt(P.revT) + "秒" : "リタイア", 0xe53935, true);
            P.ent.rb.linearVelocity = Vector3.zero; P.ent.rb.angularVelocity = Vector3.zero;
            if (P.ring != null) P.ring.SetActive(false);
            bool all = true;
            foreach (var q in players) if (!(q.down && q.revT <= 0)) all = false;
            if (all) Finish(false);
        }

        void TickRevive(float dt)
        {
            foreach (var P in players)
            {
                if (!P.down || P.revT <= 0) continue;
                P.revT -= dt;
                if (P.revT <= 0)
                {
                    P.down = false; P.revT = 0;
                    var sp = RescueSpot(P.ent, null);
                    DropBack(P.ent, sp, 0.6f);
                    P.ent.rb.rotation = Coord.RotFace(P.spawnRot);
                    hud.Mark(sp, P.col);
                    SetFace(P, "norm", 99);
                    Say(P, "もどったにゃ！", 1.6f);
                    if (P.ring != null) P.ring.SetActive(true);
                }
            }
        }

        // ================================================================ 池・溶岩・海・穴（JS版 updateHazards の後半）
        void UpdateHazards(float dt)
        {
            UpdateMoving(dt);                    // 車・ルート・歩く客・動く床（Game.Objects）
            foreach (var e in ents)
            {
                if (e.isFixed || e.infMass || e.rb == null || e.sunk) continue;
                var p = e.rb.position;
                if (p.y < -1.6f)
                {
                    if (e.player != null) continue;            // ロボは上で面倒を見る
                    if (e.kind == "guest") { RescueGuest(e); continue; }
                    SinkOut(e);
                    continue;
                }
                // いかだのマスは「その下の地形」で見る（いかだが動いて行ったら、そこはただの水）
                char ch = Tiles.At(under ?? map, p);
                var td = Tiles.Def(ch);
                float foot = p.y - (e.size.y > 0 ? e.size.y / 2 : 0.5f);
                if (td == null || !td.liquid || foot > -0.06f) { e.wet = 0; continue; }
                if (rafts.Count > 0 && OverRaftNow(p)) { e.wet = 0; continue; }   // いかだの上なら水ではない
                if (td.deadly)
                {
                    if (e.kind == "guest") { RescueGuest(e); continue; }
                    if (e.player != null && state == "play" && !e.player.down)
                    {
                        Say(e.player, "おぼれるにゃ～！", 2.4f);
                        DownPlayer(e.player, T.seaPenalty);
                    }
                    continue;
                }
                e.wet += dt;
                var v = e.rb.linearVelocity;
                float k = Mathf.Min(1, T.waterDrag * dt);
                e.rb.linearVelocity = new Vector3(v.x * (1 - k), Mathf.Max(v.y, -1.6f), v.z * (1 - k));
                if (td.burn)
                {
                    float burn = Toughed(T.lavaBurn);
                    if (e.player != null)
                    {
                        if (InvOn(e.player)) continue;
                        e.player.botDmg = Mathf.Min(100, e.player.botDmg + burn * dt);
                        if (e.player.msgT <= 0) Say(e.player, "熱いにゃーっ！！", 0.8f);
                        SetFace(e.player, "hit", 0.4f);
                        if (e.player.botDmg >= 100) DownPlayer(e.player, 0);
                    }
                    else if (e.kind == "guest")
                    {
                        float was = e.hp;
                        e.hp = Mathf.Max(0, e.hp - GuestHurt(T.lavaBurn * 1.2f * dt));
                        if (was > 0 && e.hp <= 0) GuestDown(e);
                    }
                }
                else
                {
                    if (e.player != null)
                    {
                        if (!InvOn(e.player)) e.player.botDmg = Mathf.Min(100, e.player.botDmg + Toughed(T.waterDmg) * dt);
                        if (e.player.msgT <= 0 && e.wet > 0.6f) Say(e.player, "つめたいにゃ！", 0.8f);
                        if (e.player.botDmg >= 100) DownPlayer(e.player, 0);
                    }
                    else if (e.kind == "guest")
                    {
                        float was = e.hp;
                        e.hp = Mathf.Max(0, e.hp - GuestHurt(T.waterDmg * 0.6f * dt));
                        if (was > 0 && e.hp <= 0) GuestDown(e);
                    }
                }
            }
        }

        void RescueGuest(Ent e)
        {
            MarkFell(e);
            var sv = RescueSpot(e, e.rb.position);
            DropBack(e, sv, 0.9f);
            e.wet = 0;
            hud.Mark(sv, 0xffffff);
            if (e.hp > 0)
            {
                e.hp = Mathf.Max(0, e.hp - GuestHurt(T.guestOut));
                hud.Pop(sv + Vector3.up, "助けた！", e.hp > 0 ? 0x4aa8e0 : 0xe53935, true);
                if (e.hp <= 0) GuestDown(e);
            }
        }

        void SinkOut(Ent e)
        {
            if (e.sunk) return;
            e.sunk = true;
            e.rb.linearVelocity = Vector3.zero; e.rb.angularVelocity = Vector3.zero;
            e.rb.isKinematic = true;
            e.rb.position = new Vector3(e.rb.position.x, -40, e.rb.position.z);
            e.gameObject.SetActive(false);
            if (e.ring != null) { Destroy(e.ring); e.ring = null; }
        }

        void MarkFell(Ent e)
        {
            if (t - e.fellAt < 3) e.fellN++; else e.fellN = 0;
            e.fellAt = t;
        }

        bool Standable(Vector3 p) { return Tiles.IsFloor(Tiles.At(map, p)); }
        bool Inland(Vector3 p)
        {
            foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                char ch = Tiles.At(map, p + d);
                if (!Tiles.IsFloor(ch) && !Tiles.IsWall(ch)) return false;
            }
            return true;
        }
        bool ClearOf(Vector3 p, Ent self)
        {
            foreach (var o in ents)
            {
                if (o == self || o.isFixed || o.rb == null || o.sunk) continue;
                var q = o.rb.position;
                if (q.y > -1 && new Vector2(q.x - p.x, q.z - p.z).magnitude < 0.7f) return false;
            }
            return true;
        }

        /// <summary>落ちた物・水から上げる物の戻し先（JS版 rescueSpot）。</summary>
        Vector3 RescueSpot(Ent e, Vector3? fall)
        {
            var P = e.player;
            Vector3? home = P != null ? P.spawn : (e.hasHome ? e.home : (Vector3?)null);
            int reach = 3 + Mathf.Min(4, e.fellN);
            var from = new List<Vector3>();
            if (home != null) from.Add(new Vector3(home.Value.x, 0, home.Value.z));
            if (fall != null) { var nf = Tiles.NearestFloor(map, fall.Value); if (nf != null) from.Add(nf.Value); }
            foreach (bool strict in new[] { true, false })
                foreach (var bp in from)
                {
                    System.Func<Vector3, bool> ok = q => Standable(q) && ClearOf(q, e) && (!strict || Inland(q));
                    if (ok(bp)) return RaftShift(bp);
                    for (int r = 1; r <= reach; r++)
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI / 4;
                            var q = bp + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                            if (ok(q)) return RaftShift(q);
                        }
                }
            return RaftShift(home ?? (P != null ? P.spawn : Vector3.zero));
        }

        /// <summary>戻すときは少し上から落とす（ぱっと置くと戻ったことに気づけない）。</summary>
        void DropBack(Ent e, Vector3 s, float y)
        {
            e.rb.position = new Vector3(s.x, y + 2.6f, s.z);
            e.transform.position = e.rb.position;
            e.rb.linearVelocity = new Vector3(0, -6.6f, 0);
            e.rb.angularVelocity = Vector3.zero;
        }

        // ================================================================ 終わり
        public struct Ledger { public float sales, repair, ambCost, dmg, wreck, total; public int amb, best; }

        public Ledger LedgerOf(Player P)
        {
            var L = new Ledger();
            L.sales = 0; L.wreck = 0; L.best = 0;
            foreach (var q in players) { L.sales += q.sales; L.wreck += q.wreck; L.best = Mathf.Max(L.best, q.bestCombo); }
            L.dmg = shopDmg;
            foreach (var g in guests) if (g.hp <= 50) L.amb++;
            float share = 1f / (1 + T.repairShare * (players.Count - 1));
            L.repair = L.dmg * T.repairPerPct * shop.repair * share;
            L.ambCost = (T.countAmb ? L.amb * T.ambulance : 0) * share;
            L.total = L.sales + L.wreck - L.repair - L.ambCost;
            return L;
        }

        void Finish(bool cleared)
        {
            if (result != null) return;
            state = "result";
            var L = LedgerOf(me);
            result = new Result
            {
                cleared = cleared, sales = L.sales, repair = L.repair, ambCost = L.ambCost, dmg = L.dmg,
                wreck = L.wreck, total = L.total, amb = L.amb, best = L.best, delivered = me != null ? me.delivered : 0,
            };
            cash = Mathf.Round(cash + L.total);
            // ストーリー：クリアした面ごとに決まった額（ショップを移すまでは所持金に足すだけ）
            if (cleared && entry != null && entry.bonus > 0) { cash += entry.bonus; result.bonus = entry.bonus; }
            if (cleared)
            {
                int best = PlayerPrefs.GetInt(BestKey(), 0);
                if (best == 0 || frames < best) { result.rec = true; PlayerPrefs.SetInt(BestKey(), frames); }
            }
            foreach (var P in players) SetFace(P, cleared ? "happy" : "dead", 99);
            hud.ShowResult();
        }

        public string BestKey() { return "nekorobo.best|" + shop.n + "|" + stage.n; }

        // ================================================================ 見た目の同期
        void SyncLooks()
        {
            foreach (var P in players)
            {
                P.look.SetDamaged(P.botDmg >= 100);
                P.look.DrawFace(P.face);
                if (P.ring != null)
                {
                    var rp = P.ent.transform.position;
                    P.ring.transform.position = new Vector3(rp.x, 0.012f, rp.z);
                    P.ring.transform.rotation = Quaternion.Euler(0, P.ent.transform.eulerAngles.y, 0);
                }
            }
            foreach (var g in guests)
                if (g.ring != null) { g.ring.transform.position = g.transform.position + Vector3.up * 0.55f; }
        }

        public void Say(Player P, string text, float dur) { P.msg = text; P.msgT = dur; }
        public void SetFace(Player P, string f, float d) { P.face = f; P.faceT = d; }

        public static string Yen(float v)
        {
            return (v < 0 ? "−¥" : "¥") + Mathf.Abs(Mathf.Round(v)).ToString("#,0");
        }

        public static string FmtTime(int f)
        {
            float tt = f / 60f;
            int m = Mathf.FloorToInt(tt / 60), s = Mathf.FloorToInt(tt % 60), cs = Mathf.FloorToInt((tt * 100) % 100);
            return m + ":" + s.ToString("00") + "." + cs.ToString("00");
        }

        // ================================================================ カメラ（JS版 applyCamera / fitCamera）
        Vector3 CamTarget()
        {
            var b = fitBox;
            return new Vector3((b.x0 + b.x1) / 2f, 0, (b.z0 + b.z1) / 2f);
        }

        void ApplyCamera()
        {
            if (mainCam == null) return;
            float e = cam.elev * Mathf.Deg2Rad, a = cam.azim * Mathf.Deg2Rad;
            var c = CamTarget();
            // three.js はカメラが +Z 側。Unity では -Z 側（z を反転）
            var pos = new Vector3(c.x + Mathf.Sin(a) * Mathf.Cos(e) * cam.dist,
                                  0.4f + Mathf.Sin(e) * cam.dist,
                                  c.z - Mathf.Cos(a) * Mathf.Cos(e) * cam.dist);
            if (shake > 0.01f)
            {
                pos += Random.insideUnitSphere * shake * 0.004f;
                shake = Mathf.Max(0, shake - Time.deltaTime * 30f);
            }
            mainCam.transform.position = pos;
            mainCam.transform.LookAt(new Vector3(c.x, 0.4f, c.z));
            mainCam.fieldOfView = cam.fov;
            mainCam.farClipPlane = Mathf.Max(120f, cam.dist * 3f);
            if (lastAspect != mainCam.aspect) { lastAspect = mainCam.aspect; FitCamera(); }
        }
        float lastAspect = -1;

        /// <summary>床がまるごと画面に収まる、いちばん寄った距離を探す。</summary>
        public void FitCamera()
        {
            if (stage == null) return;
            if (stage.camFix && stage.camDist != null) { cam.dist = stage.camDist.Value; ApplyCameraRaw(); RefreshTails(); return; }
            if (!cam.autoFit) { ApplyCameraRaw(); RefreshTails(); return; }
            var b = fitBox;
            float hi = cam.fitWalls ? Mathf.Max(cam.fitHead, wallTop + 0.4f) : cam.fitHead;
            var pts = new List<Vector3>();
            foreach (float x in new[] { b.x0, b.x1 })
                foreach (float z in new[] { b.z0, b.z1 })
                {
                    pts.Add(new Vector3(x, 0, z));
                    if (hi > 0.01f) pts.Add(new Vector3(x, hi, z));
                }
            float room = Mathf.Max(b.x1 - b.x0, b.z1 - b.z0);
            float pad = Mathf.Min(0.4f, Mathf.Max(0, cam.fitPad));
            float keep = 1 - pad;
            float keepTop = 1 - Mathf.Min(0.4f, Mathf.Max(pad, cam.fitTop));
            for (int k = 0; k < 6; k++)
            {
                float savedShake = shake; shake = 0;
                ApplyCameraRaw();
                shake = savedShake;
                float over = 0;
                foreach (var p in pts)
                {
                    var vp = mainCam.WorldToViewportPoint(p);
                    float nx = vp.x * 2 - 1, ny = vp.y * 2 - 1;
                    over = Mathf.Max(over, Mathf.Abs(nx) / keep, ny > 0 ? ny / keepTop : -ny / keep);
                }
                if (float.IsNaN(over) || over <= 0) break;
                float next = Mathf.Clamp(cam.dist * over, 8, room * 6 + 40);
                bool fin = Mathf.Abs(next - cam.dist) < 0.05f;
                cam.dist = next;
                if (fin) break;
            }
            ApplyCameraRaw();
            RefreshTails();                      // 道を伸ばす長さはカメラで決まる
        }

        void ApplyCameraRaw()
        {
            float e = cam.elev * Mathf.Deg2Rad, a = cam.azim * Mathf.Deg2Rad;
            var c = CamTarget();
            mainCam.transform.position = new Vector3(c.x + Mathf.Sin(a) * Mathf.Cos(e) * cam.dist,
                                                     0.4f + Mathf.Sin(e) * cam.dist,
                                                     c.z - Mathf.Cos(a) * Mathf.Cos(e) * cam.dist);
            mainCam.transform.LookAt(new Vector3(c.x, 0.4f, c.z));
            mainCam.fieldOfView = cam.fov;
        }
    }
}
