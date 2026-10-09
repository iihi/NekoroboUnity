using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// オンライン対戦の通信口。JS版の net.js（決まり）と net-ws.js（中身：部屋サーバーへの WebSocket）を写したもの。
    ///
    /// **部屋サーバーは HTML版と同じもの**（node server.js の /ws、または node rooms-server.js）。
    /// やりとりも HTML版と同じ JSON なので、同じ部屋に HTML版と Unity版が入れる。
    /// サーバがするのは待ち合わせと中継だけで、ゲームの中身は「ホストが計算して配る」（Game.Online）。
    ///
    /// **ゲームの中身は持たない。**部屋の出入りと、中継のやりとり（Send / OnFrom）だけ。
    /// 中身を別の仕組み（EOS など）へ替えるときは、この形を守った別の物を作ればよい。
    ///
    ///   つなぐ先の優先順：手で入れた値（この端末で覚える）→ HTML版の net.json の ws → localhost:8123
    ///
    /// **WebSocket は自前**（TCP の上に、握手と文字のフレームだけ）。部屋サーバー（rooms.js）も自前なので、それと同じ範囲。
    /// 受けている糸は、ソケットを閉じれば必ず抜ける（再生を止めたとき・コンパイルし直す前に閉じる）。
    ///
    /// 受けるのは裏の糸1本。届いた便りは列に積んで、**主スレッドの Pump() で配る**（Hud.Update が毎フレーム呼ぶ）。
    /// 送るのは主スレッドから（便りは小さいので、そのまま書く）。
    /// エディタでは、再生を止めるときとコンパイルし直す前に必ず畳む（Editor/NetReset.cs）。
    /// </summary>
    public static class Net
    {
        // ---- 状態（JS版 NET）
        public static string State = "off";          // off / connecting / on / error
        public static int Id = -1;                   // 自分の番号（サーバがくれる）
        public static JObject Room;                  // いまの部屋 { code, name, priv, max, host, players:[{id,name,host}] }
        public static string Name = "";
        public static string Why = "";               // つながらなかった理由

        // ---- 配るもの（JS版の on("state") など）
        public static event Action OnState;
        public static event Action<JObject> OnRoom;      // 部屋の中身が変わった（Room が null なら抜けた）
        public static event Action<JArray> OnRooms;      // 開いている部屋の一覧
        public static event Action<JObject> OnStart;     // 始まった（m.room, m.cfg）
        public static event Action<int, JObject> OnFrom; // 部屋の中の便り（送った人, 中身）
        public static event Action<JObject> OnErr;

        public const string Label = "部屋サーバー（WebSocket）";
        const string WS_KEY = "nekorobo.ws";

        static Conn conn;
        // 裏の糸から主スレッドへ渡す列（届いた文字・つながった／切れた知らせ）
        static readonly ConcurrentQueue<Action> inbox = new ConcurrentQueue<Action>();
        static string cfgWs;                          // HTML版の net.json の ws（読んだら控える）
        static bool cfgRead;

        /// <summary>届いた便りを配る。主スレッドから毎フレーム呼ぶ（Hud.Update）。</summary>
        public static void Pump()
        {
            Action a;
            int n = 0;
            while (n++ < 500 && inbox.TryDequeue(out a))
                try { a(); } catch (Exception e) { Debug.LogException(e); }
        }

        // ---------------------------------------------------------------- つなぎ先
        /// <summary>手で入れたつなぎ先（ホスト名:ポート）。空なら net.json か localhost:8123。</summary>
        public static string Addr { get { return PlayerPrefs.GetString(WS_KEY, ""); } }
        public static void SetAddr(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length > 0) PlayerPrefs.SetString(WS_KEY, v); else PlayerPrefs.DeleteKey(WS_KEY);
            PlayerPrefs.Save();
        }
        static string CfgWs()
        {
            if (!cfgRead)
            {
                cfgRead = true;
                var j = DataRoot.ReadJson("net.json");
                cfgWs = j != null ? (string)j["ws"] : null;
            }
            return cfgWs ?? "";
        }
        public static bool AddrFromCfg { get { return Addr.Length == 0 && CfgWs().Trim().Length > 0; } }
        /// <summary>つなぐ URL（画面にも出す）。</summary>
        public static string Where
        {
            get
            {
                string h = Addr;
                if (h.Length == 0) h = CfgWs().Trim();
                if (h.Length == 0) h = "localhost:8123";               // HTML版の node server.js（開発の置き方）
                if (h.StartsWith("ws://") || h.StartsWith("wss://")) return h.TrimEnd('/') + "/ws";
                return "ws://" + h.Trim('/') + "/ws";
            }
        }

        /// <summary>畳んで、受け口も全部外す（再生のし直しで二重に受けないように）。</summary>
        public static void Reset()
        {
            Close();
            OnState = null; OnRoom = null; OnRooms = null; OnStart = null; OnFrom = null; OnErr = null;
            Id = -1; Why = "";
        }

        public static bool IsHost { get { return Room != null && (int?)Room["host"] == Id; } }

        // ---------------------------------------------------------------- つなぐ・畳む
        public static void Connect(string name = null)
        {
            if (name != null) Name = name;
            if (conn != null && (State == "on" || State == "connecting")) return;
            State = "connecting"; Why = "";
            Fire(OnState);
            var c = new Conn(Where);
            conn = c;
            c.Start();
        }

        public static void Close()
        {
            var c = conn;
            conn = null; Room = null; State = "off";
            if (c != null) c.Close();
            Action a; while (inbox.TryDequeue(out a)) { }            // 畳む前に届いていた物は捨てる
        }

        // 裏の糸から呼ばれる（主スレッドの物には触らず、inbox へ積むだけ）
        static void Opened(Conn c)
        {
            inbox.Enqueue(() =>
            {
                if (conn != c) return;
                State = "on"; Why = "";
                Raw(new JObject { ["t"] = "hello", ["name"] = Name });
                Fire(OnState);
            });
        }
        static void Got(Conn c, string txt) { inbox.Enqueue(() => { if (conn == c) Handle(txt); }); }
        static void Lost(Conn c, bool wasOpen, string msg)
        {
            inbox.Enqueue(() =>
            {
                if (conn != c) return;                                // 先に畳んだ／つなぎ直した
                conn = null; Room = null; State = "error";
                Why = wasOpen ? "接続が切れました" : "サーバにつながりません";
                if (!wasOpen) Debug.Log("[Net] つながらない: " + c.url + " " + msg);
                Fire(OnState);
            });
        }

        static void Handle(string txt)
        {
            JObject m;
            try { m = JObject.Parse(txt); } catch { return; }
            string t = (string)m["t"];
            switch (t)
            {
                case "hi": Id = (int?)m["id"] ?? -1; Fire(OnState); return;
                case "room": Room = m["room"] as JObject; Fire(OnRoom, m); return;
                case "left": Room = null; Fire(OnRoom, m); return;
                case "err": Why = (string)m["why"] ?? "うまくいきませんでした"; Fire(OnErr, m); return;
                case "rooms": Fire(OnRooms, m["rooms"] as JArray ?? new JArray()); return;
                case "start": if (m["room"] is JObject rm) Room = rm; Fire(OnStart, m); return;
                case "from":
                    var d = m["d"] as JObject;
                    if (d != null && OnFrom != null)
                        foreach (Action<int, JObject> f in OnFrom.GetInvocationList())
                            try { f((int?)m["id"] ?? -1, d); } catch (Exception e) { Debug.LogException(e); }
                    return;
            }
        }

        static void Fire(Action a)
        {
            if (a == null) return;
            foreach (Action f in a.GetInvocationList()) try { f(); } catch (Exception e) { Debug.LogException(e); }
        }
        static void Fire<T>(Action<T> a, T v)
        {
            if (a == null) return;
            foreach (Action<T> f in a.GetInvocationList()) try { f(v); } catch (Exception e) { Debug.LogException(e); }
        }

        // ---------------------------------------------------------------- 送る
        static bool Raw(JObject o)
        {
            var c = conn;
            if (c == null || State != "on") return false;
            return c.Send(o.ToString(Newtonsoft.Json.Formatting.None));
        }

        public static bool SetName(string n) { Name = n ?? ""; return Raw(new JObject { ["t"] = "hello", ["name"] = Name }); }
        public static bool CreateRoom(string name, bool priv) { return Raw(new JObject { ["t"] = "create", ["name"] = name ?? "", ["priv"] = priv }); }
        public static bool JoinRoom(string code) { return Raw(new JObject { ["t"] = "join", ["code"] = code ?? "" }); }
        public static bool ListRooms() { return Raw(new JObject { ["t"] = "list" }); }
        public static bool LeaveRoom() { return Raw(new JObject { ["t"] = "leave" }); }
        public static bool StartRoom(JObject cfg) { return Raw(new JObject { ["t"] = "start", ["cfg"] = cfg }); }
        /// <summary>部屋の中へ届ける。id を省くと全員へ（自分には届かない）。</summary>
        public static bool Send(JObject d, int? id = null)
        {
            var o = new JObject { ["t"] = "to", ["d"] = d };
            if (id != null) o["id"] = id.Value;
            return Raw(o);
        }
        /// <summary>送った文字数（通信量を測る用）。</summary>
        public static long SentBytes { get { var c = conn; return c != null ? c.sent : 0; } }

        // ---------------------------------------------------------------- 部屋の中身を読む小道具
        public static JArray Players { get { return Room != null ? Room["players"] as JArray : null; } }
        public static int Max { get { return Room != null ? ((int?)Room["max"] ?? 4) : 4; } }

        // ================================================================ 自前の WebSocket（1本ぶん）
        /// <summary>
        /// TCP の上の WebSocket。握手・文字のフレーム（送るときは決まりどおり覆いを掛ける）・閉じる・ping への pong だけ。
        /// 受けるのは裏の糸。Close() はソケットを閉じるだけで、受けている糸はそこで抜ける。
        /// </summary>
        class Conn
        {
            public readonly string url;
            readonly string host, path; readonly int port;
            TcpClient tcp; NetworkStream ns;
            volatile bool closed, open;
            readonly object wlock = new object();
            readonly System.Random rnd = new System.Random();
            public long sent;

            public Conn(string u)
            {
                url = u;
                var uri = new Uri(u);
                host = uri.Host; port = uri.Port > 0 ? uri.Port : 80; path = uri.PathAndQuery;
            }

            public void Start()
            {
                var th = new Thread(Run) { IsBackground = true, Name = "Nekorobo.Net" };
                th.Start();
            }

            public void Close()
            {
                closed = true;
                try { if (ns != null) ns.Close(); } catch { }
                try { if (tcp != null) tcp.Close(); } catch { }
            }

            void Run()
            {
                try
                {
                    tcp = new TcpClient { NoDelay = true };
                    var ar = tcp.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(5000) || closed) { bool us = closed; Close(); if (!us) Lost(this, false, "時間切れ"); return; }
                    tcp.EndConnect(ar);
                    ns = tcp.GetStream();
                    // ---- 握手
                    var key = new byte[16]; rnd.NextBytes(key);
                    string req = "GET " + path + " HTTP/1.1\r\nHost: " + host + ":" + port + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                               + "Sec-WebSocket-Key: " + Convert.ToBase64String(key) + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
                    var rb = Encoding.ASCII.GetBytes(req);
                    ns.Write(rb, 0, rb.Length);
                    var head = new StringBuilder();
                    while (!head.ToString().EndsWith("\r\n\r\n"))
                    {
                        int b = ns.ReadByte();
                        if (b < 0) throw new Exception("握手の途中で切れました");
                        head.Append((char)b);
                        if (head.Length > 8192) throw new Exception("握手の返事が長すぎます");
                    }
                    if (!head.ToString().StartsWith("HTTP/1.1 101")) throw new Exception("WebSocket にしてもらえません");
                    open = true;
                    Opened(this);
                    // ---- 受ける
                    var acc = new System.IO.MemoryStream();
                    var h2 = new byte[8];
                    for (;;)
                    {
                        ReadN(h2, 2);
                        bool fin = (h2[0] & 0x80) != 0; int op = h2[0] & 0x0f;
                        bool masked = (h2[1] & 0x80) != 0; long len = h2[1] & 0x7f;
                        if (len == 126) { ReadN(h2, 2); len = (h2[0] << 8) | h2[1]; }
                        else if (len == 127) { ReadN(h2, 8); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | h2[i]; }
                        var mask = new byte[4];
                        if (masked) ReadN(mask, 4);
                        var body = new byte[len];
                        ReadN(body, (int)len);
                        if (masked) for (int i = 0; i < body.Length; i++) body[i] ^= mask[i & 3];
                        if (op == 0x8) break;                                  // 閉じる
                        if (op == 0x9) { Frame(0xA, body); continue; }         // ping → pong
                        if (op == 0xA) continue;
                        if (op == 0x1 || op == 0x0) acc.Write(body, 0, body.Length);
                        if (fin && (op == 0x1 || op == 0x0) && acc.Length > 0)
                        {
                            Got(this, Encoding.UTF8.GetString(acc.GetBuffer(), 0, (int)acc.Length));
                            acc.SetLength(0);
                        }
                    }
                    Close();
                    Lost(this, true, "");
                }
                catch (Exception e)
                {
                    bool was = open;
                    Close();
                    if (!closedByUs) Lost(this, was, e.Message);
                }
            }
            bool closedByUs { get { return closed && conn != this; } }

            void ReadN(byte[] b, int n)
            {
                int o = 0;
                while (o < n)
                {
                    int r = ns.Read(b, o, n - o);
                    if (r <= 0) throw new Exception("切れました");
                    o += r;
                }
            }

            /// <summary>1つ送る（文字）。主スレッドから呼ぶ。</summary>
            public bool Send(string txt)
            {
                if (!open || closed) return false;
                var b = Encoding.UTF8.GetBytes(txt);
                sent += b.Length;
                return Frame(0x1, b);
            }

            bool Frame(int op, byte[] body)
            {
                try
                {
                    int n = body.Length;
                    var f = new System.IO.MemoryStream(n + 14);
                    f.WriteByte((byte)(0x80 | op));
                    if (n < 126) f.WriteByte((byte)(0x80 | n));
                    else if (n < 65536) { f.WriteByte(0x80 | 126); f.WriteByte((byte)(n >> 8)); f.WriteByte((byte)n); }
                    else { f.WriteByte(0x80 | 127); for (int i = 7; i >= 0; i--) f.WriteByte((byte)((long)n >> (i * 8))); }
                    // 送る側は覆いを掛ける決まり（サーバは外して読む）
                    var mk = new byte[4]; lock (rnd) rnd.NextBytes(mk);
                    f.Write(mk, 0, 4);
                    for (int i = 0; i < n; i++) f.WriteByte((byte)(body[i] ^ mk[i & 3]));
                    lock (wlock) ns.Write(f.GetBuffer(), 0, (int)f.Length);
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
