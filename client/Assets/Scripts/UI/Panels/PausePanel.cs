using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 暂停菜单（`Pause` 站点，Popup 层，架构契约 §4）：继续 / 设置 / 投降 / 回主菜单。
    ///
    /// <para>
    /// <b>依赖方向（契约 §1 硬线）</b>：本面板⛔**不许** `using CR.Module` —— 它只知道
    /// `Core/Events.cs` 的事件名与 `Core/Stations.cs` 的站点名：
    /// 投降 → `Emit(Events.Battle.SurrenderRequest)`（处理者是 agent-07 的 `BattleManager`，
    /// 它持有 `room_id` 并发 `MsgDef.BattleSurrender`）；设置 → `Emit(Events.Flow.OpenSettingsRequest)`；
    /// 回主菜单 → `Emit(Events.Battle.ReturnToMainMenuRequest)`；继续 → `Emit(Events.Flow.StationEnterRequest, Stations.Battle)`。
    /// </para>
    /// <para>
    /// <b>⛔ 本面板<u>不</u>订阅 `SurrenderRequest` 去自己发消息</b>：`Events.cs:195` 那条事件的
    /// **既有处理者**就是 `BattleManager`（`BattleManager.cs:235` → `SurrenderAsync()` →
    /// `MsgDef.BattleSurrender`）。本面板再挂一个处理者会让一次点击发出**两条**投降请求
    /// （服务端会拒绝其中一条并回一条失败提示）。分工：面板只发"请求"，C2S 一律由 Module 侧发。
    /// </para>
    /// <para>
    /// <b>⚠️ 对局不真暂停</b>（参考规格 §7 S22 / `AppFlow.EnterPause` 的注释）：进 `Pause` 站点
    /// **不动 `timeScale`**、不关下层 HUD，服务端的 tick 照跑（计时 / 圣水 / 兵线都在动）。
    /// 所以面板上必须把这件事**写出来**，⛔ 不许假装暂停。
    /// </para>
    /// <para>
    /// <b>二次确认</b>：投降与"回主菜单"（= 弃赛）都不可撤销，两者都走引擎自带的
    /// `Game.UI.Confirm`（`PresentationContracts.cs:111`）。确认框挂 `Top` 层、
    /// 在 `Popup` 之上且自带全屏射线遮罩（`UIWidgets.cs:618-622`），所以确认期间本面板的按钮点不动。
    /// </para>
    ///
    /// <para>
    /// <b>竖版排版</b>：画布 = <see cref="CrUiStyle.DesignW"/>×<see cref="CrUiStyle.DesignH"/> = 1080×1920、`match = 0`。
    /// 内容框 = <see cref="CrUiStyle.ContentPanel"/>（贴顶居中、宽 <see cref="CrUiStyle.ContentW"/> = 1000）、
    /// 标题 = <see cref="CrUiStyle.TitleBar"/>（原版金色标题条九宫格）、
    /// 「对局不真暂停」提示框 = 原版深蓝灰圆角块 <see cref="ResPaths.ButtonDarkGrey"/> 九宫格（⛔ 不再是纯色块）、
    /// 四个动作 = <see cref="CrUiStyle.ActionButton"/>（原版金 / 深蓝灰按钮底九宫格）。
    /// </para>
    ///
    /// <para>
    /// <b>⛔ 四个按钮单列纵向堆叠</b>（⛔ 不 2×2 左右排：两列并排只在 BoxW=860 的横屏口径下成立），
    /// 按钮宽 <see cref="BtnW"/> = 824（A 的宽条按钮 572px@750 宽 ×1.44 = 823.7）。
    /// </para>
    /// </summary>
    public sealed class PausePanel : UIPanel
    {
        private const string Tag = "PausePanel";

        // ═══════════════ 竖版排版常量（内容框宽 1000） ═══════════════

        private const float Pad = CrUiStyle.PanelPad;                    // 40（CrUiStyle 已登记）
        private const float InnerW = CrUiStyle.ContentW - 2f * Pad;      // 920 = 1000 − 2×40
        private const float GapM = 24f;                                  // 行间缝隙
        private const float GapL = 32f;                                  // 块间缝隙

        /// <summary>「对局不真暂停」提示框高（两行 <see cref="CrUiStyle.FontSmall"/> + 上下留白）。本项目新增界面自定。</summary>
        private const float WarnH = 120f;

        /// <summary>
        /// 动作按钮宽 = 824（= A 宽条按钮 572px@750 × 1.44 = 823.7，与 `DeckEditPanel` 的宽条按钮同宽）。
        /// <para>
        /// ⚠️ 572 这个数**没有直接对照物**：A 的暂停界面本身未取到（`策划/参考图/清单.md:40`），
        /// `12_主菜单` 里能量到的宽条只有「Battle Deck」蓝带（实测 y=1176 行 x=130..614 ⇒
        /// 485px@750 = 屏宽 64.7% ⇒ ×1.44 = **698.4**），与 572 差 +126px。
        /// </para>
        /// </summary>
        private const float BtnW = 824f;

        /// <summary>动作按钮高。出处同 <see cref="BtnW"/>：A 宽条按钮 h=46px@750 × 1.44 = 66.2 ⇒ 66。</summary>
        private const float BtnH = 66f;

        /// <summary>
        /// 动作按钮之间的缝。**本项目自定**：A 的暂停界面未取到（`策划/参考图/清单.md:40`），
        /// 12_主菜单 里也量不到"两个同宽按钮上下相邻"的样本 ⇒ **无原版值可比**，取 16 只为让四个按钮
        /// 连成一条可点列（登记进 `策划/验收表.md` 的「允许的差异」）。
        /// </summary>
        private const float BtnGap = 16f;

        private const float RulesH = 72f;                                // 规则说明（两行 FontSmall）
        private const float StatusH = 90f;                               // 状态行（投降在途 / 被拒原因）
        private const float HintH = 30f;                                 // 底部提示行

        /// <summary>暂停菜单是 Popup 层（架构契约 §4：暂停 = Popup）。</summary>
        public override UILayer Layer => UILayer.Popup;

        private bool _built;
        private Text _status;

        /// <summary>四个动作按钮的底图（用句柄而不是 `Transform.Find` —— 竖版重排后名字与层级的耦合少一层）。</summary>
        private Image _continueButton;
        private Image _settingsButton;
        private Image _surrenderButton;
        private Image _mainMenuButton;

        /// <summary>投降 / 回主菜单的在途标记：确认后按钮置灰，防连点（服务端也会拒，但别让它看着像没反应）。</summary>
        private bool _busy;

        /// <summary>订阅（OnOpen 挂 / OnClose 摘，成对）。</summary>
        private Action<string> _onFailed;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }

            _busy = false;
            SetInteractable(true);
            Subscribe();
            SetStatus("对局仍在进行：服务端不会因为打开这个菜单而停下。点「继续」回到战场。", CrUiStyle.TextOnLightDim);
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

            float yWarn = -(CrUiStyle.TitleBarH + GapM);                                 // −116
            float yButtons = yWarn - WarnH - GapM;                                       // −260
            float yRules = yButtons - 4f * BtnH - 3f * BtnGap - GapL;                    // −604
            float yStatus = yRules - RulesH - GapM;                                      // −700
            float yHint = yStatus - StatusH - GapM;                                      // −814
            float boxH = -yHint + HintH + Pad;                                           // 884

            // 弹窗自己不铺全屏底（Popup 层的模态遮罩由 UIManager 负责，`UI.cs:143-147`），只画内容框。
            //
            // 面板底 = `SettingsPopup`（`ui_out` 014 外框 + 019 亮面），标题 = `BandTitle`
            //   （板岩带上的白字黑描边）—— 原版功能面板的底是**板岩灰蓝外框 + 亮灰蓝面**、
            //   标题是板岩带上的白字黑描边（基线 `24_设置_499x1080.jpg` 实测亮面 (229,236,242) /
            //   板岩带 (99,104,123)）；与 `RoomList` / `Room` / `DeckEdit` 同一口径。
            //   ⛔ 不用 `ContentPanel`（806 米黄撕纸）/ `TitleBar`（069 深棕木条）。
            var box = CrUiStyle.SettingsPopup("PauseBox", root, boxH, CrUiStyle.ContentW);
            var c = box.rectTransform;

            CrUiStyle.BandTitle("TitleBar", box, "暂 停", CrUiStyle.ContentW);

            // ── 对局不真暂停：必须明说（⛔ 不许让玩家以为世界停了） ──
            // 提示框底 = 原版板岩灰蓝圆角块（`ui_out/014`）。
            // 014 是**左上圆角件**（`CrUiStyle.Skin` 的注释写明：直接配 `border` 会把同一个角
            //   贴到四个角上、三个角是错的）⇒ 从 `NineSlice` 改成 `Skin(corner 24)`（四角镜像拼，只用原版像素）。
            CrUiStyle.Skin("WarnBox", c, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, yWarn),
                new Vector2(InnerW, WarnH), CrUiStyle.FieldBg, false);

            // ⛔ 可见文案里不写 `**粗体**`：`UIFactory.CreateText` 把 `supportRichText` 置为 false
            //    （`UIWidgets.cs:153`）⇒ 富文本标记会被**原样显示**成星号。
            UIFactory.CreateLabel("WarnText", c,
                "注意：本作的对局不会真正暂停 —— 这个菜单只覆盖操作，服务端仍在推进" +
                "（计时、圣水、兵线、塔血都在动）。点「继续」立刻回到战场。",
                CrUiStyle.FontSmall, new Vector2(Pad + 18f, yWarn - 8f), new Vector2(InnerW - 36f, WarnH - 16f),
                TextAnchor.MiddleLeft, CrUiStyle.Accent);

            // ── 四个动作：竖版 = 单列纵向堆叠（⛔ 不再 2×2 左右排） ──
            // 四颗按钮的底从 `ActionButton`（金 300 / 深蓝灰 014）换成 **`BlueButton`（`ui_out/165` 蓝底白字）**。
            //   看图依据：300 = 亮黄金 3D 立体块（商店/宝箱语言）、014 = 板岩块，
            //   而原版**功能面板的按钮**是蓝底白字（基线 `24_设置` 的蓝按钮 / `12_主菜单` 的 Clan 钮，
            //   量取蓝 ≈ (24,119,233)~(77,175,254)）；「继续」是主操作 ⇒ 原版主操作也是同一蓝件。
            var btnX = (CrUiStyle.ContentW - BtnW) * 0.5f;                               // 88
            _continueButton = CrUiStyle.BlueButton("ContinueButton", c, "继 续",
                new Vector2(btnX, yButtons), new Vector2(BtnW, BtnH), OnContinueClicked);

            _settingsButton = CrUiStyle.BlueButton("SettingsButton", c, "设 置",
                new Vector2(btnX, yButtons - (BtnH + BtnGap)), new Vector2(BtnW, BtnH), OnSettingsClicked);

            _surrenderButton = CrUiStyle.BlueButton("SurrenderButton", c, "投 降",
                new Vector2(btnX, yButtons - 2f * (BtnH + BtnGap)), new Vector2(BtnW, BtnH),
                OnSurrenderClicked);

            _mainMenuButton = CrUiStyle.BlueButton("MainMenuButton", c, "回主菜单",
                new Vector2(btnX, yButtons - 3f * (BtnH + BtnGap)), new Vector2(BtnW, BtnH),
                OnMainMenuClicked);

            // ── 规则说明（把"投降 / 回主菜单意味着什么"写在按钮下面，⛔ 不做"点了才知道"） ──
            UIFactory.CreateLabel("Rules", c,
                "投降：立刻判你输掉这一局（不可撤销，会二次确认）。\n" +
                "回主菜单：对局中离开等于弃赛（不可撤销，会二次确认）。",
                CrUiStyle.FontSmall, new Vector2(Pad, yRules), new Vector2(InnerW, RulesH),
                TextAnchor.UpperLeft, CrUiStyle.TextOnLightDim);

            // 状态行（投降请求在途 / 被服务端拒绝的原因都在这里）。
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(Pad, yStatus), new Vector2(InnerW, StatusH), TextAnchor.UpperLeft, CrUiStyle.TextOnLightDim);

            UIFactory.CreateLabel("Hint", c, "（暂停菜单不改变对局结果，只提供操作入口）", CrUiStyle.FontSmall,
                new Vector2(0f, yHint), new Vector2(CrUiStyle.ContentW, HintH), TextAnchor.MiddleCenter,
                CrUiStyle.TextOnLightDim);
        }

        // ───────────────────────── 四个动作 ─────────────────────────

        /// <summary>
        /// 继续：关掉自己 + 把站点交回 `Battle`。
        /// <para>
        /// **为什么必须切回 `Battle` 站点**：`AppFlow.GoTo` 对"已是当前站点"的请求会**早退**
        /// （`AppFlow.cs:357`）⇒ 若把站点停在 `Pause`，下一次进暂停时 `GoTo(Pause)` 不切换、
        /// 也就不会广播 `StationChanged`。切回 `Battle` 会让 `EnterBattle` 走一遍
        /// `CloseAll` + `BattleManager.EnterBattleStation()`（重开 HUD 并补发开打配置 / 快照 / 卡池），
        /// 这正是"回到对局"要的收尾。
        /// </para>
        /// </summary>
        private void OnContinueClicked()
        {
            Game.Logger?.Info(Tag, "继续对局：关闭暂停菜单并回到 Battle 站点");

            // 先关自己再请求切站点：即使站点已经在 Battle（GoTo 早退、不切也不广播），
            // 面板也已经收干净，不会留下一个关不掉的弹窗。重复 Close 是安全的 ——
            // UIManager 按类型在 `_panels` 里找，已经被 CloseAll 摘掉的不会再销毁第二次。
            Game.UI?.Close<PausePanel>();
            Game.Event?.Emit(Events.Flow.StationEnterRequest, Stations.Battle);
        }

        /// <summary>
        /// 设置：走 `Events.Flow.OpenSettingsRequest`（`AppFlow.OpenSettings` 会带一份现读的
        /// `SettingsSnapshot` 打开 `SettingsPanel`）—— 面板⛔不引 `CR.Module`，拿不到设置值。
        /// <para>
        /// ⚠️ `SettingsPanel` 也是 Popup 层，而 `UIManager.Open` 对 Popup 会**互斥关闭同层面板**
        /// （`UI.cs:143-147`）⇒ 打开设置时本面板会被收掉。返回后站点仍是 `Pause`，玩家再点 HUD 上的
        /// 「暂停」即可重新打开本面板（`BattleUiHost` 监听的是**请求事件**，不是 `StationChanged`，
        /// 所以"站点没变"不会让它失灵）。
        /// </para>
        /// </summary>
        private void OnSettingsClicked()
        {
            Game.Logger?.Info(Tag, "打开设置（广播 Events.Flow.OpenSettingsRequest）");
            Game.Event?.Emit(Events.Flow.OpenSettingsRequest);
        }

        /// <summary>投降：必须二次确认（不可逆）。确认后只**发请求**，C2S 由 `BattleManager` 发。</summary>
        private void OnSurrenderClicked()
        {
            if (_busy) return;

            if (Game.UI == null)
            {
                // 非预期分支：表现域未挂载 ⇒ 连确认框都弹不出来。留痕，别让点击静默无反应。
                Game.Logger?.Error(Tag, "Game.UI 为空，无法弹投降确认框，本次投降未发出");
                SetStatus("界面未就绪，投降按钮暂时不可用（已记录）", CrUiStyle.ErrorText);
                return;
            }

            Game.UI.Confirm("确认投降", "投降会立刻判你输掉这一局，不可撤销。确定要投降吗？",
                () =>
                {
                    _busy = true;
                    SetInteractable(false);
                    Game.Logger?.Info(Tag, "已确认投降，请求对局模块发 MsgBattleSurrender");
                    SetStatus("已请求投降，等待服务端结算…（结算面板会自动出现）", CrUiStyle.Accent);
                    // ⛔ 这里**不**直接发 C2S：`Events.Battle.SurrenderRequest` 的处理者是
                    //    agent-07 的 `BattleManager`（它持有 room_id）。面板再发一次就会重复请求。
                    Game.Event?.Emit(Events.Battle.SurrenderRequest);
                },
                () => SetStatus("已取消投降，对局继续。", CrUiStyle.TextOnLightDim),
                "投 降", "继续战斗");
        }

        /// <summary>回主菜单：对局中离开 = 弃赛，必须二次确认（不可逆）。</summary>
        private void OnMainMenuClicked()
        {
            if (_busy) return;

            if (Game.UI == null)
            {
                // 非预期分支：表现域未挂载 ⇒ 连确认框都弹不出来。留痕，别让点击静默无反应。
                Game.Logger?.Error(Tag, "Game.UI 为空，无法弹回主菜单确认框，本次请求未发出");
                SetStatus("界面未就绪，回主菜单按钮暂时不可用（已记录）", CrUiStyle.ErrorText);
                return;
            }

            Game.UI.Confirm("确认离开对局",
                "对局中回主菜单等于弃赛（这一局判你输，且不可撤销）。确定要离开吗？",
                () =>
                {
                    _busy = true;
                    SetInteractable(false);
                    Game.Logger?.Info(Tag, "已确认回主菜单（弃赛），请求 AppFlow 执行");
                    SetStatus("正在返回主菜单…", CrUiStyle.Accent);
                    // AppFlow 订阅了这条事件（`AppFlow.cs:297/316`）⇒ 读条 + 加载 `Main` + 切 `MainMenu`。
                    Game.Event?.Emit(Events.Battle.ReturnToMainMenuRequest);
                },
                () => SetStatus("已取消，留在对局中。", CrUiStyle.TextOnLightDim),
                "离 开", "继续战斗");
        }

        // ───────────────────────── 订阅（OnOpen 挂 / OnClose 摘，成对） ─────────────────────────

        private void Subscribe()
        {
            if (_onFailed == null) _onFailed = OnBattleFailed;

            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：引擎没 Launch（或已 Shutdown）。留痕 —— 否则表现为"投降被拒却毫无提示"。
                Game.Logger?.Error(Tag, "Game.Event 为空（引擎未 Launch？），暂停菜单收不到对局失败原因");
                return;
            }

            // 幂等：`UIManager.Open` 对已打开的面板会再次调 `OnOpen`（`UI.cs:107-117`）。
            bus.Off(Events.Battle.StartFailed, _onFailed);
            bus.On(Events.Battle.StartFailed, _onFailed);
        }

        private void Unsubscribe()
        {
            if (_onFailed == null) return;
            Game.Event?.Off(Events.Battle.StartFailed, _onFailed);
        }

        /// <summary>
        /// 对局操作失败（`Events.cs:201`）。⚠️ `BattleManager.Fail` 复用了这一条事件承载
        /// "对局操作失败原因"（它自己的注释里写明了，`BattleManager.cs:887-895`）⇒
        /// 投降被拒（对局已结束 / 已不在这一局）会从这里回到玩家眼前。
        /// </summary>
        private void OnBattleFailed(string reason)
        {
            var text = string.IsNullOrEmpty(reason) ? "对局操作失败（服务端未给出原因）" : reason;
            _busy = false;
            SetInteractable(true);
            SetStatus(text, CrUiStyle.ErrorText);
        }

        // ───────────────────────── 小工具 ─────────────────────────

        /// <summary>
        /// 四个按钮一起置灰 / 恢复（投降与回主菜单在途时不许再点）。
        /// <para>
        /// ⛔ **不自己涂 `img.color`**：按钮底是**原版图元九宫格 + `Button.colors` 四态 tint**
        /// （见 `CrUiStyle.ActionButton`），直接写 `img.color` 会与 Button 的状态机互相覆盖
        /// （`DoStateTransition` 会把颜色刷回 `normalColor`，表现为"置灰一闪就没"）。
        /// 禁用态由 `colors.disabledColor`（0.55 灰）表达，所以这里只切 `interactable`。
        /// </para>
        /// </summary>
        private void SetInteractable(bool enabled)
        {
            SetButtonInteractable(_continueButton, enabled);
            SetButtonInteractable(_settingsButton, enabled);
            SetButtonInteractable(_surrenderButton, enabled);
            SetButtonInteractable(_mainMenuButton, enabled);
        }

        private static void SetButtonInteractable(Image img, bool enabled)
        {
            if (img == null) return;
            var btn = img.GetComponent<Button>();
            if (btn == null)
            {
                // 非预期分支：按钮底图上没有 Button 组件 ⇒ 这颗按钮永远点不动（禁用/恢复也无意义）。留痕。
                Game.Logger?.Warn(Tag, "按钮 " + img.name + " 上没有 Button 组件，点击会失效");
                return;
            }
            btn.interactable = enabled;
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            // 拒因是**服务端 err 原文**（长度不受本面板控制）⇒ 过 TextFit，⛔ 不让它盖住面板（D4 同族）。
            _status.text = TextFit.Clamp(_status, text);
            _status.color = color;
        }
    }
}
