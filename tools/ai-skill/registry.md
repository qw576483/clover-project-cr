# 设施登记簿（clover-project-cr）

> 新增**对外可见**的设施（消息号 / handler / 面板 / 管理器 / 通用函数 / 配表）后**回来补一行**。
> 过期比没有更糟。

## 消息号

| 消息号 | 名称 | 方向 | 用途 | 请求 / 回包结构体 | 状态 |
| --- | --- | --- | --- | --- | --- |
| 1000101 | `MsgSetNickname` | C2S | 设置昵称 | `SetNicknameReq` / `SetNicknameReply` | 契约已冻结 |
| 1000102 | `MsgGetProfile` | C2S | 拉个人档案 | `GetProfileReq` / `GetProfileReply` | 契约已冻结 |
| 1000201 | `MsgGetCardPool` | C2S | 拉 60 张卡池 | `GetCardPoolReq` / `GetCardPoolReply` | 契约已冻结 |
| 1000202 | `MsgGetDeck` | C2S | 拉我的卡组 | `GetDeckReq` / `GetDeckReply` | 契约已冻结 |
| 1000203 | `MsgSaveDeck` | C2S | 保存 8 张卡组 | `SaveDeckReq` / `SaveDeckReply` | 契约已冻结 |
| 1000301 | `MsgRoomCreate` | C2S | 创建房间 | `RoomCreateReq` / `RoomCreateReply` | 契约已冻结 |
| 1000302 | `MsgRoomList` | C2S | 拉房间列表 | `RoomListReq` / `RoomListReply` | 契约已冻结 |
| 1000303 | `MsgRoomJoin` | C2S | 加入房间 | `RoomJoinReq` / `RoomJoinReply` | 契约已冻结 |
| 1000304 | `MsgRoomLeave` | C2S | 离开房间 | `RoomLeaveReq` / `RoomLeaveReply` | 契约已冻结 |
| 1000305 | `MsgRoomReady` | C2S | 准备 / 取消准备 | `RoomReadyReq` / `RoomReadyReply` | 契约已冻结 |
| 1000306 | `MsgRoomStart` | C2S | 房主开打 | `RoomStartReq` / `RoomStartReply` | 契约已冻结 |
| 1000307 | `MsgRoomSetAi` | C2S | 房主设 AI 补位 | `RoomSetAiReq` / `RoomSetAiReply` | 契约已冻结 |
| 1000401 | `MsgBattlePlayCard` | C2S | 出牌 | `BattlePlayCardReq` / `BattlePlayCardReply` | 契约已冻结 |
| 1000402 | `MsgBattleSurrender` | C2S | 投降 | `BattleSurrenderReq` / `BattleSurrenderReply` | 契约已冻结 |
| 1000403 | `MsgBattleSync` | C2S | 主动拉全量状态 | `BattleSyncReq` / `BattleSyncReply` | 契约已冻结 |
| 1000501 | `MsgAiBattleStart` | C2S | 主菜单「人机对战」开一局 | `AiBattleStartReq` / `AiBattleStartReply` | 契约已冻结 |
| 3002001 | `PushRoomList` | 推送 | 大厅房间列表变化 | `RoomListNotify` | 契约已冻结 |
| 3002002 | `PushRoomState` | 推送 | 房间成员 / 准备 / AI 状态 | `RoomStateNotify` | 契约已冻结 |
| 3002003 | `PushBattleStart` | 推送 | 开打：对局配置 | `BattleStartNotify` | 契约已冻结 |
| 3002004 | `PushBattleSnapshot` | 推送（BestEffort/UDP） | 周期快照 10 Hz | `BattleSnapshot` | 契约已冻结 |
| 3002005 | `PushBattleEvent` | 推送（Reliable） | 对局离散事件 | `BattleEventNotify` | 契约已冻结 |
| 3002006 | `PushBattleEnd` | 推送（Reliable） | 结算 | `BattleEndNotify` | 契约已冻结 |

## 服务端 Handler

