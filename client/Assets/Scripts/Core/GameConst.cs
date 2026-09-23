using UnityEngine;

namespace CR
{
    /// <summary>
    /// 竞技场几何与单位换算的**唯一实现处**。
    ///
    /// <para>
    /// <b>为什么全部是常量而不是散在 View 里的字面量</b>：这些值是服务端与客户端的**共同前提**
    /// （服务端用它算寻路 / 部署合法性，客户端用它摆塔与判定"看起来能不能放"），
    /// 两边一旦漂移，现象是「客户端觉得能放、服务端拒绝」这类只在联调时才暴露的问题。
    /// 因此本文件的每一个常量都能在 `策划/策划案/皇室战争参考规格.md` §1/§2 找到出处，
    /// 并与服务端 `server/game/core/{units,arena}.go` **同名同值**（改一处必须同时改两处）。
    /// </para>
    /// <para>
    /// <b>坐标约定</b>（与 `server/game/core/arena.go` 一致）：y 小 = BLUE 后方、y 大 = RED 后方；
    /// 世界空间以「格」为单位、竞技场中心在原点 ⇒ `worldY = tileY - ArenaTilesH / 2`
    /// （**不做 Y 翻转**：服务端 y 就是朝 RED 增大的）。
    /// </para>
    /// </summary>
    public static class GameConst
    {
        // ───────────────────────── 单位系统（参考规格 §1） ─────────────────────────

        /// <summary>位置的定点单位 = 1/1000 格（与服务端 `core.MilliTilePerTile` 同值）。</summary>
        public const int MilliTilePerTile = 1000;

        /// <summary>服务端 tick 率 = 20 TPS（50 ms/帧，参考规格 §1；本片只做读条与站点编排，用不到，登记以便 View 复用）。</summary>
        public const int TicksPerSecond = 20;

        /// <summary>一个 tick 的毫秒数（20 TPS ⇒ 50 ms）。</summary>
        public const int MsecPerTick = 50;

        /// <summary>服务端快照周期 = 100 ms（10 Hz，契约 §4.1 的 `PushBattleSnapshot`）。</summary>
        public const int SnapshotIntervalMs = 100;

        // ───────────────────────── 竞技场几何（参考规格 §2） ─────────────────────────

        /// <summary>场地宽 = 18 格（`anchors.json` → `arena_tiles_wide`）。</summary>
        public const float ArenaTilesW = 18f;

        /// <summary>场地高 = 32 格（`anchors.json` → `arena_tiles_tall`）。</summary>
        public const float ArenaTilesH = 32f;

        /// <summary>河流上沿 y = 15.0（`anchors.json` → `river_top_tile`）。</summary>
        public const float RiverTopTile = 15f;

        /// <summary>河流下沿 y = 17.0（`anchors.json` → `river_bottom_tile`）；河宽 = 2 格，中心 y = 16.0。</summary>
        public const float RiverBottomTile = 17f;

        /// <summary>左桥中心 x = 3.5 格（`anchors.json` → `bridge_centre_tiles[0]`）。</summary>
        public const float BridgeCxATile = 3.5f;

        /// <summary>右桥中心 x = 14.5 格（`anchors.json` → `bridge_centre_tiles[1]`）。</summary>
        public const float BridgeCxBTile = 14.5f;

        /// <summary>桥半宽 = 1 格（`bridge_width_tiles: 2.0` ⇒ 总宽 2 格）。</summary>
        public const float BridgeHalfTile = 1f;

        /// <summary>国王塔 x = 9.0 格（BLUE 与 RED 同 x，只有 y 镜像）。</summary>
        public const float KingTowerTileX = 9f;

        /// <summary>BLUE 国王塔 y = 3.0 格（RED = 32 - 3 = 29.0）。</summary>
        public const float KingTowerTileY = 3f;

        /// <summary>BLUE 公主塔 y = 6.5 格（RED = 32 - 6.5 = 25.5）；两座公主塔 x 就是两个桥中心 x。</summary>
        public const float PrincessTowerTileY = 6.5f;

        // ───────────────────────── 圣水（参考规格 §3） ─────────────────────────

        /// <summary>圣水上限 = 10 格（10000 milli，与服务端 `core.MaxElixirMilli` 同值）。</summary>
        public const int MaxElixirMilli = 10000;

        /// <summary>起始圣水 = 6 格（6000 milli，服务端 `core.StartingElixirMilli` 同值）。</summary>
        public const int StartingElixirMilli = 6000;

        // ───────────────────────── 表现层本项目自定（参考物未规定） ─────────────────────────

        /// <summary>
        /// 启动画面停留时长（秒）。
        /// 出处：**本项目自定** —— 原版启动画面没有可测的固定时长，参考规格也未规定；
        /// 取 2 s 只为「让玩家看清 Logo 与 `by clover-engine` 署名」这一目的，不参与任何玩法逻辑。
        /// </summary>
        public const float BootSplashSeconds = 2f;

        // ───────────────────────── 换算函数 ─────────────────────────

        /// <summary>
        /// milli-tile → 世界坐标（格）。竞技场中心落在世界原点，y **不翻转**。
        /// </summary>
        public static Vector2 MilliToWorld(int xMilli, int yMilli)
        {
            return TileToWorld(xMilli / (float)MilliTilePerTile, yMilli / (float)MilliTilePerTile);
        }

        /// <summary>格 → 世界坐标（格）。竞技场中心落在世界原点。</summary>
        public static Vector2 TileToWorld(float xTile, float yTile)
        {
            return new Vector2(xTile - ArenaTilesW * 0.5f, yTile - ArenaTilesH * 0.5f);
        }

        /// <summary>milli-tile → 格（不转世界坐标；给需要原始格坐标的逻辑用）。</summary>
        public static Vector2 MilliToTile(int xMilli, int yMilli)
        {
            return new Vector2(xMilli / (float)MilliTilePerTile, yMilli / (float)MilliTilePerTile);
        }

        /// <summary>格 → milli-tile（四舍五入，与服务端 `core.TileToMilli` 同口径）。</summary>
        public static int TileToMilli(float tile)
        {
            return Mathf.RoundToInt(tile * MilliTilePerTile);
        }

        /// <summary>
        /// 该队伍是否按「上下镜像」处理（RED 侧 = BLUE 侧 `y → 32 - y`）。
        /// 出处：参考规格 §2「RED 侧 = BLUE 侧 y → 32 - y 镜像」。
        /// </summary>
        public static bool IsMirroredForTeam(int team)
        {
            return team == 1; // 0=BLUE 1=RED（契约 ProtoDef.BattleStartNotify.my_team / EntitySnapshot.team）
        }

        /// <summary>把 BLUE 侧的格 y 换算成指定队伍的格 y（RED 镜像；BLUE 原样）。</summary>
        public static float MirrorTileYForTeam(float yTile, int team)
        {
            return IsMirroredForTeam(team) ? ArenaTilesH - yTile : yTile;
        }
    }
}
