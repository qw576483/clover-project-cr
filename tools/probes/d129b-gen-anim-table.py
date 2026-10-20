#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
d129b-gen-anim-table.py -- 重生成 `client/Assets/Scripts/View/UnitAnimTable.cs`
=================================================================================
本片（D129b）的口径变更（**唯一目标**）：把每个目录每档的帧段从
「该档 9 条 clip 的 shapeID **并集**」改成「**单条 clip**（= 单一视角、clip 内按时序）」。

为什么必须改（根因见 `策划/单位帧段表.md:2717-2720`）：
  `策划/单位帧段表.md:2717` 明写「一条 `0c` clip 内部 = 某一个视角下的一段按时序的逐帧动画」，
  `:2718` 明写「同一组内的 `_1 … _9` = **9 个不同视角**（不是同一段动画的第 1…9 帧）」，
  并给出处置建议 ① **「按 clip 播（推荐）」**。旧表取了并集 ⇒ 运行时会依次播 9 个视角
  （现象 = 苍蝇海「1 秒 9 次视角」、单位走路时原地打转/抽搐）。

视角选择依据（⛔ 不是目测猜，出处全部可复跑）
--------------------------------------------
* 选取 **`_5`**（该组的第 5 条 clip）。判据 = 本片出的联络图
  视角判据图（逐格标注 `视角号 + frame_NNN`）：
  在 `chr_knight_out` / `chr_musketeer_out` / `chr_archer_out` / `chr_giant_out` 四个目录上，
  `_1` = **背身**（只见后脑/帽后，无脸）、`_5` = **右向侧身**（可见侧脸，武器/枪管指向 +x）、
  `_9` = **正朝镜头**（可见双眼）⇒ 9 个视角是「绕角色的 yaw 序列，`_1`…`_9` 从背身转到正面」。
* 与本工程既有判定链同源：`ArenaView.cs:434/457-460` 的公主塔乘员取「正朝镜头」那一视角，
  本片在 `chr_musketeer_out` / `chr_knight_out` 上复核到「正朝镜头 = `_9`」（两个 `.sc` 互相印证）。
* 为什么本工程要 `_5` 而不是 `_9`：客户端朝向用 `SpriteRenderer.flipX`（`UnitView.cs:268`
  `_renderer.flipX = e.facing < 0`）表达，**只有侧身视角才能读出「掉头」**；正朝镜头的视角左右镜像后
  几乎无差别（单位会像"横向滑行"）。原版对局图 `策划/参考图/03_对局_1320x2868.jpg` 里
  蓝方女枪手是**侧身朝左**（枪管指向 -x）
  而素材 `_5` 的枪管指向 **+x** ⇒ `_5` 正是"未镜像的右向基准视角"，与 `flipX`（`facing>0` 不翻）匹配。
* 退化：该目录该档若没有 `_5`（`chr_balloon_out` / `chr_skeleton_balloon_out` 只有 `_1`、
  `chr_goblin_referee_out` 的 run 只有 `_1.._3`），则取**该目录所有档都有的、最接近 5 的视角号**，
  并在 `Source` 里如实写出实际视角号。

配色侧 / 角色组的选择（与旧表 `BC1-gen-table.py --side=main` 同口径，只是把规则写实）
----------------------------------------------------------------------------------
* 一个 `*_out` 目录里可能装**多个角色**（如 `chr_baby_dragon_out` 同时有 `baby_dragon_*` 与
  `inferno_dragon_*`；`chr_movingcannon_out` 同时有 `moving_cannon_*` 与 `broken_cannon_*`）。
  角色组 = 归一化（去掉配色词与档位后缀、去下划线）后**与目录名主干完全相等**的组；
  无完全相等则取"包含主干"里最短的那个。
* 配色侧：同一目录全局只取一侧（避免档位间闪色）；优先级 `blue` > `main` > `red`，
  与旧表「非红」的口径一致（`chr_musketeer_out` 取 `chr_musketeer_blue_*`、`chr_knight_out` 取 `Knight_*`）。
* **双侧输出**：同一角色组的 `red` 行（`Knight_enemy_*` / `archer1_enemy_*`）是敌方配色，
  与蓝侧帧号区间**不同**（同一目录里两套帧）。生成器对每档每视角同时产出：
  蓝侧 = 上面选出的 `side`（字段 `Runs` / `ViewRuns`，⛔ 取值口径不变）、
  红侧 = `side == 'red'` 的行（字段 `RunsRed` / `ViewRunsRed`）。
  红侧某档的视角覆盖不了蓝侧已收录的视角（或该目录根本没有 `red` 行）⇒ **整档回退蓝侧**
  并在生成日志里逐目录登记（⛔ 不逐视角混色：混色会让单位转身时换队色）。

自检（本片的"能失败的断言"，D129c 起是**逐视角**版本）
------------------------------------
  `--verify <UnitAnimTable.cs>` 断言：
    ① 每个 Known 档的 `ViewRuns` 必须有 **9** 个槽（缺的写 `null`）；
    ② 每个非 null 视角槽的 `runs` 必须**完整等于该 (目录, 档位, 视角) 某一条 clip 的 `runs`**
       （= 「帧段落在同一个 clip 内、不跨视角」，逐视角施加）；
    ③ 默认 `Runs` 必须是某个已收录视角的 runs（默认视角不能凭空来）；
    ④ 权威 = 引擎件 `clover-client-unity-engine/Runtime/Presentation/UnitFacingMap.cs`：必须存在，
       其 `Standard16StepToView` 必须可解析且长度 = 16；
       ⑤ 被校验的 C# 必须引用引擎表。
  · 对**旧文件**跑 → 必须 FAIL（负控：旧表没有 `ViewRuns`，槽数 = 0；且旧数据把 9 个视角并在一起）。
  · 对新生成的文件跑 → 必须 PASS。

复跑
----
  # 1) 出单 clip 帧段表（--root . --dirs all）
  # 2) 反解并校验朝向→视角表（--root .）
  /c/Python312/python tools/probes/d129b-gen-anim-table.py --root . --selftest  # 3) 负控 + 重生成 + 复验
