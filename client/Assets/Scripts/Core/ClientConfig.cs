using System;
using System.IO;
using CloverEngine;
using UnityEngine;

namespace CR
{
    [Serializable]
    public class ServerSection
    {
        // 网关 **TCP** 口（服务端 gateway.listen_tcp）。⛔ 不是 8001 —— 8001 是 WebSocket，
        // Unity 原生客户端走裸 TCP，填错的现象是「连上→秒断→重连耗尽被踢→之后所有 Call 超时」。
        public string addr = "127.0.0.1:8002";
        // 网关 UDP 口（gateway.listen_udp）。留空 = 不启用 UDP。
        // 本项目留空：快照虽以 BestEffort 推送，但引擎 gateway 的 sendUnreliable 在
        // 「无 UDP 端点」时会**降级为可靠 TCP**（见 clover-server-engine
        // internal/transport/gateway/gwcore/session.go 的 sendByDeliveryMode），
        // 因此不会丢快照，且少一类握手失败模式。
        public string udp_addr = "";
        // 必须与服务端 gateway.tcp_tls_disabled **相反**：本项目服务端 tcp_tls_disabled=true（明文 TCP）
        // ⇒ 客户端 tls=false。两边不一致的症状是「连上就断」。
        public bool tls = false;
        // 账号服 HTTP 地址（服务端 auth.listen）。**必填** ——
        // 引擎只有一种登录模式（账号服校验），留空则 LoginAsync/SignupAsync 抛 InvalidOperationException。
        public string auth_addr = "http://127.0.0.1:8051";
        public int call_timeout = 10;
        public int max_reconnect_count = 5;
    }

    [Serializable]
    public class AccountSection
    {
        public string name_prefix = "cr";
        public string name_suffix = "_cli";
        public string password = "cr123456";
        public int line = 0;
    }

    [Serializable]
    public class GameSection
    {
        public string default_nick = "玩家一";
        public int poll_interval_ms = 50;
    }

    [Serializable]
    public class RootSection
    {
        public ServerSection server = new ServerSection();
        public AccountSection account = new AccountSection();
        public GameSection game = new GameSection();
    }

    /// <summary>
    /// 客户端配置唯一入口：从 <c>Assets/Configs/config.json</c> 读取。
    /// 业务代码**不许**再出现地址 / 端口 / 账号 / 密码 / 超时 / 重连次数的字面量 —— 一律走 <see cref="Cfg"/>。
    /// </summary>
    public static class Cfg
    {
        private const string RelativePath = "Configs/config.json";
        private static RootSection _config;

        public static RootSection Config => _config ??= Load();
        public static ServerSection Server => Config.server;
        public static AccountSection Account => Config.account;
        public static GameSection Game => Config.game;

        public static string FilePath => Path.Combine(Application.dataPath, RelativePath);
        public static void Reload() => _config = Load();

        private static RootSection Load()
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path))
                {
                    // ⚠️ 必须写全名 CloverEngine.Game：本类有一个 static 属性叫 Game（GameSection），
                    // 直接写 `Game.Logger` 会解析成 Cfg.Game 而报 CS1061（引擎 skill 记录过的坑）。
                    CloverEngine.Game.Logger?.Warn("Cfg", $"未找到 {path}，使用内置默认值");
                    return new RootSection();
                }

                var root = JsonUtility.FromJson<RootSection>(File.ReadAllText(path));
                if (root == null)
                {
                    CloverEngine.Game.Logger?.Error("Cfg", $"解析失败（空/格式错误）: {path}，回退默认值");
                    return new RootSection();
                }

                // JsonUtility 对 json 里缺失的字段会留 null，逐段兜底防下游空引用。
                root.server ??= new ServerSection();
                root.account ??= new AccountSection();
                root.game ??= new GameSection();
                return root;
            }
            catch (Exception e)
            {
                // 配置问题不该让游戏起不来：回退默认值 + 一条可定位的日志。
                CloverEngine.Game.Logger?.Error("Cfg", $"读取异常，回退默认值: {e.Message}");
                return new RootSection();
            }
        }
    }
}
