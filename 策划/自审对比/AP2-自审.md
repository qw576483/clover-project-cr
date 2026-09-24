# AP2 自审（逐项 一致 / 不一致 / 原版无此界面）

- 我们的（当前构建、Play 实机、1080×1920、合成 overlay 后采）：实机截图 ×5
- 同机位并排：同机位并排图 ×5；一次看全 5 个面板的联络图 ×1
- A 侧基线：`01_启动页_Logo_1320x2868.jpg` / `17_加载页_640x955.png`；
  登录 / 注册 / 昵称 **A 本体没有这三个界面**（CR 用 Supercell ID）⇒ 形制参照 `24_设置_499x1080.jpg`
- 数值依据（L3 运行时 dump，不是叙述）：每个节点 size / `SPRITE=<帧名>` / `type` / 文本色。
  + 逐点像素取样（见各行"实测"）

## 启动页（基线 01）

| # | 部件 | 判定 | 依据（实测） |
|---|---|---|---|
| 1 | 主视觉底 | **不一致（差在哪：官方主视觉的游戏版本不同）** | 基线 01 = 2022 版构图（抛冠）；我们是 `loading_out` **024**（APK 内唯一整幅主视觉，dump `BgArt=loading_bg_0`）。⛔ 不可改：网页截图是 JPEG 二次品、不许当素材 ⇒ 登记 `AP2-允许差异.md` **D-AP2-1** |
| 2 | LOGO 图元 | **一致** | 基线顶部 LOGO 与 `loading_out` **028** 同形；dump `BOOT/Logo SPRITE=frame_028_0 size=715x308 at=0,-96`；像素取样 金 (240,205,107) / 蓝盾 (21,75,160) = 原版 LOGO 的两支主色 |
| 3 | LOGO 横宽 / 距顶 | **一致** | 基线 874px@1320 → 715@1080（比例 66.2% ↔ 66.2%）、y=117@2868 → 96；我们的 715 / 96（dump 同上） |
| 4 | 版权两行 + 底部署名 | **原版无此两行**（本项目新增） | A 启动页没有署名行；底部署名 dump `TXT='by clover-engine'` 逐字（`engine-credit` 检查 PASS） |

## 加载页（基线 17）

| # | 部件 | 判定 | 依据（实测） |
|---|---|---|---|
| 1 | 主视觉底 | **不一致（差在哪：同 D-AP2-1 的版本差）** | 同上 |
| 2 | 进度条**轨道** | **一致**（原版图元） | dump `LOAD/Track SPRITE=Rounded:frame_014_0 type=Sliced size=900x64`；像素 (92,100,120) 板岩 = `ui_out` 014。⚠️ 基线 17 图上**看不到**进度条 ⇒ 轨道位置/宽高未量到（D-AP2-3） |
| 3 | 进度条**填充** | **一致**（原版加载条） | dump 有 `Fill`；像素 (58,228,73) 亮绿 = `loading_out` 015（原版加载读条）；带顶亮缘 (141,217,130) |
| 4 | 百分比 / 提示文案 | **原版无此部件**（基线没有进度条，也就没有百分比） | dump `Percent TXT='62%' font=32` / `Hint font=24` |

## 登录（基线：**A 无此界面** ⇒ 逐部件对齐 A 同类件）

