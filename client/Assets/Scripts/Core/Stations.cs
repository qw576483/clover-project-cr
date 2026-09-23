namespace CR
{
    /// <summary>
    /// 站点名常量 —— **唯一定义处**。
    ///
    /// <para>
    /// 这些字符串**同时**是 `Game.Fsm` 的状态名与跨模块通信用的事件参数，因此必须只有一处：
    /// 面板在不引用 `CR.Module` 的前提下请求切站点时（`Events.Flow.StationEnterRequest`），
    /// 传的就是这里的常量；`AppFlow` 用同一批常量注册 FSM 状态。
    /// </para>
    /// <para>
    /// 出处：`docs/client-architecture.md` §4「站点（App Flow）与面板清单」的 `Fsm 状态` 列，逐字一致。
    /// ⚠️ `Battle` 与引擎自注册的占位状态同名（`Game.cs:855` 的 `Fsm.RegisterState("Battle")`，无回调）——
    /// 本类按契约仍用 `Battle`，注册时引擎会打一条 Warn（占位态的 null 回调被本项目的替换，无行为损失）。
    /// </para>
    /// </summary>
    public static class Stations
    {
        /// <summary>启动画面（`BootPanel`，`Boot` 场景；停留后切 `Main` 场景 —— 见 `AppFlow.LoadMainSceneFromBoot`）。</summary>
        public const string Boot = "Boot";

        /// <summary>登录 / 注册（`LoginPanel` / `RegisterPanel`，Main 场景）。</summary>
        public const string Login = "Login";

        /// <summary>创角（`NicknamePanel`，Main 场景）。</summary>
        public const string Nickname = "Nickname";

        /// <summary>主菜单（`MainMenuPanel`，Main 场景）。卡组编辑 / 房间列表是它的子面板，不单独占站点。</summary>
        public const string MainMenu = "MainMenu";

        /// <summary>房间内（`RoomPanel`，Main 场景）。由 agent-06 的 `Module/Room` 驱动。</summary>
        public const string Room = "Room";

        /// <summary>对局（`HudPanel`，Battle01 场景）。由 agent-07 的 `Module/Battle` 驱动。</summary>
        public const string Battle = "Battle";

        /// <summary>暂停（`PausePanel`，Battle01 场景，Popup 层）。对战**不真暂停**，只覆盖菜单。</summary>
        public const string Pause = "Pause";
    }
}
