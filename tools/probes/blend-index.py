#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
blend-index.py -- 判据资产：「我们实际渲染的帧 → blend_mode」表 + 生成物。
================================================================================

出处（权威，逐条可查）
---------------------
* `0c` = TAG_MOVIE_CLIP_3。字段序出处 = `scwmake/SupercellFlash`（本工程本地副本
  `.ai-tmp/SupercellFlash/`，子模块名 `plugin/ThirdParty/SC`）
  `supercell-flash/source/flash/display_object/MovieClip.cpp::MovieClip::load()`：
      u16 id | u8 frame_rate | u16 frame_count | i32 frame_elements_count
      | ── frame_elements_count × (u16 instance_index, u16 matrix_index, u16 colorTransform_index)
      | u16 instance_count | ── instance_count × u16 child_id
      |                     | ── instance_count × u8  blend_mode     （仅 TAG_MOVIE_CLIP_3/5/6）
      |                     | ── instance_count × string name
      | 之后是帧块序列 (u8 tag, i32 len, payload)* 直到 tag==0；
        `0b` = TAG_MOVIE_CLIP_FRAME_2 = u16 elements_count + string label
        （`flash/display_object/MovieClipFrame.cpp::MovieClipFrame::load()`）
  ⇒ **第 3 组 u8 数组是 blend_mode**，不是透明度（旧口径 `opacity` 是错的）。
* blend_mode 枚举出处 = 同仓库 `flash/display_object/MovieClip.h` 的
  `DisplayObjectInstance::BlendMode`：0 Normal / 2 Layer / 3 Multiply / 4 Screen / 5 Lighten /
  6 Darken / 7 Difference / 8 Add / 9 Subtract / 10 Invert / 11 Alpha / 12 Erase / 13 Overlay /
  14 HardLight。**本语料只出现 0 / 3 / 4 / 8**（见 `--report`）。
* `frame_NNN` 的 `NNN` = 该 `.sc` 里 **shape(0x12) 记录的文件内序号**（互证：AT1 结论，
  `tools/probes/sc-at1-resolve.py` 头部 H1/H3）。
* 「资源目录 → 源 `*_out` 目录」不靠命名猜：**逐 PNG 内容 md5 匹配**（落地脚本是原字节复制）。
* 「资源目录里的哪一帧真的会被渲染」= `View/UnitView.cs`（`ResPaths.UnitDir/BuildingDir`）与
  `View/EffectsView.cs`（`ResPaths.EffectDir`）两条路径；本脚本只覆盖它们。

口径（为什么这样取）
--------------------
一个 clip 的一帧里可以同时有若干实例：`frame_elements` 按帧块的 `elements_count` 切段，
段内每条 `instance_index` 指向 `childrens[]`，其 `blend_mode` 就是**那个实例**的混合。
渲染端一次只画**一个 shape 的像素帧**（`SpriteRenderer.sprite`），所以表的粒度 = 「shape 序号」。
逐实例递归展开（实例可能是嵌套 clip）时若父实例带非 Normal 混合，则其内部 shape 继承该混合。

候选 clip 两道闸门（`--report` 会印口径计数）：
  ① **必须是有名导出** —— 匿名 clip 是内部子动画；资源目录是照有名导出落地的
     （`ResPaths` 的帧区间常量 + `UnitAnimTable` 的 `Source` 串都只出现有名导出）⇒ 不会被当播放入口。
  ② 该 clip 用到的 shape **必须全落在该资源目录的帧集内**（否则它画的帧我们根本没有）；② 为空时退到「有交集」。

**保守口径**：某个 shape 若在候选 clip 之间拿到**互相冲突**的 blend（含与 Normal(0) 冲突）
⇒ 不写入表（运行期保持现状 = Normal），`--report` 里按目录登记冲突 shape 数。表里没有的一律 Normal。

复跑
----
  python tools/probes/blend-index.py --report      # 只印读数（含逐目录命中与冲突）
  python tools/probes/blend-index.py --write       # 生成 `client/Assets/Scripts/View/SpriteBlendTable.cs`
  python tools/probes/blend-index.py --verify      # 重跑生成并与已落盘生成物**逐字节**比对（exit 0/1）
  python tools/probes/blend-index.py --write --out <绝对路径>   # 另存重跑，用于一致性比对
