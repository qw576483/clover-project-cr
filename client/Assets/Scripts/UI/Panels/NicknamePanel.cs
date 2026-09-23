using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 创角（`Nickname` 站点，`Main` 场景，Normal 层）—— **竖版**：填昵称 → C2S `MsgSetNickname` → 主菜单。
    ///
    /// <para>
    /// <b>为什么需要这一步</b>：服务端 `PlayerSchema` 的 `nickname` 是房间列表 / 房间成员要展示的
    /// 玩家名（`RoomInfo.host` / `RoomMember.nickname`），空昵称会让别人看到一片空白；
    /// 所以登录成功后先拉一次档案（`MsgGetProfile`），昵称为空就进这里。
    /// </para>
    /// <para>
    /// <b>长度上限与服务端一致</b>：服务端 `logic/player.go` 判 `utf8.RuneCountInString(nickname)` ∈ [1,16]，
    /// 超了回 `SetNicknameReply{ok=false, err="昵称需为 1~16 个字符"}`，该 err 由 `AppFlow` 经
    /// `Events.Flow.NicknameFailed` 送到本面板显示（本地不复制一份校验规则，避免两边口径漂移）。
    /// 输入框的 `characterLimit = 16` 只是**界面防误输**。
    /// </para>
    /// <para>
    /// <b>竖版布局与视觉</b>：与 <see cref="LoginPanel"/> 同一套骨架（同弹窗宽 935 / 同留白 30 /
    /// 同字段高 95 / 同按钮几何 424×65 / 同原版图元），只是只有一段字段（昵称），所以弹窗矮一截。
    /// 逐部件依据见 <see cref="LoginPanel"/> 的类注释，换帧表在 `.ai-tmp/test/AP2-量取.md`。
    /// </para>
    /// </summary>
    public sealed class NicknamePanel : UIPanel
    {
        /// <summary>面板高（像素）= 标题带 58 + 亮面体 386 + 外框描边 2（分段表见下）。</summary>
        private const float PanelH = 446f;

        /// <summary>弹窗内左右留白（= `CrUiStyle.PopupPad`，基线 14px@499 × 2.1643 = 30）。</summary>
        private const float Pad = CrUiStyle.PopupPad;

        /// <summary>亮面体内可用宽 = 935 − 4 − 60 = 871。</summary>
        private const float InnerW = CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder - 2f * Pad;

        /// <summary>输入框高（= `CrUiStyle.FieldH`，A 字段带 66px@750 × 1.44 = 95）。</summary>
        private const float FieldH = CrUiStyle.FieldH;

        /// <summary>字段标签行高（= `CrUiStyle.PopupLabelH`）。</summary>
        private const float LabelH = CrUiStyle.PopupLabelH;

        /// <summary>标签与输入框之间的缝（= `CrUiStyle.PopupLabelGap`）。</summary>
        private const float LabelGap = CrUiStyle.PopupLabelGap;

        /// <summary>按钮高（= `CrUiStyle.PopupControlH`，基线按钮高 65）。</summary>
        private const float BtnH = CrUiStyle.PopupControlH;

        /// <summary>按钮宽（= `CrUiStyle.PopupControlW`，基线按钮宽 424）。</summary>
        private const float BtnW = CrUiStyle.PopupControlW;

        /// <summary>状态行高（垫板岩条用）。</summary>
        private const float StatusH = 72f;

        private const float GapL = 24f; // 段间大缝（自定）
        private const float GapM = 20f; // 段间中缝（自定）

        // ── 亮面体内纵向分段（左上角口径，y 为负 = 向下）──
        //   -30  ..  -70   标签「昵称」
        //   -76  .. -171   昵称输入框
        //  -195  .. -267   状态条（板岩条）
        //  -291  .. -356   主按钮（蓝底 165，几何 = 原版按钮 424×65）
        //  -356  .. -386   底部留白 30
        private const float FieldLabelY = -Pad;                             // -30
        private const float FieldY = -(Pad + LabelH + LabelGap);             // -76
        private const float StatusY = -(FieldY * -1f + FieldH + GapL);       // -195
        private const float PrimaryButtonY = -(StatusY * -1f + StatusH + GapL); // -291

        /// <summary>按钮水平居中位（= (871 − 424)/2 = 223.5）。</summary>
        private const float ButtonX = (InnerW - BtnW) * 0.5f;

        /// <summary>
        /// 昵称最大长度 —— 与服务端 `logic/player.go` 的 `maxNicknameRunes` 同值（16）。
        /// </summary>
        private const int MaxNicknameChars = 16;

        private bool _built;
        private InputField _nickname;
        private Text _status;
        private Image _submitButton;

        private Action<string> _onFailed;

        /// <summary>创角画面在 Normal 层（架构契约 §4）。</summary>
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
            SetStatus("给自己起个名字（1~16 个字符）", CrUiStyle.TextDim);
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
            var box = CrUiStyle.SettingsPopup("NicknameBox", root, PanelH);
            var c = box.rectTransform.Find("NicknameBoxBody") as RectTransform;
            if (c == null)
            {
                // 非预期分支（必须留痕）：弹窗亮面体没建出来 ⇒ 后面的元素全落空。
                Game.Logger?.Error("NicknamePanel", "弹窗亮面体 NicknameBoxBody 没建出来，创角界面内容无法摆放");
                return;
            }

            // 标题：白字 + 黑描边压在弹窗自带板岩带上（基线「Settings」的写法）。
            CrUiStyle.Outlined("Title", box.rectTransform, "创建角色", CrUiStyle.FontPopupTitle, Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CrUiStyle.PopupBorder, 0f),
                new Vector2(CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder, CrUiStyle.PopupTitleH),
                TextAnchor.MiddleCenter);

            // 亮面上的字 = 深墨蓝（基线亮面标签最暗像素 (42,44,46)）。
            UIFactory.CreateLabel("NicknameLabel", c, "昵称", CrUiStyle.FontBody,
                new Vector2(Pad, FieldLabelY), new Vector2(InnerW, LabelH),
                TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);
            _nickname = CrUiStyle.SlateField("NicknameInput", c,
                new Vector2(Pad, FieldY), new Vector2(InnerW, FieldH),
                "请输入昵称", MaxNicknameChars, "NicknamePanel");
            if (_nickname != null)
            {
                // 默认值取配置（⛔ 不许在代码里写昵称字面量）：`Cfg.Game.default_nick`。
                _nickname.text = Cfg.Game.default_nick;
            }

            // 状态行：垫板岩条（原版"深色字段 + 亮字"的语言）再放状态色。
            CrUiStyle.Skin("StatusBar", c, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, StatusY),
                new Vector2(InnerW, StatusH), CrUiStyle.BandSlate, false);
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(Pad + GapM, StatusY + GapM), new Vector2(InnerW - 2f * GapM, StatusH - 2f * GapM),
                TextAnchor.MiddleCenter, CrUiStyle.TextDim);

            // 主按钮 = 原版蓝按钮（`ui_out` 165）+ 白字黑描边，几何 = 原版 424×65。
            _submitButton = CrUiStyle.BlueButton("SubmitButton", c, "进入游戏",
                new Vector2(ButtonX, PrimaryButtonY), new Vector2(BtnW, BtnH), OnSubmitClicked, Color.white);

            Game.Logger?.Info("NicknamePanel",
                "创角界面已打开（原版图元：面板底 ui_out/014+019 九宫格 · 标题带 014 · 输入框底 014 四角镜像 · " +
                "主按钮 165 蓝）");
        }

        private void OnSubmitClicked()
        {
            var nickname = _nickname != null ? _nickname.text : string.Empty;
            nickname = nickname != null ? nickname.Trim() : string.Empty;

            if (nickname.Length == 0)
            {
                SetStatus("昵称不能为空", CrUiStyle.ErrorText);
                Game.Logger?.Warn("NicknamePanel", "昵称为空，未发起 SetNickname");
                return;
            }

            SetInteractable(false);
            SetStatus("正在创建…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Flow.NicknameSubmit, nickname);
        }

        // ───────────────────────── 订阅 ─────────────────────────

        private void Subscribe()
        {
            if (_onFailed == null) _onFailed = OnFailed;
            Game.Event?.Off(Events.Flow.NicknameFailed, _onFailed);
            Game.Event?.On(Events.Flow.NicknameFailed, _onFailed);
        }

        private void Unsubscribe()
        {
            if (_onFailed != null) Game.Event?.Off(Events.Flow.NicknameFailed, _onFailed);
        }

        private void OnFailed(string reason)
        {
            SetInteractable(true);
            SetStatus(string.IsNullOrEmpty(reason) ? "创建失败（原因未知）" : reason, CrUiStyle.ErrorText);
        }

        private void SetInteractable(bool on)
        {
            var btn = _submitButton != null ? _submitButton.GetComponent<Button>() : null;
            if (btn != null) btn.interactable = on;
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text ?? string.Empty;
            _status.color = color;
        }
    }
}