"""
import argparse
import collections
import csv
import json
import os
import re
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

TIERS = ('idle', 'walk', 'attack', 'die')
TIER_LABEL = {'idle': 'idle', 'walk': 'walk', 'attack': 'attack', 'die': 'die'}
# 期望视角：`_5` = 右向侧身（判据见文件头）
PREFERRED_VIEW = 5
# 视角总数（`_1`…`_9`）；`Clip.ViewRuns` 恒为 9 槽，缺的写 null
VIEW_COUNT = 9
# 运行时朝向表的**权威**（引擎件）；生成器不再把表值写进 C#，断言④ 校验该件、⑤ 校验 C# 引用它
ENGINE_FACING_MAP = ('..', 'clover-client-unity-engine', 'Runtime', 'Presentation', 'UnitFacingMap.cs')
# 档位数（= `UnitFacingMap.DefaultStepCount`）
FACING_STEP_COUNT = 16
# 配色侧优先级（与旧表 BC1 `--side=main` 的「非红」同口径）
SIDE_ORDER = ('blue', 'main', 'red')
# 敌方配色侧在帧段表 `side` 列里的取值（= 同目录另一套帧）
RED_SIDE = 'red'
COLOR_TOKENS = {'enemy', 'blue', 'red', 'td'}
TIER_RE = re.compile(r'(idle|run|walk|attacking|attack|die|death|spawn|deploy|hit|hurt)\d*$')
# 这两个目录不是实体动画（原版 UI / 特效目录，`.sc` 里没有按角色分组的 9 视角动作），
# 旧表里的格是过渡期产物；本片的口径变更不适用于它们 ⇒ ·原样保留·（不引入未验证的新值）。
PRESERVE_AS_IS = {'effects_out', 'ui_out'}

# ── 逐视角覆盖 / 逐帧剔除 ────────────────────────────────────────────────
# 有些导出把**效果件**或**另一段动画**夹进同一条 clip（`.sc` 的 clip 是线性的，不分层）：
#   · `wizard1_attack1_5`：前 6 帧只有火球（不透明 0.7%~4.3%，本体 10.7%~14.4%），
#     另有 1 帧红队配色 —— 直接播就是"法师消失 0.25 s 再换个人"；
#   · `witch1_attack1_5` / `valkyrie1_attack1_5` / `goblinArcher_*`：夹 1 帧全透明或"只有武器"的帧。
# 逐帧依据：`.ai-tmp/test/bodyless_allviews.py`（不透明占比）+ `.ai-tmp/test/wiz-frames.png`（拼图目视）。
# 这里的覆盖是**生成器输入**，重跑后仍然生效（⛔ 不许只手改 `UnitAnimTable.cs`）。
VIEW_OVERRIDES = {
    'chr_wizard_out': {'attack': {
        1: [(248, 1), (305, 7)], 2: [(247, 1), (284, 7)], 3: [(246, 1), (277, 7)],
        4: [(245, 1), (270, 7)], 5: [(244, 1), (263, 7)], 6: [(243, 1), (256, 7)],
        7: [(242, 1), (291, 7)], 8: [(241, 1), (249, 7)], 9: [(240, 1), (298, 7)],
    }},
}
DROP_FRAMES = {
    ('chr_goblin_archer_out', 'idle'): {97},
    ('chr_goblin_archer_out', 'attack'): {97},
    ('chr_witch_out', 'attack'): {162},
    ('chr_valkyrie_out', 'attack'): {163},
}
# 表项级 / 档位级说明：生成物里必须保留的「为什么这么写」。
# ⛔ 这些文字**只能写在这里**（生成器的输入），不许只手改 `UnitAnimTable.cs` —— 手改会在下次
#   重跑时被静默丢掉（本片实测：盘上的表曾比生成器输出多 7 行说明 + 6 处措辞，重跑即丢）。
ENTRY_NOTES = {
    'building_basic_cannon_out': {
        'entry': [
            '            // ⚠️ 本条**不是**为了画帧（四档全 `Known=false`），而是为了**放开工炮排期**：原版',
            '            //   `building_basic_cannon_v215.sc` 的 4 条动画记录（clip 40/41/42/43）**全是 19 帧 / 30fps /',
            '            //   唯一 shape 名都是 `turret`**，没有任何 idle / attack 语义可分 ⇒ 帧段**不臆造**',
            '            //   （加农炮本体由 `BuildingLayerTable` 的层配方画静态帧，登记见 `策划/对照表.md` §D.3）。',
            '            //   但没有这条表项时，`BattleViewRoot` 的开火排期会在 `Table.TryGetValue(dir)` 处 `continue`',
            '            //   ⇒ 加农炮**开炮时连炮弹弹道都不播**（玩家只看到目标掉血 + 受击闪光）。',
            '            //   `HitSpeedMs` 取 `server/game/table/tsv/unit.tsv:26020048` 的 `hit_speed_ms=900`。',
        ],
        'tiers': {
            'idle': 'idle 不可用（素材层无分段语义，见上）',
            'walk': 'walk 不可用（建筑不走）',
            'attack': 'attack 不可用（素材层无分段语义，见上）',
            'die': 'die 不可用（素材层无分段语义，见上）',
        },
    },
}


# ─────────────────────────── 归一化 ───────────────────────────
def norm_char(name):
    """动画组名 → 角色键（去配色词 / 去档位后缀 / 去下划线，全小写）。"""
    toks = [t for t in re.split(r'[_\s]+', name.lower()) if t]
    while toks and TIER_RE.fullmatch(toks[-1]):
        toks.pop()
    toks = [t for t in toks if t not in COLOR_TOKENS]
    return ''.join(toks)


def norm_stem(dirname):
    s = dirname.lower()
    if s.endswith('_out'):
        s = s[:-4]
    if s.startswith('chr_'):
        s = s[4:]
    return s.replace('_', '')


def pick_char(dirname, keys):
    """角色组键选择：完全相等 > 包含主干 > 最短。"""
    stem = norm_stem(dirname)
    ks = sorted(set(keys))
    exact = [k for k in ks if k == stem]
    if exact:
        return exact[0]
    cont = [k for k in ks if stem and stem in k]
    pool = cont or ks
    return sorted(pool, key=lambda k: (len(k), k))[0]


# ─────────────────────────── 读入 ───────────────────────────
def load_segments(root):
    p = os.path.join(root, '.ai-tmp', 'test', 'D129b-clip-segments.tsv')
    rows = list(csv.DictReader(open(p, encoding='utf-8'), delimiter='\t'))
    by = collections.defaultdict(list)
    for r in rows:
        if r['tier'] not in TIERS:
            continue
        r['view_no'] = int(r['view']) if r['view'] else 0
        by[(r['dir'], r['tier'])].append(r)
    return by


def load_old_table(root):
    """从旧 `UnitAnimTable.cs` 取：目录集合 + ScFps + HitSpeedMs + 原样的 4 行 Clip 文本。"""
    p = os.path.join(root, 'client', 'Assets', 'Scripts', 'View', 'UnitAnimTable.cs')
    txt = open(p, encoding='utf-8').read()
    old = collections.OrderedDict()
    cur = None
    for line in txt.splitlines():
        m = re.search(r'\["([^"]+)"\] = new Entry \{ Dir = "[^"]+", ScFps = (\d+), HitSpeedMs = (\d+)', line)
        if m:
            cur = m.group(1)
            old[cur] = {'scfps': int(m.group(2)), 'hitms': int(m.group(3)), 'clip_lines': []}
            continue
        if cur is not None and re.match(r'^\s+new Clip \{', line):
            old[cur]['clip_lines'].append(line)
    return old


def load_hit_from_unit_tsv(root):
    """旧表没有的目录 → 按旧生成器 BC1 的同口径从 unit.tsv 取 hit_speed_ms。"""
    p = os.path.join(root, 'server', 'game', 'table', 'tsv', 'unit.tsv')
    if not os.path.isfile(p):
        return {}
    lines = open(p, encoding='utf-8').read().splitlines()
    if not lines:
        return {}
    hdr = lines[0].split('\t')
    ui = {k: i for i, k in enumerate(hdr)}
    if 'sprite_dir' not in ui or 'hit_speed_ms' not in ui:
        return {}
    out = {}
    for ln in lines[1:]:
        f = ln.split('\t')
        if len(f) < len(hdr):
            continue
        sd, hs = f[ui['sprite_dir']], f[ui['hit_speed_ms']]
        if not sd or not hs.isdigit():
            continue
        h = int(hs)
        if h <= 0:
            continue
        if f[ui['kind']] not in ('0', '1', '2'):
            continue
        for d in sd.split(';'):
            d = d.strip()
            if d and d not in out:
                out[d] = h
    return out


def load_loadtime_from_unit_tsv(root):
    """按 `sprite_dir` 从 unit.tsv 取 `load_time_ms`（攻击前摇；同 `load_hit_from_unit_tsv` 的口径与行过滤）。"""
    p = os.path.join(root, 'server', 'game', 'table', 'tsv', 'unit.tsv')
    if not os.path.isfile(p):
        return {}
    lines = open(p, encoding='utf-8').read().splitlines()
    if not lines:
        return {}
    hdr = lines[0].split('\t')
    ui = {k: i for i, k in enumerate(hdr)}
    if 'sprite_dir' not in ui or 'load_time_ms' not in ui:
        return {}
    out = {}
    for ln in lines[1:]:
        f = ln.split('\t')
        if len(f) < len(hdr):
            continue
        sd, lt = f[ui['sprite_dir']], f[ui['load_time_ms']]
        if not sd or not lt.isdigit():
            continue
        v = int(lt)
        if v <= 0:
            continue
        if f[ui['kind']] not in ('0', '1', '2'):
            continue
        for d in sd.split(';'):
            d = d.strip()
            if d and d not in out:
                out[d] = v
    return out


# ─────────────────────────── 选帧段 ───────────────────────────
def select_tiers(dirname, by, log):
    """返回 `({tier: 默认视角行}, (char, side, view), {tier: {view: 行}})`。

    第 3 项即 D129c 的「每档 9 视角」来源：先按 D129b 的老口径挑出 (角色组, 配色侧)，
    再在该 (角色, 配色侧) 下按视角号收集 —— **同一档内 9 个视角必须来自同一角色同一配色侧**，
    否则就是"转身时换了个角色/换了配色"（实测 42/48 目录三档齐 9 视角）。
    """
    per_tier = {t: by.get((dirname, t), []) for t in TIERS}
    all_rows = [r for t in TIERS for r in per_tier[t]]
    if not all_rows:
        return {}, ('', '', 0), {}

    char = pick_char(dirname, [norm_char(r['group']) for r in all_rows])
    # 配色侧：该角色在哪些 tier 出现 → 覆盖最多的一侧；并列按 SIDE_ORDER
    cover = collections.Counter()
    for t in TIERS:
        for s in {r['side'] for r in per_tier[t] if norm_char(r['group']) == char}:
            cover[s] += 1
    side = sorted(cover, key=lambda s: (-cover[s], SIDE_ORDER.index(s) if s in SIDE_ORDER else 99))[0] \
        if cover else 'main'

    def rows_for(tier, ch, sd):
        return [r for r in per_tier[tier] if norm_char(r['group']) == ch and r['side'] == sd]

    # 视角：该角色选定的侧，在多少 tier 上有该视角 → 覆盖最多、然后最接近 5
    vcover = collections.Counter()
    for t in TIERS:
        for v in {r['view_no'] for r in rows_for(t, char, side)}:
            vcover[v] += 1
    if not vcover:                                   # 该侧一帧都没有 → 放宽到该角色任意侧
        for t in TIERS:
            for r in per_tier[t]:
                if norm_char(r['group']) == char:
                    vcover[r['view_no']] += 1
    view = sorted(vcover, key=lambda v: (-vcover[v], abs(v - PREFERRED_VIEW), v))[0] if vcover else 0

    def resolve_rows(tier):
        """该档 → {视角号: 行}；同角色同配色侧缺失时逐档回退（与旧口径一致）。"""
        rows = rows_for(tier, char, side)
        if not rows:                                  # 退：同角色其它配色侧
            for s in SIDE_ORDER:
                rows = rows_for(tier, char, s)
                if rows:
                    break
        if not rows:                                  # 退：该目录该档任意组
            rows = list(per_tier[tier])
        out = {}
        for r in sorted(rows, key=lambda r: r['export']):
            if usable(r):
                out.setdefault(r['view_no'], r)
        return out

    by_view = {t: resolve_rows(t) for t in TIERS}
    out = {}
    for t in TIERS:
        cand = by_view[t]
        if not cand:
            continue
        if view in cand:
            out[t] = cand[view]
        else:
            v2 = min(cand, key=lambda v: (abs(v - PREFERRED_VIEW), v))
            out[t] = cand[v2]
    if view != PREFERRED_VIEW:
        log.append('%s：无 `_%d` 视角，默认退到 `_%d`' % (dirname, PREFERRED_VIEW, view))
    return out, (char, side, view), by_view


def frame_png_path(root, dirname, frame_no):
    """该目录该帧号对应的 PNG 路径（不存在 ⇒ None）。"""
    sub = 'Buildings' if dirname.startswith('building_') else 'Units'
    return os.path.join(root, 'client', 'Assets', 'Resources', 'Sprites', sub, dirname,
                        'frame_%03d.png' % frame_no)


# ─────────────────────────── 效果件层（原版同一条 clip 里的「效果件」）───────────────────────────
#
# 原版把**本体层**与**效果层**叠画在同一条时间轴上：`.sc` 的 clip 里每个元素都带自己的变换矩阵，
# 带矩阵 = 原版对它做了位移/缩放 ⇒ 它是效果层；不带矩阵 = 本体层。
# 本工程「一帧一图元」⇒ 效果件不能塞回本体 `Runs`（那会让本体消失一帧），必须**单独一层**播。
#
# 效果件的判据（两个条件同时成立）：
#   ① 该帧在原版那条 clip 的帧列里（`D129b-clip-segments.tsv` 的 `frame_ranges` = 原版帧列）；
#   ② 该帧的不透明像素占比 **< 本 clip 本体帧中位数的 45%**（效果件在画布上只占一小块）。
# ②把「本体姿势 + 特效」的合成帧排除在外（那类帧的占比与本体同量级）。
#
# 像素占比的量法 = `frame_coverage`（alpha > 8 的像素数 ÷ 画布像素数）。
_COVER_CACHE = {}
_SC_CACHE = {}


def expand_ranges(text):
    """`153-158 200 244` → `[153,154,…,158,200,244]`（原版帧列的展开）。"""
    out = []
    for tok in (text or '').replace('，', ' ').split():
        m = re.match(r'^(\d+)-(\d+)$', tok)
        if m:
            out.extend(range(int(m.group(1)), int(m.group(2)) + 1))
        elif tok.isdigit():
            out.append(int(tok))
    return out


def frame_coverage(root, dirname, frame_no):
    """该帧的不透明像素占画布的比例（缺 Pillow / 缺 PNG ⇒ None）。"""
    key = (dirname, frame_no)
    if key in _COVER_CACHE:
        return _COVER_CACHE[key]
    try:
        from PIL import Image
    except ImportError:
        return None
    p = frame_png_path(root, dirname, frame_no)
    if not os.path.isfile(p):
        _COVER_CACHE[key] = None
        return None
    im = Image.open(p).convert('RGBA')
    a = im.split()[3]
    nz = sum(1 for v in a.getdata() if v > 8)
    val = nz / float(im.size[0] * im.size[1])
    _COVER_CACHE[key] = val
    return val


def _load_sc_reader(root):
    """`sc-anim-index.py` 的二进制读取器（复用既有 `.sc` 解析件，⛔ 不重写解析）。"""
    import importlib.util
    path = os.path.join(root, 'tools', 'probes', 'sc-anim-index.py')
    spec = importlib.util.spec_from_file_location('scanim_fx', path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m.R


def clip_matrix_frames(root, dirname):
    """该目录的 `.sc` → `{clip_id: 该 clip 里「带变换矩阵」的元素所画出来的帧号}`。

    原版 clip 的每个元素（本体层 / 效果层）都带自己的变换矩阵：带矩阵 = 原版对它做了位移/缩放
    ⇒ 它是**效果层**；不带矩阵 = 本体层。判据出处 = `.sc` 的 `0x0c` 动画记录：每帧一个元素三元组
    `(元素号, 矩阵号, 颜色变换号)`，矩阵号 `65535` = 无变换。
    """
    if dirname in _SC_CACHE:
        return _SC_CACHE[dirname]
    out = {}
    name = dirname[:-4] if dirname.endswith('_out') else dirname
    sc = os.path.join(root, '原版资源', 'sc', name + '_v215.sc')
    if os.path.isfile(sc):
        try:
            R = _load_sc_reader(root)
            data = open(sc, 'rb').read()
            S = R(data)
            for _ in range(6):
                S.read_u16()
            S.read(5)
            S.read_u16()
            cnt = S.read_u16()
            for _ in range(cnt):
                S.read_u16()
            for _ in range(cnt):
                n = S.read_byte()
                S.read(n)
            shapes = []
            clips = {}
            while len(data) - S.tell() > 0:
                tag = S.read(1).hex()
                size = S.read_u32()
                payload = S.read(size)
                if tag == '12':
                    B = R(payload)
                    shapes.append(B.read_u16())
                elif tag == '0c':
                    B = R(payload)
                    cid = B.read_u16()
                    B.read_byte()
                    B.read_u16()
                    cnt1 = B.read_i32()
                    mx = set()
                    for _ in range(cnt1):
                        ei = B.read_u16()
                        mi = B.read_u16()
                        B.read_u16()
                        if mi != 65535:
                            mx.add(ei)
                    cnt2 = B.read_i16()
                    sids = [B.read_i16() for _ in range(cnt2)]
                    clips[cid] = (sids, mx)
            sid2idx = {}
            for i, sid in enumerate(shapes):
                sid2idx.setdefault(sid, i)

            def expand(sid, seen):
                if sid in sid2idx:
                    return [sid2idx[sid]]
                if sid in clips and sid not in seen:
                    s = clips[sid][0]
                    acc = []
                    for x in s:
                        acc += expand(x, seen | {sid})
                    return acc
                return []

            for cid, (sids, mx) in clips.items():
                frames = []
                for i, sid in enumerate(sids):
                    if i in mx:
                        frames += expand(sid, frozenset())
                out[cid] = frames
        except Exception as exc:      # 非预期分支必须留痕：解析失败 ⇒ 该目录只按像素判据
            print('  · %s：`.sc` 解析失败（%s）⇒ 效果件层只按像素判据' % (dirname, exc))
    _SC_CACHE[dirname] = out
    return out


def row_effect_frames(root, dirname, row):
    """该行（= 原版的一条 clip）里的**效果件帧号**（按原版帧列的顺序）。

    两个判据取交集：
      ① `.sc` 侧：原版这条 clip 里**带变换矩阵**画出来的元素（= 原版的效果层）；
      ② 像素侧：该帧的不透明像素占比 < 本 clip 本体帧中位数的 45%（效果件在画布上只占一小块）。
    ②把「本体姿势 + 特效」的合成帧排除在外（那类帧的占比与本体同量级）。
    """
    if not row:
        return []
    frames = expand_ranges(row.get('frame_ranges', ''))
    if not frames:
        return []
    covs = [frame_coverage(root, dirname, f) for f in frames]
    vals = sorted(c for c in covs if c is not None)
    if not vals:
        return []
    thr = vals[len(vals) // 2] * 0.45
    cand = [f for f, c in zip(frames, covs) if c is not None and c < thr]
    mx = clip_matrix_frames(root, dirname).get(int(row['clip_id']), None)
    if mx is None:                    # 该 `.sc` 没有这条 clip ⇒ 只按像素判据
        return cand
    mxs = set(mx)
    return [f for f in cand if f in mxs]


def fx_by_pos(body_frames, fx_frames):
    """效果件层：逐**本体帧位**对应的效果帧号（`0` = 该位没有效果件）。

    锚点 = 本体帧里第一个排在效果件之后的帧位（= 原版效果层开始叠画的那一刻）；
    之后每个本体帧位依次轮播效果件序列（效果件自身的动画由这条序列承载）。
    """
    if not fx_frames or not body_frames:
        return None
    top = max(fx_frames)
    anchor = len(body_frames)
    for i, f in enumerate(body_frames):
        if f > top:
            anchor = i
            break
    if anchor >= len(body_frames):
        return None
    out = [0] * len(body_frames)
    for i in range(anchor, len(body_frames)):
        out[i] = fx_frames[(i - anchor) % len(fx_frames)]
    return out


def fmt_fxpos(slots):
    """效果件层的 9 个视角槽 → C# 文本（`null` = 该视角没有效果件）。"""
    out = []
    for s in slots:
        out.append('null' if not s else 'new[] { %s }' % ', '.join(str(x) for x in s))
    return ', '.join(out)


