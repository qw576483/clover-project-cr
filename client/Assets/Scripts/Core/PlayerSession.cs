using CloverEngine;

namespace CR
{
    /// <summary>
    /// 会话态的**唯一**权威存放处：玩家昵称（= 服务端档案里的 `nickname`）。
    ///
    /// <para>
    /// <b>为什么需要它</b>：`MainMenuPanel` 的打开 param 是**一次性快照**
    /// （`AppFlow._nickname` 只在登录拉档案 / 创角回包时写一次，且只有 `EnterMainMenu`
    /// 这一条路径会把它传下去）。任何"不经 `EnterMainMenu`"的打开路径
    ///（`UIManager.Open&lt;T&gt;` 对已存在面板会重调 `OnOpen(param)`，见
    /// `Runtime/Presentation/UI.cs:127`；`AppFlow.GoTo` 对"已经是当前站点"的请求早退，
    /// `AppFlow.cs:365`）都会让面板拿到 `param == null` ⇒ 界面显示**硬编码**的
    /// `"玩家"`（`MainMenuPanel.cs:105`），而服务端档案里可能是别的名字，且**一句日志都没有**。
    /// </para>
    /// <para>
    /// <b>职责边界（契约 §1）</b>：面板⛔不许 `using CR.Module`，所以它没法去问 `AppFlow`。
    /// 本类放在 `CR` 命名空间（与 `Events` / `Cfg` 同层），**写方只有 `AppFlow`**（服务端回包），
    /// **读方是面板**；面板因此能在**任何**打开路径下拿到服务端权威昵称，不依赖调用者记性。
    /// </para>
    /// </summary>
    public static class PlayerSession
    {
        private const string Tag = "PlayerSession";

        private static string _nickname = string.Empty;

        /// <summary>服务端档案里的胜场（`GetProfileReply.wins`）。<see cref="HasStats"/> 为 false 时无意义。</summary>
        private static int _wins;

        /// <summary>服务端档案里的负场（`GetProfileReply.losses`）。</summary>
        private static int _losses;

        /// <summary>参赛场次（`GetProfileReply.matches`，含平局）。</summary>
        private static int _matches;

        /// <summary>三冠胜场（`GetProfileReply.three_crown_wins`）。</summary>
        private static int _threeCrownWins;

        /// <summary>已收集卡牌数（`GetProfileReply.cards_found`）。</summary>
        private static int _cardsFound;

        /// <summary>常用卡牌 id（`GetProfileReply.favourite_card`，0 = 一张都没出过）。</summary>
        private static int _favouriteCard;

        /// <summary>常用卡牌的显示名（`GetProfileReply.favourite_card_name`，空 = 一张都没出过）。</summary>
        private static string _favouriteCardName = string.Empty;

        /// <summary>最高奖杯（`GetProfileReply.highest_trophies`；本工程无奖杯机制 ⇒ 服务端恒 0）。</summary>
        private static int _highestTrophies;

        /// <summary>累计捐赠（`GetProfileReply.cards_donated`；本工程无捐赠机制 ⇒ 服务端恒 0）。</summary>
        private static int _cardsDonated;

        /// <summary>赢得卡牌（`GetProfileReply.cards_won`；本工程无卡牌奖励机制 ⇒ 服务端恒 0）。</summary>
        private static int _cardsWon;

        /// <summary>是否已经拿到过服务端档案里的战绩（先前 Launch 残留的不算，理由同 <see cref="_bus"/>）。</summary>
        private static bool _hasStats;

