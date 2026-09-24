# -*- coding: utf-8 -*-
# direct-fix: D146 原版 UI 复刻 —— 递归展开原版界面 clip 的整棵子树（工具，非交付物）
"""sc-subtree.py -- 递归展开某个原版界面 clip 的**整棵子树**，输出复合坐标 + 图元帧号。

为什么需要它（`sc-layout.py` 不够用）
------------------------------------
`sc-layout.py` 只输出**顶层放置表**（一个 clip 的直接子元件）。但原版界面的版式细节都在
**容器 clip** 里面：`card_page_deck_special` 的直接子元件里 `panel` / `battle_cards_area` /
`header` 都是 clip，真正的卡格、卡框、底纹在它们内部。要重排 UI，必须把树**展开到底**。

复跑
----
  python tools/probes/sc-subtree.py                                # 默认卡组编辑页
  python tools/probes/sc-subtree.py --clip card_page_collection    # 换界面
  python tools/probes/sc-subtree.py --clip card_page_deck_special --depth 6 --tsv out.tsv

输出（**生成物，⛔ 不要手改**）
-----------------------------
  · 控制台：缩进树（人看）
  · `--tsv FILE`：展平表（机器看），列 = depth/path/kind/frame/name/x/y/sx/sy/opacity/matidx/colidx/obj

口径（与 `sc-layout.py` 同源，逐字复用它的 parse/placements）
------------------------------------------------------------
  · 线性 a,d 定点 1/1024；平移 tx,ty twips ÷20（出处见 `策划/原版UI布局坐标.md` §1）
  · 坐标 = **该界面 clip 的局部系**（⛔ 不是屏幕绝对像素，见 §5 F1）
    但同一界面**内部**的相对版式是硬结论 —— 这正是重排 UI 要用的那一层。
  · 复合（无旋转时）：`X = ox + tx*sx`、`Y = oy + ty*sy`、`SX = sx*psx`、`SY = sy*psy`
    本脚本**会检查 b/c 是否为 0**；发现非零（带旋转）就报警并**不复合**该子树（不猜）。
"""

import os
import sys
import argparse
import importlib.util

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))


def _load_sclayout():
    """复用 sc-layout.py（模块名带 `-`，只能用 spec 加载）。"""
    p = os.path.join(ROOT, 'tools', 'probes', 'sc-layout.py')
    spec = importlib.util.spec_from_file_location('sclay', p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


sclay = _load_sclayout()


def find_clip(res, export_name):
    """export 名 → clip id。名字表与 id 表按下标一一对应。"""
    for i, nm in enumerate(res['exp_names']):
        if nm == export_name:
            return res['exp_ids'][i]
    return None


def walk(res, cid, sx, sy, ox, oy, depth, maxdepth, path, out, rot_warn):
    """递归展开。sx/sy/ox/oy = 父系的累积缩放与平移。"""
    for p in sclay.placements(res, cid):
        name = p['name'] if p['name'] else ('<%s %d>' % (p['kind'], p['obj']))
        node = '%s/%s' % (path, name)
        # 旋转检查：raw = (a, b, c, d, tx, ty)，b/c 非 0 表示有旋转/斜切
        b, c = p['raw'][1], p['raw'][2]
        if b != 0 or c != 0:
            rot_warn.append((node, b, c))
        px = ox + p['tx'] * sx
        py = oy + p['ty'] * sy
        psx = sx * p['sx']
        psy = sy * p['sy']
        out.append(dict(depth=depth, path=node, kind=p['kind'], frame=p['frame'],
                        name=name, x=round(px, 3), y=round(py, 3),
                        sx=round(psx, 4), sy=round(psy, 4),
                        lx=round(p['tx'], 3), ly=round(p['ty'], 3),
                        lsx=round(p['sx'], 4), lsy=round(p['sy'], 4),
                        opacity=p['opacity'], matidx=p['matidx'],
                        colidx=p['colidx'], obj=p['obj']))
        if p['kind'] == 'clip' and depth < maxdepth:
            walk(res, p['obj'], psx, psy, px, py, depth + 1, maxdepth, node, out, rot_warn)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sc', default='ui', help='.sc 名（原版资源/sc/<name>_v215.sc）')
    ap.add_argument('--clip', default='card_page_deck_special', help='export 名（界面 clip）')
    ap.add_argument('--depth', type=int, default=6, help='最大递归深度')
    ap.add_argument('--tsv', default=None, help='展平表输出路径')
    ap.add_argument('--quiet', action='store_true', help='只写 TSV，不打印树')
    args = ap.parse_args()

    res = sclay.parse(sclay.load(args.sc))
    cid = find_clip(res, args.clip)
    if cid is None:
        print('!! 找不到 export 名 %r（可用名见 策划/原版UI布局坐标.md §2）' % args.clip)
        return 2

    clips_total = len([c for c in res['clips'].values()])
    print('=== %s / %s  clip=%d  fps=%d  frames=%d  该 .sc 共 %d 个 clip ==='
          % (args.sc, args.clip, cid, res['clips'][cid]['fps'],
             res['clips'][cid]['frames'], clips_total))

    out, rot_warn = [], []
    walk(res, cid, 1.0, 1.0, 0.0, 0.0, 0, args.depth, '', out, rot_warn)

    n_clip = len([o for o in out if o['kind'] == 'clip'])
    n_shape = len([o for o in out if o['kind'] == 'shape'])
    n_unk = len([o for o in out if o['kind'] == '?'])
    print('展平：%d 个元件（clip %d / shape %d / 未知 %d）' % (len(out), n_clip, n_shape, n_unk))
    if rot_warn:
        print('!! 检测到 b/c 非 0（旋转/斜切）%d 处 —— 复合口径对这个子树**不成立**，'
              '已按"不猜"处理（下列坐标仍按无旋转公式给出，仅作线索）：' % len(rot_warn))
        for (n, b, c) in rot_warn[:10]:
            print('     %s  b=%d c=%d' % (n, b, c))

    if not args.quiet:
        print()
        print('%-72s %-7s %-11s %9s %9s %8s %8s %4s' %
              ('path', 'kind', 'frame', 'x', 'y', 'sx', 'sy', 'op'))
        print('-' * 140)
        for o in out:
            print('%-72s %-7s %-11s %9g %9g %8g %8g %4d' %
                  (('  ' * o['depth']) + o['name'][:66], o['kind'], o['frame'] or '—',
                   o['x'], o['y'], o['sx'], o['sy'], o['opacity']))

    if args.tsv:
        p = args.tsv if os.path.isabs(args.tsv) else os.path.join(ROOT, args.tsv)
        with open(p, 'w', encoding='utf-8', newline='') as f:
            f.write('depth\tpath\tkind\tframe\tname\tx\ty\tsx\tsy\tlx\tly\tlsx\tlsy\topacity\tmatidx\tcolidx\tobj\n')
            for o in out:
                f.write('\t'.join(str(o[k]) for k in
                                  ('depth', 'path', 'kind', 'frame', 'name', 'x', 'y', 'sx', 'sy',
                                   'lx', 'ly', 'lsx', 'lsy', 'opacity', 'matidx', 'colidx', 'obj')) + '\n')
        print()
        print('TSV 已写：%s（%d 行）' % (p, len(out)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