def team_hue_counts(path):
    """高饱和（s ≥ 0.6）像素里的红/蓝计数（隔 2 px 采样）；缺 Pillow / 文件 ⇒ None。"""
    try:
        import colorsys
        from PIL import Image
    except ImportError:
        return None
    if not os.path.isfile(path):
        return None
    im = Image.open(path).convert('RGBA')
    px = im.load()
    red = blue = 0
    for y in range(0, im.height, 2):
        for x in range(0, im.width, 2):
            r, g, b, a = px[x, y]
            if a < 128:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            if s < 0.6 or v < 0.25:
                continue
            if h < 15 / 360.0 or h > 345 / 360.0 or (325 / 360.0 <= h <= 345 / 360.0):
                red += 1
            elif 175 / 360.0 <= h <= 250 / 360.0:
                blue += 1
    return red, blue


def tier_red_frames_ok(root, dirname, blue, red, log):
    """该档的红侧是否**真的**是红队配色（任一视角的红侧帧满足判据即可）。

    默认视角可能正好是"队色不明显"的姿势（如建筑/披风背身），只测一帧会误判整档；
    反过来，碎片层 / 与蓝侧同图的帧在任何视角都不会满足"高饱和红 ≫ 高饱和蓝"。
    """
    checked = 0
    for v, rr in red.items():
        if v not in blue:
            continue
        bf = int(blue[v]['runs'].split(',')[0])
        rf = int(rr['runs'].split(',')[0])
        if bf == rf:
            continue
        checked += 1
        bp = frame_png_path(root, dirname, bf)
        rp = frame_png_path(root, dirname, rf)
        b = team_hue_counts(bp)
        r = team_hue_counts(rp)
        if b is None or r is None:
            log.append('%s：红侧帧无法核对像素（缺 PNG / 无 Pillow），按素材 side 使用 frame_%03d'
                       % (dirname, rf))
            return True
        if r[0] >= 15 and r[0] > 1.5 * r[1]:
            return True
    if checked == 0:
        return False
    return False


def select_red(dirname, by, meta, blue_by_view, root=None, log=None):
    """敌方（红队）配色侧的每档 9 视角来源。

    红侧 = 同一角色组 `side == 'red'` 的那些行。某档红侧覆盖不了蓝侧已收录的视角
    （或缺该档、或该目录根本没有红侧）⇒ **整档回退蓝侧**：既保证「该档 9 槽非空」，
    也避免逐视角混色（转身时单位会换队色）。

    返回 `({tier: 默认红侧行}, {tier: {视角号: 行}}, {tier 集合 = 真的来自红侧}, [回退档位])`。
    """
    char, _side, _view = meta
    sel, by_view_red, real, fallback = {}, {}, set(), []
    log = log if log is not None else []
    if not char:
        return sel, by_view_red, real, fallback
    for t in TIERS:
        blue = {v: r for v, r in blue_by_view.get(t, {}).items()
                if usable(r) and 1 <= v <= VIEW_COUNT}
        if not blue:
            continue
        red = {}
        for r in sorted(by.get((dirname, t), []), key=lambda r: r['export']):
            if (norm_char(r['group']) == char and r['side'] == RED_SIDE
                    and usable(r) and 1 <= r['view_no'] <= VIEW_COUNT):
                red.setdefault(r['view_no'], r)
        if not red or not set(blue).issubset(set(red)):
            fallback.append(t)
            continue
        # 默认视角跟随蓝侧该档的默认视角；红侧没有同一视角号 ⇒ 取最接近 5 的
        blue_default = min(blue, key=lambda v: (abs(v - PREFERRED_VIEW), v))
        v_no = blue_default if blue_default in red else min(
            red, key=lambda v: (abs(v - PREFERRED_VIEW), v))
        if root is not None and not tier_red_frames_ok(root, dirname, blue, red, log):
            fallback.append(t)
            log.append('%s：%s 档红侧帧不是红队配色（与蓝侧同图 / 只剩队色碎片层）⇒ 回退蓝侧'
                       % (dirname, t))
            continue
        by_view_red[t] = red
        real.add(t)
        sel[t] = red[v_no]
    # 同一目录的配色侧必须**整目录统一**：有的档取红、有的档回退蓝 ⇒ 单位走一步换一次色。
    tiers_blue = [t for t in TIERS if any(usable(r) for r in blue_by_view.get(t, {}).values())]
    if real and any(t in fallback for t in tiers_blue):
        log.append('%s：红侧只覆盖 %s 档（缺 %s）⇒ 整目录回退蓝侧（避免档位间闪色）'
                   % (dirname, '/'.join(sorted(real)), '/'.join(sorted(set(tiers_blue) - real)) or '-'))
        return {}, {}, set(), list(tiers_blue)
    return sel, by_view_red, real, fallback


