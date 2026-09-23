#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
coverage-enumerate.py -- 全量覆盖自查 · 实体清单 + 状态矩阵 枚举器

依据：`<ai-skill>/patterns/full-coverage-audit.md`（维度定义 / 判据 / 闸门）
     `<ai-skill>/scaffold/coverage-matrix.md`（列定义 / 枚举脚本契约）

本片范围（AE1）：**D1 资源/资产 · D2 几何与场景 · D3 材质与贴图表现 · S1 数值与配表**
其余维度（D4..D12 / S2 / S3）由后续片补，本脚本**不虚构**它们的行。

⛔ 本脚本只读盘上实体并产出 TSV —— 不改任何业务代码。
⛔ 行数是数出来的：所有实体均由脚本枚举（目录遍历 / 正则 / JSON 读），不手写、不凭记忆。
⛔ 引用完整性复用既有探针 `tools/probes/check-ui-keys.ps1`（UI 键↔文件四向检查），不重写。

输出（稳定排序，同一份盘 => 同一份输出，可 diff）：
  <root>/策划/实体清单.tsv       列：维度|实体|载体/路径|出处|状态数|判据类型|归属片
  <root>/策划/状态矩阵.tsv       列：维度|实体|状态/事件|边界值|期望表现(出处)|实测|结论|证据
  <root>/策划/差异登记.tsv       列：编号|是什么|为什么|出处|何时消除        （只登记"允许的差异"）
  <root>/.ai-tmp/test/AE1-enum-summary.txt   计数汇总（给人看的）
  <root>/.ai-tmp/test/AE1-orphans.tsv        文件在盘上但没人引用（典型漏检）

用法：python tools/probes/coverage-enumerate.py [--no-ps]
      --no-ps  跳过 check-ui-keys.ps1（离线/无 PowerShell 时的降级，块内标 阻塞）
