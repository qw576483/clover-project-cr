using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 登录（`Login` 站点，`Main` 场景，Normal 层）—— **竖版**。
    ///
    /// <para>
    /// <b>面板只做两件事</b>：① 收账号密码并 `Emit(Events.Flow.LoginRequest)`；
    /// ② 订阅 `Events.Flow.LoginFailed(reason)` / `LoginSucceeded(nickname)` 把结果**显示在面板上**。
    /// ⛔ 不 `using CR.Module`、不调 `CloverAuth` / `Game.Net`（那是 `AppFlow` 的事）——
    /// 登录链因此只有一处实现，换皮肤不动链路。
    /// </para>
    ///
    /// <para>
    /// <b>失败原因必须可见</b>（任务要求：不许只打日志）：`LoginFailed` 的 reason 直接写进
    /// `_status` 文本并染成错误色。
    /// </para>
    ///
    /// <para>
    /// <b>账号密码预填</b>：打开时用 `Cfg.Account`（`Assets/Configs/config.json`）预填。
    /// 配置是账号信息的唯一定义处（⛔ 代码里不许有账号/密码字面量）。
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 如实登记：A 本体没有"账号密码登录"界面</b>（原版走 Supercell ID，
    /// `策划/参考图/清单.md` §2「未取到：登录页(Supercell ID)」）⇒ 本界面是**本项目新增**，
    /// ⛔ 不是原版复刻。它的视觉语言**逐部件对齐 A 的同类部件**（AP2 本片重做，见换帧表
    /// `.ai-tmp/test/AP2-量取.md`）：
    /// <list type="bullet">
    /// <item>面板底 = `ui_out` **014** 板岩外框 + **019** 亮面体（`CrUiStyle.SettingsPopup`）——
    /// 依据 = 基线 `24_设置_499x1080.jpg` 弹窗底实测 (229,236,242) / 外框 (99,104,123)；</item>
    /// <item>标题 = 白字 + 黑描边压在弹窗自带板岩带上（基线「Settings」的写法）；</item>
    /// <item>输入框底 = `ui_out` **014** 四角镜像九宫格（基线字段行「API Token」= 深板岩圆角块 + 白字）；</item>
    /// <item>主按钮 = 蓝底 `ui_out` **165**（基线 Language/Help/Privacy/Terms 的蓝按钮）；</item>
    /// <item>次按钮 = 板岩 `ui_out` **014**（基线灰按钮行）。</item>
    /// </list>
    /// ⛔ 旧实现用的是米色羊皮纸面板底（`ui_out` 806）+ 棕金木色标题条（`ui_out` 069）+ 金色按钮——
    /// 那三件与 A 的弹窗语言**色相都不同**，正是用户 2026-09-2x 说的「原版，界面 按钮 根本不长这样」。
    /// 排布尺度（面板高、内留白、按钮位置）是新增界面的自定值，逐条登记在
    /// `.ai-tmp/test/AP2-允许差异.md`。
    /// </para>
    /// </summary>
    public sealed class LoginPanel : UIPanel
    {
        // ── 面板几何：全部取自 A 的实测值（出处见下），⛔ 不编数 ──
        //   面板高 = 标题带 58 + 亮面体 696 + 外框描边 2（见下面的纵向分段表）
        private const float PanelH = 756f;

        /// <summary>弹窗内左右留白。基线 `24_设置`：面板左 33、首个按钮左 47 ⇒ 14px@499 × 2.1643 = <b>30</b>（= `CrUiStyle.PopupPad`）。</summary>
        private const float Pad = CrUiStyle.PopupPad;

        /// <summary>亮面体内的可用宽 = 弹窗宽 − 2×外框描边 − 2×留白 = 935 − 4 − 60 = <b>871</b>。</summary>
        private const float InnerW = CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder - 2f * Pad;

        /// <summary>输入框高。A 字段带实测 66px@750 × 1.44 = <b>95</b>（= `CrUiStyle.FieldH`）。</summary>
        private const float FieldH = CrUiStyle.FieldH;

        /// <summary>字段标签行高（= `CrUiStyle.PopupLabelH`，基线标签与控件同构）。</summary>
        private const float LabelH = CrUiStyle.PopupLabelH;

        /// <summary>标签与输入框之间的缝（= `CrUiStyle.PopupLabelGap`）。</summary>
        private const float LabelGap = CrUiStyle.PopupLabelGap;

        /// <summary>按钮高。基线 `24_设置` 绿 ON / 蓝按钮 h=30px@499 × 2.1643 = <b>65</b>（= `CrUiStyle.PopupControlH`）。</summary>
        private const float BtnH = CrUiStyle.PopupControlH;

        /// <summary>按钮宽。基线同上一行 w=196px@499 × 2.1643 = <b>424</b>（= `CrUiStyle.PopupControlW`）。</summary>
        private const float BtnW = CrUiStyle.PopupControlW;

        /// <summary>状态行高（垫板岩条用；容纳 FontSmall 一行 + 上下留白）。</summary>
        private const float StatusH = 72f;

        private const float GapL = 24f; // 段间大缝（自定）
        private const float GapM = 20f; // 段间中缝（自定）

        // ── 面板亮面体内的纵向分段（左上角口径，y 为负 = 向下；每段来历写在左侧）──
        //   -30  .. -70   标签「账号」（LabelH=40，压在输入框上方，与原版 Music/SFx 同构）
        //   -76  .. -171  账号输入框（FieldH=95）
        //  -195  .. -235  标签「密码」
        //  -241  .. -336  密码输入框
        //  -356  .. -428  状态条（板岩条 StatusH=72）
        //  -452  .. -517  主按钮（BtnH=65，蓝底 165，原版按钮几何）
        //  -537  .. -602  次按钮（板岩 014）
        //  -626  .. -666  提示行（40）
        //  -666  .. -696  底部留白 30
        private const float Field1LabelY = -Pad;                                            // -30
        private const float Field1Y = -(Pad + LabelH + LabelGap);                            // -76
        private const float Field2LabelY = -(Field1Y * -1f + FieldH + GapL);                 // -195
        private const float Field2Y = -(Field2LabelY * -1f + LabelH + LabelGap);             // -241
        private const float StatusY = -(Field2Y * -1f + FieldH + GapM);                      // -356
        private const float PrimaryButtonY = -(StatusY * -1f + StatusH + GapL);              // -452
        private const float SecondaryButtonY = -(PrimaryButtonY * -1f + BtnH + GapM);        // -537
        private const float HintY = -(SecondaryButtonY * -1f + BtnH + GapL);                 // -626

        /// <summary>按钮水平居中：亮面体内宽 871、按钮宽 424 ⇒ x = 223.5（= 原版按钮在弹窗里的居中位）。</summary>
        private const float ButtonX = (InnerW - BtnW) * 0.5f;

        /// <summary>账号最大长度。本项目自定（仅为输入框限长，避免误粘贴超长串）。</summary>
        private const int MaxAccountChars = 32;

        /// <summary>密码最大长度。本项目自定（同上，与服务端账号服的校验无关）。</summary>
        private const int MaxPasswordChars = 64;

        private bool _built;
        private InputField _account;
        private InputField _password;
        private Text _status;
        private Image _loginButton;
        private Image _registerButton;

        private Action<string> _onLoginFailed;
        private Action<string> _onLoginSucceeded;

        /// <summary>登录画面在 Normal 层（架构契约 §4 站点表）。</summary>
        public override UILayer Layer => UILayer.Normal;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }
            Subscribe();
            SetBusy(false, null);

            // param = 上一次失败的原因（`AppFlow` 在"失败/被踢后切回登录站点"时带进来）。
            // 为什么不能只靠 `LoginFailed` 事件：那时面板还没打开，事件无人接收 ⇒ 玩家只看到一个
            // 干净的登录框、不知道刚才发生了什么。
            var carriedError = param as string;
            if (!string.IsNullOrEmpty(carriedError))
            {
                SetStatus(carriedError, CrUiStyle.ErrorText);
            }
            else if (_status != null && string.IsNullOrEmpty(_status.text))
            {
                SetStatus("请输入账号与密码", CrUiStyle.TextDim);
            }
        }

        public override void OnClose()
        {
            Unsubscribe();
        }

        // ───────────────────────── 视觉树（D1：面板在 OnOpen 里用 UIFactory 自建） ─────────────────────────

        private void Build()
        {
            var root = (RectTransform)transform;
            // 面板自身必须铺满：UIManager 挂面板时不给尺寸，默认 100×100 的 RectTransform
            // 会让所有元素挤在屏幕中央一个小方块里。
            UIFactory.Stretch(root);

            CrUiStyle.Screen("Bg", root);
            CrUiStyle.SpriteBackground("BgArt", root, ResPaths.BootBackground);

            // ★ 面板底：原版**板岩外框（`ui_out` 014）+ 亮面体（`ui_out` 019）**，水平+垂直居中
            //   （基线 `24_设置` 弹窗 x=33..464 @屏宽 499 ⇒ 居中；y=166..905 @1080 ⇒ 居中）。
            //   ⛔ 不再用 `ContentPanel`（米色羊皮纸 806）+ `TitleBar`（棕金木色 069）。
            var box = CrUiStyle.SettingsPopup("LoginBox", root, PanelH);
            var c = box.rectTransform.Find("LoginBoxBody") as RectTransform;
            if (c == null)
            {
                // 非预期分支（必须留痕）：弹窗亮面体没建出来 ⇒ 后面的元素全落空。
                Game.Logger?.Error("LoginPanel", "弹窗亮面体 LoginBoxBody 没建出来，登录界面内容无法摆放");
                return;
            }

            // ★ 标题：白字 + 黑描边，压在弹窗自带的**顶部板岩带**上（基线「Settings」的写法）。
            CrUiStyle.Outlined("Title", box.rectTransform, "登 录", CrUiStyle.FontPopupTitle, Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CrUiStyle.PopupBorder, 0f),
                new Vector2(CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder, CrUiStyle.PopupTitleH),
                TextAnchor.MiddleCenter);

            // ★ 字段标签 + 输入框：亮面上的字是**深墨蓝**（基线亮面标签最暗像素 (42,44,46)）；
            //   输入框底 = 原版板岩圆角框（014 四角镜像九宫格，⛔ 不是纯色矩形）。
            UIFactory.CreateLabel("AccountLabel", c, "账号", CrUiStyle.FontBody,
                new Vector2(Pad, Field1LabelY), new Vector2(InnerW, LabelH),
                TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);
            _account = CrUiStyle.SlateField("AccountInput", c,
                new Vector2(Pad, Field1Y), new Vector2(InnerW, FieldH),
                "请输入账号", MaxAccountChars, "LoginPanel");

            UIFactory.CreateLabel("PasswordLabel", c, "密码", CrUiStyle.FontBody,
                new Vector2(Pad, Field2LabelY), new Vector2(InnerW, LabelH),
                TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);
            _password = CrUiStyle.SlateField("PasswordInput", c,
                new Vector2(Pad, Field2Y), new Vector2(InnerW, FieldH),
                "请输入密码", MaxPasswordChars, "LoginPanel");
            if (_password != null)
            {
                // 标准 uGUI 掩码（引擎的 CreateInputField 不设 contentType，默认 Standard 是明文）。
                _password.contentType = InputField.ContentType.Password;
            }

            // 预填（见类注释：账号信息的唯一定义处是 Cfg）。
            if (_account != null) _account.text = Cfg.Account.name_prefix + Cfg.Account.name_suffix;
            if (_password != null) _password.text = Cfg.Account.password;

            // ★ 状态行：先垫一条**板岩条**（原版"深色字段 + 亮字"的语言）再放状态色 ——
            //   `TextDim` / `Accent` / `ErrorText` 三个状态色都是为暗底设计的，压亮面 (229,236,242)
            //   对比度极低（AO1 在主菜单上实测过同一坑：状态字直接看不见）。
            CrUiStyle.Skin("StatusBar", c, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, StatusY),
                new Vector2(InnerW, StatusH), CrUiStyle.BandSlate, false);
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(Pad + GapM, StatusY + GapM), new Vector2(InnerW - 2f * GapM, StatusH - 2f * GapM),
                TextAnchor.MiddleCenter, CrUiStyle.TextDim);

            // ★ 主按钮 = 原版蓝按钮（`ui_out` 165）+ 白字黑描边；次按钮 = 原版板岩（`ui_out` 014）。
            _loginButton = CrUiStyle.BlueButton("LoginButton", c, "登 录",
                new Vector2(ButtonX, PrimaryButtonY), new Vector2(BtnW, BtnH), OnLoginClicked, Color.white);
            _registerButton = CrUiStyle.SlateButton("RegisterButton", c, "注册新账号",
                new Vector2(ButtonX, SecondaryButtonY), new Vector2(BtnW, BtnH), OnRegisterClicked, Color.white);

            // ★ 提示行：亮面上用深墨蓝（不是浅灰 —— 浅灰压亮面读不出）。
            UIFactory.CreateLabel("Hint", c, "请先启动服务端（网关 8002 / 账号服 8051）", CrUiStyle.FontSmall,
                new Vector2(Pad, HintY), new Vector2(InnerW, 40f),
                TextAnchor.MiddleCenter, CrUiStyle.TextOnLight);

            Game.Logger?.Info("LoginPanel",
                "登录界面已打开（原版图元：面板底 ui_out/014+019 九宫格 · 标题带 014 · 输入框底 014 四角镜像 · " +
                "主按钮 165 蓝 · 次按钮 014 板岩）");
        }

        // ───────────────────────── 交互 ─────────────────────────

        private void OnLoginClicked()
        {
            var account = _account != null ? _account.text : string.Empty;
            var password = _password != null ? _password.text : string.Empty;

            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                // 非预期分支（用户误操作）：就地提示、不发请求 —— 空账号密码到账号服必然是 401，
                // 发出去只会得到一条更难懂的远端文案。
                SetStatus("账号与密码都不能为空", CrUiStyle.ErrorText);
                Game.Logger?.Warn("LoginPanel", "账号或密码为空，未发起登录");
                return;
            }

            SetBusy(true, "正在登录…");
            Game.Event?.Emit(Events.Flow.LoginRequest, account, password);
        }

        private void OnRegisterClicked()
        {
            // 只发"我要去注册"，由 AppFlow 决定关谁开谁（面板不切站点，契约 §1）。
            Game.Event?.Emit(Events.Flow.OpenRegisterRequest);
        }

        // ───────────────────────── 订阅 ─────────────────────────

        private void Subscribe()
        {
            if (_onLoginFailed == null)
            {
                _onLoginFailed = OnLoginFailed;
                _onLoginSucceeded = OnLoginSucceeded;
            }
            // 幂等：UIManager 对已打开的面板会再次回调 OnOpen，重复 On 会让一次失败走两遍处理
            //（Event 不对"同一 handler 重复订阅"去重）。
            Game.Event?.Off(Events.Flow.LoginFailed, _onLoginFailed);
            Game.Event?.Off(Events.Flow.LoginSucceeded, _onLoginSucceeded);
            Game.Event?.On(Events.Flow.LoginFailed, _onLoginFailed);
            Game.Event?.On(Events.Flow.LoginSucceeded, _onLoginSucceeded);
        }

        private void Unsubscribe()
        {
            if (_onLoginFailed != null) Game.Event?.Off(Events.Flow.LoginFailed, _onLoginFailed);
            if (_onLoginSucceeded != null) Game.Event?.Off(Events.Flow.LoginSucceeded, _onLoginSucceeded);
        }

        private void OnLoginFailed(string reason)
        {
            SetBusy(false, null);
            SetStatus(string.IsNullOrEmpty(reason) ? "登录失败（原因未知）" : reason, CrUiStyle.ErrorText);
        }

        private void OnLoginSucceeded(string nickname)
        {
            SetBusy(false, null);
            Game.Logger?.Info("LoginPanel",
                $"登录成功（nickname='{nickname}'），由 AppFlow 决定进创角还是主菜单");
        }

        // ───────────────────────── 状态显示 ─────────────────────────

        private void SetBusy(bool busy, string text)
        {
            if (_loginButton != null)
            {
                var btn = _loginButton.GetComponent<Button>();
                if (btn != null) btn.interactable = !busy;
            }
            if (_registerButton != null)
            {
                var btn = _registerButton.GetComponent<Button>();
                if (btn != null) btn.interactable = !busy;
            }
            if (busy) SetStatus(text, CrUiStyle.Accent);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text ?? string.Empty;
            _status.color = color;
        }
    }
}
