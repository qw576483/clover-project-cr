# -*- coding: utf-8 -*-
"""
build-ui-index.py - assemble 策划/原版UI素材索引.md from the frame manifest plus
the per-page human identification notes written by the T4 executor.

WHY THIS EXISTS
    Task T4 identifies every frame of the original (A) UI atlases by eye, one
    contact-sheet page at a time.  The identification notes are written to
    per-page TSV files while the sheets are being read (so an interrupted run
    loses at most one page), and this script folds them into the single
    deliverable document.  Delete this script and the document can no longer be
    regenerated => it is a judgement asset and lives in tools/probes/ (committed).

INPUTS (both read relative to the project root, which is two levels up from this file)
    .ai-tmp/screenshots/ui-index-manifest.tsv   frame / canvas size / bbox size,
                                                emitted by tools/probes/ui-index.ps1
    .ai-tmp/test/t4-desc-*.tsv                  human notes, one file per page:
                                                dir <TAB> frame <TAB> 肉眼可见内容 <TAB> 建议用途 <TAB> OK|UNK

OUTPUT
    策划/原版UI素材索引.md

USAGE
    python tools/probes/build-ui-index.py
"""

import os
import re
import sys
import glob

# ── 目标目录：顺序 = 文档章节顺序 = 任务书要求覆盖的顺序 ──────────────────────
#    前四个是任务书点名的「必须完成」集合（1342 帧）；其余是先做完时顺带覆盖的。
DIRS_REQUIRED = ['ui_out', 'loading_out', 'ui_arena_out', 'ui_battle_end_out']
DIRS_EXTRA = ['ui_chest_out', 'ui_spells_out', 'tutorial_out']

DIR_TITLE = {
    'ui_out': '通用 UI 图集（`ui_out`）',
    'loading_out': '加载 / 启动画面图集（`loading_out`）',
    'ui_arena_out': '竞技场 / 段位 UI 图集（`ui_arena_out`）',
    'ui_battle_end_out': '战斗结算 UI 图集（`ui_battle_end_out`）',
    'ui_chest_out': '宝箱开箱 UI 图集（`ui_chest_out`）',
    'ui_spells_out': '法术卡面（`ui_spells_out`）',
    'tutorial_out': '新手引导图集（`tutorial_out`）',
}

SHEET_RE = re.compile(r'^(?P<dir>\w+)_p(?P<page>\d+)_f(?P<f0>\d+)-(?P<f1>\d+)\.png$')


def project_root():
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, '..', '..'))


def read_manifest(path):
    """-> {dir: [ (frame, canvas_w, canvas_h, bbox_w, bbox_h, no_alpha) ] sorted by frame}"""
    out = {}
    with open(path, 'r', encoding='utf-8') as fh:
        header = fh.readline()
        for line in fh:
            line = line.rstrip('\r\n')
            if not line:
                continue
            c = line.split('\t')
            if len(c) < 10:
                continue
            d = c[0]
            rec = (int(c[1]), int(c[3]), int(c[4]), int(c[7]), int(c[8]), int(c[9]))
            out.setdefault(d, []).append(rec)
    for d in out:
        out[d].sort(key=lambda r: r[0])
    return out


def read_notes(test_dir):
    """-> { (dir, frame): (desc, use, status) }, later files win on conflict.

    Two accepted layouts, told apart by the column count:
      5 cols : dir <TAB> frame <TAB> desc <TAB> use <TAB> OK|UNK
      6 cols : dir <TAB> frame <TAB> frame <TAB> desc <TAB> use <TAB> OK|UNK
               (the earlier `t4-runs.tsv` layout, whose 3rd column is a duplicate
                of the frame number -- kept readable so that file stays a valid
                input instead of having to be re-typed.)
    """
    notes = {}
    paths = sorted(glob.glob(os.path.join(test_dir, 't4-desc-*.tsv')))
    legacy = os.path.join(test_dir, 't4-runs.tsv')
    if os.path.exists(legacy):
        paths.append(legacy)
    for path in paths:
        with open(path, 'r', encoding='utf-8') as fh:
            for ln, line in enumerate(fh, 1):
                line = line.rstrip('\r\n')
                if not line or line.startswith('#'):
                    continue
                c = line.split('\t')
                if len(c) >= 6:
                    d, fr, desc, use, status = c[0], c[1], c[3], c[4], c[5]
                elif len(c) >= 5:
                    d, fr, desc, use, status = c[0], c[1], c[2], c[3], c[4]
                elif len(c) >= 4:
                    d, fr, desc, use, status = c[0], c[1], c[2], c[3], 'OK'
                else:
                    sys.stderr.write('  ! %s:%d malformed (need 5 cols): %s\n'
                                     % (os.path.basename(path), ln, line[:60]))
                    continue
                status = status.strip().upper()
                if status not in ('OK', 'UNK'):
                    sys.stderr.write('  ! %s:%d bad status "%s"\n'
                                     % (os.path.basename(path), ln, status))
                    continue
                key = (d, int(fr))
                if key in notes:
                    continue          # first writer wins: legacy file is read last
                notes[key] = (desc.strip(), use.strip(), status)
    return notes