"""

import argparse
import io
import json
import os
import re
import subprocess
import sys
import zlib

# ---------------------------------------------------------------- 路径

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))

PLAN = os.path.join(ROOT, '\u7b56\u5212')                     # 策划
SPEC_DIR = os.path.join(PLAN, '\u7b56\u5212\u6848')           # 策划案
SPEC = os.path.join(SPEC_DIR, '\u7687\u5ba4\u6218\u4e89\u53c2\u8003\u89c4\u683c.md')
NUMDIR = os.path.join(PLAN, '\u6570\u503c\u6587\u6863')       # 数值文档
ENTITY_TSV = os.path.join(PLAN, '\u5b9e\u4f53\u6e05\u5355.tsv')     # 实体清单
MATRIX_TSV = os.path.join(PLAN, '\u72b6\u6001\u77e9\u9635.tsv')     # 状态矩阵
DIFF_TSV = os.path.join(PLAN, '\u5dee\u5f02\u767b\u8bb0.tsv')       # 差异登记

TMP = os.path.join(ROOT, '.ai-tmp', 'test')
SUMMARY = os.path.join(TMP, 'AE1-enum-summary.txt')
ORPHANS = os.path.join(TMP, 'AE1-orphans.tsv')

CLIENT = os.path.join(ROOT, 'client')
RES = os.path.join(CLIENT, 'Assets', 'Resources')
SCRIPTS = os.path.join(CLIENT, 'Assets', 'Scripts')
EDITOR = os.path.join(CLIENT, 'Assets', 'Editor')
SOURCE = os.path.join(ROOT, '\u539f\u7248\u8d44\u6e90')      # 原版资源
SC_SRC = os.path.join(SOURCE, 'cr-assets-png', 'assets', 'sc')
API_JSON = os.path.join(SOURCE, 'cr-api-data', 'docs', 'json')

RESPATHS = os.path.join(SCRIPTS, 'Core', 'ResPaths.cs')
GAMECONST = os.path.join(SCRIPTS, 'Core', 'GameConst.cs')
ARENAVIEW = os.path.join(SCRIPTS, 'View', 'ArenaView.cs')
CHECKUI = os.path.join(HERE, 'check-ui-keys.ps1')

# 维度取值（scaffold 定死）
DIMS = ['D1\u8d44\u6e90', 'D2\u51e0\u4f55', 'D3\u6750\u8d28', 'D4UI', 'D5\u52a8\u753b',
        'D6\u7279\u6548', 'D7\u97f3\u4e50', 'D8\u97f3\u6548', 'D9\u78b0\u649e',
        'D10\u903b\u8f91', 'D11\u8f93\u5165', 'D12\u6d41\u7a0b', 'S1\u6570\u503c',
        'S2\u6027\u80fd', 'S3\u8bbe\u7f6e']

# ---------------------------------------------------------------- 工具

def rd_text(p):
    with io.open(p, 'r', encoding='utf-8', errors='replace') as f:
        return f.read()

def rd_bytes(p):
    with open(p, 'rb') as f:
        return f.read()

def read_tsv(p):
    """返回 (header, rows)；rows 里跳过全部为 '-' 的占位行。"""
    t = rd_text(p)
    lines = [l for l in t.split('\n')]
    if lines and lines[-1] == '':
        lines.pop()
    rows = [l.split('\t') for l in lines]
    if not rows:
        return [], []
    header = rows[0]
    body = [r for r in rows[1:] if not all((c.strip() in ('', '-')) for c in r)]
    return header, body

def read_src_table(p):
    """读《数值文档》源表（xx_cs.txt）：行0=列名，行1=类型，行2=cs，行3=说明，行4+=数据。
    ⛔ 只保留首列为纯数字 id 的数据行（类型/cs/说明行首列不是数字）。"""
    t = rd_text(p)
    lines = [l for l in t.split('\n')]
    while lines and lines[-1].strip() == '':
        lines.pop()
    if not lines:
        return [], []
    header = lines[0].split('\t')
    body = []
    for l in lines[1:]:
        if not l.strip():
            continue
        cells = l.split('\t')
        if cells and re.match(r'^\d+$', cells[0].strip()):
            body.append(cells)
    return header, body

def rel(p):
    return os.path.relpath(p, ROOT).replace('\\', '/')

def walk_files(base, ext=None):
    out = []
    if not os.path.isdir(base):
        return out
    for dp, dn, fn in os.walk(base):
        dn[:] = [d for d in dn if d != '__pycache__']
        for n in fn:
            if ext is None or n.lower().endswith(ext):
                out.append(os.path.join(dp, n))
    return sorted(out)

def walk_dirs(base):
    out = []
    if not os.path.isdir(base):
        return out
    for dp, dn, fn in os.walk(base):
        dn[:] = [d for d in dn if d != '__pycache__']
        for d in dn:
            out.append(os.path.join(dp, d))
    return sorted(out)

_png_cache = {}
def png_stats(p):
    """(w,h,nonalpha_pixels,all_near_white) —— 纯 stdlib PNG 解码（zlib），只支持 8bit RGBA/RGB/灰度。"""
    if p in _png_cache:
        return _png_cache[p]
    val = None
    try:
        b = rd_bytes(p)
        if b[:8] != b'\x89PNG\r\n\x1a\n':
            val = None
        else:
            pos = 8
            w = h = bitd = ctype = None
            idat = b''
            while pos + 8 <= len(b):
                ln = int.from_bytes(b[pos:pos + 4], 'big')
                typ = b[pos + 4:pos + 8]
                data = b[pos + 8:pos + 8 + ln]
                if typ == b'IHDR':
                    w = int.from_bytes(data[0:4], 'big')
                    h = int.from_bytes(data[4:8], 'big')
                    bitd = data[8]
                    ctype = data[9]
                elif typ == b'IDAT':
                    idat += data
                elif typ == b'IEND':
                    break
                pos += 12 + ln
            if w is None or bitd != 8 or ctype not in (0, 2, 4, 6):
                val = (w, h, None, None)
            else:
                raw = zlib.decompress(idat)
                ch = {0: 1, 2: 3, 4: 2, 6: 4}[ctype]
                stride = w * ch
                prev = bytearray(stride)
                nonalpha = 0
                white = True
                off = 0
                for y in range(h):
                    ft = raw[off]; off += 1
                    line = bytearray(raw[off:off + stride]); off += stride
                    if ft == 1:
                        for i in range(ch, stride):
                            line[i] = (line[i] + line[i - ch]) & 0xFF
                    elif ft == 2:
                        for i in range(stride):
                            line[i] = (line[i] + prev[i]) & 0xFF
                    elif ft == 3:
                        for i in range(stride):
                            a = line[i - ch] if i >= ch else 0
                            line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
                    elif ft == 4:
                        for i in range(stride):
                            a = line[i - ch] if i >= ch else 0
                            c = prev[i - ch] if i >= ch else 0
                            bb = prev[i]
                            pa, pb, pc = abs(bb - c), abs(a - c), abs(a + bb - 2 * c)
                            pr = a if (pa <= pb and pa <= pc) else (bb if pb <= pc else c)
                            line[i] = (line[i] + pr) & 0xFF
                    if ctype == 6:
                        for x in range(w):
                            al = line[x * 4 + 3]
                            if al > 8:
                                nonalpha += 1
                                if not (line[x * 4] > 235 and line[x * 4 + 1] > 235 and line[x * 4 + 2] > 235):
                                    white = False
                    elif ctype == 4:
                        for x in range(w):
                            if line[x * 2 + 1] > 8:
                                nonalpha += 1
                                if line[x * 2] < 235:
                                    white = False
                    else:
                        nonalpha = w * h
                        total = sum(line)
                        white = white and (total / stride) > 235
                    prev = line
                val = (w, h, nonalpha, bool(white) if nonalpha > 0 else False)
    except Exception:
        val = None
    _png_cache[p] = val
    return val

class Rows:
    def __init__(self):
        self.ent = []   # 实体清单
        self.mat = []   # 状态矩阵
        self.diff = []  # 差异登记
        self.orphans = []
        self.note = []

    def add_e(self, dim, entity, path, origin, nstate, judge, slice_):
        self.ent.append([dim, entity, path, origin, str(nstate), judge, slice_])

    def add_m(self, dim, entity, state, boundary, expect, actual, verdict, ev):
        self.mat.append([dim, entity, state, boundary, expect, actual, verdict, ev])

R = Rows()

# ---------------------------------------------------------------- 出处常量（唯一标准答案文件）

ORIGIN_SPEC = '\u53c2\u8003\u89c4\u683c.md'
def spec_sec(n):
    return u'%s \u00a7%s' % (ORIGIN_SPEC, n)

# ---------------------------------------------------------------- .cs 语料（引用扫描）

def build_cs_corpus():
    """散文 + 常量表。常量做**迭代展开**（处理 `CardsRoot = SpritesRoot + "/Cards"` 这类拼接）。"""
    files = walk_files(SCRIPTS, '.cs') + walk_files(EDITOR, '.cs')
    blob = '\n'.join(rd_text(f) for f in files)
    raw = {}
    for m in re.finditer(r'const\s+string\s+(\w+)\s*=\s*([^;]+);', blob):
        raw.setdefault(m.group(1), m.group(2).strip())
    vals = {}
    for _ in range(10):
        改 = False
        for n, rhs in raw.items():
            if n in vals:
                continue
            toks = re.findall(r'"[^"]*"|[A-Za-z_]\w*', rhs)
            if toks and all(t.startswith('"') or t in vals for t in toks):
                vals[n] = ''.join(t[1:-1] if t.startswith('"') else vals[t] for t in toks)
                改 = True
        if not 改:
            break
    return blob, vals

CS_BLOB, CS_CONSTS = build_cs_corpus()
# 数据驱动引用：素材目录名可能只出现在《数值文档》配表（sprite_dir 列）而不在 .cs 里
# （实测 chr_goblin_archer_out 仅由 unit_cs.txt 的 SpearGoblin 行引用）⇒ 语料并入配表，
# 避免「代码字面量」单口径把这类目录误判成孤儿（见差异登记 D23）。
if os.path.isdir(NUMDIR):
    for _n in sorted(os.listdir(NUMDIR)):
        if _n.endswith('.txt'):
            CS_BLOB += '\n' + rd_text(os.path.join(NUMDIR, _n))

def referenced_token(name):
    """目录名/常量值是否在 .cs 语料里出现（作为字面量片段）。"""
    return name in CS_BLOB

# ---------------------------------------------------------------- D1 资源/资产

RES_GROUPS = [
    ('Units', os.path.join(RES, 'Sprites', 'Units'), 'dir'),
    ('Buildings', os.path.join(RES, 'Sprites', 'Buildings'), 'dir'),
    ('Cards', os.path.join(RES, 'Sprites', 'Cards'), 'dir'),
    ('Towers', os.path.join(RES, 'Sprites', 'Towers'), 'dir'),
    ('Arenas', os.path.join(RES, 'Sprites', 'Arenas'), 'dir'),
    ('Effects', os.path.join(RES, 'Sprites', 'Effects'), 'dir'),
]

UI_DIR = os.path.join(RES, 'Sprites', 'Ui')
SOUND_DIR = os.path.join(RES, 'Sound')

def d1_resources():
    landed_dirs = {}   # bare dir name -> path
    for grp, base, mode in RES_GROUPS:
        for d in walk_dirs(base):
            files = walk_files(d, '.png')
            if not files:
                continue
            name = os.path.basename(d)
            landed_dirs[name] = d
            ref = referenced_token(name)
            R.add_e('D1\u8d44\u6e90', name, rel(d), spec_sec(6) + ' / ResPaths.cs',
                    len(files), '\u811a\u672c\u65ad\u8a00', 'AE1-D1')
            if not ref:
                R.orphans.append([name, rel(d), str(len(files)),
                                  '\u76ee\u5f55\u540d\u672a\u5728\u4efb\u4f55 .cs \u91cc\u51fa\u73b0',
                                  '\u811a\u672c(cs-corpus)', '\u4e25\u91cd'])
            if name == 'chr_goblin_archer_out':
                R.note.append('chr_goblin_archer_out: \u975e\u5b64\u513f \u2014\u2014 unit_cs.txt \u7684 SpearGoblin \u884c '
                              'sprite_dir \u6570\u636e\u5f15\u7528(\u2192 server/game/table/tsv/unit.tsv)\uff1b'
                              '\u672c\u884c\u4ec5\u4e3a\u300c.cs \u5b57\u9762\u91cf\u300d\u53e3\u5f84\u7684\u5047\u9633\u6027\uff0c\u89c1\u5dee\u5f02\u767b\u8bb0 D23')

    # Effects 子目录另计三组（Hit / Blast / Arrow）已含在 dir 模式里
    # Sound：逐个 ogg
    for f in walk_files(SOUND_DIR, '.ogg'):
        n = os.path.basename(f)[:-4]
        ref = referenced_token(n)
        R.add_e('D1\u8d44\u6e90', n, rel(f), spec_sec(7) + ' S27 / AudioPaths.cs',
                1, '\u811a\u672c\u65ad\u8a00', 'AE1-D1')
        if not ref:
            R.orphans.append([n, rel(f), '1', '\u97f3\u9891\u540d\u672a\u5728\u4efb\u4f55 .cs \u91cc\u51fa\u73b0',
                              '\u811a\u672c(cs-corpus)', '\u4e25\u91cd'])
    return landed_dirs

def d1_ui_per_frame():
    """Sprites/Ui 下逐文件登记（ResPaths 逐帧引用，帧级粒度才有意义）。"""
    files = walk_files(UI_DIR, '.png')
    for f in files:
        relp = rel(f)
        R.add_e('D1\u8d44\u6e90', os.path.basename(f)[:-4], relp,
                'ResPaths.cs (UiFrame) / copy-ui-assets.py', 2,
                '\u811a\u672c\u65ad\u8a00', 'AE1-D1')
    return files

def d1_sources():
    """原版资源/** 的来源登记（顶层来源包，出处=参考规格 §9）。"""
    for d in sorted(os.listdir(SOURCE)) if os.path.isdir(SOURCE) else []:
        p = os.path.join(SOURCE, d)
        if not os.path.isdir(p):
            continue
        n = len(walk_files(p))
        R.add_e('D1\u8d44\u6e90', d, rel(p), spec_sec(9), n,
                '\u53c2\u8003\u7269\u6bd4\u5bf9', 'AE1-D1')

def d1_respaths_keys():
    """ResPaths 键 ↔ 实际文件（复用既有 check-ui-keys.ps1 逻辑的同口径正则 + 自算存在性）。"""
    src = rd_text(RESPATHS)
    dirs = dict(re.findall(r'public const string (Ui\w+Dir) = "([^"]+)";', src))
    srcs = dict(re.findall(r'public const string (UiSrc\w+) = "([^"]+)";', src))
    keys = []
    for m in re.finditer(r'public static string (\w+) \{ get \{ return UiFrame\((\w+), (\w+), (\d+)\); \} \}', src):
        key, d, s, fr = m.group(1), dirs.get(m.group(2)), srcs.get(m.group(3)), int(m.group(4))
        if not d or not s:
            keys.append((key, None, False, m.group(2) + '/' + m.group(3)))
            continue
        relp = 'Sprites/Ui/%s/%s/frame_%03d' % (d, s, fr)
        absp = os.path.join(UI_DIR, d, s, 'frame_%03d.png' % fr)
        keys.append((key, relp, os.path.isfile(absp), None))
    for key, relp, exists, bad in keys:
        origin = 'ResPaths.cs:UiFrame'
        R.add_e('D1\u8d44\u6e90', key, relp or ('<unresolved %s>' % bad), origin, 2,
                '\u811a\u672c\u65ad\u8a00', 'AE1-D1')
        if relp is not None and not exists:
            verdict = '\u4e0d\u4e00\u81f4(\u952e\u6307\u5411\u7684\u6587\u4ef6\u4e0d\u5728\u76d8\u4e0a)'
            ev = 'Test-Path False: ' + relp + '.png'
        elif relp is None:
            verdict = '\u4e0d\u4e00\u81f4(\u57fa\u5ea7\u5e38\u91cf\u65e0\u6cd5\u89e3\u6790)'
            ev = 'unresolved ' + str(bad)
        else:
            verdict = '\u4e00\u81f4'
            ev = 'Test-Path True: ' + relp + '.png'
        R.add_m('D1\u8d44\u6e90', key, '\u952e\u2192\u6587\u4ef6\u5b58\u5728',
                'exists / missing', 'ResPaths \u952e\u5fc5\u987b\u6307\u5411\u76d8\u4e0a\u5b58\u5728\u7684 frame',
                ev, verdict, 'coverage-enumerate.py / check-ui-keys.ps1 A')

    # 根常量（SpritesRoot / UnitsRoot / TowersRoot / ArenaRoot / CardsRoot / UiRoot / BootBackground ...）
    # 递归展开 RHS：把已知 const 名替换成其字面值（处理 `SpritesRoot + "/Towers/" + TowerSpriteDir`）。
    def resolve(rhs, depth=0):
        if depth > 6:
            return None
        out = []
        for tok in re.findall(r'"[^"]*"|[A-Za-z_]\w*', rhs):
            if tok.startswith('"'):
                out.append(tok[1:-1])
            elif tok in CS_CONSTS:
                out.append(CS_CONSTS[tok])
            elif tok in ('true', 'false'):
                out.append(tok)
            else:
                return None
        return ''.join(out)

    root_keys = ['SpritesRoot', 'UnitsRoot', 'BuildingsRoot', 'TowersRoot', 'ArenaRoot',
                 'CardsRoot', 'SpellArtRoot', 'UiRoot', 'BootBackground']
    for m in re.finditer(r'public const string (\w+) = ([^;]+);', src):
        key, rhs = m.group(1), m.group(2).strip()
        if key not in root_keys:
            continue
        path = resolve(rhs)
        if path is None:
            R.note.append('blocked: cannot resolve ResPaths.%s = %s' % (key, rhs))
            R.add_m('D1\u8d44\u6e90', key, '\u8d44\u6e90\u6839\u5b58\u5728', 'exists',
                    'ResPaths \u8d44\u6e90\u6839\u5fc5\u987b\u80fd\u89e3\u6790\u4e14\u5728\u76d8\u4e0a',
                    'unresolved rhs=' + rhs, '\u963b\u585e(\u5e38\u91cf\u7f16\u8f91\u94fe\u65e0\u6cd5\u89e3\u6790)',
                    'coverage-enumerate.py')
            continue
        absp = os.path.join(RES, path.replace('/', os.sep))
        ok = os.path.isdir(absp) or os.path.isfile(absp) or os.path.isfile(absp + '.png')
        R.add_e('D1\u8d44\u6e90', key, 'Assets/Resources/' + path, 'ResPaths.cs', 1,
                '\u811a\u672c\u65ad\u8a00', 'AE1-D1')
        R.add_m('D1\u8d44\u6e90', key, '\u8d44\u6e90\u6839\u5b58\u5728', 'exists',
                'ResPaths \u8d44\u6e90\u6839\u5fc5\u987b\u5728\u76d8\u4e0a',
                'Test-Path ' + str(ok) + ': Assets/Resources/' + path,
                '\u4e00\u81f4' if ok else '\u4e0d\u4e00\u81f4(\u8d44\u6e90\u6839\u4e0d\u5b58\u5728)',
                'coverage-enumerate.py')

def d1_source_vs_landed(landed_dirs):
    """原版素材目录 → 是否落地（典型漏检：源有、我们没搬）。"""
    if not os.path.isdir(SC_SRC):
        R.note.append('blocked: sc source dir missing: ' + SC_SRC)
        return
    src_names = sorted(d for d in os.listdir(SC_SRC) if os.path.isdir(os.path.join(SC_SRC, d)))
    landed_all = set(landed_dirs.keys()) | {os.path.basename(d) for d in walk_dirs(UI_DIR)}
    for n in src_names:
        R.add_e('D1\u8d44\u6e90', n, 'cr-assets-png/assets/sc/' + n,
                spec_sec(9) + ' / cr-assets-png', 2, '\u53c2\u8003\u7269\u6bd4\u5bf9', 'AE1-D1')
        if n not in landed_all:
            R.add_m('D1\u8d44\u6e90', n, '\u6e90\u76ee\u5f55\u662f\u5426\u5df2\u843d\u5730',
                    '\u843d\u5730 / \u672a\u843d\u5730',
                    '\u53c2\u8003\u89c4\u683c \u00a76 \u5217\u51fa\u7684\u7d20\u6750\u76ee\u5f55\u5e94\u843d\u5730',
                    '\u672a\u843d\u5730\uff08Resources \u4e0b\u65e0\u540c\u540d\u76ee\u5f55\uff09',
                    '\u672a\u5224(\u5f85\u5224\u5b9a\u662f\u5426\u5c5e\u672c\u9879\u76ee\u8303\u56f4)',
                    'coverage-enumerate.py (source-vs-landed)')
        else:
            R.add_m('D1\u8d44\u6e90', n, '\u6e90\u76ee\u5f55\u662f\u5426\u5df2\u843d\u5730',
                    '\u843d\u5730 / \u672a\u843d\u5730', '\u5df2\u843d\u5730', '\u5df2\u843d\u5730',
                    '\u4e00\u81f4', 'coverage-enumerate.py')

def d1_unit_sprite_dirs():
    """unit_cs.sprite_dir 列 → 盘上是否有该目录（配表与素材是否对齐）。"""
    p = os.path.join(NUMDIR, 'unit_cs.txt')
    if not os.path.isfile(p):
        R.note.append('blocked: unit_cs.txt missing')
        return
    header, rows = read_src_table(p)
    i_sd = header.index('sprite_dir')
    i_key = header.index('key')
    seen = set()
    for r in rows:
        sd = r[i_sd].strip()
        if not sd or sd in seen:
            continue
        seen.add(sd)
        for one in sd.split(';'):
            one = one.strip()
            if not one:
                continue
            cand = [os.path.join(RES, 'Sprites', 'Units', one),
                    os.path.join(RES, 'Sprites', 'Buildings', one),
                    os.path.join(RES, 'Sprites', 'Cards', one),
                    os.path.join(RES, 'Sprites', 'Towers', one)]
            hit = next((c for c in cand if os.path.isdir(c)), None)
            R.add_e('D1\u8d44\u6e90', one, rel(hit) if hit else 'Resources/Sprites/**/' + one,
                    'unit_cs.txt:sprite_dir / ' + spec_sec(6), 1, '\u811a\u672c\u65ad\u8a00', 'AE1-D1')
            R.add_m('D1\u8d44\u6e90', one, 'sprite_dir \u662f\u5426\u6709\u5bf9\u5e94\u76ee\u5f55',
                    'dir hit / miss', 'unit_cs.sprite_dir \u5fc5\u987b\u5728\u76d8\u4e0a\u5b58\u5728',
                    ('dir hit: ' + rel(hit)) if hit else 'dir MISS',
                    '\u4e00\u81f4' if hit else '\u4e0d\u4e00\u81f4(sprite_dir \u5728\u76d8\u4e0a\u4e0d\u5b58\u5728)',
                    'coverage-enumerate.py')

