using UnityEngine;

namespace Nekorobo
{
    // ================================================================ 実績（段階5で中身を入れる）
    // ゲームの中で「取った」ことを知らせる口だけ先に置く（JS版の ach / achItem / achHurt / achDeliver / achStageEnd）。
    public partial class Game
    {
        public void Ach(Player P, string id) { }
        void AchItem(Player P, string k) { }
        void AchHurt(Player P) { }
        void AchDeliver(Player P, Order o, float integ) { }
        void AchStageEnd(bool cleared) { }
    }
}
