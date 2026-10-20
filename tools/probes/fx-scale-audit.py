# 特效缩放系数查表自检（判据资产，⛔ 不许删）
# 列出客户端实际用到、但 EffectsView.SegmentScale 查不到的 (用途目录, 起始原版帧号)
# —— 查不到 => 该段按 1.0 播（= 只有原版一半大）。
# 口径 = EffectsView.SegmentScaleFor：(1) 起始帧恰好相等；(2) 落在某段范围内且 First 最大者。
# 用法：python tools/probes/fx-scale-audit.py   （退出码 1 = 有未登记段）
import io
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8')
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
CLIENT = os.path.join(ROOT, 'client', 'Assets', 'Scripts')
RES = os.path.join(CLIENT, 'Core', 'ResPaths.cs')
VIEW = os.path.join(CLIENT, 'View')


def lines_of(p):
    return io.open(p, encoding='utf-8').read().split('\n')


def load_consts():
    out = {}
    for line in lines_of(RES):
        m = re.match(r'^\s*public const (?:int|string|float) (\w+)\s*=\s*(.+?);\s*$', line)
        if not m:
            continue
        v = m.group(2).strip()
        if v.startswith('"'):
            out[m.group(1)] = v.strip('"')
        else:
            try:
                out[m.group(1)] = int(v) if '.' not in v else float(v)
            except ValueError:
                pass
    return out


def load_table():
    """EffectsView.SegmentScale 的表项：用途目录 -> [(first, count, scale, 表里的行号)]。"""
    table = {}
    cur = None
    for i, line in enumerate(lines_of(os.path.join(VIEW, 'EffectsView.cs'))):
        m = re.search(r'\{\s*"([^"]+)"\s*,\s*new\[\]', line)
        if m:
            cur = m.group(1)
            table.setdefault(cur, [])
        m2 = re.search(r'Seg\(\s*(-?\d+)\s*,\s*(\d+)\s*,\s*([0-9.]+)f?\s*,', line)
        if m2 and cur:
            table[cur].append((int(m2.group(1)), int(m2.group(2)), float(m2.group(3)), i + 1))
    return table


def lookup(table, use, first):
    """= EffectsView.SegmentScaleFor 的同一口径。"""
    segs = table.get(use)
    if not segs:
        return None
    for s in segs:
        if s[0] == first and s[2] > 0:
            return s
    best = None
    for s in segs:
        if s[2] <= 0 or first < s[0] or first >= s[0] + s[1]:
            continue
        if best is None or s[0] > best[0]:
            best = s
    return best


def resolve(consts, tok):
    """`ResPaths.X` / `396` / `ResPaths.X + 1` / `ResPaths.X - 1` ⇒ 数值。"""
    tok = tok.strip()
    m = re.match(r'^ResPaths\.(\w+)$', tok)
    if m:
        return consts.get(m.group(1))
    m = re.match(r'^ResPaths\.(\w+)\s*([+-])\s*(\d+)$', tok)
    if m:
        base = consts.get(m.group(1))
        if isinstance(base, int):
            return base + int(m.group(3)) if m.group(2) == '+' else base - int(m.group(3))
        return None
    try:
        return int(tok)
    except ValueError:
        return None


def scan(consts):
    """扫客户端里 (Use = …, First = …) 的组合 => 实际用到的 (用途目录, 起始帧)。"""
    hits = {}
    for base, _d, files in os.walk(CLIENT):
        for f in files:
            if not f.endswith('.cs'):
                continue
            src = lines_of(os.path.join(base, f))
            for i, line in enumerate(src):
                mu = re.search(r'Use\s*=\s*ResPaths\.(\w+)', line)
                if not mu:
                    continue
                use = consts.get(mu.group(1))
                if not isinstance(use, str):
                    continue
                for j in range(i, min(len(src), i + 14)):
                    mf = re.search(r'First\s*=\s*([^,\n]+)', src[j])
                    if not mf:
                        continue
                    first = resolve(consts, mf.group(1))
                    if not isinstance(first, int):
                        break
                    hits.setdefault((use, first), f + ':' + str(j + 1))
                    break
    return hits


def main():
    consts = load_consts()
    table = load_table()
    miss = 0
    hits = scan(consts)
    for (use, first), where in sorted(hits.items()):
        s = lookup(table, use, first)
        if s is None:
            miss += 1
            print('MISS %-14s f%-5d %-26s => 1.0 (未登记)' % (use, first, where))
        else:
            print('ok   %-14s f%-5d %-26s x%.4f (表项 EffectsView.cs:%d)'
                  % (use, first, where, s[2], s[3]))
    print('MISS_TOTAL=%d / TOTAL=%d' % (miss, len(hits)))
    return 1 if miss else 0


if __name__ == '__main__':
    sys.exit(main())