def run_check_ui_keys():
    """复用既有探针（不重写），把它的四向检查折进 D1。"""
    if not os.path.isfile(CHECKUI):
        R.note.append('blocked: check-ui-keys.ps1 missing')
        return
    try:
        out = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                              '-File', CHECKUI],
                             cwd=ROOT, capture_output=True, timeout=120)
        txt = out.stdout.decode('utf-8', 'replace')
    except Exception as e:
        R.note.append('blocked: check-ui-keys.ps1 failed: %r' % (e,))
        return
    for line in txt.splitlines():
        line = line.rstrip()
        if line.startswith('B) landed png reachable'):
            R.note.append('check-ui-keys: ' + line)
        if line.strip().startswith('unreferenced:'):
            for item in line.split(':', 1)[1].split(','):
                item = item.strip()
                if item:
                    R.orphans.append(['(ui) ' + item, 'Assets/Resources/Sprites/Ui/' + item, '1',
                                      'Sprites/Ui \u4e0b\u843d\u5730\u4f46\u65e0\u4efb\u4f55\u5f15\u7528',
                                      'check-ui-keys.ps1 B', '\u4e25\u91cd'])
        if line.strip().startswith('missing:'):
            for item in line.split(':', 1)[1].split(','):
                item = item.strip()
                if item:
                    R.add_m('D1\u8d44\u6e90', item, '\u4efb\u610f .cs \u5f15\u7528\u2192\u6587\u4ef6\u5b58\u5728',
                            'exists', 'ResPaths/\u5b57\u9762\u91cf\u5f15\u7528\u7684\u8def\u5f84\u5fc5\u987b\u5b58\u5728',
                            'missing on disk', '\u4e0d\u4e00\u81f4(.cs \u5f15\u7528\u4e86\u4e0d\u5b58\u5728\u7684\u8d44\u6e90)',
                            'check-ui-keys.ps1 D')

# ---------------------------------------------------------------- D2 几何与场景

