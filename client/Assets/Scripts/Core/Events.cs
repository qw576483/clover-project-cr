namespace CR
{
    /// <summary>
    /// 事件名常量 —— **唯一定义处**（架构契约 D11）。
    ///
    /// <para>
    /// <b>为什么需要它</b>：`UI` 不许 `using CR.Module`（契约 §1 依赖方向），所以面板与流程之间只能靠
    /// `Game.Event` 通信。事件名一旦允许裸字符串，改名就变成"运行时静默不触发"——查起来极贵。
    /// 因此约定：**业务脚本里出现裸事件名字符串一律算错**（`tools/verify.ps1` 的硬规则同源要求）。
    /// </para>
    /// <para>
    /// <b>命名</b>：`常量值 = "模块.动作"`，与常量路径逐字对应（`Events.Room.ListChanged` → `"Room.ListChanged"`），
    /// 便于日志里一眼看出是谁发的。**参数类型写在每条注释里**（`Game.Event.Emit&lt;T&gt;` / `Emit&lt;T1,T2&gt;`
    /// 最多两个参数，这是引擎 `Event.cs:113-128` 的重载上限，改动时不要超过）。
    /// </para>
    /// <para>
    /// <b>各部分的使用者</b>：`Flow.*` / `Settings.*` 由 `Module/Flow`、`Module/Settings` 与
    /// `UI/Panels/*` 发布与订阅；`Room.*` / `Battle.*` / `Deck.*` 由 `Module/Room`、`Module/Battle`、
    /// `Module/Deck` 消费（本文件只**声明**事件名，消费方不必回来改本文件）。
    /// </para>
    /// </summary>
    public static class Events
    {
        /// <summary>启动链与站点切换（发布方：`Module/Flow/AppFlow`、`UI/Panels/*`）。</summary>
        public static class Flow
        {
            /// <summary>请求登录。参数：`(string account, string password)`。</summary>
            public const string LoginRequest = "Flow.LoginRequest";

            /// <summary>请求打开注册面板（登录面板的"注册新账号"）。参数：无。</summary>
            public const string OpenRegisterRequest = "Flow.OpenRegisterRequest";

            /// <summary>请求注册（账号服成功即登录）。参数：`(string account, string password)`。</summary>
            public const string RegisterRequest = "Flow.RegisterRequest";

            /// <summary>请求提交昵称（创角）。参数：`(string nickname)`。</summary>
            public const string NicknameSubmit = "Flow.NicknameSubmit";

            /// <summary>请求回到登录面板（注册面板的"返回"）。参数：无。</summary>
            public const string BackToLoginRequest = "Flow.BackToLoginRequest";

            /// <summary>请求打开设置面板（任意站点可用）。参数：无。发布方：`MainMenuPanel`。</summary>
            public const string OpenSettingsRequest = "Flow.OpenSettingsRequest";

            /// <summary>
            /// 请求退出游戏。参数：无。
            /// <para>
            /// <b>当前无发布方</b>：原版没有游戏内退出件（移动端，系统层退出），界面上的红色 X 是
            /// 「关闭按钮」而不是退出 ⇒ 没有任何面板发本事件。订阅方仍是
            /// <c>Module/Flow/AppFlow</c>（它接住后执行退出）。
            /// </para>
            /// </summary>
            public const string QuitRequest = "Flow.QuitRequest";

            /// <summary>
            /// 请求切到某站点。参数：`(string station)` —— 取值必须是 <see cref="Stations"/> 的常量。
            /// 用途：`PausePanel` / `ResultPanel` 这类面板请求进 `Pause` / 回 `Battle`，
            /// 而不必 `using CR.Module`。
            /// </summary>
            public const string StationEnterRequest = "Flow.StationEnterRequest";

            /// <summary>
            /// 站点已切换（发布方：`AppFlow`）。参数：`(string station)`。
            /// 供需要"跟着站点开关自己"的模块订阅（例如 `BattleManager` 收到 `Battle` 时开 HUD）。
            /// </summary>
            public const string StationChanged = "Flow.StationChanged";

            /// <summary>
            /// 登录链失败（发布方：`AppFlow`）。参数：`(string reason)`。
            /// 面板**必须**把 reason 显示出来（不能只打日志）——这是"失败原因可见"的唯一通道。
            /// </summary>
            public const string LoginFailed = "Flow.LoginFailed";

            /// <summary>登录链成功并已建立会话。参数：`(string nickname)`（空串 = 尚未创角）。</summary>
            public const string LoginSucceeded = "Flow.LoginSucceeded";

            /// <summary>
            /// 服务端权威昵称已知 / 已更新（发布方：`AppFlow`，在 `PlayerSession.SetNickname` 之后）。
            /// 参数：`(string nickname)`（必然非空）。
            /// <para>
            /// 为什么需要它：昵称是**迟到**的数据（登录拉档案、创角回包、以及主菜单重新拉取都可能晚于
            /// 面板 `OnOpen`）。面板据此把已经画出来的标签**就地刷新**，而不必重开面板 ——
            /// 与 `MainMenuPanel` 的"`PlayerSession` 兜底拉取"配套（推 + 拉两条都到位）。
            /// </para>
            /// </summary>
            public const string NicknameKnown = "Flow.NicknameKnown";

            /// <summary>
            /// 创角（设置昵称）失败（发布方：`AppFlow`）。参数：`(string reason)`。
            /// 复用 `LoginFailed` 是不行的：创角时玩家**已经登录**，把它当登录失败会让登录面板也弹错，
            /// 且 `AppFlow` 的重登录分支会被误触发。
            /// </summary>
            public const string NicknameFailed = "Flow.NicknameFailed";
        }

        /// <summary>
        /// 设置（音量 / 画质 / 全屏）。`*Request` 发布方：`SettingsPanel`；处理方：`SettingsManager`（由
        /// `Bootstrap` 创建并 `Init`，不经过 `AppFlow`）。
        /// <para>
        /// <b>为什么音量是三条而不是一条带"分组名"的</b>：分组名（`"BGM"` / `"SFX"` / `"Voice"`）若作为字符串常量，
        /// 就必须放在某个双方都能引用的地方 —— 而面板⛔不许引 `CR.Module`（契约 §1），
        /// 于是分组名常量无处安放、只能在面板里写裸字符串（违反 D11）。拆成三条就从根上没有这个问题。
        /// </para>
        /// <para>
        /// <b>`*Changed` 的订阅方 = 显示该值的界面</b>（它们是"权威值已变更"的通知，
        /// 不是给 `SettingsManager` 自己用的）。现状：`BgmVolumeChanged` / `SfxVolumeChanged` /
        /// `QualityChanged` / `FullscreenChanged` 由 `SettingsPanel` 订阅（事件一到就地刷新显示，
        /// 面板因此不再是"打开那一刻的快照"）；`VoiceVolumeChanged` 见其常量注释。
        /// </para>
        /// </summary>
        public static class Settings
        {
            /// <summary>请求改 BGM 音量。参数：`(float volume)`，0~1。</summary>
            public const string BgmVolumeRequest = "Settings.BgmVolumeRequest";

            /// <summary>请求改音效音量。参数：`(float volume)`，0~1。</summary>
            public const string SfxVolumeRequest = "Settings.SfxVolumeRequest";

            /// <summary>
            /// 请求改人声音量。参数：`(float volume)`，0~1。
            /// <para>
            /// ⚠️ **本事件目前在工程内没有发布方**（`SettingsPanel` 的「人声」行已删，见该面板
            /// `Build` 的注释：原版设置界面没有此项 + 本工程无任何 voice 素材 / 播放点）。
            /// 保留它与其处理者是为了不动 `Module/Settings` 已登记的设置实体（`策划/实体清单.tsv` 的
            /// S3 VoiceVolume：`audio.voice` 仍持久化、`Init` 仍把它应用到引擎 `SoundGroup.Voice`）。
            /// 要重新用它（例如将来接入语音包）⇒ 由新的显示方/触发方直接 `Emit` 本事件即可。
            /// </para>
            /// </summary>
            public const string VoiceVolumeRequest = "Settings.VoiceVolumeRequest";

            /// <summary>请求改画质档位。参数：`(int tier)` —— 0=Low 1=Medium 2=High（`QualityTier` 的取值）。</summary>
            public const string QualityRequest = "Settings.QualityRequest";

            /// <summary>请求改全屏。参数：`(bool fullscreen)`。</summary>
            public const string FullscreenRequest = "Settings.FullscreenRequest";

            /// <summary>BGM 音量已生效并落盘。参数：`(float volume)`。订阅方：`SettingsPanel`。</summary>
            public const string BgmVolumeChanged = "Settings.BgmVolumeChanged";

            /// <summary>音效音量已生效并落盘。参数：`(float volume)`。订阅方：`SettingsPanel`。</summary>
            public const string SfxVolumeChanged = "Settings.SfxVolumeChanged";

            /// <summary>
            /// 人声音量已生效并落盘。参数：`(float volume)`。
            /// <para>
            /// ⚠️ **本事件目前在工程内无人订阅，这是设计状态、不是漏挂。** 理由三条：
            /// ① 工程内**没有**任何显示"人声"的界面（设置面板的「人声」行已删：原版设置界面
            /// —— 基线图 `策划/参考图/24_设置_499x1080.jpg` —— 只有 Music / SFx 开关，没有人声项，
            /// 按"原版没有就不加"移除）；② 工程内**没有**任何 voice 素材或播放点
            /// （全工程 0 处 `PlayVoice` 调用、`AudioPaths` 无 Voice 键、`Resources` 下无 `Sound/Voice`）；
            /// ③ 它与上面两条音量事件出自 `SettingsManager` 同一段对称代码，删除会让"音量 ×3"的
            /// 契约与已登记实体（`策划/实体清单.tsv` S3 VoiceVolume）同时失效 ⇒ 保留为**待接入口**。
            /// </para>
            /// </summary>
            public const string VoiceVolumeChanged = "Settings.VoiceVolumeChanged";

            /// <summary>
            /// 画质档位已生效并落盘。参数：`(int tier)`。订阅方：`SettingsPanel`。
            /// <para>⚠️ 有**两个**发布方，两条都在 `SettingsManager`：① 玩家点 ◀▶（`QualityRequest` 的处理链）；
            /// ② **引擎自动降档**（`Game.Quality.OnLevelChanged` → `SettingsManager.OnEngineQualityChanged`）。
            /// 少了 ② 就会出现"自动降档后已打开的面板显示旧档位"。</para>
            /// </summary>
            public const string QualityChanged = "Settings.QualityChanged";

            /// <summary>全屏开关已生效并落盘。参数：`(bool fullscreen)`。订阅方：`SettingsPanel`。</summary>
            public const string FullscreenChanged = "Settings.FullscreenChanged";
        }

        /// <summary>卡组（发布方：`Module/Deck`；面板 `DeckEditPanel`）。</summary>
        public static class Deck
        {
            /// <summary>请求打开卡组编辑面板。参数：无。发布方：`MainMenuPanel`。</summary>
            public const string OpenRequest = "Deck.OpenRequest";

            /// <summary>卡池已就绪。参数：`(CR.Def.CardInfo[] cards)`。</summary>
            public const string PoolLoaded = "Deck.PoolLoaded";

            /// <summary>
            /// 卡组变更。参数：`(CR.Def.DeckRef { slot, ids })`。**双通道**（见 `Module/Deck/DeckManager`）：
            /// 面板 → 管理器 = 「把 `ids` 保存到 `slot` 这个卡组号」；
            /// 管理器 → 面板 = 「服务端确认 `slot` 号就是这 8 张」（保存成功后的回执）。
            /// </summary>
            public const string Changed = "Deck.Changed";

            /// <summary>
            /// 请求读某个卡组号的内容。参数：`(int slot)`，`slot` = 0..4；**-1 = 当前使用的那个号**。
            /// 发布方：`DeckEditPanel`（开面板 / 点卡组号）；消费方：`Module/Deck` ⇒ 回 <see cref="SlotLoaded"/>。
            /// </summary>
            public const string SlotRequest = "Deck.SlotRequest";

            /// <summary>某个卡组号的内容已就绪（服务端权威）。参数：`(CR.Def.DeckRef { slot, ids })`。</summary>
            public const string SlotLoaded = "Deck.SlotLoaded";

            /// <summary>保存卡组失败。参数：`(string reason)`。</summary>
            public const string SaveFailed = "Deck.SaveFailed";
        }

        /// <summary>房间（发布方：`Module/Room`；面板 `RoomListPanel` / `RoomPanel`）。</summary>
        public static class Room
        {
            /// <summary>请求打开房间列表面板。参数：无。发布方：`MainMenuPanel`。</summary>
            public const string OpenListRequest = "Room.OpenListRequest";

            /// <summary>请求刷新房间列表。参数：无。</summary>
            public const string RefreshRequest = "Room.RefreshRequest";

            /// <summary>请求创建房间。参数：`(string name)`。</summary>
            public const string CreateRequest = "Room.CreateRequest";

            /// <summary>请求加入房间。参数：`(string roomId)`。</summary>
            public const string JoinRequest = "Room.JoinRequest";

            /// <summary>请求离开房间。参数：无。</summary>
            public const string LeaveRequest = "Room.LeaveRequest";

            /// <summary>请求准备 / 取消准备。参数：`(bool ready)`。</summary>
            public const string ReadyRequest = "Room.ReadyRequest";

            /// <summary>请求房主开打。参数：无。</summary>
            public const string StartRequest = "Room.StartRequest";

            /// <summary>请求设置 AI 补位。参数：`(bool aiFill)`。</summary>
            public const string SetAiRequest = "Room.SetAiRequest";

            /// <summary>大厅房间列表变化（`PushRoomList`）。参数：`(CR.Def.RoomInfo[] rooms)`。</summary>
            public const string ListChanged = "Room.ListChanged";

            /// <summary>房间状态变化（`PushRoomState`）。参数：`(CR.Def.RoomStateNotify state)`。</summary>
            public const string StateChanged = "Room.StateChanged";

            /// <summary>已进入某房间。参数：`(string roomId)`。</summary>
            public const string Joined = "Room.Joined";

            /// <summary>房间操作失败。参数：`(string reason)`。</summary>
            public const string Failed = "Room.Failed";
        }

        /// <summary>对局（发布方：`Module/Battle`；面板 `HudPanel` / `PausePanel` / `ResultPanel`）。</summary>
        public static class Battle
        {
            /// <summary>主菜单请求开一局人机对战。参数：无。发布方：`MainMenuPanel`（`AppFlow` 也直接发同义事件）。</summary>
            public const string AiBattleRequest = "Battle.AiBattleRequest";

            /// <summary>服务端开打（`PushBattleStart`）。参数：`(CR.Def.BattleStartNotify start)`。</summary>
            public const string Started = "Battle.Started";

            /// <summary>周期快照（`PushBattleSnapshot`，10 Hz）。参数：`(CR.Def.BattleSnapshot snapshot)`。</summary>
            public const string Snapshot = "Battle.Snapshot";

            /// <summary>离散事件（`PushBattleEvent`）。参数：`(CR.Def.BattleEventNotify events)`。</summary>
            public const string Events = "Battle.Events";

            /// <summary>结算（`PushBattleEnd`）。参数：`(CR.Def.BattleEndNotify result)`。</summary>
            public const string Ended = "Battle.Ended";

            /// <summary>请求出牌（拖放抬起）。参数：`(int cardId, UnityEngine.Vector2 worldPos)`。服务端最终裁决合法性。</summary>
            public const string PlayCardRequest = "Battle.PlayCardRequest";

            /// <summary>请求投降。参数：无。</summary>
            public const string SurrenderRequest = "Battle.SurrenderRequest";

            /// <summary>请求回主菜单（结算 / 暂停面板）。参数：无。</summary>
            public const string ReturnToMainMenuRequest = "Battle.ReturnToMainMenuRequest";

            /// <summary>开一局失败（卡组非法 / 建房失败等）。参数：`(string reason)`。</summary>
            public const string StartFailed = "Battle.StartFailed";
        }
    }
}
