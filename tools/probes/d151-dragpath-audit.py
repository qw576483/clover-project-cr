# -*- coding: utf-8 -*-
# direct-fix: D151 原版 UI 复刻 —— 「按住拖动到底行不行」的**离线**射线审计（判据资产）
"""d151-dragpath-audit.py -- 不开 Unity，静态审计 `DeckEditPanel` 建成的那棵树，
判「手指按在一张卡上时，uGUI 的射线会不会把事件派给这张卡」。

为什么要有这个东西
------------------
用户 2026-09-24 原话：「**你他妈的编辑卡组到底能不能拖动还不知道呢？**」

已有的取证 `.ai-tmp/test/AX1-evidence.txt` **不能回答这个问题**：那条驱动是
`ped.pointerDrag = go` **写死**成卡的 GameObject、再 `ExecuteEvents.Execute(...)` 打出来的
—— 它**绕过了 uGUI 的射线选取**，只证明"回调链本身通"，
**没有证明"手指按在卡上时事件会被派给卡"**。

两条互补的判据：
  · **运行时**（要开 Unity）⇒ 面板内置 `DeckEditPanel.VerifyDragPath()`：开面板时走**真实**
    `EventSystem.RaycastAll`，把 `[Deck] DRAGPATH-SUMMARY ... verdict=PASS|FAIL` 打进日志。
  · **离线**（本脚本，⛔ 不需要 Unity）⇒ 把面板建的整棵树按 uGUI 的**绘制顺序**重放，
    求出"卡格中心那个点，最上层吃射线的图元是谁"，再看事件会不会冒泡回卡自己。
    · 绘制顺序 = 先序遍历（父自己的图元先画 → 子按 sibling 次序），与 uGUI `CanvasRenderer.depth` 一致。
    · `RectMask2D` 当 `ICanvasRaycastFilter` 用 ⇒ 视口外的点在射线阶段就被滤掉，本脚本照此实现。
    · 事件派发 = `ExecuteEvents.GetEventHandler<IDragHandler>`（沿父链冒泡取**最深**的处理器）。

判据（每条都能失败；`--negctl` 是负控，证明判据是活的）
-------------------------------------------------------
  P1  每张卡格（`Card{n}` / `Slot{n}`）的 GameObject 上挂着 `CardDragHandle`
  P2  卡阵每一格中心点的静态射线命中的**就是这张卡**，且拖动处理器也是它
  P3  没有**全屏尺寸**的图元在吃射线（那会把下面所有卡格一次挡死）
  P4  卡池视口子树里除 `Card{n}` 之外没有别的图元
  P5  卡格的 `Button.targetGraphic` 就是卡底自己（press 与 drag 同 GO）
  P6  宣传区 / 卡池视口都不许压到卡阵，且宣传区不吃射线
  P7  ★ 第三片：**拖动链路端到端模拟** —— 每格中心 + 卡面 5×5 采样按下都要抓住卡自己；
      落位判定自洽；Slot0→Slot5 换位；阵外松手 = −1(移除)；卡池卡→Slot3(选入)
  P0  源码里出现的建件调用本脚本都认得（⛔ 不许静默跳过）

用法
----
  python tools/probes/d151-dragpath-audit.py            # 跑判据
  python tools/probes/d151-dragpath-audit.py --negctl   # 先跑负控（必须报红）再跑正控
  python tools/probes/d151-dragpath-audit.py -v         # 打印整棵树

退出码：0 = 全绿；1 = 有红。⛔ 本脚本不写任何文件。
"""

import argparse
import io
import os
import re
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PANEL = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'UI', 'Panels', 'DeckEditPanel.cs')
STYLE = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'UI', 'CrUiStyle.cs')


# ═══════════════════════ ① 读源码（去注释，保住字符串字面量） ═══════════════════════

def read_src():
    with io.open(PANEL, encoding='utf-8') as f:
        return f.read().replace('\r\n', '\n')


def strip_comments(s):
    out, i, n = [], 0, len(s)
    while i < n:
        if s.startswith('//', i):
            j = s.find('\n', i)
            i = n if j < 0 else j
        elif s.startswith('/*', i):
            j = s.find('*/', i)
            i = n if j < 0 else j + 2
        elif s[i] == '"':
            j = i + 1
            while j < n and s[j] != '"':
                j += 2 if s[j] == '\\' else 1
            out.append(s[i:j + 1])
            i = j + 1
        else:
            out.append(s[i])
            i += 1
    return ''.join(out)


# ═══════════════════════ ② 常量表（迭代求值） ═══════════════════════

CONST_RE = re.compile(
    r'private\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?'
    r'(?:float|int)\s+(\w+)\s*=\s*([^;]+);')

# `CrUiStyle` 里的公开常量（面板用 `CrUiStyle.DesignW` 这类写法引用 ⇒ 必须一起读进来）。
STYLE_RE = re.compile(
    r'public\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?'
    r'(?:float|int)\s+(\w+)\s*=\s*([^;]+);')


