#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""特效逐段缩放表（判据资产，⛔ 不许删）：从 `.sc` 解出每段特效的世界尺寸 ⇒ `EffectsView` 的缩放。

口径（与 `View/UnitView.cs` 的 `UnitSpriteScale` **同一套**，出处见那份注释）
--------------------------------------------------------------------------
`原版资源/sc/effects_v215.sc` 的 shape 记录（tag `0x12`）同时给出：
  · 形状多边形的外接框（`.sc` 单位）   —— 这个世界尺寸是**权威值**
  · 同一多边形在图集上占的矩形（px）
⇒ `upp = ext_W / tex_W` = **1 图集 px = 多少 `.sc` 单位**；
   换算常数 = **1000 `.sc` 单位 = 1 格**（`UnitView` 的 PEKKA 半宽 747 ≈ 官方 collision_radius 750
   = 0.75 格；塔 `.sc` 的 1 格方块 = 1021 单位）。
本工程导入特效一律 `spritePixelsToUnits = 100`（= 100 图集 px/格），正确值是 `1000 / upp`
⇒ 相对现用 PPU=100 的缩放 = **upp / 10**。

取值规则（同 `UnitView`）：只取「多边形与图集矩形 1:1 贴图」的 shape（`0.9 ≤ upp_x/upp_y ≤ 1.1`），
取中位；旋转过的形状（外接框随角度变）会把中位带偏 ⇒ 必须筛掉。

**同一条 export 的各帧段共用一个系数**
--------------------------------------------------------------------------
一条 export 在客户端常被拆成几段播（例：火球落地爆 f412..f436 与 f426..f436 是同一条 export），
⛔ 起始帧不同不代表尺寸不同 —— 系数按**该 export 的基准帧列**算一次，各段共用。
`SEGMENTS` 的第 4 项 = 基准帧列（`None` = 用本段自己的帧列）。

复跑
----
  C:\\Python312\\python.exe tools/probes/fx-scale-table.py
输出
----
  · `.ai-tmp/test/fx-scale-table.tsv` —— 逐段（用途目录 + 起始帧）的 upp / scale / 依据 export
  · stdout —— `EffectsView` 用的 C# 表项（逐字由同一份计算生成）
