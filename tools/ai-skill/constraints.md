# 约束与风险（clover-project-cr）

> 只记两类：**改不掉的固有约束**（引擎 / 平台行为）与**静默失败风险**（不报错但结果错）。

## 引擎约束

| 约束 / 风险 | 现象 | 结论 |
| --- | --- | --- |
| 业务消息号必须 ≥ 10001 | 启动期 `app.Game.OnMsg` 校验，误用直接 **panic** | 本项目的 C2S 从 1000101 起，安全 |
| **回包帧 `msgID` 恒为 0** | 若照抄历史文档定义 `2000101+` 回包常量，会让人以为回包有协议号；实际零引用 | ⛔ 回包**不定义消息号**，只定义结构体；客户端用 `Call<T>(请求号, ...)` 配对 |
| `event.Ctx` **不提供** `Session()` | 业务取不到会话信息 | 用 `c.Account()` / `c.PlayerID()` / `c.ConnID()` |
| `Game` 上**没有** `Logger` 方法 | `g.Logger.Info(...)` 编译不过 | 服务端日志用**包级** `logger.Infof/Warnf/Errorf`（`pkg/foundation/logger`） |
| `BindMsg` 不叫 `Bind` | 写成 `c.Bind(&req)` 编译不过 | `c.BindMsg(&req)` |
| handler 签名是 `func(c event.Ctx) error` | 写成 `*event.Ctx` 编译不过 | 值接口；import `pkg/transport/event` |
| `g.Timer` 是 `*TimeEvent` | 直接当 `timer.Scheduler` 用编译不过 | `g.Timer.Every(name, d, task)` / `After` / `ByTime` / `TimerGroup(scope)` / `StopTimerGroup(scope)` |
| `timer` 的 `OnTimer` **只在 `*Group` 上** | `g.Timer.OnTimer` 不存在 | `g.Timer.TimerGroup(scope).OnTimer(name, when, task)` |
| 断线只清 `StopTimerGroup(owner)` 一个 scope | 用 `"player:"+id` 当 scope 的任务**不会**被自动清理 | 要随掉线清理 ⇒ scope **必须等于**连接级 owner |
| `room` 包**没有**顶层 `Room` / `RoomManager` / `MaxPlayers` 类型 | 按名字猜会编译不过 | `Room` 真身是 `frame.Room`；`MaxPlayers` 是 `frame.Config` 字段 + `frame.WithMaxPlayers` |
| `room.Module` 的帧推 **默认静默失效** | `PushMessageID` 默认 0 ⇒ `broadcast()` 直接 return，帧在推进而客户端永远收不到 | 本项目**不依赖**引擎帧推（快照由业务 `g.PushToPlayer` 自己推）；若启用必须 `frame.WithPushMessageID(业务号)` |
| `frame.InputTimeoutTicks` 默认 90 | 无人提交输入时**帧自锁不推进** | 同上，本项目不用锁步输入收集 |
| 官方数据 `damage_per_level` 与 `dps_per_level` 不自洽 | 照抄任一都可能与另一处对不上 | 取与 `hit_speed` 自洽的一组（参考规格 §4.1），差异登记为 D3 |

## 客户端约束

| 约束 / 风险 | 现象 | 结论 |
| --- | --- | --- |
| `Game.Launch` **不联网** | 以为 Launch 就连上了 | 联机必须显式 `CloverNet.Init(addr, udpAddr)` |
| `CloverNet.Init` / `CloverRes.Init` / `CloverData.InitDataTable` 必须在 `Launch` **之后** | 顺序错了 `Game.Net/Res` 恒为 null | 严格按顺序初始化 |
| `Game.Res` 不会随包自动挂载 | 漏 `CloverRes.Init` ⇒ `Game.Res` 恒 null，**模型永远加载不出来且在加载点抛 NRE 打断进图** | 必须显式 `CloverRes.Init(root)` |
| `CloverInput.Init()` 必须在**建 UI 之前** | 晚了 ⇒ 按钮全点不动（没有 EventSystem） | 在 `Game.Launch` 后立刻调 |
| `Game.Timer` **没有** `Once` | 编译不过 | 用 `After(delay, cb)` |
| `Game.Input` **没有** `PointerPosition` | 编译不过 | 用 `MousePosition`（`Vector3`）/ `MouseDelta`（`Vector2`） |
| `Game.Net.Call<T>` **只有泛型重载** | 无泛型 `Call` | `await Game.Net.Call<XxxReply>(msgID, req)` |
| 面板预制体路径 = `Resources/UI/{类名}` | 名字不一致 ⇒ 打开是空白 / 报"找不到预制体" | 预制体名必须 = `UIPanel.PanelName`（默认类名） |
| `net` 回包超时默认 10s | 慢操作被误判超时 | 长流程要显式处理 `TimeoutException` |
| `Game.Net` 在 `Init` 前为 null | 直接调会 NRE | 先判 `Game.IsRunning` / 用 `?.` |
| 引擎包**不含任何 `.unity` / `.prefab`** | 找不到现成场景可抄 | 场景与预制体由 **Editor 脚本生成**（`Assets/Editor/`），见全局 `reference/unity-cli.md` §7 |
| 客户端连的是**裸 TCP 网关口 8002** | 填 8001（WS 口）⇒ 「连上 → 秒断 → 被踢 → 全部 Call 超时」 | `config.json` 的 `server.addr` = `gateway.listen_tcp`（8002）；`tls` 必须与服务端 `gateway.tcp_tls_disabled` **相反**（服务端默认 false ⇒ 客户端 `true`） |
| `server.tls=true` 但服务端未配证书 | 连不上 | 本项目 `server.yaml` 配 `tls_cert`/`tls_key`（`mkcert` 生成）或客户端设 `tls=false`；**二者必须相反** |