| 文件 | Handler | 说明 | 状态 |
| --- | --- | --- | --- |
| `logic/logic.go` | （装配点） | 配表加载 → `core.CardTable` 适配 → `room.Module` + 业务 `room.Kernel` → 16 条 C2S 注册 → 断线回调 | ✅ 已实现 |
| `logic/cardtable.go` | （适配层） | 打表产物 → `core.CardTable`（按列名解析 tsv；`flying` → `core.UnitDef.Flying`） | ✅ 已实现 |
| `logic/player.go` | `onSetNickname` / `onGetProfile` | 昵称与档案（`datadef.PlayerSchema`）+ 战绩补记 | ✅ 已实现 |
| `logic/deck.go` | `onGetCardPool` / `onGetDeck` / `onSaveDeck` | 卡池与卡组（8 张 / 不重复 / 存在性校验） | ✅ 已实现 |
| `logic/room.go` | `onRoomCreate` / `onRoomList` / `onRoomJoin` / `onRoomLeave` / `onRoomReady` / `onRoomStart` / `onRoomSetAi` | 房间（`room.Module` + 业务 `room.Kernel`）+ 大厅推送 + 断线判负 | ✅ 已实现 |
| `logic/battle.go` | `onBattlePlayCard` / `onBattleSurrender` / `onBattleSync` | 对局输入 + 50 ms tick（独立 scope）+ 快照 10 Hz / 事件 / 结算推送 | ✅ 已实现 |
| `logic/ai.go` | `onAiBattleStart` | 人机对战入口 + 房间 AI 补位 | ✅ 已实现 |
| `logic/logic_test.go` | （离线断言） | 配表适配 / 卡组校验 / AI 落点合法性 / AI 必出牌 / 快照适配 / 断线簿记 | ✅ 已实现 |

## 服务端 core（对局内核，纯 Go · 零引擎依赖）

| 文件 | 职责 | 状态 |
| --- | --- | --- |
| `core/units.go` | 单位系统常量（milli-tile / 20 TPS / 竞技场尺寸 / 河桥 / 圣水）+ 定点几何 | ✅ 已实现 |
| `core/arena.go` | 竞技场几何 + `CanDeploy`（含公主塔倒塌口袋区） | ✅ 已实现 |
| `core/card.go` | `CardKind` / `UnitDef` / `SpellDef` / `CardDef` / `CardTable` | ✅ 已实现 |
| `core/entity.go` | 实体（单位 / 建筑 / 塔 / 投射物）+ 攻击状态机 + `nav` | ✅ 已实现 |
| `core/hand.go` | 8 卡组洗牌 + 4 手牌轮转 + 下一张（确定性 randSource） | ✅ 已实现 |
| `core/elixir.go` | 圣水三段回复（整数累加器，无浮点） | ✅ 已实现 |
| `core/pathing.go` | 车道 → 桥 → 目标 waypoint；`crossesRiver` | ✅ 已实现 |
| `core/targeting.go` | 索敌过滤 / 最近目标 / 锁定保持 | ✅ 已实现 |
| `core/combat.go` | 攻击结算 / 投射物 / 法术 / 亡语 / 推挤分离 / 击退 | ✅ 已实现 |
| `core/tower.go` | 塔位 + 国王塔惰性激活（3300 ms） | ✅ 已实现 |
| `core/rules.go` | 胜负判定（三冠 / 比塔 / 比血 / 平局 / 投降） | ✅ 已实现 |
| `core/battle.go` | 对局主体 + `Step()` / `PlayCard()` / `Surrender()` | ✅ 已实现 |
| `core/ai.go` | AI 决策 `Decide()`（防守 → 法术 → 进攻；落点必过 `CanDeploy`） | ✅ 已实现 |
| `core/snapshot.go` | 快照 `Snapshot()` / 事件 `DrainEvents()` | ✅ 已实现 |
| `core/*_test.go` | 17 条离线断言（几何 / 部署 / 圣水 / 手牌 / 寻路 / 飞行 / 近战 / 塔 / 胜负 / 确定性 / 性能） | ✅ 全绿 |

