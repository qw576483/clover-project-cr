using System;
using CloverEngine;
using CR.Def;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 房间内（`Room` 站点，Normal 层，架构契约 §4）：成员 / 准备 / 房主「AI 补位」/ 房主「开始对战」/ 离开。
    ///
    /// <para>
    /// <b>依赖方向（契约 §1 硬线）</b>：本面板⛔不许 `using CR.Module` —— 只
    /// `Emit(Events.Room.*Request)` 发请求；房间态由 `Module/Room/RoomManager` 收到 `PushRoomState`
    /// 后 `Emit(Events.Room.StateChanged, state)` 递进来，本面板据此重画。
    /// </para>
    /// <para>
    /// <b>"我是谁"从哪来</b>：服务端房间态给的是角色 ID（成员 <c>player_id</c> / 房主 <c>host</c>），
    /// 协议里没有"我自己是谁"的字段 ⇒ 由 `RoomManager` 反推出本机角色 ID 后经
    /// <see cref="PanelArgs.SelfPlayerId"/> 递进来（它是唯一知道怎么反推的地方）。
    /// 面板只用它做**展示与按钮置灰**；开打与否永远由服务端裁决（`room.go:1105-1119` 明确拒绝非房主）。
    /// </para>
    /// <para>
    /// <b>竖版排版</b>：面板底 = <see cref="CrUiStyle.SettingsPopup"/>（居中弹窗：
    /// `ui_out` 014 板岩外框 + 019 亮面体，与主菜单 / 设置**同一套**），标题 = <see cref="CrUiStyle.BandTitle"/>
    /// （板岩带上的白字黑描边），座位格 = `ui_out` 014（<see cref="CrUiStyle.Skin"/> 四角镜像九宫格），
    /// 按钮 = <see cref="CrUiStyle.BlueButton"/>（`ui_out` 165 蓝底白字），
    /// **「AI 补位」= 原版状态按钮**（开 = 绿 `ui_out` 610 / 关 = 红 `ui_out` 477，见
    /// <see cref="CrUiStyle.DressStateButton"/>）。
    /// ⛔ 不用 1200×780 横框、⛔ 不用 1120 宽座位条、⛔ 不用「准备 / AI 补位」与「开始 / 离开」左右并排 —— 一律单列。
    /// </para>
    /// <para>
    /// ⚠️ <b>A 本体没有「房间内 / 对战准备」界面</b>（`策划/参考图/清单.md` §2 登记：原版 CR 无「房间」概念，
    /// 本项目为用户新增）⇒ 视觉语言**逐项对齐 A 的同类部件**，⛔ **不假装是原版界面**。
    /// </para>
    /// </summary>
    public sealed class RoomPanel : UIPanel
    {
        private const string Tag = "RoomPanel";

        /// <summary>
        /// 打开参数（由 `Module/Room/RoomManager` 构造）。
        /// ⛔ 面板不许引 `CR.Module` ⇒ 房间号与"我是谁"只能这样递进来。
        /// </summary>
        public sealed class PanelArgs
        {
            /// <summary>当前房间号（显示用；发请求时房间号由管理器自己持有）。</summary>
            public string RoomId;

            /// <summary>
            /// 本机角色 ID（形如 <c>p_&lt;账号&gt;</c>）；为空 = 认不出自己，
            /// 此时面板**不**把房主按钮置灰（放行，由服务端裁决），并在提示行里说明。
            /// </summary>
            public string SelfPlayerId;
        }

        // ───────────────────────── 竖版排版常量（A = 12_主菜单_750x1334.png，折算 ×1.44） ─────────────────────────

        /// <summary>面板亮面体宽 = 931（<see cref="CrUiStyle.PopupW"/> 935 − 2×描边 2）。</summary>
        private const float BodyW = CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder;

        /// <summary>亮面体内缩留白后的可用宽 = 871（= BodyW − 2×<see cref="CrUiStyle.PopupPad"/>）。</summary>
        private const float InnerW = BodyW - 2f * CrUiStyle.PopupPad;

        /// <summary>元素左边界 = 32（= 描边 2 + 留白 30）。</summary>
        private const float InsetX = CrUiStyle.PopupBorder + CrUiStyle.PopupPad;

        /// <summary>标题行高 = 92（与主菜单同口径）。</summary>
        private const float TitleRowH = CrUiStyle.TitleBarH;

        /// <summary>板岩盘（座位格）的圆角边长 = 014 的圆角（与弹窗外框同口径）。</summary>
        private const int PlateCorner = 24;

        /// <summary>按钮宽 = 824（A "Battle Deck" 572@750 = 76.3% 屏宽 ×1.44 = 823.7；≤ 亮面体内缩 871）。</summary>
        private const float BtnW = 824f;

        /// <summary>按钮高 = 66（A 同一条 y=1145..1190，h=46@750 ×1.44 = 66.2）。</summary>
        private const float BtnH = 66f;

        /// <summary>
        /// 座位数 = 服务端一个房间的座位数。
        /// 出处：`server/game/logic/room.go:22`（`maxRoomMembers = 2`，1v1）；协议里也固定
        /// <c>RoomInfo.max</c> = 2。座位号（下标）就是队伍号：0 = BLUE、1 = RED。
        /// </summary>
        private const int SeatCount = 2;

        /// <summary>座位条高 = 95（A 字段带 66@750 ×1.44 = 95，<see cref="CrUiStyle.FieldH"/>）。</summary>
        private const float SeatH = CrUiStyle.FieldH;

        private const float SeatGap = 24f;                                  // 本项目自定行距（登记）
        private const float SeatStep = SeatH + SeatGap;                     // 119
        private const float InfoH = 36f;
        private const float MemLabelH = 36f;
        private const float RulesH = 84f;                                   // 3 行 × 28
        private const float HostH = 56f;                                    // 2 行 × 28
        private const float StatusH = 84f;                                  // 3 行 × 28
        private const float GapS = 12f;
        private const float GapM = 16f;
        private const float GapL = 32f;

        // ───────────────────────── 运行时状态 ─────────────────────────

        private bool _built;
        private string _roomId;
        private string _selfPlayerId;
        private RoomStateNotify _state;
        private bool _extraSeatWarned;   // 成员数超过座位数只告警一次

        private RectTransform _content;   // 内容框（所有元素的父节点，Build 里赋值）
        private Text _roomInfo;
        private Text _hostHint;
        private Text _status;
        private readonly Image[] _seatBoxes = new Image[SeatCount];
        private readonly Text[] _seatNames = new Text[SeatCount];
        private readonly Text[] _seatStates = new Text[SeatCount];
        private Image _readyButton;
        private Image _aiButton;
        private Text _aiButtonText;
        private Image _startButton;
        private Image _leaveButton;

        private Action<RoomStateNotify> _onStateChanged;
        private Action<string> _onFailed;

        /// <summary>房间面板是 `Room` 站点的 Normal 层面板（架构契约 §4）。</summary>
        public override UILayer Layer => UILayer.Normal;

        public override void OnOpen(object param)
        {
            var args = param as PanelArgs;
            _roomId = args != null ? args.RoomId : null;
            _selfPlayerId = args != null ? args.SelfPlayerId : null;

            if (args == null)
            {
                // 非预期分支：绕过 RoomManager 打开了面板。留痕 —— 没有参数就没有房间号与"我是谁"，
                // 界面只能在收到 StateChanged 后才说得清（这条路径正常游戏流程不会走）。
                Game.Logger?.Warn(Tag, "打开时未收到 PanelArgs（应由 RoomManager 传入房间号与自己的角色 ID），按缺省显示");
            }

            if (!_built)
            {
                Build();
                _built = true;
            }

            Subscribe();
            _extraSeatWarned = false;
            RefreshDisplay();
            SetStatus("等待服务端房间状态…", CrUiStyle.TextDim);
        }

        public override void OnClose()
        {
            Unsubscribe();
        }

        // ───────────────────────── 视觉树（D1：OnOpen 里用 UIFactory 自建） ─────────────────────────

        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);

            // Normal 层是全屏站点面板：自己铺底色 + 原版主视觉（与本项目其它站点面板一致）。
            CrUiStyle.Screen("Bg", root);
            CrUiStyle.SpriteBackground("BgArt", root, ResPaths.BootBackground);

            // ── 纵向落点（唯一真相；面板高度与摆放共用同一组算式）──
            // 坐标原点 = **亮面体左上角**（`CrUiStyle.SettingsPopup` 的 body 锚点在左上），y 为负 = 向下。
            float seatsH = SeatCount * SeatH + (SeatCount - 1) * SeatGap;   // 2×95 + 1×24 = 214
            float yInfo = -(TitleRowH + GapM);
            float yMemLabel = yInfo - InfoH - GapS;
            float ySeat0 = yMemLabel - MemLabelH - GapS;
            float yRules = ySeat0 - seatsH - GapM;
            float yReady = yRules - RulesH - GapM;
            float yAi = yReady - BtnH - GapS;
            float yStart = yAi - BtnH - GapM;
            float yLeave = yStart - BtnH - GapS;
            float yHost = yLeave - BtnH - GapM;
            // 房主提示行 + 状态行**各垫一条板岩盘**再放字（与主菜单 StatusBar 同构）：
            // 这两行的颜色是**动态**的（ErrorText 浅红 / Accent 金 / TextDim 浅灰），全是**为暗底设计**的，
            // 直接压亮面体 (229,236,242) 上对比度极低（实测："你不是房主…" 发白看不清）。
            // ⚠️ 左上角锚点 + anchoredPosition ⇒ y 越接近 0 越靠上：盘的顶边写在字的顶边**之上**（`+ GapS`）。
            float hostPlateY = yHost + GapS;
            float hostPlateH = HostH + 2f * GapS;
            float yStatus = (hostPlateY - hostPlateH) - GapM;
            float statusPlateY = yStatus + GapS;
            float statusPlateH = StatusH + 2f * GapS;
            float bodyH = -(statusPlateY - statusPlateH) + GapL;

            var box = CrUiStyle.SettingsPopup("RoomBox", root, bodyH + CrUiStyle.PopupTitleH);
            var c = box.rectTransform.Find("RoomBoxBody") as RectTransform;
            if (c == null)
            {
                // 非预期分支（必须留痕）：亮面体没建出来 ⇒ 后面的元素全落空。
                Game.Logger?.Error(Tag, "弹窗亮面体 RoomBoxBody 没建出来，房间面板内容无法摆放");
                return;
            }
            _content = c;

            // 标题：板岩带上的白字黑描边（A 原版 UI 的标题语言）。
            CrUiStyle.BandTitle("Title", box, "房 间", BodyW);

            _roomInfo = UIFactory.CreateLabel("RoomInfo", c, "正在获取房间信息…", CrUiStyle.FontSmall,
                new Vector2(InsetX, yInfo), new Vector2(InnerW, InfoH), TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);

            UIFactory.CreateLabel("MembersLabel", c, $"成员（{SeatCount} 个座位 · 座位号即队伍号：0=蓝 1=红）",
                CrUiStyle.FontSmall, new Vector2(InsetX, yMemLabel), new Vector2(InnerW, MemLabelH),
                TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);

            BuildSeats(ySeat0);

            UIFactory.CreateLabel("Rules", c,
                "房主才能开打与切 AI 补位；非房主需要先「准备」。人数不足时房主可开「AI 补位」，" +
                "服务端在开打那一刻自动补一个 AI 座位。",
                CrUiStyle.FontSmall, new Vector2(InsetX, yRules), new Vector2(InnerW, RulesH),
                TextAnchor.UpperLeft, CrUiStyle.TextOnLightDim);

            // ── 准备 / AI 补位 / 开始 / 离开：四颗整宽按钮，一列（⛔ 旧版是两两并排）──
            _readyButton = CrUiStyle.BlueButton("ReadyButton", c, "准 备",
                new Vector2(InsetX, yReady), new Vector2(InnerW, BtnH), OnReadyClicked);

            // AI 补位做成一颗"点了就切换 + 文案带当前状态"的按钮（不用 CreateToggleRow 的标签+值并排行）。
            // 底图 = **原版状态按钮**：开 = 绿 `ui_out` 610 / 关 = 红 `ui_out` 477（原版 24_设置 的 ON/OFF 语言）
            // ⇒ 由 `RefreshDisplay` 调 `CrUiStyle.DressStateButton` 现换（⛔ 不是只改文案）。
            _aiButton = CrUiStyle.BlueButton("AiFillButton", c, "AI 补位：关",
                new Vector2(InsetX, yAi), new Vector2(InnerW, BtnH), OnAiFillClicked);
            CrUiStyle.DressStateButton(_aiButton, false);   // 初始态 = 关（红）
            _aiButtonText = _aiButton != null ? _aiButton.GetComponentInChildren<Text>() : null;
            if (_aiButtonText == null)
            {
                Game.Logger?.Error(Tag, "AI 补位按钮没有取到文本节点，房主看不到当前 AI 补位状态");
            }

            _startButton = CrUiStyle.BlueButton("StartButton", c, "开始对战",
                new Vector2((BodyW - BtnW) * 0.5f, yStart), new Vector2(BtnW, BtnH), OnStartClicked);
            _leaveButton = CrUiStyle.BlueButton("LeaveButton", c, "离开房间",
                new Vector2(InsetX, yLeave), new Vector2(InnerW, BtnH), OnLeaveClicked);

            CrUiStyle.Skin("HostPlate", c, CrUiStyle.PopupFrameSlate, PlateCorner, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InsetX, hostPlateY),
                new Vector2(InnerW, hostPlateH), CrUiStyle.BandSlate, false);
            _hostHint = UIFactory.CreateLabel("HostHint", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(InsetX + 16f, yHost), new Vector2(InnerW - 32f, HostH), TextAnchor.UpperLeft, CrUiStyle.TextDim);

            CrUiStyle.Skin("StatusPlate", c, CrUiStyle.PopupFrameSlate, PlateCorner, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InsetX, statusPlateY),
                new Vector2(InnerW, statusPlateH), CrUiStyle.BandSlate, false);
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(InsetX + 16f, yStatus), new Vector2(InnerW - 32f, StatusH), TextAnchor.UpperLeft, CrUiStyle.TextDim);
        }

        private void BuildSeats(float seatY0)
        {
            for (var i = 0; i < SeatCount; i++)
            {
                var y = seatY0 - i * SeatStep;
                // 座位格 = **板岩盘**（`ui_out` 014 四角镜像九宫格）—— A 12_主菜单的"深色圆角盘 + 亮字"语言。
                // ⚠️ 原版无「房间内」界面 ⇒ 对齐同类部件，⛔ 不假装是原版。
                _seatBoxes[i] = CrUiStyle.Skin($"Seat{i}", _content, CrUiStyle.PopupFrameSlate, PlateCorner,
                    Vector4.zero, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(InsetX, y), new Vector2(InnerW, SeatH), CrUiStyle.BandSlate, false);
                _seatNames[i] = UIFactory.CreateLabel($"SeatName{i}", _content, $"座位 {i + 1}", CrUiStyle.FontSmall,
                    new Vector2(InsetX + 16f, y), new Vector2(InnerW - 32f - 220f, SeatH), TextAnchor.MiddleLeft,
                    CrUiStyle.TextColor);
                _seatStates[i] = UIFactory.CreateLabel($"SeatState{i}", _content, string.Empty, CrUiStyle.FontSmall,
                    new Vector2(InsetX + InnerW - 220f, y), new Vector2(220f, SeatH), TextAnchor.MiddleRight,
                    CrUiStyle.TextDim);
            }
        }

        // ───────────────────────── 交互 ─────────────────────────

        private void OnReadyClicked()
        {
            var me = FindSelf();
            if (me == null)
            {
                SetStatus("还没拿到你的座位信息（等房间状态同步后再点）", CrUiStyle.ErrorText);
                return;
            }
            if (IsRunning())
            {
                SetStatus("对局已开始，不能改准备状态", CrUiStyle.ErrorText);
                return;
            }

            var next = !me.ready;
            Game.Logger?.Info(Tag, $"请求把准备态改为 {next}");
            SetStatus(next ? "已请求准备…" : "已请求取消准备…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Room.ReadyRequest, next);
        }

        private void OnAiFillClicked()
        {
            if (!IsLocalHost())
            {
                SetStatus(HostOnlyReason("切换 AI 补位"), CrUiStyle.ErrorText);
                return;
            }
            if (IsRunning())
            {
                SetStatus("对局已开始，不能改 AI 补位", CrUiStyle.ErrorText);
                return;
            }

            var next = !(_state != null && _state.ai_fill);
            Game.Logger?.Info(Tag, $"请求把 AI 补位改为 {next}");
            SetStatus(next ? "已请求开启 AI 补位…" : "已请求关闭 AI 补位…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Room.SetAiRequest, next);
        }

        private void OnStartClicked()
        {
            if (!IsLocalHost())
            {
                SetStatus(HostOnlyReason("开始对战"), CrUiStyle.ErrorText);
                return;
            }

            Game.Logger?.Info(Tag, "请求开打（房主）");
            SetStatus("已请求开打，等服务端下发对局配置…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Room.StartRequest);
        }

        private void OnLeaveClicked()
        {
            Game.Logger?.Info(Tag, "请求离开房间");
            SetStatus("正在离开房间…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Room.LeaveRequest);
        }

        // ───────────────────────── 订阅（OnOpen 挂 / OnClose 摘，成对） ─────────────────────────

        private void Subscribe()
        {
            if (_onStateChanged == null)
            {
                _onStateChanged = OnStateChanged;
                _onFailed = OnFailed;
            }

            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger?.Error(Tag, "Game.Event 为空（引擎未 Launch？），房间面板收不到任何数据");
                return;
            }

            // 幂等：UIManager 对已打开的面板会再次调用 OnOpen（UI.cs:107-117），重复 On 会让一次失败走两遍处理。
            bus.Off(Events.Room.StateChanged, _onStateChanged);
            bus.Off(Events.Room.Failed, _onFailed);
            bus.On(Events.Room.StateChanged, _onStateChanged);
            bus.On(Events.Room.Failed, _onFailed);
        }

        private void Unsubscribe()
        {
            if (_onStateChanged == null) return;
            var bus = Game.Event;
            bus?.Off(Events.Room.StateChanged, _onStateChanged);
            bus?.Off(Events.Room.Failed, _onFailed);
        }

        private void OnStateChanged(RoomStateNotify state)
        {
            if (state == null)
            {
                // 非预期分支：`Emit` 用 DynamicInvoke，参数类型不对劲时会在这里显形。留痕。
                Game.Logger?.Warn(Tag, "收到 null 的房间状态（发送方参数有误？），界面不刷新");
                return;
            }

            _state = state;
            if (string.IsNullOrEmpty(_roomId)) _roomId = state.room_id;

            var count = state.members != null ? state.members.Length : 0;
            if (count > SeatCount && !_extraSeatWarned)
            {
                _extraSeatWarned = true;
                // 非预期分支：座位数超出本面板预建的槽位（服务端房间座位数被改大？）。留痕，别静默丢人。
                Game.Logger?.Warn(Tag,
                    $"房间成员 {count} 个 > 面板预建座位 {SeatCount} 个，多出的成员不显示（服务端座位数变了？）");
            }

            RefreshDisplay();
        }

        private void OnFailed(string reason)
        {
            SetStatus(string.IsNullOrEmpty(reason) ? "房间操作失败（服务端未给出原因）" : reason, CrUiStyle.ErrorText);
        }

        // ───────────────────────── 显示刷新 ─────────────────────────

        private void RefreshDisplay()
        {
            var state = _state;

            if (_roomInfo != null)
            {
                // 房名 / 房号是**服务端数据**（长度不由本面板控制）⇒ 过 TextFit（D4 同族）。
                _roomInfo.text = TextFit.Clamp(_roomInfo, state != null
                    ? $"房号 {state.room_id} · 房名 {state.name} · {(state.started ? "已开打" : "未开打")} · " +
                      $"AI 补位：{(state.ai_fill ? "开" : "关")}"
                    : $"房号 {_roomId ?? "未知"} · 正在获取房间状态…");
            }

            var members = state != null && state.members != null ? state.members : Array.Empty<RoomMember>();
            for (var i = 0; i < SeatCount; i++)
            {
                var name = _seatNames[i];
                var stateText = _seatStates[i];
                if (name == null) continue;

                if (i >= members.Length || members[i] == null)
                {
                    name.text = $"座位 {i + 1}（{TeamName(i)}）：空座";
                    name.color = CrUiStyle.TextDim;
                    if (stateText != null) { stateText.text = "等待加入"; stateText.color = CrUiStyle.TextDim; }
                    continue;
                }

                var m = members[i];
                var isSelf = !string.IsNullOrEmpty(_selfPlayerId) && m.player_id == _selfPlayerId;
                var tags = string.Empty;
                if (m.is_host) tags += " · 房主";
                if (m.is_ai) tags += " · AI";
                if (isSelf) tags += " · 我";

                // 昵称是**服务端数据** ⇒ 过 TextFit（D4 同族；服务端限 16 字，但首尾仍是外部输入）。
                name.text = TextFit.Clamp(name, $"座位 {i + 1}（{TeamName(i)}）：{m.nickname}{tags}");
                name.color = m.is_ai ? CrUiStyle.TextDim : CrUiStyle.TextColor;

                if (stateText != null)
                {
                    stateText.text = m.is_ai ? "AI 补位" : (m.ready ? "已准备" : "未准备");
                    stateText.color = m.is_ai || m.ready ? CrUiStyle.Accent : CrUiStyle.TextDim;
                }
            }

            var host = IsLocalHost();
            var running = IsRunning();

            // 房主专属按钮：非房主置灰 + 把原因写出来（⛔ 不做"悬停才知道"的提示）。
            // 置灰只走 Button.interactable（原版按钮图元的 disabled tint），⛔ 不改 Image.color
            // —— 那会把九宫格原版图元染成纯色。
            var canHost = host && !running;
            CrUiStyle.SetButtonEnabled(_startButton, canHost);
            CrUiStyle.SetButtonEnabled(_aiButton, canHost);
            // AI 补位按钮的**底图随状态换帧**（开 = 绿 610 / 关 = 红 477，与原版 24_设置的 ON/OFF 同一对帧）
            // —— ⛔ 不能只改文案（底图会恒为深蓝灰，与原版语言不符）。
            var aiOn = state != null && state.ai_fill;
            CrUiStyle.DressStateButton(_aiButton, aiOn);
            if (_aiButtonText != null) _aiButtonText.text = aiOn ? "AI 补位：开" : "AI 补位：关";

            var me = FindSelf();
            // 准备按钮**放行**（只要对局没开始）：认不出自己时（拿不到角色 ID / 成员列表里没有我）
            // 仍然让玩家点 —— 服务端是唯一裁决者，它会拒绝并给出原因；而"灰着又不说为什么"最差。
            CrUiStyle.SetButtonEnabled(_readyButton, !running);

            var readyText = _readyButton != null ? _readyButton.GetComponentInChildren<Text>() : null;
            if (readyText != null) readyText.text = me != null && me.ready ? "取消准备" : "准 备";

            if (_hostHint != null)
            {
                if (string.IsNullOrEmpty(_selfPlayerId))
                {
                    _hostHint.text = "提示：拿不到你的角色 ID，房主按钮未按房主判定置灰；非房主操作会被服务端拒绝并显示原因。";
                    _hostHint.color = CrUiStyle.ErrorText;
                }
                else if (running)
                {
                    _hostHint.text = "对局已开始。";
                    _hostHint.color = CrUiStyle.TextDim;
                }
                else if (host)
                {
                    _hostHint.text = "你是房主：可以开打（非房主以外的座位需已准备），也可以切换 AI 补位。";
                    _hostHint.color = CrUiStyle.Accent;
                }
                else
                {
                    _hostHint.text = "你不是房主，不能开打 / 切 AI 补位。" +
                        (string.IsNullOrEmpty(HostName()) ? string.Empty : $"。当前房主：{HostName()}");
                    _hostHint.color = CrUiStyle.TextDim;
                }
            }
        }

        // ───────────────────────── 小工具 ─────────────────────────

        /// <summary>座位号 → 队伍名（座位号即队伍号：0=BLUE 1=RED，见 `ProtoDef.BattleStartNotify.my_team` 的约定）。</summary>
        private static string TeamName(int seat)
        {
            return seat == 0 ? "蓝方" : (seat == 1 ? "红方" : "未知队伍");
        }

        private bool IsRunning()
        {
            return _state != null && _state.started;
        }

        /// <summary>
        /// 本机是不是房主。认不出自己（<see cref="PanelArgs.SelfPlayerId"/> 为空）时**放行**：
        /// 让玩家点、由服务端拒绝并给出原因（服务端是唯一裁决者），比"一个永远灰着且说不出为什么的按钮"好。
        /// </summary>
        private bool IsLocalHost()
        {
            if (_state == null) return false;
            if (string.IsNullOrEmpty(_selfPlayerId)) return true;
            return !string.IsNullOrEmpty(_state.host) && _state.host == _selfPlayerId;
        }

        private RoomMember FindSelf()
        {
            if (_state == null || _state.members == null || string.IsNullOrEmpty(_selfPlayerId)) return null;
            for (var i = 0; i < _state.members.Length; i++)
            {
                var m = _state.members[i];
                if (m != null && m.player_id == _selfPlayerId) return m;
            }
            return null;
        }

        private string HostName()
        {
            if (_state == null || _state.members == null || string.IsNullOrEmpty(_state.host)) return null;
            for (var i = 0; i < _state.members.Length; i++)
            {
                var m = _state.members[i];
                if (m != null && m.player_id == _state.host) return m.nickname;
            }
            return null;
        }

        private string HostOnlyReason(string what)
        {
            var who = HostName();
            return string.IsNullOrEmpty(who)
                ? $"只有房主可以{what}（服务端会拒绝非房主请求）"
                : $"只有房主可以{what}，当前房主是 {who}";
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = TextFit.Clamp(_status, text);
            _status.color = color;
        }
    }
}
