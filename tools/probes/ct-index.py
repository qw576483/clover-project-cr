#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ct-index.py -- 判据资产：「我们实际渲染的帧 → 原版颜色变换(`09`) 7 字节」表 + 生成物。
================================================================================

出处（权威，逐条可查）
---------------------
* `09` = 颜色变换记录。字段序 = `add.r, add.g, add.b, alpha, mul.r, mul.g, mul.b`（恒 7 字节），
  应用式 `out_rgb = clamp(src_rgb * mul / 255 + add)`、`out_alpha = clamp(src_alpha * alpha / 255)`，
  **255 = 1.0**。出处 = `sc-workshop/SupercellFlash`（commit 41e894d5a20cc17e47fe32db3106c4c1bec60a3e）
  `supercell-flash/source/flash/transform/ColorTransform.cpp::load()` / `ColorTransform.h`
  （`multiply` 的恒等默认值 = 255、`operator*` 的 4 处 `/255.f`、全文无 `/128`）。
  第一手解析实现 = 本目录 `sc-color-transform.py`（`--selftest` 15 PASS）。
* `ctId` = 该 `.sc` 内 `09` 记录的**文件内序号**；`0xFFFF`(65535) = 无变换。
  引用点 = `0c`(TAG_MOVIE_CLIP_3) 的 `frame_elements` 三元组第 3 个 `u16`（`colorTransform_index`）。
* `frame_NNN` 的 `NNN` = 该 `.sc` 里 shape(0x12) 记录的**文件内序号**（同 `blend-index.py`）。
* 「资源目录 → 源 `*_out` 目录」不按名字猜：**逐 PNG 内容 md5 匹配**。

口径（为什么这样取）
--------------------
* **只登记「顶点的颜色表达不了」的那些帧** —— 本工程逐件染色只走顶点色
  （`SpriteRenderer.color` / uGUI `Image.color`），顶点色只能**乘**、且分量被 clamp 到 [0,1]
  ⇒ `add != 0` 的变换表达不了（`mul` 侧 ≤1 的缩放顶点色能做）。
  故本表只收 `r_add|g_add|b_add != 0` 的帧；纯 `alpha` / 纯 `mul` 的帧**不进表**（继续走顶点色路径）。
* 净变换 = 从根 clip 到该 shape 的 **ct 链按序复合**（复合式见 `compose()`，与逐条应用等价的
  仿射合成：`mul = ma*mb/255`、`add = aa*mb/255 + ab`）。
* 某帧若在**全文件所有 clip**（含匿名）里拿到**互相冲突**的净变换 ⇒ ⛔ 不写入（运行期保持现状），
  `--report` 里按目录登记冲突帧数。

复跑
----
  python tools/probes/ct-index.py --report      # 只印读数（含冲突帧与关注件）
  python tools/probes/ct-index.py --write       # 重出 client/Assets/Scripts/View/SpriteColorTransformTable.cs
  python tools/probes/ct-index.py --verify      # 重跑生成并与已落盘生成物逐字节比对（exit 0/1）
  python tools/probes/ct-index.py --write --out <绝对路径>   # 另存重跑（一致性比对用）
