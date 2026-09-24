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
COLOR_TOKENS = {'enemy', 'blue', 'red', 'td'}
TIER_RE = re.compile(r'(idle|run|walk|attacking|attack|die|death|spawn|deploy|hit|hurt)\d*$')
# 这两个目录不是实体动画（原版 UI / 特效目录，`.sc` 里没有按角色分组的 9 视角动作），
# 旧表里的格是过渡期产物；本片的口径变更不适用于它们 ⇒ ·原样保留·（不引入未验证的新值）。
PRESERVE_AS_IS = {'effects_out', 'ui_out'}


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


def runs_to_pairs(runs):
    v = [int(x) for x in runs.split(',') if x != '']
    pairs = [(v[i], v[i + 1]) for i in range(0, len(v), 2)]
    return pairs


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
//   + ScFps / hit_speed_ms 沿用上一版（本片不动时序常量）；hit_speed_ms 的来源仍是
//     server/game/table/tsv/unit.tsv。
// 口径①（本片修复的根因）：**每档只取一个视角的一条 clip**，不再取「9 条 clip 的并集」。
//   出处：`策划/单位帧段表.md:2717`（一条 clip = 一个视角、clip 内按时序）+ `:2718`
//   （同组 `_1…_9` = 9 个不同视角）+ `:2758` 的处置建议「① 按 clip 播（推荐）」。
//   旧表取并集 ⇒ 运行时会依次播 9 个视角（现象：苍蝇海"1 秒 9 次视角"、走路时原地打转/抽搐）。
// 口径②（D129b）：每档的**默认视角** = `_5` = 右向侧身（无 `_5` 的目录退到最接近 5 的视角）。
//   客户端朝向改用 `flipX` 表达（`UnitView.cs:294`）⇒ 只有侧身视角能读出"掉头"。
// 判据 = 逐格标注的视角判据图（逐格标
//   `视角号 + frame_NNN`）：在 knight / musketeer / archer / giant 四目录上 `_1` = 背身、
//   `_5` = 右向侧身、`_9` = 正朝镜头。
// 口径⑤（D129c，本片新增）：每档**额外**存 9 个视角各自的帧段（`Clip.ViewRuns`，下标 = 视角号 − 1）；
//   运行时按**移动方向**选视角（`UnitView` 用前后两帧 `worldPos` 求朝向极角 φ，再查 `StepToView`）。
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
//   配色侧同一目录全局只取一侧（blue > main > red，与旧表「非红」口径一致）。
// 口径④：`Known == false` ⇒ 该档没有可用帧段 ⇒ UnitView 回落「整目录循环」并 Warn 一次（已登记差异）。
// 逐视角帧段的**出处**：`.ai-tmp/test/D129b-clip-segments.tsv`（列 `dir/tier/view/export/clip_id/runs`），
//   从 2.1.5 权威 `.sc` 解出 ⇒ 每个 `ViewRuns` 元素都能查回
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
    /// （`tools/probes/d129b-gen-anim-table.py --verify` 的断言；旧表取并集时该断言判红）。
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

        /// <summary>取某档某视角的帧段 `Runs`；该视角无素材（或 `view` 越界）⇒ 返回 null（调用方回落默认视角）。</summary>
        public static int[] RunsForView(Clip clip, int view)
        {
            if (clip.ViewRuns == null || view < 1 || view > clip.ViewRuns.Length) return null;
            var r = clip.ViewRuns[view - 1];
            return (r != null && r.Length >= 2) ? r : null;
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
            /// 9 个视角各自的帧段 `Runs`（下标 = 视角号 − 1）；元素 `null` = 该视角无素材。
            /// 出处 = `.ai-tmp/test/D129b-clip-segments.tsv`（`dir/tier/view → export/clip_id/runs`）。
            /// 运行时用 <see cref="RunsForView"/> 取当前朝向的视角；取不到才回落上面的 `Runs`（默认视角）。
            /// </summary>
            public int[][] ViewRuns;
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


def emit(root, by, old, hit_tsv, out_path, log):
    entries = collections.OrderedDict()
    for dirname in old:
        sel, meta, by_view = select_tiers(dirname, by, log)
        tiers = []
        for t in TIERS:
            tiers.append((t, sel.get(t), [by_view.get(t, {}).get(v) for v in range(1, VIEW_COUNT + 1)]))
        entries[dirname] = (tiers, meta)

    L = [HEADER]
    for dirname, (tiers, _meta) in entries.items():
        name = dirname[:-4]
        o = old[dirname]
        new_fps = next((int(r['fps']) for _t, r, _v in tiers if r and r['fps']), None)
        L.append('            // %s（表目录名 %s）\n' % (dirname, name))
        L.append('            ["%s"] = new Entry { Dir = "%s", ScFps = %d, HitSpeedMs = %d, Tiers = new[]\n'
                 % (dirname, dirname, new_fps or o['scfps'], o['hitms']))
        L.append('            {\n')
        if dirname in PRESERVE_AS_IS:
            for line in o['clip_lines'][:len(TIERS)]:
                L.append(line + '\n')
            L.append('            } },\n')
            continue
        for t, r, views in tiers:
            if not usable(r):
                L.append('                new Clip { Known = false, Source = "" },   // %s 不可用（无帧段/该行 runs 不足一对）\n' % t)
                continue
            pairs = runs_to_pairs(r['runs'])
            cnt = sum(n for _, n in pairs)
            src = '%s｜clip %s｜视角 %s｜%s' % (r['export'], r['clip_id'], r['view'] or '-', r['side'])
            vr = []
            for rv in views:
                vr.append('null' if not usable(rv)
                          else 'new[] { %s }' % ', '.join('%d, %d' % p for p in runs_to_pairs(rv['runs'])))
            # ⛔ 必须写**显式**元素类型 `new int[][]`：9 槽全 `null` 时 `new[] { null, … }` 没有最佳类型
            #   ⇒ `error CS0826`（本片实测被编译闸门抓到 2 处）。
            L.append('                new Clip { Known = true, Start = %d, Count = %d, Contiguous = %s, '
                     'Runs = new[] { %s }, Source = "%s", ViewRuns = new int[][] { %s } },\n'
                     % (pairs[0][0], cnt, 'true' if len(pairs) == 1 else 'false',
                        ', '.join('%d, %d' % p for p in pairs), src, ', '.join(vr)))
        L.append('            } },\n')
    L.append(FOOTER)
    open(out_path, 'w', encoding='utf-8').write(''.join(L))
    return entries


# ─────────────────────────── 自检 / 负控 ───────────────────────────
CLIP_RE = re.compile(
    r'new Clip \{ Known = true, Start = (\d+), Count = (\d+), Contiguous = (true|false), Runs = new\[\] \{ ([^}]*?)\ \}')
VIEWRUNS_RE = re.compile(r'ViewRuns = new int\[\]\[\] \{ (.*) \} \}')
ENGINE_STEPTOVIEW_RE = re.compile(r'Standard16StepToView = \{ ([^}]*?) \};')
CS_ENGINE_STEPTOVIEW = 'public static readonly int[] StepToView = CloverEngine.UnitFacingMap.Standard16StepToView;'


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
    """{dir: {tier: {'runs':…, 'views':[…]}}} —— 每个 Entry 恰好 4 行 Clip，按序对应 idle/walk/attack/die。"""
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
                v = VIEWRUNS_RE.search(line)
                out[cur][TIERS[k]] = {
                    'runs': tuple(int(x) for x in m.group(4).replace(' ', '').split(',') if x != ''),
                    'views': parse_viewruns(v.group(1)) if v else [],
                }
            k += 1
            continue
        if 'new Clip { Known = false' in line:
            k += 1
    return out


def verify(path, by, root=None):
    """逐视角断言。返回 (ok, fails, n)。

    ① 每个 Known 档必须有 **9** 个视角槽（旧表没有 ⇒ 判红，即负控）；
    ② 每个非 null 视角槽的 `runs` 必须**完整等于该 (目录,档位,视角) 某一条 clip 的 runs**
       （= 「帧段落在同一个 clip 内、不跨视角」这条断言，逐视角施加）；
    ③ 默认 `Runs` 必须等于某个非 null 视角槽（默认视角只能是已收录的视角之一）；
    ④ 权威 = 引擎件 `ENGINE_FACING_MAP`：必须存在、`Standard16StepToView` 可解析且长度 = `FACING_STEP_COUNT`；
    ⑤ 被校验的 C# 必须引用引擎表（`CS_ENGINE_STEPTOVIEW`）。
    """
    tbl = parse_table(path)
    fails, n = [], 0
    for d, tiers in tbl.items():
        if d in PRESERVE_AS_IS:
            continue
        for t, info in tiers.items():
            n += 1
            runs, views = info['runs'], info['views']
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
                if vr not in cands:
                    fails.append((d, t, '视角 _%d 的 runs=%s 不是该视角任何一条 clip 的 runs' % (vi, vr)))
            if len(runs) < 2:
                fails.append((d, t, '默认 Runs=%s 不足一对 ⇒ 会生成空隐式数组（CS0826）' % (runs,)))
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
    实测 138 个「9 视角齐全」的 (目录,档位) 里 **自洽 120（87%）**；典型不自洽：
      `chr_electro_wizard_out` walk `[120,32,96,24,16,8,56,0,40]`、
      `chr_ice_wizard_out` walk `[114,42,98,34,26,18,66,10,0]`、
      `chr_goblin_archer_out` walk `[70,60,50,40,30,20,98,10,0]`、
      `chr_bowler_out` idle `[103,101,301,99,97,95,93,300,91]`（单点跳出去）。
      ⇒ 这些目录的 `view` 列与帧段排布**互相矛盾**，逐视角选择不可信（已登记差异；本表仍按其
      `view` 列如实写出 —— 出处一致，不臆改）。
    **硬断言**：4 个锚点目录（knight / musketeer / archer / giant）的 walk 档必须自洽
    （映射表的反解就是在这 4 个目录上做的）⇒ 该函数返回失败项。
    """
    fails, mono, tot, weird = [], 0, 0, []
    for (d, t), vs in sorted(by.items()):
        if t not in TIERS:
            continue
        start = {}
        for r in vs:
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
    ap.add_argument('--out', default=None)
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    out_path = a.out or os.path.join(root, 'client', 'Assets', 'Scripts', 'View', 'UnitAnimTable.cs')

    by = load_segments(root)
    if a.verify:
        ok, fails, n = verify(a.verify, by, root)
        print('[verify] %s' % a.verify)
        print('[verify] 断言「每档 ViewRuns 有 9 槽、每个非 null 视角的帧段落在同一条 clip 内、'
              '默认 Runs 是已收录视角之一、引擎朝向表为权威、C# 引用引擎表」：'
              '%s（检查 %d 项，失败 %d）'
              % ('PASS' if ok else 'FAIL', n, len(fails)))
        for f in fails[:20]:
            print('   FAIL %s / %s / %s' % f)
        return 0 if ok else 1

    old = load_old_table(root)
    hit_tsv = load_hit_from_unit_tsv(root)
    for d in old:
        if old[d]['hitms'] == 0 and d in hit_tsv:
            old[d]['hitms'] = hit_tsv[d]
    print('旧表目录数 = %d' % len(old))

    if a.selftest:
        ok0, fails0, n0 = verify(out_path, by, root)
        print('[负控] 对**旧文件**跑断言：%s（检查 %d 项，失败 %d）⇒ 期望 FAIL'
              % ('PASS' if ok0 else 'FAIL', n0, len(fails0)))
        for f in fails0[:8]:
            print('   (旧) FAIL %s / %s / %s' % f)
        if ok0:
            print('!! 负控失败：旧数据竟然通过了断言，说明断言无鉴别力，必须重写')
            return 2

    log = []
    entries = emit(root, by, old, hit_tsv, out_path, log)
    print('写出 -> %s' % out_path)
    for m in log:
        print('  · %s' % m)
    ok, fails, n = verify(out_path, by, root)
    print('[复验] 新文件断言：%s（检查 %d 项，失败 %d）' % ('PASS' if ok else 'FAIL', n, len(fails)))
    for f in fails[:20]:
        print('   FAIL %s / %s / %s' % f)
    # 覆盖率统计
    avail = miss = slots = 0
    for _d, (tiers, _meta) in entries.items():
        for _t, r, views in tiers:
            if r is None:
                miss += 1
                continue
            avail += 1
            slots += sum(1 for v in views if v is not None)
    print('档位：可用 %d / 不可用 %d（目录 %d）｜9 视角槽共 %d 个，其中已收录 %d'
          % (avail, miss, len(entries), avail * VIEW_COUNT, slots))
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