## 平台 / 环境

| 约束 / 风险 | 现象 | 结论 |
| --- | --- | --- |
| **360 主动防御内核服务（`ZhuDongFangYu.exe`）会拦 Unity 授权通道** | `Connection to channel LicenseClient-XXX refused` + 每轮重连烧 60s ⇒ 创建工程静默卡死 | 建 `client/` 前必须确认该服务已停（托盘「退出」**不会**停它） |
| `.ps1` 无 BOM 且含中文 ⇒ PS 5.1 按 ANSI 解析 | 报 `Unexpected token`，错误行看着全是 ASCII | `.ps1` 一律 **ASCII-only**，非 ASCII 路径用**码点拼** |
| `ConvertFrom-Json` 读大 JSON 会失败 | `game_modes.json` 等报 "传入的对象无效" | 用 **Python** 解析官方 JSON（本机 Python 3.12 可用） |
| 官方 JSON 用 UTF-8；`cards_i18n.json` 含乱码字面量 | 直接读会拿到乱码 | 读原文按 UTF-8；中文名以文件为准，必要时人工核对 |

## 已定案的口径（不要重新讨论）

| 项 | 定案 | 依据 |
| --- | --- | --- |
| **配表取值下标** | `值 = <字段>_per_level[ rarities[稀有度].tournament_level_index ]`；`rarities.json` 给的是 **Common 10 / Rare 8 / Epic 5 / Legendary 2 / Champion 0** | `原版资源/cr-api-data/docs/json/rarities.json`；已用 Knight(Common,10) 与 P.E.K.K.A(Epic,5) 的 anchors 手工实测值交叉验证 |
| 塔血量 | 取 `cards_stats_building.json` 的 `hitpoints_per_level[10]` = 公主塔 **3584** / 国王塔 **6144** | 同上；与 `hit_speed` 自洽 |
| 塔单发伤害 | `cards_stats_projectile.json` 的 `damage_per_level[10]` = **128 / 128** | 同上 |
| 配表访问器名 | ⛔ **不要用中文标识符**：Go 只把「首字符为大写 ASCII 字母」视为导出，中文**一律未导出** ⇒ `table.Default.卡牌` 在 `table` 包外**根本写不出来**（`cannot refer to unexported field`），反射也读不到（`reflect.Value.Call using value obtained using unexported field`）。**已定案并落地为英文表名**：源表 `card_cs.txt` / `unit_cs.txt` / `spell_cs.txt`（页签名同）+ tsv `server/game/table/tsv/{card,unit,spell}.tsv` ⇒ 访问器 **`table.Default.Card.Get(id)` / `table.Default.Unit.Get(id)` / `table.Default.Spell.Get(id)`** | agent-03 + agent-01b 实测：中文表名导致 `go build ./...` 在 `logic` 包报 5 处 `cannot refer to unexported field`；agent-01c 已改并验证全绿 |
| 配表 tsv 解析 | ⛔ **不要依赖生成器默认的 `encoding/csv` 解析**：它设了 `Comma='\t'` + `TrimLeadingSpace=true`，而 `unicode.IsSpace('\t')` 为真 ⇒ **每个空单元格被当前导空白吃掉，后续列集体左移且不报错**（实测 archers 行 37→35 列，`summon_key` 读成 `"0"`、`summon_n` 读成 100）。解析一律用**显式 `strings.Split(line, "\t")`** | agent-03 实测；agent-01c 已在生成物上层修掉 |
| **`nats.addr` 不能留空** | 空值**不是"不启用"那么无害**：引擎 `Core.PushToPlayer` 第一行 `if co.notifyPub == nil \|\| co.notifySubject == "" { return nil }` ⇒ **所有 `Push*` 静默返回 nil**（不报错、不打日志）。后果：`PushRoomList/State/BattleStart/Snapshot/Event/End` 全发不出去，**客户端进对局必然黑屏** | agent-03 端到端实测：补 `nats.addr: "127.0.0.1:4222"` 前 0 条推送、补后全链通。已落进 `server/configs/all/server.yaml` |
| `room.Config.MasterCaller` | 引擎 **S2 修复后**可以传 `g`（需同时在 master 角色挂 `room.NewMasterHandlers(mg)`）；此前只能传 `nil`（否则建房 handler 卡死） | 服务端引擎 `修复记录.md` S2；`clover-server-engine/internal/app/master_room_handlers_test.go` 的 `TestMasterRoomHandlerRoundTripOverTCP` 给出判据 |
| 断线回调依赖引擎 S1 | `g.OnDisconnect(h)` 此前**永不触发**（网关清理路径越界 panic 把派发整段吞了）。引擎 S1 已修；`g.OnDisconnect` 现在真会被调用 | 服务端引擎 `修复记录.md` S1；回归用例 `session_cleanup_test.go` 三条 |
| `auth` 不配证书即起服失败 | 账号服 HTTP 默认要求 TLS；本机联调必须 `auth.insecure_plaintext: true` 或配证书 | agent-01 实测（报「默认要求 TLS」）；已落进 `server/configs/all/server.yaml` |
| 客户端 `Configs/config.json` | 因 `gateway.tcp_tls_disabled: true` ⇒ 客户端 **`tls: false`**、`auth_addr: "http://127.0.0.1:8051"`、`addr: "127.0.0.1:8002"`（网关 TCP 口，⛔ 不是 8001 WS 口） | 同上（"与服务端相反"原则在本项目取反） |
| 非卡牌实体的 `key` | = **官方实体名逐字**（`Skeleton` / `PrincessTower` / `TowerPrincessProjectile` …） | agent-01 已断言「所有 key 都能解析到本表某行」 |
| `server.yaml` 的强制补充 | `auth.insecure_plaintext: true`（不配证书时账号服 HTTP 直接**起服失败**）；`gateway.tls_cert/tls_key` 指向 `certs/server.pem`；`tcp_tls_disabled: true` | agent-01 实测（报「默认要求 TLS」+ 两条 WT TLSConfig ERROR） |
| 客户端 `Configs/config.json` | 因上一条 ⇒ **`tls: false`**、`auth_addr: "http://127.0.0.1:8051"` | 同上（与服务端 `tcp_tls_disabled` 相反的原则在此取反） |
| 打表重跑 | 改完源表 txt **必须带 `-Force`** 重打包，否则 tsv 仍是旧值 | agent-01 实测踩过 |
| 快照里塔的表达 | `Snapshot.Entities` **只放部队与建筑**；6 座塔由 `TowersA/TowersB` 报告（塔位是固定几何，客户端从 `core/arena.go` 常量取） | agent-02 裁决，已实现 |
| 加时赛 | **不做 sudden death**；180 s 平冠 ⇒ 加时到 300 s，仍平冠比塔血万分比 | 参考规格 §3 |

