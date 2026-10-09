using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 当たり判定を持つ物1つぶん。JS版の makeBody が返す ent と同じ役目。
    /// 種類（kind）で「ぶつかったら何が起きるか」を分ける。
    ///   floor / pit / wall / fence / counter / table / chair / guest / robot / ramp / prop
    /// </summary>
    public class Ent : MonoBehaviour
    {
        static int seq;

        public int id;
        public int nid;                 // 面を組んだ順の通し番号（オンラインで物を指す。JS版と同じ順）
        public float[] netLast;         // 前に配った位置（動いていない物は送らない）
        public string kind;
        public bool isFixed;            // 動かない物（壁・床・カウンター）
        public bool infMass;            // 押し返されない物（固定物・車・動く床）。衝撃は無限質量で計算
        public Rigidbody rb;
        public Vector3 size;            // 当たり判定の外寸[m]
        public Vector3 prevV;           // ぶつかる直前の速度（衝撃の強さに使う）
        public float hp = 100;

        // 置かれた場所。落ちたときはここへ戻す
        public bool hasHome;
        public Vector3 home;

        public Player player;           // ロボならその人
        public Player blame;            // 家具どうしの連鎖でも、元のロボまでたどる

        // 客
        public int pri;
        public bool walkStop;
        public bool bench;              // ベンチ（案内の矢印 "bench" が指す）           // 巡回している客が、ぶつかられて歩くのをやめた
        public GameObject ring;         // 倒れた印
        public float modelH;
        public GameObject vomit;        // HP が減ると口から出る虹
        public int vomitTier;            // モデルに差し替えたときの見た目の高さ（頭の上の札をそれに合わせる）

        // 落ちた・浸かった
        public bool sunk;
        public float wet;
        public int fellN;
        public float fellAt = -99f;

        void Awake() { id = ++seq; }

        public Vector3 Pos { get { return rb != null ? rb.position : transform.position; } }
        public Vector3 Vel { get { return rb != null && !rb.isKinematic ? rb.linearVelocity : Vector3.zero; } }
        public float Mass { get { return rb != null ? rb.mass : 0f; } }

        void OnCollisionEnter(Collision c)
        {
            var g = Game.I;
            if (g == null) return;
            var other = c.collider.GetComponentInParent<Ent>();
            if (other == null || other == this) return;
            // 両方に届くことがあるので、受け持つのは1回だけにする。
            // 動く物の側が受け持ち、両方とも動く物なら番号の小さい方。
            bool meDyn = rb != null && !rb.isKinematic;
            bool otDyn = other.rb != null && !other.rb.isKinematic;
            if (!meDyn) return;
            if (otDyn && id > other.id) return;
            g.OnContact(this, other);
        }
    }
}