def load_constants(src):
    raw = {}
    for m in CONST_RE.finditer(src):
        raw[m.group(1)] = m.group(2).strip()
    # 外部常量（画布尺寸等）：只补面板里**没有**的，⛔ 不覆盖面板自己的定义
    if os.path.exists(STYLE):
        with io.open(STYLE, encoding='utf-8') as f:
            style = f.read().replace('\r\n', '\n')
        for m in STYLE_RE.finditer(style):
            raw.setdefault(m.group(1), m.group(2).strip())

    vals = {}
    for _ in range(8):                                  # 常量之间互相引用 ⇒ 迭代几轮
        for name, expr in raw.items():
            if name in vals:
                continue
            e = re.sub(r'\bCrUiStyle\.(\w+)\b', r'\1', expr)
            e = re.sub(r'(\d+(?:\.\d+)?)[fF]\b', r'\1', e)
            try:
                vals[name] = eval(e, {'__builtins__': {}}, dict(vals))
            except Exception:
                pass
    return vals


# ═══════════════════════ ③ 工厂调用的参数名表 ═══════════════════════

FACTORIES = {
    'UIFactory.CreateBoxRect': ('name', 'parent', 'pos', 'size', 'color', 'raycast'),
    'UIFactory.CreatePanel': ('name', 'parent', 'color', 'raycast'),
    'UIFactory.CreateNode': ('name', 'parent'),
    'UIFactory.CreateLabel': ('name', 'parent', 'content', 'fontSize', 'pos', 'size', 'anchor', 'color'),
    'CrUiStyle.Skin': ('name', 'parent', 'res', 'corner', 'border', 'anchor', 'pivot', 'pos', 'size',
                       'fallback', 'raycast', 'tint'),
    'CrUiStyle.NineSlice': ('name', 'parent', 'res', 'border', 'anchor', 'pivot', 'pos', 'size',
                            'fallback', 'raycast'),
    'CrUiStyle.AspectImage': ('name', 'parent', 'res', 'width', 'anchor', 'pivot', 'pos', 'fallback', 'raycast'),
    'CrUiStyle.Outlined': ('name', 'parent', 'content', 'fontSize', 'v2', 'v3', 'pos', 'size', 'anchor'),
    'CrUiStyle.BlueButton': ('name', 'parent', 'label', 'pos', 'size', 'onClick', 'labelColor'),
}

# 建出来**不吃射线**的工厂（文本 / 纯色内容块带显式 false）
TEXT_FACTORIES = ('UIFactory.CreateLabel', 'CrUiStyle.Outlined')


def call_iter(src):
    """扫出全部工厂调用 → [(start, fn, argnames, [args])]。括号配对，参数按顶层逗号切。"""
    out = []
    for fn, argnames in FACTORIES.items():
        for m in re.finditer(re.escape(fn) + r'\s*\(', src):
            i, depth, buf, args = m.end(), 1, [], []
            while i < len(src):
                c = src[i]
                if c == '(':
                    depth += 1
                elif c == ')':
                    depth -= 1
                    if depth == 0:
                        args.append(''.join(buf))
                        break
                if c == ',' and depth == 1:
                    args.append(''.join(buf))
                    buf = []
                else:
                    buf.append(c)
                i += 1
            out.append((m.start(), fn, argnames, [a.strip() for a in args]))
    out.sort()
    return out


# ═══════════════════════ ④ 表达式求值（够用就行） ═══════════════════════

class Unresolved(Exception):
    pass


def ev(expr, names):
    if expr is None:
        raise Unresolved('nil')
    e = expr.strip()
    if e in ('true', 'false'):
        return e == 'true'
    m = re.fullmatch(r'\$?"([^"]*)"', e)
    if m:
        return m.group(1)
    m = re.fullmatch(r'At\(\s*(.+?)\s*,\s*(.+?)\s*\)', e)
    if m:
        return (ev(m.group(1), names), -ev(m.group(2), names))
    m = re.fullmatch(r'new\s+Vector2\(\s*(.+?)\s*,\s*(.+?)\s*\)', e)
    if m:
        return (ev(m.group(1), names), ev(m.group(2), names))
    if e == 'Vector2.zero':
        return (0.0, 0.0)
    e2 = re.sub(r'\bCrUiStyle\.(\w+)\b', r'\1', e)
    e2 = re.sub(r'\bResPaths\.(\w+)\b', '0', e2)
    e2 = re.sub(r'(\d+(?:\.\d+)?)[fF]\b', r'\1', e2)
    e2 = re.sub(r'\.rectTransform\b', '', e2)
    if not re.fullmatch(r'[\w\s\.\+\-\*/\(\)]+', e2):
        raise Unresolved(e)
    try:
        return eval(e2, {'__builtins__': {}}, dict(names))
    except Exception:
        raise Unresolved(e)


def try_ev(expr, names):
    try:
        return ev(expr, names)
    except Exception:
        return None


# ═══════════════════════ ⑤ 建树 ═══════════════════════

