// EOS の SDK を入れたときだけ（Tools/eos_setup.ps1 が Assets/csc.rsp で NEKOROBO_EOS を立てる）
#if NEKOROBO_EOS
using System;
using System.Collections.Generic;
using System.Text;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 通信口の中身：**EOS（Epic Online Services）**。部屋は EOS の Lobby、部屋の中の便りは EOS の P2P。
    /// 自前の部屋サーバーは要らない（NAT を越えられないときは Epic の中継を通る）。立ち上げと後片付けは EosHost。
    ///
    /// 部屋サーバー（rooms.js）と同じ決まりにそろえてある：
    ///   名乗り   … Connect の端末ID（Epic アカウントは要らない）。番号（Net.Id）は EOS の長い ID から作る8桁の数（どの端末でも同じ数になる）
    ///   部屋     … Lobby（4人まで）。属性 code（12桁のルームID）・name・priv・playing・game を持たせる
    ///   一覧     … game が一致・priv でない・playing でない部屋を探す（40件まで）
    ///   番号で入る… code が一致する部屋を探して入る。始まっている／いっぱいなら断る
    ///   ホスト   … Lobby の持ち主。抜けたら EOS が次の人へ移す
    ///   始める   … 持ち主が playing を立て、{t:"start", room, cfg} を全員へ P2P で送る（自分にも知らせる）。
    ///              room は**ホストが見ている並び**を入れる（全員で同じ並びにしないと、誰がどのロボか食い違う）
    ///   便り     … {t:"to", d} を P2P で送る。受けたら Net.ReportFrom
    ///
    /// **P2P は1回に 1170 バイトまで**なので、長い便りは切って送る（頭の1バイトが 1 なら続きあり、0 なら最後）。
    /// 確実・順番どおり（ReliableOrdered）で送るので、切った物は届いた順につなげばよい。
    /// HTML版はこの仕組みを使えない（ブラウザから P2P は使えない）ので、EOS の部屋は Unity版どうしだけ。
    /// </summary>
    public class NetEos : INetBackend
    {
        public string Label { get { return "EOS（Epic Online Services）"; } }
        public bool NeedsAddr { get { return false; } }
        public string Addr { get { return ""; } }
        public void SetAddr(string v) { }
        public bool AddrFromCfg { get { return false; } }
        public string Where { get { return "Epic のサーバー"; } }
        public long SentBytes { get { return sent; } }

        const uint MAX = 4;
        const string BUCKET = "nekorobo:1";
        const string GAME = "nekorobo";
        static readonly SocketId SOCK = new SocketId { SocketName = "nekorobo" };

        int gen;                     // 畳むたびに増やす。古い知らせを捨てる用
        ProductUserId me;
        string lobby;                // いま居る部屋（LobbyId）
        string lastRoom;             // 前に知らせた部屋の中身（同じなら知らせない）
        long sent;
        readonly Dictionary<int, ProductUserId> users = new Dictionary<int, ProductUserId>();
        readonly Dictionary<string, List<byte>> partial = new Dictionary<string, List<byte>>();
        readonly byte[] rbuf = new byte[P2PInterface.MAX_PACKET_SIZE];
        ulong nAuth, nStatus, nMemUpd, nLobUpd, nReq;

        static LobbyInterface Lb { get { return EosHost.P.GetLobbyInterface(); } }
        static P2PInterface P2p { get { return EosHost.P.GetP2PInterface(); } }
        static ConnectInterface Co { get { return EosHost.P.GetConnectInterface(); } }

        // ================================================================ つなぐ・畳む
        public void Connect()
        {
            int g = ++gen;
            if (!EosHost.Init()) { string w = EosHost.Why; Do(g, () => Net.ReportLost(w)); return; }
            // 同じ PC で2人を試すとき、2つ目は -eosnewid で起こす（端末IDを作り直して別人になる。1回の起動で1度だけ）
            if (!newIdDone && Array.IndexOf(Environment.GetCommandLineArgs(), "-eosnewid") >= 0)
            {
                newIdDone = true;
                var del = new DeleteDeviceIdOptions();
                Co.DeleteDeviceId(ref del, null, (ref DeleteDeviceIdCallbackInfo d) => { if (g == gen) MakeDeviceId(g); });
                return;
            }
            MakeDeviceId(g);
        }

        static bool newIdDone;

        void MakeDeviceId(int g)
        {
            var o = new CreateDeviceIdOptions { DeviceModel = Cut(SystemInfo.deviceModel, 60, "PC") };
            Co.CreateDeviceId(ref o, null, (ref CreateDeviceIdCallbackInfo d) =>
            {
                if (g != gen) return;
                // 2回目からは「もうある」と返る。それでよい
                if (d.ResultCode != Result.Success && d.ResultCode != Result.DuplicateNotAllowed)
                { Lost(g, "端末IDを作れません（" + d.ResultCode + "）"); return; }
                Login(g);
            });
        }

        void Login(int g)
        {
            var o = new LoginOptions
            {
                Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
                UserLoginInfo = new UserLoginInfo { DisplayName = Cut(Net.Name, 32, "Player") },
            };
            Co.Login(ref o, null, (ref LoginCallbackInfo d) =>
            {
                if (g != gen) return;
                if (d.ResultCode == Result.Success) { LoggedIn(g, d.LocalUserId); return; }
                if (d.ResultCode == Result.InvalidUser)
                {
                    // 初めての端末。EOS に登録してから使う
                    var c = new CreateUserOptions { ContinuanceToken = d.ContinuanceToken };
                    Co.CreateUser(ref c, null, (ref CreateUserCallbackInfo u) =>
                    {
                        if (g != gen) return;
                        if (u.ResultCode == Result.Success) LoggedIn(g, u.LocalUserId);
                        else Lost(g, "EOS に登録できません（" + u.ResultCode + "）");
                    });
                    return;
                }
                Lost(g, "EOS にログインできません（" + d.ResultCode + "）");
            });
        }

        void LoggedIn(int g, ProductUserId id)
        {
            if (me != null) { me = id; return; }                 // 期限切れで入り直したとき
            me = id;
            Debug.Log("[EOS] ログインしました: " + id + "（番号 " + IdOf(id) + "）");

            var a = new AddNotifyLobbyMemberStatusReceivedOptions();
            nStatus = Lb.AddNotifyLobbyMemberStatusReceived(ref a, null, (ref LobbyMemberStatusReceivedCallbackInfo d) =>
                OnStatus(g, d.LobbyId, d.TargetUserId, d.CurrentStatus));
            var b = new AddNotifyLobbyMemberUpdateReceivedOptions();
            nMemUpd = Lb.AddNotifyLobbyMemberUpdateReceived(ref b, null, (ref LobbyMemberUpdateReceivedCallbackInfo d) =>
            { if (g == gen && lobby != null && (string)d.LobbyId == lobby) RoomChanged(null); });
            var c = new AddNotifyLobbyUpdateReceivedOptions();
            nLobUpd = Lb.AddNotifyLobbyUpdateReceived(ref c, null, (ref LobbyUpdateReceivedCallbackInfo d) =>
            { if (g == gen && lobby != null && (string)d.LobbyId == lobby) RoomChanged(null); });
            var r = new AddNotifyPeerConnectionRequestOptions { LocalUserId = me, SocketId = SOCK };
            nReq = P2p.AddNotifyPeerConnectionRequest(ref r, null, (ref OnIncomingConnectionRequestInfo d) =>
            { if (g == gen) Accept(d.RemoteUserId); });
            var e = new AddNotifyAuthExpirationOptions();
            nAuth = Co.AddNotifyAuthExpiration(ref e, null, (ref AuthExpirationCallbackInfo d) => { if (g == gen) Login(g); });

            Do(g, () => { Net.ReportOpen(); Net.ReportId(IdOf(me)); });
        }

        public void Close()
        {
            gen++;
            if (EosHost.P != null)
            {
                LeaveQuiet();
                if (nStatus != 0) Lb.RemoveNotifyLobbyMemberStatusReceived(nStatus);
                if (nMemUpd != 0) Lb.RemoveNotifyLobbyMemberUpdateReceived(nMemUpd);
                if (nLobUpd != 0) Lb.RemoveNotifyLobbyUpdateReceived(nLobUpd);
                if (nReq != 0) P2p.RemoveNotifyPeerConnectionRequest(nReq);
                if (nAuth != 0) Co.RemoveNotifyAuthExpiration(nAuth);
            }
            nStatus = nMemUpd = nLobUpd = nReq = nAuth = 0;
            me = null; lobby = null; lastRoom = null;
            users.Clear(); partial.Clear();
        }

        void Lost(int g, string why)
        {
            Debug.LogWarning("[EOS] " + why);
            Do(g, () => Net.ReportLost(why));
        }

        public void Tick()
        {
            EosHost.Tick();
            if (me == null || EosHost.P == null) return;
            var p2p = P2p;
            var go = new GetNextReceivedPacketSizeOptions { LocalUserId = me };
            for (int n = 0; n < 500; n++)
            {
                uint size;
                if (p2p.GetNextReceivedPacketSize(ref go, out size) != Result.Success) break;
                var ro = new ReceivePacketOptions { LocalUserId = me, MaxDataSizeBytes = (uint)rbuf.Length };
                ProductUserId peer = null;
                SocketId sock = default(SocketId);
                byte ch;
                uint got;
                if (p2p.ReceivePacket(ref ro, ref peer, ref sock, out ch, new ArraySegment<byte>(rbuf), out got) != Result.Success) break;
                Got(peer, (int)got);
            }
        }

        // ================================================================ 部屋
        public bool SetName(string name)
        {
            if (lobby != null) Modify(gen, h => MAttr(h, "name", Cut(name, 16, "")), null);
            return true;
        }

        public bool CreateRoom(string name, bool priv)
        {
            if (me == null) return false;
            int g = gen;
            LeaveQuiet();
            string code = NewCode(), nm = Cut(name, 24, "へや");
            var o = new CreateLobbyOptions
            {
                LocalUserId = me, MaxLobbyMembers = MAX, PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                PresenceEnabled = false, AllowInvites = false, BucketId = BUCKET, DisableHostMigration = false,
                EnableRTCRoom = false, EnableJoinById = false,
            };
            Lb.CreateLobby(ref o, null, (ref CreateLobbyCallbackInfo d) =>
            {
                if (g != gen) return;
                if (d.ResultCode != Result.Success) { Err(g, "部屋を作れません（" + d.ResultCode + "）"); return; }
                lobby = d.LobbyId;
                Modify(g, h =>
                {
                    Attr(h, "game", new AttributeDataValue { AsUtf8 = GAME });
                    Attr(h, "code", new AttributeDataValue { AsUtf8 = code });
                    Attr(h, "name", new AttributeDataValue { AsUtf8 = nm });
                    Attr(h, "priv", new AttributeDataValue { AsBool = priv });
                    Attr(h, "playing", new AttributeDataValue { AsBool = false });
                    MAttr(h, "name", Cut(Net.Name, 16, ""));
                }, rc =>
                {
                    if (rc != Result.Success) { Err(g, "部屋を作れません（" + rc + "）"); LeaveQuiet(); return; }
                    RoomChanged(new JObject { ["you"] = IdOf(me) });
                });
            });
            return true;
        }

        public bool JoinRoom(string code)
        {
            if (me == null) return false;
            int g = gen;
            var sb = new StringBuilder();
            foreach (var ch in code ?? "") if (ch >= '0' && ch <= '9') sb.Append(ch);
            string c = sb.ToString();
            if (c.Length == 0) { Err(g, "その番号の部屋がありません"); return true; }
            Search(g, 1, s => { Param(s, "game", new AttributeDataValue { AsUtf8 = GAME }); Param(s, "code", new AttributeDataValue { AsUtf8 = c }); }, (n, s) =>
            {
                if (n == 0) { Err(g, "その番号の部屋がありません"); return; }
                LobbyDetails det;
                var co = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
                if (s.CopySearchResultByIndex(ref co, out det) != Result.Success) { Err(g, "その番号の部屋がありません"); return; }
                if (ABool(det, "playing")) { det.Release(); Err(g, "その部屋はもう始まっています"); return; }
                var io = new LobbyDetailsCopyInfoOptions();
                LobbyDetailsInfo? info;
                if (det.CopyInfo(ref io, out info) == Result.Success && info.HasValue && info.Value.AvailableSlots == 0)
                { det.Release(); Err(g, "その部屋はいっぱいです"); return; }
                LeaveQuiet();
                var jo = new JoinLobbyOptions { LobbyDetailsHandle = det, LocalUserId = me, PresenceEnabled = false };
                Lb.JoinLobby(ref jo, null, (ref JoinLobbyCallbackInfo d) =>
                {
                    det.Release();
                    if (g != gen) return;
                    if (d.ResultCode == Result.LobbyTooManyPlayers) { Err(g, "その部屋はいっぱいです"); return; }
                    if (d.ResultCode == Result.NotFound) { Err(g, "その番号の部屋がありません"); return; }
                    if (d.ResultCode != Result.Success) { Err(g, "部屋に入れません（" + d.ResultCode + "）"); return; }
                    lobby = d.LobbyId;
                    AcceptAll();
                    Modify(g, h => MAttr(h, "name", Cut(Net.Name, 16, "")), null);
                    RoomChanged(new JObject { ["you"] = IdOf(me) });
                });
            });
            return true;
        }

        public bool ListRooms()
        {
            if (me == null) return false;
            int g = gen;
            Search(g, 40, s =>
            {
                Param(s, "game", new AttributeDataValue { AsUtf8 = GAME });
                Param(s, "priv", new AttributeDataValue { AsBool = false });
                Param(s, "playing", new AttributeDataValue { AsBool = false });
            }, (n, s) =>
            {
                var list = new JArray();
                for (uint i = 0; i < n; i++)
                {
                    LobbyDetails det;
                    var co = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    if (s.CopySearchResultByIndex(ref co, out det) != Result.Success) continue;
                    var io = new LobbyDetailsCopyInfoOptions();
                    LobbyDetailsInfo? info;
                    if (det.CopyInfo(ref io, out info) == Result.Success && info.HasValue && info.Value.AvailableSlots > 0)
                    {
                        uint max = info.Value.MaxMembers;
                        list.Add(new JObject
                        {
                            ["code"] = AStr(det, "code"), ["name"] = AStr(det, "name"),
                            ["n"] = (int)(max - info.Value.AvailableSlots), ["max"] = (int)max,
                        });
                    }
                    det.Release();
                }
                Do(g, () => Net.ReportRooms(list));
            });
            return true;
        }

        public bool LeaveRoom()
        {
            LeaveQuiet();
            Do(gen, () => Net.ReportRoom(null, new JObject { ["t"] = "left" }));
            return true;
        }

        public bool StartRoom(JObject cfg)
        {
            if (lobby == null || me == null) return false;
            var room = BuildRoom();
            if (room == null || (int?)room["host"] != IdOf(me)) return false;
            int g = gen;
            Modify(g, h => Attr(h, "playing", new AttributeDataValue { AsBool = true }), null);
            var m = new JObject { ["t"] = "start", ["room"] = room, ["cfg"] = cfg };
            SendAll(m);
            lastRoom = room.ToString(Formatting.None);
            Do(g, () => Net.ReportStart(room, m));
            return true;
        }

        public bool Send(JObject d, int? id)
        {
            if (lobby == null || me == null) return false;
            var m = new JObject { ["t"] = "to", ["d"] = d };
            if (id == null) return SendAll(m);
            ProductUserId to;
            return users.TryGetValue(id.Value, out to) && SendTo(Bytes(m), to);
        }

        // ---------------------------------------------------------------- 部屋の出入りの知らせ
        void OnStatus(int g, string lid, ProductUserId who, LobbyMemberStatus st)
        {
            if (g != gen || lobby == null || lid != lobby || who == null) return;
            bool self = me != null && Same(who, me);
            int id = IdOf(who);
            switch (st)
            {
                case LobbyMemberStatus.Joined:
                    Accept(who);
                    RoomChanged(new JObject { ["joined"] = id });
                    break;
                case LobbyMemberStatus.Left:
                case LobbyMemberStatus.Disconnected:
                case LobbyMemberStatus.Kicked:
                    if (self) { Gone(g); break; }
                    CloseConn(who);
                    RoomChanged(new JObject { ["left"] = id });
                    break;
                case LobbyMemberStatus.Promoted:
                    RoomChanged(new JObject());
                    break;
                case LobbyMemberStatus.Closed:
                    Gone(g);
                    break;
            }
        }

        /// <summary>部屋が無くなった・追い出された。</summary>
        void Gone(int g)
        {
            CloseAll();
            lobby = null; lastRoom = null;
            Do(g, () => Net.ReportRoom(null, new JObject { ["t"] = "left" }));
        }

        /// <summary>部屋の中身を作り直して知らせる。extra が null なら、中身が前と同じときは知らせない。</summary>
        void RoomChanged(JObject extra)
        {
            var r = BuildRoom();
            if (r == null) return;
            string s = r.ToString(Formatting.None);
            if (extra == null && s == lastRoom) return;
            lastRoom = s;
            var m = new JObject { ["t"] = "room", ["room"] = r };
            if (extra != null) foreach (var p in extra) m[p.Key] = p.Value;
            Do(gen, () => Net.ReportRoom(r, m));
        }

        /// <summary>部屋の中身（部屋サーバーと同じ形）：{ code, name, priv, max, host, players:[{ id, name, host }] }</summary>
        JObject BuildRoom()
        {
            if (lobby == null || me == null || EosHost.P == null) return null;
            LobbyDetails det;
            var co = new CopyLobbyDetailsHandleOptions { LobbyId = lobby, LocalUserId = me };
            if (Lb.CopyLobbyDetailsHandle(ref co, out det) != Result.Success) return null;
            try
            {
                var io = new LobbyDetailsCopyInfoOptions();
                LobbyDetailsInfo? info;
                uint max = det.CopyInfo(ref io, out info) == Result.Success && info.HasValue ? info.Value.MaxMembers : MAX;
                var oo = new LobbyDetailsGetLobbyOwnerOptions();
                var owner = det.GetLobbyOwner(ref oo);
                var mc = new LobbyDetailsGetMemberCountOptions();
                uint cnt = det.GetMemberCount(ref mc);
                var players = new JArray();
                for (uint i = 0; i < cnt; i++)
                {
                    var mo = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                    var u = det.GetMemberByIndex(ref mo);
                    if (u == null) continue;
                    int id = IdOf(u);
                    string nm = MStr(det, u, "name");
                    players.Add(new JObject { ["id"] = id, ["name"] = string.IsNullOrEmpty(nm) ? "P" + id : nm, ["host"] = owner != null && Same(u, owner) });
                }
                return new JObject
                {
                    ["code"] = AStr(det, "code"), ["name"] = AStr(det, "name"), ["priv"] = ABool(det, "priv"),
                    ["max"] = (int)max, ["host"] = owner != null ? (JToken)IdOf(owner) : JValue.CreateNull(), ["players"] = players,
                };
            }
            finally { det.Release(); }
        }

        void LeaveQuiet()
        {
            if (lobby == null || me == null || EosHost.P == null) { lobby = null; return; }
            var o = new LeaveLobbyOptions { LocalUserId = me, LobbyId = lobby };
            Lb.LeaveLobby(ref o, null, (ref LeaveLobbyCallbackInfo d) => { });
            CloseAll();
            lobby = null; lastRoom = null;
        }

        // ---------------------------------------------------------------- Lobby の小道具
        void Modify(int g, Action<LobbyModification> fill, Action<Result> done)
        {
            if (lobby == null || me == null) return;
            LobbyModification h;
            var mo = new UpdateLobbyModificationOptions { LocalUserId = me, LobbyId = lobby };
            var r = Lb.UpdateLobbyModification(ref mo, out h);
            if (r != Result.Success) { if (done != null) done(r); return; }
            fill(h);
            var uo = new UpdateLobbyOptions { LobbyModificationHandle = h };
            Lb.UpdateLobby(ref uo, null, (ref UpdateLobbyCallbackInfo d) =>
            {
                var rc = d.ResultCode;
                if (g == gen && done != null) done(rc);
            });
            h.Release();
        }

        static void Attr(LobbyModification h, string key, AttributeDataValue v)
        {
            var o = new LobbyModificationAddAttributeOptions { Attribute = new AttributeData { Key = key, Value = v }, Visibility = LobbyAttributeVisibility.Public };
            h.AddAttribute(ref o);
        }

        static void MAttr(LobbyModification h, string key, string v)
        {
            var o = new LobbyModificationAddMemberAttributeOptions
            { Attribute = new AttributeData { Key = key, Value = new AttributeDataValue { AsUtf8 = v ?? "" } }, Visibility = LobbyAttributeVisibility.Public };
            h.AddMemberAttribute(ref o);
        }

        static void Param(LobbySearch s, string key, AttributeDataValue v)
        {
            var o = new LobbySearchSetParameterOptions { Parameter = new AttributeData { Key = key, Value = v }, ComparisonOp = ComparisonOp.Equal };
            s.SetParameter(ref o);
        }

        /// <summary>部屋を探す。見つからないときも done(0) を呼ぶ。</summary>
        void Search(int g, uint max, Action<LobbySearch> setup, Action<uint, LobbySearch> done)
        {
            LobbySearch s;
            var so = new CreateLobbySearchOptions { MaxResults = max };
            if (Lb.CreateLobbySearch(ref so, out s) != Result.Success) { Err(g, "部屋を探せません"); return; }
            setup(s);
            var fo = new LobbySearchFindOptions { LocalUserId = me };
            s.Find(ref fo, null, (ref LobbySearchFindCallbackInfo d) =>
            {
                var rc = d.ResultCode;
                try
                {
                    if (g != gen) return;
                    if (rc == Result.NotFound) { done(0, s); return; }
                    if (rc != Result.Success) { Err(g, "部屋を探せません（" + rc + "）"); return; }
                    var co = new LobbySearchGetSearchResultCountOptions();
                    done(s.GetSearchResultCount(ref co), s);
                }
                finally { s.Release(); }
            });
        }

        static string AStr(LobbyDetails det, string key)
        {
            var o = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
            Epic.OnlineServices.Lobby.Attribute? a;
            if (det.CopyAttributeByKey(ref o, out a) != Result.Success || !a.HasValue || !a.Value.Data.HasValue) return "";
            string s = a.Value.Data.Value.Value.AsUtf8;
            return s ?? "";
        }

        static bool ABool(LobbyDetails det, string key)
        {
            var o = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
            Epic.OnlineServices.Lobby.Attribute? a;
            if (det.CopyAttributeByKey(ref o, out a) != Result.Success || !a.HasValue || !a.Value.Data.HasValue) return false;
            return a.Value.Data.Value.Value.AsBool == true;
        }

        static string MStr(LobbyDetails det, ProductUserId u, string key)
        {
            var o = new LobbyDetailsCopyMemberAttributeByKeyOptions { TargetUserId = u, AttrKey = key };
            Epic.OnlineServices.Lobby.Attribute? a;
            if (det.CopyMemberAttributeByKey(ref o, out a) != Result.Success || !a.HasValue || !a.Value.Data.HasValue) return "";
            string s = a.Value.Data.Value.Value.AsUtf8;
            return s ?? "";
        }

        // ---------------------------------------------------------------- P2P
        void Accept(ProductUserId u)
        {
            if (me == null || u == null || Same(u, me)) return;
            IdOf(u);
            var o = new AcceptConnectionOptions { LocalUserId = me, RemoteUserId = u, SocketId = SOCK };
            P2p.AcceptConnection(ref o);
        }

        /// <summary>部屋の全員と、つながりを受ける用意をしておく（入った直後）。</summary>
        void AcceptAll() { foreach (var u in Members()) Accept(u); }

        void CloseConn(ProductUserId u)
        {
            if (me == null || EosHost.P == null) return;
            var o = new CloseConnectionOptions { LocalUserId = me, RemoteUserId = u, SocketId = SOCK };
            P2p.CloseConnection(ref o);
            partial.Remove(u.ToString());
        }

        void CloseAll()
        {
            if (me == null || EosHost.P == null) return;
            var o = new CloseConnectionsOptions { LocalUserId = me, SocketId = SOCK };
            P2p.CloseConnections(ref o);
            partial.Clear();
        }

        List<ProductUserId> Members()
        {
            var list = new List<ProductUserId>();
            if (lobby == null || me == null) return list;
            LobbyDetails det;
            var co = new CopyLobbyDetailsHandleOptions { LobbyId = lobby, LocalUserId = me };
            if (Lb.CopyLobbyDetailsHandle(ref co, out det) != Result.Success) return list;
            var mc = new LobbyDetailsGetMemberCountOptions();
            uint cnt = det.GetMemberCount(ref mc);
            for (uint i = 0; i < cnt; i++)
            {
                var mo = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                var u = det.GetMemberByIndex(ref mo);
                if (u != null && !Same(u, me)) list.Add(u);
            }
            det.Release();
            return list;
        }

        static byte[] Bytes(JObject m) { return Encoding.UTF8.GetBytes(m.ToString(Formatting.None)); }

        bool SendAll(JObject m)
        {
            var b = Bytes(m);
            bool ok = true;
            foreach (var u in Members()) ok &= SendTo(b, u);
            return ok;
        }

        /// <summary>切って送る。頭の1バイトが 1 なら続きあり、0 なら最後。</summary>
        bool SendTo(byte[] b, ProductUserId to)
        {
            const int CH = P2PInterface.MAX_PACKET_SIZE - 1;
            var p2p = P2p;
            int off = 0;
            do
            {
                int len = Math.Min(CH, b.Length - off);
                var pkt = new byte[len + 1];
                pkt[0] = (byte)(off + len < b.Length ? 1 : 0);
                Buffer.BlockCopy(b, off, pkt, 1, len);
                var o = new SendPacketOptions
                {
                    LocalUserId = me, RemoteUserId = to, SocketId = SOCK, Channel = 0, Data = new ArraySegment<byte>(pkt),
                    AllowDelayedDelivery = true, Reliability = PacketReliability.ReliableOrdered, DisableAutoAcceptConnection = false,
                };
                var r = p2p.SendPacket(ref o);
                if (r != Result.Success) { Debug.LogWarning("[EOS] 送れません: " + r); return false; }
                sent += pkt.Length;
                off += len;
            } while (off < b.Length);
            return true;
        }

        void Got(ProductUserId peer, int n)
        {
            if (peer == null || n < 1 || lobby == null) return;
            string key = peer.ToString();
            List<byte> buf;
            if (!partial.TryGetValue(key, out buf)) partial[key] = buf = new List<byte>();
            for (int i = 1; i < n; i++) buf.Add(rbuf[i]);
            if (rbuf[0] != 0) return;                              // 続きを待つ
            partial.Remove(key);
            JObject m;
            try { m = JObject.Parse(Encoding.UTF8.GetString(buf.ToArray())); }
            catch (Exception e) { Debug.LogWarning("[EOS] 便りが読めません: " + e.Message); return; }
            int id = IdOf(peer);
            int g = gen;
            switch ((string)m["t"])
            {
                case "to":
                    var d = m["d"] as JObject;
                    Do(g, () => Net.ReportFrom(id, d));
                    break;
                case "start":
                    var room = m["room"] as JObject;
                    if (room != null) lastRoom = room.ToString(Formatting.None);
                    Do(g, () => Net.ReportStart(room, m));
                    break;
            }
        }

        // ---------------------------------------------------------------- 小道具
        /// <summary>知らせは主スレッドで（Net.Pump が配る）。畳んだあとの物は捨てる。</summary>
        void Do(int g, Action a) { Net.Post(() => { if (g == gen) a(); }); }

        void Err(int g, string why) { Do(g, () => Net.ReportErr(why, new JObject { ["t"] = "err", ["why"] = why })); }

        /// <summary>EOS の長い ID から8桁の番号を作る（どの端末でも同じ数になる）。</summary>
        int IdOf(ProductUserId u)
        {
            string s = u.ToString();
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            int id = 10000000 + (int)(h % 90000000u);
            users[id] = u;
            return id;
        }

        static bool Same(ProductUserId a, ProductUserId b) { return a.ToString() == b.ToString(); }

        static string NewCode()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 12; i++) sb.Append((char)('0' + UnityEngine.Random.Range(0, 10)));
            return sb.ToString();
        }

        static string Cut(string s, int n, string dflt)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) s = dflt;
            return s.Length > n ? s.Substring(0, n) : s;
        }
    }
}
#endif