## 数据 Schema

| Schema | Type | OwnerType | 用途 | 关键字段 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `PlayerSchema` | `player` | `OwnerPlayer` | 玩家档案 | `nickname` / `deck`(8 卡 id) / `wins` / `losses` | ✅ 已实现（`logic/player.go`） |

## 客户端面板 / 管理器

| 类 | 文件 | 挂载方式 | 职责 | 状态 |
| --- | --- | --- | --- | --- |
| `PanelFactory` | `UI/PanelFactory.cs` | `CloverPresentation.PanelProvider` 替换 | 运行时 new GameObject + AddComponent（**零 prefab 资产**，架构契约 D2①） | ✅ 已实现 |
| `CrUiStyle` | `UI/CrUiStyle.cs` | 静态 | 面板共用配色/字号/建件 | ✅ 已实现 |
| `BootPanel` | `UI/Panels/` | 运行时构建 | 启动画面 + `by clover-engine` 署名 | ✅ 已实现 |
| `LoginPanel` / `RegisterPanel` | `UI/Panels/` | 运行时构建 | 账号密码 → 换 token → `EMsg.Login` | ✅ 已实现 |
| `MainMenuPanel` | `UI/Panels/` | 运行时构建 | 卡组编辑 / 房间列表 / 人机对战 / 设置 / 退出 | ✅ 已实现 |
| `NicknamePanel` | `UI/Panels/` | 运行时构建 | 创角（昵称，上限 16 与服务端同值） | ✅ 已实现 |
| `SettingsPanel` | `UI/Panels/` | 运行时构建 | 音量 ×3 / 画质 / 全屏（真改真存） | ✅ 已实现 |
| `LoadingPanel` | `UI/Panels/` | 运行时构建 | 读条（`Game.Scene.Load` 真进度） | ✅ 已实现 |
| `DeckEditPanel` | `UI/Panels/DeckEditPanel.cs` | 运行时构建 | 60 张卡池里选 8 张（`PanelArgs.MaxSelected`） | ✅ 已实现（agent-06） |
| `RoomListPanel` | `UI/Panels/RoomListPanel.cs` | 运行时构建 | 房间列表 + 创建 + 刷新 + 加入 | ✅ 已实现（agent-06） |
| `RoomPanel` | `UI/Panels/RoomPanel.cs` | 运行时构建 | 房间内：成员 / 准备 / AI 补位 / 开打 | ✅ 已实现（agent-06） |
| `HudPanel` | `UI/Panels/HudPanel.cs` | 运行时构建 | 圣水条 / 手牌 / 下一张 / 计时 / 冠数 / **拖放出牌** | ✅ 已实现（agent-07a + 主 agent 补落点指示集成） |
| `PausePanel` | `UI/Panels/` | 运行时构建 | 继续 / 设置 / 投降 / 回主菜单 | 待实现（agent-08） |
| `ResultPanel` | `UI/Panels/` | 运行时构建 | 冠数 / 胜负 / 再来一局 / 回主菜单 | 待实现（agent-08） |
| `AppFlow` | `Module/Flow/AppFlow.cs` | 由 `Bootstrap` 建（`AppFlow.EnsureCreated`） | 站点状态机 + 面板/场景编排；**对外契约** | ✅ 已实现（agent-05） |
| `SettingsManager` | `Module/Settings/SettingsManager.cs` | Flow 持有 | 音量 / 画质 / 全屏，落 `Game.Setting` | ✅ 已实现（agent-05） |
| `DeckManager` | `Module/Deck/DeckManager.cs` | `DeckModuleHost` 启动钩子 | 卡池拉取 / 卡组读存 / 8 张校验 | ✅ 已实现（agent-05，越界产出，见台账 NOTE） |
| `RoomManager` | `Module/Room/RoomManager.cs` | `RoomModuleHost` 启动钩子（**不是** Flow 持有） | 房间 C2S 门面 + 房间推送接收；`self_player_id` 判房主 | ✅ 已实现（agent-06） |
| `BattleManager` | `Module/Battle/BattleManager.cs` | `BattleModuleHost` 启动钩子 | 对局推送接收 + 出牌/投降/同步上行 | ✅ 已实现（agent-07a） |
| `BattleViewRoot` | `View/BattleViewRoot.cs` | `[RuntimeInitializeOnLoadMethod]` **自安装** | 对局表现层根：订阅 `Events.Battle.*`、**坐标插值的归属方**、驱动竞技场/单位/落点指示 | ✅ 已实现（agent-07b） |
| `ArenaView` | `View/ArenaView.cs` | 由 `BattleViewRoot` 建 | 竞技场底图（**分段定标**）+ 6 座塔 | ✅ 已实现（agent-07b） |
| `UnitView` | `View/UnitView.cs` | 由 `BattleViewRoot` 池化 | 逐帧精灵 + `anim` 档 + `WorldHpBar` | ✅ 已实现（agent-07b） |
| `PlacementIndicator` | `View/PlacementIndicator.cs` | 由 `BattleViewRoot` 建 | 落点圈（合法绿 / 非法红）+ **部署合法性唯一实现** | ✅ 已实现（agent-07b，主 agent 补调用方） |

