using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// 通信口の**中身**が守る形（JS版 net.js の「通信口の決まり」と同じ）。
    /// いまの中身は NetWs（部屋サーバーへの WebSocket）。EOS などへ替えるときは、これを満たす別の物を作って
    /// Net.Backends に1行足す。ゲーム側は Net しか見ないので、1行も変わらない。
    ///
    /// **結果は Net の Report〜 で知らせる**（主スレッドから。裏の糸で受けたときは Net.Post で主スレッドへ渡す）。
    ///   つながった      → Net.ReportOpen()      … このあと名乗る（hello）のは中身の役目
    ///   切れた／届かない → Net.ReportLost(理由)
    ///   自分の番号       → Net.ReportId(id)
    ///   部屋の中身       → Net.ReportRoom(部屋, 届いた物)   部屋が null なら抜けた
    ///   部屋の一覧       → Net.ReportRooms(一覧)
    ///   始まった         → Net.ReportStart(部屋, 届いた物)   届いた物の cfg が始める中身
    ///   部屋の中の便り   → Net.ReportFrom(送った人, 中身)
    ///   うまくいかない   → Net.ReportErr(理由, 届いた物)
    ///
    /// **部屋の中身の形**：{ code, name, priv, max, host, players:[{ id, name, host }] }
    /// host は「いま計算する人」。抜けたら次の人へ移すこと。
    /// </summary>
    public interface INetBackend
    {
        /// <summary>画面に出す呼び名。</summary>
        string Label { get; }

        /// <summary>つなぎ先を入れる欄が要るか（JS版 caps.addr）。要らない仕組みでは、オンラインの画面に「サーバ」の欄を出さない。</summary>
        bool NeedsAddr { get; }

        /// <summary>つなぐ（名乗るのは Net.Name）。すぐ返し、つながったら Net.ReportOpen。**この中で Net の知らせを出さないこと**（Net が出す）。</summary>
        void Connect();

        /// <summary>畳む。このあとは何も知らせない。</summary>
        void Close();

        bool SetName(string name);
        bool CreateRoom(string name, bool priv);
        bool JoinRoom(string code);
        bool ListRooms();
        bool LeaveRoom();
        bool StartRoom(JObject cfg);

        /// <summary>部屋の中へ届ける。id が null なら全員へ（自分には届かない）。</summary>
        bool Send(JObject d, int? id);

        /// <summary>送った量[バイト]（通信量を測る用）。数えない仕組みは 0。</summary>
        long SentBytes { get; }

        // ---- つなぎ先（NeedsAddr が false の仕組みでは空でよい）
        /// <summary>手で入れたつなぎ先（この端末で覚える）。</summary>
        string Addr { get; }
        void SetAddr(string v);
        /// <summary>手で入れておらず、置いた先の設定（net.json）を使っているか。</summary>
        bool AddrFromCfg { get; }
        /// <summary>つなぐ先の文字（画面に出す）。持たない仕組みは呼び名でよい。</summary>
        string Where { get; }
    }
}
