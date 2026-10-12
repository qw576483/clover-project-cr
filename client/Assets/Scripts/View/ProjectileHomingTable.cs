// ⚠️ 本文件**由生成器产出**，不要手改（手改会被下次重跑丢掉）。
//   生成器：`tools/probes/gen-projectile-homing.py`（用法见该文件头部）。
//
// 投射物名 → 是否**制导**（`homing`）。出处 = `server/game/table/tsv/unit.tsv` 里
// **`kind == 3`（投射物本体）** 那一行的 `homing` 列；该行的 `key` 就是发射者行
// `projectile_key` 列的值（例：`archers.projectile_key = ArcherArrow` ↔ 投射物行 `key = ArcherArrow`）。
// ⛔ 发射者行按约定 `homing` 恒 0，读那些行会得出"全部不制导"的错结论。
//
// 用法（`BattleViewRoot` 的攻击排期）：`homing == false` 的投射物飞向**起手时刻锁定的目标点**
// （⛔ 不逐帧重瞄），`true` 的每帧对准目标当前位置。
namespace CR.View
{
    /// <summary>投射物名 → 是否制导（见文件头出处）。</summary>
    public static class ProjectileHomingTable
    {
        /// <summary>投射物名 → `homing`。键 = 发射者行 `projectile_key` 的取值。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, bool> Table =
            new System.Collections.Generic.Dictionary<string, bool>
            {
                { "ArcherArrow", true },
                { "SpearGoblinProjectile", true },
                { "MinionSpit", true },
                { "WitchProjectile", true },
                { "BombSkeletonProjectile", false },
                { "MusketeerProjectile", true },
                { "BabyDragonProjectile", true },
                { "chr_wizardProjectile", true },
                { "ice_wizardProjectile", true },
                { "RoyalGiantProjectile", true },
                { "PrincessProjectile", false },
                { "LavaHoundProjectile", true },
                { "IceSpiritsProjectile", true },
                { "FireSpiritsProjectile", true },
                { "BowlerProjectile", false },
                { "MegaMinionSpit", true },
                { "BlowdartGoblinProjectile", true },
                { "AxeManProjectile", false },
                { "MovingCannonProjectile", true },
                { "TowerCannonball", true },
                { "MortarProjectile", false },
                { "BombTowerProjectile", false },
                { "xbow_projectile", true },
                { "TowerPrincessProjectile", true },
                { "KingProjectile", true },
                { "LavaPupProjectile", true },
            };

        /// <summary>表里收录的投射物条数（自检用）。</summary>
        public static int Count { get { return Table.Count; } }

        /// <summary>取某投射物的 `homing`；表里没有返回 false。**表外的一律按制导处理**（⛔ 不猜）。</summary>
        public static bool IsHoming(string projectileKey, out bool known)
        {
            bool v = false;
            known = !string.IsNullOrEmpty(projectileKey) && Table.TryGetValue(projectileKey, out v);
            return known ? v : true;
        }
    }
}