        /// <summary>
        /// 记下这个昵称时那条引擎总线（= "**当前会话**"的标识）。
        /// <para>
        /// <b>为什么需要它</b>：`_nickname` 是**裸静态**，而本工程**关闭了域重载**
        /// ⇒ 静态残留会把**上一次 Launch** 的昵称带进下一次：面板会显示上一个账号的名字，
        /// 与服务端档案不一致，且一句日志都没有（同类静态残留见 `App/Bootstrap.cs` 的 `_bus` 注释）。
        /// 判据口径与工程其余 5 处逐字一致（`RoomManager` / `DeckManager` / `BattleManager` /
        /// `BattleUiHost` / `BgmView`）：**按当前总线对象标识当前会话** —— `Game.Launch` 每次新建
        /// `EventBus`（引擎 `Game.cs:381`），总线一换，先前的昵称就不再算数。
        /// </para>
        /// </summary>
        private static IEventBus _bus;

        /// <summary>
        /// 记下的昵称是否仍属**当前**这次 Launch（总线未变 ⇒ 数据还算数）。
        /// 启动早期 `Game.Event` 为 null 且 `_bus` 也为 null 时判为"当前"（此时昵称本就是空串）。
        /// </summary>
        private static bool IsCurrentRound
        {
            get { return ReferenceEquals(_bus, Game.Event); }
        }

        /// <summary>
        /// 服务端权威昵称；未知时为空串（⛔ 这里不塞本地默认值，默认值属兜底策略、由界面决定）。
        /// 先前 Launch 残留的昵称**不算数**（见 <see cref="_bus"/>）。
        /// </summary>
        public static string Nickname
        {
            get { return IsCurrentRound ? (_nickname ?? string.Empty) : string.Empty; }
        }

        /// <summary>是否已经从服务端拿到过非空昵称（先前残留的不算）。</summary>
        public static bool HasNickname
        {
            get { return !string.IsNullOrEmpty(Nickname); }
        }

        /// <summary>
        /// 服务端档案里的胜场；<see cref="HasStats"/> 为 false 时返回 0。
        /// <para>⚠️ 读方必须**先问 <see cref="HasStats"/>**：0 是合法战绩（一场没赢），
        /// 「没有数据」与「一场没赢」在界面上必须能分开（否则统计行会把"缺数据"画成 0）。</para>
        /// </summary>
        public static int Wins
        {
            get { return IsCurrentRound ? _wins : 0; }
        }

        /// <inheritdoc cref="Wins"/>
        public static int Losses
        {
            get { return IsCurrentRound ? _losses : 0; }
        }

        /// <summary>是否已经从服务端档案拿到过战绩（先前 Launch 残留的不算）。</summary>
        public static bool HasStats
        {
            get { return IsCurrentRound && _hasStats; }
        }

        /// <inheritdoc cref="Matches"/>
        public static int Matches
        {
            get { return IsCurrentRound ? _matches : 0; }
        }

        /// <inheritdoc cref="Matches"/>
        public static int ThreeCrownWins
        {
            get { return IsCurrentRound ? _threeCrownWins : 0; }
        }

        /// <inheritdoc cref="Matches"/>
        public static int CardsFound
        {
            get { return IsCurrentRound ? _cardsFound : 0; }
        }

        /// <inheritdoc cref="Matches"/>
        public static int HighestTrophies
        {
            get { return IsCurrentRound ? _highestTrophies : 0; }
        }

        /// <inheritdoc cref="Matches"/>
        public static int CardsDonated
        {
            get { return IsCurrentRound ? _cardsDonated : 0; }
        }

        /// <inheritdoc cref="Matches"/>
        public static int CardsWon
        {
            get { return IsCurrentRound ? _cardsWon : 0; }
        }

        /// <summary>常用卡牌的显示名（未知 / 一张都没出过时为空串）。</summary>
        public static string FavouriteCardName
        {
            get { return IsCurrentRound ? (_favouriteCardName ?? string.Empty) : string.Empty; }
        }

        /// <summary>常用卡牌 id（0 = 一张都没出过）。</summary>
        public static int FavouriteCard
        {
            get { return IsCurrentRound ? _favouriteCard : 0; }
        }