"""
import importlib.util
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SC = os.path.join(ROOT, '原版资源', 'sc', 'effects_v215.sc')
OUT_TSV = os.path.join(ROOT, '.ai-tmp', 'test', 'fx-scale-table.tsv')

spec = importlib.util.spec_from_file_location('scp', os.path.join(HERE, 'sc-placement.py'))
scp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scp)

# (用途目录, 起始帧, 帧数, 基准帧列, 依据 export)。用途目录/起始帧/帧数 = 客户端实际播的那一段。
# 基准帧列 = 该段所属 export 的权威帧列（同 export 的各段共用同一个系数）。
SEGMENTS = [
    ('Hit', 295, 4, None, 'effect_Hit1'),
    ('Blast', 418, 10, None, 'fireball_projectile1'),
    ('Arrow', 437, 15, None, 'projectile_arrow_basic_enemy'),
    ('Deploy', 119, 1, None, 'deploy_arrows_effect'),
    ('Death/Blue', 52, 3, None, 'Death_blue（帧列 52 62 65）'),
    ('Death/Purple', 52, 2, None, 'Death_purple（帧列 52 63）'),
    ('Death/Ground', 67, 3, None, 'death_ground（帧列 67-69）'),
    ('Death/Particle', 50, 2, [50, 66], 'death_particle（帧列 50 66）'),
    ('Spear', 363, 33, None, 'projectile_spear_360'),
    ('Cannonball', 480, 2, None, 'projectile_cannonball_small/_large'),
    ('Bowler', 356, 1, None, 'bowler_projectile'),
    ('Axe', 469, 1, None, 'executioner_projectile'),
    ('Catapult', 471, 9, None, 'catapult_projectile1'),
    ('Bomb', 482, 1, None, 'projectile_bomb'),
    ('IceWizard', 459, 9, None, 'ice_wizard_projectile'),
    ('IceSpirit', 453, 6, None, 'projectile_icespirit'),
    ('FireSpirit', 512, 24, None, 'projectile_firespirit'),
    ('Dragon', 470, 1, None, 'dragon_projectile'),
    # 火球弹体（`fireball_projectile1` 的弹体本体 = f396 出膛闪光 + f397-411 火舌拖尾）：
    # 法术飞行段（f396-425）与法师弹体（f397-411）、`SpellFlight` 目录那份是**同一条 export** ⇒ 同系数。
    ('Spell', 396, 30, list(range(396, 412)), 'fireball_projectile1（弹体本体 f396-411）'),
    ('SpellFlight', 396, 16, list(range(396, 412)), 'fireball_projectile1（弹体本体 f396-411）'),
    ('SpellFlight', 397, 15, list(range(396, 412)), 'fireball_projectile1（弹体本体 f396-411）'),
    # 火球落地爆（`fireball` export 的 f412-436）：f412-425 实心火球闪 + f426-436 余烬火星。
    ('Spell', 412, 25, None, 'fireball 落地爆炸段（帧列 412-436）'),
    ('Spell', 426, 11, list(range(412, 437)), 'fireball 落地爆炸段（同一 export，f426 起算）'),
    ('Spell', 498, 7, None, 'Arrow_enemy_ground_anim'),
    ('Spell', 206, 6, None, 'rage_effect'),
    ('Spell', 536, 60, None, 'projectile_rocket'),
    ('Spell', 73, 7, None, 'freeze_effect'),
    ('Spell', 172, 8, None, 'lightning / zap（同一段）'),
    ('Spell', 263, 31, None, 'poison'),
    ('Spell', 180, 8, None, 'log'),
    ('SpellBarrel', 0, 12, None, 'spell_goblin_barrel（帧列 0 2-12）'),
    ('HitCloud', 22, 1, None, 'bowler_hit_cloud / catapult_hit_cloud（帧列 22）'),
    ('IceWizHit', 123, 2, None, 'ice_wiz_hit（帧列 123-124）'),
]

# 帧列与 [first, first+count-1] 不一致的那几段：显式给帧号（出处 = ResPaths 的注释）
EXPLICIT_FRAMES = {
    ('Death/Purple', 52): [52, 63],
    ('SpellBarrel', 0): [0] + list(range(2, 13)),
}


def frames_of(use, first, count):
    return EXPLICIT_FRAMES.get((use, first)) or list(range(first, first + count))


def basis_of(use, first, count, basis):
    """该段算系数用的帧列：显式基准优先，否则用本段自己的帧列。"""
    if basis:
        return list(basis)
    return frames_of(use, first, count)


def shapes_in(res, frames):
    by = {s['idx']: s for s in res['shapes']}
    out = []
    for i in frames:
        s = by.get(i)
        if s and s['ext'] and s['tex']:
            x0, y0, x1, y1 = s['ext']
            ew, eh = x1 - x0 + 1, y1 - y0 + 1
            tw = s['tex'][2] - s['tex'][0] + 1
            th = s['tex'][3] - s['tex'][1] + 1
            if tw > 0 and th > 0:
                out.append((ew / tw, eh / th))
    return out


def median(v):
    v = sorted(v)
    return v[len(v) // 2]


def upp_for(res, frames):
    """返回 (upp, n_used, kind)。kind ∈ {strict, relaxed, geo, none}。"""
    us = shapes_in(res, frames)
    strict = [ux for (ux, uy) in us if 0.9 <= ux / uy <= 1.1]
    if len(strict) >= 2 or (len(strict) == 1 and len(us) == 1):
        return median(strict), len(strict), 'strict'
    relaxed = [ux for (ux, uy) in us if 0.85 <= ux / uy <= 1.18]
    if len(relaxed) >= 2:
        return median(relaxed), len(relaxed), 'relaxed'
    if us:
        return median([(ux * uy) ** 0.5 for (ux, uy) in us]), len(us), 'geo'
    return None, 0, 'none'


def fmt(v):
    return ('%.4f' % v).rstrip('0').rstrip('.')


def main():
    res = scp.parse_sc(SC)
    rows = []
    print('%-14s %-6s %-6s %-30s %-6s %-4s %-8s %-9s %s'
          % ('use', 'first', 'count', '依据 export', 'upp', 'n', 'kind', 'scale', '段内最大世界宽度(格)'))
    for use, first, count, basis, note in SEGMENTS:
        frames = basis_of(use, first, count, basis)
        upp, n, kind = upp_for(res, frames)
        by = {s['idx']: s for s in res['shapes']}
        maxw = 0
        for i in frames:
            s = by.get(i)
            if s and s['ext']:
                maxw = max(maxw, s['ext'][2] - s['ext'][0] + 1)
        if upp is None:
            print('%-14s %-6d %-6d %-30s %-6s %-4s %-8s %-9s %s'
                  % (use, first, count, note, '-', 0, kind, '(保持 1.0)', '无可用 shape'))
            rows.append((use, first, count, note, None, 0, kind, 1.0, maxw, frames))
            continue
        scale = upp / 10.0
        print('%-14s %-6d %-6d %-30s %-6.2f %-4d %-8s %-9s %.2f'
              % (use, first, count, note, upp, n, kind, fmt(scale), maxw / 1000.0))
        rows.append((use, first, count, note, upp, n, kind, scale, maxw, frames))

    # ---- TSV ----
    lines = ['use\tfirst\tcount\t依据_export\tupp(1图集px=.sc单位)\tn_used\t取值\t'
             'scale_vs_100\t段内最大世界宽度(格)\t基准帧列']
    for (use, first, count, note, upp, n, kind, scale, maxw, frames) in rows:
        basis_txt = ','.join(str(f) for f in frames)
        lines.append('%s\t%d\t%d\t%s\t%s\t%d\t%s\t%s\t%s\t%s'
                     % (use, first, count, note,
                        ('%.2f' % upp) if upp else '-', n, kind, fmt(scale),
                        ('%.2f' % (maxw / 1000.0)) if upp else '-', basis_txt))
    os.makedirs(os.path.dirname(OUT_TSV), exist_ok=True)
    with open(OUT_TSV, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    print('\nTSV -> %s (%d 段)' % (OUT_TSV, len(rows)))

    # ---- C# 表项（逐字由同一份计算生成）----
    grouped = []
    index = {}
    for (use, first, count, note, upp, n, kind, scale, maxw, frames) in rows:
        if use not in index:
            index[use] = len(grouped)
            grouped.append([use, []])
        grouped[index[use]][1].append((first, count, scale, note if upp else '无可用 shape', kind))
    print('\n---- EffectsView 表项 ----')
    print('        private static readonly Dictionary<string, ScaleSegment[]> SegmentScale =')
    print('            new Dictionary<string, ScaleSegment[]>')
    print('        {')
    for use, items in grouped:
        if len(items) == 1:
            f0, c0, s0, note, kind = items[0]
            print('            // %s（upp 取值 %s）' % (note, kind))
            print('            { "%s", new[] { Seg(%d, %d, %sf, "%s") } },'
                  % (use, f0, c0, fmt(s0), note))
            continue
        print('            // %s：一个目录里并了多段（逐段 upp 不同）' % use)
        print('            { "%s", new[]' % use)
        print('                {')
        for f0, c0, s0, note, kind in items:
            print('                    Seg(%d, %d, %sf, "%s"),   // %s（upp 取值 %s）'
                  % (f0, c0, fmt(s0), note, note, kind))
        print('                } },')
    print('        };')
    print()
    print('        private static ScaleSegment Seg(int first, int count, float scale, string note)')
    print('        {')
    print('            return new ScaleSegment { First = first, Count = count, Scale = scale, Note = note };')
    print('        }')
    return 0


if __name__ == '__main__':
    sys.exit(main())
