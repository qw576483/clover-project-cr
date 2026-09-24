using CloverEngine;

namespace CR
{
    /// <summary>
    /// 会话态的**唯一**权威存放处：玩家昵称（= 服务端档案里的 `nickname`）。
    ///
    /// <para>
    /// <b>为什么需要它</b>：`MainMenuPanel` 的打开 param 是**一次性快照**
    /// （`AppFlow._nickname` 只在登录拉档案 / 创角回包时写一次，且只有 `EnterMainMenu`
    /// 这一条路径会把它传下去）。任何"不经 `EnterMainMenu`"的打开路径
    ///（`UIManager.Open&lt;T&gt;` 对已存在面板会重调 `OnOpen(param)`，见
    /// `Runtime/Presentation/UI.cs:127`；`AppFlow.GoTo` 对"已经是当前站点"的请求早退，
    /// `AppFlow.cs:365`）都会让面板拿到 `param == null` ⇒ 界面显示**硬编码**的
    /// `"玩家"`（`MainMenuPanel.cs:105`），而服务端档案里可能是别的名字，且**一句日志都没有**。
    /// </para>
    /// <para>
    /// <b>职责边界（契约 §1）</b>：面板⛔不许 `using CR.Module`，所以它没法去问 `AppFlow`。
    /// 本类放在 `CR` 命名空间（与 `Events` / `Cfg` 同层），**写方只有 `AppFlow`**（服务端回包），
    /// **读方是面板**；面板因此能在**任何**打开路径下拿到服务端权威昵称，不依赖调用者记性。
    /// </para>
    /// </summary>
    public static class PlayerSession
    {
        private const string Tag = "PlayerSession";

        private static string _nickname = string.Empty;

        /// <summary>
        /// 记下这个昵称时那条引擎总线（= "**当前会话**"的标识）。
        /// <para>
        /// <b>为什么需要它</b>：`_nickname` 是**裸静态**，而本工程**关闭了域重载**
        /// ⇒ 静态残留会把**上一次 Launch** 的昵称带进下一次：面板会显示上一个账号的名字，
        /// 与服务端档案不一致，且一句日志都没有（同类静态残留见 `App/Bootstrap.cs` 的 `_bus` 注释）。
        /// 判据口径与工程其余 5 处逐字一致（`RoomManager` / `DeckManager` / `BattleManager` /
        /// `BattleUiHost` / `BgmView`）：**按当前总线对象标识当前会话** —— `Game.Launch` 每次新建
        /// `EventBus`（引擎 `Game.cs:381`），总线一换，先前的昵称就不再算数。
        /// </para>
        /// </summary>
        private static IEventBus _bus;

        /// <summary>
        /// 记下的昵称是否仍属**当前**这次 Launch（总线未变 ⇒ 数据还算数）。
        /// 启动早期 `Game.Event` 为 null 且 `_bus` 也为 null 时判为"当前"（此时昵称本就是空串）。
        /// </summary>
        private static bool IsCurrentRound
        {
            get { return ReferenceEquals(_bus, Game.Event); }
        }

        /// <summary>
        /// 服务端权威昵称；未知时为空串（⛔ 这里不塞本地默认值，默认值属兜底策略、由界面决定）。
        /// 先前 Launch 残留的昵称**不算数**（见 <see cref="_bus"/>）。
        /// </summary>
        public static string Nickname
        {
            get { return IsCurrentRound ? (_nickname ?? string.Empty) : string.Empty; }
        }

        /// <summary>是否已经从服务端拿到过非空昵称（先前残留的不算）。</summary>
        public static bool HasNickname
        {
            get { return !string.IsNullOrEmpty(Nickname); }
        }

        /// <summary>
        /// 记下服务端下发的昵称。`source` = 哪条回包写的（`GetProfile` / `SetNickname`），
        /// 便于在日志里区分"登录时拿到"和"创角后拿到"。
        /// 传 null / 空串表示"服务端该玩家还没创角"——那是**正常态**（随后进创角站点），
        /// 不是错误，因此只 Info 不 Warn。
        /// </summary>
        public static void SetNickname(string nickname, string source)
        {
            _nickname = nickname ?? string.Empty;
            _bus = Game.Event;   // 标记"这个昵称属于当前引擎"（见 _bus 字段）
            Game.Logger?.Info(Tag, HasNickname
                ? $"服务端昵称已记下 nickname='{_nickname}'（来自 {source}）"
                : $"服务端昵称为空（来自 {source}）—— 该玩家尚未创角，属正常态");
        }

        /// <summary>清空（登出 / 换账号时用，避免把上一个会话的名字带进新会话）。</summary>
        public static void Clear()
        {
            _nickname = string.Empty;
            _bus = null;         // 会话已结束：这个昵称不再属于任何一次 Launch（见 _bus 字段）
            Game.Logger?.Info(Tag, "会话昵称已清空（登出）");
        }
    }
}