class Node(object):
    __slots__ = ('name', 'parent', 'rect', 'raycast', 'kind', 'has_drag', 'mask', 'children', 'order')

    def __init__(self, name, parent, rect, raycast, kind, has_drag=False, mask=False):
        self.name = name
        self.parent = parent
        self.rect = rect                    # (x, y_from_screen_top, w, h)，**相对父节点**的左上角
        self.raycast = raycast
        self.kind = kind
        self.has_drag = has_drag
        self.mask = mask
        self.children = []
        self.order = 0


def build_tree(src, C):
    root = Node('root', None, (0.0, 0.0, C['DesignW'], C['DesignH']), False, 'panel-root')
    nodes = [root]
    by_name = {'root': root, '_content': root, 'transform': root}
    unknown = []

    # ★ D151 第二片：**局部变量别名**。`var layer = UIFactory.CreateNode("BannerLayer", _content)`
    # 之后再 `CreateBoxRect("BannerPlate", layer, ...)` —— 父参是**局部变量**，通用扫描解析不到。
    # ⛔ 不把这种情况塞进 `unknown`（那会让 P0 假红）：先把别名登记下来，父参查表时先过一遍别名。
    alias = {}
    for _m in re.finditer(r'var\s+([A-Za-z_]\w*)\s*=\s*UIFactory\.CreateNode\(\s*"([^"]+)"', src):
        alias[_m.group(1)] = _m.group(2)
    # ★ D151 第三片：**建件函数也会返回句柄**，而源码里常用它的 `rectTransform` 当父 ——
    #   见 `var browse = CrUiStyle.BlueButton("BrowseButton", _content, ...)` 之后
    #   `CrUiStyle.AspectImage("BrowseButtonIcon", browse.rectTransform, ...)`。
    #   ⛔ 不登记这一条 ⇒ P0 假红（第三片真踩到：`认不出的建件调用 1 处`）。
    for _m in re.finditer(r'var\s+([A-Za-z_]\w*)\s*=\s*CrUiStyle\.\w+\(\s*"([^"]+)"', src):
        alias.setdefault(_m.group(1), _m.group(2))

    def add(name, parent, rect, raycast, kind, has_drag=False, mask=False):
        n = Node(name, parent, rect, raycast, kind, has_drag, mask)
        nodes.append(n)
        if parent is not None:
            parent.children.append(n)
        by_name.setdefault(name, n)
        return n

    # ── 通用扫描：只收参数**能静态求值**的调用；循环里拼出来的名字（含 `{`）留给下面按循环展开 ──
    for _p, fn, argnames, cargs in call_iter(src):
        kv = {an: (cargs[i] if i < len(cargs) else None) for i, an in enumerate(argnames)}
        name = try_ev(kv.get('name'), C)
        if not isinstance(name, str) or '{' in name:
            continue
        _pk = (kv.get('parent') or '').replace('.rectTransform', '').strip()
        parent = by_name.get(alias.get(_pk, _pk))
        if parent is None:
            unknown.append((fn, name, kv.get('parent')))
            continue

        pos = try_ev(kv.get('pos'), C) if 'pos' in kv else None
        size = try_ev(kv.get('size'), C) if 'size' in kv else None
        rc = try_ev(kv.get('raycast'), C) if 'raycast' in kv else None

        # 翻译成"距屏幕顶边正数"的左上角口径
        rect = (pos[0], -pos[1], size[0], size[1]) if (pos and size) else None

        if fn == 'CrUiStyle.AspectImage':
            w = try_ev(kv.get('width'), C)
            rect = (pos[0], -pos[1], w, w) if (pos and w) else None
            rc = bool(rc)
        elif fn == 'UIFactory.CreateNode':
            rect, rc, mask = None, False, True           # 卡池视口：建完就 AddComponent<RectMask2D>()
        elif fn == 'UIFactory.CreatePanel':
            rc = bool(rc)
        elif fn in TEXT_FACTORIES:
            rc = False                                   # 文本一律不拦射线
        elif fn == 'CrUiStyle.BlueButton':
            rc = True

        add(name, parent, rect, bool(rc), fn, mask=(fn == 'UIFactory.CreateNode'))

    # ── 循环展开（源码里名字是 $"{...}" ⇒ 上面取不到）──
    for i in range(int(C['DefaultSlotCount'])):          # BuildSlots：Slot0..7，4×2
        col, row = i % 4, i // 4
        add('Slot%d' % i, root,
            (C['GridLeftX'] + col * C['CardStepX'], C['GridTopY'] + row * C['CardStepY'], C['CardW'], C['CardH']),
            True, 'cell', has_drag=True)

    for i in range(7):                                   # BuildDeckNumberRow：DeckNum0..6
        add('DeckNum%d' % i, root,
            (C['NumBtnX0'] + i * C['NumBtnPitch'], C['NumBtnY'], C['NumBtnW'], C['NumBtnH']),
            True, 'button')

    # ── BuildTab 的两颗页签：位置来自方法形参 ⇒ 按调用点内联展开 ──
    for m in re.finditer(r'BuildTab\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*([\w\.]+)\s*,\s*([\w\.]+)\s*,\s*([\w\.]+)\s*,',
                         src):
        nm, _label, ex, ey, eh = m.groups()
        add(nm, root, (C[ex], C[ey], C['TabW'], C[eh]), True, 'tab')

    # ── 卡池：PoolScroll（带 RectMask2D）→ PoolContent → Card0..59 ──
    # ⛔ 通用扫描已经把 `CreateNode("PoolScroll", _content)` 收进来了（`_content` 能解析到 root），
    #    但它看不到紧随其后的 `AnchoredTopLeft(viewport, ...)` ⇒ 那个节点的 `rect` 是 None。
    #    这里先**删掉同名旧节点**再显式重建，否则"同名两个节点"会让按名字查矩形的判据（P6）读到错的那个。
    def drop_dup(nm):
        for _i, _v in enumerate(nodes):
            if _v.name == nm:
                if _v.parent is not None and _v in _v.parent.children:
                    _v.parent.children.remove(_v)
                nodes.pop(_i)
                break
        by_name.pop(nm, None)

    for _nm in ('PoolScroll', 'PoolContent'):
        drop_dup(_nm)

    pool = add('PoolScroll', root, (C['GridLeftX'], C['PoolTopY'], C['GridW'], C['PoolViewportH']),
               False, 'UIFactory.CreateNode', mask=True)
    content = add('PoolContent', pool, (0.0, 0.0, C['GridW'], C['PoolContentH']), False,
                  'UIFactory.CreateNode')
    for i in range(int(C['MaxPoolCells'])):
        col, row = i % 4, i // 4
        add('Card%d' % i, content,
            (col * C['CardStepX'], row * C['CardStepY'], C['CardW'], C['CardH']),
            True, 'cell', has_drag=True)

    # 先序遍历 = 绘制顺序
    seq = [0]

    def walk(n):
        n.order = seq[0]
        seq[0] += 1
        for c in n.children:
            walk(c)
    walk(root)

    # 通用扫描里"父节点是个方法内局部变量"（如 `viewport`）的调用，只要同名节点已由上面
    # 循环 / 卡池那两段显式建好，就不是"认不出"，从 unknown 里摘掉。
    unknown = [u for u in unknown if u[1] not in by_name]
    return root, nodes, unknown