def runs_to_pairs(runs):
    v = [int(x) for x in runs.split(',') if x != '']
    pairs = [(v[i], v[i + 1]) for i in range(0, len(v), 2)]
    return pairs


def frames_to_pairs(frames):
    """帧号升序 → [(起始, 长度), ...]（连续帧合并）。"""
    out = []
    for f in sorted(set(frames)):
        if out and f == out[-1][0] + out[-1][1]:
            out[-1] = (out[-1][0], out[-1][1] + 1)
        else:
            out.append((f, 1))
    return out


def drop_pairs(pairs, drops):
    if not drops:
        return pairs
    return frames_to_pairs([f for s, n in pairs for f in range(s, s + n) if f not in drops])


def usable(row):
    """该行是否**真的**带帧段：`runs` 至少要有 [起始帧号, 长度] 一对。
    ⛔ 少于一对的行必须当成"不可用"——否则会生成 `new[] {  }`（空隐式数组）⇒ `error CS0826`。
    （本片实测：离线 `--verify` 会漏判这种行，因为空 runs 的 `tuple()` == `()` 恰好命中了候选集合；
     现已同时收紧断言，见 `verify()` 的"槽内 run 数必须 ≥ 2"。）"""
    return row is not None and len([x for x in row['runs'].split(',') if x != '']) >= 2


# ─────────────────────────── 生成 C# ───────────────────────────
HEADER = r'''// <auto-generated> 由 tools/probes/d129b-gen-anim-table.py 生成 —— 请勿手改；改数据请改生成器的输入。
// 生成链（两步，均可复跑）：
//   ① 单 clip 帧段表（--root . --dirs all）
//      —— 从 2.1.5 权威 `.sc` 解出「目录 × 档位 × 视角 → **单条 clip** 的帧段」（sid 用 AT1 的
//         「sid→shape 记录序 + 递归展开嵌套 clip」映射，`sc-at1-resolve.py:resolve_unit`）。
//   ② python tools/probes/d129b-gen-anim-table.py --root . --selftest
//   + ScFps / hit_speed_ms 沿用既有值（时序常量由其它来源维护）；hit_speed_ms 的来源仍是
//     server/game/table/tsv/unit.tsv。
// 口径①：**每档只取一个视角的一条 clip**（⛔ 不取「9 条 clip 的并集」）。
//   出处：`策划/单位帧段表.md:2717`（一条 clip = 一个视角、clip 内按时序）+ `:2718`
//   （同组 `_1…_9` = 9 个不同视角）+ `:2758` 的处置建议「① 按 clip 播（推荐）」。
//   取并集 ⇒ 运行时会依次播 9 个视角（现象：苍蝇海"1 秒 9 次视角"、走路时原地打转/抽搐）。
// 口径②：每档的**默认视角** = `_5` = 右向侧身（无 `_5` 的目录退到最接近 5 的视角）。
//   客户端朝向改用 `flipX` 表达（`UnitView` 里 `_renderer.flipX = UnitAnimTable.StepFlip(_viewStep)`）
//   ⇒ 只有侧身视角能读出"掉头"。
//   判据 = 逐格标注的视角判据图（每格标
//   `视角号 + frame_NNN`）：在 knight / musketeer / archer / giant 四目录上 `_1` = 背身、
//   `_5` = 右向侧身、`_9` = 正朝镜头。
// 口径⑤（逐视角帧段）：每档**额外**存 9 个视角各自的帧段（`Clip.ViewRuns`，下标 = 视角号 − 1）；
//   运行时按**移动方向**选视角（`UnitView` 用**本插值窗口两端快照之差**求朝向极角 φ，再查 `StepToView`）。
//   ⛔ 不是"前后两帧 worldPos"：逐帧位移被服务端的**毫格量化**主导，站立单位会被噪声翻 180°
//   （实测 `id=320` vstep 片段 `[4,12,12,12,4]`；门槛见 `UnitView.FacingMinMoveTiles`）。
//   映射表 = 引擎件 `clover-client-unity-engine/Runtime/Presentation/UnitFacingMap.cs` 的
//   `Standard16StepToView`（**权威 = 引擎件**），其正确性由逐视角剪影反解断言：
//     A1 相邻视角对的旋转 Δ **同号且有界**（4 目录 × 8 对 = 32/32 为正）
//     A2 链式推定朝向与映射表逐视角吻合（容差 ±25°）
//     A3/A4 武器极角 θ 严格单调、过零点落在 `_4`/`_5` 之间、`_9` 最负
//     A5 表 == 公式 `heading = 90° − step×22.5°`
//     A6 投影比例 k ∈ [1.0, 1.8]
//   负控：把表里某一条故意改错 ⇒ 该脚本必须判红（已实测 `--corrupt 4=3` ⇒ FAIL 5 条）。
//   绝对锚点：`ArenaView.cs:434/457-460` 已判定公主塔乘员取"正朝镜头"那一视角（= `_9`）。
//   ⇒ `_1` = 背身（φ=+90°，远离镜头）→ `_5` = 侧身（φ=0°，朝 +x）→ `_9` = 正朝镜头（φ=−90°）。
// 口径③（配色/角色组）：一个 `*_out` 目录可能装多个角色（`chr_baby_dragon_out` 同时有
//   `baby_dragon_*` / `inferno_dragon_*`）⇒ 角色组取「归一化后与目录主干完全相等」的那个；
//   配色侧同一目录全局只取一侧（blue > main > red，与「非红」口径一致）。
// 口径④：`Known == false` ⇒ 该档没有可用帧段 ⇒ UnitView 回落「整目录循环」并 Warn 一次（已登记差异）。
// 逐视角帧段的**出处**：从 2.1.5 权威 `.sc` 解出（列 `dir/tier/view/export/clip_id/runs`）
//   ⇒ 每个 `ViewRuns` 元素都能查回
//   具体 export 名与 clip id（`--verify` 也按这条逐视角断言）。
// 覆盖：48 个目录里 **42 个**在 idle/walk/attack 三档都齐 9 视角；不足的 6 个是
//   `building_mortar_out` / `building_xbow_out`（建筑，本就不该有 9 向）/ `chr_balloon_out` /
//   `chr_goblin_referee_out` / `chr_skeleton_balloon_out`（退化素材，只有 1~3 个视角）/ `effects_out`（原样保留）
//   ⇒ 规则：**逐 (目录, 档位)：有该视角就存，否则该视角位写 null**（运行时回落默认视角）。
using System;
using System.Collections.Generic;

namespace CR.View
{
    /// <summary>
    /// 单位/建筑动画帧段表（**纯数据**，与 UnitView 的实现分离 —— 便于帧段结论刷新后只改本文件）。
    /// <para>
    /// 每目录一条：4 个档位的 (start, count) + 帧号**段表** + 来源（export 名 / clip id / 视角 / 配色侧）。
    /// `Runs` = 扁平化的 [起始帧号, 长度, 起始帧号, 长度, ...]；**每档的 Runs 必须完整落在同一条 clip 内**
    /// （`tools/probes/d129b-gen-anim-table.py --verify` 的断言；取并集时该断言判红）。
    /// </para>
    /// <para>`Known == false` ⇒ 该档位**没有可用帧段** ⇒ 保持原行为（整目录循环）。</para>
    /// </summary>
    public static class UnitAnimTable
    {
        /// <summary>档位下标（与 <see cref="UnitView.AnimIdle"/> 等一致）。</summary>
        public const int Idle = 0, Walk = 1, Attack = 2, Die = 3;

        /// <summary>档位数（idle/walk/attack/die）。</summary>
        public const int TierCount = 4;

        /// <summary>朝向档数（16 档 = 全周 360° ÷ 22.5°；= 引擎 <see cref="CloverEngine.UnitFacingMap.DefaultStepCount"/>）。</summary>
        public const int StepCount = CloverEngine.UnitFacingMap.DefaultStepCount;

        /// <summary>
        /// 16 档朝向 → 视角号（= 引擎 <see cref="CloverEngine.UnitFacingMap.Standard16StepToView"/>；
        /// ⛔ 按**只读**对待，不许写它）。
        /// <para>
        /// 表 = 引擎 `clover-client-unity-engine/Runtime/Presentation/UnitFacingMap.cs` 的
        /// `Standard16StepToView`（**权威 = 引擎件**；`tools/probes/d129b-gen-anim-table.py` 的断言④
        /// 校验它、⑤ 校验本文件确实引用它）；表本身的正确性由逐视角剪影反解断言
        /// 断言 A1–A6 + 负控判红保证。
        /// </para>
        /// </summary>
        public static readonly int[] StepToView = CloverEngine.UnitFacingMap.Standard16StepToView;

        /// <summary>朝向档 `step` 是否要水平镜像（西半边 = 同一视角 + `flipX`；档位按 16 取模）。</summary>
        public static bool StepFlip(int step) { return CloverEngine.UnitFacingMap.Default.StepFlip(step); }

        /// <summary>
        /// 朝向极角（**度**；从 **+x 轴**起算、**+y 为正** = 远离镜头/上场方向）→ 视角号 + 是否镜像。
        /// `step = round((90° − φ) / 22.5°) mod 16`（取整口径与档位边界见引擎
        /// <see cref="CloverEngine.UnitFacingMap.StepForHeading"/>）。
        /// </summary>
        public static void ViewForHeading(float headingDeg, out int view, out bool flip)
        {
            CloverEngine.UnitFacingMap.Default.ViewForHeading(headingDeg, out view, out flip);
        }

        /// <summary>蓝队（我方）配色侧。</summary>
        public const int TeamBlue = 0;

        /// <summary>红队（敌方）配色侧。</summary>
        public const int TeamRed = 1;

        /// <summary>
        /// 取某档某视角的帧段 `Runs`；该视角无素材（或 `view` 越界）⇒ 返回 null（调用方回落默认视角）。
        /// <paramref name="team"/> = <see cref="TeamRed"/> 时取敌方配色侧的 9 视角帧段。
        /// </summary>
        public static int[] RunsForView(Clip clip, int view, int team = TeamBlue)
        {
            var views = team == TeamRed && clip.ViewRunsRed != null ? clip.ViewRunsRed : clip.ViewRuns;
            if (views == null || view < 1 || view > views.Length) return null;
            var r = views[view - 1];
            return (r != null && r.Length >= 2) ? r : null;
        }

        /// <summary>
        /// 该档的**默认视角**帧段（`team` = <see cref="TeamRed"/> 时优先红侧默认帧段；
        /// 红侧没有该档才回落蓝侧）。
        /// </summary>
        public static int[] RunsForTeam(Clip clip, int team)
        {
            if (team == TeamRed && clip.RunsRed != null && clip.RunsRed.Length >= 2) return clip.RunsRed;
            return clip.Runs;
        }

        /// <summary>`Runs` 的帧数（= 各段长度之和）。</summary>
        public static int CountOfRuns(int[] runs)
        {
            if (runs == null) return 0;
            var n = 0;
            for (var i = 1; i < runs.Length; i += 2) n += runs[i];
            return n;
        }

        /// <summary>一个档位的帧段。</summary>
        public struct Clip
        {
            /// <summary>是否有可用帧段（false ⇒ 回落整目录）。</summary>
            public bool Known;
            /// <summary>起始帧号（`frame_NNN` 的 NNN）；`Known==false` 时为 0。</summary>
            public int Start;
            /// <summary>帧数（= `Runs` 各段长度之和）；`Known==false` 时为 0。</summary>
            public int Count;
            /// <summary>帧号段表：[起始帧号, 长度, ...]。`Known==false` 时为 null。</summary>
            public int[] Runs;
            /// <summary>是否为一个连续区间（true ⇒ `Runs` 只有一对）。</summary>
            public bool Contiguous;
            /// <summary>来源（export 名 / clip id / 视角 / 配色侧），供追溯与回归对照。</summary>
            public string Source;
            /// <summary>
            /// 该档 clip 里**被有意剔除、不播**的帧号（= 原版同一条 clip 的**效果件帧**：
            /// 它们的像素只占画布的一小块、位置也在本体之外）。
            /// <para>
            /// 原版把本体层与效果层**叠画**在同一条时间轴上（`.sc` 的 clip 不分层）；
            /// 本工程一帧一图元、按层拆开播 ⇒ 效果件帧不进 <see cref="Runs"/>（播它 = 本体消失一帧），
            /// 但它们仍列在原版 export 的帧列里 ⇒ 核对帧集合时须把本字段与 <see cref="Runs"/> 合并后再比。
            /// </para>
            /// </summary>
            public int[] Dropped;
            /// <summary>
            /// 效果件层：9 个视角各自的「**每个本体帧位**对应的效果帧号」（下标 = 视角号 − 1；
            /// 值 `0` = 该帧位没有效果件）。
            /// <para>
            /// 原版把本体层与效果层**叠画**在同一条时间轴上；本工程一帧一图元 ⇒ 效果件单独一层播：
            /// 本体播到第 <c>i</c> 帧位时，若该位有效果件，就在**同一画布坐标**上再画一层效果件精灵
            /// （同一目录、同一锚点口径、同一世界缩放 ⇒ 与本体严丝合缝）。
            /// </para>
            /// <para>
            /// 每个视角槽的长度 = 该视角本体帧段的帧数（与 <see cref="Runs"/> / <see cref="ViewRuns"/> 对齐）。
            /// </para>
            /// </summary>
            public int[][] FxByPos;
            /// <summary>效果件层（红队配色侧）；结构同 <see cref="FxByPos"/>。</summary>
            public int[][] FxByPosRed;
            /// <summary>
            /// 9 个视角各自的帧段 `Runs`（下标 = 视角号 − 1）；元素 `null` = 该视角无素材。
            /// 出处 = 从 2.1.5 权威 `.sc` 解出的 `dir/tier/view → export/clip_id/runs` 表。
            /// 运行时用 <see cref="RunsForView"/> 取当前朝向的视角；取不到才回落上面的 `Runs`（默认视角）。
            /// </summary>
            public int[][] ViewRuns;
            /// <summary>敌方（红队）配色侧的默认视角帧段；`null` = 该档红侧回退蓝侧（用 `Runs`）。</summary>
            public int[] RunsRed;
            /// <summary>
            /// 敌方（红队）配色侧的 9 个视角帧段（下标 = 视角号 − 1）；`null` = 该档红侧回退蓝侧。
            /// 与 <see cref="ViewRuns"/> 同构，只是帧号取自同目录另一套（`*_enemy_*`）帧。
            /// </summary>
            public int[][] ViewRunsRed;
        }

        /// <summary>一个目录的 4 档帧段 + 该目录的 .sc 帧率 + `hit_speed_ms`。</summary>
        public struct Entry
        {
            /// <summary>sprite 目录名（`ResPaths.UnitDir/BuildingDir` 里那一列，如 `chr_knight_out`）。</summary>
            public string Dir;
            /// <summary>4 档（下标 = Idle/Walk/Attack/Die）。</summary>
            public Clip[] Tiers;
            /// <summary>该目录 `.sc` 自带播放帧率（walk/die 用；attack 由 `hit_speed_ms` 反推，见 UnitView）。</summary>
            public int ScFps;
            /// <summary>`hit_speed_ms`（ms，来源 `server/game/table/tsv/unit.tsv`）；0 = 该目录无对应单位行。</summary>
            public int HitSpeedMs;
            /// <summary>
            /// `load_time_ms`（ms，前摇；来源 `server/game/table/tsv/unit.tsv`）；0 = 该目录无对应单位行。
            /// 用途 = 攻击档起播后推迟这么久才出刀（见 `BattleViewRoot` 的攻击排期）。
            /// </summary>
            public int LoadTimeMs;
            /// <summary>
            /// 该目录是否有**真实的**敌方配色帧（true = 红侧字段来自另一套帧；
            /// false = 红侧已回退蓝侧）。仅作追溯用，运行时按 `<see cref="Clip.RunsRed"/>` 是否为 null 判断。
            /// </summary>
            public bool HasRedSide;
        }

        /// <summary>sprite 目录 → 帧段。</summary>
        public static readonly Dictionary<string, Entry> Table = new Dictionary<string, Entry>
        {
'''

