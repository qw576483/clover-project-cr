#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sc-as1-index.py  --  AS1 片判据资产：把「2.1.5 权威 .sc」的动画分组**全量**解出来。

与 sc-anim-index.py（R5b，3 个目录）的关系
-----------------------------------------
本脚本 `import` sc-anim-index.py 复用它的解析器（load_sc / parse_unit / analyse / fmt_ranges / _tile），
⛔ 不复制、不改写那份解析逻辑；R5b 那份脚本保持原样（它自己的 --selfcheck 只对 R5b 的三个目录成立）。

本脚本做的事
------------
1. 枚举 `原版资源/cr-assets-png/assets/sc/*_out` 目录，找同名 `原版资源/sc/<name>_v215.sc`；
2. **版本指纹**：`ShapeCount`（.sc 头部声明）vs 目录内 PNG 数 → 写 TSV（✅相等 / ❌不等 / 下载失败）；
3. 对 ✅ 的目录解出 `export 名 → clip id → shapeID(=frame_NNN)`，按 export 名语义归 `idle/walk/attack/die/…`；
4. 抽检拼图：每个「目录 × 档位」一张（首/中/末 clip × 首/中/末帧）；
5. 产出 `策划/单位帧段表.md`（重写，旧 v1.0.0 结论留「附录 B」）与 `策划/单位动画分组表.md`（全量三元组）。

复跑
----
  python tools/probes/sc-as1-index.py --root <项目根> --all
  python tools/probes/sc-as1-index.py --root <项目根> --selfcheck