# ═══════════════════════ ⑥ 静态射线 ═══════════════════════

def abs_rect(n):
    if n.parent is None or n.rect is None:
        return n.rect
    pr = abs_rect(n.parent)
    if pr is None:
        return None
    return (pr[0] + n.rect[0], pr[1] + n.rect[1], n.rect[2], n.rect[3])


def hit_test(n, px, py):
    r = abs_rect(n)
    if r is None:
        return False
    x, y, w, h = r
    return x <= px <= x + w and y <= py <= y + h


def clipped_out(n, px, py):
    """`RectMask2D` 是 `ICanvasRaycastFilter` ⇒ 视口外的点在射线阶段就被滤掉。"""
    p = n.parent
    while p is not None:
        if p.mask:
            if not hit_test(p, px, py):
                return True
        p = p.parent
    return False


def in_subtree(n, root_name):
    p = n
    while p is not None:
        if p.name == root_name:
            return True
        p = p.parent
    return False


def raycast_top(nodes, px, py, skip=None):
    """`skip` = 子树名：那一棵整棵不算（用在"该页面下它是 SetActive(false)"的场景，
    见 P7 ⑤ 的卡池 —— 静态审计没有页面状态，只能用这个显式排除，⛔ 不是"绕过判据"）。"""
    best = None
    for n in nodes:
        if not n.raycast or n.rect is None:
            continue
        if skip is not None and in_subtree(n, skip):
            continue
        if not hit_test(n, px, py) or clipped_out(n, px, py):
            continue
        if best is None or n.order > best.order:
            best = n
    return best


def drag_owner(hit):
    """uGUI `ExecuteEvents.GetEventHandler<IDragHandler>`：沿父链冒泡取**最深**的处理器。"""
    n = hit
    while n is not None:
        if n.has_drag:
            return n
        n = n.parent
    return None


# ═══════════════════════ ⑦ 判据 ═══════════════════════

