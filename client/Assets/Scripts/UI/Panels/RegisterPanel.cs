using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 注册（`Login` 站点的子面板，`Main` 场景，Normal 层）—— **竖版**。
    ///
    /// <para>
    /// 与 <see cref="LoginPanel"/> 同样的分工：面板只收输入 + 发事件 + 显示结果，
    /// ⛔ 不碰 `CloverAuth` / `Game.Net`。注册成功**即登录**（账号服注册接口直接签发 token），
    /// 因此注册成功后由 `AppFlow` 接着跑同一条登录链，面板不需要第二条分支。
    /// </para>
    /// <para>
    /// <b>为什么注册与登录是两个面板而不是一个带 Tab 的</b>：架构契约 §4 站点表把
    /// `LoginPanel` / `RegisterPanel` 列为两个面板（`Login` 站点的两个面孔），
    /// 且两者的按钮语义（"登录" vs "注册并登录"）与错误文案不同。
    /// </para>
    /// <para>
    /// <b>竖版布局与视觉</b>与 <see cref="LoginPanel"/> 同一套骨架（同面板高 / 同字段位 / 同按钮位 /
    /// 同原版图元），只有标题文案与两个按钮的语义不同 —— 两个界面的视觉必须一致，⛔ 不许各摆一套。
    /// 视觉的逐部件依据见 <see cref="LoginPanel"/> 的类注释，换帧表在 `.ai-tmp/test/AP2-量取.md`。
    /// </para>
    /// </summary>
    public sealed class RegisterPanel : UIPanel
    {
        /// <summary>面板高（像素）。与 <see cref="LoginPanel"/> 同值 —— 两个界面共用同一骨架（= 标题带 58 + 亮面体 696 + 描边 2）。</summary>
        private const float PanelH = 756f;

        /// <summary>弹窗内左右留白（= `CrUiStyle.PopupPad`，基线 14px@499 × 2.1643 = 30）。</summary>
        private const float Pad = CrUiStyle.PopupPad;

        /// <summary>亮面体内可用宽 = 弹窗宽 935 − 2×描边 − 2×留白 = 871。</summary>
        private const float InnerW = CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder - 2f * Pad;

        /// <summary>输入框高（= `CrUiStyle.FieldH`，A 字段带 66px@750 × 1.44 = 95）。</summary>
        private const float FieldH = CrUiStyle.FieldH;

        /// <summary>字段标签行高（= `CrUiStyle.PopupLabelH`）。</summary>
        private const float LabelH = CrUiStyle.PopupLabelH;

        /// <summary>标签与输入框之间的缝（= `CrUiStyle.PopupLabelGap`）。</summary>
        private const float LabelGap = CrUiStyle.PopupLabelGap;

        /// <summary>按钮高（= `CrUiStyle.PopupControlH`，基线按钮高 30px@499 × 2.1643 = 65）。</summary>
        private const float BtnH = CrUiStyle.PopupControlH;

        /// <summary>按钮宽（= `CrUiStyle.PopupControlW`，基线按钮宽 196px@499 × 2.1643 = 424）。</summary>
        private const float BtnW = CrUiStyle.PopupControlW;

        /// <summary>状态行高（垫板岩条用）。</summary>
        private const float StatusH = 72f;

        private const float GapL = 24f; // 段间大缝（自定）
        private const float GapM = 20f; // 段间中缝（自定）

        // 面板内纵向分段：与 LoginPanel 逐项同值（见那边的注释表）。
        private const float Field1LabelY = -Pad;                                            // -30
        private const float Field1Y = -(Pad + LabelH + LabelGap);                            // -76
        private const float Field2LabelY = -(Field1Y * -1f + FieldH + GapL);                 // -195
        private const float Field2Y = -(Field2LabelY * -1f + LabelH + LabelGap);             // -241
        private const float StatusY = -(Field2Y * -1f + FieldH + GapM);                      // -356
        private const float PrimaryButtonY = -(StatusY * -1f + StatusH + GapL);              // -452
        private const float SecondaryButtonY = -(PrimaryButtonY * -1f + BtnH + GapM);        // -537
        private const float HintY = -(SecondaryButtonY * -1f + BtnH + GapL);                 // -626

        /// <summary>按钮水平居中位（= (871 − 424)/2 = 223.5）。</summary>
        private const float ButtonX = (InnerW - BtnW) * 0.5f;

        /// <summary>账号最大长度（本项目自定，同 LoginPanel）。</summary>
        private const int MaxAccountChars = 32;

        /// <summary>密码最大长度（本项目自定，同 LoginPanel）。</summary>
        private const int MaxPasswordChars = 64;

        private bool _built;
        private InputField _account;
        private InputField _password;
        private Text _status;
        private Image _submitButton;
        private Image _backButton;

        private Action<string> _onLoginFailed;
        private Action<string> _onLoginSucceeded;

        /// <summary>注册画面在 Normal 层（架构契约 §4）。</summary>
        public override UILayer Layer => UILayer.Normal;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }
            Subscribe();
            SetInteractable(true);
            SetStatus("注册成功后会自动登录", CrUiStyle.TextDim);
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

            // 面板底 = 原版板岩外框（`ui_out` 014）+ 亮面体（019），居中（依据见 LoginPanel 类注释）。
            var box = CrUiStyle.SettingsPopup("RegisterBox", root, PanelH);
            var c = box.rectTransform.Find("RegisterBoxBody") as RectTransform;
            if (c == null)
            {
                // 非预期分支（必须留痕）：弹窗亮面体没建出来 ⇒ 后面的元素全落空。
                Game.Logger?.Error("RegisterPanel", "弹窗亮面体 RegisterBoxBody 没建出来，注册界面内容无法摆放");
                return;
            }

            // 标题：白字 + 黑描边压在弹窗自带板岩带上（基线「Settings」的写法）。
            CrUiStyle.Outlined("Title", box.rectTransform, "注册新账号", CrUiStyle.FontPopupTitle, Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CrUiStyle.PopupBorder, 0f),
                new Vector2(CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder, CrUiStyle.PopupTitleH),
                TextAnchor.MiddleCenter);

            // 亮面上的字 = 深墨蓝（基线亮面标签最暗像素 (42,44,46)）。
            UIFactory.CreateLabel("AccountLabel", c, "账号", CrUiStyle.FontBody,
                new Vector2(Pad, Field1LabelY), new Vector2(InnerW, LabelH),
                TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);
            _account = CrUiStyle.SlateField("AccountInput", c,
                new Vector2(Pad, Field1Y), new Vector2(InnerW, FieldH),
                "请输入账号", MaxAccountChars, "RegisterPanel");

            UIFactory.CreateLabel("PasswordLabel", c, "密码", CrUiStyle.FontBody,
                new Vector2(Pad, Field2LabelY), new Vector2(InnerW, LabelH),
                TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);
            _password = CrUiStyle.SlateField("PasswordInput", c,
                new Vector2(Pad, Field2Y), new Vector2(InnerW, FieldH),
                "请输入密码", MaxPasswordChars, "RegisterPanel");
            if (_password != null)
            {
                _password.contentType = InputField.ContentType.Password;
            }

            if (_account != null) _account.text = Cfg.Account.name_prefix + Cfg.Account.name_suffix;
            if (_password != null) _password.text = Cfg.Account.password;

            // 状态行：垫板岩条（原版"深色字段 + 亮字"的语言）再放状态色。
            CrUiStyle.Skin("StatusBar", c, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, StatusY),
                new Vector2(InnerW, StatusH), CrUiStyle.BandSlate, false);
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(Pad + GapM, StatusY + GapM), new Vector2(InnerW - 2f * GapM, StatusH - 2f * GapM),
                TextAnchor.MiddleCenter, CrUiStyle.TextDim);

            // 主按钮 = 原版蓝按钮（165）；次按钮 = 原版板岩（014）。
            _submitButton = CrUiStyle.BlueButton("SubmitButton", c, "注册并登录",
                new Vector2(ButtonX, PrimaryButtonY), new Vector2(BtnW, BtnH), OnSubmitClicked, Color.white);
            _backButton = CrUiStyle.SlateButton("BackButton", c, "返回登录",
                new Vector2(ButtonX, SecondaryButtonY), new Vector2(BtnW, BtnH), OnBackClicked, Color.white);

            UIFactory.CreateLabel("Hint", c, "账号仅用于本项目联机（服务端自建账号服）", CrUiStyle.FontSmall,
                new Vector2(Pad, HintY), new Vector2(InnerW, 40f),
                TextAnchor.MiddleCenter, CrUiStyle.TextOnLight);

            Game.Logger?.Info("RegisterPanel",
                "注册界面已打开（原版图元：面板底 ui_out/014+019 九宫格 · 标题带 014 · 输入框底 014 四角镜像 · " +
                "主按钮 165 蓝 · 次按钮 014 板岩）");
        }

        // ───────────────────────── 交互 ─────────────────────────

        private void OnSubmitClicked()
        {
            var account = _account != null ? _account.text : string.Empty;
            var password = _password != null ? _password.text : string.Empty;

            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                SetStatus("账号与密码都不能为空", CrUiStyle.ErrorText);
                Game.Logger?.Warn("RegisterPanel", "账号或密码为空，未发起注册");
                return;
            }

            SetInteractable(false);
            SetStatus("正在注册…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Flow.RegisterRequest, account, password);
        }

        private void OnBackClicked()
        {
            Game.Event?.Emit(Events.Flow.BackToLoginRequest);
        }

        // ───────────────────────── 订阅 ─────────────────────────

        private void Subscribe()
        {
            if (_onLoginFailed == null)
            {
                _onLoginFailed = OnLoginFailed;
                _onLoginSucceeded = OnLoginSucceeded;
            }
            // 注册失败与登录失败走同两条事件（注册成功后紧接着跑登录链，链路是同一条）。
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
            SetInteractable(true);
            SetStatus(string.IsNullOrEmpty(reason) ? "注册失败（原因未知）" : reason, CrUiStyle.ErrorText);
        }

        private void OnLoginSucceeded(string nickname)
        {
            SetInteractable(true);
            Game.Logger?.Info("RegisterPanel", $"注册并登录成功（nickname='{nickname}'）");
        }

        // ───────────────────────── 状态显示 ─────────────────────────

        private void SetInteractable(bool on)
        {
            var a = _submitButton != null ? _submitButton.GetComponent<Button>() : null;
            if (a != null) a.interactable = on;
            var b = _backButton != null ? _backButton.GetComponent<Button>() : null;
            if (b != null) b.interactable = on;
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text ?? string.Empty;
            _status.color = color;
        }
    }
}