FOOTER = r'''        };

        /// <summary>取某 sprite 目录的帧段；不在表中返回 false（⇒ UnitView 报到"未收录"并整目录循环）。</summary>
        public static bool TryGet(string dir, out Entry entry)
        {
            return Table.TryGetValue(dir ?? string.Empty, out entry);
        }

        /// <summary>表里已知档位的目录数（自检用）。</summary>
        public static int DirCount => Table.Count;
    }
}
'''


def emit(root, by, old, hit_tsv, load_tsv, out_path, log):
    entries = collections.OrderedDict()
    for dirname in old:
        sel, meta, by_view = select_tiers(dirname, by, log)
        red_sel, red_by_view, red_real, red_fb = select_red(dirname, by, meta, by_view, root, log)
        tiers = []
        for t in TIERS:
            tiers.append((t,
                          sel.get(t),
                          [by_view.get(t, {}).get(v) for v in range(1, VIEW_COUNT + 1)],
                          red_sel.get(t),
                          [red_by_view.get(t, {}).get(v) for v in range(1, VIEW_COUNT + 1)]))
        entries[dirname] = (tiers, meta)
        if dirname in PRESERVE_AS_IS:
            log.append('%s：非实体动画目录，原样保留（不生成红侧）' % dirname)
        elif red_real:
            log.append('%s：红侧取自 side=red 的帧（%d 档），蓝侧口径不变' % (dirname, len(red_real)))
            if red_fb:
                log.append('%s：红侧缺 %s 档 ⇒ 这几档回退蓝侧' % (dirname, '/'.join(red_fb)))
        else:
            log.append('%s：无红侧素材 ⇒ 红侧回退蓝侧' % dirname)

    L = [HEADER]
    for dirname, (tiers, _meta) in entries.items():
        name = dirname[:-4]
        o = old[dirname]
        new_fps = next((int(r['fps']) for _t, r, _v, _rr, _rv in tiers if r and r['fps']), None)
        L.append('            // %s（表目录名 %s）\n' % (dirname, name))
        notes = ENTRY_NOTES.get(dirname, {})
        for ln in notes.get('entry', []):
            L.append(ln + '\n')
        L.append('            ["%s"] = new Entry { Dir = "%s", ScFps = %d, HitSpeedMs = %d, LoadTimeMs = %d, '
                 'HasRedSide = %s, Tiers = new[]\n'
                 % (dirname, dirname, new_fps or o['scfps'], o['hitms'], load_tsv.get(dirname, 0),
                    'true' if any(rr for _t, _r, _v, rr, _rv in tiers) else 'false'))
        L.append('            {\n')
        if dirname in PRESERVE_AS_IS:
            for line in o['clip_lines'][:len(TIERS)]:
                L.append(line + '\n')
            L.append('            } },\n')
            continue
        for t, r, views, red_r, red_views in tiers:
            if not usable(r):
                msg = notes.get('tiers', {}).get(t, '%s 不可用（无帧段/该行 runs 不足一对）' % t)
                L.append('                new Clip { Known = false, Source = "" },   // %s\n' % msg)
                continue
            ov = VIEW_OVERRIDES.get(dirname, {}).get(t)
            drops = DROP_FRAMES.get((dirname, t), set())

            def pair_of(rv):
                if ov is not None and int(rv['view']) in ov:
                    return ov[int(rv['view'])]
                return drop_pairs(runs_to_pairs(rv['runs']), drops)

            pairs = ov[int(r['view'])] if ov is not None and int(r['view']) in ov \
                else drop_pairs(runs_to_pairs(r['runs']), drops)
            cnt = sum(n for _, n in pairs)
            # 该档 clip 里**被剔除、不播**的帧号 = clip 原帧集合 − 实际播的帧集合。
            # （被剔除的是原版同一条 clip 里的**效果件层**：原版叠画，本工程按层拆开播。）
            clip_frames = set(f for s, n in runs_to_pairs(r['runs']) for f in range(s, s + n))
            played_frames = set(f for s, n in pairs for f in range(s, s + n))
            dropped = sorted(clip_frames - played_frames)
            src = '%s｜clip %s｜视角 %s｜%s' % (r['export'], r['clip_id'], r['view'] or '-', r['side'])
            if ov is not None:
                src += '｜逐视角覆盖（剔除效果件/异视角帧）'
            vr = []
            for rv in views:
                vr.append('null' if not usable(rv)
                          else 'new[] { %s }' % ', '.join('%d, %d' % p for p in pair_of(rv)))

            # ── 效果件层（蓝侧）：逐视角「每个本体帧位对应的效果帧号」──
            # 效果件 = 原版那条 clip 里与本体层叠画的效果层（本档 clip 的低占比帧，见 `row_effect_frames`）。
            # 它**不进本体 `Runs`**（进则本体消失一帧），单独一层在本体帧位上叠播。
            fxpos_blue = []
            fx_all_blue = []
            blue_body = set(f for s, n in pairs for f in range(s, s + n))
            for rv in views:
                if not usable(rv):
                    fxpos_blue.append(None)
                    continue
                fx = row_effect_frames(root, dirname, rv)
                # 效果件层里只留**本体不播**的那些帧（本体已经在播的帧再画一遍 = 无意义的重绘）。
                body_frames = [f for s, n in pair_of(rv) for f in range(s, s + n)]
                fx = [f for f in fx if f not in body_frames]
                fx_all_blue.append(fx)
                fxpos_blue.append(fx_by_pos(body_frames, fx))
            if any(fx_all_blue):
                src += '｜效果件层 %s' % ' '.join(
                    '%d-%d' % (fx[0], fx[-1]) if len(fx) > 1 and fx[-1] - fx[0] == len(fx) - 1
                    else ','.join(str(x) for x in fx)
                    for fx in fx_all_blue if fx)

            if usable(red_r):
                # 红侧 = 同目录另一套帧；**效果件同样剔除**（红蓝同口径），效果件层另存 `FxByPosRed`。
                fx_red = row_effect_frames(root, dirname, red_r)
                red_pairs = drop_pairs(runs_to_pairs(red_r['runs']), set(fx_red))
                if not red_pairs:                       # 剔完没有帧 ⇒ 不剔（保持 clip 原值）
                    red_pairs = runs_to_pairs(red_r['runs'])
                red_src = '%s｜clip %s｜视角 %s｜%s' % (red_r['export'], red_r['clip_id'],
                                                       red_r['view'] or '-', red_r['side'])
                vr_red = []
                fxpos_red = []
                for rv in red_views:
                    if not usable(rv):
                        vr_red.append('null')
                        fxpos_red.append(None)
                        continue
                    fx = row_effect_frames(root, dirname, rv)
                    rp = drop_pairs(runs_to_pairs(rv['runs']), set(fx))
                    if not rp:
                        rp = runs_to_pairs(rv['runs'])
                    vr_red.append('new[] { %s }' % ', '.join('%d, %d' % p for p in rp))
                    body = [f for s, n in rp for f in range(s, s + n)]
                    fxpos_red.append(fx_by_pos(body, fx))
                red_tail = ('RunsRed = new[] { %s }, ViewRunsRed = new int[][] { %s }'
                            % (', '.join('%d, %d' % p for p in red_pairs), ', '.join(vr_red)))
                src += '｜红侧 %s' % red_src
            else:
                # 红侧回退：整档沿用蓝侧帧段（⛔ 不逐视角混色）。
                red_tail = ('RunsRed = new[] { %s }, ViewRunsRed = new int[][] { %s }'
                            % (', '.join('%d, %d' % p for p in pairs), ', '.join(vr)))
                fxpos_red = fxpos_blue
            fx_tail = (', FxByPos = new int[][] { %s }, FxByPosRed = new int[][] { %s }'
                       % (fmt_fxpos(fxpos_blue), fmt_fxpos(fxpos_red)))

            # ⛔ 必须写**显式**元素类型 `new int[][]`：9 槽全 `null` 时 `new[] { null, … }` 没有最佳类型
            #   ⇒ `error CS0826`（被编译闸门抓到过 2 处）。
            # `Dropped` 只在确有剔除帧时写（空数组 `new[] { }` 无最佳类型 ⇒ 同样会编译不过）。
            tail = red_tail + fx_tail + \
                (', Dropped = new[] { %s }' % ', '.join(str(f) for f in dropped) if dropped else '')
            L.append('                new Clip { Known = true, Start = %d, Count = %d, Contiguous = %s, '
                     'Runs = new[] { %s }, Source = "%s", ViewRuns = new int[][] { %s }, %s },\n'
                     % (pairs[0][0], cnt, 'true' if len(pairs) == 1 else 'false',
                        ', '.join('%d, %d' % p for p in pairs), src, ', '.join(vr), tail))
        L.append('            } },\n')
    L.append(FOOTER)
    open(out_path, 'w', encoding='utf-8').write(''.join(L))
    return entries


