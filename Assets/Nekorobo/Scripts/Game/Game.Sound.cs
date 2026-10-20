using UnityEngine;
using UnityEngine.InputSystem;

namespace Nekorobo
{
    /// <summary>
    /// 効果音と BGM の「どこで鳴らすか」（JS版 se / seUi / bgmTick）。音の中身は HTML版の se.json、鳴らす仕組みは Sound.cs。
    /// </summary>
    public partial class Game
    {
        /// <summary>
        /// 遊びの中の音。at … 世界の位置（渡すと、画面の左右に振って鳴らす）。who … 鳴らした人（NPC なら少し小さく）。
        /// **オンラインではホストが鳴らした音を参加者にも配る**（"se"）。参加者は遊びの計算を回さないため。
        /// local … 配らない。その関数ごと参加者の画面でも呼ばれる所（爆発の見た目など）で使う。
        /// </summary>
        public void Se(string name, Vector3? at = null, Player who = null, float pitch = 1, float vol = 1, bool local = false)
        {
            if (who != null && who.src != null && who.src.kind == "npc") vol *= 0.7f;
            float pan = 0;
            if (at != null && mainCam != null)
            {
                var vp = mainCam.WorldToViewportPoint(at.Value);
                pan = Mathf.Clamp(vp.x * 2 - 1, -1, 1) * 0.6f;
            }
            Sound.Play(name, pitch, vol, pan);
            if (!local)
            {
                if (at != null) NetEv("se", name, R2(at.Value.x), R2(at.Value.y), R2(-at.Value.z), Mathf.Round(pitch * 1000) / 1000f, Mathf.Round(vol * 100) / 100f);
                else NetEv("se", name, null, 0, 0, Mathf.Round(pitch * 1000) / 1000f, Mathf.Round(vol * 100) / 100f);
            }
        }

        /// <summary>画面の操作の音（配らない・左右に振らない）。</summary>
        public static void SeUi(string name, float vol = 1, float pitch = 1) { Sound.Play(name, pitch, vol, 0); }

        // ---- BGM。assets/bgm/ に置いたファイルを、場面ごとにくり返し鳴らす。**ファイルが無ければ鳴らさない。**
        // 場面は毎フレーム、画面の様子から決める（あちこちで切り替えると、戻し忘れが出るため）。
        //   title … タイトルと、その先の選ぶ画面すべて（エンディングもここ）
        //   game  … 遊んでいる間（3・2・1、会話、結果の画面も含む）
        //   shop  … ショップ
        void SoundTick()
        {
            if (hud == null) return;
            string scene = hud.TitleOpen || hud.EndingOpen ? "title" : hud.ShopOpen ? "shop" : "game";
            Sound.Bgm("assets/bgm/" + (scene == "title" ? "BGM01.mp3" : scene == "shop" ? "BGM03.mp3" : "BGM02.mp3"));
            // 止めている間は少し下げる（タイトルの裏で止めているのは除く）
            Sound.BgmLevel(hud.MenuOpen && !hud.TitleOpen ? 0.4f : 1);
            // M キーで音の ON/OFF（BGM も効果音もまとめて止める）
            var kb = Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame && !kb.ctrlKey.isPressed && !kb.altKey.isPressed) hud.FlipSound();
        }
    }
}
