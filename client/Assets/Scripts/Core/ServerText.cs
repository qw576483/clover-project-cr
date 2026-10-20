using CloverEngine;

namespace CR
{
    /// <summary>
    /// 服务端 / 网络异常原文 → 玩家可读文案。
    ///
    /// <para>
    /// <b>为什么需要它</b>：战斗与房间的失败原因是服务端 <c>err</c> 原样透传
    /// （`Module/Battle/BattleManager.cs` 的 <c>Fail</c> / `Module/Room/RoomManager.cs` 的 <c>Fail</c>），
    /// 而战斗规则层的错误串是英文（`server/game/core/battle.go` 的 <c>Err*</c>，例如
    /// <c>core: point is outside the deploy zone: (9000,-444)</c>）；网络层抛的是
    /// <see cref="System.TimeoutException"/> 这类英文异常消息。直接弹给玩家就是英文原文。
    /// </para>
    ///
    /// <para>
    /// <b>映射口径</b>：
    /// ① 按关键字匹配（大小写不敏感）⇒ 返回固定中文文案；
    /// ② 没有匹配、但原文是中文（尾段不含连续的 ASCII 单词）⇒ 原样返回（服务端很多拒绝理由本来就是中文）；
    /// ③ 其余（英文原文）⇒ 返回 null，由 <see cref="ToPlayer"/> 换成 <see cref="Unknown"/> 并留痕。
    /// 关键字表覆盖 `server/game/core/battle.go`、`server/game/logic/room.go`、
    /// `server/game/logic/battle.go` 里会下发到客户端的 <c>err</c> 串。
    /// </para>
    /// </summary>
    public static class ServerText
    {
        /// <summary>没有对应文案时的玩家兜底提示（⛔ 不把英文原文弹给玩家）。</summary>
        public const string Unknown = "操作被服务端拒绝，请重试";

        private struct Rule
        {
            public string Key;
            public string Text;

            public Rule(string key, string text)
            {
                Key = key;
                Text = text;
            }
        }

        /// <summary>
        /// 关键字 → 中文。顺序即优先级：**先具体后笼统**（例如 `connection timeout` 必须先命中超时）。
        /// </summary>
        private static readonly Rule[] Rules =
        {
            // ── 战斗规则（core/battle.go 的 Err*）──
            new Rule("outside the deploy zone", "不能在部署区外放卡"),
            new Rule("not enough elixir", "圣水不足"),
            new Rule("card is not in hand", "这张卡不在手牌里"),
            new Rule("battle already ended", "对局已结束"),
            new Rule("unknown card", "这张卡不存在"),
            new Rule("invalid team", "队伍信息异常，无法操作"),
            new Rule("cardtable is nil", "对局数据异常，请重新开局"),
            new Rule("a deck must hold exactly 8 cards", "卡组必须是 8 张卡"),
            new Rule("does not resolve in the table", "对局数据异常，请重新开局"),
            new Rule("with no spelldef", "对局数据异常，请重新开局"),
            new Rule("has no unit_key", "对局数据异常，请重新开局"),

            // ── 房间 / 对局流程（logic/*.go 的中文 err）──
            new Rule("房间不存在", "房间不存在（可能已解散）"),
            new Rule("房间已满", "房间已满"),
            new Rule("只有房主", "只有房主可以执行这个操作"),
            new Rule("你不在这个房间里", "你已经不在这个房间里了"),
            new Rule("玩家不在房间内", "你已经不在这个房间里了"),
            new Rule("已在其他房间", "你已经在一个房间里了"),
            new Rule("已在别的房间", "你已经在一个房间里了"),
            new Rule("对局未进行", "对局还没有开始"),
            new Rule("对局已开始", "对局已经开始了"),
            new Rule("对局已结束", "对局已经结束了"),
            new Rule("还没有对局", "对局还没有开始"),
            new Rule("未登录", "登录状态已失效，请重新登录"),
            new Rule("配表未就绪", "服务端数据未就绪，请稍后重试"),
            new Rule("网络模块未挂载", "网络未就绪，请重启游戏"),

            // ── 网络异常（System.TimeoutException / 连接断开）──
            new Rule("timed out", "网络超时，请重试"),
            new Rule("timeout", "网络超时，请重试"),
            new Rule("连接超时", "网络超时，请重试"),
            new Rule("connection", "与服务器的连接已断开，请重试"),
            new Rule("disconnected", "与服务器的连接已断开，请重试"),
            new Rule("not connected", "与服务器的连接已断开，请重试"),
            new Rule("task was canceled", "请求已取消，请重试"),
        };

        /// <summary>
        /// 映射为中文文案：命中关键字返回固定文案；原文本身是中文则原样返回；其余返回 null（调用方按未映射处理）。
        /// </summary>
        public static string Map(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            for (var i = 0; i < Rules.Length; i++)
            {
                if (raw.IndexOf(Rules[i].Key, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return Rules[i].Text;
            }

            // 原文里已全是中文（服务端很多拒绝理由本来是中文，如「人数不足：需要 2 个座位」）⇒ 原样显示。
            return ContainsAsciiWord(raw) ? null : raw;
        }

        /// <summary>
        /// 给玩家看的失败文案。映射不到时返回 <see cref="Unknown"/>，并把服务端原文写进日志（⛔ 不弹英文）。
        /// </summary>
        public static string ToPlayer(string raw, string logTag)
        {
            if (string.IsNullOrEmpty(raw))
            {
                Game.Logger?.Warn(logTag, "失败原因为空（服务端未给出原因 / 空回包）");
                return Unknown;
            }

            var mapped = Map(raw);
            if (mapped != null) return mapped;

            Game.Logger?.Warn(logTag, "未映射的失败原文（按兜底文案显示）：" + raw);
            return Unknown;
        }

        /// <summary>是否含连续 4 个 ASCII 字母（用于识别"这段文字还是英文"，跳过纯数字 / 单字母单位）。</summary>
        private static bool ContainsAsciiWord(string text)
        {
            var run = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
                {
                    run++;
                    if (run >= 4) return true;
                }
                else
                {
                    run = 0;
                }
            }
            return false;
        }
    }
}