# ─────────────────────────── 自检 / 负控 ───────────────────────────
CLIP_RE = re.compile(
    r'new Clip \{ Known = true, Start = (\d+), Count = (\d+), Contiguous = (true|false), Runs = new\[\] \{ ([^}]*?)\ \}')
VIEWRUNS_RE = re.compile(r'ViewRuns = new int\[\]\[\] \{ (.*?) \} \}')
VIEWRUNS_RED_RE = re.compile(r'ViewRunsRed = new int\[\]\[\] \{ (.*?) \} \}')
RUNSRED_RE = re.compile(r'RunsRed = new\[\] \{ ([^}]*?) \}')
ENTRY_RE = re.compile(r'\["([^"]+)"\] = new Entry \{ Dir = "([^"]+)", ScFps = (\d+), '
                      r'HitSpeedMs = (\d+), LoadTimeMs = (\d+)')
FRAME_FILE_RE = re.compile(r'frame_(\d+)\.png$')
ENGINE_STEPTOVIEW_RE = re.compile(r'Standard16StepToView = \{ ([^}]*?) \};')
CS_ENGINE_STEPTOVIEW = 'public static readonly int[] StepToView = CloverEngine.UnitFacingMap.Standard16StepToView;'


def extract_array(line, key):
    """取 `key` 之后第一个 `{` 起、到与之配对的 `}` 为止的**括号内文本**（按花括号深度配对）。

    不用正则：数组元素自身也是 `{ … }`，终止符 ` } }` 会与元素的收尾括号混淆
    （`new[] { 72, 1 } }` 里的 ` } }` 正好跨了元素与数组两层）。
    """
    i = line.find(key)
    if i < 0:
        return None
    j = line.find('{', i)
    if j < 0:
        return None
    depth = 0
    for k in range(j, len(line)):
        if line[k] == '{':
            depth += 1
        elif line[k] == '}':
            depth -= 1
            if depth == 0:
                return line[j + 1:k]
    return None


def parse_viewruns(s):
    """`new[] { 1, 6 }, null, new[] { 2, 5 }` → [(1,6), None, (2,5)]。"""
    out, i = [], 0
    while i < len(s):
        m = re.match(r'\s*new\[\] \{ ([^}]*?) \}', s[i:])
        if m:
            out.append(tuple(int(x) for x in m.group(1).replace(' ', '').split(',') if x != ''))
            i += m.end()
        else:
            m2 = re.match(r'\s*null', s[i:])
            if not m2:
                break
            out.append(None)
            i += m2.end()
        m3 = re.match(r'\s*,\s*', s[i:])
        if m3:
            i += m3.end()
    return out


def parse_table(path):
    """{dir: {tier: {'runs','views','runs_red','views_red'}}} —— 每个 Entry 恰好 4 行 Clip。"""
    out = {}
    cur = None
    k = 0
    for line in open(path, encoding='utf-8'):
        m = re.search(r'\["([^"]+)"\] = new Entry', line)
        if m:
            cur = m.group(1)
            out[cur] = {}
            k = 0
            continue
        if cur is None:
            continue
        m = CLIP_RE.search(line)
        if m:
            if k < len(TIERS):
                v = extract_array(line, 'ViewRuns = new int[][]')
                vred = extract_array(line, 'ViewRunsRed = new int[][]')
                rred = extract_array(line, 'RunsRed = new[]')
                vfx = extract_array(line, 'FxByPos = new int[][]')
                vfxr = extract_array(line, 'FxByPosRed = new int[][]')
                out[cur][TIERS[k]] = {
                    'runs': tuple(int(x) for x in m.group(4).replace(' ', '').split(',') if x != ''),
                    'views': parse_viewruns(v) if v is not None else [],
                    'runs_red': tuple(int(x) for x in rred.replace(' ', '').split(',')
                                      if x != '') if rred is not None else None,
                    'views_red': parse_viewruns(vred) if vred is not None else None,
                    'fx': parse_viewruns(vfx) if vfx is not None else None,
                    'fx_red': parse_viewruns(vfxr) if vfxr is not None else None,
                }
            k += 1
            continue
        if 'new Clip { Known = false' in line:
            k += 1
    return out


def parse_entry_headers(path):
    """{dir: (ScFps, HitSpeedMs, LoadTimeMs)} —— 只比与帧段无关的时序常量。"""
    out = {}
    for line in open(path, encoding='utf-8'):
        m = ENTRY_RE.search(line)
        if m:
            out[m.group(1)] = (int(m.group(3)), int(m.group(4)), int(m.group(5)))
    return out


def dir_frame_numbers(root, dirname):
    """该目录本地 PNG 的帧号集合（`frame_NNN.png` 的 NNN）；目录不存在 ⇒ None。"""
    sub = os.path.join(root, 'client', 'Assets', 'Resources', 'Sprites',
                       'Buildings' if dirname.startswith('building_') else 'Units', dirname)
    if not os.path.isdir(sub):
        return None
    out = set()
    for f in os.listdir(sub):
        m = FRAME_FILE_RE.search(f)
        if m:
            out.add(int(m.group(1)))
    return out