> ⚠️ `BattleManager.Sample(...)` / `BattleUnitSample` / `BattleTowerSample` **目前无调用方**
> （插值改由 `View/BattleViewRoot` 自己做；理由见 `docs/client-architecture.md` §6.2）。**⛔ 不要新增调用方。**

## 协议字段（两端同一批改）

| 字段 | 位置 | 为什么 |
| --- | --- | --- |
| `RoomStateNotify.self_player_id` | `server/game/def/push.go` + `client/.../Def/ProtoDef.cs` | **逐接收者**字段（服务端按推送目标逐个填）。协议原先没有"我自己是谁"的字段，客户端只能复制服务端 `playerIDPrefix`（`"p_"`）反推，前缀一漂移就"认不出自己"（表现＝房主按钮永远是灰的）。客户端现在优先用它，派生规则只作退化兜底 |

## 客户端流程 / 菜单站点（App Flow）

> ★ **场景只有 2 个**（架构契约 §0/D3）：`Main`（启动→登录→创角→主菜单→房间）与 `Battle01`（对局）。
> ⛔ 站内切换**不切场景**，用 `Game.UI.CloseAll()` + `Game.UI.Open<T>()`。

| 站点 | 状态机状态（`Core/Stations.cs`） | 面板 | 场景 | 说明 |
| --- | --- | --- | --- | --- |
| 启动画面 | `Stations.Boot` | `BootPanel` | `Main` | Logo + 版权字 + 底部 `by clover-engine` |
| 登录 | `Stations.Login` | `LoginPanel` / `RegisterPanel` | `Main` | 账号服换 token → `Call<ELoginReply>(EMsg.Login)` |
| 创角 | `Stations.Nickname` | `NicknamePanel` | `Main` | 设置昵称 |
| 主菜单 | `Stations.MainMenu` | `MainMenuPanel` | `Main` | 卡组 / 房间列表 / 人机 / 设置 / 退出 |
| 卡组编辑 | `MainMenu` 子面板 | `DeckEditPanel` | `Main` | 8/60 选卡 |
| 房间列表 | `MainMenu` 子面板 | `RoomListPanel` | `Main` | 列表 + 创建 + 加入 |
| 房间 | `Stations.Room` | `RoomPanel` | `Main` | 成员 / 准备 / AI / 开打 |
| 读条 | `Stations.Loading` | `LoadingPanel` | 切换中 | `Game.Scene.Load` 真进度 |
| 对局 | `Stations.Battle` | `HudPanel` | `Battle01` | 竞技场 + 拖放出牌 |
| 暂停 | `Stations.Pause` | `PausePanel` | `Battle01` | 对战不真暂停，只覆盖菜单 + 投降 |
| 结算 | `Battle` 子面板 | `ResultPanel` | `Battle01` | 冠数 / 胜负 / 再来一局 / 回主菜单 |