"""
import argparse
import collections
import hashlib
import importlib.util
import os
import re
import struct
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
_spec = importlib.util.spec_from_file_location('bi', os.path.join(HERE, 'blend-index.py'))
bi = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bi)

SC_DIR = bi.SC_DIR
PNG_ROOT = bi.PNG_ROOT
RES_ROOT = bi.RES_ROOT
DEFAULT_OUT = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'View', 'SpriteColorTransformTable.cs')

# 覆盖根：`Sprites/Ui/<用途>/<源图集>` 会递归落到 `Sprites/Ui/Panels/ui_out` 这一层。
COVERED_ROOTS = ('Sprites/Units', 'Sprites/Buildings', 'Sprites/Effects', 'Sprites/Ui')

NONE = 0xFFFF
IDENTITY = (0, 0, 0, 255, 255, 255, 255)
FIELDS = ('r_add', 'g_add', 'b_add', 'alpha', 'r_mul', 'g_mul', 'b_mul')


def hexof(ct):
    return ''.join('%02x' % b for b in ct)


def has_add(ct):
    return ct is not None and (ct[0] or ct[1] or ct[2])


def decode(pl):
    return struct.unpack('<7B', pl)


def compose(chain):
    """ct 链（从根到叶）→ 净 7 字节。空链 = 恒等。"""
    mul = [255, 255, 255]
    add = [0, 0, 0]
    alpha = 255
    for ct in chain:
        ra, ga, ba, al, rm, gm, bm = ct
        # 先叠 add（父的 add 也吃子的 mul），再叠 mul
        add = [add[0] * rm / 255.0 + ra, add[1] * gm / 255.0 + ga, add[2] * bm / 255.0 + ba]
        mul = [mul[0] * rm / 255.0, mul[1] * gm / 255.0, mul[2] * bm / 255.0]
        alpha = alpha * al / 255.0
    cl = lambda v: max(0, min(255, int(round(v))))
    return (cl(add[0]), cl(add[1]), cl(add[2]), cl(alpha), cl(mul[0]), cl(mul[1]), cl(mul[2]))


def scan09(data):
    """按文件序取出 `09` payload（7 字节）= colorTransform_index 的取值序。"""
    out = []
    o = 0
    for _ in range(6):
        o += 2
    o += 5
    ec = struct.unpack_from('<H', data, o)[0]
    o += 2 + 2 * ec
    for _ in range(ec):
        n = data[o]
        o += 1 + n
    while len(data) - o > 0:
        tag = data[o:o + 1].hex()
        size = int.from_bytes(data[o + 1:o + 5], 'little')
        pl = data[o + 5:o + 5 + size]
        o += 5 + size
        if tag == '09':
            out.append(decode(pl) if size == 7 else None)
    return out


class Sc:
    def __init__(self, path):
        self.path = path
        self.data = bi.load_sc(path)
        self.shapes, self.clips, self.eids, self.enames = bi.parse_sc(self.data)
        self.cts = scan09(self.data)
        self.sid2idx = {}
        for i, s in enumerate(self.shapes):
            self.sid2idx.setdefault(s, i)
        self.clip_by_id = {c['id']: c for c in self.clips}
        self.name_by_clip = dict(zip(self.eids, self.enames))

    def ct_of(self, ci):
        if ci == NONE or ci >= len(self.cts):
            return None
        return self.cts[ci]

    def shape_netcts(self):
        """{shape 记录序: set(净 7 字节)}（从**每个** clip 为根递归展开，含匿名）。"""
        res = collections.defaultdict(set)
        memo = {}

        def contrib(oid, chain, seen):
            key = (oid, chain)
            hit = memo.get(key)
            if hit is not None:
                return hit
            out = set()
            if oid in self.sid2idx:
                out.add((self.sid2idx[oid], compose(chain)))
            else:
                c = self.clip_by_id.get(oid)
                if c is not None and oid not in seen:
                    for seg in c['segs']:
                        for (ii, _mi, ci) in seg:
                            if ii >= len(c['children']):
                                continue
                            ct = self.ct_of(ci)
                            out |= contrib(c['children'][ii], chain + tuple([ct] if ct else []), seen | {oid})
            memo[key] = out
            return out

        for cid, c in self.clip_by_id.items():
            for seg in c['segs']:
                for (ii, _mi, ci) in seg:
                    if ii >= len(c['children']):
                        continue
                    ct = self.ct_of(ci)
                    for s, v in contrib(c['children'][ii], tuple([ct] if ct else []), {cid}):
                        res[s].add(v)
        return res


def res_dirs():
    """[(resPath, 绝对目录)]：覆盖根下**含 frame_*.png 的目录**。"""
    out = []
    for rel in COVERED_ROOTS:
        base = os.path.join(RES_ROOT, rel.replace('/', os.sep))
        if not os.path.isdir(base):
            continue
        for dirpath, _dirs, files in os.walk(base):
            if not any(f.startswith('frame_') and f.endswith('.png') for f in files):
                continue
            rp = os.path.relpath(dirpath, RES_ROOT).replace(os.sep, '/')
            out.append((rp, dirpath))
    return sorted(out)


_sc_cache = {}


def sc_for_dir(idx, abs_dir, frames):
    """按 PNG 内容 md5 定该目录的源 `_out` 目录 → 对应 `.sc`。"""
    votes = collections.Counter()
    for n, f in frames:
        h = hashlib.md5(open(os.path.join(abs_dir, f), 'rb').read()).hexdigest()
        cands = idx.get(h, [])
        same = [c for c in cands if c[1] == n]
        for c in (same or cands):
            votes[c[0]] += 1
    src = votes.most_common(1)[0][0] if votes else None
    if src is None:
        return None, None
    base = src[:-4] if src.endswith('_out') else src
    for suf in ('_v215.sc', '.sc'):
        p = os.path.join(SC_DIR, base + suf)
        if os.path.isfile(p):
            if p not in _sc_cache:
                _sc_cache[p] = Sc(p)
            return src, _sc_cache[p]
    return src, None


# 生成期关注的件（语义名 → 我们落地的资源目录 + 帧号）。
WATCH = (
    ('计时板外框 193（我方 ct 115 / 敌方 ct 312）', 'Sprites/Ui/Panels/ui_out', 193),
    ('圣水预览 ghost 157', 'Sprites/Ui/Bars/ui_out', 157),
    ('条流光 bar_flow 153', 'Sprites/Ui/Bars/ui_out', 153),
    ('法术范围圈 我方 221', 'Sprites/Effects/RangeRing', 221),
    ('法术范围圈 敌方 294', 'Sprites/Effects/RangeRing', 294),
)

# 语义件级（原版**同帧两用**、帧级定不了 ⇒ 由调用方按自己的上下文选）：
#   (C# 名, .sc 文件名, shape 记录序, ctId)
# ctId 是该 `.sc` 内 `09` 记录的文件内序号；生成器会断言「该 ctId 确实被引用于放置该 shape 的实例」。
NAMED = (
    ('RangeRingFriendly', 'effects_v215.sc', 221, 3355),   # 原版 `spell_*_radius_blue` 一族的蓝环
    ('RangeRingHostile', 'effects_v215.sc', 221, 3334),    # 原版 `spell_*_radius_red` 一族的红环
    ('TimerPlateOurs', 'ui_v215.sc', 193, 115),            # 原版 `HUD_topRight` 板框：把纯黑件加成白
    ('TimerPlateEnemy', 'ui_v215.sc', 193, 312),           # 同帧的另一变体：加成 (200,0,30)
)


def build():
    idx = bi.png_index()
    tables, diag, conflicts, watch_detail = {}, [], [], {}
    for res_path, abs_dir in res_dirs():
        frames = sorted((int(f[6:-4]), f) for f in os.listdir(abs_dir)
                        if f.startswith('frame_') and f.endswith('.png'))
        fset = {n for n, _f in frames}
        src, sc = sc_for_dir(idx, abs_dir, frames)
        if src is None:
            diag.append((res_path, 'UNMATCHED', '该目录没有一张 PNG 能按内容匹配到 源 out 目录'))
            continue
        if sc is None:
            diag.append((res_path, 'NO_SC', '源目录 %s 没有对应 .sc' % src))
            continue
        net = sc.shape_netcts()
        for label, wpath, wframe in WATCH:
            if wpath == res_path:
                watch_detail[(res_path, wframe)] = sorted(hexof(x) for x in net.get(wframe, ()))
        hits = {}
        for n in sorted(fset):
            s = net.get(n)
            if not s:
                continue
            if len(s) > 1:
                conflicts.append((res_path, n, sorted(hexof(x) for x in s)))
                continue
            ct = next(iter(s))
            if has_add(ct):
                hits[n] = ct
        if hits:
            tables[res_path] = dict(sorted(hits.items()))
        else:
            diag.append((res_path, 'NONE', '源 %s（%s）无 ADD 型唯一净变换' % (src, os.path.basename(sc.path))))
    named = {}
    for cn, scname, frame, ctid in NAMED:
        sc = _sc_cache.get(os.path.join(SC_DIR, scname))
        if sc is None:
            sc = Sc(os.path.join(SC_DIR, scname))
            _sc_cache[sc.path] = sc
        ct = sc.ct_of(ctid)
        ok = bool(ct) and any(x == frame for x in _refs_of(sc, frame, ctid))
        named[cn] = dict(sc=scname, frame=frame, ctid=ctid, ct=ct, ok=ok)
    return tables, diag, conflicts, watch_detail, named


def _refs_of(sc, frame, ctid):
    """列出「放置 shape `frame` 且链上含 ctId `ctid`」的全部实例的 shape 记录序。"""
    out = []

    def walk(oid, has, seen):
        if oid in sc.sid2idx:
            if sc.sid2idx[oid] == frame and has:
                out.append(frame)
            return
        c = sc.clip_by_id.get(oid)
        if c is None or oid in seen:
            return
        for seg in c['segs']:
            for (ii, _mi, ci) in seg:
                if ii >= len(c['children']):
                    continue
                walk(c['children'][ii], has or ci == ctid, seen | {oid})

    for cid in sc.clip_by_id:
        walk(cid, False, set())
    return out


HEADER = '''// <auto-generated> 由 tools/probes/ct-index.py 生成 —— 请勿手改；改数据请改生成器的输入。
// 生成链（可复跑，`--verify` 比对逐字节一致）：
//   python tools/probes/ct-index.py --report     # 只印读数
//   python tools/probes/ct-index.py --write      # 重出本文件
//
// 口径：**只登记「顶点色表达不了」的帧** —— 逐件染色走顶点色（`SpriteRenderer.color` / uGUI
// `Image.color`），顶点色只能乘、分量 clamp 到 [0,1] ⇒ `add != 0` 的变换进本表；
// 纯 alpha / 纯 mul 的帧不进表（继续走顶点色路径）。表里没有的帧 ⇒ 材质不参与。
//
// 出处（逐条可查）：
//  · `09` 恒 7 字节，字段序 `add.r,add.g,add.b, alpha, mul.r,mul.g,mul.b`；
//    `out_rgb = clamp(src*mul/255 + add)`、`out_alpha = clamp(src_alpha*alpha/255)`，**255 = 1.0**
//    → `sc-workshop/SupercellFlash` 的 `flash/transform/ColorTransform.cpp::load()`（无 `/128`）。
//  · `ctId` = 该 `.sc` 内 `09` 记录的文件内序号；引用点 = `0c`(TAG_MOVIE_CLIP_3) 的
//    `frame_elements` 第 3 个 `u16`（`colorTransform_index`），`65535` = 无。
//  · `frame_NNN` 的 NNN = 该 `.sc` 里 shape(0x12) 记录的文件内序号。
//  · 净变换 = 根 clip → 该 shape 的 ct 链按序复合（等价仿射合成）。
//  · 「资源目录 → 源 out 目录」= 逐 PNG 内容 md5 匹配（不是按名字猜）。
//  · 同一帧在全文件所有 clip 间拿到冲突净变换 ⇒ ⛔ 不写入（运行期保持现状）。
'''

TAIL = '''
        /// <summary>表内登记的**资源目录**数（自检用）。</summary>
        public static int DirCount { get { return Table.Count; } }

        /// <summary>表内登记的**帧**数（自检用）。</summary>
        public static int FrameCount { get { return _frameCount; } }
    }
}
'''


def render_cs(tables, named):
    total = sum(len(v) for v in tables.values())
    L = [HEADER]
    L.append('using System.Collections.Generic;\n')
    L.append('namespace CR.View\n{\n')
    L.append('    /// <summary>\n')
    L.append('    /// **原版颜色变换表**：「我们渲染的 (资源目录, 帧号) → 原版 `09` 的 7 字节」。\n')
    L.append('    /// <para>\n')
    L.append('    /// 只收 `add != 0` 的帧（顶点色只能乘 ⇒ 加色项表达不了）；纯 alpha / 纯 mul 的帧\n')
    L.append('    /// 继续走顶点色路径。取用方 = <see cref="SpriteBlendMaterial.For"/>（与 blend 合并成一份共享材质）。\n')
    L.append('    /// </para>\n')
    L.append('    /// <para>\n')
    L.append('    /// 值 = 原版 `09` 原字节（<c>add.r,add.g,add.b, alpha, mul.r,mul.g,mul.b</c>），\n')
    L.append('    /// 由其按序应用：<c>rgb = clamp(rgb*mul/255 + add)</c>、<c>a = a*alpha/255</c>。⛔ 不是本工程自造的枚举。\n')
    L.append('    /// </para>\n')
    L.append('    /// </summary>\n')
    L.append('    public static class SpriteColorTransformTable\n    {\n')
    L.append('        /// <summary>资源路径 → (帧号 → 7 字节颜色变换)。⛔ 生成物，不许手改。</summary>\n')
    L.append('        private static readonly Dictionary<string, Dictionary<int, byte[]>> Table =\n')
    L.append('            new Dictionary<string, Dictionary<int, byte[]>>\n        {\n')
    for res_path, tbl in sorted(tables.items()):
        L.append('            { "%s", new Dictionary<int, byte[]>\n            {\n' % res_path)
        for n, ct in sorted(tbl.items()):
            L.append('                { %d, new byte[] { %s } },\n' % (n, ', '.join(str(b) for b in ct)))
        L.append('            } },\n')
    L.append('        };\n')
    L.append('\n        /// <summary>表内帧数（生成期常量）。</summary>\n')
    L.append('        private const int _frameCount = %d;\n' % total)
    L.append('''
        /// <summary>取某资源目录下某一帧的原版颜色变换。</summary>
        /// <param name="resPath">资源路径，如 <c>Sprites/Ui/Panels/ui_out</c>。</param>
        /// <param name="frameNo">帧号（`frame_NNN` 的 `NNN` = `.sc` 里 shape 记录序号）。</param>
        /// <returns>7 字节（<c>add.r,add.g,add.b, alpha, mul.r,mul.g,mul.b</c>）；表里没有 / 越界 ⇒ <c>null</c>（不参与）。</returns>
        public static byte[] CtFor(string resPath, int frameNo)
        {
            Dictionary<int, byte[]> row;
            byte[] ct;
            if (resPath == null || !Table.TryGetValue(resPath, out row)) return null;
            return row.TryGetValue(frameNo, out ct) ? ct : null;
        }
''')
    L.append('\n        // ── 语义件级：原版**同帧两用**（同一 shape 在不同有名导出下带不同变换）⇒ 帧级定不了，\n')
    L.append('        //    由调用方按自己的上下文选。每个字段的值 = 该 `.sc` 内那个 `09` 记录的原字节。\n')
    for cn, info in named.items():
        L.append('\n        /// <summary>原版 `%s` 的 `09`(ctId %d)，置于 shape 记录序 %d。</summary>\n'
                 % (info['sc'], info['ctid'], info['frame']))
        L.append('        public static readonly byte[] %s = new byte[] { %s };\n'
                 % (cn, ', '.join(str(b) for b in info['ct'])))
    L.append(TAIL)
    return ''.join(L)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--report', action='store_true')
    ap.add_argument('--write', action='store_true')
    ap.add_argument('--verify', action='store_true')
    ap.add_argument('--out', default=None)
    args = ap.parse_args()

    tables, diag, conflicts, watch_detail, named = build()
    total = sum(len(v) for v in tables.values())
    bad = [cn for cn, i in named.items() if not i['ok']]

    if args.report:
        print('覆盖根 = %s' % (', '.join(COVERED_ROOTS),))
        print('命中目录 %d / 帧 %d' % (len(tables), total))
        for res_path, tbl in sorted(tables.items()):
            print('  %-46s %2d 帧 %s' % (res_path, len(tbl),
                                         ', '.join('%d:%s' % (n, hexof(c)) for n, c in sorted(tbl.items())[:4])))
        print('冲突帧（未写）%d：' % len(conflicts))
        for res_path, n, cts in conflicts[:40]:
            print('  %-46s f%-4d %s' % (res_path, n, ' vs '.join(cts)))
        print('关注件（team-lead 清单）：')
        for label, res_path, n in WATCH:
            ct = tables.get(res_path, {}).get(n)
            det = watch_detail.get((res_path, n))
            print('  %-30s %-32s f%-4d 表内=%s' % (
                label, res_path, n, hexof(ct) if ct else '—'))
            if det:
                print('      该帧在全 .sc 的净变换集合（%d 个，含冲突）: %s' % (len(det), ', '.join(det[:6])))
        print('语义件级（全 .sc 同帧两用 ⇒ 由调用方选；生成器已断言「该 ctId 确实引用于该 shape」）：')
        for cn, i in named.items():
            print('  %-20s %-18s shape f%-4d ctId %-6d %s  %s' % (
                cn, i['sc'], i['frame'], i['ctid'], hexof(i['ct']),
                'OK' if i['ok'] else '!! 未在 .sc 里被引用于该 shape'))
        print('未命中目录 %d 个（前 10）：' % len(diag))
        for res_path, kind, msg in diag[:10]:
            print('  %-46s %-9s %s' % (res_path, kind, msg))
        return 0 if not bad else 1

    text = render_cs(tables, named)
    out = args.out or DEFAULT_OUT
    if args.verify:
        if not os.path.isfile(out):
            print('FAIL 生成物不存在：%s' % out)
            return 1
        old = open(out, 'rb').read()
        new = text.encode('utf-8')
        if old == new:
            print('PASS 重跑生成 == 已落盘生成物（%d 字节 / SHA256 %s）'
                  % (len(new), hashlib.sha256(new).hexdigest().upper()))
            return 0
        print('FAIL 重跑生成 != 已落盘生成物（old %d 字节 / new %d 字节）' % (len(old), len(new)))
        return 1
    if args.write:
        with open(out, 'w', encoding='utf-8', newline='\n') as f:
            f.write(text)
        print('已写 %s（%d 目录 / %d 帧 / %d 字节）' % (out, len(tables), total, len(text.encode('utf-8'))))
        return 0
    ap.print_help()
    return 2


if __name__ == '__main__':
    sys.exit(main())
