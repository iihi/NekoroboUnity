using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 通信口の中身（その1）：**WebSocket の部屋サーバー**。JS版の net-ws.js を写したもの。
    ///
    /// **部屋サーバーは HTML版と同じもの**（node server.js の /ws、または node rooms-server.js）。
    /// やりとりも HTML版と同じ JSON なので、同じ部屋に HTML版と Unity版が入れる。
    /// サーバがするのは待ち合わせと中継だけ。
    ///
    ///   つなぐ先の優先順：手で入れた値（この端末で覚える）→ HTML版の net.json の ws → localhost:8123
    ///
    /// **WebSocket は自前**（TCP の上に、握手と文字のフレームだけ）。部屋サーバー（rooms.js）も自前なので、それと同じ範囲。
    /// 受けるのは裏の糸1本。届いた物は Net.Post で主スレッドへ渡す。送るのは主スレッドから（便りは小さいので、そのまま書く）。
    /// 受けている糸は、ソケットを閉じれば必ず抜ける（再生を止めたとき・コンパイルし直す前に閉じる）。
    /// </summary>
    public class NetWs : INetBackend
    {
        public string Label { get { return "部屋サーバー（WebSocket）"; } }
        public bool NeedsAddr { get { return true; } }

        const string WS_KEY = "nekorobo.ws";
        Conn conn;
        string cfgWs;                          // HTML版の net.json の ws（読んだら控える）
        bool cfgRead;

        // ---------------------------------------------------------------- つなぎ先
        /// <summary>手で入れたつなぎ先（ホスト名:ポート）。空なら net.json か localhost:8123。</summary>
        public string Addr { get { return PlayerPrefs.GetString(WS_KEY, ""); } }
        public void SetAddr(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length > 0) PlayerPrefs.SetString(WS_KEY, v); else PlayerPrefs.DeleteKey(WS_KEY);
            PlayerPrefs.Save();
        }
        string CfgWs()
        {
            if (!cfgRead)
            {
                cfgRead = true;
                var j = DataRoot.ReadJson("net.json");
                cfgWs = j != null ? (string)j["ws"] : null;
            }
            return cfgWs ?? "";
        }
        public bool AddrFromCfg { get { return Addr.Length == 0 && CfgWs().Trim().Length > 0; } }
        /// <summary>つなぐ URL（画面にも出す）。</summary>
        public string Where
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

        // ---------------------------------------------------------------- つなぐ・畳む
        public void Connect()
        {
            Close();
            var c = new Conn(this, Where);
            conn = c;
            c.Start();
        }

        public void Close()
        {
            var c = conn;
            conn = null;
            if (c != null) c.Close();
        }

        // 裏の糸から呼ばれる（主スレッドの物には触らず、Net.Post で渡すだけ）
        void Opened(Conn c)
        {
            Net.Post(() =>
            {
                if (conn != c) return;
                Raw(new JObject { ["t"] = "hello", ["name"] = Net.Name });     // 名乗ってから知らせる（知らせを受けて部屋を作ることがある）
                Net.ReportOpen();
            });
        }
        void Got(Conn c, string txt) { Net.Post(() => { if (conn == c) Handle(txt); }); }
        void Lost(Conn c, bool wasOpen, string msg)
        {
            Net.Post(() =>
            {
                if (conn != c) return;                                // 先に畳んだ／つなぎ直した
                conn = null;
                if (!wasOpen) Debug.Log("[Net] つながらない: " + c.url + " " + msg);
                Net.ReportLost(wasOpen ? "接続が切れました" : "サーバにつながりません");
            });
        }

        void Handle(string txt)
        {
            JObject m;
            try { m = JObject.Parse(txt); } catch { return; }
            switch ((string)m["t"])
            {
                case "hi": Net.ReportId((int?)m["id"] ?? -1); return;
                case "room": Net.ReportRoom(m["room"] as JObject, m); return;
                case "left": Net.ReportRoom(null, m); return;
                case "err": Net.ReportErr((string)m["why"], m); return;
                case "rooms": Net.ReportRooms(m["rooms"] as JArray); return;
                case "start": Net.ReportStart(m["room"] as JObject, m); return;
                case "from": Net.ReportFrom((int?)m["id"] ?? -1, m["d"] as JObject); return;
            }
        }

        // ---------------------------------------------------------------- 送る
        bool Raw(JObject o)
        {
            var c = conn;
            if (c == null) return false;
            return c.Send(o.ToString(Newtonsoft.Json.Formatting.None));
        }

        public bool SetName(string n) { return Raw(new JObject { ["t"] = "hello", ["name"] = n ?? "" }); }
        public bool CreateRoom(string name, bool priv) { return Raw(new JObject { ["t"] = "create", ["name"] = name ?? "", ["priv"] = priv }); }
        public bool JoinRoom(string code) { return Raw(new JObject { ["t"] = "join", ["code"] = code ?? "" }); }
        public bool ListRooms() { return Raw(new JObject { ["t"] = "list" }); }
        public bool LeaveRoom() { return Raw(new JObject { ["t"] = "leave" }); }
        public bool StartRoom(JObject cfg) { return Raw(new JObject { ["t"] = "start", ["cfg"] = cfg }); }
        public bool Send(JObject d, int? id)
        {
            var o = new JObject { ["t"] = "to", ["d"] = d };
            if (id != null) o["id"] = id.Value;
            return Raw(o);
        }
        public long SentBytes { get { var c = conn; return c != null ? c.sent : 0; } }

        // ================================================================ 自前の WebSocket（1本ぶん）
        /// <summary>
        /// TCP の上の WebSocket。握手・文字のフレーム（送るときは決まりどおり覆いを掛ける）・閉じる・ping への pong だけ。
        /// 受けるのは裏の糸。Close() はソケットを閉じるだけで、受けている糸はそこで抜ける。
        /// </summary>
        class Conn
        {
            public readonly string url;
            readonly NetWs owner;
            readonly string host, path; readonly int port;
            TcpClient tcp; NetworkStream ns;
            volatile bool closed, open;
            readonly object wlock = new object();
            readonly System.Random rnd = new System.Random();
            public long sent;

            public Conn(NetWs o, string u)
            {
                owner = o; url = u;
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
                    if (!ar.AsyncWaitHandle.WaitOne(5000) || closed) { bool us = closed; Close(); if (!us) owner.Lost(this, false, "時間切れ"); return; }
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
                    owner.Opened(this);
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
                            owner.Got(this, Encoding.UTF8.GetString(acc.GetBuffer(), 0, (int)acc.Length));
                            acc.SetLength(0);
                        }
                    }
                    Close();
                    owner.Lost(this, true, "");
                }
                catch (Exception e)
                {
                    bool was = open;
                    Close();
                    if (!closedByUs) owner.Lost(this, was, e.Message);
                }
            }
            bool closedByUs { get { return closed && owner.conn != this; } }

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