# 竞技场几何实体（出处 = 参考规格 §2；常量 = client/Assets/Scripts/Core/GameConst.cs）
# 每项：(实体名, [GameConst 里必须存在的常量名], 几何值描述, [参考规格 §2 里必须逐字出现的字面量])
ARENA_GEOM = [
    ('\u573a\u5730\u5c3a\u5bf8',        ['ArenaTilesW', 'ArenaTilesH'],          '18 \u00d7 32 \u683c',      ['18', '32']),
    ('\u4e2d\u7ebf',                    ['ArenaTilesH'],                          'y = 16.0',                 ['16']),
    ('\u6cb3\u9053',                    ['RiverTopTile', 'RiverBottomTile'],      'y \u2208 [15.0, 17.0)\uff0c\u5bbd 2 \u683c', ['15.0', '17.0']),
    ('\u5de6\u6865',                    ['BridgeCxATile', 'BridgeHalfTile'],      'x = 3.5\uff0c\u5bbd 2 \u683c', ['3.5']),
    ('\u53f3\u6865',                    ['BridgeCxBTile', 'BridgeHalfTile'],      'x = 14.5\uff0c\u5bbd 2 \u683c', ['14.5']),
    ('\u56fd\u738b\u5854 BLUE',         ['KingTowerTileX', 'KingTowerTileY'],     '(9.0, 3.0)\uff0c\u5360\u5730 3\u00d73', ['9.0', '3.0']),
    ('\u56fd\u738b\u5854 RED',          ['KingTowerTileX', 'IsMirroredForTeam'],  '(9.0, 29.0)\uff08y\u219232-y\uff09', ['32']),
    ('\u516c\u4e3b\u5854 BLUE \u5de6',  ['BridgeCxATile', 'PrincessTowerTileY'],  '(3.5, 6.5)\uff0c\u534a\u5f84 1 \u683c', ['3.5', '6.5']),
    ('\u516c\u4e3b\u5854 BLUE \u53f3',  ['BridgeCxBTile', 'PrincessTowerTileY'],  '(14.5, 6.5)', ['14.5', '6.5']),
    ('\u516c\u4e3b\u5854 RED \u5de6',   ['BridgeCxATile', 'IsMirroredForTeam'],   '(3.5, 25.5)', ['25.5']),
    ('\u516c\u4e3b\u5854 RED \u53f3',   ['BridgeCxBTile', 'IsMirroredForTeam'],   '(14.5, 25.5)', ['25.5']),
    ('\u5750\u6807\u7ea6\u5b9a',        ['TileToWorld', 'ArenaTilesW', 'ArenaTilesH'], 'y \u5c0f = BLUE \u540e\u65b9\uff0c\u4e2d\u5fc3\u5728\u539f\u70b9', ['\u5750\u6807\u7ea6\u5b9a']),
]

# 竞技场里"该有就得有"的渲染节点（出处 = ArenaView.cs 节点名 / 方法名）
# 每项：(实体名, [ArenaView.cs 里必须出现的 token], 来源描述)
ARENA_NODES = [
    ('\u7ade\u6280\u573a\u5e95\u56fe\u5bf9\u8c61 Base', ['BuildBaseQuad', '"Base"'], 'ArenaView.BuildBaseQuad'),
    ('\u5e95\u56fe\u5206\u6bb5 Seg / SegF',             ['MakeSegment', '"Seg"', '"SegF"'], 'ArenaView.MakeSegment'),
    ('\u6cb3\u9053\u5e26 RiverBand',                    ['MakeCrop', '"RiverBand"'], 'ArenaView.BuildRiverAndBridges'),
    ('\u5de6\u6865 BridgeLeft',                         ['MakeCrop', '"BridgeLeft"'], 'ArenaView.BuildRiverAndBridges'),
    ('\u53f3\u6865 BridgeRight',                        ['MakeCrop', '"BridgeRight"'], 'ArenaView.BuildRiverAndBridges'),
    ('\u5854\u6839\u8282\u70b9 Towers',                 ['BuildTowers', '"Towers"'], 'ArenaView.BuildTowers'),
    ('KingTower \u00d7 2 / PrincessTower \u00d7 4',     ['"KingTower"', '"PrincessTower"', '_towers.Count != 6'], 'ArenaView.AddTower + 6 \u5ea7\u65ad\u8a00'),
    ('\u5854\u4f4d\u6765\u81ea GameConst\uff08\u4e0d\u5199\u6b7b\uff09', ['GameConst.TileToWorld', 'GameConst.KingTowerTileX', 'GameConst.BridgeCxATile'], 'ArenaView.BuildTowers'),
]

PANELS = [
    ('BootPanel',        'S1 \u542f\u52a8\u753b\u9762'),
    ('MainMenuPanel',    'S2 \u4e3b\u83dc\u5355'),
    ('LoginPanel',       'S3 \u767b\u5f55'),
    ('RegisterPanel',    'S3 \u6ce8\u518c'),
    ('NicknamePanel',    'S4 \u6635\u79f0'),
    ('RoomListPanel',    'S5 \u623f\u95f4\u5217\u8868'),
    ('RoomPanel',        'S6 \u623f\u95f4\u5185'),
    ('DeckEditPanel',    'S7 \u5361\u7ec4\u7f16\u8f91'),
    ('LoadingPanel',     'S8 \u8bfb\u6761\u8fdb\u56fe'),
    ('HudPanel',         'S9/S10/S11/S12 \u5bf9\u5c40 HUD'),
    ('PausePanel',       'S22 \u6682\u505c\u83dc\u5355'),
    ('SettingsPanel',    'S23 \u8bbe\u7f6e'),
    ('ResultPanel',      'S21 \u7ed3\u7b97'),
]

def d2_arena():
    gc = rd_text(GAMECONST)
    av = rd_text(ARENAVIEW)
    spec = rd_text(SPEC)
    for name, consts, val, lit in ARENA_GEOM:
        found = [(c, c in gc) for c in consts]
        ok = all(x[1] for x in found)
        R.add_e('D2\u51e0\u4f55', '\u7ade\u6280\u573a\u5b9e\u4f53\uff1a' + name,
                'client/Assets/Scripts/Core/GameConst.cs', spec_sec(2), 3,
                '\u811a\u672c\u65ad\u8a00', 'AE1-D2')
        R.add_m('D2\u51e0\u4f55', '\u7ade\u6280\u573a\u5b9e\u4f53\uff1a' + name,
                '\u5e38\u91cf\u5b58\u5728\u4e0e\u53d6\u503c', val,
                spec_sec(2) + '\uff1a' + val + ' \u5fc5\u987b\u6709\u5bf9\u5e94\u5e38\u91cf',
                ('consts=%s' % ','.join('%s=%s' % (c, o) for c, o in found)),
                '\u4e00\u81f4' if ok else '\u4e0d\u4e00\u81f4(\u5e38\u91cf\u7f3a\u5931)',
                'coverage-enumerate.py (GameConst.cs)')
        # 参考规格 §2 是否逐字写着该几何值（参考物自带判据）
        miss = [s for s in lit if s not in spec]
        R.add_m('D2\u51e0\u4f55', '\u7ade\u6280\u573a\u5b9e\u4f53\uff1a' + name,
                '\u53c2\u8003\u89c4\u683c \u00a72 \u5b57\u9762\u503c', '/'.join(lit),
                spec_sec(2) + ' \u5fc5\u987b\u9010\u5b57\u5199\u7740\u8be5\u51e0\u4f55\u503c',
                'missing=' + (','.join(miss) if miss else 'none'),
                '\u4e00\u81f4' if not miss else '\u4e0d\u4e00\u81f4(\u53c2\u8003\u89c4\u683c\u00a72 \u7f3a\u8be5\u5b57\u9762\u503c)',
                'coverage-enumerate.py (SPEC \u00a72)')

    for name, toks, src in ARENA_NODES:
        miss = [t for t in toks if t not in av]
        ok = not miss
        R.add_e('D2\u51e0\u4f55', '\u7ade\u6280\u573a\u8282\u70b9\uff1a' + name,
                'client/Assets/Scripts/View/ArenaView.cs', spec_sec(2) + ' + ArenaView.cs',
                2, '\u811a\u672c\u65ad\u8a00+\u5e76\u6392\u56fe', 'AE1-D2')
        R.add_m('D2\u51e0\u4f55', '\u7ade\u6280\u573a\u8282\u70b9\uff1a' + name,
                '\u8be5\u6709\u7684\u8282\u70b9\u662f\u5426\u5b58\u5728', 'exists / missing',
                spec_sec(2) + ' \u8981\u6c42\u7684\u51e0\u4f55\u7269\u4ef6\u5fc5\u987b\u751f\u6210',
                'source=' + src + ' | tokens ok' if ok else ('MISSING tokens=' + ','.join(miss)),
                '\u4e00\u81f4' if ok else '\u4e0d\u4e00\u81f4(\u4ee3\u7801\u91cc\u627e\u4e0d\u5230\u8be5\u8282\u70b9)',
                'coverage-enumerate.py (ArenaView.cs)')

