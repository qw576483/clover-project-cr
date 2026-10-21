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
    /// <para>
    /// 加载链由引擎件 <see cref="ConfigSectionLoader{T}"/> 执行：来源按序试 → 读不出 / 解析不了就退下一个 →
    /// 全不可用回 <see cref="RootSection"/> 的内置默认值 → <see cref="Reload"/> 重跑整条链。
    /// 默认值只有一处 = 上面各段的字段初始化器。
    /// </para>
    /// </summary>
    public static class Cfg
    {
        private const string Tag = "Cfg";
        private const string RelativePath = "Configs/config.json";

        /// <summary>加载链（首次访问构造；来源表在首次访问时求值，静态构造期不读盘）。</summary>
        private static ConfigSectionLoader<RootSection> _loader;

        private static ConfigSectionLoader<RootSection> Loader => _loader ??= new ConfigSectionLoader<RootSection>(
            Tag,
            new[]
            {
                // 本工程只有磁盘来源（`Assets/Configs/config.json` 不在 Resources 下）；`LogLabel` = 磁盘路径。
                new ConfigSource("文件:" + FilePath, ReadFileText, FilePath),
            },
            Parse,
            () => new RootSection());

        public static RootSection Config => Loader.Value;
        public static ServerSection Server => Config.server;
        public static AccountSection Account => Config.account;
        public static GameSection Game => Config.game;

        /// <summary>本次加载命中的来源显示名（<c>文件:&lt;路径&gt;</c> / <c>默认值</c>）。</summary>
        public static string Source => Loader.Source;

        public static string FilePath => Path.Combine(Application.dataPath, RelativePath);

        /// <summary>重跑整条来源链（改完 config.json 不必重启）。</summary>
        public static void Reload() => Loader.Reload();

        /// <summary>
        /// 读 <see cref="FilePath"/> 的文本。文件不存在 / 读不动 ⇒ 一条日志 + 返回 <c>null</c>
        /// （= 本来源没有内容，引擎改试下一个来源 = 内置默认值）。
        /// </summary>
        private static string ReadFileText()
        {
            var path = FilePath;
            try
            {
                if (!File.Exists(path))
                {
                    // ⚠️ 必须写全名 CloverEngine.Game：本类有一个 static 属性叫 Game（GameSection），
                    // 直接写 `Game.Logger` 会解析成 Cfg.Game 而报 CS1061（引擎 skill 记录过的坑）。
                    CloverEngine.Game.Logger?.Warn(Tag, $"未找到 {path}，使用内置默认值");
                    return null;
                }

                return File.ReadAllText(path);
            }
            catch (Exception e)
            {
                // 配置问题不该让游戏起不来：回退默认值 + 一条可定位的日志。
                CloverEngine.Game.Logger?.Error(Tag, $"读取异常，回退默认值: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析一段 json；<b>返回 null = 本来源不可用</b>（引擎改试下一个来源 / 回默认值）。
        /// </summary>
        private static RootSection Parse(string json, string from)
        {
            try
            {
                var root = JsonUtility.FromJson<RootSection>(json);
                if (root == null)
                {
                    CloverEngine.Game.Logger?.Error(Tag, $"解析失败（空/格式错误）: {from}，回退默认值");
                    return null;
                }

                // JsonUtility 对 json 里缺失的字段会留 null，逐段兜底防下游空引用。
                if (root.server == null)
                {
                    CloverEngine.Game.Logger?.Warn(Tag, $"{from} 缺少 server 段，使用默认 server 段");
                    root.server = new ServerSection();
                }
                if (root.account == null)
                {
                    CloverEngine.Game.Logger?.Warn(Tag, $"{from} 缺少 account 段，使用默认 account 段");
                    root.account = new AccountSection();
                }
                if (root.game == null)
                {
                    CloverEngine.Game.Logger?.Warn(Tag, $"{from} 缺少 game 段，使用默认 game 段");
                    root.game = new GameSection();
                }

                return root;
            }
            catch (Exception e)
            {
                CloverEngine.Game.Logger?.Error(Tag, $"解析异常（JSON 格式错误？）: {from} → {e.Message}，回退默认值");
                return null;
            }
        }
    }
}
