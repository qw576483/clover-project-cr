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

# (用途目录, 起始帧, 帧数, 依据 export)。用途目录/起始帧/帧数 = `Core/ResPaths.cs` 的特效区段（逐字对齐）。
SEGS = [
    ('Hit', 295, 4, 'effect_Hit1'),
    ('Blast', 418, 10, 'fireball_projectile1'),
    ('Arrow', 437, 15, 'projectile_arrow_basic_enemy'),
    ('Deploy', 119, 1, 'deploy_arrows_effect'),
    ('Death/Blue', 52, 3, 'Death_blue（帧列 52 62 65）'),
    ('Death/Purple', 52, 2, 'Death_purple（帧列 52 63）'),
    ('Death/Ground', 67, 3, 'death_ground（帧列 67-69）'),
    ('Spear', 363, 33, 'projectile_spear_360'),
    ('Cannonball', 480, 2, 'projectile_cannonball_small/_large'),
    ('Bowler', 356, 1, 'bowler_projectile'),
    ('Axe', 469, 1, 'executioner_projectile'),
    ('Catapult', 471, 9, 'catapult_projectile1'),
    ('Bomb', 482, 1, 'projectile_bomb'),
    ('IceWizard', 459, 9, 'ice_wizard_projectile'),
    ('IceSpirit', 453, 6, 'projectile_icespirit'),
    ('FireSpirit', 512, 24, 'projectile_firespirit'),
    ('Dragon', 470, 1, 'dragon_projectile'),
    ('Spell', 396, 41, 'fireball'),
    ('Spell', 498, 7, 'Arrow_enemy_ground_anim'),
    ('Spell', 206, 6, 'rage_effect'),
    ('Spell', 536, 60, 'projectile_rocket'),
    ('Spell', 73, 7, 'freeze_effect'),
    ('Spell', 172, 8, 'lightning / zap（同一段）'),
    ('Spell', 263, 31, 'poison'),
    ('Spell', 180, 8, 'log'),
    ('SpellBarrel', 0, 12, 'spell_goblin_barrel（帧列 0 2-12）'),
]

# 帧列与 [first, first+count-1] 不一致的那几段：显式给帧号（出处 = ResPaths 的注释）
EXPLICIT_FRAMES = {
    ('Death/Purple', 52): [52, 63],
    ('SpellBarrel', 0): [0] + list(range(2, 13)),
}


def frames_of(use, first, count):
    return EXPLICIT_FRAMES.get((use, first)) or list(range(first, first + count))


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


def upp_for(res, use, first, count):
    """返回 (upp, n_used, kind)。kind ∈ {strict, relaxed, geo, none}。"""
    us = shapes_in(res, frames_of(use, first, count))
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
    print('%-12s %-6s %-6s %-16s %-6s %-4s %-8s %-9s %s'
          % ('use', 'first', 'count', '依据 export', 'upp', 'n', 'kind', 'scale', '段内最大世界宽度(格)'))
    for use, first, count, note in SEGS:
        upp, n, kind = upp_for(res, use, first, count)
        frames = frames_of(use, first, count)
        by = {s['idx']: s for s in res['shapes']}
        maxw = 0
        for i in frames:
            s = by.get(i)
            if s and s['ext']:
                maxw = max(maxw, s['ext'][2] - s['ext'][0] + 1)
        if upp is None:
            print('%-12s %-6d %-6d %-16s %-6s %-4s %-8s %-9s %s'
                  % (use, first, count, note, '-', 0, kind, '(保持 1.0)', '无可用 shape'))
            rows.append((use, first, count, note, None, 0, kind, 1.0, maxw))
            continue
        scale = upp / 10.0
        print('%-12s %-6d %-6d %-16s %-6.2f %-4d %-8s %-9s %.2f'
              % (use, first, count, note, upp, n, kind, fmt(scale), maxw / 1000.0))
        rows.append((use, first, count, note, upp, n, kind, scale, maxw))

    # ---- TSV ----
    lines = ['use\tfirst\tcount\t依据_export\tupp(1图集px=.sc单位)\tn_used\t取值\t'
             'scale_vs_100\t段内最大世界宽度(格)']
    for (use, first, count, note, upp, n, kind, scale, maxw) in rows:
        lines.append('%s\t%d\t%d\t%s\t%s\t%d\t%s\t%s\t%s'
                     % (use, first, count, note,
                        ('%.2f' % upp) if upp else '-', n, kind, fmt(scale),
                        ('%.2f' % (maxw / 1000.0)) if upp else '-'))
    os.makedirs(os.path.dirname(OUT_TSV), exist_ok=True)
    with open(OUT_TSV, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    print('\nTSV -> %s (%d 段)' % (OUT_TSV, len(rows)))

    # ---- C# 表项（逐字由同一份计算生成）----
    grouped = []
    for (use, first, count, note, upp, n, kind, scale, maxw) in rows:
        if not grouped or grouped[-1][0] != use:
            grouped.append([use, []])
        grouped[-1][1].append((first, scale, note if upp else '无可用 shape', kind))
    print('\n---- EffectsView 表项 ----')
    print('        private static readonly Dictionary<string, Dictionary<int, float>> SegmentScale =')
    print('            new Dictionary<string, Dictionary<int, float>>')
    print('        {')
    for use, items in grouped:
        if len(items) == 1:
            f0, s0, note, kind = items[0]
            print('            // %s（upp 取值 %s）' % (note, kind))
            print('            { "%s", Single(%d, %sf) },' % (use, f0, fmt(s0)))
            continue
        print('            // %s：一个目录里并了多段（逐段 upp 不同）' % use)
        print('            { "%s", new Dictionary<int, float>' % use)
        print('                {')
        for f0, s0, note, kind in items:
            print('                    { %d, %sf },   // %s（upp 取值 %s）' % (f0, fmt(s0), note, kind))
        print('                } },')
    print('        };')
    print()
    print('        private static Dictionary<int, float> Single(int first, float scale)')
    print('        {')
    print('            return new Dictionary<int, float> { { first, scale } };')
    print('        }')
    return 0


if __name__ == '__main__':
    sys.exit(main())