| # | 部件 | 判定 | 依据（实测） |
|---|---|---|---|
| 1 | 面板底 | **一致** | dump `LoginBox=Rounded:frame_014_0` + `LoginBoxBody=Rounded:frame_019_0`；像素 亮面 (220,232,240) ≈ 019 / 标题带 (100,108,124) ≈ AM2 在 `24_设置` 实测的 (99,104,123) |
| 2 | 标题 | **一致** | dump `Title font=48 outline=True` 白字压板岩带（基线「Settings」写法） |
| 3 | 输入框底 | **一致** | dump `AccountInput SPRITE=Rounded:frame_014_0 type=Sliced`（四角镜像九宫格）；像素 (100,108,124) = 原版板岩框 + 白字（基线字段行「API Token」语言） |
| 4 | 主按钮 | **一致** | dump `LoginButton=Rounded:frame_165_0 size=424x65`；像素 (44,152,255) 蓝 = 原版蓝按钮族（基线 7 处蓝 (104,171,249)） |
| 5 | 次按钮 | **一致** | dump `RegisterButton=Rounded:frame_014_0`；像素 (100,108,124) 板岩 = 基线灰按钮行 (103,106,121) |
| 6 | 状态行 | **一致** | dump `StatusBar=Rounded:frame_014_0`（板岩条 + 浅色字）= 原版"深色字段 + 亮字" |
| 7 | 标签 / 提示字色 | **一致** | dump `AccountLabel/Status/Hint col=0.16,0.18,0.22` = `TextOnLight` (42,46,56)（基线亮面标签最暗像素） |
| 8 | 界面本身 | **原版无此界面** | `策划/参考图/清单.md` §2「未取到：登录页(Supercell ID)」⇒ 本界面是新增，几何登记 D-AP2-2 |

## 注册（同登录骨架；**A 无此界面**）

与登录逐项同判：1~7 全 **一致**（dump `RegisterBox/RegisterBoxBody=014/019`、`AccountInput/PasswordInput=Rounded:frame_014_0`、
`SubmitButton=Rounded:frame_165_0`、`BackButton=Rounded:frame_014_0`），第 8 项 **原版无此界面**。

## 昵称 / 创角（**A 无此界面**）

| # | 部件 | 判定 | 依据（实测） |
|---|---|---|---|
| 1 | 面板底 | **一致** | dump `NicknameBox=014` + `NicknameBoxBody=019` |
| 2 | 标题 | **一致** | dump `Title font=48 outline=True`，压板岩带 |
| 3 | 输入框底 | **一致** | dump `NicknameInput=Rounded:frame_014_0` |
| 4 | 主按钮 | **一致** | dump `SubmitButton=Rounded:frame_165_0` |
| 5 | 状态行 | **一致** | dump `StatusBar=Rounded:frame_014_0` |
| 6 | 标签字色 | **一致** | dump `NicknameLabel col=0.16,0.18,0.22` = TextOnLight |
| 7 | 界面本身 | **原版无此界面** | 同上 |

## 计数（⛔ 无一条"基本一致 / 大致像"）

| 面板 | 一致 | 不一致 | 原版无此界面/部件 |
|---|---|---|---|
| 启动页 | 2 | **1**（主视觉版本，D-AP2-1） | 1（版权两行） |
| 加载页 | 2 | **1**（同上 D-AP2-1） | 1（百分比/提示） |
| 登录 | 7 | 0 | 1（整界面） |
| 注册 | 7 | 0 | 1（整界面） |
| 昵称 | 6 | 0 | 1（整界面） |
| **合计** | **24** | **2** | **5** |

两条 `不一致` 都是同一条、且**不可通过改代码消除**（网页截图不许当素材 ⇒ 只能取 APK 内的 024），
已登记 `AP2-允许差异.md` **D-AP2-1**，请主 agent 裁决是否并入 `策划/验收表.md` §3。

## 本片修掉的两个实机缺陷（首轮 Play 探针才发现）

1. **输入框底板仍是错的帧**：`CrUiStyle.Field` 内部会**异步**把底板换成 `ui_out` 015 的 `NineSlice` 版，
   与本片要的 014 四角镜像**抢同一张 Image** ⇒ 实测 dump 里是 `Sliced:frame_015_0`（单角件被按 border
   拉到四角 = 三角错）。改为直接调**同一引擎工厂** `UIFactory.CreateInputField` + 只 `Dress` 一次 ⇒
   复采后 dump 为 `Rounded:frame_014_0`。
2. **`LoadingPanel` 在 System 层（高于 Normal）**，采登录/注册/昵称时它仍盖在最上面 ⇒ 前三张截图
   采到的都是加载页那一帧（md5 全同）。驱动里加 `ui.Close<LoadingPanel>()` 后 5 张 md5 才各不相同
   （boot 36F0A565 / loading 2CFF934A / login 8EBB4AF8 / register 964C711B / nickname D3C29875）。
