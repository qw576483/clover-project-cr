using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloverEngine;
using CR.Def;
using CR.UI.Panels;

namespace CR.Module.Deck
{
    /// <summary>
    /// 卡组的 C2S 门面 + 卡池缓存（卡池 / 卡组的**唯一**读写处）。
    ///
    /// <para>
    /// <b>数据从哪来</b>：60 张卡池走 <c>MsgDef.GetCardPool</c>，我的卡组走 <c>MsgDef.GetDeck</c>，
    /// 保存走 <c>MsgDef.SaveDeck</c>。⛔ 客户端**不落地 tsv**（架构契约 D8 + `Bootstrap` 第 ⑤ 步的
    /// 显式声明）：卡牌的名称 / 费用 / 稀有度随回包下发，权威在服务端 `game/table`，
    /// 客户端再存一份必然漂移。
    /// </para>
    /// <para>
    /// <b>与面板的关系（架构契约 §1 依赖方向）</b>：`DeckEditPanel` ⛔不许 `using CR.Module`，
    /// 所以它只能 `Emit(Events.Deck.*)`；本类订阅这些事件、执行动作，再把结果
    /// `Emit` 回面板。面板因此完全不认识本类，换皮肤不动链路。
    /// </para>
    /// <para>
    /// <b>谁创建本类</b>：`DeckModuleHost`（`[RuntimeInitializeOnLoadMethod]` +
    /// `Game.RegisterLaunchHook`）。引擎的启动钩子就是「上层模块把自己挂接到门面」的正规入口
    /// （`Game.cs:661-671`，注释里写明"可安全配合 RuntimeInitializeOnLoadMethod 使用"）；
    /// ⚠️ 项目级 skill 的 `registry.md` 记的是「Flow 持有」，与本文件的挂载点不一致。
    /// </para>
    /// <para>
    /// <b>`Events.Deck.Changed` 是双通道（必须知道，否则会自激）</b>：`Events.cs` 里 Deck 只有
    /// 4 条事件，**没有**独立的「保存请求」事件，所以本类按方向区分同一条事件：
    /// 面板 → 管理器的 `Changed(ids)` = 「请把这份卡组保存为我的卡组」；
    /// 管理器 → 面板的 `Changed(ids)` = 「服务端确认的当前卡组就是这样」。
    /// 本类发了通知后会被**自己的**订阅立刻收到（`Event.Emit` 是同步分发）——因此下面用
    /// <c>_notifyingDeck</c> 做重入保护；`AppFlow` 的对应做法是"订阅处理器绝不重发同一个事件"——
    /// 本类因 `Events.cs` 的 Deck 段没有独立的「保存请求」事件，只能取重入保护这条。
    /// </para>
    /// </summary>
    public sealed class DeckManager
    {
        private const string Tag = "Deck";

        /// <summary>
        /// 一副卡组的张数 = 8。
        /// 出处：服务端 `server/game/logic/deck.go` 的 `deckSize = 8`（校验口径与
        /// `core.NewBattle` 的 8 张断言同源）；客户端**不**再发明第二个值 ——
        /// 面板需要这个数时由本类经打开参数递进去（见 <see cref="DeckEditPanel.PanelArgs"/>）。
        /// </summary>
        public const int DeckSize = 8;

        /// <summary>当前实例（纯 C# 单例，跨场景存活；见类注释「谁创建本类」）。</summary>
        public static DeckManager Instance { get; private set; }

        private CardInfo[] _pool;                    // 60 张卡池（拉一次并缓存）
        private int[] _deck = Array.Empty<int>();    // 我的卡组（服务端确认过的）
        private bool _deckFetched;                   // 「已拉过卡组」——空卡组也是合法状态，不能用长度判
        private bool _saving;                        // 保存在途（防连点）
        private bool _notifyingDeck;                 // 重入保护：见类注释「双通道」
        private IEventBus _bus;                      // 已经订阅过的那条事件总线（判据：同一条只装一次）

        private Action _onOpenRequest;
        private Action<int[]> _onChangedFromPanel;
        private Action<string> _onStationChanged;

        /// <summary>已缓存的卡池（未拉到时为 null）。</summary>
        public CardInfo[] CardPool => _pool;

        /// <summary>服务端确认过的卡组 id（未拉到 / 未设置时为空数组）。</summary>
        public int[] CurrentDeck => _deck;

        /// <summary>
        /// 取（或首次创建）卡组模块并装上订阅。由启动钩子调用，幂等；
        /// 每次 `Game.Launch` 后都要再调一次 —— `Game.Event` 是 Launch 时新建的对象，
        /// 先前的订阅随它一起消失。
        /// </summary>
        public static DeckManager EnsureCreated()
        {
            Instance ??= new DeckManager();
            Instance.Install();
            return Instance;
        }