## 未决问题

| 问题 | 待谁定 | 时间 |
| --- | --- | --- |
| **360 主动防御内核服务未停**（`ZhuDongFangYu.exe`）⇒ **`client/` 无法创建，客户端全线阻塞** | **用户** | 2026-09-20 |
| `verify.ps1` 第 13 条曾全量误报（台账时间手填 01:05 晚于产出 mtime）→ 已改为真实派活时刻 | 主 agent（已修） | 2026-09-20 |
| `verify.ps1` 第 14 条在项目根跑 `go build`（模块根在 `server/`）→ 已改 `go -C server`，并避免原生 stderr 中断脚本 | 主 agent（已修） | 2026-09-20 |
| 官方数据里契约装不下的字段（`multiple_projectiles` / `charge_range` / `minimum_range` / `can_deploy_on_enemy_side` / `spawn_start_time`）是否扩 schema | 主 agent（暂不扩：60 张卡的核心行为已覆盖；`Miner` 可落敌方半场是唯一有玩法影响的，登记为待办） | 2026-09-20 |
| 服务端引擎 **S1 还有两条同型尾巴未修**：`session.go:798`（换绑解绑旧 owner 索引）、`gwcore.go:965`（`Kick` 摘索引）——同 owner 只有一条连接时同样 `[-1]` 越界 | 待派（修法与 S1 逐字相同） | 2026-09-20 |
| 端到端测试在 MySQL/Redis 里留了 `cr_a_*` / `cr_b_*` / `cr_c_*` / `cr_mid_*` 测试账号 | 收尾时清理或保留（无实害） | 2026-09-20 |
