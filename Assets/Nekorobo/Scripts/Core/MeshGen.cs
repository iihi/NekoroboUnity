using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// three.js の図形（Box / Cylinder / Sphere / Torus / Ring / Circle / Plane / Capsule）を
    /// **同じ作り方で**組む。JS版の見た目の数値をそのまま写せるようにするため。
    ///
    /// 頂点は three.js の座標で作り、最後に z を反転して Unity の Mesh にする（Coord と同じ決まり）。
    /// z を反転しても画面に映る回り方は変わらないので、表裏の決まり
    /// （three.js は反時計回りが表、Unity は時計回りが表）に合わせて**三角形の並びを逆にする**（ToMesh）。
    ///
    /// 同じ形は使い回す（客20人ぶん球を作ると無駄なので）。
    /// </summary>
    public static class MeshGen
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        class B
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> idx = new List<int>();
            public int Add(Vector3 p, Vector3 nn, Vector2 t) { v.Add(p); n.Add(nn); uv.Add(t); return v.Count - 1; }
            public void Tri(int a, int b, int c) { idx.Add(a); idx.Add(b); idx.Add(c); }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                var vv = new Vector3[v.Count];
                var nn = new Vector3[n.Count];
                for (int i = 0; i < v.Count; i++)
                {
                    vv[i] = new Vector3(v[i].x, v[i].y, -v[i].z);
                    nn[i] = new Vector3(n[i].x, n[i].y, -n[i].z);
                }
                if (vv.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.vertices = vv;
                m.normals = nn;
                m.uv = uv.ToArray();
                // z を反転しても、画面に映る三角形の回り方は同じ（反時計回り）のまま。
                // three.js は反時計回りが表、Unity は時計回りが表なので、**並びを逆にする**。
                // （逆にしないと、箱は内側の面が見え、板は裏から見えなくなる）
                var tri = idx.ToArray();
                for (int i = 0; i + 2 < tri.Length; i += 3) { int t = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t; }
                m.triangles = tri;
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh Cached(string key, System.Func<Mesh> make)
        {
            Mesh m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = make();
            cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------ 箱
        /// <summary>three.js の BoxGeometry(w, h, d)。uvRep で貼り絵の繰り返し回数。</summary>
        public static Mesh Box(float w, float h, float d, float uvRepU = 1f, float uvRepV = 1f)
        {
            string key = "box|" + w + "|" + h + "|" + d + "|" + uvRepU + "|" + uvRepV;
            return Cached(key, () =>
            {
                var b = new B();
                float x = w / 2f, y = h / 2f, z = d / 2f;
                // 面ごとに (中心, u, v)。u×v が外向きの法線になる並び（three.js の表＝反時計回り）
                Face(b, new Vector3(x, 0, 0), new Vector3(0, 0, -z), new Vector3(0, y, 0), new Vector3(1, 0, 0), uvRepU, uvRepV);
                Face(b, new Vector3(-x, 0, 0), new Vector3(0, 0, z), new Vector3(0, y, 0), new Vector3(-1, 0, 0), uvRepU, uvRepV);
                Face(b, new Vector3(0, y, 0), new Vector3(x, 0, 0), new Vector3(0, 0, -z), new Vector3(0, 1, 0), uvRepU, uvRepV);
                Face(b, new Vector3(0, -y, 0), new Vector3(x, 0, 0), new Vector3(0, 0, z), new Vector3(0, -1, 0), uvRepU, uvRepV);
                Face(b, new Vector3(0, 0, z), new Vector3(x, 0, 0), new Vector3(0, y, 0), new Vector3(0, 0, 1), uvRepU, uvRepV);
                Face(b, new Vector3(0, 0, -z), new Vector3(-x, 0, 0), new Vector3(0, y, 0), new Vector3(0, 0, -1), uvRepU, uvRepV);
                return b.ToMesh("Box");
            });
        }

        static void Face(B b, Vector3 c, Vector3 u, Vector3 v, Vector3 nn, float ru, float rv)
        {
            int i0 = b.Add(c - u - v, nn, new Vector2(0, 0));
            int i1 = b.Add(c + u - v, nn, new Vector2(ru, 0));
            int i2 = b.Add(c + u + v, nn, new Vector2(ru, rv));
            int i3 = b.Add(c - u + v, nn, new Vector2(0, rv));
            b.Tri(i0, i1, i2); b.Tri(i0, i2, i3);
        }

        // ------------------------------------------------------------ 円柱・円錐
        /// <summary>three.js の CylinderGeometry(rTop, rBot, h, seg, 1, open)。</summary>
        public static Mesh Cylinder(float rTop, float rBot, float h, int seg = 16, bool open = false,
                                    float thetaStart = 0f, float thetaLen = Mathf.PI * 2f)
        {
            string key = "cyl|" + rTop + "|" + rBot + "|" + h + "|" + seg + "|" + open + "|" + thetaStart + "|" + thetaLen;
            return Cached(key, () =>
            {
                var b = new B();
                AddCylinder(b, rTop, rBot, h, seg, open, thetaStart, thetaLen, 0f);
                return b.ToMesh("Cylinder");
            });
        }

        /// <summary>three.js の ConeGeometry(r, h, seg)（＝上の半径が0の円柱）。</summary>
        public static Mesh Cone(float r, float h, int seg = 16, bool open = false)
        {
            return Cylinder(0f, r, h, seg, open);
        }

        static void AddCylinder(B b, float rTop, float rBot, float h, int seg, bool open,
                                float thetaStart, float thetaLen, float yOff)
        {
            float half = h / 2f, slope = (rBot - rTop) / h;
            int start = b.v.Count;
            var grid = new int[2, seg + 1];
            for (int y = 0; y <= 1; y++)
            {
                float vv = y;
                float r = vv * (rBot - rTop) + rTop;
                for (int x = 0; x <= seg; x++)
                {
                    float u = (float)x / seg;
                    float th = u * thetaLen + thetaStart;
                    float s = Mathf.Sin(th), c = Mathf.Cos(th);
                    var p = new Vector3(r * s, -vv * h + half + yOff, r * c);
                    var nn = new Vector3(s, slope, c).normalized;
                    grid[y, x] = b.Add(p, nn, new Vector2(u, 1 - vv));
                }
            }
            for (int x = 0; x < seg; x++)
            {
                int a = grid[0, x], bb = grid[1, x], c = grid[1, x + 1], d = grid[0, x + 1];
                if (rTop > 0f) b.Tri(a, bb, d);
                if (rBot > 0f) b.Tri(bb, c, d);
            }
            if (open) return;
            if (rTop > 0f) Cap(b, true, rTop, half + yOff, seg, thetaStart, thetaLen);
            if (rBot > 0f) Cap(b, false, rBot, -half + yOff, seg, thetaStart, thetaLen);
        }

        static void Cap(B b, bool top, float r, float y, int seg, float thetaStart, float thetaLen)
        {
            float sign = top ? 1f : -1f;
            int c0 = b.v.Count;
            for (int x = 1; x <= seg; x++) b.Add(new Vector3(0, y, 0), new Vector3(0, sign, 0), new Vector2(0.5f, 0.5f));
            int r0 = b.v.Count;
            for (int x = 0; x <= seg; x++)
            {
                float u = (float)x / seg;
                float th = u * thetaLen + thetaStart;
                float s = Mathf.Sin(th), c = Mathf.Cos(th);
                b.Add(new Vector3(r * s, y, r * c), new Vector3(0, sign, 0),
                      new Vector2(c * 0.5f + 0.5f, s * 0.5f * sign + 0.5f));
            }
            for (int x = 0; x < seg; x++)
            {
                int c = c0 + x, i = r0 + x;
                if (top) b.Tri(i, i + 1, c); else b.Tri(i + 1, i, c);
            }
        }

        // ------------------------------------------------------------ 球（部分も）
        /// <summary>three.js の SphereGeometry(r, wSeg, hSeg, phiStart, phiLen, thetaStart, thetaLen)。</summary>
        public static Mesh Sphere(float r, int wSeg = 16, int hSeg = 12,
                                  float phiStart = 0f, float phiLen = Mathf.PI * 2f,
                                  float thetaStart = 0f, float thetaLen = Mathf.PI)
        {
            string key = "sph|" + r + "|" + wSeg + "|" + hSeg + "|" + phiStart + "|" + phiLen + "|" + thetaStart + "|" + thetaLen;
            return Cached(key, () =>
            {
                var b = new B();
                AddSphere(b, r, wSeg, hSeg, phiStart, phiLen, thetaStart, thetaLen, Vector3.zero);
                return b.ToMesh("Sphere");
            });
        }

        static void AddSphere(B b, float r, int wSeg, int hSeg, float phiStart, float phiLen,
                              float thetaStart, float thetaLen, Vector3 off)
        {
            float thetaEnd = Mathf.Min(thetaStart + thetaLen, Mathf.PI);
            var grid = new int[hSeg + 1, wSeg + 1];
            for (int iy = 0; iy <= hSeg; iy++)
            {
                float v = (float)iy / hSeg;
                for (int ix = 0; ix <= wSeg; ix++)
                {
                    float u = (float)ix / wSeg;
                    float ph = phiStart + u * phiLen, th = thetaStart + v * thetaLen;
                    var p = new Vector3(-r * Mathf.Cos(ph) * Mathf.Sin(th), r * Mathf.Cos(th), r * Mathf.Sin(ph) * Mathf.Sin(th));
                    grid[iy, ix] = b.Add(p + off, p.sqrMagnitude > 0 ? p.normalized : Vector3.up, new Vector2(u, 1 - v));
                }
            }
            for (int iy = 0; iy < hSeg; iy++)
                for (int ix = 0; ix < wSeg; ix++)
                {
                    int a = grid[iy, ix + 1], bb = grid[iy, ix], c = grid[iy + 1, ix], d = grid[iy + 1, ix + 1];
                    if (iy != 0 || thetaStart > 0) b.Tri(a, bb, d);
                    if (iy != hSeg - 1 || thetaEnd < Mathf.PI) b.Tri(bb, c, d);
                }
        }

        // ------------------------------------------------------------ カプセル
        /// <summary>three.js の CapsuleGeometry(r, length)（筒の長さ＋両端の半球）。縦向き。</summary>
        public static Mesh Capsule(float r, float length, int seg = 8)
        {
            string key = "cap|" + r + "|" + length + "|" + seg;
            return Cached(key, () =>
            {
                var b = new B();
                AddCylinder(b, r, r, length, seg, true, 0f, Mathf.PI * 2f, 0f);
                AddSphere(b, r, seg, 4, 0f, Mathf.PI * 2f, 0f, Mathf.PI / 2f, new Vector3(0, length / 2f, 0));
                AddSphere(b, r, seg, 4, 0f, Mathf.PI * 2f, Mathf.PI / 2f, Mathf.PI / 2f, new Vector3(0, -length / 2f, 0));
                return b.ToMesh("Capsule");
            });
        }

        // ------------------------------------------------------------ 輪
        /// <summary>three.js の TorusGeometry(R, tube, radialSeg, tubularSeg)。XY 面に寝ている。</summary>
        public static Mesh Torus(float R, float tube, int radialSeg = 8, int tubularSeg = 24)
        {
            string key = "tor|" + R + "|" + tube + "|" + radialSeg + "|" + tubularSeg;
            return Cached(key, () =>
            {
                var b = new B();
                for (int j = 0; j <= radialSeg; j++)
                    for (int i = 0; i <= tubularSeg; i++)
                    {
                        float u = (float)i / tubularSeg * Mathf.PI * 2f;
                        float v = (float)j / radialSeg * Mathf.PI * 2f;
                        var p = new Vector3((R + tube * Mathf.Cos(v)) * Mathf.Cos(u),
                                            (R + tube * Mathf.Cos(v)) * Mathf.Sin(u),
                                            tube * Mathf.Sin(v));
                        var c = new Vector3(R * Mathf.Cos(u), R * Mathf.Sin(u), 0);
                        b.Add(p, (p - c).normalized, new Vector2((float)i / tubularSeg, (float)j / radialSeg));
                    }
                for (int j = 1; j <= radialSeg; j++)
                    for (int i = 1; i <= tubularSeg; i++)
                    {
                        int a = (tubularSeg + 1) * j + i - 1;
                        int bb = (tubularSeg + 1) * (j - 1) + i - 1;
                        int c = (tubularSeg + 1) * (j - 1) + i;
                        int d = (tubularSeg + 1) * j + i;
                        b.Tri(a, bb, d); b.Tri(bb, c, d);
                    }
                return b.ToMesh("Torus");
            });
        }

        /// <summary>three.js の RingGeometry(inner, outer, seg)。XY 面（表は +Z）。</summary>
        public static Mesh Ring(float inner, float outer, int seg = 28)
        {
            string key = "ring|" + inner + "|" + outer + "|" + seg;
            return Cached(key, () =>
            {
                var b = new B();
                for (int j = 0; j <= 1; j++)
                {
                    float r = inner + j * (outer - inner);
                    for (int i = 0; i <= seg; i++)
                    {
                        float s = (float)i / seg * Mathf.PI * 2f;
                        var p = new Vector3(r * Mathf.Cos(s), r * Mathf.Sin(s), 0);
                        b.Add(p, new Vector3(0, 0, 1), new Vector2(p.x / outer / 2 + 0.5f, p.y / outer / 2 + 0.5f));
                    }
                }
                for (int i = 0; i < seg; i++)
                {
                    int a = i, bb = i + seg + 1, c = i + seg + 2, d = i + 1;
                    b.Tri(a, bb, d); b.Tri(bb, c, d);
                }
                return b.ToMesh("Ring");
            });
        }

        /// <summary>three.js の CircleGeometry(r, seg)。XY 面（表は +Z）。seg=3 で三角。</summary>
        public static Mesh Circle(float r, int seg = 16)
        {
            string key = "circ|" + r + "|" + seg;
            return Cached(key, () =>
            {
                var b = new B();
                b.Add(Vector3.zero, new Vector3(0, 0, 1), new Vector2(0.5f, 0.5f));
                for (int s = 0; s <= seg; s++)
                {
                    float a = (float)s / seg * Mathf.PI * 2f;
                    b.Add(new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), 0), new Vector3(0, 0, 1),
                          new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                }
                for (int i = 1; i <= seg; i++) b.Tri(i, i + 1, 0);
                return b.ToMesh("Circle");
            });
        }

        /// <summary>three.js の PlaneGeometry(w, h)。XY 面（表は +Z＝Unity ではカメラ側の -Z）。</summary>
        public static Mesh Plane(float w, float h, float uvRepU = 1f, float uvRepV = 1f)
        {
            string key = "pl|" + w + "|" + h + "|" + uvRepU + "|" + uvRepV;
            return Cached(key, () =>
            {
                var b = new B();
                int a = b.Add(new Vector3(-w / 2, h / 2, 0), new Vector3(0, 0, 1), new Vector2(0, uvRepV));
                int bb = b.Add(new Vector3(-w / 2, -h / 2, 0), new Vector3(0, 0, 1), new Vector2(0, 0));
                int c = b.Add(new Vector3(w / 2, -h / 2, 0), new Vector3(0, 0, 1), new Vector2(uvRepU, 0));
                int d = b.Add(new Vector3(w / 2, h / 2, 0), new Vector3(0, 0, 1), new Vector2(uvRepU, uvRepV));
                b.Tri(a, bb, d); b.Tri(bb, c, d);
                return b.ToMesh("Plane");
            });
        }

        /// <summary>
        /// 床に寝かせた板（表が上）。three.js の Plane を rotation.x = -π/2 したのと同じ。
        /// </summary>
        public static Mesh FlatUp(float w, float d, float uvRepU = 1f, float uvRepV = 1f)
        {
            string key = "flat|" + w + "|" + d + "|" + uvRepU + "|" + uvRepV;
            return Cached(key, () =>
            {
                var b = new B();
                var up = new Vector3(0, 1, 0);
                // three.js の座標で。奥（-z）が uv の上
                int a = b.Add(new Vector3(-w / 2, 0, -d / 2), up, new Vector2(0, uvRepV));
                int bb = b.Add(new Vector3(-w / 2, 0, d / 2), up, new Vector2(0, 0));
                int c = b.Add(new Vector3(w / 2, 0, d / 2), up, new Vector2(uvRepU, 0));
                int dd = b.Add(new Vector3(w / 2, 0, -d / 2), up, new Vector2(uvRepU, uvRepV));
                b.Tri(a, bb, dd); b.Tri(bb, c, dd);
                return b.ToMesh("FlatUp");
            });
        }
    }
}
