using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// 照明の表。**HTML版の lights.js をそのまま読む**（INTERIORS・TIMES・LEGACY）。
    /// JS の書き方（コメント・引用符の無い名前・0x の色）を JSON に直してから読む。数値を直せば Unity にも効く。
    /// </summary>
    public static class LightsCfg
    {
        public static JObject Interiors, Times, Legacy;
        public static string InteriorDefault = "plain", TimeDefault = "day";
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            var src = DataRoot.ReadText("lights.js");
            if (src == null) { Debug.LogWarning("[Nekorobo] lights.js が読めません（照明は内蔵の値）"); }
            Interiors = Obj(src, "INTERIORS") ?? new JObject { ["plain"] = new JObject { ["n"] = "ふつう", ["top"] = 0xfff4e6, ["topI"] = 2.5, ["topEl"] = 48, ["topAz"] = -30, ["amb"] = 0xe2ecf8, ["gnd"] = 0x8e8a84, ["hemi"] = 0.78, ["pend"] = 0, ["pendCol"] = 0xffd7a0, ["sconce"] = 0, ["shadow"] = 0.85 } };
            Times = Obj(src, "TIMES") ?? new JObject { ["day"] = new JObject { ["n"] = "昼", ["sky"] = 0xffffff, ["hemi"] = 1.0, ["bg"] = 1.0, ["out"] = 1.0, ["low"] = 0 } };
            Legacy = Obj(src, "LEGACY") ?? new JObject();
            var m = src != null ? Regex.Match(src, "INTERIOR_DEFAULT\\s*=\\s*\"(\\w+)\"") : null;
            if (m != null && m.Success) InteriorDefault = m.Groups[1].Value;
            m = src != null ? Regex.Match(src, "TIME_DEFAULT\\s*=\\s*\"(\\w+)\"") : null;
            if (m != null && m.Success) TimeDefault = m.Groups[1].Value;
        }

        // export const NAME = { ... }; の中身を取り出して JSON にする
        static JObject Obj(string src, string name)
        {
            if (src == null) return null;
            int i = src.IndexOf("const " + name);
            if (i < 0) return null;
            int a = src.IndexOf('{', i);
            int depth = 0, b = -1;
            for (int k = a; k < src.Length; k++)
            {
                char c = src[k];
                if (c == '/' && k + 1 < src.Length && src[k + 1] == '/') { int e = src.IndexOf('\n', k); if (e < 0) break; k = e; continue; }
                if (c == '"') { int e = src.IndexOf('"', k + 1); if (e < 0) break; k = e; continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) { b = k; break; } }
            }
            if (b < 0) return null;
            var body = src.Substring(a, b - a + 1);
            body = Regex.Replace(body, @"//[^\n]*", "");                               // コメント
            body = Regex.Replace(body, @"0x([0-9a-fA-F]+)", mm => System.Convert.ToInt64(mm.Groups[1].Value, 16).ToString());
            body = Regex.Replace(body, @"([{,]\s*)([A-Za-z_][A-Za-z0-9_]*)\s*:", "$1\"$2\":");   // 名前に引用符
            body = Regex.Replace(body, @":\s*null", ":null");
            body = Regex.Replace(body, @",(\s*[}\]])", "$1");                          // 最後のカンマ
            try { return JObject.Parse(body); }
            catch (System.Exception e) { Debug.LogWarning("[Nekorobo] lights.js の " + name + " が読めません: " + e.Message); return null; }
        }
    }

    // ================================================================ 照明（JS版 applyLight）
    // **店の中の照明（お店ごと）と時間帯（ステージごと）を分けて決める**。光には必ず光源を置く：
    //   見えない天井の照明 … sun（平行ライト）を、奥の右上から斜めに当てる。影は物の左手前へ落ちる
    //   吊り下げランプ     … テーブルの上に笠と電球を描いて、下へスポットライト
    //   壁のランプ         … 高い壁の床側の面に器具を付けて、下向きのスポットライト＋弱い全方向の光
    //   外からの低い光     … dusk（夕日・月明かり。影は落とさない）
    //   外の飾り           … 夜や夕方は暗くする。窓・車のライト・街灯のガラスは光らせる（Unity は街灯と車のライトも本物の光）
    // three.js の光は強さ ÷π で効く（Unity は割らない）ので、強さはどれも π で割って入れる
    public partial class Game
    {
        Light dusk;
        Transform lampRoot;
        JObject lightI, lightT;
        public string lightInterior, lightTime;
        /// <summary>検証パネルの「照明（試す）」で選んだもの。いちばん先に効く（覚えない）</summary>
        public string lightTryInterior = "", lightTryTime = "";
        const int PEND_MAX = 8, SCONCE_MAX = 6, PEND_SHADOWS = 4;
        static float F(JObject o, string k, float d) { return o != null && o[k] != null && o[k].Type != JTokenType.Null ? (float)o[k] : d; }
        static Color HexC(JObject o, string k, int d) { return Mats.Hex(o != null && o[k] != null ? (int)(long)o[k] : d); }

        void LightKeys(out string interior, out string time)
        {
            LightsCfg.Load();
            var raw = stage != null ? stage.raw : null;
            string legK = raw != null ? (string)raw["light"] : null;
            var leg = legK != null ? LightsCfg.Legacy[legK] as JObject : null;
            System.Func<JObject, string[], string, string> pick = (table, cands, def) =>
            {
                foreach (var v in cands) if (!string.IsNullOrEmpty(v) && table[v] != null) return v;
                return def;
            };
            interior = pick(LightsCfg.Interiors, new[] { lightTryInterior, raw != null ? (string)raw["interior"] : null,
                                                         leg != null ? (string)leg["interior"] : null, shop != null ? shop.light : null }, LightsCfg.InteriorDefault);
            time = pick(LightsCfg.Times, new[] { lightTryTime, raw != null ? (string)raw["time"] : null,
                                                 leg != null ? (string)leg["time"] : null }, LightsCfg.TimeDefault);
        }

        public void ApplyLight()
        {
            string ik, tk; LightKeys(out ik, out tk);
            lightInterior = ik; lightTime = tk;
            var I = LightsCfg.Interiors[ik] as JObject; var T2 = LightsCfg.Times[tk] as JObject;
            lightI = I; lightT = T2;
            float PI = Mathf.PI;
            // まわりの光：店の中の色に、時間帯の空の色を半分混ぜる（半球光は Unity の三色の環境光で同じにする）
            float hk = F(I, "hemi", 1) * F(T2, "hemi", 1) / PI;
            Color sky = Color.Lerp(HexC(I, "amb", 0xffffff).linear, HexC(T2, "sky", 0xffffff).linear, 0.5f) * hk;
            Color gnd = HexC(I, "gnd", 0x9aa2ad).linear * hk;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky.gamma;
            RenderSettings.ambientEquatorColor = ((sky + gnd) * 0.5f).gamma;
            RenderSettings.ambientGroundColor = gnd.gamma;
            // 見えない天井の照明（影を落とす）。盤面のまん中を、奥の右上から斜めに
            var B = bounds;
            float cx = (B.x0 + B.x1) / 2, czU = (B.z0 + B.z1) / 2;
            float el = F(I, "topEl", 80) * Mathf.Deg2Rad, az = F(I, "topAz", 41) * Mathf.Deg2Rad;
            // JS の位置（x, y, z）→ Unity は z を反転
            var from = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), -Mathf.Cos(el) * Mathf.Sin(az));
            sun.color = HexC(I, "top", 0xffffff);
            sun.intensity = F(I, "topI", 1.5f) / PI;
            sun.transform.rotation = Quaternion.LookRotation(-from);
            sun.shadowStrength = Mathf.Clamp01(F(I, "shadow", 1));
            sun.shadowBias = 0.02f; sun.shadowNormalBias = 0.3f;
            // 外からの低い光（夕日・月明かり。影は落とさない）
            if (dusk == null)
            {
                dusk = new GameObject("Dusk").AddComponent<Light>();
                dusk.type = LightType.Directional; dusk.shadows = LightShadows.None;
            }
            float le = F(T2, "lowEl", 50) * Mathf.Deg2Rad, la = F(T2, "lowAz", 41) * Mathf.Deg2Rad;
            dusk.color = HexC(T2, "lowCol", 0xffffff);
            dusk.intensity = F(T2, "low", 0) / PI;
            dusk.transform.rotation = Quaternion.LookRotation(-new Vector3(Mathf.Cos(le) * Mathf.Cos(la), Mathf.Sin(le), -Mathf.Cos(le) * Mathf.Sin(la)));
            dusk.enabled = dusk.intensity > 0;
            // 仕上げ（接地の陰・にじみ・露出）の倍率
            PostFx.lightExpo = F(I, "expo", 1);
            PostFx.lightBloom = F(I, "bloom", 1) * F(T2, "bloom", 1);
            // 氷の映り込み。まわりの光が弱い所（暗い店・夜）では映り込みも弱く
            iceReflect = 0.75f * Mathf.Min(1.2f, F(I, "hemi", 1) * F(T2, "hemi", 1) / 0.75f);
            if (iceMat != null && iceMat.HasProperty("_Reflect")) iceMat.SetFloat("_Reflect", iceReflect);
            // 背景の色（時間帯）
            if (mainCam != null)
            {
                if (bg0 == null) bg0 = mainCam.backgroundColor;
                mainCam.backgroundColor = bg0.Value * F(T2, "bg", 1);
            }
            // 明かりと器具。前の分は消す
            if (lampRoot != null) Destroy(lampRoot.gameObject);
            lampRoot = new GameObject("Lamps").transform; lampRoot.SetParent(stageRoot, false);
            BuildPendants(I);
            BuildSconces(I);
            glowAt = new[] { 0.4f, 1.2f, 2.5f, 4.5f }; glowI = 0; glowT = 0; streetN = -1;
            RefreshGlow();
            BuildSparkle(T2);
        }
        Color? bg0;
        [System.NonSerialized] public Material iceMat;
        float iceReflect = 0.75f;

        // 吊り下げランプ。盤面のまん中に近いテーブルから PEND_MAX 台。笠・電球・上へ伸びるコードを描いて、下へ当てる
        void BuildPendants(JObject I)
        {
            float pend = F(I, "pend", 0);
            if (!(pend > 0)) return;
            var B = bounds; float cx = (B.x0 + B.x1) / 2, cz = (B.z0 + B.z1) / 2;
            var tables = ents.FindAll(e => e.kind == "table" && e.rb != null).ConvertAll(e => e.rb.position);
            tables.Sort((a, b) => new Vector2(a.x - cx, a.z - cz).magnitude.CompareTo(new Vector2(b.x - cx, b.z - cz).magnitude));
            if (tables.Count > PEND_MAX) tables.RemoveRange(PEND_MAX, tables.Count - PEND_MAX);
            var pc = HexC(I, "pendCol", 0xffd7a0);
            var shadeM = Mats.Get(0x9a7440, 0.4f, 0.6f);
            var innerM = Mats.Basic(pc * 0.9f, false, true);
            var bulbM = Mats.Basic(pc * 1.6f);
            var cordM = Mats.Get(0x1a1a1e, 0.8f);
            const float Y = 1.6f;
            for (int i = 0; i < tables.Count; i++)
            {
                var p = tables[i];
                var g = new GameObject("Pendant").transform; g.SetParent(lampRoot, false); g.position = new Vector3(p.x, 0, p.z);
                Part.Add(g, MeshGen.Cylinder(0.012f, 0.012f, 0.9f, 6), cordM, new Vector3(0, Y + 0.55f, 0), false, "Cord");
                Part.Add(g, MeshGen.Cylinder(0.07f, 0.27f, 0.22f, 22, true), shadeM, new Vector3(0, Y, 0), false, "Shade");
                Part.Add(g, MeshGen.Cylinder(0.068f, 0.265f, 0.215f, 22, true), innerM, new Vector3(0, Y, 0), false, "Inner");
                Part.Add(g, MeshGen.Sphere(0.06f, 12, 8), bulbM, new Vector3(0, Y - 0.07f, 0), false, "Bulb");
                var l = new GameObject("PendLight").AddComponent<Light>();
                l.transform.SetParent(g, false);
                l.transform.localPosition = new Vector3(0, Y - 0.1f, 0);
                l.transform.rotation = Quaternion.LookRotation(Vector3.down);
                l.type = LightType.Spot; l.color = pc; l.intensity = 13 * pend / Mathf.PI; l.range = 5;
                l.spotAngle = 0.8f * 2 * Mathf.Rad2Deg; l.innerSpotAngle = l.spotAngle * 0.35f;
                l.shadows = cam.shadow && i < PEND_SHADOWS ? LightShadows.Soft : LightShadows.None;
                l.shadowStrength = Mathf.Clamp01(F(I, "shadow", 1));
            }
        }

        // 壁のランプ。高い壁（H）の床に面した側に付ける。壁ぞいに 3マスおき、盤面のまん中に近い所から SCONCE_MAX 個。
        // **まわりは薄く、下へ強く**：下向きのスポットライトと、弱い全方向の光を重ねる（デザイナーの見本）
        void BuildSconces(JObject I)
        {
            float sc = F(I, "sconce", 0);
            if (!(sc > 0)) return;
            var B = bounds; float cx = (B.x0 + B.x1) / 2, cz = (B.z0 + B.z1) / 2;
            var spots = new List<Vector4>();      // x, z（Unity）, di, dj（JS のマスの向き）
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value);
                if (td == null || !td.wall || !td.high || td.hidden) continue;
                int i = kv.Key.x, j = kv.Key.y;
                if ((((i + j) % 3) + 3) % 3 != 0) continue;
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    char nc; var n = map.TryGetValue(new Vector2Int(i + d.x, j + d.y), out nc) ? Tiles.Def(nc) : null;
                    if (n == null || !n.floor) continue;
                    var c = Coord.Cell(i, j);
                    // JS の dj（+j は JS の +z）は Unity の −z
                    spots.Add(new Vector4(c.x + d.x * Coord.Tile * 0.5f, c.z - d.y * Coord.Tile * 0.5f, d.x, -d.y));
                    break;
                }
            }
            spots.Sort((a, b) => new Vector2(a.x - cx, a.y - cz).magnitude.CompareTo(new Vector2(b.x - cx, b.y - cz).magnitude));
            var pc = HexC(I, "pendCol", 0xffd7a0);
            var plateM = Mats.Get(0xb09a7a, 0.5f, 0.3f);
            var glowM = Mats.Basic(Color.Lerp(pc, Color.white, 0.6f) * 3.2f);
            for (int k = 0; k < Mathf.Min(SCONCE_MAX, spots.Count); k++)
            {
                var sp = spots[k]; float dx = sp.z, dz = sp.w;     // 床の側の向き（Unity）
                var g = new GameObject("Sconce").transform; g.SetParent(lampRoot, false);
                g.position = new Vector3(sp.x + dx * 0.04f, 1.85f, sp.y + dz * 0.04f);
                g.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dz));
                Part.Add(g, MeshGen.Box(0.1f, 0.16f, 0.025f), plateM, Vector3.zero, false, "Plate");
                var shade = Part.Add(g, MeshGen.Sphere(0.11f, 14, 8, 0, Mathf.PI * 2, 0, Mathf.PI / 2), glowM, new Vector3(0, 0.02f, 0.1f), false, "Shade");
                shade.transform.localRotation = Quaternion.Euler(180, 0, 0); shade.transform.localScale = new Vector3(1, 0.9f, 0.8f);
                var l = new GameObject("SconceSpot").AddComponent<Light>();
                l.transform.SetParent(lampRoot, false);
                l.transform.position = new Vector3(sp.x + dx * 0.09f, 1.8f, sp.y + dz * 0.09f);
                l.transform.rotation = Quaternion.LookRotation(new Vector3(sp.x + dx * 0.4f, 0, sp.y + dz * 0.4f) - l.transform.position);
                l.type = LightType.Spot; l.color = pc; l.intensity = 28 * sc / Mathf.PI; l.range = 4.2f;
                l.spotAngle = 0.9f * 2 * Mathf.Rad2Deg; l.innerSpotAngle = l.spotAngle * 0.15f; l.shadows = LightShadows.None;
                var lw = new GameObject("SconceGlow").AddComponent<Light>();
                lw.transform.SetParent(lampRoot, false);
                lw.transform.position = new Vector3(sp.x + dx * 0.3f, 1.75f, sp.y + dz * 0.3f);
                lw.type = LightType.Point; lw.color = pc; lw.intensity = 4 * sc / Mathf.PI; lw.range = 3.2f; lw.shadows = LightShadows.None;
            }
        }

        // ---- 光らせる物（材質の名前で見分ける）：[光の色, 強さ, 車には付けないか]
        static readonly Dictionary<string, object[]> GLOW_MATS = new Dictionary<string, object[]>
        {
            { "lamp_glass", new object[] { 0xffe6a0, 2.2f, false } }, { "glass_dark", new object[] { 0xffc46a, 0.9f, true } },
            { "glass_blue", new object[] { 0xffd27a, 0.8f, true } }, { "headlight", new object[] { 0xfff4cc, 2.6f, false } },
            { "taillight", new object[] { 0xff3020, 1.8f, false } }, { "lamp_on", new object[] { 0xffe699, 2.0f, false } },
            { "lantern_light", new object[] { 0xffd180, 2.0f, false } },
        };
        readonly Dictionary<Material, Color> glow0 = new Dictionary<Material, Color>();
        readonly Dictionary<Material, Color> out0 = new Dictionary<Material, Color>();
        float[] glowAt; int glowI; float glowT; int streetN;
        Transform streetRoot;

        // 面を組んでから数秒のあいだ、光らせる物と街灯を見直す（モデルが遅れて差し替わるので）。毎フレーム
        void LightTick(float dt)
        {
            SparkleTick(dt);
            if (glowAt == null || glowI >= glowAt.Length) return;
            glowT += dt;
            if (glowT >= glowAt[glowI]) { glowI++; RefreshGlow(); }
        }

        void RefreshGlow()
        {
            if (lightT == null || stageRoot == null) return;
            float g = F(lightT, "glow", 0), street = F(lightT, "street", 0);
            DimOutside(F(lightT, "out", 1));
            var lamps = ApplyGlow(g);
            CarBeams(g);
            if (!(street > 0) || lamps.Count == streetN) return;
            streetN = lamps.Count;
            if (streetRoot != null) Destroy(streetRoot.gameObject);
            streetRoot = new GameObject("Street").transform; streetRoot.SetParent(stageRoot, false);
            var B = bounds; float cx = (B.x0 + B.x1) / 2, cz = (B.z0 + B.z1) / 2;
            lamps.Sort((a, b) => new Vector2(a.x - cx, a.z - cz).magnitude.CompareTo(new Vector2(b.x - cx, b.z - cz).magnitude));
            for (int i = 0; i < Mathf.Min(6, lamps.Count); i++)
            {
                var l = new GameObject("StreetLamp").AddComponent<Light>();
                l.transform.SetParent(streetRoot, false);
                l.transform.position = lamps[i] + Vector3.down * 0.15f;
                l.type = LightType.Point; l.color = Mats.Hex(0xffd29a); l.intensity = 14 * street / Mathf.PI; l.range = 7.5f; l.shadows = LightShadows.None;
            }
        }

        // 外の飾り（建物・木など）の明るさ。夜や夕方は暗くする
        void DimOutside(float k)
        {
            var sc = stageRoot.Find("Scenery");
            if (sc == null) return;
            foreach (var r in sc.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i]; if (m == null) continue;
                    if (!m.name.EndsWith(" (Out)"))
                    {
                        if (Mathf.Approximately(k, 1)) continue;
                        m = new Material(m) { name = m.name + " (Out)" }; out0[m] = ModelStore.MatColor(ms[i]); ms[i] = m; ch = true;
                    }
                    Color c0; if (!out0.TryGetValue(m, out c0)) continue;
                    var c = c0 * k; c.a = c0.a; ModelStore.SetMatColor(m, c);
                }
                if (ch) r.sharedMaterials = ms;
            }
        }

        // 強さ g で光らせる（0 なら元に戻す）。返すのは街灯のガラスの位置（明かりを置く所）
        List<Vector3> ApplyGlow(float g)
        {
            var lamps = new List<Vector3>();
            var skip = new HashSet<Transform>();
            foreach (var e in ents) if (e != null && (e.kind == "car" || e.player != null || e.kind == "guest")) skip.Add(e.transform);
            foreach (var r in stageRoot.GetComponentsInChildren<Renderer>(true))
            {
                bool onCar = false;
                for (var p = r.transform; p != null; p = p.parent) if (skip.Contains(p)) { onCar = true; break; }
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    object[] d;
                    var nm = ModelStore.MatName(m);
                    if (!GLOW_MATS.TryGetValue(nm, out d) || !m.HasProperty("emissiveFactor")) continue;
                    if ((bool)d[2] && onCar) continue;
                    if (nm == "lamp_glass") lamps.Add(r.bounds.center);
                    if (!glow0.ContainsKey(m)) glow0[m] = m.GetColor("emissiveFactor");
                    if (g > 0) m.SetColor("emissiveFactor", Mats.Hex((int)d[0]) * (float)d[1] * g);
                    else m.SetColor("emissiveFactor", glow0[m]);
                }
            }
            return lamps;
        }

        // 車のライト。Unity版は本物の光（前照灯の位置から前へスポットライト）
        readonly Dictionary<Ent, Light> carBeam = new Dictionary<Ent, Light>();
        void CarBeams(float g)
        {
            foreach (var c in cars)
            {
                var e = c.ent; if (e == null) continue;
                Light l;
                if (carBeam.TryGetValue(e, out l) && l != null) { l.enabled = g > 0; l.intensity = 9 * g / Mathf.PI; continue; }
                if (!(g > 0)) continue;
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var r in e.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null && ModelStore.MatName(m) == "headlight") { sum += e.transform.InverseTransformPoint(r.bounds.center); n++; }
                if (n == 0) continue;
                sum /= n;
                var f = new Vector3(sum.x, 0, sum.z); if (f.magnitude < 0.05f) continue;
                l = new GameObject("CarBeam").AddComponent<Light>();
                l.transform.SetParent(e.transform, false);
                l.transform.localPosition = sum;
                l.transform.localRotation = Quaternion.LookRotation(f.normalized + Vector3.down * 0.35f);
                l.type = LightType.Spot; l.color = Mats.Hex(0xfff0cd); l.intensity = 9 * g / Mathf.PI; l.range = 6;
                l.spotAngle = 70; l.innerSpotAngle = 25; l.shadows = LightShadows.None;
                carBeam[e] = l;
            }
        }

        // ---- 水面のきらめき（水・海のマスに光る粒を散らして、またたかせる）
        class Spark { public Transform t; public float ph, sp; }
        readonly List<Spark> sparks = new List<Spark>();
        Material sparkMat; float sparkK;
        static Texture2D sparkTex;
        static Texture2D SparkTex()
        {
            if (sparkTex != null) return sparkTex;
            const int S = 32; var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                    float cross = Mathf.Max(0, 1 - Mathf.Abs(u) * 7) * Mathf.Max(0, 1 - Mathf.Abs(v)) + Mathf.Max(0, 1 - Mathf.Abs(v) * 7) * Mathf.Max(0, 1 - Mathf.Abs(u));
                    float a = Mathf.Clamp01(Mathf.Max(0, 1 - r * 2.2f) + cross * 0.8f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            sparkTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            sparkTex.SetPixels32(px); sparkTex.Apply(false);
            return sparkTex;
        }
        void BuildSparkle(JObject L)
        {
            sparks.Clear();
            sparkK = F(L, "sparkle", 0);
            if (!(sparkK > 0)) return;
            var root = new GameObject("Sparkle").transform; root.SetParent(lampRoot, false);
            sparkMat = Mats.BasicTex(SparkTex(), true, true);
            sparkMat.SetColor("_BaseColor", HexC(L, "sparkCol", 0xffffff));
            var rr = new System.Random(4711);
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value);
                if (td == null || !td.liquid || td.burn) continue;
                var c = Coord.Cell(kv.Key.x, kv.Key.y);
                int n = 1 + (rr.NextDouble() < 0.5 ? 1 : 0);
                for (int q = 0; q < n; q++)
                {
                    var t = Part.Add(root, MeshGen.Plane(0.3f, 0.3f), sparkMat,
                                     new Vector3(c.x + ((float)rr.NextDouble() - 0.5f) * 0.9f, -0.06f, c.z + ((float)rr.NextDouble() - 0.5f) * 0.9f), false, "Spark").transform;
                    sparks.Add(new Spark { t = t, ph = (float)rr.NextDouble() * 6.28f, sp = 0.6f + (float)rr.NextDouble() * 1.4f });
                }
            }
        }
        void SparkleTick(float dt)
        {
            if (sparks.Count == 0 || mainCam == null) return;
            float tt = Time.time;
            var up = mainCam.transform.up; var back = -mainCam.transform.forward;
            var rot = Quaternion.LookRotation(up, back);
            foreach (var s in sparks)
            {
                if (s.t == null) continue;
                float v = Mathf.Pow(Mathf.Max(0, Mathf.Sin(tt * s.sp + s.ph)), 6);    // ほとんどの時間は消えていて、ときどき光る
                s.t.gameObject.SetActive(v * sparkK > 0.01f);
                if (v * sparkK <= 0.01f) continue;
                s.t.rotation = rot;
                s.t.localScale = Vector3.one * (0.6f + 0.6f * v) * Mathf.Min(1, sparkK * 2.5f);
            }
        }
    }
}