def d2_panels():
    for name, sec in PANELS:
        p = os.path.join(SCRIPTS, 'UI', 'Panels', name + '.cs')
        ok = os.path.isfile(p)
        R.add_e('D2\u51e0\u4f55', '\u9762\u677f\uff1a' + name,
                rel(p) if ok else '(missing) ' + rel(p),
                '\u53c2\u8003\u89c4\u683c \u00a77 / ' + sec, 3,
                '\u5e76\u6392\u56fe', 'AE1-D2')
        R.add_m('D2\u51e0\u4f55', '\u9762\u677f\uff1a' + name, '\u5b58\u5728\u6027(\u8be5\u6709\u7684\u6709\u6ca1\u6709)',
                'exists / missing', '\u53c2\u8003\u89c4\u683c \u00a77 \u5217\u4e3a [\u6838\u5fc3] \u7684\u754c\u9762\u5fc5\u987b\u5b58\u5728',
                ('file exists: ' + rel(p)) if ok else 'file MISSING',
                '\u4e00\u81f4' if ok else '\u4e0d\u4e00\u81f4(\u754c\u9762\u6587\u4ef6\u7f3a\u5931)',
                'coverage-enumerate.py')

    # 场景文件（Editor 生成器 + 已生成产物）
    sb = os.path.join(EDITOR, 'SceneBuilder.cs')
    scenes_dir = os.path.join(CLIENT, 'Assets', 'Scenes')
    R.add_e('D2\u51e0\u4f55', '\u573a\u666f\u751f\u6210\u5668 SceneBuilder', rel(sb), spec_sec(7),
            1, '\u811a\u672c\u65ad\u8a00', 'AE1-D2')
    R.add_m('D2\u51e0\u4f55', '\u573a\u666f\u751f\u6210\u5668 SceneBuilder', '\u5b58\u5728\u6027',
            'exists', 'Boot/Main/Battle01 \u573a\u666f\u5fc5\u987b\u914d\u7f6e', 'exists=' + str(os.path.isfile(sb)),
            '\u4e00\u81f4' if os.path.isfile(sb) else '\u4e0d\u4e00\u81f4(\u573a\u666f\u751f\u6210\u5668\u7f3a\u5931)',
            'coverage-enumerate.py')
    for s in ['Boot', 'Main', 'Battle01']:
        p = os.path.join(scenes_dir, s + '.unity')
        ok = os.path.isfile(p)
        R.add_e('D2\u51e0\u4f55', '\u573a\u666f\uff1a' + s, rel(p) if ok else '(missing) ' + rel(p),
                'verif\u9a8c\u6536\u8868 T2-B1 / ' + spec_sec(7), 1, '\u811a\u672c\u65ad\u8a00', 'AE1-D2')
        R.add_m('D2\u51e0\u4f55', '\u573a\u666f\uff1a' + s, '\u5b58\u5728\u6027', 'exists',
                'Build Settings \u5e8f\u5217 Boot\u2192Main\u2192Battle01 \u5fc5\u987b\u5b58\u5728',
                'exists=' + str(ok),
                '\u4e00\u81f4' if ok else '\u4e0d\u4e00\u81f4(\u573a\u666f\u6587\u4ef6\u7f3a\u5931)',
                'coverage-enumerate.py')

# ---------------------------------------------------------------- D3 材质与贴图表现

D3_OBJECTS = [
    ('\u7ade\u6280\u573a\u5e95\u56fe Base',    '\u7eaf\u8272\u515c\u5e95(SpriteBank.WhiteSprite)', 'ArenaView.BuildBaseQuad'),
    ('\u7ade\u6280\u573a Art Seg \u00d74',      'arena_training_out frame_006', 'ArenaView.BuildArt'),
    ('\u6cb3\u9053\u5e26 RiverBand',           'arena_training_out frame_022 \u88c1\u6761', 'ArenaView.BuildRiverAndBridges'),
    ('\u5de6\u6865 BridgeLeft',                'arena_training_out frame_022 \u88c1\u6761', 'ArenaView.BuildRiverAndBridges'),
    ('\u53f3\u6865 BridgeRight',               'arena_training_out frame_022 \u88c1\u6761', 'ArenaView.BuildRiverAndBridges'),
    ('\u56fd\u738b\u5854 BLUE \u5854\u8eab',    'building_tower_out frame_203', 'ArenaView.BlueKingFrame'),
    ('\u56fd\u738b\u5854 RED \u5854\u8eab',     'building_tower_out frame_201', 'ArenaView.RedKingFrame'),
    ('\u516c\u4e3b\u5854 BLUE \u5854\u8eab',    'building_tower_out frame_213', 'ArenaView.BluePrincessFrame'),
    ('\u516c\u4e3b\u5854 RED \u5854\u8eab',     'building_tower_out frame_211', 'ArenaView.RedPrincessFrame'),
    ('\u5355\u4f4d\u7cbe\u7075',                'chr_*_out/ Sprites/Units', 'UnitView / ResPaths.UnitDir'),
    ('\u547d\u4e2d\u95ea\u5149\u7279\u6548',    'effects_out Hit f050..f056', 'ResPaths.EffectHit*'),
    ('\u7206\u70b8\u7279\u6548',                'effects_out Blast f418..f427', 'ResPaths.EffectBlast*'),
    ('\u5f39\u9053\u7279\u6548',                'effects_out Arrow f440..f459', 'ResPaths.EffectArrow*'),
    ('UI \u9762\u677f\u5e95/\u6807\u9898\u6761/\u6309\u94ae', 'Sprites/Ui/**', 'ResPaths.cs + copy-ui-assets.py'),
]

def d3(landed):
    ui_files = walk_files(UI_DIR, '.png')
    ui_white = []
    for f in ui_files:
        st = png_stats(f)
        if st and st[1] is not None and st[2] is not None:
            w, h, na, white = st
            if na == 0:
                ui_white.append((os.path.basename(f), rel(f), '\u5168\u900f\u660e(\u7a7a\u56fe)'))
            elif white:
                ui_white.append((os.path.basename(f), rel(f), '\u975e\u900f\u660e\u50cf\u7d20\u5168\u4e3a\u8fd1\u767d(\u53ef\u80fd\u5360\u4f4d/\u7eaf\u767d)'))

    # 每个已落地精灵目录：抽首帧判"纯白/空图"（数值判据 → 典型漏检：skin 没绑 / 占位白）
    for dirname, offset in sorted(landed.items()):
        f = sorted(walk_files(offset, '.png'))[0]
        st = png_stats(f)
        R.add_e('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a(\u76ee\u5f55) ' + dirname, rel(offset),
                spec_sec(6) + ' / ResPaths.cs', len(walk_files(offset, '.png')),
                '\u811a\u672c\u65ad\u8a00', 'AE1-D3')
        if st is None or st[2] is None:
            R.add_m('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a(\u76ee\u5f55) ' + dirname,
                    '\u9996\u5e27\u662f\u5426\u7a7a\u56fe/\u7eaf\u767d', '\u975e\u7a7a\u975e\u767d / \u7a7a / \u7eaf\u767d',
                    '\u7cbe\u7075\u9996\u5e27\u5fc5\u987b\u975e\u7a7a\u4e14\u975e\u7eaf\u767d',
                    'decode-failed(or unsupported ctype)', '\u672a\u5224(\u56fe\u50cf\u65e0\u6cd5\u89e3\u7801)',
                    'coverage-enumerate.py (png_stats)')
        else:
            w, h, na, white = st
            bad = (na == 0) or bool(white)
            R.add_m('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a(\u76ee\u5f55) ' + dirname,
                    '\u9996\u5e27\u662f\u5426\u7a7a\u56fe/\u7eaf\u767d', '\u975e\u7a7a\u975e\u767d / \u7a7a / \u7eaf\u767d',
                    '\u7cbe\u7075\u9996\u5e27\u5fc5\u987b\u975e\u7a7a\u4e14\u975e\u7eaf\u767d',
                    '%s: %dx%d nonalpha=%s near_white=%s' % (os.path.basename(f), w, h, na, white),
                    '\u4e0d\u4e00\u81f4(\u7a7a\u56fe/\u7eaf\u767d)' if bad else '\u4e00\u81f4',
                    'coverage-enumerate.py (png_stats)')
    for name, slot, src in D3_OBJECTS:
        R.add_e('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a' + name,
                src, spec_sec(6) + ' / ResPaths.cs', 3, '\u811a\u672c\u65ad\u8a00+\u5e76\u6392\u56fe', 'AE1-D3')
        # 是否绑上 / 是否纯白 —— 静态可见的只能给"引用是否指向存在的文件"
        if name.startswith('\u56fd\u738b\u5854') or name.startswith('\u516c\u4e3b\u5854'):
            fr = re.search(r'frame_(\d+)', slot)
            f = os.path.join(RES, 'Sprites', 'Towers', 'building_tower_out',
                             'frame_%03d.png' % int(fr.group(1)))
            st = png_stats(f) if os.path.isfile(f) else None
            if st is None:
                R.add_m('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a' + name, '\u8d34\u56fe\u662f\u5426\u7ed1\u4e0a\u4e14\u975e\u7a7a',
                        'exists / blank / white', '\u5854\u8eab\u5e27\u5fc5\u987b\u5728\u76d8\u4e0a\u4e14\u975e\u7a7a',
                        'file MISSING: ' + rel(f), '\u4e0d\u4e00\u81f4(\u5854\u8eab\u5e27\u7f3a\u5931)',
                        'coverage-enumerate.py (png_stats)')
            else:
                w, h, na, white = st
                bad = (na == 0) or bool(white)
                R.add_m('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a' + name, '\u8d34\u56fe\u662f\u5426\u7ed1\u4e0a\u4e14\u975e\u7a7a',
                        'exists / blank / white', '\u5854\u8eab\u5e27\u5fc5\u987b\u975e\u7a7a\u4e14\u975e\u7eaf\u767d',
                        '%dx%d nonalpha=%s near_white=%s' % (w, h, na, white),
                        '\u4e0d\u4e00\u81f4(\u7eaf\u767d/\u7a7a\u56fe)' if bad else '\u4e00\u81f4',
                        'coverage-enumerate.py (png_stats)')
        else:
            R.add_m('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a' + name, '\u8d34\u56fe\u662f\u5426\u7ed1\u4e0a\u4e14\u975e\u7eaf\u767d',
                    '\u7ed1\u5b9a / \u672a\u7ed1\u5b9a', '\u7279\u5b9a\u7d20\u6750\u5e27\u5fc5\u987b\u88ab\u8d4b\u7ed9 SpriteRenderer',
                    'pending: \u9700\u5b9e\u4f8b\u5316\u5e27\u7d20\u636e', '\u672a\u5224(\u5f85\u5e76\u6392\u56fe)',
                    'coverage-enumerate.py (struct)')
    # 纯白/空图清单（数值判据，非截图）
    for base, p, why in ui_white:
        R.add_e('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a(ui) ' + base, p,
                'copy-ui-assets.py \u843d\u5730', 2, '\u811a\u672c\u65ad\u8a00', 'AE1-D3')
        R.add_m('D3\u6750\u8d28', '\u53ef\u89c1\u7269\u4ef6\uff1a(ui) ' + base, '\u8d34\u56fe\u662f\u5426\u7eaf\u767d/\u7a7a',
                '\u975e\u7a7a\u975e\u767d / \u7a7a / \u7eaf\u767d', 'UI \u56fe\u5143\u5fc5\u987b\u975e\u7a7a\u4e14\u975e\u7eaf\u767d',
                why, '\u4e0d\u4e00\u81f4(\u7a7a\u56fe/\u7eaf\u767d)', 'coverage-enumerate.py (png_stats)')

