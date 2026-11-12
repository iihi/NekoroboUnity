using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nekorobo
{
    /// <summary>
    /// ポーズメニューの出し入れと、オンラインで途中で抜けた人の扱い（JS版 pauseOpen / pauseClose / pauseTick / pauseQuit / npQuit）。
    ///   開く … Esc（開くだけ）・P（開け閉め）・パッドの≡（START。開け閉め）。遊んでいる最中で、ほかの画面が出ていないときだけ
    ///   1人・同じ画面のマルチはゲームを止める（Time.timeScale = 0）。オンラインは止めない
    ///   開いている間（と、閉じたあと決定に使ったボタンを離すまで）は、この画面の人の入力をロボへ通さない（pauseBlock）
    /// </summary>
    public partial class Game
    {
        [System.NonSerialized] public bool pauseBlock;
        public bool PauseOn { get { return hud != null && hud.Pause != null && hud.Pause.Open; } }

        bool CanPause()
        {
            return stage != null && (state == "play" || state == "ready") && result == null
                && !hud.TitleOpen && !hud.ShopOpen && !TalkOn && !hud.Result.Open && !hud.FinalOpen && !hud.EndingOpen && !hud.MenuOpen
                && !hud.NetWaitShown;
        }

        public void PauseOpen()
        {
            if (PauseOn || !CanPause()) return;
            if (!NpOn) Time.timeScale = 0;                 // オンラインでは止めない
            hud.Pause.Show();
        }

        public void PauseClose()
        {
            if (!PauseOn) return;
            hud.Pause.Close();
            if (Time.timeScale == 0) Time.timeScale = 1;
            pauseBlock = true;                             // 押していたボタンを離すまで、ロボへ通さない
        }

        void PauseToggle() { if (PauseOn) PauseClose(); else PauseOpen(); }

        /// <summary>毎フレーム（Update の始め）。開け閉めのボタンと、ほかの画面に変わったら閉じる。</summary>
        void PauseTick()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.pKey.wasPressedThisFrame) PauseToggle();
                else if (kb.escapeKey.wasPressedThisFrame && !PauseOn) PauseOpen();   // 開いている間の Esc はメニューの「再開」「やめない」
            }
            foreach (var gp in Gamepad.all) if (gp.startButton.wasPressedThisFrame) { PauseToggle(); break; }
            if (pauseBlock)
            {
                bool held = kb != null && (kb.enterKey.isPressed || kb.spaceKey.isPressed || kb.escapeKey.isPressed);
                foreach (var gp in Gamepad.all) held |= gp.buttonSouth.isPressed || gp.buttonEast.isPressed || gp.startButton.isPressed;
                if (!held) pauseBlock = false;
            }
            // ほかの画面に変わった（オンラインで面が終わった・切れた・タイトルへ）ら閉じる
            if (PauseOn && !(stage != null && !hud.TitleOpen && result == null && (state == "play" || state == "ready"))) PauseClose();
            // 念のため：ポーズでないのに止まったままにしない
            if (!PauseOn && Time.timeScale == 0) Time.timeScale = 1;
        }

        /// <summary>1人・同じ画面のマルチ：タイトルへ。</summary>
        public void PauseTitle()
        {
            PauseClose();
            BackToTitle();
        }

        /// <summary>オンライン：自分だけ抜ける。参加者はホストに知らせてから部屋を出る（ホストはその人のロボをリタイアにする）。</summary>
        public void PauseQuit()
        {
            EarlyQuit.Quitted();                                // 自動マッチングの対戦なら途中退出
            if (NetGuest) Net.Send(new JObject { ["p"] = "quit" });
            PauseClose();
            NpEnd();                                       // 先に同期をやめる（部屋を出た知らせを「ホストが切れた」と取り違えない）
            if (Net.Room != null) Net.LeaveRoom();
            BackToTitle();
        }

        /// <summary>
        /// ホスト：その人が遊んでいる最中に抜けた（ポーズの「ゲームから抜ける」・部屋から居なくなった）。
        /// ロボはその場でリタイア（もう戻らない）にして、この回は最下位あつかい（P.quit。結果と上の帯の順位）。
        /// 参加者の画面へは、配る値（pl）の「止まっている」で配る（1＝リタイア、2＝抜けた）
        /// </summary>
        void NpQuit(int id)
        {
            if (!NetHost || result != null || !(state == "play" || state == "ready")) return;
            int i = np.ids.IndexOf(id);
            var P = i >= 0 && i < players.Count ? players[i] : null;
            if (P == null || P.quit || P.src.kind == "npc") return;
            P.quit = true;
            if (P.down)
            {
                P.revT = 0; P.botDmg = 100;                // 海に落ちて戻る途中だったら、戻らないようにする
                if (players.TrueForAll(q => q.down && q.revT <= 0)) Finish(false);
            }
            else DownPlayer(P, 0);
        }

        /// <summary>ホスト：部屋から居なくなった人を、抜けたあつかいにする（部屋の中身が変わったとき）。</summary>
        void NpQuitGone()
        {
            if (!NetHost || Net.Room == null) return;
            var ps = Net.Room["players"] as JArray; if (ps == null) return;
            var here = new System.Collections.Generic.HashSet<int>();
            foreach (var m in ps) here.Add((int?)m["id"] ?? -1);
            foreach (var id in np.ids.ToArray()) if (id >= 0 && !here.Contains(id)) NpQuit(id);
        }

        /// <summary>その枠の人が、もう部屋に居ないか（結果の画面で待たない。JS版 resultAllOk の「抜けた人」）。</summary>
        public bool SeatGone(int i)
        {
            if (!NpOn || Net.Room == null || i >= np.ids.Count || np.ids[i] < 0) return false;
            var ps = Net.Room["players"] as JArray; if (ps == null) return false;
            foreach (var m in ps) if ((int?)m["id"] == np.ids[i]) return false;
            return true;
        }
    }
}