口径（与 AS1 任务书一致，⛔ 不许自行改名）
--------------------------------------
- `frame_NNN` 的 NNN = `.sc` 里 shape 定义序号（0 起），对应 `<dir>/<name>_sprite_NNN.png`；
- `export 名` = 原版自己的命名；组名 = export 名去掉结尾 `_N`，`_N` = 视角/朝向组；
- `enemy_` 前缀 = 红方（同一单位另一配色）；其余 = 主套（蓝方）；
- 判不出档位的组 ⇒ `未命名组 N`，⛔ 不瞎起名。
"""

import argparse
import glob
import importlib.util
import json
import os
import re
import subprocess
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

URL_TPL = ('https://raw.githubusercontent.com/smlbiobot/cr/master/'
           'apk/2.1.5/com.supercell.clashroyale-2.1.5/assets/sc/%s')

COLLAGE_UNITS = ['chr_knight', 'chr_musketeer', 'chr_archer', 'chr_giant', 'chr_pekka',
                 'chr_hog_rider', 'chr_barbarian', 'chr_goblin', 'chr_minion', 'chr_wizard',
                 'chr_valkyrie', 'chr_baby_dragon']

TIER_ORDER = ['idle', 'walk', 'attack', 'die', 'spawn', 'hit', '其他']


def load_parser(root):
    p = os.path.join(root, 'tools', 'probes', 'sc-anim-index.py')
    spec = importlib.util.spec_from_file_location('scanim_r5b', p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def load_any(R, path):
    """2.1.5 Android 那批 `.sc` 是 **QuickBMS 解密后的明文**（不带头、不是 lzma）；
    v1.0.0 那批才是 `SC` + LZMA 壳。两者都支持（按头部字节分派）。"""
    raw = open(path, 'rb').read()
    if raw[:2] == b'SC':
        try:
            return R.load_sc(path)
        except Exception:
            pass
    return raw


# ---------------------------------------------------------------------------
# 档位 / 阵营判定（语义规则，全部写进产出的「判定依据」列）
# ---------------------------------------------------------------------------
def tier_of(name):
    n = name.lower()
    if 'idle' in n:
        return 'idle'
    if 'run' in n or 'walk' in n:
        return 'walk'
    if 'attack' in n:
        return 'attack'
    if 'die' in n or 'death' in n:
        return 'die'
    if 'spawn' in n or 'deploy' in n:
        return 'spawn'
    if 'hit' in n or 'hurt' in n:
        return 'hit'
    return None


def side_of(name):
    n = name.lower()
    if 'enemy_' in n:
        return '红(enemy_)'
    if '_blue_' in n:
        return '蓝(_blue_)'
    return '蓝(主套)'


def sprite_prefix(frames_dir):
    for f in sorted(os.listdir(frames_dir)):
        mm = re.match(r'^(.*)_(\d{3,4})\.png$', f)
        if mm:
            return mm.group(1), len(mm.group(2))
    return 'frame', 3


def frame_path(frames_dir, prefix, width, sid):
    p = os.path.join(frames_dir, '%s_%0*d.png' % (prefix, width, sid))
    return p if os.path.isfile(p) else os.path.join(frames_dir, 'frame_%03d.png' % sid)


# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------
def collect(root):
    sc_dir = os.path.join(root, '原版资源', 'sc')
    png_root = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc')
    rows = []
    for d in sorted(os.listdir(png_root)):
        if not d.endswith('_out'):
            continue
        name = d[:-4]
        sc = os.path.join(sc_dir, name + '_v215.sc')
        fd = os.path.join(png_root, d)
        png_n = len([f for f in os.listdir(fd) if f.lower().endswith('.png')])
        rows.append(dict(dir=d, name=name, sc=sc, frames_dir=fd, png_n=png_n,
                         sc_exists=os.path.isfile(sc),
                         bytes=os.path.getsize(sc) if os.path.isfile(sc) else 0,
                         url=URL_TPL % name))
    return rows


def analyse_rows(R, rows, log):
    derived, failed = [], []
    for r in rows:
        if not r['sc_exists'] or r['bytes'] < 100:
            r['ShapeCount'] = None
            r['equal'] = False
            r['note'] = '下载失败/文件不存在'
            failed.append(r)
            continue
        try:
            res = R.parse_unit(load_any(R, r['sc']))
        except Exception as e:                                    # 非 .sc / 解析失败
            r['ShapeCount'] = None
            r['equal'] = False
            r['note'] = '解析失败: %s' % str(e)[:60]
            failed.append(r)
            continue
        r['ShapeCount'] = res['ShapeCount']
        r['ExportCount'] = len(res['exp_names'])
        r['TotalsAnim'] = res['TotalsAnim']
        r['unknown'] = len(res['unknown'])
        r['equal'] = (res['ShapeCount'] == r['png_n'])
        r['note'] = ''
        if r['equal']:
            r['raw'] = res
            derived.append(r)
        else:
            failed.append(r)
        log('  %-28s ShapeCount=%-6s PNG=%-6s %s' %
            (r['dir'], r['ShapeCount'], r['png_n'], 'OK' if r['equal'] else 'MISMATCH'))
    return derived, failed


def build_unit(R, r, viewed):
    res = r['raw']
    mapping, groups = R.analyse(res)
    prefix, width = sprite_prefix(r['frames_dir'])
    r['prefix'] = prefix

    # 档位聚合：key = (tier, side)
    agg = {}
    unmapped = []
    for g in groups:
        t = tier_of(g['group'])
        s = side_of(g['group'])
        if t is None:
            unmapped.append(g)
            continue
        a = agg.setdefault((t, s), dict(groups=[], sids=set(), clips=[]))
        a['groups'].append(g['group'])
        a['sids'] |= set(g['sids'])
        a['clips'] += g['clip_ids']

    tiers = {}
    for (t, s), a in agg.items():
        sids = sorted(a['sids'])
        tiers.setdefault(t, {})[s] = dict(
            groups=sorted(a['groups']), sids=sids,
            ranges=R.fmt_ranges(sids), count=len(sids),
            clip_min=min(a['clips']), clip_max=max(a['clips']), n_clips=len(a['clips']),
            contiguous=(sids == list(range(sids[0], sids[-1] + 1))) if sids else False)

    oob = sorted({s for m in mapping for s in m['sids'] if s < 0 or s >= res['ShapeCount']})
    r['mapping'] = mapping
    r['groups'] = groups
    r['tiers'] = tiers
    r['unmapped'] = [g['group'] for g in unmapped]
    r['oob'] = oob
    r['oob_clips'] = sorted({m['name'] for m in mapping
                             if any(s < 0 or s >= res['ShapeCount'] for s in m['sids'])})
    return r


def make_collages(R, r, out_dir, viewed):
    """每个「目录 × 档位」一张：行 = 该档位首/中/末条 clip，列 = 该 clip 首/中/末帧。"""
    if r['name'] not in COLLAGE_UNITS:
        return []
    out = []
    for t in TIER_ORDER:
        if t not in r['tiers']:
            continue
        side = sorted(r['tiers'][t])[0]
        a = r['tiers'][t][side]
        members = [m for m in r['mapping'] if m['name'].rsplit('_', 1)[0] in a['groups']]
        if not members:
            continue
        idxs = sorted({0, len(members) // 2, len(members) - 1})
        paths, labels = [], []
        for ri in idxs:
            m = members[ri]
            sids = m['sids']
            picks = sids if len(sids) <= 3 else [sids[0], sids[len(sids) // 2], sids[-1]]
            for si in picks:
                paths.append(frame_path(r['frames_dir'], r['prefix'], 3, si))
                labels.append('%s f%d' % (m['name'].rsplit('_', 1)[-1], si))
        if not paths:
            continue
        fn = os.path.join(out_dir, 'AS1-%s-%s.png' % (r['name'], t))
        R._tile(paths, labels, cell=110).save(fn)
        out.append(fn)
    return out


# ---------------------------------------------------------------------------
# 文档
# ---------------------------------------------------------------------------
def write_fingerprint_tsv(path, rows, failed_set):
    L = ['目录\t.sc名\tShapeCount\tPNG数\t是否相等\tURL\t字节']
    for r in rows:
        if r['equal']:
            flag = '✅同版本，可作权威'
        elif r['ShapeCount'] is None:
            flag = '❌下载失败/无法解析: ' + r.get('note', '')
        else:
            flag = '❌对不上'
        L.append('%s\t%s\t%s\t%d\t%s\t%s\t%d' %
                 (r['dir'], r['name'] + '_v215.sc', r['ShapeCount'] if r['ShapeCount'] is not None else 'NA',
                  r['png_n'], flag, r['url'] if r['sc_exists'] else '(未下载)', r['bytes']))
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    open(path, 'w', encoding='utf-8').write('\n'.join(L) + '\n')
    return path


def _tier_cells(r, t):
    """返回 (clip id 串, 帧数, 帧区间, 蓝/红, 判定依据)。"""
    if t not in r['tiers']:
        return ('未找到', 0, '-', '-', '该目录无 `%s*` 语义的 export 名' % t)
    keys = sorted(r['tiers'][t])
    cid = ', '.join('%d–%d（%d 条 clip）' % (r['tiers'][t][s]['clip_min'],
                                            r['tiers'][t][s]['clip_max'],
                                            r['tiers'][t][s]['n_clips']) for s in keys)
    cnt = sum(r['tiers'][t][s]['count'] for s in keys)
    rng = '；'.join('`%s`' % r['tiers'][t][s]['ranges'] for s in keys)
    sides = ' + '.join(keys)
    ev = []
    for s in keys:
        a = r['tiers'][t][s]
        ev.append('`%s`（export 名语义含 `%s`）' % ('`/`'.join(a['groups']), t))
    vk = r['name'] + ':' + t
    vn = r.get('viewed', {}).get(t)
    tail = '；看图确认：%s' % vn if vn else '；**未看图（仅按 export 名判定）**'
    uniq = '唯一帧不连续' if (t in r['tiers'] and not all(r['tiers'][t][s]['contiguous'] for s in keys)) else '唯一帧连续'
    return (cid, cnt, rng, sides, '；'.join(ev) + '；' + uniq + tail)


def write_frame_segments(root, rows, collages, viewed_map):
    derived = [r for r in rows if r.get('equal')]
    failed = [r for r in rows if not r.get('equal')]
    L = []
    A = L.append
    A('# 单位帧段表（**2.1.5 权威 `.sc` 全量解析**，AS1 片重写）')
    A('')
    A('> **本版 = AS1。数据源换成 CR 2.1.5 的 Android 解密 `.sc`**（`apk/2.1.5/.../assets/sc/<name>`，')
    A('> 原文件**无扩展名**、已是 QuickBMS 解密后的明文），本地副本 = `原版资源/sc/<name>_v215.sc`。')
    A('> 权威性判定 = **版本指纹**：`.sc` 头部 `ShapeCount` **==** 该目录 PNG 数（逐目录核，见 §1）。')
    A('>')
    A('> ⚠️ **替代关系**：§9「附录 B」保留 R5b/v1.0.0 来源的旧结论（三个目录 chr_knight / chr_musketeer /')
    A('> chr_archer），**已被本版 2.1.5 权威源替换，以本表 §2 起为准**。')
    A('> ⛔ 旧 R1 的 `BLOCKED`（「该目录不要填 AnimRanges」）**仍是被推翻的状态**，只作过程记录。')
    A('')
    A('## 0. 口径与复跑')
    A('')
    A('| 项 | 值 |')
    A('| --- | --- |')
    A('| `.sc` 持久副本 | `原版资源/sc/<name>_v215.sc`（本轮新增 **%d** 个） |' % len([r for r in rows if r['sc_exists']]))
    A('| 逐帧 PNG | `原版资源/cr-assets-png/assets/sc/<dir>/<name>_sprite_NNN.png` |')
    A('| 判据资产 | `tools/probes/sc-as1-index.py`（解析器复用 `tools/probes/sc-anim-index.py`，未改动它） |')
    A('| 指纹表 ||')
    A('| 全量三元组 | `策划/单位动画分组表.md` |')
    A('')
    A('**关键口径**（与任务书一致）：`frame_NNN` 的 `NNN` = `.sc` 里 shape 定义序号（0 起）；')
    A('`export 名` = 原版自带命名，组名 = 去掉结尾 `_N`（`_N` = 视角/朝向组）；')
    A('`enemy_` 前缀 = 红方（同一单位另一配色），其余 = 主套（蓝方）；判不出档位的组一律写 `未命名组 N`。')
    A('')
    A('复跑（一条命令重出本表所有数字）：')
    A('')
    A('```powershell')
    A('cd <项目根>')
    A("$env:PYTHONIOENCODING='utf-8'")
    A('python tools/probes/sc-as1-index.py --root . --all --selfcheck')
    A('```')
    A('')
    A('## 1. 版本指纹核对（每个目录逐条）')
    A('')
    A('| 目录 | `.sc` | ShapeCount | PNG 数 | 结论 |')
    A('| --- | --- | --- | --- | --- |')
    for r in rows:
        c = ('✅同版本，可作权威' if r['equal'] else
             ('❌下载失败/无法解析（%s）' % r.get('note', '') if r['ShapeCount'] is None else '❌对不上'))
        A('| `%s` | `%s` | %s | %d | %s |' %
          (r['dir'], r['name'] + '_v215.sc', r['ShapeCount'] if r['ShapeCount'] is not None else '—',
           r['png_n'], c))
    A('')
    A('- 三数：**✅相等 %d / ❌不等 %d / 下载失败 %d**（口径：不等与失败分开计，失败=未取得可解析文件）' %
      (len([r for r in rows if r['equal']]),
       len([r for r in rows if r['ShapeCount'] is not None and not r['equal']]),
       len([r for r in rows if r['ShapeCount'] is None])))
    A('')
    A('## 2. 逐目录档位表（**只含指纹 ✅ 的目录**；❌ 的目录⛔ 不许拿去填帧段）')
    A('')
    for r in derived:
        A('### `%s` （`.sc` = `%s`，ShapeCount=%d == PNG %d ✅）' %
          (r['dir'], r['name'] + '_v215.sc', r['ShapeCount'], r['png_n']))
        A('')
        A('| 档位 | clip id | 帧数 | frame_NNN 区间或列表 | 蓝/红 | 判定依据 |')
        A('| --- | --- | --- | --- | --- | --- |')
        for t in TIER_ORDER:
            if t == '其他':
                continue
            cid, cnt, rng, sides, ev = _tier_cells(r, t)
            A('| %s | %s | %d | %s | %s | %s |' % (t, cid, cnt, rng, sides, ev))
        for i, g in enumerate(sorted(set(r['unmapped']))):
            gg = [x for x in r['groups'] if x['group'] == g][0]
            A('| 未命名组 %d（`%s`） | %d–%d | %d | `%s` | %s | export 名无 idle/run/attack/die 语义 ⇒ ⛔ 不瞎起名 |' %
              (i + 1, g, gg['clip_ids'][0], gg['clip_ids'][-1], gg['n_sids_unique'],
               R_fmt(gg['sids']), side_of(g)))
        A('')
        if r.get('oob'):
            A('> ⚠️ **越界引用**：%d 个 shapeID ≥ ShapeCount(%d)（frame_NNN.png 不存在）⇒ 下列 clip 的帧**不可用**：%s'
              % (len(r['oob']), r['ShapeCount'], ', '.join('`%s`' % x for x in r['oob_clips'])))
            A('>')
        A('> export 条目数=%d；未识别 tag=%d；全部组名：%s' %
          (r.get('ExportCount', -1), r.get('unknown', -1),
           ', '.join('`%s`' % g['group'] for g in r['groups'])))
        A('')
    if not derived:
        A('（无）')
        A('')
    A('## 3. 可直接填 `UnitView.AnimRanges` 的四元组')
    A('')
    A('**帧号语义**：`start` = 该档位唯一帧的最小 `frame_NNN` 序号；`count` = 该档位唯一帧个数。')
    A('⚠️ `连续?` = 否时，`start/count` **不能**当连续区间用，必须按 §2 的帧号列表逐帧取。')
    A('')
    A('| 目录 | idle | walk | attack | die | 连续? |')
    A('| --- | --- | --- | --- | --- | --- |')
    for r in derived:
        cells, cont = [], True
        for t in ('idle', 'walk', 'attack', 'die'):
            if t in r['tiers']:
                s = sorted(r['tiers'][t])[0]
                a = r['tiers'][t][s]
                cells.append('(%d, %d) ｜ %s' % (a['sids'][0], a['count'], a['ranges']))
                cont = cont and a['contiguous']
            else:
                cells.append('未找到')
        A('| `%s` | %s | %s | %s | %s | %s |' %
          (r['name'], cells[0], cells[1], cells[2], cells[3], '是' if cont else '否'))
    A('')
    A('## 9. 附录 B：v1.0.0 旧结论（⛔ 保留，已被 2.1.5 权威源替换）')
    A('')
    oldp = os.path.join(root, '.ai-tmp', 'test', 'AS1-old-frame-seg-backup.md')
    if os.path.isfile(oldp):
        A('> 以下为 AS1 重写前 `策划/单位帧段表.md` 的原文（v1.0.0 来源的 R5b/R1 结论）。')
        A('> **三个目录（chr_knight / chr_musketeer / chr_archer）的旧数字仅作过程记录；以本文 §2/§3 的 2.1.5 数字为准。**')
        A('')
        A(open(oldp, encoding='utf-8').read())
    else:
        A('（无备份）')
    os.makedirs(os.path.join(root, '策划'), exist_ok=True)
    open(os.path.join(root, '策划', '单位帧段表.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
    return len(derived)


def write_group_table(root, rows):
    derived = [r for r in rows if r.get('equal')]
    L = []
    A = L.append
    A('# 单位动画分组表（`.sc` 显式三元组：export 名 → clip id → shapeID）')
    A('')
    A('> **本文件是生成物，⛔ 不要手改。** 生成器 = `tools/probes/sc-as1-index.py`；')
    A('> 复跑 = `python tools/probes/sc-as1-index.py --root . --all`。')
    A('>')
    A('> 本表回答：「这个动画在原版里叫什么、clip id 多少、逐帧用哪些 `frame_NNN.png`」。')
    A('> `export 名 ↔ clip id` 取自 `.sc` **Export 表自带的 id 数组** ⇒ **显式引用，不是顺序推断**。')
    A('> 档位（idle/walk/attack/die）与 `AnimRanges` 结论在 **`策划/单位帧段表.md`**。')
    A('>')
    A('> 数据源 = **CR 2.1.5**（`原版资源/sc/<name>_v215.sc`）；仅列**指纹 ✅** 的目录（共 %d 个）。' % len(derived))
    A('')
    for r in derived:
        A('---')
        A('')
        A('## `%s`' % r['name'])
        A('')
        A('`%s` ｜ ShapeCount=%d ｜ ExportCount=%d ｜ TotalsAnimations=%s ｜ PNG %d 张'
          % (os.path.basename(r['sc']), r['ShapeCount'], r.get('ExportCount', -1),
             r.get('TotalsAnim', '?'), r['png_n']))
        A('')
        A('### 动画组表')
        A('')
        A('| 组名（export 名去 `_N`） | clip id | FPS | timeline 帧数 | 唯一像素帧 | frame_NNN 区间 | 蓝/红 | 档位 |')
        A('| --- | --- | --- | --- | --- | --- | --- | --- |')
        for g in r['groups']:
            t = tier_of(g['group']) or ('未命名组' if g['group'] in r['unmapped'] else '-')
            A('| `%s` | %d – %d（%d 条） | %s | %s | %d | `%s` | %s | %s |'
              % (g['group'], g['clip_ids'][0], g['clip_ids'][-1], len(g['clip_ids']),
                 ','.join(str(x) for x in g['fps']), ','.join(str(x) for x in g['frames']),
                 g['n_sids_unique'], R_fmt(g['sids']), side_of(g['group']), t))
        A('')
        A('### export 名 → clip id → shapeID 全表（%d 条，逐条）' % len(r['mapping']))
        A('')
        A('| # | export 名 | clip id | FPS | timeline 帧数 | 像素帧数 | frame_NNN（shapeID） | 档位 | 蓝/红 |')
        A('| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
        for m in r['mapping']:
            t = tier_of(m['name']) or '未命名组'
            A('| %d | `%s` | %d | %s | %s | %d | `%s` | %s | %s |'
              % (m['i'], m['name'], m['clip_id'], m['fps'], m['frames'], len(m['sids']),
                 R_fmt(sorted(m['sids'])) if m['sids'] else '-', t, side_of(m['name'])))
        A('')
    open(os.path.join(root, '策划', '单位动画分组表.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
    return len(derived)


def R_fmt(sids):
    if not sids:
        return '-'
    out, start, prev = [], sids[0], sids[0]
    for s in sids[1:]:
        if s == prev + 1:
            prev = s
            continue
        out.append((start, prev))
        start = prev = s
    out.append((start, prev))
    return ' '.join(('%d-%d' % (a, b)) if a != b else str(a) for a, b in out)


# ---------------------------------------------------------------------------
# 自检
# ---------------------------------------------------------------------------
def selfcheck(root, rows, collages, tsv, unit_n, grp_n):
    import glob as G
    ok = True

    def chk(c, m):
        nonlocal ok
        print(('  PASS  ' if c else '  FAIL  ') + m)
        if not c:
            ok = False

    print('---- 1. 指纹表自洽 ----')
    lines = [l for l in open(tsv, encoding='utf-8').read().splitlines() if l.strip()]
    chk(len(lines) == len(rows) + 1, 'TSV 行数(%d) == 目录数(%d)+表头' % (len(lines), len(rows)))
    e = sum(1 for r in rows if r['equal'])
    ne = sum(1 for r in rows if r['ShapeCount'] is not None and not r['equal'])
    fl = sum(1 for r in rows if r['ShapeCount'] is None)
    chk(e + ne + fl == len(rows), '✅%d + ❌%d + 失败%d == 目录总数%d' % (e, ne, fl, len(rows)))
    chk(e == unit_n, '帧段表已推导目录数(%d) == 指纹 ✅ 数(%d)' % (unit_n, e))

    print('---- 2. .sc 落盘与内容 ----')
    miss = [r['name'] for r in rows if not r['sc_exists']]
    chk(not miss, '.sc 持久副本齐全（缺=%s）' % (miss or '无'))
    for r in rows:
        chk(r['sc_exists'], '%s_v215.sc 存在（%d 字节）' % (r['name'], r['bytes']))

    print('---- 3. 文档 ----')
    tp = os.path.join(root, '策划', '单位帧段表.md')
    gp = os.path.join(root, '策划', '单位动画分组表.md')
    chk(os.path.isfile(tp), '单位帧段表.md 存在')
    chk(os.path.isfile(gp), '单位动画分组表.md 存在')
    doc = open(tp, encoding='utf-8').read()
    gdoc = open(gp, encoding='utf-8').read()
    chk('## 3. 可直接填 `UnitView.AnimRanges` 的四元组' in doc, '帧段表有四元组一节')
    chk('附录 B：v1.0.0 旧结论' in doc, '帧段表保留 v1.0.0 旧结论（附录 B）')
    chk('以 2.1.5 为准' in doc or '以本文 §2/§3 的 2.1.5 数字为准' in doc, '帧段表注明「以 2.1.5 为准」替换旧结论')
    nd = 0
    for r in rows:
        if r.get('equal'):
            nd += 1
            chk('### `%s`' % r['dir'] in doc, '帧段表含目录 %s' % r['dir'])
            chk('## `%s`' % r['name'] in gdoc, '分组表含目录 %s' % r['name'])
        else:
            chk('`%s`' % r['dir'] in doc, '帧段表列出未推导目录 %s' % r['dir'])
    chk(nd == unit_n, '文档内目录节数(%d) == 已推导(%d)' % (nd, unit_n))

    print('---- 4. 拼图与新鲜度 ----')
    script_mt = os.path.getmtime(__file__)
    for c in collages:
        chk(os.path.isfile(c), '拼图存在: %s' % os.path.basename(c))
    units_collaged = sorted({os.path.basename(c).split('-')[1] for c in collages})
    chk(len(units_collaged) >= 12, '出拼图的目录数 >= 12（实际 %d: %s）' % (len(units_collaged), units_collaged))
    old = [c for c in collages if os.path.getmtime(c) < script_mt]
    chk(not old, '拼图比脚本新（过期=%s）' % [os.path.basename(x) for x in old])
    jp = os.path.join(root, '.ai-tmp', 'test', 'AS1-anim-index.json')
    chk(os.path.isfile(jp), 'JSON 产物存在')
    chk(os.path.getmtime(jp) >= script_mt, 'JSON 比脚本新')

    print('---- 5. 临时文件/硬规则 ----')
    hits = []
    for d in (root, os.path.join(root, 'tools'), os.path.join(root, 'client', '_dev'),
              os.path.join(root, 'Assets'), os.path.join(root, '原版资源', 'sc')):
        for pat in ('AS1-*.tsv', 'names-*.txt', 'dl-AS1.ps1', '*.tmp'):
            hits += [p for p in G.glob(os.path.join(d, pat))]
    chk(not hits, '项目根/tools/client/_dev/Assets/sc 下无临时产物（命中=%s）' % hits)
    chk(os.path.isfile(os.path.join(root, 'tools', 'probes', 'sc-as1-index.py')), '判据资产落在 tools/probes/')
    try:
        out = subprocess.run(['git', 'status', '--porcelain', '--', 'client'],
                             cwd=root, capture_output=True, text=True, timeout=60)
        chk(out.stdout.strip() == '', 'client/** 零改动（git status 空）')
    except Exception as ex:
        chk(False, 'client/** git status 检查失败: %s' % ex)

    print('---- 结论: %s ----' % ('全部 PASS' if ok else '存在 FAIL'))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--all', action='store_true', help='跑全量：指纹 + 分组 + 拼图 + 文档')
    ap.add_argument('--selfcheck', action='store_true')
    ap.add_argument('--viewed', default='.ai-tmp/test/AS1-viewed.json')
    ap.add_argument('--verbose', action='store_true')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    R = load_parser(root)

    rows = collect(root)

    def log(s):
        if a.verbose:
            print(s)
    print('目录数 = %d' % len(rows))
    derived, failed = analyse_rows(R, rows, log)

    viewed = {}
    vp = os.path.join(root, a.viewed)
    if os.path.isfile(vp):
        viewed = json.load(open(vp, encoding='utf-8'))

    collages = []
    units_col = []
    for r in derived:
        r['viewed'] = viewed.get(r['name'], {})
        build_unit(R, r, viewed)
        cl = make_collages(R, r, os.path.join(root, '.ai-tmp', 'screenshots'), viewed)
        r['collages'] = cl
        collages += cl
        if cl:
            units_col.append(r['name'])

    # 备份旧帧段表（只备份一次，供「附录 B」引用）
    oldp = os.path.join(root, '.ai-tmp', 'test', 'AS1-old-frame-seg-backup.md')
    tp = os.path.join(root, '策划', '单位帧段表.md')
    if not os.path.isfile(oldp) and os.path.isfile(tp):
        open(oldp, 'w', encoding='utf-8').write(open(tp, encoding='utf-8').read())
        print('旧帧段表已备份 -> %s' % oldp)

    tsv = write_fingerprint_tsv(os.path.join(root, '.ai-tmp', 'test', 'AS1-指纹核对.tsv'), rows, set())
    unit_n = write_frame_segments(root, rows, collages, viewed)
    grp_n = write_group_table(root, rows)

    # JSON 产物（数值证据）
    jp = os.path.join(root, '.ai-tmp', 'test', 'AS1-anim-index.json')
    slim = {}
    for r in derived:
        slim[r['name']] = dict(dir=r['dir'], sc=os.path.relpath(r['sc'], root).replace('\\', '/'),
                               ShapeCount=r['ShapeCount'], png_n=r['png_n'],
                               ExportCount=r.get('ExportCount'), TotalsAnim=r.get('TotalsAnim'),
                               unknown=r.get('unknown'), groups=r['groups'],
                               tiers=r['tiers'], unmapped=r['unmapped'],
                               mapping=[{k: v for k, v in m.items()} for m in r['mapping']])
    os.makedirs(os.path.dirname(jp), exist_ok=True)
    json.dump(slim, open(jp, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('指纹 TSV -> %s' % tsv)
    print('帧段表   -> 策划/单位帧段表.md（已推导 %d 目录）' % unit_n)
    print('分组表   -> 策划/单位动画分组表.md（已推导 %d 目录）' % grp_n)
    print('JSON     -> %s' % jp)
    print('拼图     -> %d 张，覆盖 %d 个目录' % (len(collages), len(units_col)))

    if a.selfcheck:
        return selfcheck(root, rows, collages, tsv, unit_n, grp_n)
    return 0


if __name__ == '__main__':
    sys.exit(main())