# ---------------------------------------------------------------- S1 数值与配表

RARITY_IDX = {'Common': 10, 'Rare': 8, 'Epic': 5, 'Legendary': 2, 'Champion': 0,
              'common': 10, 'rare': 8, 'epic': 5, 'legendary': 2, 'champion': 0}
RARITY_CN = {'0': 'Common', '1': 'Rare', '2': 'Epic', '3': 'Legendary'}

def load_api():
    d = {}
    for n in ['cards', 'cards_stats_characters', 'cards_stats_building', 'cards_stats_troop',
              'cards_stats_spell', 'cards_stats_projectile', 'rarities']:
        p = os.path.join(API_JSON, n + '.json')
        d[n] = json.load(io.open(p, encoding='utf-8')) if os.path.isfile(p) else None
    return d

def api_level_idx(rarity_name):
    return RARITY_IDX.get(rarity_name, None)

def pick_per_level(row, field, idx):
    v = row.get(field)
    if isinstance(v, list) and 0 <= idx < len(v):
        return v[idx]
    if not isinstance(v, list):
        return v
    return None

def s1_tables():
    """配表覆盖 + 逐值比官方 JSON（差值必须 0 或已登记）。"""
    api = load_api()
    cards = api.get('cards') or []
    by_name_card = {}
    for c in cards:
        for k in ('key', 'name', 'id'):
            if k in c and c[k]:
                by_name_card[str(c[k]).lower()] = c
    chars = {r['name']: r for r in (api.get('cards_stats_characters') or [])}
    builds = {r['name']: r for r in (api.get('cards_stats_building') or [])}
    spells = api.get('cards_stats_spell') or []
    projs = {r['name']: r for r in (api.get('cards_stats_projectile') or [])}
    rar = {r['name']: r for r in (api.get('rarities') or [])}

    # ---- 产物 vs 源表 行数（打表产物 server/game/table/tsv/*.tsv）
    for tsvname, srcname in [('card.tsv', 'card_cs.txt'), ('unit.tsv', 'unit_cs.txt'),
                             ('spell.tsv', 'spell_cs.txt')]:
        tf = os.path.join(ROOT, 'server', 'game', 'table', 'tsv', tsvname)
        sf = os.path.join(NUMDIR, srcname)
        if not (os.path.isfile(tf) and os.path.isfile(sf)):
            R.note.append('blocked: table tsv/src missing: %s / %s' % (tsvname, srcname))
            continue
        _, tr = read_tsv(tf)
        _, sr = read_src_table(sf)
        R.add_e('S1\u6570\u503c', '\u6253\u8868\u4ea7\u7269 ' + tsvname,
                rel(tf), '\u7b56\u5212/\u6570\u503c\u6587\u6863/' + srcname, len(tr),
                '\u811a\u672c\u65ad\u8a00', 'AE1-S1')
        R.add_m('S1\u6570\u503c', '\u6253\u8868\u4ea7\u7269 ' + tsvname,
                '\u4ea7\u7269\u884c\u6570 vs \u6e90\u8868\u884c\u6570', '%d' % len(sr),
                '\u6e90\u8868\u6570\u636e\u884c\u5fc5\u987b\u7b49\u4e8e\u4ea7\u7269\u6570\u636e\u884c',
                'src=%d tsv=%d' % (len(sr), len(tr)),
                '\u4e00\u81f4' if len(sr) == len(tr) else '\u4e0d\u4e00\u81f4(\u6253\u8868\u4ea7\u7269\u884c\u6570\u4e0d\u5339\u914d)',
                'coverage-enumerate.py (table tsv)')

    # ---- card_cs 逐值比官方
    h, rows = read_src_table(os.path.join(NUMDIR, 'card_cs.txt'))
    for r in rows:
        d = dict(zip(h, r))
        key = d['key']
        off = by_name_card.get(key.lower())
        R.add_e('S1\u6570\u503c', 'card_cs:' + key, '\u7b56\u5212/\u6570\u503c\u6587\u6863/card_cs.txt',
                spec_sec(6), 4, '\u53c2\u8003\u7269\u6bd4\u5bf9', 'AE1-S1')
        if off is None:
            R.add_m('S1\u6570\u503c', 'card_cs:' + key, 'key\u2194\u5b98\u65b9 cards.json',
                    'found / missing', '\u6bcf\u884c key \u5fc5\u987b\u80fd\u5728\u5b98\u65b9 cards.json \u627e\u5230',
                    'official cards.json: MISS', '\u4e0d\u4e00\u81f4(key \u5728\u5b98\u65b9\u6570\u636e\u91cc\u4e0d\u5b58\u5728)',
                    'coverage-enumerate.py')
            continue
        # 圣水（官方 cards.json 的字段名是 elixir；兼容 mana_cost 拼写）
        oc = off.get('elixir')
        if oc is None:
            oc = off.get('mana_cost')
        try:
            ou = int(d['elixir'])
        except Exception:
            ou = None
        ok = (oc == ou)
        R.add_m('S1\u6570\u503c', 'card_cs:' + key, 'elixir \u2194\u5b98\u65b9 cards.json:elixir', str(oc),
                'cards.json:elixir = %s' % oc, 'ours=%s official=%s' % (ou, oc),
                '\u4e00\u81f4' if ok else '\u4e0d\u4e00\u81f4(elixir \u2260 \u5b98\u65b9 elixir)',
                'coverage-enumerate.py (cards.json)')
        # 稀有度
        orr = off.get('rarity')
        ourname = RARITY_CN.get(d['rarity'], '?')
        ok2 = (str(orr).lower() == ourname.lower())
        R.add_m('S1\u6570\u503c', 'card_cs:' + key, 'rarity \u2194\u5b98\u65b9 rarity', ourname,
                'cards.json:rarity = %s' % orr, 'ours=%s official=%s' % (ourname, orr),
                '\u4e00\u81f4' if ok2 else '\u4e0d\u4e00\u81f4(rarity \u4e0d\u7b26)',
                'coverage-enumerate.py (cards.json)')

    # ---- unit_cs 逐值比官方
    h, rows = read_src_table(os.path.join(NUMDIR, 'unit_cs.txt'))
    for r in rows:
        d = dict(zip(h, r))
        key = d['key']
        R.add_e('S1\u6570\u503c', 'unit_cs:' + key, '\u7b56\u5212/\u6570\u503c\u6587\u6863/unit_cs.txt',
                spec_sec(4) + '/' + spec_sec(5) + '/' + spec_sec(6), 12, '\u53c2\u8003\u7269\u6bd4\u5bf9', 'AE1-S1')
    return api, chars, builds, projs, rar

def _norm(s):
    return re.sub(r'[^a-z0-9]', '', str(s).lower())

def build_official_index(api):
    """官方实体索引：norm(name) → 行；card key → 卡行；卡名 → 召唤实体名。"""
    by_norm = {}
    for src in ('cards_stats_characters', 'cards_stats_building', 'cards_stats_projectile'):
        for r in (api.get(src) or []):
            nm = r.get('name')
            if nm:
                by_norm.setdefault(_norm(nm), r)
    card_by_key = {}
    card_sc = {}                 # 卡 key(norm) -> 官方 cards.json:sc_key（= 该卡的战斗实体名）
    for c in (api.get('cards') or []):
        if c.get('key'):
            card_by_key[_norm(c['key'])] = c
            if c.get('sc_key'):
                card_sc[_norm(c['key'])] = c['sc_key']
    troop_summon = {}
    for r in (api.get('cards_stats_troop') or []):
        nm, sc = r.get('name'), r.get('summon_character')
        if nm and sc:
            troop_summon.setdefault(_norm(nm), sc)
    return by_norm, card_by_key, troop_summon, card_sc

def _lookup(name, by_norm):
    """官方实体名查找：先按原样、再按复数（官方把 FireSpirit 记作 FireSpirits 一类）。"""
    n = _norm(name)
    return by_norm.get(n) or by_norm.get(n + 's')

def resolve_official(key, kind, summon_key, by_norm, card_by_key, troop_summon, card_sc=None):
    """按 unit_cs 的 kind 定位官方实体行（出处 = 官方 JSON 的 name 字段）。
    ① 卡 key 有官方 sc_key ⇒ 以 sc_key 为准（bandit→Assassin / executioner→AxeMan /
       cannon-cart→MovingCannon / dart-goblin→BlowdartGoblin 等官方改名卡由此定位）；
    ② 部队再退到 summon_key（群体卡的实体行）；③ 最后按原名。"""
    if card_sc:
        sc = card_sc.get(_norm(key))
        if sc:
            r = _lookup(sc, by_norm)
            if r is not None:
                return r
    if kind in ('3', '2'):               # 投射物 / 塔
        return _lookup(key, by_norm)
    if kind == '0':                      # 部队：以"本体/被召唤实体"为准
        cand = summon_key.strip() or key
        return _lookup(cand, by_norm) or _lookup(key, by_norm)
    # kind == '1' 建筑：unit_cs.key 是卡 key ⇒ 先原名，再经卡名映射到官方建筑名
    r = _lookup(key, by_norm)
    if r is not None:
        return r
    c = card_by_key.get(_norm(key))
    if c is not None and c.get('name'):
        nm = c['name']
        r = _lookup(troop_summon.get(_norm(nm), nm), by_norm) or _lookup(nm, by_norm)
        if r is not None:
            return r
    return None

