using System;

namespace CR.Def
{
    // ============================================================================
    // 协议结构体 —— **客户端唯一定义处**，与服务端 server/game/def/{msg,push,types}.go
    // 逐字段对齐（字段名 = 服务端 json tag，**必须逐字一致**，否则反序列化静默拿到默认值）。
    //
    // ★★ `[Serializable]` 是**硬要求，一个都不能少** ★★
    //   客户端的 `Serializer.Deserialize<T>` 走的是 `JsonUtility.FromJson<T>`
    //   （`clover-client-unity-engine/Runtime/Core/Contracts.cs:145-148`），而 Unity 的 `JsonUtility`：
    //     · 扁平类（字段全是 int/string/bool）**不标也能解** —— 实测 `SetNicknameReply` 正常；
    //     · 但**元素类型是普通类的数组字段会被静默丢成 null** —— 实测 `GetCardPoolReply.cards` 恒为 null。
    //   ⇒ 现象极具误导性：登录/创角（扁平回包）全过，卡池/房间/对局快照（含对象数组）全空，
    //     且**一条报错都没有**，服务端日志还显示"下发卡池 60 张"。
    //   实测判据：`typeof(T).IsSerializable` 必须为 true；`JsonUtility.FromJson<T>("{\"cards\":[{\"id\":1}]}")`
    //   解出来的 `cards` 不能是 null。⛔ 新增任何 DTO 都必须带 `[Serializable]`。
    //
    //   约定：
    //   · 对局坐标字段一律带单位后缀：x_milli / y_milli（1/1000 格）
    //   · 服务端 `omitempty` 的字段在客户端就是普通字段（缺省即默认值）
    //   · Unity 的 JsonUtility **不支持** Dictionary / 多态 / 顶层数组，故推送体一律包一层对象
    // ============================================================================

    // ---------------------------------------------------------------- 玩家
    [Serializable] public class SetNicknameReq { public string nickname; }
    [Serializable] public class SetNicknameReply { public bool ok; public string nickname; public string err; }

    [Serializable] public class GetProfileReq { }
    [Serializable] public class GetProfileReply { public string nickname; public int wins; public int losses; public int[] deck; }

    // ------------------------------------------------------------ 卡池与卡组
    [Serializable] public class GetCardPoolReq { }
    [Serializable] public class GetCardPoolReply { public CardInfo[] cards; }

    [Serializable] public class GetDeckReq { }
    [Serializable] public class GetDeckReply { public int[] card_ids; }

    [Serializable] public class SaveDeckReq { public int[] card_ids; }
    [Serializable] public class SaveDeckReply { public bool ok; public string err; }

    // ---------------------------------------------------------------- 房间
    [Serializable] public class RoomCreateReq { public string name; }
    [Serializable] public class RoomCreateReply { public bool ok; public string room_id; public string err; }

    [Serializable] public class RoomListReq { }
    [Serializable] public class RoomListReply { public RoomInfo[] rooms; }

    [Serializable] public class RoomJoinReq { public string room_id; }
    [Serializable] public class RoomJoinReply { public bool ok; public string room_id; public string err; public bool ai_fill; }

    [Serializable] public class RoomLeaveReq { public string room_id; }
    [Serializable] public class RoomLeaveReply { public bool ok; }

    [Serializable] public class RoomReadyReq { public string room_id; public bool ready; }
    [Serializable] public class RoomReadyReply { public bool ok; }

    [Serializable] public class RoomStartReq { public string room_id; }
    [Serializable] public class RoomStartReply { public bool ok; public string err; }

    [Serializable] public class RoomSetAiReq { public string room_id; public bool ai_fill; }
    [Serializable] public class RoomSetAiReply { public bool ok; }

    // ---------------------------------------------------------------- 对局
    [Serializable] public class BattlePlayCardReq { public string room_id; public int card_id; public int x_milli; public int y_milli; }
    [Serializable] public class BattlePlayCardReply { public bool ok; public string err; }