        /// <summary>
        /// 装上事件订阅（幂等：同一条事件总线只装一次）。
        /// 「换总线才重装」的判据是 <c>Game.Event</c> 的**对象标识** —— 每次 <c>Game.Launch</c> 都会新建
        /// `EventBus`（引擎 `Runtime/Core/Game.cs:381`），先前的订阅随它一起消失，所以每次 Launch 都必须重装。
        /// ⛔ 这里**不能用裸 bool** 当"已装载"：本工程关闭了域重载（`client/ProjectSettings/EditorSettings.asset`
        /// 的 `m_EnterPlayModeOptionsEnabled`），静态量跨 Play 存活 ⇒ 裸 bool 会让编辑器里 **第 2 次及以后**
        /// 每次 Play 在 `Install()` 开头直接 return（不订阅、不写日志、不报错），
        /// 主菜单点「卡组编辑」时 `Deck.OpenRequest` 的监听者为 0 ⇒ 点击全静默。
        /// 同口径见 `Module/Room/RoomManager.cs:146`。
        /// </summary>
        public void Install()
        {
            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：启动钩子早于 Launch 核心初始化（不该发生）。留痕，别静默。
                Game.Logger?.Error(Tag, "Game.Event 为空（启动钩子时机异常），卡组模块未订阅任何事件");
                return;
            }

            if (ReferenceEquals(bus, _bus)) return;   // 同一条总线：已装过

            if (_bus != null)
            {
                // 换总线 = 引擎重新 Launch（编辑器里每次 Play 都会发生）。只在这一刻报一次，不刷屏。
                Game.Logger?.Info(Tag, "检测到新的事件总线（引擎重新 Launch），卡组模块重新装载订阅");
            }

            _bus = bus;
            _onOpenRequest = OnOpenRequest;
            _onChangedFromPanel = OnChangedFromPanel;
            _onStationChanged = OnStationChanged;

            bus.On(Events.Deck.OpenRequest, _onOpenRequest);
            bus.On<int[]>(Events.Deck.Changed, _onChangedFromPanel);

            // 「首次进主菜单时拉一次并缓存」：主菜单是卡组数据的第一个可能入口，
            // 在这里预取 ⇒ 玩家点「卡组编辑」时面板能立刻出内容，不必先看一次转圈。
            bus.On<string>(Events.Flow.StationChanged, _onStationChanged);

            Game.Logger?.Info(Tag,
                $"卡组模块已装载（订阅 Deck.OpenRequest / Deck.Changed / Flow.StationChanged，卡组张数 {DeckSize}）");
        }

        // ═════════════════════════ 面板请求的处理 ═════════════════════════

        /// <summary>面板（或 `AppFlow.RequestOpenDeckEdit`）请求打开卡组编辑。</summary>
        private void OnOpenRequest()
        {
            if (Game.UI == null)
            {
                Game.Logger?.Error(Tag, "Game.UI 为空（表现域未挂载），无法打开卡组编辑面板");
                return;
            }

            // 先把已有缓存喂给面板（服务端往返可能在几十~几百毫秒后才有回包），
            // 再触发一次真拉取：拉到后本类会再发一次同样的通知，面板据此重画（幂等）。
            Game.UI.Open<DeckEditPanel>(new DeckEditPanel.PanelArgs { MaxSelected = DeckSize });
            NotifyPool();
            NotifyDeck(_deck);

            LoadAsync();
        }

        private void OnStationChanged(string station)
        {
            if (station != Stations.MainMenu) return;
            // 进主菜单就预取（幂等：已缓存时 EnsureXxx 不会重复发 C2S）。
            LoadAsync();
        }

        /// <summary>面板请求保存（见类注释「双通道」）。</summary>
        private void OnChangedFromPanel(int[] ids)
        {
            if (_notifyingDeck)
            {
                // 这是本类自己刚发出去的通知（Emit 同步分发 ⇒ 立刻回到这里），不是面板的请求。
                return;
            }
            SaveDeckAsync(ids);
        }

        // ═════════════════════════ 拉取 ═════════════════════════

        /// <summary>拉卡池 + 卡组（各自幂等：已缓存 / 已拉过就不重复发）。</summary>
        private async void LoadAsync()
        {
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return;
                }

                if (_pool == null)
                {
                    var poolReply = await Game.Net.Call<GetCardPoolReply>(MsgDef.GetCardPool, new GetCardPoolReq());
                    if (poolReply == null || poolReply.cards == null || poolReply.cards.Length == 0)
                    {
                        Fail("卡池为空（服务端配表未就绪？）");
                        return;
                    }
                    _pool = poolReply.cards;
                    Game.Logger?.Info(Tag, $"卡池已缓存 {_pool.Length} 张");
                    NotifyPool();
                }