"""
import argparse
import collections
import hashlib
import os
import re
import struct
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
SC_DIR = os.path.join(ROOT, '原版资源', 'sc')
PNG_ROOT = os.path.join(ROOT, '原版资源', 'cr-assets-png', 'assets', 'sc')
RES_ROOT = os.path.join(ROOT, 'client', 'Assets', 'Resources')
DEFAULT_OUT = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'View', 'SpriteBlendTable.cs')

# 本表覆盖的资源根（= 渲染端两条路径：UnitView 的单位/建筑、EffectsView 的特效）
COVERED_ROOTS = ('Sprites/Units', 'Sprites/Buildings', 'Sprites/Effects')

BLEND_NAME = {0: 'Normal', 2: 'Layer', 3: 'Multiply', 4: 'Screen', 5: 'Lighten', 6: 'Darken',
              7: 'Difference', 8: 'Add', 9: 'Subtract', 10: 'Invert', 11: 'Alpha', 12: 'Erase',
              13: 'Overlay', 14: 'HardLight'}


# ─────────────────────────── .sc 解析 ───────────────────────────

def lzma_hack(data):
    """supercell 的 lzma 头少 4 字节 uncompressed-size（在偏移 9 处补 0）。"""
    import lzma
    d = data[0:9] + b'\x00' * 4 + data[9:]
    return lzma.LZMADecompressor().decompress(d)


def load_sc(path):
    """明文（2.1.5 批）直接返回；`SC` 前缀 / 裸 LZMA 头（1.0.0 批）先解压。"""
    raw = open(path, 'rb').read()
    body = raw[10 + int.from_bytes(raw[6:10], 'big'):] if raw[:2] == b'\x53\x43' else raw
    try:
        return lzma_hack(body)
    except Exception:
        return raw


def parse_sc(data):
    """返回 (shapes, clips, exp_ids, exp_names)。

    shapes = [sid]（记录序即下标）；clips = [dict(id, fps, frames, elements, children, blend, names)]
    elements = [(instance_index, matrix_index, color_index)]（已按帧块切好：每帧一段）
    """
    o = 0
    for _ in range(6):
        o += 2
    o += 5
    ec = struct.unpack_from('<H', data, o)[0]
    o += 2
    exp_ids = list(struct.unpack_from('<%dH' % ec, data, o))
    o += 2 * ec
    exp_names = []
    for _ in range(ec):
        n = data[o]; o += 1
        exp_names.append(data[o:o + n].decode('utf-8', 'replace'))
        o += n

    shapes = []
    clips = []
    while len(data) - o > 0:
        tag = data[o:o + 1].hex()
        size = int.from_bytes(data[o + 1:o + 5], 'little')
        pl = data[o + 5:o + 5 + size]
        o += 5 + size
        if tag == '00':
            continue
        if tag == '12':
            shapes.append(struct.unpack_from('<H', pl, 0)[0])
        elif tag == '0c':
            cid = struct.unpack_from('<H', pl, 0)[0]
            fps = pl[2]
            frames = struct.unpack_from('<H', pl, 3)[0]
            n_elem = struct.unpack_from('<i', pl, 5)[0]
            p = 9
            elements = []
            for _ in range(n_elem):
                elements.append(struct.unpack_from('<HHH', pl, p)); p += 6
            icnt = struct.unpack_from('<H', pl, p)[0]; p += 2
            children = list(struct.unpack_from('<%dH' % icnt, pl, p)); p += 2 * icnt
            blend = list(pl[p:p + icnt]); p += icnt
            names = []
            for _ in range(icnt):
                L = pl[p]; p += 1
                if L >= 255:
                    names.append(None)
                else:
                    names.append(pl[p:p + L].decode('utf-8', 'replace')); p += L
            # 帧块序列：0x0b = TAG_MOVIE_CLIP_FRAME_2 = u16 elements_count + string label
            per_frame = []
            blocks_ok = True
            while p < len(pl):
                ftag = pl[p]; flen = struct.unpack_from('<i', pl, p + 1)[0]
                if ftag == 0:
                    break
                if ftag == 0x0b:
                    cnt = struct.unpack_from('<H', pl, p + 5)[0]
                    per_frame.append(cnt)
                p += 5 + flen
            if not per_frame or sum(per_frame) != n_elem:
                blocks_ok = False
                per_frame = [n_elem]
            # 切段
            segs, k = [], 0
            for cnt in per_frame:
                segs.append(elements[k:k + cnt]); k += cnt
            clips.append(dict(id=cid, fps=fps, frames=frames, n_elem=n_elem, segs=segs,
                              children=children, blend=blend, names=names, blocks_ok=blocks_ok))
    return shapes, clips, exp_ids, exp_names


def _walker(shapes, clips, conflicts=None):
    """造一个 walk(oid, inherited, seen) → {shape 记录序: set(blend)}（父实例非 Normal 混合下传）。"""
    sid2idx = {}
    for i, s in enumerate(shapes):
        sid2idx.setdefault(s, i)
    clip_by_id = {c['id']: c for c in clips}
    memo = {}

    def walk(oid, inherited, seen):
        key = (oid, inherited)
        if key in memo:
            return memo[key]
        out = collections.defaultdict(set)
        if oid in sid2idx:
            out[sid2idx[oid]].add(inherited)
        elif oid in clip_by_id and oid not in seen:
            c = clip_by_id[oid]
            for seg in c['segs']:
                for (ii, _mi, _ci) in seg:
                    if ii >= len(c['children']):
                        continue
                    b = c['blend'][ii] if ii < len(c['blend']) else 0
                    if inherited != 0 and b != 0 and b != inherited and conflicts is not None:
                        conflicts.append((c['id'], c['children'][ii], sorted({inherited, b})))
                    eff = inherited if inherited != 0 else b
                    for s, bs in walk(c['children'][ii], eff, seen | {oid}).items():
                        out[s] |= bs
        memo[key] = out
        return out

    return walk


def clip_blend_maps(shapes, clips, name_by_clip):
    """逐 clip 的 {shape 记录序: set(blend)}；返回 {clip_id: dict}。"""
    walk = _walker(shapes, clips)
    out = {}
    for c in clips:
        m = collections.defaultdict(set)
        for seg in c['segs']:
            for (ii, _mi, _ci) in seg:
                if ii >= len(c['children']):
                    continue
                b = c['blend'][ii] if ii < len(c['blend']) else 0
                for s, bs in walk(c['children'][ii], b, {c['id']}).items():
                    m[s] |= bs
        out[c['id']] = dict(m)
    return out


def sc_clip_tables():
    """{sc 文件名: {clip_id: {shape 序号: set(blend)}}}（逐 clip，不做跨 clip 合并）。"""
    out = {}
    for fn in sorted(f for f in os.listdir(SC_DIR) if f.endswith('.sc')):
        data = load_sc(os.path.join(SC_DIR, fn))
        shapes, clips, eids, enames = parse_sc(data)
        name_by_clip = dict(zip(eids, enames))
        out[fn] = dict(shapes=shapes, clips={c['id']: c for c in clips},
                       name_by_clip=name_by_clip,
                       maps=clip_blend_maps(shapes, clips, name_by_clip))
    return out


# ───────────────── 「client 真正用到的导出」= 渲染端表的 Source ─────────────────

def used_clip_ids():
    """从 `UnitAnimTable.cs` 的 `Source` 串里取每个目录真正会播的 clip id。

    `Source` 形如 `princess_attack1_5｜clip 123｜视角 5｜main`（出处：`UnitAnimTable.cs` 生成器
    `tools/probes/d129b-gen-anim-table.py`）。只取**出现在表里**的 clip ⇒ 没出现在表里的 clip
    我们不播，它的混合与本次渲染无关。
    """
    path = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'View', 'UnitAnimTable.cs')
    out = collections.defaultdict(set)
    if not os.path.isfile(path):
        return out
    cur = None
    for line in open(path, encoding='utf-8', errors='replace'):
        m = re.search(r'\["([^"]+)"\]\s*=\s*new Entry', line)
        if m:
            cur = m.group(1)
            continue
        if cur is None or 'Source' not in line:
            continue
        for s in re.findall(r'Source\s*=\s*"([^"]*)"', line):
            mm = re.search(r'clip\s+(\d+)(?:\s*-\s*(\d+))?', s)
            if not mm:
                continue
            a = int(mm.group(1))
            b = int(mm.group(2)) if mm.group(2) else a
            if 0 <= b - a <= 200:
                for cid in range(a, b + 1):
                    out[cur].add(cid)
    return out


# ───────────────────── 资源目录 → 源 out 目录（内容 md5） ─────────────────────

def png_index():
    """{md5: [(out 目录名, sprite 序号)]}，扫 `原版资源/cr-assets-png/assets/sc/*/`。"""
    idx = collections.defaultdict(list)
    if not os.path.isdir(PNG_ROOT):
        return idx
    for d in sorted(x for x in os.listdir(PNG_ROOT) if os.path.isdir(os.path.join(PNG_ROOT, x))):
        dd = os.path.join(PNG_ROOT, d)
        for f in os.listdir(dd):
            if not f.endswith('.png'):
                continue
            parts = f[:-4].rsplit('_', 1)
            if len(parts) != 2 or not parts[1].isdigit():
                continue
            h = hashlib.md5(open(os.path.join(dd, f), 'rb').read()).hexdigest()
            idx[h].append((d, int(parts[1])))
    return idx


def res_dirs():
    """本表覆盖的资源目录：[(resPath, 绝对目录)]，resPath 与 `SpriteBank.LoadDir` 的入参同形。"""
    out = []
    for rel in COVERED_ROOTS:
        base = os.path.join(RES_ROOT, rel.replace('/', os.sep))
        if not os.path.isdir(base):
            continue
        for d in sorted(os.listdir(base)):
            p = os.path.join(base, d)
            if os.path.isdir(p):
                out.append((rel + '/' + d, p))
    return out


def resource_tables():
    """算出 {resPath: {frame 号: blend}} + 诊断 + 候选口径计数。"""
    sc_tbl = sc_clip_tables()
    idx = png_index()
    used = used_clip_ids()
    tables, diag = {}, []
    scope = collections.Counter()
    xcheck = collections.Counter()
    for res_path, abs_dir in res_dirs():
        frames = sorted((int(f[6:-4]), f) for f in os.listdir(abs_dir)
                        if f.startswith('frame_') and f.endswith('.png'))
        fset = set(n for n, _f in frames)
        votes = collections.Counter()
        for n, f in frames:
            h = hashlib.md5(open(os.path.join(abs_dir, f), 'rb').read()).hexdigest()
            cands = idx.get(h, [])
            same = [c for c in cands if c[1] == n]
            for c in (same or cands):
                votes[c[0]] += 1
        src = votes.most_common(1)[0][0] if votes else None
        if src is None:
            diag.append((res_path, 'UNMATCHED', '该目录没有一张 PNG 能按内容匹配到 源 out 目录', 0))
            continue
        sc_cands = [src[:-4] + '_v215.sc', src[:-4] + '.sc']
        sc = next((x for x in sc_cands if os.path.isfile(os.path.join(SC_DIR, x))), None)
        if sc is None:
            diag.append((res_path, 'NO_SC', '源目录 %s 没有对应 .sc' % src, 0))
            continue
        info = sc_tbl[sc]
        # 候选 clip 两道闸门（口径见文件头「口径」）：
        #   ① 必须是**有名导出** —— 匿名 clip 是内部子动画，资源目录是照有名导出落地的，
        #      不会被当播放入口（`ResPaths` 与两份生成表的 Source 串都只出现有名导出）。
        #   ② 形状集必须落在该资源目录的帧集内（否则它用的帧我们根本没有）。
        #   ② 为空时退到「有交集」。
        named = {cid: m for cid, m in info['maps'].items() if info['name_by_clip'].get(cid)}
        cand = {cid for cid, m in named.items() if m and set(m) <= fset}
        kind = 'B(有名导出·形状⊆目录帧集)'
        if not cand:
            cand = {cid for cid, m in named.items() if set(m) & fset}
            kind = 'C(有名导出·形状∩目录帧集)'
        agg = collections.defaultdict(set)
        for cid in cand:
            for s, bs in info['maps'][cid].items():
                if s in fset:
                    agg[s] |= bs
        hits, amb = {}, 0
        for s, bs in agg.items():
            if len(bs) == 1 and next(iter(bs)) != 0:
                hits[s] = next(iter(bs))
            elif len(bs) > 1:
                amb += 1
        # 交叉对账：`UnitAnimTable` 点名的 clip 必须都在候选集里（否则说明候选闸门漏了我们真播的动画）。
        for cid in used.get(res_path.rsplit('/', 1)[-1], ()):
            if cid in info['maps']:
                xcheck['点名 clip 总数'] += 1
                if cid not in cand:
                    xcheck['不在候选集'] += 1
        if not hits:
            diag.append((res_path, 'NONE', '源 %s（%s）候选 clip %d 个 / 无唯一非 Normal 混合（冲突 %d 个 shape）'
                         % (src, sc, len(cand), amb), len(frames)))
            continue
        scope[kind] += 1
        tables[res_path] = dict(sorted(hits.items()))
        diag.append((res_path, 'HIT', '源 %s（%s）｜候选 clip %d 个｜%s｜冲突未写 %d 个 shape'
                     % (src, sc, len(cand), kind, amb), len(frames)))
    return tables, diag, scope, xcheck


# ─────────────────────────── 生成物 ───────────────────────────

HEADER = '''// <auto-generated> 由 tools/probes/blend-index.py 生成 —— 请勿手改；改数据请改生成器的输入。
// 生成链（可复跑，`--verify` 比对逐字节一致）：
//   python tools/probes/blend-index.py --report     # 只印读数
//   python tools/probes/blend-index.py --write      # 重出本文件
//
// 口径：**只登记「我们真的会渲染的帧」**（`ResPaths.UnitDir/BuildingDir` + `ResPaths.EffectDir`
// 三条路径下的资源目录），且只登记 `.sc` 里能唯一确定的非 Normal 混合。
// 表里没有的帧 / 目录 ⇒ 保持现状（默认 `SpriteRenderer`，Normal 混合）。
//
// 出处（逐条可查）：
//  · `0c`(TAG_MOVIE_CLIP_3) 的字段序 → `.ai-tmp/SupercellFlash/supercell-flash/source/flash/
//    display_object/MovieClip.cpp::MovieClip::load()`；第 3 组 u8 数组 = `blend_mode`。
//  · blend_mode 枚举 → 同仓库 `display_object/MovieClip.h` 的 `DisplayObjectInstance::BlendMode`。
//  · 帧块 `0b`(TAG_MOVIE_CLIP_FRAME_2) = `u16 elements_count + string label`
//    → 同仓库 `MovieClipFrame.cpp::MovieClipFrame::load()`。
//  · `frame_NNN` 的 NNN = 该 `.sc` 里 shape(0x12) 记录的**文件内序号**。
//  · 「资源目录 → 源 out 目录」= 逐 PNG 内容 md5 匹配（不是按名字猜）。
//
// 混合值 = 原版 `blend_mode` 原值（0/3/4/8 三个在本语料出现），⛔ 不是本工程自造的枚举。
'''

TAIL = '''
        /// <summary>取某资源目录（与 <see cref="SpriteBank.LoadDir"/> 入参同形的路径）下某一帧的原始 blend_mode。</summary>
        /// <param name="resPath">资源路径，如 <c>Sprites/Units/chr_princess_out</c>。</param>
        /// <param name="frameNo">帧号（`frame_NNN` 的 `NNN` = `.sc` 里 shape 记录序号）。</param>
        /// <returns>原版 blend_mode 原值；<see cref="Normal"/> = 表里没有 / 越界 ⇒ 保持默认渲染。</returns>
        public static int BlendFor(string resPath, int frameNo)
        {
            byte[] row;
            if (resPath == null || frameNo < 0 || !Table.TryGetValue(resPath, out row)) return Normal;
            return frameNo < row.Length ? row[frameNo] : Normal;
        }

        /// <summary>表里登记的**资源目录**数（自检用）。</summary>
        public static int DirCount { get { return Table.Count; } }

        /// <summary>表里登记的**帧**数（自检用）。</summary>
        public static int FrameCount { get { return _frameCount; } }
    }
}
'''


def render_cs(tables):
    lines = [HEADER]
    lines.append('using System.Collections.Generic;\n')
    lines.append('namespace CR.View\n{\n')
    lines.append('    /// <summary>\n')
    lines.append('    /// **原版精灵混合表**：「我们渲染的 (资源目录, 帧号) → 原版 `blend_mode`」。\n')
    lines.append('    /// <para>\n')
    lines.append('    /// 覆盖 = `Sprites/Units/*`（含建筑，<see cref="UnitView"/>）、`Sprites/Effects/*`\n')
    lines.append('    /// （<see cref="EffectsView"/>）。⛔ 未覆盖的目录（塔 / 竞技场 / UI / 卡面）不在表内，\n')
    lines.append('    /// 一律走默认材质。\n')
    lines.append('    /// </para>\n')
    lines.append('    /// <para>\n')
    lines.append('    /// 值的换算（<see cref="SpriteBlendMaterial"/>）：Add(8) = `Blend One One`、\n')
    lines.append('    /// Multiply(3) = `Blend DstColor Zero`、Screen(4) = `Blend OneMinusDstColor One`\n')
    lines.append('    ///（Unity Manual `SL-Blend.html`）。\n')
    lines.append('    /// </para>\n')
    lines.append('    /// </summary>\n')
    lines.append('    public static class SpriteBlendTable\n    {\n')
    lines.append('        /// <summary>原版 `blend_mode` 0 = Normal（= 使用 <c>SpriteRenderer</c> 的默认材质）。</summary>\n')
    lines.append('        public const int Normal = 0;\n')
    for v in (3, 4, 8):
        lines.append('        /// <summary>原版 `blend_mode` %d = %s。</summary>\n' % (v, BLEND_NAME[v]))
        lines.append('        public const int %s = %d;\n' % (BLEND_NAME[v], v))
    lines.append('\n')
    lines.append('        /// <summary>资源路径 → 稠密表（下标 = 帧号，值 = blend_mode，0 = Normal）。⛔ 生成物，不许手改。</summary>\n')
    lines.append('        private static readonly Dictionary<string, byte[]> Table =\n')
    lines.append('            new Dictionary<string, byte[]>\n        {\n')
    total = 0
    for res_path, tbl in sorted(tables.items()):
        maxn = max(tbl)
        row = bytearray(maxn + 1)
        for n, v in tbl.items():
            row[n] = v
        total += len(tbl)
        vals = ','.join(str(b) for b in row)
        lines.append('            { "%s", new byte[] { %s } },\n' % (res_path, vals))
    lines.append('        };\n\n')
    lines.append('        /// <summary>表内帧数（生成期常量）。</summary>\n')
    lines.append('        private const int _frameCount = %d;\n' % total)
    lines.append(TAIL)
    return ''.join(lines)


# ─────────────────────────── main ───────────────────────────

def report(tables, diag, scope, xcheck):
    print('== 覆盖的资源目录 = %d ；其中命中表 = %d' % (len(diag), len(tables)))
    bykind = collections.Counter(k for _p, k, _m, _n in diag)
    print('   逐目录结论: %s' % dict(bykind))
    print('   候选 clip 口径: %s' % dict(scope))
    print('   交叉对账（UnitAnimTable 点名的 clip 是否都在候选集里）: %s' % dict(xcheck))
    for p, k, msg, n in diag:
        if k != 'HIT':
            print('   %-8s %-44s %s（该目录 %d 帧）' % (k, p, msg, n))
    print()
    print('   ---- 命中逐目录 ----')
    for p, k, msg, n in diag:
        if k == 'HIT':
            print('   %-42s %s' % (p, msg))
    print()
    dist = collections.Counter()
    for tbl in tables.values():
        for v in tbl.values():
            dist[v] += 1
    print('== 表内帧数 = %d ；分布 = %s' % (sum(dist.values()),
                                          {BLEND_NAME.get(k, k): v for k, v in sorted(dist.items())}))
    for v in sorted(dist):
        top = [(n, p) for p, t in tables.items() for n, b in t.items() if b == v]
        print('   %-9s %5d 帧' % (BLEND_NAME.get(v, v), dist[v]))
        per = collections.Counter(p for _n, p in top)
        for p, c in per.most_common():
            print('        %-42s %d 帧  %s' % (p, c, [n for n, q in top if q == p][:24]))
    return


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--report', action='store_true')
    ap.add_argument('--write', action='store_true')
    ap.add_argument('--verify', action='store_true',
                    help='重跑生成并与已落盘的生成物逐字节比对（exit 0/1）')
    ap.add_argument('--out', default=DEFAULT_OUT)
    a = ap.parse_args()

    tables, diag, scope, xcheck = resource_tables()
    report(tables, diag, scope, xcheck)
    txt = render_cs(tables)
    outp = a.out if os.path.isabs(a.out) else os.path.join(ROOT, a.out)
    if a.write:
        os.makedirs(os.path.dirname(outp), exist_ok=True)
        with open(outp, 'w', encoding='utf-8', newline='\n') as f:
            f.write(txt)
        print('\n已写 %s（%d 行 / %d 字节 / %d 目录 / %d 帧）'
              % (outp, txt.count('\n') + 1, len(txt.encode('utf-8')),
                 len(tables), sum(len(t) for t in tables.values())))
        return 0
    if a.verify:
        if not os.path.isfile(outp):
            print('  FAIL  生成物不存在：%s' % outp)
            return 1
        old = open(outp, 'rb').read()
        new = txt.encode('utf-8')
        same = old == new
        print('  %s  重跑生成 == 已落盘生成物（%s；旧 %d 字节 / 新 %d 字节）'
              % ('PASS' if same else 'FAIL', outp, len(old), len(new)))
        bad = [(p, n, v) for p, t in tables.items() for n, v in t.items() if v not in (3, 4, 8)]
        print('  %s  表内值域只含 3/4/8（越界 %d 条 %s）' % ('PASS' if not bad else 'FAIL', len(bad), bad[:5]))
        return 0 if (same and not bad) else 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