## 客户端 Core（常量与唯一处）

| 文件 | 职责 | 状态 |
| --- | --- | --- |
| `Core/ClientConfig.cs` | `Cfg` 配置入口（读 `Assets/Configs/config.json`） | ✅ 已实现（冻结） |
| `Core/GameConst.cs` | 几何 / 单位 / 时长**唯一换算处**（与服务端 `core/units.go` 同值） | ✅ 已实现 |
| `Core/Stations.cs` | 7 个站点名（= `Game.Fsm` 状态名） | ✅ 已实现 |
| `Core/Events.cs` | 事件名**唯一定义处**（Flow / Settings / Room / Battle / Deck 五段） | ✅ 已实现 |
| `Core/ResPaths.cs` | 资源路径**唯一定义处** | ✅ 已实现 |
| `Core/SettingsSnapshot.cs` | 设置面板的只读参数载体（面板不许引 `CR.Module`） | ✅ 已实现 |
| `App/Bootstrap.cs` | 唯一组装点（**112 行** ≤ 200） | ✅ 已实现 |
| `Assets/Editor/SceneBuilder.cs` | 生成 `Main.unity`/`Battle01.unity` + 写 `EditorBuildSettings.scenes` | ✅ 已实现（**待编辑器打开后执行**） |

## 素材管线

| 项 | 内容 |
| --- | --- |
| 脚本 | `<根>/.ai-tmp/hosts/copy_assets.py`（幂等；源 = `<根>/原版资源/cr-assets-png/assets/sc/`，**只读**） |
| 落点 | `client/Assets/Resources/Sprites/{Units,Buildings,Towers,Arenas,Cards,Ui}/` |
| 规模 | **53 个目录 / 14000 文件 / 132.2 MB**（⛔ 未全拷 `sc/` 的 21059 张） |
| 帧名规范化 | `frame_NNN.png`（源序号补零位数不统一，统一后 `ResPaths` 才能一条规则拼路径） |
| 已知缺项 | ① 原版**字体**文件不在解包素材内 ⇒ 用 `UIFactory.DefaultFont()`；② 卡面/UI 素材是**匿名编号帧**、无 key 映射 ⇒ 需要目视认图后登记 |

## 通用函数（后续代码优先复用）

| 函数 / 类 | 位置 | 用途 | 状态 |
| --- | --- | --- | --- |
| `Core.MilliToTile` / `TileToMilli` | `core/units.go` | 格 ↔ milli-tile | 待实现 |
| `Core.Arena.CanDeploy` | `core/arena.go` | 落点合法性（含摧毁公主塔后的口袋区） | 待实现 |
| `Events`（事件名常量） | `client/Assets/Scripts/Core/Events.cs` | 禁止裸事件名 | 待实现 |
| `ResPaths`（资源路径常量） | `client/Assets/Scripts/Core/ResPaths.cs` | 禁止散落资源路径字面量 | 待实现 |

## 配表登记

| 源表 | 页签名 | 用途 | 主键 | 行数 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `card_cs.txt` | `card_cs` | 60 张卡登记 | `id` | 60 | ✅ 已生成（`table.Default.Card`） |
| `unit_cs.txt` | `unit_cs` | 战斗实体（部队 + 建筑 + 塔 + 投射物） | `id` | 90 | ✅ 已生成（`table.Default.Unit`） |
| `spell_cs.txt` | `spell_cs` | 10 张法术参数 | `id` | 10 | ✅ 已生成（`table.Default.Spell`） |

> ⚠️ **表名必须英文**（`card_cs` / `unit_cs` / `spell_cs`）：Go 只把「首字符为大写 ASCII 字母」视为导出，
> 中文表名会让生成物在 `table` 包外**完全不可用**（详见 `constraints.md`）。
> tsv 落在 `server/game/table/tsv/{card,unit,spell}.tsv`；打表用 `tools/table.ps1 -Force`（不带 `-Force` 不覆盖已有 `-pack.xlsx`）。