                if (!_deckFetched)
                {
                    var deckReply = await Game.Net.Call<GetDeckReply>(MsgDef.GetDeck, new GetDeckReq());
                    if (deckReply == null)
                    {
                        Fail("拉取卡组失败（空回包）");
                        return;
                    }
                    _deckFetched = true;
                    _deck = deckReply.card_ids ?? Array.Empty<int>();
                    Game.Logger?.Info(Tag, $"当前卡组已就绪 {_deck.Length} 张");
                    NotifyDeck(_deck);
                }
            }
            catch (Exception e)
            {
                // Call 超时 / 断线 / 服务端拒绝都以异常结束（引擎契约）。
                Fail("拉取卡池/卡组失败：" + e.Message);
            }
        }

        // ═════════════════════════ 保存 ═════════════════════════

        /// <summary>
        /// 保存卡组。**客户端先校验**（8 张 / 不重复 / 都在卡池里），
        /// 校验不过**一个字节都不发**，并把原因 `Emit(SaveFailed)` 交给面板显示
        /// （⛔ 不许只打日志）。
        /// </summary>
        private async void SaveDeckAsync(int[] ids)
        {
            try
            {
                var reason = ValidateDeck(ids);
                if (reason != null)
                {
                    Fail(reason);
                    return;
                }
                if (_saving)
                {
                    Game.Logger?.Warn(Tag, "已有一次保存在途，忽略重复的保存请求（按钮连点？）");
                    return;
                }
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return;
                }

                _saving = true;
                var reply = await Game.Net.Call<SaveDeckReply>(MsgDef.SaveDeck, new SaveDeckReq { card_ids = ids });
                if (reply == null || !reply.ok)
                {
                    Fail("服务端拒绝保存：" + (reply != null && !string.IsNullOrEmpty(reply.err) ? reply.err : "空回包"));
                    return;
                }

                _deck = (int[])ids.Clone();   // 以服务端确认的结果为准（而不是面板的引用）
                _deckFetched = true;
                Game.Logger?.Info(Tag, $"卡组已保存 {_deck.Length} 张");
                NotifyDeck(_deck);
            }
            catch (Exception e)
            {
                Fail("保存卡组失败：" + e.Message);
            }
            finally
            {
                _saving = false;
            }
        }

        /// <summary>
        /// 卡组的客户端校验；返回 null = 合法，否则返回**给玩家看的原因**。
        /// 口径与服务端 `logic/deck.go` 的 `checkDeck` **逐条对齐**（8 张 / 不重复 / 都在卡池里）：
        /// 客户端先拦是为了不让明知非法的请求白跑一趟（服务端仍会再校验一次，它是权威）。
        /// </summary>
        public string ValidateDeck(int[] ids)
        {
            if (ids == null) return "卡组不能为空";
            if (ids.Length != DeckSize) return $"卡组必须正好 {DeckSize} 张（当前 {ids.Length} 张）";
            if (_pool == null) return "卡池还没加载好，请稍后重试";

            var inPool = new HashSet<int>();
            for (var i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] != null) inPool.Add(_pool[i].id);
            }

            var seen = new HashSet<int>();
            for (var i = 0; i < ids.Length; i++)
            {
                if (!seen.Add(ids[i])) return $"卡组里有重复的卡（id={ids[i]}，同一张卡只能带一张）";
                if (!inPool.Contains(ids[i])) return $"卡 id={ids[i]} 不在卡池里（数据异常，请重新打开卡组编辑）";
            }
            return null;
        }

        // ═════════════════════════ 通知面板 ═════════════════════════

        /// <summary>卡池就绪（没有面板在听时是空操作，由 `OnOpenRequest` 补发缓存）。</summary>
        private void NotifyPool()
        {
            if (_pool == null) return;
            Game.Event?.Emit(Events.Deck.PoolLoaded, _pool);
        }

        /// <summary>当前卡组通知（见类注释「双通道」：发出后会被自己的订阅立刻收到，故置重入位）。</summary>
        private void NotifyDeck(int[] ids)
        {
            if (ids == null) return;
            _notifyingDeck = true;
            try
            {
                Game.Event?.Emit(Events.Deck.Changed, ids);
            }
            finally
            {
                _notifyingDeck = false;
            }
        }

        private void Fail(string reason)
        {
            Game.Logger?.Warn(Tag, "卡组操作失败：" + reason);
            Game.Event?.Emit(Events.Deck.SaveFailed, reason);
        }
    }
}
