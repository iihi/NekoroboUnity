using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// オンライン対戦の通信口。JS版の net.js を写したもの。部屋の出入りと、中継のやりとりだけを持つ。
    ///
    /// **ゲームの中身は持たない。**部屋の中は「ホストが全部計算して結果を配る」形で（Game.Online / Game.NetSync）、
    /// その便りもここを通す（送るのは Send、受けるのは OnFrom）。
    ///
    /// **ここは差し替え口。**中身（WebSocket・EOS…）は別のクラスにあり（INetBackend を満たす物）、
    /// ここはどれを使うかを決めて、決まった形で外へ出すだけ。ゲーム側はここしか見ないので、
    /// 中身を替えてもゲーム側は1行も変わらない。
    ///
    ///   ws … 部屋サーバー（WebSocket。HTML版と同じサーバ・同じ便りなので、HTML版と同じ部屋で遊べる）。既定
    ///   （EOS を足すときは Backends に1行足す）
    ///
    ///   どれを使うか：起動の引数 -net=名前 → この端末で覚えた値（PlayerPrefs nekorobo.net）→ ws
    ///
    /// **配るもの**（JS版の on("state") など）
    ///   OnState … つながり具合が変わった
    ///   OnRoom  … 部屋の中身が変わった（Room が null なら抜けた）
    ///   OnRooms … 部屋の一覧が届いた
    ///   OnStart … 始まった（m.room, m.cfg）
    ///   OnFrom  … 部屋の中の便り（送った人, 中身）
    ///   OnErr   … うまくいかなかった（Why に理由）
    ///
    /// 知らせは**主スレッドで**配る。中身が裏の糸で受けるときは Post で積み、Hud.Update が毎フレーム呼ぶ Pump() で配る。
    /// エディタでは、再生を止めるときとコンパイルし直す前に必ず畳む（Editor/NetReset.cs）。
    /// </summary>
    public static class Net
    {
        // ---- 状態（JS版 NET）
        public static string State = "off";          // off / connecting / on / error
        public static int Id = -1;                   // 自分の番号（仕組みがくれる）
        public static JObject Room;                  // いまの部屋 { code, name, priv, max, host, players:[{id,name,host}] }
        public static string Name = "";
        public static string Why = "";               // つながらなかった理由

        // ---- 配るもの
        public static event Action OnState;
        public static event Action<JObject> OnRoom;      // 部屋の中身が変わった（Room が null なら抜けた）
        public static event Action<JArray> OnRooms;      // 開いている部屋の一覧
        public static event Action<JObject> OnStart;     // 始まった（m.room, m.cfg）
        public static event Action<int, JObject> OnFrom; // 部屋の中の便り（送った人, 中身）
        public static event Action<JObject> OnErr;

        // ================================================================ 中身を選ぶ
        /// <summary>**差し替えるのはここだけ。**EOS を足すときは1行増やす。</summary>
        static readonly Dictionary<string, Func<INetBackend>> Backends = new Dictionary<string, Func<INetBackend>>
        {
            ["ws"] = () => new NetWs(),
        };
        const string KIND_KEY = "nekorobo.net";
        static INetBackend be;
        static string kind;

        /// <summary>いま使っている中身の名前（ws など）。</summary>
        public static string Kind { get { Be(); return kind; } }

        static INetBackend Be()
        {
            if (be != null) return be;
            string want = null;
            foreach (var a in Environment.GetCommandLineArgs())
                if (a.StartsWith("-net=")) want = a.Substring(5).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(want)) want = PlayerPrefs.GetString(KIND_KEY, "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(want) || !Backends.ContainsKey(want)) want = "ws";
            kind = want;
            be = Backends[want]();
            return be;
        }

        /// <summary>中身を替える（この端末で覚える）。畳んでから替える。</summary>
        public static void SetKind(string k)
        {
            k = (k ?? "").Trim().ToLowerInvariant();
            if (!Backends.ContainsKey(k)) return;
            Close();
            PlayerPrefs.SetString(KIND_KEY, k); PlayerPrefs.Save();
            be = null; kind = null;
        }

        // ---- どの仕組みで動いているか（画面に出す用）
        public static string Label { get { return Be().Label; } }
        public static bool NeedsAddr { get { return Be().NeedsAddr; } }

        // ---- つなぎ先（NeedsAddr が false の仕組みでは空）
        public static string Addr { get { return Be().Addr ?? ""; } }
        public static void SetAddr(string v) { Be().SetAddr(v); }
        public static bool AddrFromCfg { get { return Be().AddrFromCfg; } }
        public static string Where { get { var w = Be().Where; return string.IsNullOrEmpty(w) ? Label : w; } }

        // ================================================================ 知らせの受け渡し（主スレッドへ）
        static readonly ConcurrentQueue<Action> inbox = new ConcurrentQueue<Action>();

        /// <summary>裏の糸から主スレッドへ渡す（中身が使う）。</summary>
        public static void Post(Action a) { inbox.Enqueue(a); }

        /// <summary>届いた便りを配る。主スレッドから毎フレーム呼ぶ（Hud.Update）。</summary>
        public static void Pump()
        {
            Action a;
            int n = 0;
            while (n++ < 500 && inbox.TryDequeue(out a))
                try { a(); } catch (Exception e) { Debug.LogException(e); }
        }

        // ================================================================ 決まりぶん（中身へそのまま渡す）
        /// <summary>畳んで、受け口も全部外す（再生のし直しで二重に受けないように）。</summary>
        public static void Reset()
        {
            Close();
            OnState = null; OnRoom = null; OnRooms = null; OnStart = null; OnFrom = null; OnErr = null;
            Id = -1; Why = "";
        }

        public static void Connect(string name = null)
        {
            if (name != null) Name = name;
            if (State == "on" || State == "connecting") return;
            // **知らせる前に「つないでいる最中」にしておく。**先に知らせていたころは、知らせを受けたオンラインの画面が
            // 描き直しでまた Connect を呼び、際限なく繰り返してスタックがあふれた（エディタが Reloading Domain で固まる元になった）
            State = "connecting"; Why = "";
            Be().Connect();
            Fire(OnState);
        }

        public static void Close()
        {
            if (be != null) be.Close();
            Room = null; State = "off";
            Action a; while (inbox.TryDequeue(out a)) { }            // 畳む前に届いていた物は捨てる
        }

        public static bool SetName(string n) { Name = n ?? ""; return On && Be().SetName(Name); }
        public static bool CreateRoom(string name, bool priv) { return On && Be().CreateRoom(name ?? "", priv); }
        public static bool JoinRoom(string code) { return On && Be().JoinRoom(code ?? ""); }
        public static bool ListRooms() { return On && Be().ListRooms(); }
        public static bool LeaveRoom() { return On && Be().LeaveRoom(); }
        public static bool StartRoom(JObject cfg) { return On && Be().StartRoom(cfg); }
        /// <summary>部屋の中へ届ける。id を省くと全員へ（自分には届かない）。</summary>
        public static bool Send(JObject d, int? id = null) { return On && Be().Send(d, id); }
        /// <summary>送った量（通信量を測る用）。</summary>
        public static long SentBytes { get { return be != null ? be.SentBytes : 0; } }
        static bool On { get { return State == "on"; } }

        /// <summary>自分がホストか（＝いま計算する人）。ホストが抜けたら次の人に移る。</summary>
        public static bool IsHost { get { return Room != null && (int?)Room["host"] == Id; } }

        // ---------------------------------------------------------------- 部屋の中身を読む小道具
        public static JArray Players { get { return Room != null ? Room["players"] as JArray : null; } }
        public static int Max { get { return Room != null ? ((int?)Room["max"] ?? 4) : 4; } }

        // ================================================================ 中身から呼ぶ（主スレッドで）
        public static void ReportOpen() { State = "on"; Why = ""; Fire(OnState); }
        public static void ReportLost(string why)
        {
            Room = null; State = "error"; Why = why ?? "";
            Fire(OnState);
        }
        public static void ReportId(int id) { Id = id; Fire(OnState); }
        public static void ReportRoom(JObject room, JObject m) { Room = room; Fire(OnRoom, m); }
        public static void ReportRooms(JArray rooms) { Fire(OnRooms, rooms ?? new JArray()); }
        public static void ReportStart(JObject room, JObject m) { if (room != null) Room = room; Fire(OnStart, m); }
        public static void ReportErr(string why, JObject m) { Why = why ?? "うまくいきませんでした"; Fire(OnErr, m); }
        public static void ReportFrom(int id, JObject d)
        {
            if (d == null || OnFrom == null) return;
            foreach (Action<int, JObject> f in OnFrom.GetInvocationList())
                try { f(id, d); } catch (Exception e) { Debug.LogException(e); }
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
    }
}
