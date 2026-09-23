using System;
using CloverEngine;
using CR.Def;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 房间列表（`MainMenu` 站点的 Popup 子面板，架构契约 §4）：浏览 / 创建 / 刷新 / 加入。
    ///
    /// <para>
    /// <b>依赖方向（契约 §1 硬线）</b>：本面板⛔不许 `using CR.Module` —— 它只
    /// `Emit(Events.Room.*Request)` 发请求，数据由 `Module/Room/RoomManager` 收推送后再 `Emit` 回来
    /// （<c>Room.ListChanged</c> / <c>Room.Failed</c> / <c>Room.Joined</c>）。
    /// </para>
    /// <para>
    /// <b>为什么不用 ScrollView</b>：引擎的 `UIFactory`（`Runtime/Presentation/UIWidgets.cs` +
    /// `UIWidgetControls.cs`）里**没有**滚动列表工厂（全引擎无 `ScrollRect` 构建代码），
    /// 而任务书允许"可滚动或分页"。这里取**分页**：每页 5 行 + 上一页/下一页，
    /// 页面切换只切 5 个固定行节点的 <c>SetActive</c>（不反复销毁重建节点）。
    /// </para>
    /// <para>
    /// <b>竖版重排（G2）+ AP1 换帧</b>：面板底 = <see cref="CrUiStyle.SettingsPopup"/>（居中弹窗：
    /// `ui_out` 014 板岩外框 + 019 亮面体，与主菜单 / 设置**同一套**），标题 = <see cref="CrUiStyle.BandTitle"/>
    /// （板岩带上的白字黑描边），行底 / 字段盘 = `ui_out` 014（<see cref="CrUiStyle.Skin"/> 四角镜像九宫格），
    /// 按钮 = <see cref="CrUiStyle.BlueButton"/>（`ui_out` 165 蓝底白字），输入框 = <see cref="CrUiStyle.Field"/>。
    /// ⛔ 建房三件套（"房名 + 输入框 + 创建按钮"）已**拆成三行**；⛔ 没有任何一行并排三件套。
    /// </para>
    /// <para>
    /// ⚠️ <b>A 本体没有「房间列表」界面</b>（`策划/参考图/清单.md` §2 明确登记：原版 CR 无「房间」概念，
    /// 本项目为用户新增）⇒ 本面板的视觉语言**逐项对齐 A 的同类部件**，⛔ **不假装是原版界面**：
    /// 面板底 / 标题 / 行底 / 按钮 四类都是 A 的原版图元（见表与本文件常量注释），逐行依据见
    /// `.ai-tmp/test/AP1-量取.md` A 段 + `策划/自审对比/AP1-自审.md`。
    /// </para>
    /// </summary>
    public sealed class RoomListPanel : UIPanel
    {
        private const string Tag = "RoomListPanel";

        // ───── 竖版排版常量（AP1 换帧后 = 居中弹窗；出处见类注释与 .ai-tmp/test/AP1-量取.md A 段） ─────

        /// <summary>面板亮面体宽 = 931（<see cref="CrUiStyle.PopupW"/> 935 − 2×描边 2）。</summary>
        private const float BodyW = CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder;

        /// <summary>亮面体内缩留白后的可用宽 = 871（= BodyW − 2×<see cref="CrUiStyle.PopupPad"/>）。</summary>
        private const float InnerW = BodyW - 2f * CrUiStyle.PopupPad;

        /// <summary>元素左边界 = 32（= 描边 2 + 留白 30）。基线内留白量取见 AM2 的 `24_设置`（14px@499 ⇒ 30@1080）。</summary>
        private const float InsetX = CrUiStyle.PopupBorder + CrUiStyle.PopupPad;

        /// <summary>标题行高 = 92（与主菜单同口径：标题压弹窗顶部板岩带，其下留 92 再放首个元素）。</summary>
        private const float TitleRowH = CrUiStyle.TitleBarH;

        private const float BtnW = 824f;                                    // A "Battle Deck" 572@750(76.3%W) ×1.44 = 823.7（≤ 871）
        private const float BtnH = 66f;                                     // A 同一条 y=1145..1190 h=46@750 ×1.44 = 66.2
        private const float RowH = CrUiStyle.FieldH;                        // 95 = A 字段带 66@750 ×1.44
        private const float RowGap = 24f;                                   // 本项目自定行距（登记）
        private const float RowStep = RowH + RowGap;                        // 119
        private const float JoinW = 180f;                                   // 行内「加入」按钮宽（项目自定）
        private const float JoinH = 48f;                                    // A "Clan" 按钮 h=33@750 ×1.44 = 47.5 ⇒ 48
        private const float PageBtnH = JoinH;                               // 上一页/下一页同档
        private const float HintH = 28f;
        private const float HeaderH = 36f;
        private const float StatusH = 40f;
        private const float GapS = 12f;
        private const float GapM = 16f;
        private const float GapL = 32f;

        /// <summary>板岩盘（行底 / 字段盘）的圆角边长 = 014 的圆角（与 <see cref="CrUiStyle.SettingsPopup"/> 同口径）。</summary>
        private const int PlateCorner = 24;

        /// <summary>每页行数（固定 5 个行节点，翻页只切 active；行数由内容框高度倒推，见 G2-量取.md）。</summary>
        private const int PageSize = 5;

        /// <summary>房名输入上限。本项目自定：服务端对房名没有长度限制（`room.go:895` 只做 TrimSpace），限长只为防误粘贴超长串。</summary>
        private const int MaxNameChars = 32;

        // ───────────────────────── 运行时状态 ─────────────────────────

        private bool _built;
        private int _page;

        private RoomInfo[] _rooms = Array.Empty<RoomInfo>();
        private readonly string[] _pageRoomIds = new string[PageSize];
        private readonly Image[] _rowBoxes = new Image[PageSize];
        private readonly Text[] _rowLabels = new Text[PageSize];
        private readonly Image[] _rowJoinButtons = new Image[PageSize];

        private RectTransform _content;   // 内容框（所有元素的父节点，Build 里赋值）
        private InputField _nameInput;
        private Text _header;
        private Text _pageLabel;
        private Text _status;
        private Image _createButton;
        private Image _refreshButton;

        private bool _busy;   // 建房 / 加入在途（响应要么是 ListChanged+站点切换，要么是 Failed）

        private Action<RoomInfo[]> _onListChanged;
        private Action<string> _onFailed;
        private Action<string> _onJoined;

        /// <summary>房间列表是 `MainMenu` 的子面板（架构契约 §4 ⇒ Popup 层）。</summary>
        public override UILayer Layer => UILayer.Popup;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }

            Subscribe();
            _busy = false;
            _page = 0;
            RefreshRows();
            SetStatus("点「加入」进房；没有房间就自己开一间（创建后你是房主，可开 AI 补位）。", CrUiStyle.TextDim);
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

            // Popup 层不铺全屏背景（模态遮罩由 UIManager 负责，UI.cs:143-147），只画居中弹窗。
            // 坐标原点 = **亮面体左上角**（`CrUiStyle.SettingsPopup` 的 body 锚点在左上），y 为负 = 向下。
            float rowsH = PageSize * RowH + (PageSize - 1) * RowGap;      // 5×95 + 4×24 = 571
            float yLabel = -(TitleRowH + GapM);
            float yInput = yLabel - CrUiStyle.LabelH - GapS;
            float yCreate = yInput - CrUiStyle.FieldH - GapM;
            float yHint = yCreate - BtnH - GapS;
            float yHeader = yHint - HintH - GapM;
            float yRefresh = yHeader - HeaderH - GapS;
            float yRows = yRefresh - BtnH - GapM;
            float yStatus = yRows - rowsH - GapM;
            // 状态行**垫一条板岩盘**再放字（与主菜单 StatusBar 同构）：
            // 状态色（TextDim 浅灰 / Accent 金 / ErrorText 浅红）全是**为暗底设计的**，
            // 直接压在亮面体 (229,236,242) 上对比度极低（AP1 首版实机截图实测：状态字发白看不清）。
            // ⚠️ 坐标是"左上角锚点 + anchoredPosition"，**y 越接近 0 越靠上** ⇒ 盘的顶边要写在
            //    字的顶边**之上** = `yStatus + GapS`（写成 `− GapS` 会把盘压到字下面，实测首版就是这样）。
            float statusPlateY = yStatus + GapS;
            float statusPlateH = StatusH + 2f * GapS;
            float yPage = statusPlateY - statusPlateH - GapS;
            float yPrev = yPage - HeaderH - GapS;
            float yNext = yPrev - PageBtnH - GapS;
            float yBack = yNext - PageBtnH - GapS;
            float bodyH = -yBack + BtnH + GapL;

            var box = CrUiStyle.SettingsPopup("RoomListBox", root, bodyH + CrUiStyle.PopupTitleH);
            var c = box.rectTransform.Find("RoomListBoxBody") as RectTransform;
            if (c == null)
            {
                // 非预期分支（必须留痕）：亮面体没建出来 ⇒ 后面的元素全落空。
                Game.Logger?.Error(Tag, "弹窗亮面体 RoomListBoxBody 没建出来，房间列表内容无法摆放");
                return;
            }
            _content = c;

            // 标题：板岩带上的白字黑描边（A 原版 UI 的标题语言；基线上「Player Profile」就是这个写法）。
            CrUiStyle.BandTitle("Title", box, "房 间 列 表", BodyW);

            // ── 建房三件套 = 三行（⛔ 不并排）──
            UIFactory.CreateLabel("NameLabel", c, "房名（留空 = 用「<你的昵称>的房间」）", CrUiStyle.FontSmall,
                new Vector2(InsetX, yLabel), new Vector2(InnerW, CrUiStyle.LabelH), TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);
            _nameInput = CrUiStyle.Field("RoomNameInput", c, new Vector2(InsetX, yInput),
                new Vector2(InnerW, CrUiStyle.FieldH), "房名（可留空）", MaxNameChars);
            _createButton = CrUiStyle.BlueButton("CreateButton", c, "创建房间",
                new Vector2((BodyW - BtnW) * 0.5f, yCreate), new Vector2(BtnW, BtnH), OnCreateClicked);

            UIFactory.CreateLabel("CreateHint", c, "创建成功即成为房主，可开「AI 补位」补满座位。",
                CrUiStyle.FontSmall, new Vector2(InsetX, yHint), new Vector2(InnerW, HintH), TextAnchor.MiddleLeft,
                CrUiStyle.TextOnLightDim);

            // ── 列表头 + 刷新（两行，⛔ 不并排）──
            _header = UIFactory.CreateLabel("Header", c, "共 0 间", CrUiStyle.FontSmall,
                new Vector2(InsetX, yHeader), new Vector2(InnerW, HeaderH), TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);
            _refreshButton = CrUiStyle.BlueButton("RefreshButton", c, "刷 新 列 表",
                new Vector2((BodyW - BtnW) * 0.5f, yRefresh), new Vector2(BtnW, BtnH), OnRefreshClicked);

            BuildRows(yRows);

            CrUiStyle.Skin("StatusPlate", c, CrUiStyle.PopupFrameSlate, PlateCorner, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InsetX, statusPlateY),
                new Vector2(InnerW, statusPlateH), CrUiStyle.BandSlate, false);
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(InsetX + 16f, yStatus), new Vector2(InnerW - 32f, StatusH), TextAnchor.MiddleLeft,
                CrUiStyle.TextDim);

            // ── 分页与返回：标签独占一行 + 三颗整宽按钮（⛔ 不并排）──
            _pageLabel = UIFactory.CreateLabel("PageLabel", c, "第 1/1 页", CrUiStyle.FontSmall,
                new Vector2(InsetX, yPage), new Vector2(InnerW, HeaderH), TextAnchor.MiddleLeft, CrUiStyle.TextOnLightDim);
            CrUiStyle.BlueButton("PrevButton", c, "上一页", new Vector2(InsetX, yPrev),
                new Vector2(InnerW, PageBtnH), OnPrevPage);
            CrUiStyle.BlueButton("NextButton", c, "下一页", new Vector2(InsetX, yNext),
                new Vector2(InnerW, PageBtnH), OnNextPage);
            CrUiStyle.BlueButton("BackButton", c, "返 回", new Vector2((BodyW - BtnW) * 0.5f, yBack),
                new Vector2(BtnW, BtnH), OnBackClicked);
        }

        /// <summary>固定 5 行：整页一次建好，翻页只切 active（行数不随房间数变化）。</summary>
        private void BuildRows(float rowY0)
        {
            for (var i = 0; i < PageSize; i++)
            {
                var index = i; // 闭包捕获：每行的「加入」记自己的行下标
                var y = rowY0 - i * RowStep;

                // 行底：**板岩盘**（`ui_out` 014 的四角镜像九宫格）—— A 12_主菜单的深色圆角盘语言
                // （`ItsRafaXD` 名条 / `667` 字段块都是"深色圆角块 + 亮字"）。⚠️ 原版无「房间列表」界面
                // ⇒ 这是**对齐同类部件**（依据见 AP1-量取.md A 段），⛔ 不是"原版房间列表长这样"。
                _rowBoxes[index] = CrUiStyle.Skin($"Row{index}", _content, CrUiStyle.PopupFrameSlate, PlateCorner,
                    Vector4.zero, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(InsetX, y), new Vector2(InnerW, RowH), CrUiStyle.BandSlate, false);

                _rowLabels[index] = UIFactory.CreateLabel($"RowLabel{index}", _content, string.Empty,
                    CrUiStyle.FontSmall, new Vector2(InsetX + 16f, y), new Vector2(InnerW - 32f - JoinW - GapS, RowH),
                    TextAnchor.MiddleLeft, CrUiStyle.TextColor);

                _rowJoinButtons[index] = CrUiStyle.BlueButton($"Join{index}", _content, "加入",
                    new Vector2(InsetX + InnerW - JoinW, y - (RowH - JoinH) * 0.5f), new Vector2(JoinW, JoinH),
                    () => OnJoinClicked(index));
            }
        }

        // ───────────────────────── 交互 ─────────────────────────

        private void OnCreateClicked()
        {
            if (_busy)
            {
                SetStatus("上一个操作还在进行中，请稍候", CrUiStyle.Accent);
                return;
            }

            var name = _nameInput != null ? _nameInput.text : string.Empty;
            _busy = true;
            Game.Logger?.Info(Tag, $"请求创建房间（房名='{name}'）");
            SetStatus("正在创建房间…", CrUiStyle.Accent);
            RefreshRows();

            // 空房名是合法输入（服务端会兜底成「<昵称>的房间」）⇒ 这里不拦，原样发出去。
            Game.Event?.Emit(Events.Room.CreateRequest, name ?? string.Empty);
        }

        private void OnJoinClicked(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= PageSize) return;
            var roomId = _pageRoomIds[rowIndex];
            if (string.IsNullOrEmpty(roomId))
            {
                Game.Logger?.Warn(Tag, $"第 {rowIndex} 行没有房间号（界面与数据不同步？），忽略加入请求");
                return;
            }
            if (_busy)
            {
                SetStatus("上一个操作还在进行中，请稍候", CrUiStyle.Accent);
                return;
            }

            _busy = true;
            Game.Logger?.Info(Tag, $"请求加入房间 {roomId}");
            SetStatus($"正在加入房间 {roomId}…", CrUiStyle.Accent);
            RefreshRows();
            Game.Event?.Emit(Events.Room.JoinRequest, roomId);
        }

        private void OnRefreshClicked()
        {
            Game.Logger?.Info(Tag, "请求刷新房间列表");
            SetStatus("正在刷新…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Room.RefreshRequest);
        }

        private void OnPrevPage()
        {
            if (_page <= 0) { SetStatus("已经是第一页", CrUiStyle.TextDim); return; }
            _page--;
            RefreshRows();
        }

        private void OnNextPage()
        {
            if (_page >= PageCount - 1) { SetStatus("已经是最后一页", CrUiStyle.TextDim); return; }
            _page++;
            RefreshRows();
        }

        private void OnBackClicked()
        {
            // 关自己（Popup 层；设置面板同一做法，见 SettingsPanel.cs:116-117）。
            Game.UI.Close<RoomListPanel>();
        }

        // ───────────────────────── 显示刷新 ─────────────────────────

        private void RefreshRows()
        {
            var total = _rooms != null ? _rooms.Length : 0;
            var pages = PageCount;
            if (_page > pages - 1) _page = pages - 1;
            if (_page < 0) _page = 0;

            if (_header != null) _header.text = $"共 {total} 间房间";
            if (_pageLabel != null) _pageLabel.text = $"第 {_page + 1}/{pages} 页";

            var pageStart = _page * PageSize;
            for (var i = 0; i < PageSize; i++)
            {
                var index = pageStart + i;
                var hasRoom = index < total && _rooms[index] != null;

                if (!hasRoom)
                {
                    _pageRoomIds[i] = null;
                    // 首页且一间房都没有时给一条可读的空态提示（而不是空框）。
                    var emptyHint = total == 0 && i == 0;
                    SetRowVisible(i, emptyHint);
                    if (emptyHint && _rowLabels[i] != null)
                    {
                        _rowLabels[i].text = "（暂无房间：点「创建房间」开一间，或点「刷新列表」再看看）";
                        _rowLabels[i].color = CrUiStyle.TextDim;
                    }
                    if (_rowJoinButtons[i] != null) _rowJoinButtons[i].gameObject.SetActive(false);
                    continue;
                }

                var room = _rooms[index];
                _pageRoomIds[i] = room.room_id;
                SetRowVisible(i, true);
                if (_rowJoinButtons[i] != null) _rowJoinButtons[i].gameObject.SetActive(true);

                // 任务书要求的五项：房号 / 房名 / 人数 / 是否 AI 补位 / 是否已开打。
                var joinable = !room.started && room.cur < room.max;
                var why = room.started ? "（已开打）" : (room.cur >= room.max ? "（已满）" : string.Empty);
                if (_rowLabels[i] != null)
                {
                    // 房名是**服务端数据**（输入侧只受 `MaxNameChars` 约束，历史数据可能更长）⇒ 过 TextFit（D4 同族）。
                    _rowLabels[i].text = TextFit.Clamp(_rowLabels[i],
                        $"[{room.room_id}] {room.name}    {room.cur}/{room.max} 人 · " +
                        $"AI 补位：{(room.ai_fill ? "开" : "关")} · {(room.started ? "已开打" : "未开打")}{why}");
                    _rowLabels[i].color = joinable ? CrUiStyle.TextColor : CrUiStyle.TextDim;
                }

                // 非房主 / 满员 / 已开打的加入按钮置灰：置灰原因已经写在同一行的文字里（不做悬停提示）。
                CrUiStyle.SetButtonEnabled(_rowJoinButtons[i], joinable && !_busy);
            }

            if (_createButton != null) CrUiStyle.SetButtonEnabled(_createButton, !_busy);
            if (_refreshButton != null) CrUiStyle.SetButtonEnabled(_refreshButton, !_busy);
        }

        private void SetRowVisible(int i, bool visible)
        {
            if (_rowBoxes[i] != null) _rowBoxes[i].gameObject.SetActive(visible);
            if (_rowLabels[i] != null) _rowLabels[i].gameObject.SetActive(visible);
        }

        private int PageCount => Mathf.Max(1, Mathf.CeilToInt((_rooms != null ? _rooms.Length : 0) / (float)PageSize));

        // ───────────────────────── 订阅（OnOpen 挂 / OnClose 摘，成对） ─────────────────────────

        private void Subscribe()
        {
            if (_onListChanged == null)
            {
                _onListChanged = OnListChanged;
                _onFailed = OnFailed;
                _onJoined = OnJoined;
            }

            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger?.Error(Tag, "Game.Event 为空（引擎未 Launch？），房间列表收不到任何数据");
                return;
            }

            // 幂等：UIManager 对已打开的面板会再次调用 OnOpen（UI.cs:107-117），重复 On 会让一次失败走两遍处理。
            bus.Off(Events.Room.ListChanged, _onListChanged);
            bus.Off(Events.Room.Failed, _onFailed);
            bus.Off(Events.Room.Joined, _onJoined);
            bus.On(Events.Room.ListChanged, _onListChanged);
            bus.On(Events.Room.Failed, _onFailed);
            bus.On(Events.Room.Joined, _onJoined);
        }

        private void Unsubscribe()
        {
            if (_onListChanged == null) return;
            var bus = Game.Event;
            bus?.Off(Events.Room.ListChanged, _onListChanged);
            bus?.Off(Events.Room.Failed, _onFailed);
            bus?.Off(Events.Room.Joined, _onJoined);
        }

        private void OnListChanged(RoomInfo[] rooms)
        {
            _rooms = rooms ?? Array.Empty<RoomInfo>();
            _busy = false;   // 列表变化 = 服务端处理过我的请求（创建/加入成功都会改列表）
            RefreshRows();
        }

        private void OnFailed(string reason)
        {
            _busy = false;
            RefreshRows();
            SetStatus(string.IsNullOrEmpty(reason) ? "操作失败（服务端未给出原因）" : reason, CrUiStyle.ErrorText);
        }

        private void OnJoined(string roomId)
        {
            // 成功进房：站点会切到 Room（`RoomManager` 请求站点切换 → `AppFlow` CloseAll 收掉本面板）。
            SetStatus($"已进入房间 {roomId}，正在切换到房间界面…", CrUiStyle.Accent);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = TextFit.Clamp(_status, text);
            _status.color = color;
        }
    }
}
