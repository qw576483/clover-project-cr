using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 设置（任意站点可开的 Popup 层面板）：背景音乐 / 音效。
    ///
    /// <para>
    /// <b>面板只显示 + 发请求</b>：初值来自打开时传入的 <see cref="SettingsSnapshot"/>（由 `AppFlow`
    /// 从 `SettingsManager` 现读），改动一律 `Emit(Events.Settings.*Request)`
    /// —— ⛔ 面板不 `using CR.Module`、不持 `SettingsManager`。真正生效与落盘在 `SettingsManager`。
    /// </para>
    /// <para>
    /// <b>快照只是初值，打开期间跟随权威值</b>。只读打开那一刻的快照，会让面板在"权威值随后变了"时
    /// 显示旧值（典型：音频设备切换后引擎把分组音量同步下来）。
    /// 因此面板在 <see cref="Subscribe"/>
    /// 里订阅 `BgmVolumeChanged` / `SfxVolumeChanged`，
    /// 事件一到就地刷新显示（<see cref="OnClose"/> 里摘掉）。刷新一律走
    /// `SetValueWithoutNotify` / 直接改文字 ⇒ **不会反手发出请求**、不构成回环。
    /// </para>
    /// <para>
    /// <b>图层 = Popup</b>（架构契约 §4：设置 = Popup，任意站点可开）。Popup 会触发 UIManager 的
    /// 模态遮罩与"同层互斥"，因此它天然盖住下层站点面板；也正因此**同一时刻只有一个设置面板**。
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 视觉语言 = 原版设置界面</b>（基线图 = `策划/参考图/24_设置_499x1080.jpg`，
    /// 原版设置界面整屏 499×1080，
    /// 来源 https://www.gameuidatabase.com/uploads/Clash-Royale01022022-071826-52305.jpg）。
    /// 选帧口径 = **先取设置界面基线图，再按外观比对选帧**，逐点取样 + 按颜色连通块量取：
    /// ⛔ 不按 `策划/原版UI素材索引.md` 的「建议用途」列挑帧（那一列是按缩略图猜的）；
    /// ⛔ 不用**加载条图元** `loading_out` 015 当滑条填充（`ui_bars` 绿色读条）。
    /// </para>
    ///
    /// <para>
    /// <b>基线图上量到的原版观感（读数见 <see cref="CrUiStyle"/> 的对应用色常量）</b>：
    /// 弹窗体是**亮灰蓝 (229,236,242)**；顶部**板岩灰蓝标题带 (99,104,123)**；
    /// 右上角是**红色圆形 X 关闭**（不是宽金条）；控件是**绿 / 红 / 蓝 / 灰**四种圆角按钮，
    /// 标签压在控件上方、字是**深墨蓝**（不是黄字）；**原版设置界面上没有任何滑条 / 任何三角箭头按钮**
    /// （Music / SFx 是绿 ON 按钮，Language / Name 是蓝 / 灰按钮）。
    /// ⇒ 音量两项按任务契约保留（⛔ 不许改消息号与字段），控件外观按同一套原版视觉语言表达；
    /// 原版没有滑条 / 三角箭头这两类件，本面板的对应件是**本项目等价件**，⛔ 不声称是原版复刻。
    /// </para>
    /// </summary>
    public sealed class SettingsPanel : UIPanel
    {
        private const string Tag = "SettingsPanel";

        // ═══════════ 竖版排版常量（全部是基线图 `24_设置` 的实测值） ═══════════

        private const float Pad = CrUiStyle.PopupPad;                 // 30（基线：面板左 33、按钮左 47 ⇒ 14px@499）
        private const float RowPitch = CrUiStyle.PopupRowPitch;       // 136（基线：行节距 63px@499）
        private const float CtrlH = CrUiStyle.PopupControlH;          // 65（基线：按钮高 30px@499）
        private const float LabelH = CrUiStyle.PopupLabelH;           // 40（标签压在控件上方）
        private const float LabelGap = CrUiStyle.PopupLabelGap;       // 6
        private const float CtrlTopInRow = LabelH + LabelGap;         // 46：控件在行内的纵向偏移

        private const float ValueW = 120f;                            // 百分比列宽（"100%" 放得下；原版无此列，见允许差异 D3）
        private const float ValueGap = 16f;
        private const float SliderW = CrUiStyle.PopupW - 2f * Pad - ValueW - ValueGap; // 715
        private const float SliderH = 48f;                            // 滑块高（控件的下缘以内）

        /// <summary>
        /// 行数 = **2**：背景音乐 / 音效。
        /// <para>⚠️ 原来的「人声」行（CR-F2）与「画质」行已删，本次再删「全屏」行。</para>
        /// <para><b>「画质」为什么删</b>：`SettingsManager.ApplyQuality` 先 `Game.Quality.SetLevel(tier)`
        /// 写 `targetFrameRate / vSyncCount`，**紧接着**又调 `FramePacingPolicy.Pin(...)` 把它们改回去
        /// ⇒ 对帧率没有任何净效果；同一次 `SetLevel` 里写的其余三项（`shadows / shadowCascades /
        /// maximumLODLevel / ScalableBufferManager`）在本工程没有作用对象 —— 2D 精灵竖版，
        /// 全工程 0 个 `LODGroup`、0 个 2D 阴影投射体 ⇒ 点 ◀▶ 时画面**逐像素不变**。</para>
        /// <para><b>「全屏」为什么删</b>：判据 = 点一次读 `Screen.fullScreen` 的 before/after
        /// （实测 `.ai-tmp/test/fixui2-room.txt`：`before=False` → 点击后 `after=False`，面板文案却乐观地
        /// 翻成"开"）；再绕过本工程的链**直接写引擎原生开关**，读回仍是 `False`
        /// （`DIRECT-WRITE … nativeSetterWorks=False`）⇒ 编辑器里这条链**无法验证、玩家也看不出任何变化**
        /// ⇒ 属"看起来有用其实不动"的假控件。**打包后 `Screen.fullScreen` 本身是有效的**，
        /// 若日后要恢复：把本节删掉的 UI 行 + `Events.Settings.FullscreenRequest/FullscreenChanged` 的
        /// 那一段订阅加回即可（`SettingsManager` 侧全链路**未动**，仍在）。</para>
        /// </summary>
        private const int Rows = 2;

        /// <summary>弹窗高 = 标题带 + 上下留白 + 2 行（基线**高度不一致**，原因见 AM2-允许差异 D1）。</summary>
        private const float BoxH = CrUiStyle.PopupTitleH + 2f * Pad + Rows * RowPitch;   // 58+60+272 = 390

        /// <summary>设置面板是 Popup 层（架构契约 §4）。</summary>
        public override UILayer Layer => UILayer.Popup;

        private bool _built;
        private Slider _bgmSlider;
        private Slider _sfxSlider;
        private Text _bgmValue;
        private Text _sfxValue;
        // ── 权威值变更的订阅（CR-F2）：面板是"活视图"，不是"打开那一刻的快照" ──
        //    处理器必须是**实例字段**（理由与 `SettingsManager.Subscribe` 同：事件总线按委托相等性
        //    去重 / 注销，走局部 lambda 会在重开面板时叠加且 Off 不掉，`Event.cs:310-328`）。
        private Action<float> _onBgmChanged;
        private Action<float> _onSfxChanged;

        public override void OnOpen(object param)
        {
            var snapshot = param as SettingsSnapshot;
            if (snapshot == null)
            {
                // 非预期分支：说明有人绕过 AppFlow 直接 Open 了本面板。留痕并用默认值起（不崩）。
                Game.Logger?.Warn(Tag,
                    "打开时未收到 SettingsSnapshot（应由 AppFlow 传入），界面按默认值显示（实际值不为此面板所改）");
                snapshot = new SettingsSnapshot();
            }

            if (!_built)
            {
                Build();
                _built = true;
            }

            // ⛔ 顺序：**先同步显示值再挂/改回调**。`UIFactory.CreateSlider` 内部已经保证
            //    "先赋 value 再 AddListener"，但面板重开（OnOpen 再来一次）时改 value 会触发已有监听
            //    ⇒ 用 `Slider.SetValueWithoutNotify`（**只改显示、不发通知**）同时同步**滑块的填充比例**与文本。
            //    ⚠️ 只改文本、不改 slider.value 会让填充条恒满格（Sfx 42% 也画成 100%）、与文本自相矛盾
            //    ⇒ 必须同时同步**滑块的填充比例**与文本（见 `SetSlider`）。
            SetSlider(_bgmSlider, _bgmValue, snapshot.BgmVolume);
            SetSlider(_sfxSlider, _sfxValue, snapshot.SfxVolume);

            // ⛔ 顺序：显示值已同步**之后**才挂订阅（反过来会让打开瞬间的陈旧快照覆盖掉刚到的权威值）。
            Subscribe();
        }

        /// <summary>
        /// 关闭时：① 摘掉订阅（`UIManager.Close` 在销毁根节点**之前**调 `OnClose`，引擎 `UI.cs:203-215`，
        /// `CloseAll` 也走同一条路 ⇒ 不会漏摘）；
        /// ② **从暂停菜单进来的这次设置，关掉后必须把暂停菜单放回来**。
        ///
        /// <para>
        /// <b>为什么必须做</b>：`SettingsPanel` 与 `PausePanel` 同为 Popup 层，`UIManager.Open` 对 Popup 会
        /// **互斥关闭同层面板**（`UI.cs:155-159`）⇒ 打开设置的那一刻 `PausePanel` 就被收掉了，而**没有任何代码重开它**。
        /// 于是关掉设置后出现中间态：`station` 仍是 `Pause`、屏幕上却**没有任何菜单**，HUD 仍开着且可交互
        /// ⇒ 后果是"这种画面上按下手牌，一次完整的出牌请求真的发到了服务端"。
        /// </para>
        /// <para>
        /// <b>为什么选"回 Pause 菜单"而不是"回 Battle 站点"</b>：原版从暂停菜单进设置、按 X 是**回到暂停菜单**，
        /// 不是直接回到战场（1:1 复刻口径）。回 Battle 还会让玩家莫名其妙地"被踢回对局"。
        /// </para>
        /// <para>
        /// <b>为什么发 `StationEnterRequest` 就够</b>：站点此刻**已经是** `Pause` ⇒ `AppFlow.GoTo` 会早退
        /// （`AppFlow.cs:406-409`，也就不会有 `StationChanged`）；而 `CR.UI.BattleUiHost` 监听的是**请求事件**
        /// （`BattleUiHost.cs:167-170`），它收到 `Pause` 请求就会 `EnsurePausePanel` 把菜单重新打开
        /// （面板开关仍然只在那一处，⛔ 本面板不自己 `Open<PausePanel>`）。
        /// </para>
        /// <para>
        /// <b>重入安全性</b>：`UIManager.Close` **先摘表、再回调 `OnClose`**（`UI.cs:203-209` 的注释就是为了这个），
        /// 所以这里再 `Open` 一个 Popup 时，`CloseMutexPanels` 已经看不到本面板 —— 不会二次 `OnClose`、
        /// 也不会把刚开的暂停菜单又关掉；随后的 `HideMask` 会因"还有 Popup 面板"而保留遮罩（`UI.cs:463-470`）。
        /// </para>
        /// </summary>
        public override void OnClose()
        {
            // ① 先摘订阅（原有职责，⛔ 不许因为加了 ② 而不做）。
            Unsubscribe();

            // ② 把被 Popup 互斥关掉的暂停菜单放回来（详见上面的方法注释段落）。
            var fsm = Game.Fsm;
            var station = fsm != null ? fsm.Current : null;
            if (station != Stations.Pause) return; // 从主菜单等别的站点进来的设置：关掉就是关掉，⛔ 不切站点

            Game.Logger?.Info(Tag, "从暂停菜单进来的设置已关闭 ⇒ 请求重开暂停菜单（避免「站点 Pause 但无菜单」的中间态）");
            Game.Event?.Emit(Events.Flow.StationEnterRequest, Stations.Pause);
        }

        // ───────────────────────── 视觉树 ─────────────────────────

        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);

            // 弹窗自己不铺全屏背景（Popup 层的模态遮罩由 UIManager 负责），只画内容框。
            var box = CrUiStyle.SettingsPopup("SettingsBox", root, BoxH);
            var c = box.rectTransform;

            // 标题：白字压在板岩灰蓝标题带上（基线 y=163..192 处 "Settings" 画的是**白字 + 黑描边**）。
            UIFactory.CreateLabel("Title", c, "设 置", CrUiStyle.FontPopupTitle,
                new Vector2(0f, 0f), new Vector2(CrUiStyle.PopupW, CrUiStyle.PopupTitleH),
                TextAnchor.MiddleCenter, Color.white);

            // 关闭 = 原版**右上角红色圆形 X**（基线：红块 x=431..460 / y=157..182 @499，压在弹窗右上角上）。
            // ⛔ 不用「金色宽条 + 关 闭」：原版没有那颗按钮。
            var close = CrUiStyle.Skin("CloseButton", c, CrUiStyle.ButtonRed, 0,
                new Vector4(14f, 14f, 14f, 14f),
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(861f, -25f), new Vector2(65f, 65f), CrUiStyle.ButtonPressed, true);
            var closeBtn = close.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = close;
            var closeColors = closeBtn.colors;
            closeColors.normalColor = Color.white;
            closeColors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            closeColors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            closeColors.selectedColor = Color.white;
            closeColors.colorMultiplier = 1f;
            closeColors.fadeDuration = CrUiStyle.ButtonFade;
            closeBtn.colors = closeColors;
            closeBtn.onClick.AddListener(() => { Game.UI.Close<SettingsPanel>(); });

            // 关闭按钮上的白色 X = 原版图元（`ui_out` 164，`ResPaths.IconQuitCross`，已落地）。
            CrUiStyle.Icon("CloseIcon", close.rectTransform, ResPaths.IconQuitCross,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));

            // ── 音量两档（每档两行：标签一行，滑块 + 数值一行） ──
            // ⚠️ CR-F2：原来的第 3 行「人声」**已删**，理由是两条硬依据、一条都不能打折：
            //    ① **原版没有这一项**：基线图 `策划/参考图/24_设置_499x1080.jpg`（原版设置界面整屏）
            //       上只有 Music(ON) / SFx(ON) / Your Offers / Filter Clan Chat / Language / Name /
            //       Facebook / 四个链接按钮 / API Token / Credits —— **没有任何人声（Voice）项、也没有任何音量滑条**；
            //       按"原版有才做、原版没有就不加"（`SKILL.md` §0 铁律 1）⇒ 不加。
            //    ② **工程里没有它的生效对象**：全工程 **0 处** `PlayVoice` 调用（唯一命中是
            //       `Core/AudioPaths.cs` 的注释）、`AudioPaths` 无 Voice 键、`Resources` 下无 `Sound/Voice`，
            //       且没有任何表情/语音包系统 ⇒ 那颗滑块拖到底也**听不出任何差别**，是标准的假控件
            //       （⛔ 不许留一个不生效的滑块）。
            //    ⚠️ 注意：`SettingsManager` 侧的 `audio.voice` 持久化与 `SoundGroup.Voice` 应用**保留**
            //       （它仍是 `策划/实体清单.tsv` S3 VoiceVolume 登记的实体，删了会让那份台账悬空）——
            //       本次只删"面板上那颗没有听感的滑块"，`Events.Settings.VoiceVolumeChanged` 的
            //       "工程内无人订阅"也一并登记在该常量注释里。
            _bgmSlider = AddVolumeRow(c, "Bgm", "背景音乐", RowY(0),
                v => Game.Event?.Emit(Events.Settings.BgmVolumeRequest, v), out _bgmValue);

            _sfxSlider = AddVolumeRow(c, "Sfx", "音效", RowY(1),
                v => Game.Event?.Emit(Events.Settings.SfxVolumeRequest, v), out _sfxValue);

        }

        /// <summary>第 <paramref name="i"/> 行的标签行顶 y（负值向下；0 行从标题带下 + 留白开始）。</summary>
        private static float RowY(int i)
        {
            return -(CrUiStyle.PopupTitleH + Pad + i * RowPitch);
        }

        /// <summary>
        /// 建一档音量：**两行** —— ① 标签（居中压在控件上方，与原版 Music/SFx 的标签同位）
        /// ② 滑块 + 百分比（同行）。返回百分比文本（供刷新用）。
        /// </summary>
        private static Slider AddVolumeRow(Transform parent, string name, string label, float y,
            Action<float> onChanged, out Text valueText)
        {
            UIFactory.CreateLabel(name + "Label", parent, label, CrUiStyle.FontBody,
                new Vector2(Pad, y), new Vector2(CrUiStyle.PopupControlW, LabelH), TextAnchor.MiddleCenter,
                CrUiStyle.TextOnLight);

            var yCtrl = y - CtrlTopInRow;

            var style = CrUiStyle.SliderStyle();
            var slider = UIFactory.CreateSlider(name + "Slider", parent,
                new Vector2(Pad, yCtrl + (CtrlH - SliderH) * 0.5f),
                new Vector2(SliderW, SliderH), 0f, 1f, 1f, onChanged, style);
            DressSlider(slider);

            valueText = UIFactory.CreateLabel(name + "Value", parent, "100%", CrUiStyle.FontBody,
                new Vector2(Pad + SliderW + ValueGap, yCtrl), new Vector2(ValueW, CtrlH),
                TextAnchor.MiddleRight, CrUiStyle.TextOnLight);
            return slider;
        }

        // ───────────────────────── 权威值变更 → 就地刷新（CR-F2 活视图） ─────────────────────────

        /// <summary>
        /// 订阅"权威值已变更"的四条事件。**为什么面板要订**：这四条事件的设计用途就是给
        /// **显示方**用的（发布方 `SettingsManager` 自己不需要听，它改完就地生效）；
        /// 面板不订就会退化成"打开那一刻的快照"，这正是自动降档显示的缺陷（见类注释）。
        /// <para>幂等：`UIManager` 对已打开的面板会再次调 `OnOpen`（引擎 `UI.cs:119-128`）
        /// ⇒ 用"先 Off 再 On"保证重开时只挂一份（与 `DeckEditPanel.Subscribe` 同一口径）。</para>
        /// </summary>
        private void Subscribe()
        {
            if (_onBgmChanged == null)
            {
                _onBgmChanged = OnBgmVolumeChanged;
                _onSfxChanged = OnSfxVolumeChanged;
            }

            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：Game.Event 是引擎核心总线，Launch 之后必然非空。留痕（面板会退化成静态快照）。
                Game.Logger?.Warn(Tag, "Game.Event 为空，设置面板不会跟随权威值刷新（只显示打开时的快照）");
                return;
            }

            bus.Off(Events.Settings.BgmVolumeChanged, _onBgmChanged);
            bus.Off(Events.Settings.SfxVolumeChanged, _onSfxChanged);

            bus.On(Events.Settings.BgmVolumeChanged, _onBgmChanged);
            bus.On(Events.Settings.SfxVolumeChanged, _onSfxChanged);

            Game.Logger?.Info(Tag,
                "已订阅权威值变更（BGM/SFX）⇒ 面板改为活视图；" +
                "画质 / 全屏不再订阅（两项控件已删，见 Rows 的说明）；" +
                "人声无订阅（本工程无该显示项，见 Events.Settings.VoiceVolumeChanged 注释）");
        }

        private void Unsubscribe()
        {
            if (_onBgmChanged == null) return;
            var bus = Game.Event;
            bus?.Off(Events.Settings.BgmVolumeChanged, _onBgmChanged);
            bus?.Off(Events.Settings.SfxVolumeChanged, _onSfxChanged);
        }

        /// <summary>BGM 权威音量变了 ⇒ 同步填充比例 + 百分比文本（⛔ 不发通知，刷新界面 ≠ 用户改动）。</summary>
        private void OnBgmVolumeChanged(float volume)
        {
            // 日志标记用 ASCII（[Live] BGM）⇒ 运行时可按"过程"计数（数值类判据，不看截图）。
            Game.Logger?.Info(Tag, $"[Live] BGM follow-authority → {Mathf.Clamp01(volume) * 100f:0}%");
            SetSlider(_bgmSlider, _bgmValue, volume);
        }

        /// <summary>音效权威音量变了 ⇒ 同步显示。</summary>
        private void OnSfxVolumeChanged(float volume)
        {
            Game.Logger?.Info(Tag, $"[Live] SFX follow-authority → {Mathf.Clamp01(volume) * 100f:0}%");
            SetSlider(_sfxSlider, _sfxValue, volume);
        }

        // ───────────────────────── 显示刷新 ─────────────────────────

        /// <summary>同步「滑块填充比例 + 百分比文本」，**不发 onValueChanged**（刷新界面 ≠ 用户改动）。</summary>
        private static void SetSlider(Slider target, Text label, float value)
        {
            var v = Mathf.Clamp01(value);
            if (target != null) target.SetValueWithoutNotify(v);
            if (label != null) label.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        // ───────────────────────── 原版素材：把图元贴回引擎控件 ─────────────────────────

        /// <summary>
        /// 给引擎滑块换上**原版素材**（全部走 <see cref="CrUiStyle.Dress"/>）：
        /// 轨道 = 原版板岩灰蓝圆角框体（`ui_out` 014 左上圆角件拼九宫格）、
        /// 填充 = 原版**绿色按钮底**（`ui_out` 610，与原版 ON 按钮同一个绿）、
        /// 手柄 = 原版**亮面圆角块**（`ui_out` 019）。
        /// <para>⚠️ **原版设置界面上没有滑条**（Music / SFx 是绿 ON 按钮）⇒ 这三件是"原版无此部件"下
        /// 按同一套原版视觉语言表达的**等价件**。
        /// ⛔ 不用 `loading_out` 015（**绿色加载读条**）当滑条填充 —— 那是加载条。</para>
        /// </summary>
        private static void DressSlider(Slider slider)
        {
            if (slider == null) return;

            CrUiStyle.Dress(slider.GetComponent<Image>(), CrUiStyle.PopupFrameSlate, 10, Vector4.zero);

            var fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            CrUiStyle.Dress(fill, CrUiStyle.ButtonGreen, 0, new Vector4(14f, 14f, 14f, 14f));

            var handle = slider.handleRect != null ? slider.handleRect.GetComponent<Image>() : null;
            CrUiStyle.Dress(handle, CrUiStyle.PopupSkinLight, 10, Vector4.zero);
        }

        // ⚠️ 面板侧没有素材加载 / 切片代码：轨道 / 填充 / 手柄的贴图全走
        //    `CrUiStyle.Dress` / `CrUiStyle.Skin` / `CrUiStyle.Icon`（唯一实现在 `CrUiStyle.cs`）。
    }
}