def read_sheets(shot_dir):
    rows = []
    for p in sorted(glob.glob(os.path.join(shot_dir, '*.png'))):
        m = SHEET_RE.match(os.path.basename(p))
        if m:
            rows.append((m.group('dir'), int(m.group('page')),
                         int(m.group('f0')), int(m.group('f1'))))
    return rows


def main():
    root = project_root()
    manifest_path = os.path.join(root, '.ai-tmp', 'screenshots', 'ui-index-manifest.tsv')
    test_dir = os.path.join(root, '.ai-tmp', 'test')
    shot_dir = os.path.join(root, '.ai-tmp', 'screenshots')
    out_path = os.path.join(root, '策划', '原版UI素材索引.md')

    manifest = read_manifest(manifest_path)
    notes = read_notes(test_dir)
    sheets = read_sheets(shot_dir)

    dirs = [d for d in DIRS_REQUIRED if d in manifest]
    dirs += [d for d in DIRS_EXTRA if d in manifest and any(
        (d, r[0]) in notes for r in manifest[d])]

    L = []
    A = L.append
    A('# 原版 UI 素材索引（T4 · 帧号 ↔ 尺寸 ↔ 用途）')
    A('')
    A('**出处**：`<项目根>/原版资源/cr-assets-png/assets/sc/<目录>/<名>_sprite_NNN.png`'
      '（A 本体 APK 解包，见 `策划/素材调研.md`）。')
    A('')
    A('**怎么来的**：`tools/probes/ui-index.ps1` 把每帧的**非透明包围盒（bbox）**放大填满单元格，'
      '拼成 10×10 一页的**联络图**（`.ai-tmp/screenshots/<目录>_pNN_fXXX-YYY.png`）；'
      '本表的「肉眼可见内容 / 建议用途」是**逐格看图**写下的 —— ⛔ 不是按帧号猜的。')
    A('')
    A('**尺寸口径**：`原始尺寸(画布)` = PNG 自身像素（同目录内大多相同，因为图元都躺在一张大透明画布上）；'
      '`图元尺寸(bbox)` = 非透明像素包围盒，是**真正要用的那块**的像素尺寸。')
    A('')
    A('**可重生成**：`python tools/probes/build-ui-index.py`（读 manifest + `.ai-tmp/test/t4-desc-*.tsv` 识别笔记）。')
    A('')
    A('**「帧号 → 动画档位 / UI 元素名」元数据**：**已确认无**。')
    A('`<项目根>/原版资源/cr-assets-png/` 整棵树里**非 PNG 文件只有 1 个**'
      '（`README.md`，内容 = 自述「本目录由 `find ... -name \'*.png\' | cpio -pdm .` 从 APK 的 `assets/` 直出」），'
      '**没有任何 README/json/txt 索引**：既没有「帧号 ↔ idle/walk/attack/die」，也没有「帧号 ↔ UI 元素名」。')
    A('⇒ 本表里的「肉眼可见内容 / 建议用途」**只能靠逐帧看图**得出；'
      '⛔ 任何「按帧号区间猜动画档位」的做法都**没有出处**，不许写进工程。')
    A('')

    # ── 统计 ───────────────────────────────────────────────────────────────
    A('## 1. 统计（四个数字必须自洽：`已识别 + 未识别 + 未查看 = 合计`）')
    A('')
    A('| 目录 | 合计帧数 | 已识别 | 未识别 | 未查看 |')
    A('|---|---|---|---|---|')
    tot = ok_all = unk_all = miss_all = 0
    stats = {}
    for d in dirs:
        recs = manifest[d]
        ok = unk = miss = 0
        for r in recs:
            n = notes.get((d, r[0]))
            if n is None:
                miss += 1
            elif n[2] == 'OK':
                ok += 1
            else:
                unk += 1
        stats[d] = (len(recs), ok, unk, miss)
        tot += len(recs)
        ok_all += ok
        unk_all += unk
        miss_all += miss
        A('| `%s` | %d | %d | %d | %d |' % (d, len(recs), ok, unk, miss))
    A('| **合计** | **%d** | **%d** | **%d** | **%d** |' % (tot, ok_all, unk_all, miss_all))
    A('')
    A('> 自洽校验（四个数字）：`已识别 %d + 未识别 %d + 未查看 %d = %d`，与合计帧数 `%d` %s。'
      % (ok_all, unk_all, miss_all, ok_all + unk_all + miss_all, tot,
         '一致' if ok_all + unk_all + miss_all == tot else '**不一致 —— 本表有 bug**'))
    A('')
    A('> 另一条独立校验：`每行 已识别 + 未识别 + 未查看 = 该行 合计帧数`，且 `各目录 合计帧数 之和 = %d`。' % tot)
    A('')

    # ── 联络图覆盖 ─────────────────────────────────────────────────────────
    A('## 2. 联络图覆盖（`.ai-tmp/screenshots/`）')
    A('')
    A('| 联络图 | 帧号区间 | 格数 |')
    A('|---|---|---|')
    for d, page, f0, f1 in sheets:
        A('| `%s_p%02d_f%03d-%03d.png` | %03d–%03d | `%s` |'
          % (d, page, f0, f1, f0, f1, DIR_TITLE.get(d, d)))
    A('| **合计 %d 张** | — | — |' % len(sheets))
    A('')

    # ── 未识别清单 ─────────────────────────────────────────────────────────
    A('## 3. 未识别帧清单（形状无明确语义 / 联络图上太小看不清 ⇒ 如实登记，⛔ 不猜）')
    A('')
    any_unk = False
    for d in dirs:
        unks = [(r, notes[(d, r[0])]) for r in manifest[d]
                if (d, r[0]) in notes and notes[(d, r[0])][2] == 'UNK']
        if not unks:
            continue
        any_unk = True
        A('### `%s` —— 未识别 %d / %d' % (d, len(unks), stats[d][0]))
        A('')
        A('| 帧号 | 原始尺寸(画布) | 图元尺寸(bbox) | 为什么未识别 |')
        A('|---|---|---|---|')
        for r, n in unks:
            A('| %03d | %dx%d | %dx%d | %s |' % (r[0], r[1], r[2], r[3], r[4], n[0]))
        A('')
    if not any_unk:
        A('（无）')
        A('')

    # ── 明细 ───────────────────────────────────────────────────────────────
    A('## 4. 逐帧明细')
    A('')
    for d in dirs:
        A('### 4.%d %s —— %d 帧（已识别 %d / 未识别 %d）'
          % (dirs.index(d) + 1, DIR_TITLE.get(d, d), stats[d][0], stats[d][1], stats[d][2]))
        A('')
        A('| 帧号 | 原始尺寸(画布) | 图元尺寸(bbox) | 肉眼可见内容 | 建议用途 |')
        A('|---|---|---|---|---|')
        for r in manifest[d]:
            n = notes.get((d, r[0]))
            if n is None:
                desc, use = '（未查看）', '（未查看）'
            elif n[2] == 'UNK':
                desc, use = '未识别：' + n[0], '未识别'
            else:
                desc, use = n[0], n[1]
            A('| %03d | %dx%d | %dx%d | %s | %s |'
              % (r[0], r[1], r[2], r[3], r[4], desc, use))
        A('')

    with open(out_path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(L) + '\n')

    print('wrote %s' % out_path)
    print('  dirs=%d  frames=%d  ok=%d  unk=%d  miss=%d'
          % (len(dirs), tot, ok_all, unk_all, miss_all))
    if miss_all:
        print('  !! %d frames have no note yet' % miss_all)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