# 官方列：unit_cs 列名 -> 官方 JSON 字段（出处 = cards_stats_character/building/projectile.json）
UNIT_OFFICIAL = [
    ('hp',                 'hitpoints_per_level'),
    ('damage',             'damage_per_level'),
    ('hit_speed_ms',       'hit_speed'),
    ('load_time_ms',       'load_time'),
    ('speed',              'speed'),
    ('range_mt',           'range'),
    ('sight_mt',           'sight_range'),
    ('deploy_ms',          'deploy_time'),
    ('radius_mt',          'collision_radius'),
    ('mass',               'mass'),
    ('atk_air',            'attacks_air'),
    ('atk_ground',         'attacks_ground'),
    ('only_buildings',     'target_only_buildings'),
    ('only_towers',        'target_only_towers'),
    ('only_troops',        'target_only_troops'),
    ('projectile_key',     'custom_first_projectile/projectile'),
    ('death_spawn_key',    'death_spawn_character'),
    ('death_spawn_n',      'death_spawn_count'),
    ('death_damage',       'death_damage'),
    ('death_aoe_radius_mt', 'death_damage_radius'),
    ('life_ms',            'life_time'),
    ('spawn_key',          'spawn_character'),
    ('spawn_n',            'spawn_number'),
    ('spawn_radius_mt',    'spawn_radius'),
    ('spawn_interval_ms',  'spawn_interval/spawn_pause_time'),
    ('spawn_limit',        'spawn_limit'),
    ('aoe_radius_mt',      'area_damage_radius'),
    ('flying',             'flying_height'),
]
UNIT_BOOL = ('atk_air', 'atk_ground', 'only_buildings', 'only_towers', 'only_troops', 'flying')
UNIT_STR = ('projectile_key', 'death_spawn_key', 'spawn_key')
# 本项目自定列（取值口径见 策划/数值文档/unit_cs.txt 说明行 / docs/步骤文档.md §4.2）
UNIT_CUSTOM = ['id', 'key', 'name_cn', 'kind', 'sprite_dir', 'summon_key', 'summon_n',
               'summon_radius_mt', 'summon_deploy_delay_ms']

def _official_unit_value(off, off_k, ours_k, idx, by_norm):
    """返回官方取值（None = 官方无此字段/该等级无值，跳过）。damage 本体为 0 时退到投射物。"""
    if ours_k == 'projectile_key':
        v = off.get('custom_first_projectile') or off.get('projectile')
        return v if v else None
    if ours_k == 'spawn_interval_ms':
        v = off.get('spawn_interval') or off.get('spawn_pause_time')
        return int(v) if v is not None else 0
    if ours_k == 'damage':
        v = pick_per_level(off, 'damage_per_level', idx)
        if v in (None, 0):
            pj = by_norm.get(_norm(off.get('projectile') or ''))
            if pj is not None:
                v2 = pick_per_level(pj, 'damage_per_level', api_level_idx(pj.get('rarity')))
                if v2 is None and 'damage' in pj:
                    v2 = pj.get('damage')
                if v2 is not None:
                    v = v2
        return int(v) if v is not None else None
    if ours_k == 'radius_mt':
        for f in ('collision_radius', 'radius'):
            if f in off:
                v = pick_per_level(off, f, idx)
                return int(v) if v is not None else None
        return None
    if ours_k == 'flying':
        v = off.get('flying_height')
        return bool(v) if v is not None else None
    if ours_k in UNIT_BOOL:
        v = off.get(off_k)
        return bool(v) if v is not None else None
    if off_k not in off:
        return None
    v = pick_per_level(off, off_k, idx) if isinstance(off.get(off_k), list) else off.get(off_k)
    if v is None:
        return None
    return v if ours_k in UNIT_STR else int(v)

def s1_unit_values(api, chars, builds, projs, rar):
    by_norm, card_by_key, troop_summon, card_sc = build_official_index(api)
    h, rows = read_src_table(os.path.join(NUMDIR, 'unit_cs.txt'))
    for r in rows:
        d = dict(zip(h, r))
        key = d['key']
        off = resolve_official(key, d.get('kind', ''), d.get('summon_key', '').split(';')[0],
                               by_norm, card_by_key, troop_summon, card_sc)
        if off is None:
            R.add_m('S1\u6570\u503c', 'unit_cs:' + key, '\u2194\u5b98\u65b9\u5b9e\u4f53\u884c',
                    'found / missing', 'unit_cs \u6bcf\u884c\u5e94\u80fd\u5bf9\u5e94\u5b98\u65b9\u5b9e\u4f53',
                    'official entity: MISS', '\u672a\u5224(\u65e0\u6cd5\u5b9a\u4f4d\u5b98\u65b9\u884c)',
                    'coverage-enumerate.py')
            continue
        idx = api_level_idx(off.get('rarity'))
        # ① 官方列逐值比（27 列）
        for ours_k, off_k in UNIT_OFFICIAL:
            vs = _official_unit_value(off, off_k, ours_k, idx, by_norm)
            if vs is None:
                continue
            ours = d.get(ours_k, '')
            if ours_k in UNIT_BOOL:
                ouv = (str(ours).strip() == '1')
                ok = (ouv == vs)
                ovs = '1' if vs else '0'
            elif ours_k in UNIT_STR:
                ouv = str(ours).strip()
                ok = (ouv == str(vs))
                ovs = str(vs)
            else:
                try:
                    ouv = int(ours)
                except Exception:
                    ouv = None
                ok = (ouv == int(vs))
                ovs = str(vs)
            R.add_m('S1\u6570\u503c', 'unit_cs:' + key, ours_k + ' \u2194 \u5b98\u65b9 ' + off_k,
                    ovs, '%s:%s = %s' % (off.get('name'), off_k, ovs),
                    'ours=%s official=%s' % (ours, ovs),
                    '\u4e00\u81f4' if ok else '\u4e0d\u4e00\u81f4(%s: \u6211\u4eec %s vs \u5b98\u65b9 %s)' % (off_k, ours, ovs),
                    'coverage-enumerate.py (cards_stats_* / rarities)')
        # ② 本项目自定列（登记为允许的差异，出处 = 差异登记 D22）
        for col in UNIT_CUSTOM:
            R.add_m('S1\u6570\u503c', 'unit_cs:' + key, col + ' (\u672c\u9879\u76ee\u81ea\u5b9a\u5217)',
                    '-', '\u672c\u9879\u76ee\u81ea\u5b9a\uff1a' + col,
                    'ours=%s' % d.get(col, ''),
                    '\u5141\u8bb8\u7684\u5dee\u5f02(\u672c\u9879\u76ee\u81ea\u5b9a\u5217)',
                    '\u5dee\u5f02\u767b\u8bb0 D22 / docs/\u6b65\u9aa4\u6587\u6863.md \u00a74.2')

def s1_spell_values(api):
    h, rows = read_src_table(os.path.join(NUMDIR, 'spell_cs.txt'))
    off_by_name = {}
    for c in (api.get('cards') or []):
        off_by_name[str(c.get('key', '')).lower()] = c
    for r in rows:
        d = dict(zip(h, r))
        key = d['key']
        R.add_e('S1\u6570\u503c', 'spell_cs:' + key, '\u7b56\u5212/\u6570\u503c\u6587\u6863/spell_cs.txt',
                spec_sec(6), 6, '\u53c2\u8003\u7269\u6bd4\u5bf9', 'AE1-S1')
        c = off_by_name.get(key.lower())
        if c is None:
            R.add_m('S1\u6570\u503c', 'spell_cs:' + key, 'key\u2194\u5b98\u65b9 cards.json',
                    'found/missing', 'key \u5fc5\u987b\u5728\u5b98\u65b9 cards.json', 'MISS',
                    '\u4e0d\u4e00\u81f4(key \u4e0d\u5b58\u5728)', 'coverage-enumerate.py')
            continue
        oc = c.get('elixir')
        if oc is None:
            oc = c.get('mana_cost')
        try:
            ou = int(d['elixir'])
        except Exception:
            ou = None
        R.add_m('S1\u6570\u503c', 'spell_cs:' + key, 'elixir \u2194\u5b98\u65b9 cards.json:elixir', str(oc),
                'cards.json:mana_cost = %s' % oc, 'ours=%s official=%s' % (ou, oc),
                '\u4e00\u81f4' if oc == ou else '\u4e0d\u4e00\u81f4(elixir)',
                'coverage-enumerate.py (cards.json)')

# ---------------------------------------------------------------- 差异登记（第三张表）

