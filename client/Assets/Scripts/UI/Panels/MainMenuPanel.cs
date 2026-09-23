using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 主菜单（`MainMenu` 站点，`Main` 场景，Normal 层）：卡组编辑 / 房间列表 / 人机对战 / 设置 / 退出。
    ///
    /// <para>
    /// <b>本面板只负责"按钮存在 + 发事件"</b>：四个动作的**实现**分属其它片
    /// （卡组编辑 = agent-08、房间列表 = agent-06、人机对战与对局 = agent-06/07），
    /// 所以这里一律 `Emit(Events.Xxx.…Request)`，由各模块（或 `AppFlow`）订阅后执行。
    /// ⛔ 面板不 `using CR.Module` —— 这是契约 §1 的硬线，也是"按钮与链路解耦"的收益：
    /// agent-06/07 接入时**不需要**动本文件。
    /// </para>
    /// <para>
    /// <b>为什么"人机对战"走事件而不是直接调 <c>AppFlow.RequestStartAiBattle()</c></b>：
    /// 面板引不到 `CR.Module`（同上），所以由 `AppFlow` 订阅
    /// `Events.Battle.AiBattleRequest` 再调自己的 `RequestStartAiBattle()`
    /// —— 契约里那个 public 方法就是 agent-06/07 的接入点，面板只是它的触发源之一。
    /// </para>
    /// <para>
    /// <b>竖版重排（G2）</b>：画布 = <see cref="CrUiStyle.DesignW"/>×<see cref="CrUiStyle.DesignH"/>（1080×1920、
    /// `match=0`）。内容框 = <see cref="CrUiStyle.ContentPanel"/>（贴顶居中、宽 <see cref="CrUiStyle.ContentW"/>=1000），
    /// 标题 = <see cref="CrUiStyle.TitleBar"/>（原版金色标题条九宫格），
    /// 按钮 = <see cref="CrUiStyle.ActionButton"/>（原版金 / 深蓝灰按钮底九宫格），
    /// 纵向一律"面板内左上角锚点 + 逐行累加 y"（⛔ 不再有 900×660 的居中横框、⛔ 没有"大负 y"顶出屏）。
    /// 每个数值的出处见 `.ai-tmp/test/G2-量取.md`（A = `策划/参考图/12_主菜单_750x1334.png`，折算 ×1.44）。
    /// </para>
    /// </summary>
    public sealed class MainMenuPanel : UIPanel
    {
        // ───────────────────────── 竖版排版常量（出处：G2-量取.md，基线图宽 750 → 1080 画布 ×1.44） ─────────────────────────

        /// <summary>
        /// 弹窗亮面体 / 内侧留白 / 元素左边界（AO1 换帧后**不再用** `CrUiStyle.ContentW` 那一套 ——
        /// 面板底已经从"贴顶居中的米色纸框"换成"居中弹窗(935 宽)"）。
        /// <para>⚠️ AO1 改：原来元素宽取 `ContentW − 2×Pad` = 920，而弹窗亮面体只有 931
        /// ⇒ 玩家条(920)几乎贴着面板边（实测首版截图），留白等于没有。基线 `12_主菜单` 的面板内留白
        /// 是 14px@499 ⇒ 30@1080（`CrUiStyle.PopupPad`，AM2 在 `24_设置` 上同一量取）⇒ 改用它。</para>
        /// </summary>
        private const float BodyW = CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder;   // 931 = 亮面体宽
        private const float InnerW = BodyW - 2f * CrUiStyle.PopupPad;               // 871 = 亮面体内缩留白后
        private const float InsetX = CrUiStyle.PopupBorder + CrUiStyle.PopupPad;    // 32 = 元素左边界

        /// <summary>
        /// 顶部玩家信息条高度 = <see cref="CrUiStyle.FieldH"/> = 95。
        /// <para>出处：A 12_主菜单 字段带 y=449..514（h=66@750）×1.44 = 95.0；同图顶部信息区 资源条 78 + 名字条 38 = 116@750
        /// （→ 167@1080，即 <see cref="CrUiStyle.PanelTopOffset"/> 的同一比例）。本项目把这两条合成**一条**信息盘（95 高）。</para>
        /// </summary>
        private const float BarH = CrUiStyle.FieldH;

        /// <summary>
        /// 按钮宽 = 824。
        /// <para>出处：A 12_主菜单 底部蓝色宽按钮/ribbon "Battle Deck" x=126..697（w=572@750 = 屏宽 76.3%）×1.44 = 823.7 ⇒ 824。
        /// 「同时满足内容框内缩 40×2(=920)」⇒ 取 824 居中（两侧各留 88）。</para>
        /// </summary>
        private const float BtnW = 824f;

        /// <summary>
        /// 按钮高 = 66。
        /// <para>出处：A 12_主菜单 同一条 "Battle Deck" y=1145..1190（h=46@750）×1.44 = 66.2 ⇒ 66。</para>
        /// </summary>
        private const float BtnH = 66f;

        /// <summary>按钮行距 = 按钮高 + 24 空隙（24 = 本项目自定节奏，登记在 G2-量取.md）。</summary>
        private const float Step = BtnH + 24f;

        private const float GapS = 12f;
        private const float GapM = 16f;
        private const float GapL = 32f;

        /// <summary>状态行高（一行 <see cref="CrUiStyle.FontSmall"/> 24 + 余量）。</summary>
        private const float StatusH = 40f;

        /// <summary>按钮颗数（卡组编辑 / 房间列表 / 人机对战 / 设置 / 退出游戏）。</summary>
        private const int ButtonCount = 5;

        private bool _built;
        private Text _nicknameLabel;
        private Text _status;

        /// <summary>
        /// "没拿到服务端昵称"这条非预期分支**只报一次**。
        /// <para>
        /// ⛔ 刻意是 <c>static</c>（整个会话只报一次，而不是"每个面板实例一次"）：本面板每次
        /// `Open` / `Close` 都是新实例（`UIManager` 析构重建），实例级标志在"开→关→再开"下会重复刷屏，
        /// 而这条日志想说的事实是"这个会话没拿到服务端昵称"，与会话同粒度。
        /// </para>
        /// </summary>
        private static bool _nicknameWarned;

        private Action<string> _onBattleStartFailed;
        private Action<string> _onNicknameKnown;

        /// <summary>主菜单在 Normal 层（架构契约 §4）。</summary>
        public override UILayer Layer => UILayer.Normal;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }
            Subscribe();

            // 昵称来源有两条，按权威性排序（根因见 `Core/PlayerSession.cs` 的类注释）：
            //  ① param —— 由 AppFlow 传入（`Game.UI.Open<MainMenuPanel>(nickname)`）；
            //  ② `PlayerSession.Nickname` —— **服务端权威会话态**（AppFlow 从 GetProfile / SetNickname
            //     回包写入）。⛔ 之所以必须要有 ②：param 是**一次性快照**，只有 `AppFlow.EnterMainMenu`
            //     会传；别的打开路径（`UIManager.Open<T>` 对已存在面板重调 `OnOpen(param)`，`UI.cs:127`；
            //     `AppFlow.GoTo` 对同站点早退，`AppFlow.cs:365`）拿到的就是 null ⇒ 原先会**静默**显示
            //     硬编码的 "玩家"，与服务端档案里的名字不一致且**一句日志都没有**（实测 AN1 step7）。
            //     面板⛔不许 `using CR.Module`（契约 §1）⇒ 只能读同层 `CR.PlayerSession`，不能直接问 Flow。
            var nickname = param as string;
            if (string.IsNullOrEmpty(nickname)) nickname = PlayerSession.Nickname;
            ApplyNickname(nickname);
            SetStatus(string.Empty, CrUiStyle.TextDim);
        }

        public override void OnClose()
        {
            Unsubscribe();
        }

        // ───────────────────────── 视觉树 ─────────────────────────

        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);

            CrUiStyle.Screen("Bg", root);
            CrUiStyle.SpriteBackground("BgArt", root, ResPaths.BootBackground);

            // ── 纵向落点（唯一真相；面板高度与摆放共用同一组算式，避免两处漂移）──
            // 坐标 = **亮面体左上角**为原点（`CrUiStyle.Skin` 的 body 锚点在左上），y 为负 = 向下。
            var titleY = 0f;                                              // 标题行顶
            var barY = -(TitleRowH + GapM);                               // 玩家信息条顶
            var btnY0 = barY - BarH - GapL;                               // 第 1 颗按钮顶
            var statusY = btnY0 - (ButtonCount - 1) * Step - BtnH - GapM;  // 最后一颗按钮底 − 16
            var bodyH = -statusY + StatusH + GapS + GapL;                 // 状态条 + 底部留白

            var box = CrUiStyle.SettingsPopup("MenuBox", root, bodyH + CrUiStyle.PopupTitleH);
            var c = box.rectTransform.Find("MenuBoxBody") as RectTransform;
            if (c == null)
            {
                // 非预期分支（必须留痕）：弹窗亮面体没建出来 ⇒ 后面的元素全落空。
                Game.Logger?.Error("MainMenuPanel", "弹窗亮面体 MenuBoxBody 没建出来，主菜单内容无法摆放");
                return;
            }

            // ── 标题：**白字 + 黑描边**，压在弹窗**顶部板岩带上**（不是亮面体上）──
            // ⛔ 不再用 `CrUiStyle.TitleBar`（棕金木色条）—— 基线图上主菜单面板**没有**那条木色带。
            // ⚠️ 为什么要放板岩带而不是亮面：白字压亮面(229,236,242)对比度太低（实测首版截图里标题发灰，
            //    只有 2px 描边救不回来）；基线 `12_主菜单` 的标题之所以白字读得出，是因为它的描边更粗、
            //    并且字是加粗体 —— 这两样本项目的引擎文本件都给不了 ⇒ 改放到同族板岩带上（settings 弹窗
            //    在 AM2 就是这么做的，标题带实测 (99,104,123) 压白字对比度足够）。
            CrUiStyle.Outlined("Title", box.rectTransform, "主 菜 单", CrUiStyle.FontTitle, Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, titleY),
                new Vector2(CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder, CrUiStyle.PopupTitleH),
                TextAnchor.MiddleCenter);
            // ── 顶部玩家信息条：板岩框（`ui_out` 014）+ 徽章 + 昵称（白字黑描边，压板岩才读得出）──
            var bar = CrUiStyle.Skin("PlayerBar", c, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InsetX, barY),
                new Vector2(InnerW, BarH), CrUiStyle.BandSlate, true);
            CrUiStyle.AspectImage("PlayerBadge", bar.rectTransform, ResPaths.IconMedal, 56f,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -(BarH - 56f) * 0.5f),
                CrUiStyle.TextDim);
            _nicknameLabel = CrUiStyle.Outlined("Nickname", bar.rectTransform, "玩家", CrUiStyle.FontBody,
                Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, 0f),
                new Vector2(InnerW - 112f, BarH), TextAnchor.MiddleLeft);

            // ── 五颗功能按钮：一列、居中；蓝底（`ui_out` 165）+ 白字黑描边（A 的蓝色按钮语言）──
            var bx = (BodyW - BtnW) * 0.5f;
            var btnSize = new Vector2(BtnW, BtnH);
            CrUiStyle.BlueButton("DeckButton", c, "卡组编辑", new Vector2(bx, btnY0), btnSize,
                () => EmitRequest(Events.Deck.OpenRequest, "卡组编辑"));
            CrUiStyle.BlueButton("RoomListButton", c, "房间列表", new Vector2(bx, btnY0 - Step), btnSize,
                () => EmitRequest(Events.Room.OpenListRequest, "房间列表"));
            CrUiStyle.BlueButton("AiBattleButton", c, "人机对战", new Vector2(bx, btnY0 - Step * 2f), btnSize,
                OnAiBattleClicked);
            CrUiStyle.BlueButton("SettingsButton", c, "设置", new Vector2(bx, btnY0 - Step * 3f), btnSize,
                () => EmitRequest(Events.Flow.OpenSettingsRequest, "设置"));
            CrUiStyle.BlueButton("QuitButton", c, "退出游戏", new Vector2(bx, btnY0 - Step * 4f), btnSize,
                OnQuitClicked);

            // ── 状态条：**板岩底 + 浅色字**（原版"深色字段 + 亮字"的语言，见 12_主菜单 的深藏青值块）──
            // ⛔ 不能把状态字直接压在亮面上：`CrUiStyle.TextDim`(浅灰) / `Accent`(金) / `ErrorText`(浅红)
            //    三个状态色全是**为暗底设计的**，压亮面(229,236,242)对比度极低（实测首次换成亮面后
            //    状态字直接看不见）⇒ 给状态行垫一条同族板岩条，三种状态色的含义与可读性都保住。
            CrUiStyle.Skin("StatusBar", c, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InsetX, statusY + GapS),
                new Vector2(InnerW, StatusH + 2f * GapS), CrUiStyle.BandSlate, false);
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(InsetX + GapM, statusY), new Vector2(InnerW - 2f * GapM, StatusH),
                TextAnchor.MiddleLeft, CrUiStyle.TextDim);
        }

        // ══════════════ 面板侧不再自备建件（AP1：`Outlined` / `BlueButton` 已并入 `CrUiStyle`） ══════════════
        //
        // <b>本片改了什么</b>：AO1 因范围限制在本文件自备的 `Outlined()` / `BlueButton()` 两份实现
        // **已整体搬进 `CrUiStyle`**（`Outlined` 放 `CenteredText` 旁、`BlueButton` 放 `ActionButton` 旁，
        // 位置 = AO1-report §6 的建议）⇒ 本文件现在只调用 `CrUiStyle.*`，⛔ 不再有第二份实现
        // （否则房间列表 / 房间内 / 卡组编辑三处再各抄一份，必漂移）。
        // ⛔ 本片的换帧结果**未被改动**：标题仍是板岩带上的白字黑描边、5 颗按钮仍是蓝底 165。

        /// <summary>标题行高（A 12_主菜单 标题带 h=63@750 ×1.44 = 90.7 ⇒ 取 <see cref="CrUiStyle.TitleBarH"/>=92）。</summary>
        private const float TitleRowH = CrUiStyle.TitleBarH;

        // ───────────────────────── 交互 ─────────────────────────

        private void OnAiBattleClicked()
        {
            SetStatus("正在向服务端申请人机对战…", CrUiStyle.Accent);
            EmitRequest(Events.Battle.AiBattleRequest, "人机对战");
        }

        private void OnQuitClicked()
        {
            // 退出是"应用级"动作，交给 AppFlow（面板不引 `Application` 之外的平台行为分支）。
            EmitRequest(Events.Flow.QuitRequest, "退出游戏");
        }

        private void EmitRequest(string eventName, string what)
        {
            Game.Logger?.Info("MainMenuPanel", $"请求：{what}（事件 {eventName}）");
            Game.Event?.Emit(eventName);
        }

        // ───────────────────────── 订阅 ─────────────────────────

        private void Subscribe()
        {
            if (_onBattleStartFailed == null) _onBattleStartFailed = OnBattleStartFailed;
            Game.Event?.Off(Events.Battle.StartFailed, _onBattleStartFailed);
            Game.Event?.On(Events.Battle.StartFailed, _onBattleStartFailed);

            // 服务端权威昵称是**迟到**数据（登录拉档案 / 创角回包 / 重新拉取都可能晚于本面板 OnOpen）
            // ⇒ 订阅推送，到了就地刷新标签，⛔ 不用重开面板。
            if (_onNicknameKnown == null) _onNicknameKnown = OnNicknameKnown;
            Game.Event?.Off(Events.Flow.NicknameKnown, _onNicknameKnown);
            Game.Event?.On(Events.Flow.NicknameKnown, _onNicknameKnown);
        }

        private void Unsubscribe()
        {
            if (_onBattleStartFailed != null) Game.Event?.Off(Events.Battle.StartFailed, _onBattleStartFailed);
            if (_onNicknameKnown != null) Game.Event?.Off(Events.Flow.NicknameKnown, _onNicknameKnown);
        }

        /// <summary>推送侧：服务端权威昵称已到达。</summary>
        private void OnNicknameKnown(string nickname)
        {
            if (string.IsNullOrEmpty(nickname)) return;
            ApplyNickname(nickname);
        }

        /// <summary>
        /// 把昵称写到标签上；空值走**兜底策略**并留痕。
        /// <para>
        /// 兜底策略（写清）：拿不到服务端昵称时显示 <see cref="Cfg.Game.default_nick"/>（= 本地配置里的
        /// 默认名，出处 `Core/ClientConfig.cs:42`），⛔ 不是硬编码字面量、⛔ 不是留空；同时
        /// <c>Game.Logger?.Warn</c> **只报一次**（<see cref="_nicknameWarned"/>）——下一次
        /// `Flow.NicknameKnown` 或重开面板拿到真名就会覆盖掉它。
        /// </para>
        /// </summary>
        private void ApplyNickname(string nickname)
        {
            if (_nicknameLabel == null) return;
            if (string.IsNullOrEmpty(nickname))
            {
                // 非预期分支：拿不到档案 / 档案为空（服务端该玩家还没创角 / 档案请求失败）。
                if (!_nicknameWarned)
                {
                    _nicknameWarned = true;
                    Game.Logger?.Warn("MainMenuPanel",
                        "未拿到服务端昵称（Open 的 param 为空且 PlayerSession 无值）⇒ 按兜底策略显示本地默认名 " +
                        $"'{Cfg.Game.default_nick}'；请查 AppFlow 登录链 / GetProfile 是否走完（根因见 Core/PlayerSession.cs）");
                }
                nickname = Cfg.Game.default_nick;
            }
            // ⛔ 必须过 `TextFit`：昵称是**服务端数据**（长度不由本面板控制），而引擎的文本节点
            //    默认 `Wrap + Overflow`（`UIWidgets.cs:151-152`）⇒ 超长昵称换行后会画出标签矩形、
            //    **盖住整个面板**（D4 实测 preferredW 24576 > rectW 808）。
            //    ⛔ 不改字号（那是"糊过去"，D4 明令禁止）；uGUI 也没有 `horizontalOverflow=Truncate`
            //    这个取值（`HorizontalWrapMode` 只有 Wrap/Overflow）⇒ 只能按宽度裁**字符串**。
            _nicknameLabel.text = TextFit.Clamp(_nicknameLabel, nickname);
        }

        private void OnBattleStartFailed(string reason)
        {
            // 失败原因必须显示（同样不许只打日志）：最常见的是"还没编辑过卡组"
            //（服务端 `onAiBattleStart` 在 len(deck) != 8 时回落到档案卡组，档案卡组为空 ⇒ 回 err）。
            SetStatus(string.IsNullOrEmpty(reason) ? "人机对战启动失败（原因未知）" : reason, CrUiStyle.ErrorText);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text ?? string.Empty;
            _status.color = color;
        }
    }
}
