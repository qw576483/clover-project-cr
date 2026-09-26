using System.Collections.Generic;

namespace CR.View
{
    /// <summary>
    /// **建筑层配方**：一个已部署建筑由哪几帧（= `.sc` 的 shapeID = `frame_NNN`）叠成，每帧的画布像素偏移与缩放。
    /// <para>
    /// <b>覆盖帧归并</b>：先收齐该队全部去重子件层，再对**这组层的像素掩膜**求并集 U；若某一层自己 == U
    /// （它单独就画出整个外形，其余层全在它内部）⇒ 只留这一层（其余是画在完整件上的过场件，会重绘内部像素）；
    /// 若没有任何层覆盖 U ⇒ 这些层是**互补子件**，全部保留（例：tesla 的 23/34 层拼出木箱本体）。
    /// </para>
    /// <para>
    /// <b>为什么需要它</b>：`building_*_out` 目录里装的是**建物的多个子件 + 两队配色 + 小道具**（不是一套动画帧），
    /// 而 <see cref="UnitView"/> 只有一个 <c>SpriteRenderer</c>，对未收录目录只能「整目录逐帧循环」⇒ 会在部件帧与
    /// 敌队配色帧之间闪。本表给出「某队伍的这个建筑 = 这几帧按此顺序叠起来」。
    /// </para>
    /// <para>
    /// <b>出处（原版数据反解，⛔ 本表是生成物、不许手改）</b>：`原版资源/sc/building_*_v215.sc` 的
    /// `0x0C` 记录 —— 它的 `cnt2` 条目是**子件的 clip id**，`cnt1` 条 `(childIndex, matrixId, ctId)` 是
    /// **逐帧放置表**（本表取**第 1 帧**的放置顺序 = 绘制顺序），子件 clip 的 `cnt2` 给出它真正画的 shapeID；
    /// shapeID 就是 `frame_NNN` 的 `NNN`。矩阵（`0x08`）= `(a,b,c,d,tx,ty)`：`sx=a/1024`、`sy=d/1024`、
    /// `dxPx=tx/20`、`dyPx=ty/20`（`65535` = 无矩阵）。队伍取导出名（含 `red`/`enemy` = 红）。
    /// </para>
    /// <para>
    /// <b>正向对照</b>：对 `building_tower_v215.sc` 的 `KingTower_red`(clip 307) / `KingTower_blue`(clip 308)
    /// 逐层复现 `ArenaView.cs` 塔层配方段记录的那 5 层（rec 值、`matrix 89/91/184/185`、`dy −25/−33/−57/−42px`）**逐条一致**。
    /// </para>
    /// <para>生成路径：`tools/probes/sc-placement.py` + `tools/probes/sc-anim-index.py` → `.ai-tmp/test/ux4-gen-table.py`。</para>
    /// </summary>
    public static class BuildingLayerTable
    {
        /// <summary>一层：帧号（`frame_NNN` 的 NNN）+ 缩放 + 相对建筑根节点的**画布像素**偏移（x 右为正、y 上为正）。</summary>
        public readonly struct Layer
        {
            /// <summary>帧号（= `.sc` shapeID = `frame_NNN` 的 NNN）。</summary>
            public readonly int Frame;
            /// <summary>该层的画布像素缩放（矩阵 `a/1024`；同层 x/y 分别给）。</summary>
            public readonly float Sx;
            /// <summary>该层的画布像素缩放（矩阵 `d/1024`）。</summary>
            public readonly float Sy;
            /// <summary>相对根节点的画布像素偏移，x 右为正（矩阵 `tx/20`）。</summary>
            public readonly float DxPx;
            /// <summary>相对根节点的画布像素偏移，**y 向下为正**（`.sc` 矩阵口径，= `ty/20`）；
            /// 写到 Unity 的 `localPosition.y` 时必须取反（Unity 的 +y 向上）。</summary>
            public readonly float DyPx;
            public Layer(int frame, float sx, float sy, float dxPx, float dyPx)
            {
                Frame = frame; Sx = sx; Sy = sy; DxPx = dxPx; DyPx = dyPx;
            }
        }

        /// <summary>队伍常量（与契约 `EntitySnapshot.team` 同值：0=BLUE 1=RED）。</summary>
        public const int TeamBlue = 0;
        /// <summary>队伍常量（与契约 `EntitySnapshot.team` 同值）。</summary>
        public const int TeamRed = 1;