    [Serializable] public class BattleSurrenderReq { public string room_id; }
    [Serializable] public class BattleSurrenderReply { public bool ok; }

    [Serializable] public class BattleSyncReq { public string room_id; }
    [Serializable] public class BattleSyncReply { public BattleSnapshot snapshot; }

    // ---------------------------------------------------------------- 人机
    [Serializable] public class AiBattleStartReq { public int[] deck; }
    [Serializable] public class AiBattleStartReply { public bool ok; public string room_id; public string err; }

    // ============================================================ 推送体

    [Serializable] public class RoomListNotify { public RoomInfo[] rooms; }

    [Serializable]
    public class RoomStateNotify
    {
        public string room_id;
        public string name;
        public string host;
        public bool ai_fill;
        public bool started;
        public RoomMember[] members;
        /// <summary>
        /// 本机自己的角色 ID（**逐接收者**：服务端按推送目标逐个填，同一份房间态推给不同玩家时值不同）。
        /// 用来判定「我是不是房主」「成员列表里哪个是我」——⛔ 不要再用账号去反推角色 ID。
        /// </summary>
        public string self_player_id;
    }

    [Serializable]
    public class BattleStartNotify
    {
        public string room_id;
        public long seed;
        public int server_ms;
        public int my_team;              // 0=BLUE 1=RED
        public BattleTimeline timeline;
        public int[] deck_a;
        public int[] deck_b;
        public int[] hand_a;
        public int next_a;
        public int[] hand_b;
        public int next_b;
    }

    [Serializable] public class BattleEventNotify { public BattleEvent[] events; }

    /// <summary>结算。reason 取值：king_destroyed / time_up_crowns / time_up_hp / surrender / draw。</summary>
    [Serializable]
    public class BattleEndNotify
    {
        public bool win;
        public bool draw;
        public int crowns_a;
        public int crowns_b;
        public string reason;
        public int hp_rate_a;            // 剩余塔血占总上限的万分比
        public int hp_rate_b;
    }

    // ============================================================ 共享类型

    [Serializable]
    public class TowerState
    {
        public int id;
        public int kind;                 // 0=公主塔 1=国王塔
        public int hp;
        public int max_hp;
        public bool alive;
    }

    [Serializable]
    public class EntitySnapshot
    {
        public int id;
        public int kind;                 // 0=部队 1=建筑 2=塔
        public int card_id;
        public int team;                 // 0=BLUE 1=RED
        public int x_milli;              // 1/1000 格
        public int y_milli;              // 1/1000 格
        public int hp;
        public int max_hp;
        public int anim;                 // 0=idle 1=walk 2=attack 3=die
        public int facing;               // -1 / 1
        public int deploy_ms;            // 剩余部署时间
    }

    /// <summary>
    /// 周期全量快照（10 Hz，BestEffort）。塔**不在** entities 里 —— 6 座塔由 towers_a / towers_b 报告，
    /// 塔位是固定几何（客户端从 Core/GameConst.cs 的常量取），不要在这里期待坐标。
    /// </summary>
    [Serializable]
    public class BattleSnapshot
    {
        // 本帧属于哪个房间（服务端 `def.BattleSnapshot.RoomID`，同名同值）。
        // 客户端按它丢弃"不是本局房间"的帧 —— 否则旧房间若还在推快照，它更大的 seq
        // 会让 `BattleManager.ApplySnapshot` 把新房的帧全判"倒退"丢掉。
        // ⚠️ 本字段缺省（服务端未下发）时 ⇒ 反序列化出来是 null/空串，客户端按"未知房间"放行（见 ApplySnapshot）。
        public string room_id;
        public int seq;
        public int server_ms;
        public int phase;                // 0=normal 1=overtime 2=ended
        public int elixir_a;             // 1/1000，0..10000
        public int elixir_b;
        public int crowns_a;
        public int crowns_b;
        public TowerState[] towers_a;
        public TowerState[] towers_b;
        public EntitySnapshot[] entities;
        public int[] hand_a;
        public int next_a;
        public int[] hand_b;
        public int next_b;
    }

