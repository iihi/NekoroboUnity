using System.Reflection;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// 遊びの数値。**名前も意味も JS版の DEFAULT_TUNE と同じ**にしてある。
    /// ここの初期値は「コードに書いてある既定値」（JS版の DEFAULT_TUNE）。
    /// 起動時に HTML版の tune.json（右パネルで保存した既定値）を上から読む。
    /// ステージの "tune" はさらにその上に乗る（その面だけ）。
    /// </summary>
    [System.Serializable]
    public class Tune
    {
        [Header("操作（SI単位）")]
        public float thrust = 18, reverseRatio = 0.55f, steerAccel = 14, steerMax = 3.0f,
                     lateralGrip = 3.0f, linDamp = 4.3f;
        [Header("吹っ飛び")]
        public float knockback = 2.0f, knockUp = 1.6f;
        [Header("ジャンプ")]
        public float jumpSpeed = 3.4f, jumpCooldown = 0.5f, airControl = 0.35f;
        [Header("ダメージ")]
        public float hitThreshold = 18, hitCooldown = 0.30f, botTough = 1.0f, botDmg = 0.033f,
                     shopDmg = 0.039f, chairWeight = 0.30f, chainWeight = 0.50f, dishDmg = 0.32f,
                     guestDmg = 0.43f, guestTough = 1.0f, guestOut = 100;
        [Header("池・海")]
        public float seaPenalty = 10.0f, waterDrag = 3.0f, waterDmg = 2.5f, lavaBurn = 9.0f;
        [Header("アイテム（まだ移していない）")]
        public float bananaSlip = 2.0f, bananaSpin = 5.5f, droneSpeed = 4.6f, invTime = 10,
                     boomSpeed = 11, boomOut = 0.6f, boomR = 0.8f, missileSpeed = 16.0f,
                     homingSpeed = 11.0f, homingTurn = 2.4f, homingLife = 6.0f,
                     ballRange = 12.0f, ballAimSpeed = 9.0f, ballFlight = 1.6f, ballHeight = 9.0f,
                     ballRadius = 6.4f, blastRadius = 3.4f, blastForce = 26, blastBotDmg = 22,
                     blastGuestDmg = 70, blastShopDmg = 6, carRespawn = 4.0f;
        [Header("凍りの床")]
        public float iceGrip = 0.10f, iceDrag = 0.35f, icePush = 0.50f;
        [Header("落とした料理")]
        public float dropImpulse = 55, dropBlast = 0.40f, dropDmg = 28, dropSettle = 0.6f,
                     dropArm = 1.2f, pickupR = 0.85f, dishTimeout = 30;
        [Header("壁・ジャンプ台")]
        public float wallLow = 0.55f, wallHigh = 3.00f, wallPhys = 20.00f, rampRise = 0.85f;
        [Header("故障")]
        public float breakSteer = 50, breakDrive = 75, repairHeal = 50;
        [Header("画面")]
        public float menuLock = 0.7f, shopTime = 45, ptrCross = 1.1f;
        public bool soloShopTimer = false;
        public float resultTime = 0;
        [Header("大暴れ・配膳コンボ")]
        public float wreckMin = 4, wreckBack = 0.40f, wreckStep = 0.15f, wreckMax = 0.95f,
                     comboWindow = 20, comboBonus = 0.25f, comboMax = 5;
        [Header("収支")]
        public float startCash = 20000, stageBonus = 10000, bonusEvery = 3, bonusSpread = 0.5f,
                     repairPerPct = 150, ambulance = 2000;
        public bool countAmb = true;
        public float repairShare = 0.30f, orderCount = 6, orderAddPer = 3;

        public Tune Clone() { return (Tune)MemberwiseClone(); }

        /// <summary>
        /// 中身だけ入れ替える。**Tune そのものは作り直さない**（つまみが古い方を触り続けるため。JS版と同じ注意）。
        /// </summary>
        public void CopyFrom(Tune src)
        {
            foreach (var f in typeof(Tune).GetFields(BindingFlags.Public | BindingFlags.Instance)) f.SetValue(this, f.GetValue(src));
        }

        /// <summary>{ 名前: 値 } の JSON にする（tune.json の "tune"）。</summary>
        public JObject ToJson()
        {
            var o = new JObject();
            foreach (var f in typeof(Tune).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var v = f.GetValue(this);
                if (v is bool) o[f.Name] = (bool)v;
                else o[f.Name] = (double)System.Math.Round((float)v, 6);
            }
            return o;
        }

        /// <summary>
        /// 「爽快」の調子（JS版の WILD）。コードの既定値の上に、これだけ乗せる。
        /// 速くて壊れにくく、客がよく飛ぶ。見ている人も楽しい大味な調子。
        /// </summary>
        public static Tune Wild()
        {
            var t = new Tune();
            t.thrust = 34; t.linDamp = 3.4f; t.reverseRatio = 0.75f; t.steerAccel = 20; t.steerMax = 4.5f;
            t.jumpSpeed = 4.6f; t.airControl = 0.50f;
            t.botTough = 8.0f; t.botDmg = 0.024f; t.breakSteer = 65; t.breakDrive = 90;
            t.knockback = 7.0f; t.knockUp = 3.2f; t.guestDmg = 0.05f;
            t.guestTough = 10.0f;
            t.blastRadius = 4.6f; t.blastForce = 48; t.blastGuestDmg = 45;
            t.hitThreshold = 30; t.dropImpulse = 110;
            t.shopDmg = 0.022f; t.repairPerPct = 90; t.ambulance = 1200;
            return t;
        }

        /// <summary>JSON の { 名前: 値 } を、同じ名前の項目へ入れる。知らない名前は無視。</summary>
        public void Apply(JObject o)
        {
            if (o == null) return;
            foreach (var p in o.Properties())
            {
                var f = typeof(Tune).GetField(p.Name, BindingFlags.Public | BindingFlags.Instance);
                if (f == null) continue;
                try
                {
                    if (f.FieldType == typeof(float)) f.SetValue(this, p.Value.Value<float>());
                    else if (f.FieldType == typeof(bool)) f.SetValue(this, p.Value.Type == JTokenType.Boolean
                                                                           ? (bool)p.Value : p.Value.Value<float>() != 0f);
                }
                catch { /* 型の合わない値は飛ばす */ }
            }
        }
    }

    /// <summary>遊びの調子。JS版の右パネル「遊びの調子」と同じ3つ。</summary>
    public enum Preset
    {
        普通,      // コードに書いてある内蔵の数値（企画書の想定に近い、詰めて遊ぶ調子）
        爽快,      // WILD
        カスタム,  // 保存してある数値（tune.json）
    }

    /// <summary>カメラの数値。JS版の CAM と同じ。</summary>
    [System.Serializable]
    public class CamTune
    {
        public float elev = 50, azim = 0, dist = 20, fov = 38;
        public bool shadow = true, autoFit = true;
        public float fitPad = 0.02f, fitTop = 0.09f, fitHead = 0;
        public bool fitWalls = false, fitRoomWalls = false;

        public void Apply(JObject o)
        {
            if (o == null) return;
            foreach (var p in o.Properties())
            {
                var f = typeof(CamTune).GetField(p.Name, BindingFlags.Public | BindingFlags.Instance);
                if (f == null) continue;
                try
                {
                    if (f.FieldType == typeof(float)) f.SetValue(this, p.Value.Value<float>());
                    else if (f.FieldType == typeof(bool)) f.SetValue(this, (bool)p.Value);
                }
                catch { }
            }
        }

        public JObject ToJson()
        {
            var o = new JObject();
            foreach (var f in typeof(CamTune).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var v = f.GetValue(this);
                if (v is bool) o[f.Name] = (bool)v; else o[f.Name] = (double)System.Math.Round((float)v, 6);
            }
            return o;
        }
    }

    /// <summary>tune.json 全体（遊びの数値・カメラ・客の見た目）。</summary>
    public class TuneFile
    {
        public Tune tune = new Tune();
        public CamTune cam = new CamTune();
        public float heads = 2.6f, bodyTint = 0.30f;     // 客の頭身・ロボの色の濃さ（TUNE_G）
        public string log = "";

        public static TuneFile Load()
        {
            var t = new TuneFile();
            var j = DataRoot.ReadJson("tune.json");
            if (j == null) { t.log = "tune.json なし（コードの既定値）"; return t; }
            t.tune.Apply(j["tune"] as JObject);
            t.cam.Apply(j["camera"] as JObject);
            var g = j["guest"] as JObject;
            if (g != null) { t.heads = J.F(g, "heads", t.heads); t.bodyTint = J.F(g, "bodyTint", t.bodyTint); }
            t.log = "tune.json を読みました";
            return t;
        }
    }
}
