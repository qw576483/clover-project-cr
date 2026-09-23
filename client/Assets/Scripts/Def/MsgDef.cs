namespace CR.Def
{
    /// <summary>
    /// 业务消息号 —— **客户端唯一定义处**，与 <c>server/game/def/{msg,push}.go</c> 一一对应，
    /// 改动必须两端同一批进行。
    ///
    /// 引擎消息号见 <c>CloverEngine.EMsg</c>（占 [1,10000]）；业务必须 &gt; 10000。
    /// ⛔ 回包**不占消息号**（回包帧 msgID 恒为 0，用 <c>Call&lt;XxxReply&gt;</c> 按 requestID 配对），
    /// 所以这里**没有** Reply 常量 —— 这是引擎契约，不是遗漏。
    /// </summary>
    public static class MsgDef
    {
        // ---- 玩家 1000101+ ----
        public const uint SetNickname = 1000101;   // C2S 设置昵称
        public const uint GetProfile  = 1000102;   // C2S 拉个人档案

        // ---- 卡池与卡组 1000201+ ----
        public const uint GetCardPool = 1000201;   // C2S 拉 60 张卡池
        public const uint GetDeck     = 1000202;   // C2S 拉我的卡组
        public const uint SaveDeck    = 1000203;   // C2S 保存卡组（8 张）

        // ---- 房间 1000301+ ----
        public const uint RoomCreate = 1000301;    // C2S 创建房间
        public const uint RoomList   = 1000302;    // C2S 拉房间列表
        public const uint RoomJoin   = 1000303;    // C2S 加入房间
        public const uint RoomLeave  = 1000304;    // C2S 离开房间
        public const uint RoomReady  = 1000305;    // C2S 准备 / 取消准备
        public const uint RoomStart  = 1000306;    // C2S 房主开打
        public const uint RoomSetAi  = 1000307;    // C2S 房主设 AI 补位

        // ---- 对局 1000401+ ----
        public const uint BattlePlayCard = 1000401; // C2S 出牌（落点单位 = 1/1000 格）
        public const uint BattleSurrender = 1000402; // C2S 投降
        public const uint BattleSync      = 1000403; // C2S 主动拉一次全量状态（进场 / 重连）

        // ---- 人机 1000501+ ----
        public const uint AiBattleStart = 1000501;  // C2S 主菜单「人机对战」开一局

        // ---- 推送 3002001+（推送**必须**定义常量，与回包不同）----
        public const uint PushRoomList       = 3002001; // Reliable    大厅房间列表变化
        public const uint PushRoomState      = 3002002; // Reliable    房间成员 / 准备 / AI 状态
        public const uint PushBattleStart    = 3002003; // Reliable    开打：对局配置
        public const uint PushBattleSnapshot = 3002004; // BestEffort  周期快照（10 Hz）
        public const uint PushBattleEvent    = 3002005; // Reliable    对局离散事件
        public const uint PushBattleEnd      = 3002006; // Reliable    结算
    }
}