    [Serializable]
    public class BattleTimeline
    {
        public int regulation_ms;        // 180000
        public int overtime_ms;          // 120000
        public int starting_elixir;      // 6
        public int max_elixir;           // 10
        public int[] elixir_ms_per_unit; // [2800,1400,930]
        public int[] elixir_phase_ms;    // [120000,120000,60000]
    }

    [Serializable]
    public class BattleEvent
    {
        public int kind;                 // 0=出牌 1=生成 2=死亡 3=塔毁 4=圣水满 5=塔激活 6=塔开火
        public int card_id;
        public int x_milli;
        public int y_milli;
        public int entity_id;
        public int team;
        public string text;
        // 只在 kind=6（塔开火）时有意义：投射物速度，单位 = 格/分钟。
        // 服务端对"该塔没有投射物 / 投射物表里没有速度"的行写 0 ⇒ 0 表示
        // "无飞行段"，客户端只播枪口闪光、不播飞行轨迹。
        public int proj_speed;
    }

    [Serializable]
    public class RoomInfo
    {
        public string room_id;
        public string name;
        public string host;
        public int cur;
        public int max;                  // 固定 2
        public bool ai_fill;
        public bool started;
    }

    [Serializable]
    public class RoomMember
    {
        public string player_id;
        public string nickname;
        public bool ready;
        public bool is_host;
        public bool is_ai;
    }

    /// <summary>卡池里的一张卡（GetCardPoolReply.cards 的元素）。字段取自服务端配表 card_cs。</summary>
    [Serializable]
    public class CardInfo
    {
        public int id;
        public string key;
        public string name_cn;
        public string name_en;
        public int type;                 // 0=部队 1=法术 2=建筑
        public int rarity;               // 0=普通 1=稀有 2=史诗 3=传说
        public int elixir;
        public int arena;
        public string icon;

        /// <summary>
        /// 这张卡战斗本体的**攻击投射物 key**（服务端 `CardInfo.ProjectileKey`，json tag
        /// **`projectile_key`**，逐字对齐 `server/game/def/types.go`）。
        /// **空串 = 非远程**（近战部队 / 法术）—— 客户端据此判「这张卡是不是远程」。
        /// </summary>
        public string projectile_key;

        /// <summary>
        /// 弹道速度，单位 **格/分钟**（服务端 `CardInfo.ProjSpeed`，json tag **`proj_speed`**），
        /// 与官方 `cards_stats_projectile.json` 的 `speed` 同口径
        /// （出处 `策划/策划案/皇室战争参考规格.md` §4：`anchors.json` → `tiles_per_minute_per_speed_unit: 1`）。
        /// 用法：飞行时长(秒) = 距离(格) ÷ (proj_speed ÷ 60) = 距离 × 60 ÷ proj_speed。
        /// `projectile_key` 为空时为 0。
        /// </summary>
        public int proj_speed;

        /// <summary>
        /// **法术卡**的作用半径，单位 = milli-tile（1 格 = 1000）；非法术卡为 0
        /// （服务端 `CardInfo.AoeRadiusMilli`，json tag **`aoe_radius_milli`**，
        /// 数值出处 = `spell.tsv` 的 `radius_mt` 列）。
        /// <para>
        /// 用法：拖出法术时落点半径圈的半格数 = <c>aoe_radius_milli / 1000f</c>。
        /// ⛔ 不许在客户端写死一个"法术半径"常量 —— 10 张法术各不相同
        /// （万箭齐发 1.4 格 … 雷电/毒药 3.5 格），一刀切会让玩家照着圈放却打空。
        /// </para>
        /// </summary>
        public int aoe_radius_milli;
    }
}
