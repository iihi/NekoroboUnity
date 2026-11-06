// EOS の SDK を入れたときだけ中身がある（Tools/eos_setup.ps1 が Assets/csc.rsp で NEKOROBO_EOS を立てる）
#if NEKOROBO_EOS
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Achievements;
using Epic.OnlineServices.Connect;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 実績を EOS（Epic Online Services）の実績にも送る。ゲームの側は Game.AchGive が EosAch.Unlock(id) を呼ぶだけ。
    ///
    ///   名乗り … Connect の端末ID（Epic アカウントは要らない。NetEos と同じ）。スイッチなどに出すときは、
    ///            ここを各機種のログイン（Connect の外部の資格情報）に替えれば、送り方はそのまま使える。
    ///   ID     … Game.ACHS の id をそのまま使う（Dev Portal に同じ ID で登録する。docs は README）。
    ///   送る   … 1つずつ UnlockAchievements。失敗したら 30 秒おきに送り直す（つながっていない間も貯めておく）。
    ///   合わせる … ログインしたら EOS 側の取った実績を読み、端末だけで取っていた物を送り、EOS だけで取っていた物を端末にも入れる
    ///             （別の PC で取った物も一覧に出るように。知らせは出さない）。
    /// 鍵（eos.json）が無ければ何もしない（この端末の記録だけ）。
    /// エディタで再生を止めると EosHost が畳まれて DLL が外れるので、そのときはこちらも最初からやり直す（呼ぶと落ちるため）。
    /// </summary>
    public static class EosAch
    {
        static int state;                    // 0 まだ / 1 ログイン中 / 2 ログイン済み / -1 使えない（鍵が無いなど） / -2 しばらく待ってやり直す
        static int gen;
        static ProductUserId me;
        static readonly List<string> queue = new List<string>();
        static bool sending, blocked;
        static float retryAt;
        static ulong nAuth;
        public static string Status = "";

        static ConnectInterface Co { get { return EosHost.P.GetConnectInterface(); } }
        static AchievementsInterface Ac { get { return EosHost.P.GetAchievementsInterface(); } }

        static bool quitting;
        /// <summary>再生（起動）のはじめに持ち物を空にする。終わるときは EOS を立ち上げ直さない（畳んだあとに呼ぶと落ちるため）</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            state = 0; gen++; me = null; sending = false; blocked = false; quitting = false; queue.Clear(); retryAt = 0; Status = "";
            Application.quitting -= OnQuitting; Application.quitting += OnQuitting;
        }
        static void OnQuitting() { quitting = true; gen++; }
        /// <summary>止める（エディタで再生を止める・コンパイルし直す前。EosHost を畳む前に呼ぶ）</summary>
        public static void Stop() { OnQuitting(); }

        /// <summary>取った実績を送る（ログインする前なら貯めておく）</summary>
        public static void Unlock(string id)
        {
            if (state == -1) return;
            if (!queue.Contains(id)) queue.Add(id);
        }

        /// <summary>毎フレーム（Hud から）。始める・回す・送る</summary>
        public static void Tick()
        {
            if (state == -1 || quitting) return;
            if (state == -2) { if (Time.realtimeSinceStartup < retryAt) return; state = 0; }
            // 畳まれた（エディタで再生を止めた・NetReset）。持っていた物は使えないので最初から
            if (state > 0 && EosHost.P == null) { state = 0; me = null; sending = false; gen++; }
            if (state == 0) { Begin(); if (state <= 0) return; }
            EosHost.Tick();
            if (state == 2 && !sending && !blocked && queue.Count > 0 && Time.realtimeSinceStartup >= retryAt) Send();
        }

        static void Begin()
        {
            if (EosHost.Keys() == null) { state = -1; Status = "eos.json が無いので、実績はこの端末だけ"; return; }
            if (!EosHost.Init()) { state = -1; Status = "EOS を始められません：" + EosHost.Why; Debug.LogWarning("[EOS実績] " + Status); return; }
            state = 1; int g = ++gen;
            Login(g, true);
        }

        static void MakeDeviceId(int g)
        {
            var o = new CreateDeviceIdOptions { DeviceModel = string.IsNullOrEmpty(SystemInfo.deviceModel) ? "PC" : (SystemInfo.deviceModel.Length > 60 ? SystemInfo.deviceModel.Substring(0, 60) : SystemInfo.deviceModel) };
            Co.CreateDeviceId(ref o, null, (ref CreateDeviceIdCallbackInfo d) =>
            {
                if (g != gen) return;
                // 2回目からは「もうある」と返る。それでよい
                if (d.ResultCode != Result.Success && d.ResultCode != Result.DuplicateNotAllowed) { Fail("端末IDを作れません（" + d.ResultCode + "）"); return; }
                Login(g);
            });
        }

        static void Login(int g, bool first = false)
        {
            var o = new LoginOptions
            {
                Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
                UserLoginInfo = new UserLoginInfo { DisplayName = "Player" },
            };
            Co.Login(ref o, null, (ref LoginCallbackInfo d) =>
            {
                if (g != gen) return;
                if (d.ResultCode == Result.Success) { LoggedIn(g, d.LocalUserId); return; }
                // この PC に端末IDがまだ無い。作ってから入り直す
                if (first && d.ResultCode != Result.InvalidUser) { MakeDeviceId(g); return; }
                if (d.ResultCode == Result.InvalidUser)
                {
                    var c = new CreateUserOptions { ContinuanceToken = d.ContinuanceToken };
                    Co.CreateUser(ref c, null, (ref CreateUserCallbackInfo u) =>
                    {
                        if (g != gen) return;
                        if (u.ResultCode == Result.Success) LoggedIn(g, u.LocalUserId); else Fail("EOS に登録できません（" + u.ResultCode + "）");
                    });
                    return;
                }
                Fail("EOS にログインできません（" + d.ResultCode + "）");
            });
        }

        static void Fail(string why)
        {
            // つながらないだけなら、60 秒してからやり直す（遊ぶのは止めない）
            Status = why; Debug.LogWarning("[EOS実績] " + why);
            gen++; me = null; state = -2;
            retryAt = Time.realtimeSinceStartup + 60;
        }

        static void LoggedIn(int g, ProductUserId id)
        {
            bool first = me == null;
            me = id; state = 2; Status = "EOS にログインしました";
            if (!first) return;                                        // 期限切れで入り直したとき
            Debug.Log("[EOS実績] ログインしました: " + id);
            var e = new AddNotifyAuthExpirationOptions();
            nAuth = Co.AddNotifyAuthExpiration(ref e, null, (ref AuthExpirationCallbackInfo d) => { if (g == gen) Login(g); });
            // EOS 側の取った実績と、この端末の記録を合わせる
            var q = new QueryPlayerAchievementsOptions { LocalUserId = id, TargetUserId = id };
            Ac.QueryPlayerAchievements(ref q, null, (ref OnQueryPlayerAchievementsCompleteCallbackInfo d) =>
            {
                if (g != gen) return;
                if (d.ResultCode != Result.Success) { Debug.LogWarning("[EOS実績] 取った実績を読めません（" + d.ResultCode + "）"); SyncLocal(null); return; }
                var on = new HashSet<string>();
                var co = new GetPlayerAchievementCountOptions { UserId = id };
                uint n = Ac.GetPlayerAchievementCount(ref co);
                for (uint i = 0; i < n; i++)
                {
                    var ci = new CopyPlayerAchievementByIndexOptions { LocalUserId = id, TargetUserId = id, AchievementIndex = i };
                    if (Ac.CopyPlayerAchievementByIndex(ref ci, out var pa) == Result.Success && pa.HasValue && pa.Value.UnlockTime.HasValue)
                        on.Add((string)pa.Value.AchievementId);
                }
                SyncLocal(on);
            });
        }

        static void SyncLocal(HashSet<string> on)
        {
            Game.AchStore.Load();
            bool ch = false;
            if (on != null)
                foreach (var k in on)
                    if (!Game.AchStore.got.ContainsKey(k) && Game.AchOf(k) != null) { Game.AchStore.got[k] = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); ch = true; }
            if (ch) Game.AchStore.Save();
            foreach (var k in Game.AchStore.got.Keys) if (on == null || !on.Contains(k)) Unlock(k);
        }

        static void Send()
        {
            string id = queue[0];
            sending = true; int g = gen;
            var o = new UnlockAchievementsOptions { UserId = me, AchievementIds = new Utf8String[] { id } };
            Ac.UnlockAchievements(ref o, null, (ref OnUnlockAchievementsCompleteCallbackInfo d) =>
            {
                if (g != gen) return;
                sending = false;
                if (d.ResultCode == Result.Success) { queue.Remove(id); Debug.Log("[EOS実績] 送りました: " + id); return; }
                // Dev Portal に無い ID は何度送っても通らないので捨てる。ほかは 30 秒おきに送り直す
                if (d.ResultCode == Result.ClientPolicyMissingAction)
                {
                    blocked = true;
                    Status = "Dev Portal のクライアントの方針（Client Policy）に、実績の許可がありません。この端末の記録だけにします";
                    Debug.LogWarning("[EOS実績] " + Status);
                    return;
                }
                if (d.ResultCode == Result.NotFound || d.ResultCode == Result.InvalidParameters) { queue.Remove(id); Debug.LogWarning("[EOS実績] Dev Portal に無い実績です: " + id); return; }
                Debug.LogWarning("[EOS実績] 送れません（" + id + "：" + d.ResultCode + "）。あとで送り直します");
                retryAt = Time.realtimeSinceStartup + 30;
            });
        }
    }
}
#else
namespace Nekorobo
{
    /// <summary>EOS の SDK が無いときは、実績はこの端末の記録だけ</summary>
    public static class EosAch
    {
        public static string Status = "EOS の SDK が入っていません（実績はこの端末だけ）";
        public static void Unlock(string id) { }
        public static void Tick() { }
        public static void Stop() { }
    }
}
#endif