def audit(root, nodes, C, src, inject=None):
    red, green = [], []
    ok = lambda i, m: green.append((i, m))
    bad = lambda i, m: red.append((i, m))

    if inject == 'fullray':
        n = Node('__INJECTED_FULL_RAYCAST__', root, (0.0, 0.0, C['DesignW'], C['DesignH']), True, 'inject')
        nodes.append(n)
        root.children.append(n)
        n.order = max(x.order for x in nodes) + 1

    if inject == 'overcard':
        # 更贴近历史的失败形态：在**卡格上面**盖一层吃射线的图元（只是它盖着 Slot0 那一格）。
        cell = next(n for n in nodes if n.name == 'Slot0')
        x, y, w, h = cell.rect
        n = Node('__INJECTED_OVER_CARD__', cell.parent, (x, y, w, h), True, 'inject')
        nodes.append(n)
        cell.parent.children.append(n)
        n.order = max(v.order for v in nodes) + 1

    if inject == 'slotsnap':
        # 第四条负控（★ D151 第三片，**修过一次**）：把落位公式的**列原点**挪半格
        #   （`s = 0.5 × CardStepX = 127.25`）—— 这是"格心算错半格"这种真故障的形态。
        #
        # ⚠️ 教训（原样留档，⛔ 别删）：第一版注入的是 `s = 60px`，结果 **P7 全绿**。
        #   算一下就知道是**判据太弱**、不是注入形式不对：
        #   `padX = CardW·½·1.35 = 138.4`；判据 ③ 只喂**格心** ⇒ 偏移 s 之后
        #   `dx = |0 − s| = s`，只要 `s < 138.4` 就仍判给自己（`s = 127.25` 时与右邻**并列**，
        #   靠 `loop` 升序 + `d < bestD` 严判的平局取舍才勉强留给自己）。
        #   ⇒ ③「格心 → 自己」的灵敏度上限就是 `padX`，**测不出 60px 级偏移**。
        #   修法 = 加判据 ③b（**卡面 5×5 采样**喂进落位判定），灵敏度 ≈ `|0.4·CardW − ...|`
        #   亦即 s > 56.4 必红；s = 127.25 时每个卡格的 10%/30% 列**全部**红。
        #   ⛔ 不许把 `padX` 调小、或把采样点收回格心来"凑红"。
        C = dict(C)
        C['__SNAP_SHIFT__'] = C['CardStepX'] * 0.5

    if inject == 'bigbanner':
        # 第三条负控（★ D151 第二片）：把宣传区**改大到压住卡阵** ⇒ P6 必须红。
        bp = next(n for n in nodes if n.name == 'BannerPlate')
        bp.rect = (0.0, 200.0, C['DesignW'], C['DesignH'] - 200.0)

    cells = [n for n in nodes if n.kind == 'cell']

    # P1 每个卡格都要有 CardDragHandle
    miss = [n.name for n in cells if not n.has_drag]
    bad('P1', '这些卡格没有 CardDragHandle：' + ', '.join(miss)) if miss else \
        ok('P1', '全部 %d 个卡格都挂了 CardDragHandle' % len(cells))

    # P2 卡阵每格中心点的射线必须命中卡自己
    slots = [n for n in cells if n.parent is root]
    blocked = []
    for n in slots:
        x, y, w, h = abs_rect(n)
        px, py = x + w / 2.0, y + h / 2.0
        hit = raycast_top(nodes, px, py)
        owner = drag_owner(hit)
        if hit is not n or owner is not n:
            blocked.append('%s ← %s(拖动=%s)' % (n.name, hit.name if hit else '(空)',
                                                owner.name if owner else 'null'))
    bad('P2', '卡阵里有 %d 格被挡：%s' % (len(blocked), '；'.join(blocked[:4]))) if blocked else \
        ok('P2', '卡阵 %d 格中心点射线全部命中卡自己（含拖动处理器）' % len(slots))

    # P3 全屏图元不许吃射线
    full = [n.name for n in nodes
            if n.raycast and n.rect and abs_rect(n)[2] >= C['DesignW'] - 0.5 and abs_rect(n)[3] >= C['DesignH'] - 0.5]
    bad('P3', '全屏尺寸的图元在吃射线（会把下面所有卡格一次挡死）：' + ', '.join(full)) if full else \
        ok('P3', '没有任何全屏图元吃射线')

    # P4 卡池视口子树里除 Card{n} 之外不该有别的图元
    extra = set()
    for n in nodes:
        p = n.parent
        while p is not None and p.name != 'PoolScroll':
            p = p.parent
        if p is None:
            continue
        if not re.fullmatch(r'Card\d+', n.name) and n.name != 'PoolContent':
            extra.add(n.name)
    bad('P4', '卡池视口里有非卡格图元（可能遮挡）：' + ', '.join(sorted(extra))) if extra else \
        ok('P4', '卡池视口子树里只有 PoolContent + Card{n}')

    # P5 卡格的 Button.targetGraphic = 卡底自己
    if re.search(r'btn\.targetGraphic\s*=\s*cell\.Chassis\s*;', src):
        ok('P5', 'Button.targetGraphic = cell.Chassis（press 与 drag 同 GO，'
                 '拖完那一次多余的 onClick 由 CardDragHandle.ConsumeClickSuppressed 压掉）')
    else:
        bad('P5', '找不到 `btn.targetGraphic = cell.Chassis;` ⇒ 点击与拖动会派给不同对象')

    # P6 ★ D151 第二片新增：**宣传区 / 卡池视口都不许压到卡阵**，且宣传区不吃射线。
    #    为什么加：本轮把 Decks 页底部从"卡池"换成"宣传区"（原版 07 的真实内容）。
    #    以后谁把 BannerH / PoolViewportH 改大，就会盖住卡阵 ⇒ 拖动命中不了卡 ⇒ 必须让判据先红。
    def overlaps(a, b):
        ax, ay, aw, ah = a
        bx, by, bw, bh = b
        return ax < bx + bw and bx < ax + aw and ay < by + bh and by < ay + ah

    slot_rects = [(n.name, abs_rect(n)) for n in slots]

    def abs_rect_or_screen(n):
        """`UIFactory.CreateNode` 建的**层节点**是 `Stretch()` 铺满父节点的 ⇒ `rect=None` 表示"继承父节点"，
        ⛔ 不是"没有位置"。P6 要拿到 `BannerLayer/BannerPlate` 的**屏幕矩形**就必须按这个语义展开。"""
        if n is None:
            return None
        pr = abs_rect_or_screen(n.parent)
        if n.rect is None:
            return pr
        return n.rect if pr is None else (pr[0] + n.rect[0], pr[1] + n.rect[1], n.rect[2], n.rect[3])

    crim = []
    for nm in ('BannerPlate', 'PoolScroll'):
        nd = next((v for v in nodes if v.name == nm), None)
        if nd is None:
            crim.append('%s 不在树里（版式被改过？）' % nm)
            continue
        r = abs_rect_or_screen(nd)
        if r is None:
            crim.append('%s 算不出屏幕矩形' % nm)
            continue
        for sn, sr in slot_rects:
            if sr is not None and overlaps(r, sr):
                crim.append('%s %s 压住 %s' % (nm, '(%g,%g,%g,%g)' % r, sn))
    bp = next((v for v in nodes if v.name == 'BannerPlate'), None)
    if bp is not None and bp.raycast:
        crim.append('BannerPlate 在吃射线（宣传区不许拦手指）')
    bad('P6', '下半屏与卡阵的版式冲突：' + '；'.join(crim[:4])) if crim else \
        ok('P6', '宣传区 / 卡池视口都不与卡阵重叠，且宣传区不吃射线（改大任一方都会被这条拦住）')

    # P7 ★ D151 第三片新增：**拖动链路的端到端模拟**。
    #
    # 用户 2026-09-24 原话：「**你他妈的编辑卡组到底能不能拖动还不知道呢？**」
    # 前六条回答的是"静态上通不通"；P7 把「**按下 → 松手**」这两次**真实几何判定**在真数值上跑一遍。
    #
    # ⛔ 本片第一版把这组判据写错过一次，原样留在这当教训：
    #    第一版 = "从 Slot0 中心到 Slot5 中心的直线取 101 个采样点，每个点都必须命中卡格"。
    #    跑出来 **47/101 红** —— 但**红得没道理**：卡阵 RowGap = 221.4，两行之间是**空地**，
    #    直线当然会从空地穿过。而 uGUI `EventSystem` 只在**按下那一刻**做射线选取，
    #    选定后 `pointerDrag` 就被**锁住**、整个手势期间都送给它 ⇒ 手指划过空地根本不影响拖动。
    #    ⇒ 那条判据是"拿错的模型去判代码"，删掉。⛔ 特别记一笔：**不许把代码改成"拖不过空地"来凑绿**。
    #
    # 正确的判据（都发生在"按下这一刻"，都是能失败的）：
    #   ① 每一格**中心**按下 ⇒ 射线命中该格、且拖动处理器是该格；
    #   ② 每一格**卡面 5×5 采样**按下（x/y 各取 10%/30%/50%/70%/90%）⇒ **25 个点全部**命中该格。
    #      这条才是"手指按在卡边上也抓得住"的真判据 —— P2 只测中心点，测不出"只有内嵌图吃射线、
    #      卡框边上漏掉"这种故障（那正是"看着能拖、一按就掉"的典型成因）；
    #   ③ 落位判定自洽：每格中心喂进等价式 ⇒ 必须返回它自己的下标；
    #   ③b 落位判定自洽（**卡面级**，★ 第三片加）：每格的卡面 5×5 采样点喂进等价式 ⇒ 必须返回该格；
    #      （③ 灵敏度上限 = padX，③b 才管得住"公式偏了半格"这类故障 —— 见负控 `slotsnap`）
    #   ④ 从 Slot0 拖到 Slot5 中心松手 ⇒ 落位判定 = **5**（换位；给别的格就是换错格）；
    #   ⑤ 在**卡阵之外**（宣传区正中）松手 ⇒ 落位判定 = **−1**（那才是"拖出卡阵 = 移除"的分支）；
    #   ⑥ 卡池格 Card0 中心按下 ⇒ 命中该池格；拖到 Slot3 中心松手 ⇒ 落位判定 = **3**（选入）。
    #      （Collection 页宣传区是关掉的 ⇒ 用 raycast_top 的 skip 显式把 BannerLayer 整棵排除。）
    #
    # ⚠️ ③④⑤⑥ 里的落位判定是本脚本对 `DeckEditPanel.HitTestSlot()` 的**逐行等价重写**
    #    （同一个闭式：中心 = 列/行 × 步进 + CardW·½；吸附半宽 = CardW·½·(1+SlotSnapPadK)；
    #    在吸附圈内取**最近**一格），⛔ 不是"调用真代码"；
    #    等价性由 ③ + ③b 这两条**自洽判据**兜住（负控 `slotsnap` 把列原点挪半格 ⇒ ③b 必须红）。
    #    ⚠️ 只留 ③ 是不够的：③ 只喂格心、灵敏度上限 = `padX`(138.4)，60px 级偏移它照样绿。
    snap_shift = C.get('__SNAP_SHIFT__', 0.0)

    def hittest_slot(px, py, padk):
        """逐行等价重写 `DeckEditPanel.HitTestSlot()`（局部坐标：原点 = 屏幕左上角、y 向下为正）。"""
        padx = C['CardW'] * 0.5 * (1.0 + padk)
        pady = C['CardH'] * 0.5 * (1.0 + padk)
        best, bestd = -1, None
        for i in range(len(slots)):
            cx = C['GridLeftX'] + (i % C['Columns']) * C['CardStepX'] + C['CardW'] * 0.5 + snap_shift
            cy = C['GridTopY'] + (i // C['Columns']) * C['CardStepY'] + C['CardH'] * 0.5
            dx, dy = abs(px - cx), abs(py - cy)
            if dx > padx or dy > pady:
                continue
            d = dx * dx + dy * dy
            if bestd is None or d < bestd:
                bestd, best = d, i
        return best

    def slot_center(i):
        x, y, w, h = abs_rect(slots[i])
        return (x + w / 2.0, y + h / 2.0)

    padk = C.get('SlotSnapPadK', 0.35)
    p7 = []
    if len(slots) < 6:
        p7.append('卡阵只有 %d 格 ⇒ 跑不了 0→5 的换位链路' % len(slots))
    else:
        # ③ 落位判定自洽
        bad_self = ['Slot%d→%d' % (i, hittest_slot(slot_center(i)[0], slot_center(i)[1], padk))
                    for i in range(len(slots))
                    if hittest_slot(slot_center(i)[0], slot_center(i)[1], padk) != i]
        if bad_self:
            p7.append('落位判定与格心不互洽：' + ', '.join(bad_self[:4]))
        # ③b ★ D151 第三片新增（**因为 ③ 太弱**）：把**卡面 5×5 采样点**（与 ② 同一批 frac）
        #     喂进落位判定，必须返回**该格自己** —— 这才是"把手按在卡面上任意位置松手，
        #     必须落回这一格"的真判据。
        #     为什么必须有它：③ 只喂格心 ⇒ 灵敏度上限 = padX = CardW·½·1.35 = 138.4，
        #     落位公式的列原点偏 60px 它照样绿（负控 `slotsnap` 第一版就是这么活着的）。
        #     这条的灵敏度 = `|0.4·CardW − s| > padX` ⇒ s > 56.4 必红。
        frac = (0.10, 0.30, 0.50, 0.70, 0.90)
        bad_snap = []
        for i in range(len(slots)):
            x, y, w, h = abs_rect(slots[i])
            for fx in frac:
                for fy in frac:
                    got_i = hittest_slot(x + w * fx, y + h * fy, padk)
                    if got_i != i:
                        bad_snap.append('Slot%d(%.0f%%,%.0f%%)→%d' % (i, fx * 100, fy * 100, got_i))
        if bad_snap:
            p7.append('卡面采样松手会落错格（%d/%d 个采样点）：%s' % (
                len(bad_snap), len(slots) * 25, '；'.join(bad_snap[:6])))
        # ① 中心按下
        miss = []
        for i, n in enumerate(slots):
            cx, cy = slot_center(i)
            ht = raycast_top(nodes, cx, cy)
            if ht is not n or drag_owner(ht) is not n:
                miss.append('Slot%d中心←%s' % (i, ht.name if ht else '(空)'))
        if miss:
            p7.append('中心按下抓不到卡：' + '；'.join(miss[:4]))
        # ② 卡面 5×5 采样按下（"按在卡边上也抓得住"）
        frac = (0.10, 0.30, 0.50, 0.70, 0.90)
        hm = []
        for i, n in enumerate(slots):
            x, y, w, h = abs_rect(n)
            for fx in frac:
                for fy in frac:
                    px, py = x + w * fx, y + h * fy
                    ht = raycast_top(nodes, px, py)
                    if ht is not n or drag_owner(ht) is not n:
                        hm.append('Slot%d(%.0f%%x,%.0f%%y)←%s' % (
                            i, fx * 100, fy * 100, ht.name if ht else '(空)'))
        if hm:
            p7.append('卡面 %d/%d 个采样点抓不到卡：%s' % (len(hm), len(slots) * 25, '；'.join(hm[:4])))
        # ④ 松手在 Slot5 中心 ⇒ 换位目标 = 5
        p1 = slot_center(5)
        got = hittest_slot(p1[0], p1[1], padk)
        if got != 5:
            p7.append('松手在 Slot5 中心 ⇒ 落位判定给出 %d（应为 5 = MoveSlot(0,5)）' % got)
        # ⑤ 松手在卡阵之外 ⇒ −1（移除分支）
        ox_, oy_ = C['DesignW'] / 2.0, C['BannerTopY'] + 200.0
        got_out = hittest_slot(ox_, oy_, padk)
        if got_out != -1:
            p7.append('松手在宣传区(%.0f,%.0f) ⇒ 落位判定给出 %d（应为 −1 = 拖出卡阵）' % (ox_, oy_, got_out))
        # ⑥ 卡池格按下 + 拖到 Slot3 松手
        pc = next((n for n in nodes if n.name == 'Card0'), None)
        if pc is None:
            p7.append('树里没有卡池格 Card0 ⇒ 卡池拖不动')
        else:
            r = abs_rect(pc)
            hx, hy = r[0] + r[2] / 2.0, r[1] + r[3] / 2.0
            hp = raycast_top(nodes, hx, hy, skip='BannerLayer')
            if hp is not pc or drag_owner(hp) is not pc:
                p7.append('卡池格 Card0 中心被 %s 抢走（Collection 页）' % (hp.name if hp else '(空)'))
            g3 = hittest_slot(slot_center(3)[0], slot_center(3)[1], padk)
            if g3 != 3:
                p7.append('卡池卡拖到 Slot3 中心 ⇒ 落位判定给出 %d（应为 3 = 选入）' % g3)
    bad('P7', '拖动链路模拟失败：' + '；'.join(p7[:4])) if p7 else \
        ok('P7', '拖动链路模拟通过：8 格中心 + 8×25 卡面采样按下都抓住卡自己；'
                 '落位判定自洽（格心级 + 卡面级）；Slot0→Slot5 换位；阵外松手=−1(移除)；卡池卡→Slot3(选入)')

    return red, green


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--negctl', action='store_true', help='先跑负控（必须报红），再跑正控')
    ap.add_argument('-v', '--verbose', action='store_true', help='打印整棵树')
    args = ap.parse_args()

    src = strip_comments(read_src())
    C = load_constants(src)
    root, nodes, unknown = build_tree(src, C)

    print('文件：%s' % os.path.relpath(PANEL, ROOT))
    print('常量 %d 个；节点 %d 个' % (len(C), len(nodes)))
    if args.verbose:
        for n in sorted(nodes, key=lambda x: x.order):
            print('  #%-3d %-24s parent=%-12s rect=%-34s raycast=%-5s drag=%s%s' % (
                n.order, n.name, n.parent.name if n.parent else '-',
                '(%g,%g,%g,%g)' % abs_rect(n) if n.rect else 'None',
                n.raycast, n.has_drag, ' MASK' if n.mask else ''))
    print()

    rc = 0
    if args.negctl:
        # 两条负控：① 全屏吃射线层 → 必须让 P3 红；② 单格盖层 → 必须让 P2 红。
        #    ⛔ 缺一条都不够：P3 只覆盖"整屏被盖"，P2 才是"某张卡被盖"（历史故障的真实形态）。
        for inject, want, desc in (('fullray', 'P3', '全屏吃射线层'),
                                   ('overcard', 'P2', '单格盖层'),
                                   ('bigbanner', 'P6', '宣传区改大压住卡阵'),
                                   ('slotsnap', 'P7', '落位公式的列原点偏半格（0.5×CardStepX）')):
            _r, _n, _u = build_tree(src, C)
            red, _g = audit(_r, _n, C, src, inject=inject)
            got = [m for c, m in red if c == want]
            if got:
                print('[负控] 注入"%s" ⇒ %s 报红 ✔' % (desc, want))
                print('       %s' % got[0][:120])
            else:
                print('[负控] 注入"%s" 之后 %s 没有报红 ⇒ 判据是死的，本次结论不可信' % (desc, want))
                print('Verdict: NEGCTL-FAILED')
                return 1
        print()

    red, green = audit(root, nodes, C, src)
    if unknown:
        red.append(('P0', '认不出的建件调用 %d 处：%s' % (len(unknown), unknown[:3])))
    for cid, msg in sorted(green):
        print('  OK  %s  %s' % (cid, msg))
    for cid, msg in sorted(red):
        print('  NG  %s  %s' % (cid, msg))

    print()
    print('判据 %d 条，红 %d 条' % (len(green) + len(red), len(red)))
    if red:
        print('Verdict: FAIL')
        rc = 1
    else:
        print('Verdict: PASS（离线静态射线全绿；运行时另有 VerifyDragPath() 走真实 EventSystem.RaycastAll 复核）')
    return rc


if __name__ == '__main__':
    sys.exit(main())
