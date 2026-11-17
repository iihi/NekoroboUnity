using UnityEngine;
using UnityEngine.InputSystem;

namespace Nekorobo
{
    /// <summary>
    /// メニューの上下左右（HTML版 menuTick）。**スティックも十字キーと同じく、選択枠を1つずつ動かす**
    /// （担当「ショップ・面選択・ユーザー選択以外は、選択枠有りのスティック・十字キーで選択」）。
    /// 斜めに倒したときは、大きく倒した向きだけ。押し続けると 0.38 秒待ってから 0.12 秒ごとに続けて動く（長い一覧を送れるように）。
    /// 各画面は「押した瞬間」（今 true で前が false）で動くので、続けて動かすときは、そのフレームだけ離したことにする
    /// </summary>
    public static class Nav
    {
        const float ST = 0.55f, WAIT = 0.38f, EVERY = 0.12f;
        static int frame = -1, repK = -1; static float repT;
        static bool cu, cd, cl, cr;

        /// <summary>up・down・left・right（キーと十字キーで読んだ物）にスティックを足し、押し続けの送りを入れる</summary>
        public static void Apply(ref bool up, ref bool down, ref bool left, ref bool right)
        {
            if (frame == Time.frameCount) { up = cu; down = cd; left = cl; right = cr; return; }   // 同じフレームに2回呼ばれても同じ
            frame = Time.frameCount;
            foreach (var gp in Gamepad.all)
            {
                var s = gp.leftStick.ReadValue();
                bool h = Mathf.Abs(s.x) >= Mathf.Abs(s.y);
                if (h && s.x < -ST) left = true;
                if (h && s.x > ST) right = true;
                if (!h && s.y > ST) up = true;
                if (!h && s.y < -ST) down = true;
            }
            int k = up ? 0 : down ? 1 : left ? 2 : right ? 3 : -1;
            if (k != repK) { repK = k; repT = WAIT; }
            else if (k >= 0 && (repT -= Time.unscaledDeltaTime) <= 0)
            {
                repT = EVERY;
                if (k == 0) up = false; else if (k == 1) down = false; else if (k == 2) left = false; else right = false;
            }
            cu = up; cd = down; cl = left; cr = right;
        }
        public static void Apply(ref bool up, ref bool down) { bool l = false, r = false; Apply(ref up, ref down, ref l, ref r); }
    }
}