def verify_blue_unchanged(path, baseline):
    """蓝侧回归：`baseline`（改前文件）与 `path` 的**蓝侧帧段逐值相同**。

    比 `runs` / `views`（逐槽）与 Entry 的 ScFps / HitSpeedMs / LoadTimeMs；红侧字段不参与。
    返回 (fails, 比较项数, 差异项数)。
    """
    cur, base = parse_table(path), parse_table(baseline)
    cur_h, base_h = parse_entry_headers(path), parse_entry_headers(baseline)
    fails, n = [], 0
    if set(cur) != set(base):
        fails.append('(蓝侧回归) 目录集合变化：新增 %s / 缺失 %s'
                     % (sorted(set(cur) - set(base)), sorted(set(base) - set(cur))))
    for d in sorted(base):
        if d not in cur:
            continue
        if cur_h.get(d) != base_h.get(d):
            fails.append((d, 'Entry', '时序常量变化 %s → %s' % (base_h.get(d), cur_h.get(d))))
        for t in TIERS:
            if t not in base[d]:
                continue
            n += 1
            a, b = base[d][t], cur[d][t]
            if a.get('runs') != b.get('runs'):
                fails.append((d, t, '蓝侧 Runs 变化 %s → %s' % (a.get('runs'), b.get('runs'))))
            if a.get('views') != b.get('views'):
                fails.append((d, t, '蓝侧 ViewRuns 变化'))
    return fails, n, len(fails)


def verify(path, by, root=None, baseline=None):
    """逐视角断言。返回 (ok, fails, n)。

    ① 每个 Known 档必须有 **9** 个视角槽（旧表没有 ⇒ 判红，即负控）；
    ② 每个非 null 视角槽的 `runs` 必须**完整等于该 (目录,档位,视角) 某一条 clip 的 runs**
       （= 「帧段落在同一个 clip 内、不跨视角」这条断言，逐视角施加）；
    ③ 默认 `Runs` 必须等于某个非 null 视角槽（默认视角只能是已收录的视角之一）；
    ④ 权威 = 引擎件 `ENGINE_FACING_MAP`：必须存在、`Standard16StepToView` 可解析且长度 = `FACING_STEP_COUNT`；
    ⑤ 被校验的 C# 必须引用引擎表（`CS_ENGINE_STEPTOVIEW`）；
    ⑥ 红侧：每档 `ViewRunsRed` 也有 9 槽；每个非 null 槽的帧号在目录 PNG 内，
       且要么是 `side=red` 的某条 clip 的 runs（真红侧）、要么与该槽的蓝侧值相同（整档回退）；
       红侧素材齐 9 视角的档必须 9 槽全非空；
    ⑦ 给了 `baseline`（改前文件）⇒ 蓝侧帧段与 Entry 时序常量**逐值相同**（差异 0）。
    """
    tbl = parse_table(path)
    fails, n = [], 0
    frames_cache = {}
    for d, tiers in tbl.items():
        if d in PRESERVE_AS_IS:
            continue
        if d not in frames_cache:
            frames_cache[d] = dir_frame_numbers(root, d) if root else None
        _keys = [norm_char(r['group']) for t in TIERS for r in by.get((d, t), [])]
        char = pick_char(d, _keys) if _keys else ''
        for t, info in tiers.items():
            n += 1
            runs, views = info['runs'], info['views']
            # 有逐视角覆盖 / 逐帧剔除的档位：帧段按构造规则产生，不再要求"逐字等于某条 clip"
            # （但仍校验 9 槽齐全、槽内非空 —— 见下）。
            patched = (t in VIEW_OVERRIDES.get(d, {})) or ((d, t) in DROP_FRAMES)
            if len(views) != VIEW_COUNT:
                fails.append((d, t, 'ViewRuns 槽数 = %d（应 %d）' % (len(views), VIEW_COUNT)))
                continue
            for vi, vr in enumerate(views, start=1):
                if vr is None:
                    continue
                # ★ 槽内必须至少有一对 (起始帧号, 长度)：否则生成的是 `new[] {  }`（空隐式数组）
                #   ⇒ 编译期 `error CS0826`。本条是本片被编译闸门抓到 2 处真错之后补上的
                #   （之前漏判的原因：空 runs 行的 `tuple()` == `()` 恰好命中了下面的候选集合）。
                if len(vr) < 2:
                    fails.append((d, t, '视角 _%d 的 runs=%s 不足一对 ⇒ 会生成空隐式数组（CS0826）' % (vi, vr)))
                    continue
                cands = {tuple(int(x) for x in r['runs'].split(',') if x != '')
                         for r in by.get((d, t), [])
                         if r['view_no'] == vi and len([x for x in r['runs'].split(',') if x != '']) >= 2}
                if vr not in cands and not patched:
                    fails.append((d, t, '视角 _%d 的 runs=%s 不是该视角任何一条 clip 的 runs' % (vi, vr)))
            if len(runs) < 2:
                fails.append((d, t, '默认 Runs=%s 不足一对 ⇒ 会生成空隐式数组（CS0826）' % (runs,)))
            elif patched:
                pass
            elif runs not in [v for v in views if v is not None]:
                # `die` 等档在 `.sc` 里**本来就没有视角分组**（该档 clip 不带 `_N` 视角名 ⇒ 全部 9 槽为 null）
                # ⇒ 此时默认 Runs 就是"与朝向无关的唯一那条"。仍须断言它**确实来自某条 clip**，
                #   否则"默认视角凭空来"这条就变成了恒真。
                if any(v is not None for v in views):
                    fails.append((d, t, '默认 Runs=%s 不在 ViewRuns 里（默认视角必须是已收录视角之一）' % (runs,)))
                else:
                    cands = {tuple(int(x) for x in r['runs'].split(',') if x != '')
                             for r in by.get((d, t), [])}
                    if runs not in cands:
                        fails.append((d, t, '该档无视角分组，但默认 Runs=%s 也不是该档任何一条 clip 的 runs' % (runs,)))
            # ── ⑥ 红侧 ─────────────────────────────────────────────────
            views_red, runs_red = info.get('views_red'), info.get('runs_red')
            if views_red is None or len(views_red) != VIEW_COUNT:
                fails.append((d, t, 'ViewRunsRed 槽数 = %s（应 %d）'
                              % ('缺失' if views_red is None else len(views_red), VIEW_COUNT)))
                continue
            frames = frames_cache.get(d)
            red_cov = {r['view_no'] for r in by.get((d, t), [])
                       if char and norm_char(r['group']) == char and r['side'] == RED_SIDE
                       and usable(r) and 1 <= r['view_no'] <= VIEW_COUNT}
            if len(red_cov) >= VIEW_COUNT:
                nonnull = sum(1 for v in views_red if v is not None)
                if nonnull != VIEW_COUNT:
                    fails.append((d, t, '红侧素材齐 9 视角，但 ViewRunsRed 只有 %d 个非空槽' % nonnull))
            for vi, vr in enumerate(views_red, start=1):
                if vr is None:
                    continue
                if len(vr) < 2:
                    fails.append((d, t, '红侧视角 _%d 的 runs=%s 不足一对' % (vi, vr)))
                    continue
                if frames:
                    bad = [f for i in range(0, len(vr), 2)
                           for f in range(vr[i], vr[i] + vr[i + 1]) if f not in frames]
                    if bad:
                        fails.append((d, t, '红侧视角 _%d 的帧号不在目录内（%d 个，例 %s）'
                                      % (vi, len(bad), bad[:5])))
                # 红侧口径 = **该红侧 clip 的帧段 − 该 clip 的效果件帧**（红蓝同口径）；
                # 红侧素材缺该视角时整档回退蓝侧 ⇒ 允许等于蓝侧同槽。
                cands_red = set()
                for r in by.get((d, t), []):
                    if r['side'] != RED_SIDE or r['view_no'] != vi:
                        continue
                    if len([x for x in r['runs'].split(',') if x != '']) < 2:
                        continue
                    fx = set(row_effect_frames(root, d, r))
                    cands_red.add(tuple(x for s, n in drop_pairs(runs_to_pairs(r['runs']), fx)
                                        for x in (s, n)))
                blue_slot = views[vi - 1] if vi - 1 < len(views) else None
                if vr not in cands_red and vr != blue_slot:
                    fails.append((d, t, '红侧视角 _%d 的 runs=%s 既不是「红侧 clip 的帧段 − 效果件帧」，'
                                        '也不等于蓝侧同槽（混色/凭空）' % (vi, vr)))
            # ── ⑧ 效果件层 ───────────────────────────────────────────────
            # 效果件**不进本体帧段**（进则本体消失一帧），单独一层在本体帧位上叠播；
            # 效果件层的长度必须与该视角的本体帧数一致（逐帧位一一对应）。
            for label, slots, body_slots in (('蓝侧', info.get('fx'), views),
                                             ('红侧', info.get('fx_red'), views_red)):
                if slots is None:
                    fails.append((d, t, '%s 效果件层缺失' % label))
                    continue
                if len(slots) != VIEW_COUNT:
                    fails.append((d, t, '%s 效果件层视角槽数 = %d（应 %d）'
                                  % (label, len(slots), VIEW_COUNT)))
                    continue
                for vi, sl in enumerate(slots, start=1):
                    if sl is None:
                        continue
                    body = body_slots[vi - 1] if vi - 1 < len(body_slots) else None
                    if body is None:
                        fails.append((d, t, '%s 效果件层视角 _%d 有内容，但该视角没有本体帧段'
                                      % (label, vi)))
                        continue
                    if len(sl) != sum(body[1::2]):
                        fails.append((d, t, '%s 效果件层视角 _%d 的长度 = %d（应 = 本体帧数 %d）'
                                      % (label, vi, len(sl), sum(body[1::2]))))
                    body_set = set(f for s, n in zip(body[0::2], body[1::2]) for f in range(s, s + n))
                    for f in sl:
                        if not f:
                            continue
                        if frames and f not in frames:
                            fails.append((d, t, '%s 效果件层视角 _%d 的帧号 %d 不在目录内'
                                          % (label, vi, f)))
                        if f in body_set:
                            fails.append((d, t, '%s 效果件层视角 _%d 的帧号 %d 同时进了本体帧段'
                                          % (label, vi, f)))
            if runs_red is None or len(runs_red) < 2:
                fails.append((d, t, '红侧默认 RunsRed=%s 不足一对' % (runs_red,)))
            elif runs_red not in [v for v in views_red if v is not None] \
                    and any(v is not None for v in views_red):
                fails.append((d, t, '红侧默认 RunsRed=%s 不在 ViewRunsRed 里' % (runs_red,)))
    if baseline is not None:
        if not os.path.isfile(baseline):
            fails.append(('(全局)', '蓝侧回归', '基线文件不存在：%s' % baseline))
        else:
            bfails, bn, bdiff = verify_blue_unchanged(path, baseline)
            n += bn
            fails.extend(bfails)
            print('[verify] 蓝侧回归：比较 %d 个 (目录,档位)，与基线差异 %d 项' % (bn, bdiff))
    if root is not None:
        # ④a 权威 = 引擎件：存在 + 表可解析 + 长度 = 档位数
        eng = os.path.join(root, *ENGINE_FACING_MAP)
        if not os.path.isfile(eng):
            fails.append(('(全局)', 'StepToView', '权威引擎件不存在：%s' % eng))
        else:
            m = ENGINE_STEPTOVIEW_RE.search(open(eng, encoding='utf-8').read())
            got = [int(x) for x in m.group(1).replace(' ', '').split(',') if x != ''] if m else []
            if len(got) != FACING_STEP_COUNT:
                fails.append(('(全局)', 'StepToView',
                              '引擎表解析失败或长度 != %d：%s' % (FACING_STEP_COUNT, got)))
            else:
                n += 1
        # ⑤ 被校验的 C# 必须引用引擎表
        if CS_ENGINE_STEPTOVIEW not in open(path, encoding='utf-8').read():
            fails.append(('(全局)', 'StepToView',
                          '被校验的 C# 未引用引擎表（缺 `%s`）' % CS_ENGINE_STEPTOVIEW))
        else:
            n += 1
    return (len(fails) == 0), fails, n


