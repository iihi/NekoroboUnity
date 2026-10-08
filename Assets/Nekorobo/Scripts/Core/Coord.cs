using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// JS版（three.js）の座標を Unity へ写す決まり。**ここ以外で符号をいじらない。**
    ///
    ///   three.js … 右手系。カメラは +Z 側から -Z を見ている（j が大きいほど手前）
    ///   Unity    … 左手系。カメラは -Z 側から +Z を見る
    ///
    /// **z の符号を反転するだけ**で、画面に映る絵は同じになる（右が +X のまま）。
    ///   位置   (x, y, z) → (x, y, -z)
    ///   回転   x軸まわり θ → -θ ／ y軸まわり θ → -θ ／ z軸まわり θ → そのまま
    ///   物の前 three.js の -Z（ロボ・客・イス）→ Unity の +Z（transform.forward）
    ///
    /// マップのマス (i, j) の中心は Unity で (i+0.5, 0, -(j+0.5))。
    /// </summary>
    public static class Coord
    {
        public const float Tile = 1f;

        /// <summary>three.js の座標を Unity の座標へ。</summary>
        public static Vector3 W(float x, float y, float z) { return new Vector3(x, y, -z); }

        /// <summary>マス (i, j) の中心（床の高さ）。</summary>
        public static Vector3 Cell(int i, int j) { return new Vector3((i + 0.5f) * Tile, 0f, -(j + 0.5f) * Tile); }

        public static int ToI(float x) { return Mathf.FloorToInt(x / Tile); }
        /// <summary>Unity の z からマップの行 j へ（z の符号が逆なので注意）。</summary>
        public static int ToJ(float unityZ) { return Mathf.FloorToInt(-unityZ / Tile); }

        /// <summary>
        /// エディタの rot（度）を「ローカル +X がその向き」になる回転へ。
        /// rot 0 = +X、90 = 奥（Unity の +Z）、180 = -X、270 = 手前。
        /// 受取カウンター・テーブル・ジャンプ台はこちら（JS版で yaw = rot を当てている物）。
        /// </summary>
        public static Quaternion RotX(float rotDeg) { return Quaternion.Euler(0f, -rotDeg, 0f); }

        /// <summary>
        /// rot（度）を「前（Unity の +Z）がその向き」になる回転へ。
        /// ロボ・客・イス（JS版で前が -Z、yaw = rot-90° を当てている物）はこちら。
        /// </summary>
        public static Quaternion RotFace(float rotDeg) { return Quaternion.Euler(0f, 90f - rotDeg, 0f); }

        /// <summary>rot（度）の向きの単位ベクトル（Unity の xz）。</summary>
        public static Vector3 Dir(float rotDeg)
        {
            float r = rotDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r));
        }

        /// <summary>three.js の y軸まわりの角度[rad] を Unity の回転へ。</summary>
        public static Quaternion YawRad(float rad) { return Quaternion.Euler(0f, -rad * Mathf.Rad2Deg, 0f); }
    }

    /// <summary>
    /// JS版と**同じ並びの乱数**。客の並び・注文の順・料理の選び方を JS版とそろえるため。
    ///
    /// JS版は `seed = (seed*1103515245 + 12345) & 0x7fffffff` を**倍精度の小数で**計算している。
    /// 掛け算が 2^53 を超えて丸められるので、整数で正しく計算すると**別の並びになる**。
    /// ここでは同じように double で掛けてから、下位32ビットを取る。
    /// </summary>
    public class Lcg
    {
        int seed;
        public Lcg(int seed) { this.seed = seed; }

        /// <summary>0〜1。JS と同じく double のまま返す（float にすると端で選ぶ物がずれる）。</summary>
        public double Next()
        {
            double d = (double)seed * 1103515245.0 + 12345.0;   // JS と同じく丸めが入る
            long l = (long)d;                                     // 丸めたあとの値はちょうど整数
            int v = (int)(l & 0xffffffffL);                       // ToInt32（下位32ビット）
            seed = v & 0x7fffffff;
            return seed / (double)0x7fffffff;
        }

        /// <summary>JS の Math.floor(rr()*n)。</summary>
        public int Pick(int n) { return System.Math.Min(n - 1, (int)System.Math.Floor(Next() * n)); }
    }
}