        /// <summary>spriteDir → 两队各自的层序列（蓝 0 / 红 1）。⛔ 生成物，不许手改。</summary>
        private static readonly Dictionary<string, Layer[][]> Table =
            new Dictionary<string, Layer[][]>
        {
            {
                "building_barbarian_hut_out",
                new[]
                {
                    // BLUE 层：导出 building_barbarian_hut_blue
                    new[] { new Layer(1, 1f, 1f, 0f, 0f) },
                    // RED 层：导出 building_barbarian_hut_red
                    new[] { new Layer(0, 1f, 1f, 0f, 0f) },
                }
            },
            {
                "building_basic_cannon_out",
                new[]
                {
                    // BLUE 层：导出 building_cannon
                    new[] { new Layer(0, 0.5996f, 0.5996f, 0f, -10f), new Layer(20, 1f, 1f, 0f, -84.75f) },
                    // RED 层：导出 building_enemy_cannon
                    new[] { new Layer(0, 0.5996f, 0.5996f, 0f, -10f), new Layer(1, 1f, 1f, 0f, -25f) },
                }
            },
            {
                "building_bomb_tower_out",
                new[]
                {
                    // BLUE 层：导出 building_bomb_tower_blue, building_bomb_tower_top_blue
                    new[] { new Layer(2, 1f, 1f, 0f, 0f), new Layer(1, 1f, 1f, 0f, 0f) },
                    // RED 层：导出 building_bomb_tower_red, building_bomb_tower_top_red
                    new[] { new Layer(2, 1f, 1f, 0f, 0f), new Layer(0, 1f, 1f, 0f, 0f) },
                }
            },
            {
                "building_elixir_collector_out",
                new[]
                {
                    // BLUE 层：导出 building_elixir_pump_blue
                    new[] { new Layer(0, 1f, 1f, 0f, 0f), new Layer(1, 0.6299f, 0.6299f, -245.2f, -323.95f), new Layer(2, 0.3096f, 0.3096f, -37.5f, -18.75f), new Layer(4, 1f, 1f, 0f, 0f) },
                    // RED 层：导出 building_elixir_pump_red
                    new[] { new Layer(0, 1f, 1f, 0f, 0f), new Layer(1, 0.6299f, 0.6299f, -245.2f, -323.95f), new Layer(2, 0.3096f, 0.3096f, -37.5f, -18.75f), new Layer(3, 1f, 1f, 0f, 0f) },
                }
            },
            {
                "building_goblin_hut_out",
                new[]
                {
                    // BLUE 层：导出 building_goblinHut_player  ⚠️ building_goblinHut_player: child 5 -> shape 5 越界（ShapeCount=5）
                    new[] { new Layer(0, 1f, 1f, 0f, -0.05f), new Layer(1, 1f, 1f, 0f, -0.05f), new Layer(2, 1f, 1f, -0.3f, 0.75f), new Layer(3, 1f, 1f, 7.95f, -6f) },
                    // RED 层：导出 building_goblinHut_enemy  ⚠️ building_goblinHut_enemy: child 5 -> shape 5 越界（ShapeCount=5）
                    new[] { new Layer(0, 1f, 1f, 0f, -0.05f), new Layer(1, 1f, 1f, 0f, -0.05f), new Layer(2, 1f, 1f, -0.3f, 0.75f), new Layer(4, 1f, 1f, 7.95f, -5f) },
                }
            },
            {
                "building_inferno_tower_out",
                new[]
                {
                    // BLUE 层：导出 inferno_blue_idle1_1
                    new[] { new Layer(2, 1f, 1f, 0f, 0f), new Layer(1, 0.5f, 0.5f, 0f, 0f) },
                    // RED 层：导出 inferno_red_idle1_1
                    new[] { new Layer(0, 1f, 1f, 0f, 0f), new Layer(1, 0.5f, 0.5f, 0f, 0f) },
                }
            },
            {
                "building_mortar_out",
                new[]
                {
                    // BLUE 层：导出 building_mortar1_idle1_5
                    new[] { new Layer(0, 1f, 1f, 0f, 0f) },
                    // RED 层：导出 building_mortar1_enemy_idle1_5
                    new[] { new Layer(5, 1f, 1f, 0f, 0f) },
                }
            },
            {
                "building_tesla_out",
                new[]
                {
                    // BLUE 层：导出 tesla1_blue
                    new[] { new Layer(34, 1f, 1f, 0f, 0f), new Layer(36, 1f, 1f, 0f, 0f), new Layer(4, 0.4893f, 0.1396f, 0f, 10.65f), new Layer(35, 0.4023f, 0.3496f, 0f, 33.4f), new Layer(5, 1f, 1f, 0f, 0.15f), new Layer(6, 1f, 1f, 0f, 0.05f), new Layer(7, 1f, 1f, 0f, 0.05f), new Layer(10, 1f, 1f, 0f, 0.05f), new Layer(37, 1f, 1f, 0f, 0.05f), new Layer(13, 1f, 1f, 0f, 0.05f), new Layer(14, 1f, 1f, 0f, 0.05f), new Layer(17, 1f, 1f, 0f, 0.05f), new Layer(19, 0.5f, 0.5f, 0f, 0.15f), new Layer(20, 1f, 1f, 0f, 0.05f), new Layer(22, 0.5f, 0.5f, 0f, 0.15f), new Layer(23, 1f, 1f, 0f, 0.05f), new Layer(24, 1f, 1f, 0f, 0.05f), new Layer(1, 0.5f, 0.5f, 0f, 0.15f), new Layer(38, 1f, 1f, 0f, 0.25f), new Layer(39, 1f, 1f, 0f, 0.25f), new Layer(40, 1f, 1f, 0f, 0.25f), new Layer(30, 1f, 1f, 0f, 0.25f), new Layer(32, 1f, 1f, 0f, 0.25f) },
                    // RED 层：导出 tesla1_red
                    new[] { new Layer(0, 1f, 1f, 0f, 0f), new Layer(3, 1f, 1f, 0f, 0f), new Layer(4, 0.4893f, 0.1396f, 0f, 10.6f), new Layer(2, 0.4023f, 0.3496f, 0f, 33.35f), new Layer(5, 1f, 1f, 0f, 0.1f), new Layer(6, 1f, 1f, 0f, 0f), new Layer(7, 1f, 1f, 0f, 0f), new Layer(8, 1f, 1f, 0f, 0f), new Layer(9, 1f, 1f, 0f, 0f), new Layer(10, 1f, 1f, 0f, 0f), new Layer(11, 1f, 1f, 0f, 0f), new Layer(12, 1f, 1f, 0f, 0f), new Layer(13, 1f, 1f, 0f, 0f), new Layer(14, 1f, 1f, 0f, 0f), new Layer(15, 1f, 1f, 0f, 0f), new Layer(16, 1f, 1f, 0f, 0f), new Layer(17, 1f, 1f, 0f, 0f), new Layer(18, 1f, 1f, 0f, 0f), new Layer(19, 0.5f, 0.5f, 0f, 0.1f), new Layer(20, 1f, 1f, 0f, 0f), new Layer(21, 1f, 1f, 0f, 0f), new Layer(22, 0.5f, 0.5f, 0f, 0.1f), new Layer(23, 1f, 1f, 0f, 0f), new Layer(24, 1f, 1f, 0f, 0f), new Layer(25, 1f, 1f, 0f, 0f), new Layer(26, 1f, 1f, 0f, 0f), new Layer(1, 0.5f, 0.5f, 0f, 0.1f), new Layer(27, 1f, 1f, 0f, 0.25f), new Layer(28, 1f, 1f, 0f, 0.25f), new Layer(29, 1f, 1f, 0f, 0.25f), new Layer(30, 1f, 1f, 0f, 0.25f), new Layer(31, 1f, 1f, 0f, 0.25f), new Layer(32, 1f, 1f, 0f, 0.25f), new Layer(33, 1f, 1f, 0f, 0.25f) },
                }
            },
            {
                "building_tombstone_out",
                new[]
                {
                    // BLUE 层：导出 building_tombstone_blue  ⏭ 跳过瞬时子件 2 个（<50% 帧）
                    new[] { new Layer(17, 1f, 1f, 0f, 0f), new Layer(1, 1f, 1f, -39.5f, -59.25f) },
                    // RED 层：导出 building_tombstone_red  ⏭ 跳过瞬时子件 1 个（<50% 帧）
                    new[] { new Layer(0, 1f, 1f, 0f, 0f), new Layer(1, 1f, 1f, -39.5f, -60.15f), new Layer(1, 1f, 1f, -39f, -60.15f) },
                }
            },
            {
                "building_xbow_out",
                new[]
                {
                    // BLUE 层：导出 building_xbow_attack1_1
                    new[] { new Layer(20, 0.5f, 0.5f, 0f, 0f), new Layer(1, 1f, 1f, 0f, 0f) },
                    // RED 层：导出 building_xbow_red_attack1
                    new[] { new Layer(0, 0.5f, 0.5f, 0f, 0f), new Layer(1, 1f, 1f, 0f, 0f) },
                }
            },
        };

        /// <summary>取该目录 / 队伍的层配方（目录未收录或队伍越界 ⇒ false，调用方保持原行为）。</summary>
        public static bool TryGet(string spriteDir, int team, out Layer[] layers)
        {
            layers = null;
            if (string.IsNullOrEmpty(spriteDir) || team < 0 || team > 1) return false;
            Layer[][] pair;
            if (!Table.TryGetValue(spriteDir, out pair) || pair == null) return false;
            layers = pair[team];
            return layers != null && layers.Length > 0;
        }

        /// <summary>表内目录数（自检 / 日志用）。</summary>
        public static int DirCount { get { return Table.Count; } }
    }
}
