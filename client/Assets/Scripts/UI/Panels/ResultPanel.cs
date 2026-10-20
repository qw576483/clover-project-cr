using CloverEngine;
using CR.Def;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 结算（`Battle` 站点的子面板，Popup 层，架构契约 §4）：胜负 / 冠数 / 原因 / 再来一局 / 回主菜单。
    ///
    /// <para>
    /// <b>数据来源</b>：`Events.Battle.Ended` 的 `BattleEndNotify`（`Def/ProtoDef.cs:105`：
    /// <c>win</c> / <c>draw</c> / <c>crowns_a</c> / <c>crowns_b</c> / <c>reason</c> /
    /// <c>hp_rate_a</c> / <c>hp_rate_b</c>）。打开本面板的是 `UI/BattleUiHost`（它订阅 `Ended`
    /// 并把结算体 + 本机队伍 + 房间号打包成 <see cref="PanelArgs"/> 递进来）。
    /// </para>
    /// <para>
    /// <b>依赖方向（契约 §1 硬线）</b>：本面板⛔**不许** `using CR.Module` ——
    /// 「再来一局」与「回主菜单」都只 `Emit` 事件：
    /// 人机 → `Events.Battle.AiBattleRequest`（`AppFlow.StartAiBattleAsync` 是它的处理者，
    /// `AppFlow.cs:296`）；房间 / 任意 → `Events.Battle.ReturnToMainMenuRequest`
    /// （`AppFlow.RequestReturnToMainMenu`，`AppFlow.cs:297`）。
    /// </para>
    /// <para>
    /// <b>⛔ 不假设自己永远是 A 方</b>：`crowns_*` / `hp_rate_*` 是**按 A/B 队**下发的，
    /// 本机队伍来自 `Events.Battle.Started` 的 `my_team`（由 `BattleUiHost` 记下并随
    /// <see cref="PanelArgs.MyTeam"/> 递进来）。未知队伍时如实标注"队伍未知"，⛔ 不许默认成 A。
    /// </para>
    /// <para>
    /// <b>为什么「房间对局不能再来一局」</b>：协议里**没有**任何"重开原房 / 重连回原房"的消息
    /// （`client/Assets/Scripts/Def/MsgDef.cs` 的 C2S 全表里没有这一条），且服务端的房间会在
    /// 没有真人座位时被回收（`server/game/logic/room.go:1170-1181` 的 `tidyRoom`）。
    /// 所以房间对局点「再来一局」= 回主菜单并提示重新开房，⛔ 不编一个假的"重连回原房"实现。
    /// </para>
    /// </summary>
    public sealed class ResultPanel : UIPanel
    {
        private const string Tag = "ResultPanel";

        // ───────────────────────── 竖版排版常量（G4 重排） ─────────────────────────
        //
        // ⚠️ **原版结算界面无基线**（`策划/参考图/清单.md` §2「结算(胜负/三冠) = 未取到」）⇒
        // 面板宽高、胜负字样/皇冠位置、按钮布局一律沿用项目里**有实测出处**的竖版口径
        // （`CrUiStyle` 的 A 侧实测值），不许填估计值冒充：
        //   · 面板宽 = `CrUiStyle.ContentW` = 1000（A 面板宽实测 711px@750 × 1.44）
        //   · 贴顶距 = `CrUiStyle.PanelTopOffset` = 167（A 面板顶边 y=116@1334）
        //   · 标题条高 = `CrUiStyle.TitleBarH` = 92、内缩 = `CrUiStyle.PanelPad` = 40
        // 只有**竖版重排本身**（上下排布、按钮由左右并排改上下两行）与 BoxH 是内容驱动。

        /// <summary>面板高（内容驱动：标题条 92 + 徽记 90 + 结局 100 + 冠数 80 + 提示 36 + 原因 48 + 细节 36 + 两按钮 221 + 间隔与留白）。</summary>
        private const float BoxH = 870f;

        /// <summary>全屏暗底色（结算页压住对局画面；原版结算页也是暗底 —— 具体色值本项目自定）。</summary>
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.62f);

        /// <summary>结局徽记显示宽（原版结算图元 `ui_battle_end_out/195` 原生 96×89）。</summary>
        private const float OutcomeIconW = 96f;

        /// <summary>冠数图标（原版结算王冠帧）显示宽 = 74。</summary>
        private const float CrownIconW = 74f;

        /// <summary>冠数上限（原版 3 冠制）。240 帧里没有 2 冠 / 3 冠的堆叠帧 ⇒ 同一张复制 N 次。</summary>
        private const int CrownMax = 3;

        /// <summary>内容框内缩（= `CrUiStyle.PanelPad`，A 侧实测口径）。</summary>
        private const float Pad = CrUiStyle.PanelPad;

        /// <summary>队伍取值（`ProtoDef.BattleStartNotify.my_team`：0=BLUE 1=RED）。</summary>
        private const int TeamBlue = 0;
        private const int TeamRed = 1;

        /// <summary>未知队伍（面板按此值显示"队伍未知"，⛔ 不默认成 A 方）。</summary>
        private const int TeamUnknown = -1;

        /// <summary>本机视角的胜负（<see cref="MySideVerdict"/> 的返回值）。</summary>
        private const int VerdictDraw = 0;
        private const int VerdictWin = 1;
        private const int VerdictLose = 2;

        /// <summary>结算面板是 Popup 层（架构契约 §4：结算 = Battle 站点的子面板，Popup）。</summary>
        public override UILayer Layer => UILayer.Popup;

        /// <summary>
        /// 打开参数（由 `UI/BattleUiHost` 构造）。
        /// ⛔ 面板不许引 `CR.Module` ⇒ 本机队伍 / 房间号 / 对局模式只能这样递进来。
        /// </summary>
        public sealed class PanelArgs
        {
            /// <summary>结算体（`Events.Battle.Ended` 的原件）。</summary>
            public BattleEndNotify Result;

            /// <summary>本机队伍：0=BLUE 1=RED；<see cref="TeamUnknown"/> = 还没收到 `Battle.Started`。</summary>
            public int MyTeam = TeamUnknown;

            /// <summary>true = 人机对局（可以「再来一局」）；false = 房间对局（要重新开房）。</summary>
            public bool AiBattle;

            /// <summary>本局房间号（显示用；未知时为空串）。</summary>
            public string RoomId = string.Empty;
        }

        private bool _built;
        private bool _unknownReasonWarned;
        private string _roomId = string.Empty;
        private bool _aiBattle;

        private Text _outcome;
        private Text _crowns;
        private Text _reason;
        private Text _detail;
        private Text _status;

        /// <summary>冠数图标（最多 <see cref="CrownMax"/> 个；N 冠 = 同一张原版王冠帧复制 N 次）。</summary>
        private Image[] _crownIcons;

        /// <summary>冠数图标当前用的是哪张原版帧（蓝 `ui_battle_end_out/027` / 红 `115`）—— 同一条路径不重复加载。</summary>
        private string _crownsPath = string.Empty;

        /// <summary>素材类非预期分支只报一次（缺图 Warn 不刷屏）。</summary>
        private bool _assetWarned;

        public override void OnOpen(object param)
        {
            var args = param as PanelArgs;
            if (args == null || args.Result == null)
            {
                // 非预期分支：有人绕过 `BattleUiHost` 直接 Open 了本面板（正常流程不会走）。
                // 留痕并把"没有结算数据"写在界面上 —— ⛔ 不许显示一个空白结果页。
                Game.Logger?.Error(Tag,
                    "打开时未收到 PanelArgs.Result（应由 BattleUiHost 从 Events.Battle.Ended 传入），结算内容无法显示");
                if (!_built)
                {
                    Build();
                    _built = true;
                }
                ShowNoData();
                return;
            }

            if (!_built)
            {
                Build();
                _built = true;
            }

            _roomId = args.RoomId ?? string.Empty;
            _aiBattle = args.AiBattle;
            _unknownReasonWarned = false;

            var r = args.Result;
            Game.Logger?.Info(Tag,
                $"结算面板：my_team={args.MyTeam} win={r.win} draw={r.draw} crowns={r.crowns_a}:{r.crowns_b} " +
                $"reason={r.reason} hp_rate={r.hp_rate_a}:{r.hp_rate_b} ai={args.AiBattle} room={_roomId}");

            RefreshOutcome(r, args.MyTeam);
            RefreshCrowns(r, args.MyTeam);
            RefreshReason(r, args.MyTeam);
            RefreshDetail(r, args.MyTeam);
            SetStatus(string.Empty, CrUiStyle.TextOnLightDim);
        }

        /// <summary>
        /// 关闭钩子。
        /// <para>
        /// <b>为什么这里没有 `Unsubscribe()`</b>：本面板**不订阅任何事件** —— 它是一份**静态**结果
        /// （数据全部由 `BattleUiHost` 在 `Events.Battle.Ended` 那一刻随 <see cref="PanelArgs"/> 递进来，
        /// 打开后不会再有新的结算数据），所以没有任何需要配对的 `Off`，也就不存在订阅泄漏。
        /// 「OnOpen 挂 / OnClose 摘」这条硬规则在这里是**空集成立**，不是漏写：⛔ 不为了凑形式
        /// 而订阅一条本面板用不到的事件（那才会制造一个真正没人注销的订阅）。
        /// </para>
        /// </summary>
        public override void OnClose()
        {
            CrUiFx.PanelOut(transform);
        }

        // ───────────────────────── 视觉树（D1：OnOpen 里用 UIFactory 自建） ─────────────────────────

        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);

            // 全屏暗底：结算页要压住对局画面（Popup 层），否则 HUD 与场上的东西会跟结算内容抢注意力。
            var dim = UIFactory.CreatePanel("Dim", root, DimColor, true);
            UIFactory.Stretch(dim.rectTransform);

            // 竖版面板：宽 = ContentW(1000)、高 = BoxH（内容驱动）。
            // 面板底 = `SettingsPopup`（`ui_out` 014 板岩外框 + 019 亮灰蓝面），标题 = `BandTitle`
            //   （板岩带白字黑描边）—— 与基线 `24_设置_499x1080.jpg` 实测一致（亮面 (229,236,242) /
            //   板岩带 (99,104,123)），也与 `RoomList` / `Room` / `DeckEdit` 同一口径。
            //   ⛔ 不用 `ContentPanel`（`ui_out/806` 米色纸）与 `TitleBar`（`ui_out/069` 棕木条）：
            //   这两张与原版功能面板的色相都不同。
            var box = CrUiStyle.SettingsPopup("ResultBox", root, BoxH, CrUiStyle.ContentW);
            var c = box.rectTransform;

            // 标题（板岩带上的居中白字黑描边）。
            CrUiStyle.BandTitle("Title", box, "对局结束", CrUiStyle.ContentW);

            // 结局徽记：原版结算图元（金色奖杯 + 橄榄枝）—— 原版结算界面未取到基线，位置/尺寸为本项目自定。
            CrUiStyle.AspectImage("OutcomeIcon", c, ResPaths.IconTrophyLaurel, OutcomeIconW,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(CrUiStyle.TitleBarH + 18f)), CrUiStyle.Accent);

            // 结局（胜 / 负 / 平）：面板里字号最大的一条，单次绘制 + 2px 黑描边。
            // 用 `CrUiStyle.Outlined`（白/彩字 + 黑描边）—— 亮灰蓝面板上原版所有文字都是
            //   白字黑描边（基线 `24_设置` / `12_主菜单`）；不加描边的白字压在亮面上等于看不见。
            // ⚠️ 头像框（原版结算页的双方头像外框 / 等级徽记）**不画**：本图集 240 帧里**没有**
            //    任何圆形/方形头像外框、也没有等级徽记（逐页看完 240 格 +
            //    `.sc` 20 个 export 名里没有 avatar / portrait / frame 语义）⇒ ⛔ 不自绘、⛔ 不拿纯色块顶替。
            //    消除条件 = 在 `ui_out` 或玩家档案 `.sc` 里找到这类图元。
            Game.Logger?.Info(Tag,
                "头像框：原版 ui_battle_end_out 240 帧内无头像外框/等级徽记（AR2 §3.3）⇒ 本面板不画该件（不自绘兜底）");

            _outcome = CrUiStyle.Outlined("Outcome", c, "对局结束", CrUiStyle.FontLogo,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -(CrUiStyle.TitleBarH + 130f)), new Vector2(CrUiStyle.ContentW, 100f),
                TextAnchor.MiddleCenter);

            // 冠数（按本机队伍换算）：原版皇冠图元 + 数字。
            var crownRow = UIFactory.CreateNode("CrownRow", c);
            UIFactory.Place(crownRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(CrUiStyle.TitleBarH + 240f)), new Vector2(CrUiStyle.ContentW - 2f * Pad, 80f));

            // 冠数图元 = **原版结算自己的王冠帧**（蓝 `ui_battle_end_out/027` / 红 `115`；
            //   原版是两段 80 帧动画，这里取**起始帧**）。
            // ⚠️ 原版 240 帧里**没有** 2 冠 / 3 冠的堆叠帧 ⇒ **N 冠 = 同一张复制 N 次**
            //   这里建满 `CrownMax` 个格子，
            //   由 `RefreshCrowns` 按实际冠数点亮前 N 个、并按本机队伍换蓝/红那张。
            _crownIcons = new Image[CrownMax];
            for (var i = 0; i < CrownMax; i++)
            {
                _crownIcons[i] = CrUiStyle.AspectImage("CrownIcon" + i, crownRow, ResPaths.CrownBlue, CrownIconW,
                    new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-10f - (CrownMax - 1 - i) * (CrownIconW + 6f), 0f), CrUiStyle.Accent);
                _crownIcons[i].gameObject.SetActive(false);
            }

            // 亮灰蓝面板上的字一律用暗色（亮面上放近白字等于看不见）⇒ `TextOnLight`。
            _crowns = CrUiStyle.ValueText("Crowns", crownRow, string.Empty, CrUiStyle.FontTitle,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(420f, 70f), TextAnchor.MiddleLeft);

            // ── 奖励区：协议没有奖励字段（`Def/ProtoDef.cs:118-125` 的 `BattleEndNotify` 只有
            //    win / draw / crowns_a / crowns_b / reason / hp_rate_a / hp_rate_b）⇒
            //    ⛔ 不画假奖励图标块、也⛔ 不留"无奖励数据"这类说明文字（原版这一区是奖励条目，缺数据时该区整块不出现）。
            //    出现条件 = 服务端在 `PushBattleEnd` 的 payload 里下发奖励条目。

            // ── 结算原因：中文短句（去掉"结算原因："前缀）──
            var reasonBar = CrUiStyle.NineSlice("ReasonBar", c, ResPaths.PanelFrameDark, CrUiStyle.BorderButtonDark,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -(CrUiStyle.TitleBarH + 400f)),
                new Vector2(CrUiStyle.ContentW - 2f * Pad, 80f), CrUiStyle.FieldBg, false);
            _reason = UIFactory.CreateText("Reason", reasonBar.rectTransform, string.Empty, CrUiStyle.FontBody,
                TextAnchor.MiddleCenter, CrUiStyle.TextColor);
            UIFactory.Stretch(_reason.rectTransform);

            // ── 细节行：塔血 + 对局模式，居中灰字，不带房间号 ──
            _detail = UIFactory.CreateLabel("Detail", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(Pad, -(CrUiStyle.TitleBarH + 500f)), new Vector2(CrUiStyle.ContentW - 2f * Pad, 40f),
                TextAnchor.MiddleCenter, CrUiStyle.TextOnLightDim);

            // ── 两个动作：竖版上下两行，原版蓝按钮 + 投影（`CrUiFx.ButtonFx`）──
            CrUiStyle.BlueButton("PlayAgainButton", c, "再来一局", new Vector2(Pad, -(CrUiStyle.TitleBarH + 570f)),
                new Vector2(CrUiStyle.ContentW - 2f * Pad, CrUiStyle.ButtonPrimaryH), OnPlayAgainClicked);

            CrUiStyle.BlueButton("MainMenuButton", c, "回主菜单",
                new Vector2(Pad, -(CrUiStyle.TitleBarH + 570f + CrUiStyle.ButtonPrimaryH + 16f)),
                new Vector2(CrUiStyle.ContentW - 2f * Pad, CrUiStyle.ButtonSecondaryH), OnMainMenuClicked);

            // 状态行：只在异常时出现（结算数据缺失等）。
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(Pad, -(CrUiStyle.TitleBarH + 810f)), new Vector2(CrUiStyle.ContentW - 2f * Pad, 40f),
                TextAnchor.UpperLeft, CrUiStyle.TextOnLightDim);

            CrUiFx.PanelIn(c);
        }

        // ───────────────────────── 两个动作 ─────────────────────────

        /// <summary>
        /// 再来一局：人机 → `Events.Battle.AiBattleRequest`（`AppFlow` 会发 `MsgAiBattleStart`：
        /// 服务端一条消息就完成"建房 + AI 占座 + 立刻开打"）；房间 → 回主菜单 + 提示重新开房。
        /// <para>
        /// **为什么先关自己**：切站点不一定发生（例如已经在 `Battle` 站点时 `AppFlow.GoTo` 会早退），
        /// 那样 `CloseAll` 不会跑，一个 Popup 结算页会一直压在 HUD 上。
        /// </para>
        /// </summary>
        private void OnPlayAgainClicked()
        {
            if (_aiBattle)
            {
                Game.Logger?.Info(Tag, "再来一局（人机）：关闭结算面板并请求 AppFlow 开新的人机对战");
                Game.UI?.Close<ResultPanel>();
                // ⚠️ 不在这里调 `AppFlow.Instance.RequestStartAiBattle()`：面板⛔不引 `CR.Module`，
                //    而这条事件的处理者就是它的**内部实现**（⛔ 不是 `RequestStartAiBattle` 本身，
                //    否则会自激成死循环 —— `AppFlow.cs:294-296` 写明了）。
                Game.Event?.Emit(Events.Battle.AiBattleRequest);
                return;
            }

            Game.Logger?.Info(Tag, "再来一局（房间对局）：协议没有重开原房，改为回主菜单并提示重新开房");
            Game.UI?.Toast("房间对局已结束，请重新开房（已回主菜单）");
            Game.UI?.Close<ResultPanel>();
            Game.Event?.Emit(Events.Battle.ReturnToMainMenuRequest);
        }

        /// <summary>
        /// 回主菜单。⚠️ **不需要二次确认**：本面板只在 `PushBattleEnd` 之后出现（对局已经结算完毕），
        /// 离开不会再改变任何结果 —— 与 `PausePanel` 里"对局中回主菜单 = 弃赛"是两回事。
        /// </summary>
        private void OnMainMenuClicked()
        {
            Game.Logger?.Info(Tag, "结算后回主菜单");
            // 反馈用 `Toast`（Top 层的常驻通用件）而不是本面板的状态行 —— 面板马上就关了，
            // 写在面板上的提示一个字也看不到。读条期间玩家看到的是 Toast + `LoadingPanel`。
            Game.UI?.Toast("正在返回主菜单…");
            Game.UI?.Close<ResultPanel>();
            Game.Event?.Emit(Events.Battle.ReturnToMainMenuRequest);
        }

        // ───────────────────────── 文案刷新 ─────────────────────────

        /// <summary>显示"拿不到结算数据"的界面（非预期分支的唯一出口，⛔ 不留空白页）。</summary>
        private void ShowNoData()
        {
            if (_outcome != null)
            {
                _outcome.text = "对局结束";
                _outcome.color = CrUiStyle.TextDim;
            }
            if (_crowns != null) _crowns.text = "结算数据缺失";
            if (_reason != null)
            {
                _reason.text = "没有收到本局结算内容";
                _reason.color = CrUiStyle.ErrorText;
            }
            if (_detail != null) _detail.text = "请回主菜单后重新开一局；本页无法给出胜负。";
            SetStatus("结算内容缺失，请回主菜单后重试。", CrUiStyle.ErrorText);
        }

        /// <summary>
        /// 胜 / 负 / 平。
        /// <para>
        /// 主判据用服务端的 `win` / `draw`（它是**逐接收者**填的，不依赖 `my_team`，是权威值）；
        /// 同时用 <see cref="MySideVerdict"/> 按 `crowns_a/crowns_b` + `hp_rate` + 我方 `my_team`
        /// 自己算一遍并**交叉校验**，两边不一致时留一条 Warn（把两边的数都打出来，便于定位）。
        /// </para>
        /// </summary>
        private void RefreshOutcome(BattleEndNotify r, int myTeam)
        {
            var server = ServerVerdict(r);
            var mine = MySideVerdict(r, myTeam);
            if (mine != TeamUnknown && mine != server)
            {
                // 非预期分支：客户端的冠数/血量推算与服务端判定不一致（协议字段口径变了？）。
                // 显示以服务端为准（它是权威），但必须留痕，⛔ 不许静默按自己的算。
                Game.Logger?.Warn(Tag,
                    $"胜负交叉校验不一致：服务端 win={r.win} draw={r.draw}（记为 {VerdictName(server)}），" +
                    $"按 my_team={myTeam} + crowns={r.crowns_a}:{r.crowns_b} + hp_rate={r.hp_rate_a}:{r.hp_rate_b} " +
                    $"推算为 {VerdictName(mine)}；界面按服务端显示");
            }

            if (_outcome == null) return;
            switch (server)
            {
                case VerdictWin:
                    _outcome.text = "胜 利";
                    _outcome.color = CrUiStyle.Accent;
                    break;
                case VerdictLose:
                    _outcome.text = "失 败";
                    _outcome.color = CrUiStyle.ErrorText;
                    break;
                default:
                    _outcome.text = "平 局";
                    _outcome.color = CrUiStyle.TextColor;
                    break;
            }
        }

        /// <summary>冠数：按本机队伍换算成"我方 / 对方"（队伍未知时按 A/B 原样显示并留痕）。</summary>
        private void RefreshCrowns(BattleEndNotify r, int myTeam)
        {
            if (_crowns == null) return;

            if (myTeam == TeamBlue || myTeam == TeamRed)
            {
                var mine = myTeam == TeamBlue ? r.crowns_a : r.crowns_b;
                var theirs = myTeam == TeamBlue ? r.crowns_b : r.crowns_a;
                SetCrownsText($"我方冠 {mine} : 对方冠 {theirs}");
                // 图 = 原版结算王冠帧：本机是蓝方取蓝那张（`ui_battle_end_out/027`）、红方取红那张（115）；
                // N 冠 = 同一张复制 N 次（原版没有 2/3 冠堆叠帧，见 CrownMax 的注释）。
                ShowCrowns(mine, myTeam == TeamRed ? ResPaths.CrownRed : ResPaths.CrownBlue);
                return;
            }

            // 非预期分支：没收到 `Battle.Started`（my_team 未知）⇒ ⛔ 不许默认自己是 A 方。
            Game.Logger?.Warn(Tag,
                "my_team 未知（没收到 Events.Battle.Started？）：冠数按 A/B 原样显示，不做我方/对方换算");
            SetCrownsText($"A 方冠 {r.crowns_a} : B 方冠 {r.crowns_b}（本机队伍未知）");
            // 队伍未知时**不猜我方**：按协议口径 A 方 = 蓝（`ProtoDef.BattleStartNotify.my_team` 0=BLUE），
            // 所以这里显示 A 方的冠数并用蓝王冠，与上面"按 A/B 原样显示"的文字同口径。
            ShowCrowns(r.crowns_a, ResPaths.CrownBlue);
        }

        /// <summary>
        /// 点亮前 <paramref name="count"/> 个冠数图标，并把它们换成 <paramref name="resPath"/> 那张原版王冠帧
        /// （蓝 `ui_battle_end_out/027` / 红 `115`）。同一条路径只加载一次（`Sprite` 归 Resources 管，不自己造）。
        /// </summary>
        private void ShowCrowns(int count, string resPath)
        {
            if (_crownIcons == null) return;

            var shown = Mathf.Clamp(count, 0, CrownMax);
            for (var i = 0; i < _crownIcons.Length; i++)
            {
                if (_crownIcons[i] != null) _crownIcons[i].gameObject.SetActive(i < shown);
            }
            if (shown == 0) return;                     // 0 冠：格子全灭，不必加载图

            if (_crownsPath == resPath)
            {
                // 非预期分支（正常态：同一局内多次刷新）：已加载过同一张帧，留一条 Info，别静默。
                Game.Logger?.Info(Tag, $"冠数图标沿用已加载的原版王冠帧：{resPath}");
                return;
            }
            _crownsPath = resPath;

            if (Game.Res == null)
            {
                // 非预期分支：CloverRes.Init 缺失 ⇒ 王冠只剩兜底纯色。留痕（只报一次）。
                WarnAssetOnce("Game.Res 为空（漏了 CloverRes.Init？），王冠图标加载不了，退化为纯色块");
                return;
            }

            Game.Res.LoadAsset<Sprite>(resPath, sprite =>
            {
                if (sprite == null)
                {
                    // 素材没进工程 / 没导入为 Sprite（Texture Type 不是 Sprite 时取到 null）。
                    WarnAssetOnce("原版王冠帧加载不到（退化为纯色块）：" + resPath);
                    return;
                }
                if (_crownIcons == null) return;

                var ratio = sprite.rect.width > 0.01f ? sprite.rect.height / sprite.rect.width : 1f;
                for (var i = 0; i < _crownIcons.Length; i++)
                {
                    var img = _crownIcons[i];
                    if (img == null) continue;
                    img.sprite = sprite;
                    img.rectTransform.sizeDelta = new Vector2(CrownIconW, CrownIconW * ratio);
                    img.color = Color.white;    // 有图时不能再用兜底色去乘（兜底色会当色调滤镜）
                }
            });
        }

        /// <summary>素材类非预期分支只报一次（否则每次开面板都刷屏）。</summary>
        private void WarnAssetOnce(string message)
        {
            if (_assetWarned) return;
            _assetWarned = true;
            Game.Logger?.Warn(Tag, message);
        }

        /// <summary>
        /// 结算原因 → 中文短句（去掉"结算原因："前缀）。⛔ 未知取值**原文写日志**、界面显示「未知」。
        /// 取值表出处：`Def/ProtoDef.cs:104`（king_destroyed / time_up_crowns / time_up_hp / sudden_death /
        /// surrender / draw）+ `server/game/core/rules.go:16-23`。
        /// </summary>
        private void RefreshReason(BattleEndNotify r, int myTeam)
        {
            if (_reason == null) return;

            var reason = r.reason;
            switch (reason)
            {
                case "king_destroyed":
                    _reason.text = "国王塔被摧毁";
                    break;
                case "time_up_crowns":
                    _reason.text = "加时结束，比冠数";
                    break;
                case "time_up_hp":
                    _reason.text = "加时结束，比塔血";
                    break;
                case "sudden_death":
                    _reason.text = "加时赛先破塔获胜";
                    break;
                case "surrender":
                    _reason.text = r.draw ? "有一方投降（判定为平局）"
                        : (r.win ? "对方投降了" : "你投降了");
                    break;
                case "draw":
                    _reason.text = "平局";
                    break;
                case null:
                case "":
                    if (!_unknownReasonWarned)
                    {
                        _unknownReasonWarned = true;
                        Game.Logger?.Warn(Tag, "结算原因为空（服务端未下发 reason），界面用兜底文案显示");
                    }
                    _reason.text = "未收到原因";
                    break;
                default:
                    if (!_unknownReasonWarned)
                    {
                        _unknownReasonWarned = true;
                        Game.Logger?.Warn(Tag,
                            $"未知的结算原因 reason='{reason}'（协议只定义 king_destroyed / time_up_crowns / " +
                            "time_up_hp / sudden_death / surrender / draw），界面显示「未知」");
                    }
                    _reason.text = "未知";
                    break;
            }
        }

        /// <summary>细节行：终局塔血 + 对局模式（居中灰字，不带房间号）。</summary>
        private void RefreshDetail(BattleEndNotify r, int myTeam)
        {
            if (_detail == null) return;

            // `hp_rate_*` 是"剩余塔血占总上限的**万分比**"（`Def/ProtoDef.cs:112-113`）⇒ ÷100 得百分数。
            string hp;
            if (myTeam == TeamBlue || myTeam == TeamRed)
            {
                var mine = myTeam == TeamBlue ? r.hp_rate_a : r.hp_rate_b;
                var theirs = myTeam == TeamBlue ? r.hp_rate_b : r.hp_rate_a;
                hp = $"我方塔血 {mine / 100f:0}% ｜ 对方塔血 {theirs / 100f:0}%";
            }
            else
            {
                hp = $"塔血 A {r.hp_rate_a / 100f:0}% ｜ B {r.hp_rate_b / 100f:0}%";
            }

            var mode = _aiBattle ? "人机对局" : "房间对局";
            _detail.text = $"{hp} ｜ {mode}";
        }

        // ───────────────────────── 胜负判定（本机视角） ─────────────────────────

        /// <summary>胜负取值 → 日志用文案（只用于留痕，⛔ 不参与界面判定）。</summary>
        private static string VerdictName(int verdict)
        {
            return verdict == VerdictWin ? "我方胜" : verdict == VerdictLose ? "我方负" : "平局";
        }

        /// <summary>服务端判定的本机视角胜负（`win` / `draw` 都是逐接收者填的）。</summary>
        private static int ServerVerdict(BattleEndNotify r)
        {
            if (r.draw) return VerdictDraw;
            return r.win ? VerdictWin : VerdictLose;
        }

        /// <summary>
        /// **本机视角**胜负（0=平 1=我方胜 2=我方负；-1 = 队伍未知，判不出来）。
        ///
        /// <para>
        /// 判据顺序：① `draw` → 平局；② 冠数（`crowns_a` / `crowns_b` 按本机 `my_team` 取我方那侧）——
        /// 多者胜；③ 冠数相同但不判平局 ⇒ 只能是"加时结束比总血量"，比 `hp_rate_*`。
        /// ⛔ 全程用 `my_team` 选边，**绝不假设自己永远是 A 方**。
        /// </para>
        /// </summary>
        private static int MySideVerdict(BattleEndNotify r, int myTeam)
        {
            if (myTeam != TeamBlue && myTeam != TeamRed) return -1;
            if (r.draw) return VerdictDraw;

            var mineCrowns = myTeam == TeamBlue ? r.crowns_a : r.crowns_b;
            var theirCrowns = myTeam == TeamBlue ? r.crowns_b : r.crowns_a;
            if (mineCrowns != theirCrowns)
            {
                return mineCrowns > theirCrowns ? VerdictWin : VerdictLose;
            }

            var mineHp = myTeam == TeamBlue ? r.hp_rate_a : r.hp_rate_b;
            var theirHp = myTeam == TeamBlue ? r.hp_rate_b : r.hp_rate_a;
            if (mineHp != theirHp)
            {
                return mineHp > theirHp ? VerdictWin : VerdictLose;
            }

            return VerdictDraw;
        }

        /// <summary>
        /// 冠数写进单行标签：从 <see cref="CrUiStyle.FontTitle"/> 起按**实测宽度**逐档缩字号，
        /// 直到装进标签矩形（420 宽）—— ⛔ 不换行（换行后的第二行会压到奖励条）。
        /// 缩到 <see cref="CrUiStyle.FontSmall"/> 仍装不下（只有"队伍未知"这类超长串）才按宽度截断。
        /// </summary>
        private void SetCrownsText(string text)
        {
            if (_crowns == null) return;

            var width = _crowns.rectTransform.rect.width;
            var size = CrUiStyle.FontTitle;
            _crowns.fontSize = size;
            _crowns.text = text;
            while (size > CrUiStyle.FontSmall && TextFit.Measure(_crowns, text) > width)
            {
                size -= 2;
                _crowns.fontSize = size;
            }
            if (TextFit.Measure(_crowns, text) > width) TextFit.ClampSelf(_crowns);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = TextFit.Clamp(_status, text);
            _status.color = color;
        }
    }
}
