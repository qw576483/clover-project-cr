# agent-04：服务端引擎缺陷修复（**改的是引擎仓库**）

> 走的流程是全局 skill 的 **`patterns/engine-fix.md`**（"改引擎 SOP"）：
> ① 先写**最小复现**证明是 bug → ② **最小修复** → ③ **修完自证**（引擎全量 build/vet + 复现用例修复前后对照 + 业务端到端复跑）
> → ④ 写进**引擎仓库**的 `修复记录.md`（**不是**业务项目里）。

## 0. 技能（开工必做）

- **必读**：`~/.codebuddy/skills/ai-skill/patterns/engine-fix.md`（全局 skill 的改引擎 SOP，逐条照做）
- 项目级：`<项目根>/tools/ai-skill/constraints.md`（本项目依赖了哪些引擎行为）
- ⛔ 不许改任何 skill（发现问题写进回报）

⛔ **红线**：只许读本项目、`clover-server-engine`、`clover-doc`、skill。
工作区里**其它** `clover-project-*` **一律不许读、不许 grep、不许照抄**。

## 1. 两个缺陷（由 agent-03 端到端实测发现，**需要你先复现**）

### 缺陷 1（★ 必做）：`gwcore` 清理切片越界 panic ⇒ 断线回调永不触发

**现象**：客户端断开连接后，服务端 **0 条**业务断线日志，stderr 出现 3 条同款 panic；
`g.OnDisconnect(...)` 注册的回调**从不触发**。

**agent-03 的定位**（你必须自己复核）：
`clover-server-engine/internal/transport/gateway/gwcore/session.go` 附近：
- 约 `:1280` 的「清尾」代码写在 `append(sessions[:i], sessions[i+1:]...)` **之后** ——
  当被删元素恰是**最后一个**时，`sessions[i+1:]` 为空、`sessions` 变短，
  随后对 `sessions[i]`（或 `[-1]`）的访问**越界** ⇒ `index out of range [-1]`
- 派发断线事件的代码在约 `:1340`，**在 panic 之后** ⇒ 永远执行不到

**要求**：
1. 先写**最小复现**（跑起来就 panic；可用路由/会话相关的单测或一个最小起服+断连脚本）。
2. **最小修复**（先记住旧长度再 append，或截断前先 `sessions[i] = nil`）—— ⛔ 不许顺手重构、不许改公开签名。
3. 补一条**回归用例**（`*_test.go`，注释写清"复现什么缺陷、修复前什么现象、修复后什么断言"），
   跑法带超时：`go test ./internal/transport/gateway/... -run <用例> -v -timeout 90s`。
4. 复核同文件里是否还有**同类**写法（只在回报里列出，不要一起改 —— 最小修复原则）。

### 缺陷 2（次要，也要修）：`room.NewMasterHandlers` 注册 panic

**现象**：`room.NewMasterHandlers(mg).Register()` 直接 panic：
```
app: business message id must be > 10000; got 6001
```
门面守卫（`app.Game.OnMsg` 的业务号校验）拒绝了**引擎自己内建**的 master↔game 房间消息号 `EMasterRoomRegister=6001`。

**要求**：
1. 先写最小复现（注册即 panic）。
2. **最小修复**：让引擎内建消息号能通过内部注册路径（`Logic.InternalOnMsg` 之类），
   ⛔ **不要**放宽"业务消息号必须 > 10000"这条对**业务**的硬约束（那是给用户的保护）。
3. 回归用例同缺陷 1。
4. 回报里说明：修好后业务侧 `room.Config.MasterCaller` 应该传什么
   （agent-03 目前传 `nil` 绕开，单进程不受影响；修好后可换回 `g` 以启用跨节点接管）。

## 2. 记录（★ 落点只许是引擎仓库）

写进 **`c:\Work\Server\f-v2\clover-server-engine\修复记录.md`**（没有就新建）。

⛔ **不要复用客户端引擎的 E 编号体系**（`E1=Timer unscaled / E2=UIFactory / E3=TryGet`）——
服务端仓库用它自己的编号（建议 **S1 / S2**，并在文件里写明"本文件是服务端引擎的编号体系，与客户端 E 号无关"）。

每条必须写全 `engine-fix.md` §5 的六段：
`### 缺陷本质` / `### 最小复现（修复前失败）` / `### 修复后自证（带对照组）` /
`### 已知边界 / 精度限制` / `### 用法 + 首个消费方`，外加表头的 `日期 / 文件 / 改动表`。

## 3. 端到端复跑（引擎改了，业务必须重验）

- 引擎：`cd clover-server-engine && go build ./... && go vet ./...` + 两条复现用例（修复前红 → 修复后绿）
- 业务：`cd <项目根>/server && go build ./... && go vet ./...`；起服
  （`go run . -config configs/all`，环境先 `clover-server-tools/windows-env/core/env.exe start`）
- **真验证断线**：起服 + 用 agent-03 留下的端到端驱动建房开打 →
  中途**直接关掉一方连接** →
  - 服务端**不再出现 panic**
  - 业务侧断线回调**真的被调用**（贴日志行）
  - 该玩家的房间座位被正确标记/清理（贴日志行）
  如果业务侧此刻还没有接好"断线即判负/保留座位"的动作，**只验证回调被调到**即可（那是 agent-03 的簿记，已离线断言）。

## 4. 允许改动的范围（⛔ 严格）

- `c:\Work\Server\f-v2\clover-server-engine\`（引擎仓库：源码 + 回归用例 + `修复记录.md`）
- `<项目根>/.ai-tmp/`（复现脚本 / 驱动 / 日志）

⛔ **不许**改 `<项目根>` 下的任何业务代码、配置、文档、skill、`tools/verify.ps1`
（`server/configs/` 我刚修过 `nats.addr`，别动）。
⛔ 不许动 `clover-client-unity-engine`、`clover-doc`（若确实需要同步文档，**只在回报里说明该改哪儿**）。
⛔ 不许再派生任何子 agent。
⛔ 不许写 `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`。

## 5. 验收标准（做完把原始输出贴进回报）

- [ ] `cd clover-server-engine && go build ./... && go vet ./...` 全绿
- [ ] 两条回归用例：**修复前失败/panic 的原始输出** + **修复后 PASS 的原始输出**（各贴一次，缺一不可）
- [ ] `clover-server-engine/修复记录.md` 已建/已追加，两条各含六段结构（贴文件内容）
- [ ] `cd <项目根>/server && go build ./... && go vet ./...` 全绿；`go test ./game/core/... ./game/logic/... -count=1` 全绿
- [ ] 起服 + 端到端驱动复跑：**不再有 panic**、**断线回调被调用**（贴日志原行）
- [ ] 明确回答：缺陷 2 修好后，`room.Config.MasterCaller` 是否可以换回 `g`（给判据，不要猜）
- [ ] 回报里写明「本轮修了服务端引擎 N 处（S1..Sn）」，供我写进交付说明

## 6. 回报格式

```
产出物：<绝对路径清单>
自检：<命令 + 原始输出：引擎 build/vet / 复现用例修复前后对照 / 修复记录.md 全文 / 业务 build+test / 端到端断线日志>
未决：无 / <具体条目>
```
