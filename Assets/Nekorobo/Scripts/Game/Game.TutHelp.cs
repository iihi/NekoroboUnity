using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 研修（ストーリー）の手助け（HTML版 tutHelpTick）。担当「チュートリアルの場合のみ、おじさんが一言いって助けてくれるように」
    ///   川・池に落ちた（ジャンプの高さが変わって、跳んでも岸へ上がれない）
    ///     → 店主がひとこと言って、クレーンで引き上げる（4つ長押しの救済と同じクレーン。お金は取らない）
    ///   故障した（旋回・駆動）・壊れて止まりそう（体力 0%）
    ///     → 店主がひとこと言って、体力 100% まで直す（アイテムのリペアと同じ出し方）
    /// 遊んでいる人（キーボード・パッド）だけ。ライバルの NPC は今までどおり。オンラインでは出さない
    /// </summary>
    public partial class Game
    {
        const float TUT_WET_SEC = 1.0f;          // 水に落ちてから、店主が引き上げるまで[秒]
        const float TUT_FIX_SEC = 1.2f;          // 故障してから、店主が直すまで[秒]（何が起きたか見えるように少し待つ）

        bool TutHelpOn(Player P)
        {
            return entry != null && course != null && !NpOn && P != null && (P.src.kind == "key" || P.src.kind == "pad");
        }

        // 店主のひとこと（案内の吹き出しへ。出来事へのひとことと同じ出し方）
        void TutSay(string text, float sec = 3.2f)
        {
            if (tut == null) return;
            tut.reactT = sec; tut.reactSay = text;
            TutRender(false, true);
            SeUi("talk_open");
        }

        void TutHelpTick(float dt)
        {
            if (state != "play") return;
            foreach (var P in players)
            {
                if (!TutHelpOn(P)) continue;
                if (P.down || P.cr != null) { P.tutWetT = 0; continue; }
                // 川・池の水（~ と、橋の下 b）に落ちている
                var p = P.ent.rb.position; char ch = Tiles.At(under ?? map, p);
                bool wet = (ch == '~' || ch == 'b') && p.y < 0.1f;
                P.tutWetT = wet ? P.tutWetT + dt : 0;
                if (P.tutWetT >= TUT_WET_SEC)
                {
                    P.tutWetT = 0;
                    TutSay("川に落ちたな！ わしがクレーンで引き上げてやろう！");
                    RescueStart(P, true);
                    continue;
                }
                if (P.broken != null || P.brokenDrive) TutFixCall(P);
                if (P.tutFixT > 0 && (P.tutFixT -= dt) <= 0) { P.tutFixT = 0; TutFix(P); }
            }
        }

        // 直しに行く（まだ行っていなければ）
        void TutFixCall(Player P)
        {
            if (P.tutFixT > 0) return;
            P.tutFixT = TUT_FIX_SEC;
            TutSay("おっと、故障したな！ わしが直してやろう！");
        }

        void TutFix(Player P)
        {
            float before = P.botDmg;
            P.botDmg = 0; P.broken = null; P.brokenDrive = false; P.misfire = false;
            var pp = P.ent.rb.position;
            hud.Pop(pp + Vector3.up * 1.0f, Lang.Tr("修理 +{0}%", Mathf.Max(1, Mathf.RoundToInt(before))), 0x5fd08a, true);
            Se("it_repair", pp, P);
            Say(P, "直ったにゃ！", 1.4f);
            SetFace(P, "happy", 1.2f);
        }
    }
}