def layout_check(by, log):
    """独立判据（不看像素、只看素材打包顺序）：同一 (目录, 档位) 的 9 个视角，其帧段**起点**应随视角号
    **严格单调** —— 原版导出把同一动作的 9 个视角按 `_1…_9` 顺序**连排**，所以起点必然递增或递减。

    这条与像素侧的逐视角剪影反解（A1–A6）**互相独立**：一个看画面、一个看帧号排布。
    判据（**允许相邻等值**，因为同一动作相邻视角可能共用一帧）：9 个起点要么**非递减**、要么
    **非递增** ⇒ 自洽；否则是"视角号与帧段排布存在**非相邻级矛盾**"，逐视角选择不可信。
    实测 137 个「9 视角齐全」的 (目录,档位) 里 **自洽 119（87%）**、不自洽 18；典型不自洽：
      `chr_bowler_out` idle `[103,101,301,99,97,95,93,300,91]`（单点跳出去）、
      `chr_ice_wizard_out` walk `[246,42,238,34,26,18,230,10,0]`、
      `chr_electro_wizard_out` walk `[120,32,96,24,16,8,56,0,40]`。
      ⇒ 这些目录的 `view` 列与帧段排布**互相矛盾**，逐视角选择不可信（已登记差异；本表仍按其
      `view` 列如实写出 —— 出处一致，不臆改）。
    **硬断言**：4 个锚点目录（knight / musketeer / archer / giant）的 walk 档必须自洽
    （映射表的反解就是在这 4 个目录上做的）⇒ 该函数返回失败项。

    ⛔ **只看被选中的 (角色组, 配色侧)**：一个 `*_out` 目录可能装多个角色（`chr_barbarian_out` 有
    `barbarian1_*` / `angry_barbarian_*` / `angry_barbarian_td_*`），它们的 9 个视角号各不相同 ⇒
    若把同目录所有组混在一张 `view → 起点` 表里，后写的组会盖掉前一组，凭空造出"排布不自洽"。
    """
    fails, mono, tot, weird = [], 0, 0, []
    sel_cache = {}
    for (d, t), vs in sorted(by.items()):
        if t not in TIERS:
            continue
        if d not in sel_cache:
            sel_cache[d] = select_tiers(d, by, [])[1]     # (char, side, view)
        ch, sd, _v = sel_cache[d]
        start = {}
        for r in vs:
            if norm_char(r['group']) != ch or r['side'] != sd:
                continue
            if r['view_no'] and len([x for x in r['runs'].split(',') if x != '']) >= 2:
                start[r['view_no']] = int(r['runs'].split(',')[0])
        if len(start) < VIEW_COUNT:
            continue
        tot += 1
        seq = [start[v] for v in range(1, VIEW_COUNT + 1)]
        inc = all(seq[i] >= seq[i - 1] for i in range(1, VIEW_COUNT))
        dec = all(seq[i] <= seq[i - 1] for i in range(1, VIEW_COUNT))
        if inc or dec:
            mono += 1
        else:
            weird.append((d, t, seq))
    log.append('视角帧段排布自洽性（独立于像素判据）：9 视角齐全的 (目录,档位) 共 %d，'
               '**自洽 %d（%.0f%%）**、不自洽 %d'
               % (tot, mono, 100.0 * mono / tot if tot else 0, len(weird)))
    for d, t, seq in weird:
        log.append('  ⚠ 排布不自洽（逐视角选择不可信，已登记差异）：%s / %s = %s' % (d, t, seq))
    for d in ('chr_knight_out', 'chr_musketeer_out', 'chr_archer_out', 'chr_giant_out'):
        if (d, 'walk') not in by:
            fails.append((d, 'walk', 'walk 档缺失，无法做锚点排布断言'))
            continue
        start = {r['view_no']: int(r['runs'].split(',')[0]) for r in by[(d, 'walk')]
                 if r['view_no'] and len([x for x in r['runs'].split(',') if x != '']) >= 2}
        if len(start) < VIEW_COUNT:
            fails.append((d, 'walk', 'walk 档没有齐 9 视角（%d）' % len(start)))
            continue
        seq = [start[v] for v in range(1, VIEW_COUNT + 1)]
        inc = all(seq[i] >= seq[i - 1] for i in range(1, VIEW_COUNT))
        dec = all(seq[i] <= seq[i - 1] for i in range(1, VIEW_COUNT))
        if not (inc or dec):
            fails.append((d, 'walk', '锚点目录的 walk 视角起点不自洽：%s' % seq))
        else:
            log.append('  ✓ 锚点 %s walk 视角起点%s：%s'
                       % (d, '非递减' if inc else '非递增', seq))
    return fails


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--selftest', action='store_true', help='先对旧文件跑负控（必须判红），再重生成并复验')
    ap.add_argument('--verify', default=None, help='只校验某个 UnitAnimTable.cs，不生成')
    ap.add_argument('--baseline', default=None,
                    help='蓝侧回归基线（改前的 UnitAnimTable.cs）；缺省取 '
                         '.ai-tmp/test/teamcolor-blue-baseline.cs')
    ap.add_argument('--out', default=None)
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    out_path = a.out or os.path.join(root, 'client', 'Assets', 'Scripts', 'View', 'UnitAnimTable.cs')
    baseline = a.baseline or os.path.join(root, '.ai-tmp', 'test', 'teamcolor-blue-baseline.cs')

    by = load_segments(root)
    if a.verify:
        ok, fails, n = verify(a.verify, by, root, baseline)
        print('[verify] %s' % a.verify)
        print('[verify] 断言「每档 ViewRuns / ViewRunsRed 各有 9 槽、非 null 视角的帧段落在同一条 clip 内、'
              '默认 Runs 是已收录视角之一、红侧帧号在目录内且不混色、蓝侧与基线逐值相同、'
              '引擎朝向表为权威、C# 引用引擎表」：'
              '%s（检查 %d 项，失败 %d）'
              % ('PASS' if ok else 'FAIL', n, len(fails)))
        for f in fails[:20]:
            print('   FAIL %s / %s / %s' % f)
        return 0 if ok else 1

    old = load_old_table(root)
    hit_tsv = load_hit_from_unit_tsv(root)
    load_tsv = load_loadtime_from_unit_tsv(root)
    for d in old:
        if old[d]['hitms'] == 0 and d in hit_tsv:
            old[d]['hitms'] = hit_tsv[d]
    print('旧表目录数 = %d' % len(old))
    print('unit.tsv：hit_speed_ms 覆盖 %d 目录 / load_time_ms 覆盖 %d 目录'
          % (len(hit_tsv), len(load_tsv)))

    if a.selftest:
        # 负控对象 = 改前基线（没有红侧字段的旧表）；没有基线才退回当前文件。
        neg = baseline if os.path.isfile(baseline) else out_path
        ok0, fails0, n0 = verify(neg, by, root, neg)
        print('[负控] 对**旧文件**跑断言：%s（检查 %d 项，失败 %d）⇒ 期望 FAIL'
              % ('PASS' if ok0 else 'FAIL', n0, len(fails0)))
        for f in fails0[:8]:
            print('   (旧) FAIL %s / %s / %s' % f)
        if ok0:
            print('!! 负控失败：旧数据竟然通过了断言，说明断言无鉴别力，必须重写')
            return 2

    log = []
    entries = emit(root, by, old, hit_tsv, load_tsv, out_path, log)
    print('写出 -> %s' % out_path)
    for m in log:
        print('  · %s' % m)
    ok, fails, n = verify(out_path, by, root, baseline)
    print('[复验] 新文件断言：%s（检查 %d 项，失败 %d）' % ('PASS' if ok else 'FAIL', n, len(fails)))
    for f in fails[:20]:
        print('   FAIL %s / %s / %s' % f)
    # 覆盖率统计
    avail = miss = slots = red_dirs = fb_dirs = 0
    for _d, (tiers, _meta) in entries.items():
        real = any(rr for _t, _r, _v, rr, _rv in tiers)
        red_dirs += 1 if real else 0
        fb_dirs += 0 if real else 1
        for _t, r, views, _rr, _rv in tiers:
            if r is None:
                miss += 1
                continue
            avail += 1
            slots += sum(1 for v in views if v is not None)
    print('档位：可用 %d / 不可用 %d（目录 %d）｜9 视角槽共 %d 个，其中已收录 %d'
          % (avail, miss, len(entries), avail * VIEW_COUNT, slots))
    print('敌方配色：有真实红侧 %d 个目录 / 回退蓝侧 %d 个目录' % (red_dirs, fb_dirs))
    # 独立判据：素材打包顺序（不看像素）—— 与逐视角剪影反解的像素侧判据互相独立
    layout_fails = layout_check(by, log)
    for m in log:
        if m.startswith('视角帧段排布') or m.startswith('  '):
            print('  · %s' % m)
    if layout_fails:
        ok = False
        print('[排布判据] FAIL（失败 %d）' % len(layout_fails))
        for f in layout_fails:
            print('   FAIL %s / %s / %s' % f)
    else:
        print('[排布判据] PASS（锚点目录 walk 档的 9 视角帧段起点严格单调）')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