ALLOWED = [
    ['D5', '\u4e0d\u505a\u517b\u6210\uff08S29\u2013S31\uff09', '\u7528\u6237\u660e\u786e\u300c\u4e0d\u7528\u505a\u517b\u6210\u300d',
     '\u7528\u6237\u672c\u8f6e\u539f\u8bdd', '\u2014'],
    ['D2', '\u5361\u6c60 60 \u5f20\u800c\u975e\u539f\u7248 120+', '\u7528\u6237\u660e\u786e\u6307\u5b9a\u300c\u505a 60 \u5f20\u5178\u578b\u5361\u300d',
     '\u7528\u6237\u672c\u8f6e\u539f\u8bdd', '\u7528\u6237\u8981\u6c42\u6269\u5145\u65f6'],
    ['D3', '\u5854\u8840\u91cf\u53d6 hitpoints_per_level \u81ea\u6d3d\u89e3',
     '\u5b98\u65b9\u540c\u4e00\u6570\u636e\u6587\u4ef6\u5185 damage_per_level \u4e0e dps_per_level \u4e0d\u81ea\u6d3d',
     'cards_stats_projectile.json', '\u62ff\u5230\u66f4\u6743\u5a01\u53e3\u5f84\u65f6'],
    ['D4', '\u670d\u52a1\u7aef tick = 20 TPS\uff08\u975e 60\uff09',
     '\u5b98\u65b9\u6240\u6709\u65f6\u957f\u5747\u4e3a 50ms \u500d\u6570\uff0c20 TPS \u53ef\u6574\u9664',
     'cr-sim/cr_sim/engine/constants.py:34', '\u2014'],
    ['D21', 'S28 \u5355\u4f4d\u52a8\u753b\u5e27\u6bb5\u53ea\u63a5\u5165 3 \u4e2a\u76ee\u5f55',
     '\u539f\u59cb\u8d44\u6e90/sc/ \u53ea\u6709 3 \u4e2a .sc\uff0c\u6269\u5c55\u4e0d\u51fa\u522b\u7684\u76ee\u5f55',
     '\u7b56\u5212/\u5355\u4f4d\u5e27\u6bb5\u8868.md / \u9a8c\u6536\u8868 S28 D21', '\u62ff\u5230\u66f4\u591a .sc \u65f6'],
    ['D22', 'unit_cs \u7684 9 \u4e2a\u672c\u9879\u76ee\u81ea\u5b9a\u5217\uff08id/key/name_cn/kind/sprite_dir/summon_* \u5171 9 \u5217\uff09',
     '\u5217\u540d\u7531\u672c\u9879\u76ee\u5b9a\u4e49\uff08\u5b98\u65b9 JSON \u65e0\u540c\u540d\u5217\uff09\uff1b\u53d6\u503c\u53e3\u5f84\u5df2\u9010\u5217\u5199\u5165 unit_cs.txt \u8bf4\u660e\u884c',
     'docs/\u6b65\u9aa4\u6587\u6863.md \u00a74.2 / \u7b56\u5212/\u6570\u503c\u6587\u6863/unit_cs.txt \u8bf4\u660e\u884c', '\u7528\u6237\u8981\u6c42\u6269\u5145\u5217\u65f6'],
    ['D23', 'chr_goblin_archer_out\uff08342 \u5e27\uff09\u5728 .cs \u8bed\u6599\u91cc\u96f6\u5f15\u7528',
     '\u5047\u9633\u6027\uff1a\u8be5\u76ee\u5f55\u88ab unit_cs.txt \u7684 SpearGoblin \u884c sprite_dir \u6570\u636e\u5f15\u7528'
     '\uff08\u2192 server/game/table/tsv/unit.tsv\uff09\uff0c\u4e0d\u662f\u4ee3\u7801\u5b57\u9762\u91cf \u21d2 '
     'coverage-enumerate.py \u6309\u300c\u76ee\u5f55\u540d\u51fa\u73b0\u5728 .cs\u300d\u5224 \u21d2 \u6f0f\u5224\u6570\u636e\u9a71\u52a8\u5f15\u7528',
     'client/Assets/Resources/Sprites/Units/chr_goblin_archer_out + unit_cs.txt:SpearGoblin.sprite_dir',
     '\u679a\u4e3e\u811a\u672c\u53e3\u5f84\u6539\u4e3a\u300c\u4ee3\u7801 + \u914d\u8868\u300d\u53cc\u6e90\u626b\u63cf\u65f6'],
]

def build_diff():
    for r in ALLOWED:
        R.diff.append(r)

# ---------------------------------------------------------------- 主流程

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--no-ps', action='store_true')
    args = ap.parse_args()

    if not os.path.isdir(PLAN):
        sys.stderr.write('FATAL: plan dir missing: %s\n' % PLAN)
        return 2

    landed = d1_resources()
    d1_ui_per_frame()
    d1_sources()
    d1_respaths_keys()
    d1_source_vs_landed(landed)
    d1_unit_sprite_dirs()
    if not args.no_ps:
        run_check_ui_keys()

    d2_arena()
    d2_panels()

    d3(landed)

    api, chars, builds, projs, rar = s1_tables()
    s1_unit_values(api, chars, builds, projs, rar)
    s1_spell_values(api)
    build_diff()

    # ---- 写出（稳定排序）
    R.ent.sort(key=lambda r: (r[0], r[1], r[2]))
    R.mat.sort(key=lambda r: (r[0], r[1], r[2], r[3]))
    R.orphans.sort(key=lambda r: (r[0], r[1]))

    if not os.path.isdir(TMP):
        os.makedirs(TMP)

    with io.open(ENTITY_TSV, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\t'.join(['\u7ef4\u5ea6', '\u5b9e\u4f53', '\u8f7d\u4f53/\u8def\u5f84', '\u51fa\u5904',
                           '\u72b6\u6001\u6570', '\u5224\u636e\u7c7b\u578b', '\u5f52\u5c5e\u7247']) + '\n')
        for r in R.ent:
            f.write('\t'.join(x.replace('\t', ' ') for x in r) + '\n')

    with io.open(MATRIX_TSV, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\t'.join(['\u7ef4\u5ea6', '\u5b9e\u4f53', '\u72b6\u6001/\u4e8b\u4ef6', '\u8fb9\u754c\u503c',
                           '\u671f\u671b\u8868\u73b0(\u51fa\u5904)', '\u5b9e\u6d4b', '\u7ed3\u8bba', '\u8bc1\u636e']) + '\n')
        for r in R.mat:
            f.write('\t'.join(x.replace('\t', ' ') for x in r) + '\n')

    with io.open(DIFF_TSV, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\t'.join(['\u7f16\u53f7', '\u662f\u4ec0\u4e48', '\u4e3a\u4ec0\u4e48', '\u51fa\u5904',
                           '\u4f55\u65f6\u6d88\u9664']) + '\n')
        for r in R.diff:
            f.write('\t'.join(r) + '\n')

    with io.open(ORPHANS, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\t'.join(['\u5b9e\u4f53', '\u8def\u5f84', '\u6587\u4ef6\u6570', '\u95ee\u9898',
                           '\u5224\u636e', '\u4e25\u91cd\u5ea6']) + '\n')
        for r in R.orphans:
            f.write('\t'.join(r) + '\n')

    per_dim_e = {}
    for r in R.ent:
        per_dim_e[r[0]] = per_dim_e.get(r[0], 0) + 1
    per_dim_m = {}
    for r in R.mat:
        per_dim_m[r[0]] = per_dim_m.get(r[0], 0) + 1
    vd = {}
    for r in R.mat:
        vd[r[6]] = vd.get(r[6], 0) + 1
    nonagree = sum(1 for r in R.mat if r[6].startswith('\u4e0d\u4e00\u81f4'))
    unjudged = sum(1 for r in R.mat if r[6].startswith('\u672a\u5224') or r[6].startswith('\u963b\u585e'))
    dims_covered = sorted(per_dim_e.keys())
    dims_missing = [d for d in DIMS if d not in per_dim_e]

    with io.open(SUMMARY, 'w', encoding='utf-8', newline='\n') as f:
        f.write('AE1 \u679a\u4e3e\u6c47\u603b\uff08coverage-enumerate.py\uff09\n')
        f.write('root=%s\n' % ROOT)
        f.write('\u5b9e\u4f53\u6e05\u5355\u884c\u6570 = %d\n' % len(R.ent))
        for d in DIMS:
            if d in per_dim_e:
                f.write('  %-8s = %d\n' % (d, per_dim_e[d]))
        f.write('\u72b6\u6001\u77e9\u9635\u884c\u6570 = %d\n' % len(R.mat))
        for d in DIMS:
            if d in per_dim_m:
                f.write('  %-8s = %d\n' % (d, per_dim_m[d]))
        f.write('\u7ed3\u8bba\u76f4\u65b9\u56fe:\n')
        for k in sorted(vd):
            f.write('  %s = %d\n' % (k, vd[k]))
        f.write('\u4e0d\u4e00\u81f4\u884c\u6570 = %d\n' % nonagree)
        f.write('\u672a\u5224/\u963b\u585e\u884c\u6570 = %d\n' % unjudged)
        f.write('\u5df2\u8986\u76d6\u7ef4\u5ea6 = %s\n' % ','.join(dims_covered))
        f.write('\u7f3a\u5931\u7ef4\u5ea6(%d/15) = %s\n' % (len(dims_missing), ','.join(dims_missing)))
        f.write('\u76d8\u4e0a\u672a\u88ab\u5f15\u7528(\u5b64\u513f) = %d\n' % len(R.orphans))
        for n in R.note:
            f.write('note: %s\n' % n)
    print(rd_text(SUMMARY))
    return 0

if __name__ == '__main__':
    sys.exit(main())