        /// <summary>
        /// 是否已经有「常用卡牌」可显示。<b>必须先问它</b>：本工程确实存在"一张牌都没出过"的
        /// 场合（新号第一局之前）⇒ 那种情况界面显示占位符，而不是空格子。
        /// </summary>
        public static bool HasFavouriteCard
        {
            get { return IsCurrentRound && !string.IsNullOrEmpty(_favouriteCardName); }
        }

        /// <summary>
        /// 记下服务端档案里的 8 项统计。数据出处 = 服务端 `GetProfileReply`
        /// （服务端 `server/game/datadef/player.go` 的 `PlayerData` + 卡牌表）。
        /// <para>
        /// `highestTrophies` / `cardsDonated` / `cardsWon` 三项本工程没有对应机制，
        /// 服务端恒下发 0 —— 这里照收，不做任何"补一个好看的数"的加工。
        /// </para>
        /// <paramref name="source"/> = 哪条回包写的（`GetProfile`），便于在日志里区分来源。
        /// </summary>
        public static void SetStats(int wins, int losses, int matches, int threeCrownWins,
            int cardsFound, int favouriteCard, string favouriteCardName,
            int highestTrophies, int cardsDonated, int cardsWon, string source)
        {
            _wins = wins < 0 ? 0 : wins;
            _losses = losses < 0 ? 0 : losses;
            _matches = matches < 0 ? 0 : matches;
            _threeCrownWins = threeCrownWins < 0 ? 0 : threeCrownWins;
            _cardsFound = cardsFound < 0 ? 0 : cardsFound;
            _favouriteCard = favouriteCard < 0 ? 0 : favouriteCard;
            _favouriteCardName = favouriteCardName ?? string.Empty;
            _highestTrophies = highestTrophies < 0 ? 0 : highestTrophies;
            _cardsDonated = cardsDonated < 0 ? 0 : cardsDonated;
            _cardsWon = cardsWon < 0 ? 0 : cardsWon;
            _hasStats = true;
            _bus = Game.Event;   // 标记"这份战绩属于当前引擎"（见 _bus 字段）
            Game.Logger?.Info(Tag,
                $"服务端战绩已记下 wins={_wins} losses={_losses} matches={_matches}" +
                $" threeCrownWins={_threeCrownWins} cardsFound={_cardsFound}" +
                $" favouriteCard={_favouriteCard}('{_favouriteCardName}')" +
                $" highestTrophies={_highestTrophies} cardsDonated={_cardsDonated}" +
                $" cardsWon={_cardsWon}（来自 {source}）");
        }

        /// <summary>
        /// 记下服务端下发的昵称。`source` = 哪条回包写的（`GetProfile` / `SetNickname`），
        /// 便于在日志里区分"登录时拿到"和"创角后拿到"。
        /// 传 null / 空串表示"服务端该玩家还没创角"——那是**正常态**（随后进创角站点），
        /// 不是错误，因此只 Info 不 Warn。
        /// </summary>
        public static void SetNickname(string nickname, string source)
        {
            _nickname = nickname ?? string.Empty;
            _bus = Game.Event;   // 标记"这个昵称属于当前引擎"（见 _bus 字段）
            Game.Logger?.Info(Tag, HasNickname
                ? $"服务端昵称已记下 nickname='{_nickname}'（来自 {source}）"
                : $"服务端昵称为空（来自 {source}）—— 该玩家尚未创角，属正常态");
        }

        /// <summary>清空（登出 / 换账号时用，避免把上一个会话的名字带进新会话）。</summary>
        public static void Clear()
        {
            _nickname = string.Empty;
            _wins = 0;
            _losses = 0;
            _matches = 0;
            _threeCrownWins = 0;
            _cardsFound = 0;
            _favouriteCard = 0;
            _favouriteCardName = string.Empty;
            _highestTrophies = 0;
            _cardsDonated = 0;
            _cardsWon = 0;
            _hasStats = false;
            _bus = null;         // 会话已结束：这些值不再属于任何一次 Launch（见 _bus 字段）
            Game.Logger?.Info(Tag, "会话昵称与战绩已清空（登出）");
        }
    }
}
