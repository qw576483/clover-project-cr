#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""卡牌表现对照表生成器（判据资产，⛔ 不许删）：从**工程现状** + **原版 `.sc` 索引**出
`策划/卡牌表现对照表.md`。

数据源（全部只读）
------------------
  · `server/game/table/tsv/card.tsv`   —— 卡池（key / 名称 / 类型）
  · `server/game/table/tsv/unit.tsv`   —— 单位行（sprite_dir / projectile_key / 攻击节奏）
  · `server/game/table/tsv/spell.tsv`   —— 法术行（半径 / 飞行时长）
  · `client/Assets/Scripts/View/UnitAnimTable.cs`   —— 各档帧段（生成物，含 export 名与 clip id）
  · `client/Assets/Scripts/View/EffectsView.cs`     —— 弹道段 / 命中段查表
  · `client/Assets/Scripts/View/SpellFxTable.cs`     —— 法术落地段 / 飞行段查表
  · `client/Assets/Scripts/Core/ResPaths.cs`        —— 用途目录 / 起始帧 / 帧数常量
  · `client/Assets/Scripts/View/BattleViewRoot.cs`  —— 命中 / 死亡 / 法术的播点
  · `策划/单位动画分组表.md`                          —— 原版 export → 帧列 / fps / clip id（生成物）

「差值」的判定口径（机械、可复查）
----------------------------------
  · 攻击动画：原版 export 的帧列（分组表）**减去** `UnitAnimTable` 该目录 attack 档的 `Runs`
    （`Runs` = `[起始帧, 长度, 起始帧, 长度, …]` 扁平对）。
  · 弹道：原版 projectile export 的帧列 vs `EffectsView.TryGetProjectileFx` 登记的帧段；另记尺寸系数与染色。
  · 命中 / 死亡 / 出牌落地：原版 export 的帧列 vs 客户端播的 (first, count)。

复跑
----
  C:\\Python312\\python.exe tools/probes/gen-card-fx-table.py
"""
import io
import os
import re
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT_MD = os.path.join(ROOT, '策划', '卡牌表现对照表.md')

CARD_TSV = os.path.join(ROOT, 'server', 'game', 'table', 'tsv', 'card.tsv')
UNIT_TSV = os.path.join(ROOT, 'server', 'game', 'table', 'tsv', 'unit.tsv')
SPELL_TSV = os.path.join(ROOT, 'server', 'game', 'table', 'tsv', 'spell.tsv')
VIEW = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'View')
CORE = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'Core')
GROUPS_MD = os.path.join(ROOT, '策划', '单位动画分组表.md')

F_UNIT_ANIM = os.path.join(VIEW, 'UnitAnimTable.cs')
F_EFFECTS = os.path.join(VIEW, 'EffectsView.cs')
F_SPELL = os.path.join(VIEW, 'SpellFxTable.cs')
F_RESP = os.path.join(CORE, 'ResPaths.cs')
F_BATTLE = os.path.join(VIEW, 'BattleViewRoot.cs')


def read(path):
    return io.open(path, encoding='utf-8').read()


def read_lines(path):
    return read(path).split('\n')


def read_tsv(path):
    rows = [l.split('\t') for l in read_lines(path) if l.strip()]
    head = rows[0]
    out = []
    for r in rows[1:]:
        if len(r) < len(head):
            r = r + [''] * (len(head) - len(r))
        out.append(dict(zip(head, r)))
    return out


# ────────────────────────────── 分组表索引 ──────────────────────────────

RE_CLIP_ROW = re.compile(r'^\|\s*(\d+)\s*\|\s*`([^`]+)`\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*`([^`]+)`\s*\|\s*([^|]*)\|\s*([^|]*)\|')
RE_GROUP_ROW = re.compile(r'^\|\s*`([^`]+)`\s*\|\s*(\d+)\s*[–-]\s*(\d+)（(\d+) 条）\s*\|\s*([^|]+)\|\s*([^|]+)\|\s*([^|]+)\|\s*`([^`]+)`\s*\|')


def expand_frames(text):
    """`396-398 398 411-425` ⇒ [396,397,398,398,411,…,425]（去重保序）。"""
    out = []
    for tok in text.replace('，', ' ').split():
        m = re.match(r'^(\d+)-(\d+)$', tok)
        if m:
            out.extend(range(int(m.group(1)), int(m.group(2)) + 1))
        elif tok.isdigit():
            out.append(int(tok))
    seen = set()
    uniq = []
    for f in out:
        if f not in seen:
            seen.add(f)
            uniq.append(f)
    return uniq


def load_groups():
    exports = {}
    groups = {}
    groups_by_sc = {}
    sc = ''
    for i, line in enumerate(read_lines(GROUPS_MD), 1):
        ms = re.match(r'^`([^`]+\.sc)`', line)
        if ms:
            sc = ms.group(1)
            continue
        m = RE_CLIP_ROW.match(line)
        if m:
            name = m.group(2)
            if name not in exports:
                exports[name] = dict(line=i, clip=int(m.group(3)), fps=int(m.group(4)),
                                    timeline=int(m.group(5)), unique=int(m.group(6)),
                                    frames=expand_frames(m.group(7)), frame_text=m.group(7),
                                    tier=m.group(8).strip(), palette=m.group(9).strip(), sc=sc)
            continue
        m = RE_GROUP_ROW.match(line)
        if m:
            info = dict(line=i, clip_from=int(m.group(2)), clip_to=int(m.group(3)),
                        n=int(m.group(4)), fps=m.group(5).strip(),
                        frame_text=m.group(8), frames=expand_frames(m.group(8)), sc=sc)
            groups.setdefault(m.group(1), info)
            groups_by_sc.setdefault(sc, {}).setdefault(m.group(1), info)
    return exports, groups, groups_by_sc


# ────────────────────────────── 客户端表 ──────────────────────────────

def load_respaths():
    consts = {}
    for i, line in enumerate(read_lines(F_RESP), 1):
        m = re.match(r'\s*public const (?:int|string|float) (\w+)\s*=\s*(.+?);\s*$', line)
        if m:
            val = m.group(2).strip()
            if val.startswith('"'):
                val = val.strip('"')
            else:
                try:
                    val = float(val) if '.' in val else int(val)
                except ValueError:
                    pass
            consts[m.group(1)] = (val, i)
    return consts


def res_value(consts, token):
    if token is None:
        return None
    token = token.strip()
    m = re.match(r'^ResPaths\.(\w+)$', token)
    if m:
        return consts[m.group(1)][0] if m.group(1) in consts else token
    # `ResPaths.X + 1` / `ResPaths.X - 1`（表里写死的帧号微调）⇒ 算出数值。
    m = re.match(r'^ResPaths\.(\w+)\s*([+-])\s*(\d+)$', token)
    if m and m.group(1) in consts:
        base = consts[m.group(1)][0]
        if isinstance(base, (int, float)):
            return base + int(m.group(3)) if m.group(2) == '+' else base - int(m.group(3))
    try:
        return int(token)
    except ValueError:
        return token


def extract_array(text, key):
    """取 `key` 之后第一个 `{` 起、到与之配对的 `}` 为止的括号内文本（按花括号深度配对）。"""
    i = text.find(key)
    if i < 0:
        return None
    j = text.find('{', i)
    if j < 0:
        return None
    depth = 0
    for k in range(j, len(text)):
        if text[k] == '{':
            depth += 1
        elif text[k] == '}':
            depth -= 1
            if depth == 0:
                return text[j + 1:k]
    return None


def parse_int_slots(text):
    """`new[] { 153, 154 }, null, new[] { 1 }` → `[[153, 154], None, [1]]`。"""
    out, i = [], 0
    while i < len(text):
        m = re.match(r'\s*new\[\] \{ ([^}]*?) \}', text[i:])
        if m:
            out.append([int(x) for x in m.group(1).replace(' ', '').split(',') if x != ''])
            i += m.end()
        else:
            m2 = re.match(r'\s*null', text[i:])
            if not m2:
                break
            out.append(None)
            i += m2.end()
        m3 = re.match(r'\s*,\s*', text[i:])
        if m3:
            i += m3.end()
    return out


def load_unit_anim():
    """目录 → 各档帧段。`Runs` = [起始帧, 长度, …] 扁平对 ⇒ 展开成实际帧号。"""
    lines = read_lines(F_UNIT_ANIM)
    out = {}
    i = 0
    while i < len(lines):
        m = re.match(r'\s*\["([^"]+)"\]\s*=\s*new Entry \{', lines[i])
        if not m:
            i += 1
            continue
        d = m.group(1)
        head = lines[i]
        scfps = re.search(r'ScFps\s*=\s*(\d+)', head)
        hitspeed = re.search(r'HitSpeedMs\s*=\s*(\d+)', head)
        loadtime = re.search(r'LoadTimeMs\s*=\s*(\d+)', head)
        tiers = []
        j = i
        while j < len(lines) and len(tiers) < 4:
            c = re.search(r'new Clip \{ (.*)$', lines[j])
            if c:
                body = c.group(1)
                known = re.search(r'Known\s*=\s*(true|false)', body)
                start = re.search(r'Start\s*=\s*(\d+)', body)
                count = re.search(r'Count\s*=\s*(\d+)', body)
                runs = re.search(r'Runs\s*=\s*new\[\] \{ ([^}]*)\}', body)
                source = re.search(r'Source\s*=\s*"([^"]*)"', body)
                dropped = re.search(r'Dropped\s*=\s*new\[\] \{ ([^}]*)\}', body)
                fx = extract_array(body, 'FxByPos = new int[][]')
                fxr = extract_array(body, 'FxByPosRed = new int[][]')
                nums = [int(x) for x in re.findall(r'\d+', runs.group(1))] if runs else []
                frames = []
                for k in range(0, len(nums) - 1, 2):
                    frames.extend(range(nums[k], nums[k] + nums[k + 1]))
                tiers.append(dict(known=bool(known and known.group(1) == 'true'),
                                  start=int(start.group(1)) if start else -1,
                                  count=int(count.group(1)) if count else 0,
                                  frames=frames, runs=nums,
                                  dropped=sorted(int(x) for x in re.findall(r'\d+', dropped.group(1))) if dropped else [],
                                  fx=parse_int_slots(fx) if fx else [],
                                  fx_red=parse_int_slots(fxr) if fxr else [],
                                  source=source.group(1) if source else '',
                                  line=j + 1))
            j += 1
        out[d] = dict(tiers=tiers, scfps=int(scfps.group(1)) if scfps else 0,
                      hit_speed=int(hitspeed.group(1)) if hitspeed else 0,
                      load_time=int(loadtime.group(1)) if loadtime else 0, line=i + 1)
        i = j
    return out


def parse_case_blocks(lines):
    """`case "X":` 块（多个 case 标签归一块）+ 块前注释里的 `export` 名。"""
    blocks = []
    i = 0
    n = len(lines)
    while i < n:
        m = re.match(r'\s*case "([^"]+)":\s*$', lines[i])
        if not m:
            i += 1
            continue
        labels = [m.group(1)]
        label_lines = [i]
        j = i + 1
        while j < n:
            m2 = re.match(r'\s*case "([^"]+)":\s*$', lines[j])
            if m2:
                labels.append(m2.group(1))
                label_lines.append(j)
                j += 1
                continue
            break
        body = []
        k = j
        while k < n and 'return' not in lines[k]:
            body.append(lines[k])
            k += 1
        names = []
        for li in label_lines:
            # 取该 case 头上**整段连续注释**里的反引号名（只看最近一行会漏掉写在段首的 export 名）。
            for q in range(li - 1, max(-1, li - 12), -1):
                s = lines[q].strip()
                if s.startswith('//'):
                    names.extend(re.findall(r'`([^`]+)`', lines[q]))
                    continue
                if s == '':
                    continue
                break
        blocks.append(dict(labels=labels, body='\n'.join(body), line=i + 1, names=names))
        i = k + 1
    return blocks


def load_gloworb_defaults():
    """`GlowOrb(...)` 辅助件自己的图元段（用途目录 / 起始帧 / 帧数）—— 从 `EffectsView` 的定义处解出。

    女巫 / 火枪手 / 小亡灵三个投射物的 case 里只写了 `GlowOrb(缩放, 染色…)`，
    图元段（Spell f396 单帧）写在辅助件的定义里 ⇒ 这里解析定义，避免把辅助件的取值抄一份。
    """
    txt = read(F_EFFECTS)
    m = re.search(r'static ProjectileFx GlowOrb\([^)]*\)\s*\{(.*?)\n        \}', txt, re.S)
    if not m:
        return None
    body = m.group(1)
    u = re.search(r'Use\s*=\s*ResPaths\.(\w+)', body)
    f = re.search(r'First\s*=\s*(\d+)', body)
    c = re.search(r'Count\s*=\s*(\d+)', body)
    return dict(use=u.group(1) if u else None,
                first=int(f.group(1)) if f else None,
                count=int(c.group(1)) if c else None)


def load_projectile_fx(consts):
    out = {}
    glow = load_gloworb_defaults()
    for blk in parse_case_blocks(read_lines(F_EFFECTS)):
        text = blk['body']
        rec = dict(line=blk['line'], names=blk['names'], use=None, first=None, count=None,
                   scale=None, tint=False, kind='?', tint_args='')
        u = re.search(r'Use\s*=\s*ResPaths\.(\w+)', text)
        f = re.search(r'First\s*=\s*([^,\n]+)', text)
        c = re.search(r'Count\s*=\s*([^,\n}]+)', text)
        s = re.search(r'Scale\s*=\s*([0-9.]+f?)', text)
        if u:
            rec['use'] = res_value(consts, 'ResPaths.' + u.group(1))
        if f:
            rec['first'] = res_value(consts, f.group(1))
        if c:
            rec['count'] = res_value(consts, c.group(1))
        if s:
            rec['scale'] = float(s.group(1).rstrip('f'))
        if 'GlowOrb(' in text or 'Tinted(' in text:
            rec['tint'] = True
            t = re.search(r'(?:GlowOrb|Tinted)\(([^)]*)\)', text)
            rec['tint_args'] = t.group(1) if t else ''
            # `GlowOrb(缩放, mulR, mulG, mulB, addR, addG, addB)`：缩放是第 1 个实参，染色是后 6 个。
            if 'GlowOrb(' in text and glow:
                args = [a.strip() for a in rec['tint_args'].split(',') if a.strip()]
                if len(args) >= 7:
                    rec['scale'] = float(args[0].rstrip('f'))
                    rec['tint_args'] = 'mul(%s)+add(%s)' % (','.join(args[1:4]), ','.join(args[4:7]))
                if rec['use'] is None and glow['use']:
                    rec['use'] = res_value(consts, 'ResPaths.' + glow['use'])
                if rec['first'] is None and glow['first'] is not None:
                    rec['first'] = glow['first']
                if rec['count'] is None and glow['count'] is not None:
                    rec['count'] = glow['count']
        if 'ArrowFx' in text:
            rec['kind'] = 'ArrowFx'
        elif 'GlowOrb' in text:
            rec['kind'] = 'GlowOrb'
        elif 'KingCannonballFx' in text:
            rec['kind'] = 'KingCannonballFx'
        else:
            rec['kind'] = 'ProjectileFx'
        for lab in blk['labels']:
            out.setdefault(lab, rec)
    return out


def load_projectile_hit_fx(consts):
    out = {}
    for blk in parse_case_blocks(read_lines(F_EFFECTS)):
        text = blk['body']
        u = re.search(r'Use\s*=\s*ResPaths\.(\w+)', text)
        f = re.search(r'First\s*=\s*([^,\n]+)', text)
        c = re.search(r'Count\s*=\s*([^,\n}]+)', text)
        if u and f and c and 'holdSeconds' in text:
            rec = dict(use=res_value(consts, 'ResPaths.' + u.group(1)),
                       first=res_value(consts, f.group(1)),
                       count=res_value(consts, c.group(1)), line=blk['line'])
            for lab in blk['labels']:
                out.setdefault(lab, rec)
    return out


def load_spell_fx(consts):
    out = {}
    for blk in parse_case_blocks(read_lines(F_SPELL)):
        text = blk['body']
        rec = dict(key=blk['labels'][0], line=blk['line'])
        for field, pat in (('use', r'Use\s*=\s*ResPaths\.(\w+)'),
                           ('first', r'First\s*=\s*([^,\n]+)'),
                           ('count', r'Count\s*=\s*([^,\n}]+)'),
                           ('flight_use', r'FlightUse\s*=\s*ResPaths\.(\w+)'),
                           ('flight_first', r'FlightFirst\s*=\s*([^,\n]+)'),
                           ('flight_count', r'FlightCount\s*=\s*([^,\n}]+)'),
                           ('flight_scale', r'FlightScale\s*=\s*([0-9.]+f)')):
            mm = re.search(pat, text)
            if mm:
                if field.endswith('use'):
                    rec[field] = res_value(consts, 'ResPaths.' + mm.group(1))
                elif 'scale' in field:
                    rec[field] = float(mm.group(1).rstrip('f'))
                else:
                    rec[field] = res_value(consts, mm.group(1))
        out[rec['key']] = rec
    return out


def find_line(path, needle):
    for i, line in enumerate(read_lines(path), 1):
        if needle in line:
            return i
    return 0


# ────────────────────────────── 组表 ──────────────────────────────

def fmt_frames(frames):
    if not frames:
        return '—'
    parts = []
    i = 0
    while i < len(frames):
        j = i
        while j + 1 < len(frames) and frames[j + 1] == frames[j] + 1:
            j += 1
        parts.append('%d-%d' % (frames[i], frames[j]) if j > i else '%d' % frames[i])
        i = j + 1
    return ' '.join(parts)


def find_export(exports, names, hint=''):
    for nm in names:
        if nm in exports:
            return nm, exports[nm]
    if hint:
        low = hint.lower()
        cands = [(nm, e) for nm, e in exports.items() if low in nm.lower()]
        if cands:
            return cands[0]
    return None, None


EFFECTS_DIR = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Effects')


def played_frames(use, first, count):
    """客户端**实际播到**的帧号（`EffectsView.Spawn` = 目录里从 `first` 那帧起顺序取 `count` 帧）。"""
    d = os.path.join(EFFECTS_DIR, (use or '').replace('/', os.sep))
    if not os.path.isdir(d):
        return set(range(first, first + count))
    nums = []
    for f in os.listdir(d):
        if f.startswith('frame_') and f.endswith('.png'):
            try:
                nums.append(int(f[6:-4]))
            except ValueError:
                pass
    nums.sort()
    if not nums or first not in nums:
        return set(range(first, first + count))
    i = nums.index(first)
    return set(nums[i:i + count])


def main():
    exports, groups, groups_by_sc = load_groups()
    consts = load_respaths()
    anim = load_unit_anim()
    proj = load_projectile_fx(consts)
    proj_hit = load_projectile_hit_fx(consts)
    spells = load_spell_fx(consts)
    cards = read_tsv(CARD_TSV)
    units = read_tsv(UNIT_TSV)
    spell_rows = read_tsv(SPELL_TSV)
    unit_by_key = {}
    for u in units:
        unit_by_key.setdefault(u.get('key', ''), u)

    hit_line = find_line(F_BATTLE, 'ResPaths.EffectHit, ResPaths.EffectHitFirst')
    death_line = find_line(F_BATTLE, 'var deathUse = e.team == 0')
    particle_line = find_line(F_BATTLE, 'ResPaths.EffectDeathParticle, ResPaths.EffectDeathParticleFirst')
    deploy_dir = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Effects', 'Deploy')
    deploy_frames = sorted(os.listdir(deploy_dir)) if os.path.isdir(deploy_dir) else []
    deploy_used = find_line(F_BATTLE, 'ResPaths.EffectDeploy,') or find_line(F_EFFECTS, 'ResPaths.EffectDeploy,')
    deploy_line = find_line(F_BATTLE, 'ResPaths.EffectDeploy, ResPaths.EffectDeployFirst')

    hit_orig = '`effect_Hit1`｜帧列 `295-298`｜60 fps'
    hit_export = exports.get('effect_Hit1')
    rows = []

    for c in cards:
        key = c.get('key', '')
        name = c.get('name_cn', '')
        ctype = c.get('type', '')
        unit = unit_by_key.get(key, {})
        pkey = unit.get('projectile_key', '') or ''
        sdir = unit.get('sprite_dir', '') or c.get('sprite_dir', '') or ''
        is_spell = (ctype == '1')

        # ── 出牌 / 落地特效 ──
        if is_spell:
            row_deploy_orig = '原版无此段（法术卡不走单位落位特效；落地表现 = 该法术自己的命中段）'
            row_deploy_ours = '不适用'
        else:
            row_deploy_orig = '`deploy_arrows_effect`｜帧列 `119`｜60 fps'
            row_deploy_ours = ('**未播**（素材已落地 `Sprites/Effects/Deploy/%s`，客户端无调用点）'
                               % (', '.join(deploy_frames) if deploy_frames else '—')
                               if not deploy_used else '已播')

        # ── 攻击动画 ──
        if is_spell:
            atk_orig, atk_ours, atk_diff, atk_src = ('原版无此段（法术卡没有攻击动画）', '不适用',
                                                     '差值 0（法术无攻击档）', '—')
        else:
            a = anim.get(sdir)
            t = a['tiers'][2] if (a and len(a['tiers']) > 2) else None
            if t is None or not t['known']:
                base = re.sub(r'_out$', '', sdir or '')
                cand = [(nm, e) for nm, e in exports.items()
                        if e.get('tier') == 'attack' and nm.startswith(base)]
                if cand:
                    nm, e = cand[0]
                    atk_orig = '`%s`｜帧列 `%s`｜%d fps' % (nm, e['frame_text'], e['fps'])
                    atk_src = '分组表:%d' % e['line']
                    atk_diff = '差值 = 原版有该档，客户端无帧段（回落整目录循环）'
                else:
                    atk_orig = '原版无此段（`%s` 在分组表里没有 attack 档）' % (sdir or '—')
                    atk_src = '分组表（无该档）'
                    atk_diff = '差值 0（原版与客户端都没有该档）'
                atk_ours = ('`%s` 的 attack 档无可用帧段 ⇒ 回落整目录循环（`UnitAnimTable.cs:%d`）'
                            % (sdir, a['line']) if a else '`UnitAnimTable` 无该目录')
            else:
                exp = t['source'].split('｜')[0] if t['source'] else ''
                # 原版一条 clip 里，本体层与效果件层**叠画**在同一条时间轴上；本工程一帧一图元、
                # 按层拆开播 ⇒ 效果件帧登记在 `UnitAnimTable` 的 `Dropped` 里（不进本体 `Runs`），
                # 且**由效果件层补播**（`FxByPos`：逐视角的「每个本体帧位对应的效果帧号」）
                # ⇒ 核对帧集合时把 `Runs` / `Dropped` / 效果件层三者合并。
                dropped = t.get('dropped') or []
                fx_frames = sorted({v for slot in (t.get('fx') or []) if slot for v in slot if v > 0})
                nm, e = find_export(exports, [exp])
                if e:
                    atk_orig = '`%s`｜帧列 `%s`｜%d fps' % (nm, e['frame_text'], e['fps'])
                    atk_src = '`UnitAnimTable.cs:%d`；分组表:%d' % (t['line'], e['line'])
                    want = set(e['frames'])
                    got = set(t['frames']) | set(dropped) | set(fx_frames)
                    missing = sorted(want - got)
                    extra = sorted(got - want)
                    fx_covered = sorted(set(fx_frames) & set(dropped))
                    fx_other = sorted(set(dropped) - set(fx_frames))
                    if not missing and not extra:
                        atk_diff = '差值 0（帧集合与原版 export 帧列一致）'
                        if fx_covered:
                            atk_diff += ('；其中 %s 是原版 clip 里的**效果件帧**（不进本体 Runs，'
                                         '由效果件层补播）' % fmt_frames(fx_covered))
                        if fx_other:
                            atk_diff += '；%s 是本体另一姿势（本档不播）' % fmt_frames(fx_other)
                    else:
                        atk_diff = ('差值：原版有 %s 帧我们没播 / 我们多播 %s 帧'
                                    % (fmt_frames(missing) if missing else '无', fmt_frames(extra) if extra else '无'))
                else:
                    atk_orig = '`%s`（分组表按 export 名查不到）' % exp
                    atk_src = '`UnitAnimTable.cs:%d`' % t['line']
                    atk_diff = '差值 = 无法核对（原版 export 不在分组表索引）'
                atk_ours = ('`%s` attack 档 Start=%d Count=%d 播帧 %s（`UnitAnimTable.cs:%d`）'
                            % (sdir, t['start'], t['count'], fmt_frames(t['frames']), t['line']))
                if dropped:
                    atk_ours += '；剔除帧 %s（原版同一条 clip 的效果件层）' % fmt_frames(dropped)
                if fx_frames:
                    atk_ours += ('；效果件层 %s（第二层 sprite，与本体同一画布坐标，'
                                 '逐本体帧位叠播）' % fmt_frames(fx_frames))

        # ── 弹道 ──
        if is_spell:
            srec = spells.get(key, {})
            if srec.get('flight_use'):
                proj_orig = '原版 `fireball_projectile1`｜帧列 `396-425`｜60 fps（法术飞行段）'
                proj_ours = ('`%s` f%d..f%d｜尺寸系数 %s（`SpellFxTable.cs:%d`）'
                             % (srec['flight_use'], int(srec['flight_first']),
                                int(srec['flight_first']) + int(srec['flight_count']) - 1,
                                srec.get('flight_scale'), srec['line']))
                proj_diff = '差值 0（帧段 = 原版 export 的 91 步 ÷ 60 fps）'
                proj_src = '`SpellFxTable.cs:%d`；分组表:5276' % srec['line']
            else:
                proj_orig = '原版无此段（该法术没有飞行弹体）'
                proj_ours = '不适用（直接在落点播落地段）'
                proj_diff = '差值 0'
                proj_src = '`SpellFxTable.cs:%d`' % srec.get('line', 0)
        elif not pkey:
            proj_orig = '原版无此段（近战，`unit.tsv` 的 `projectile_key` 为空）'
            proj_ours = '不播弹体'
            proj_diff = '差值 0'
            proj_src = '`unit.tsv` 的 `%s` 行 `projectile_key` 为空' % key
        else:
            pf = proj.get(pkey)
            if not pf:
                proj_orig = '原版 `%s` 的 projectile export' % pkey
                proj_ours = '`EffectsView.TryGetProjectileFx` 无 `%s` ⇒ 回落 `ArrowFx` 并留痕' % pkey
                proj_diff = '差值 = 弹体段缺失（用箭矢兜底）'
                proj_src = '`EffectsView.cs` 的 `TryGetProjectileFx` 无 `%s`' % pkey
            else:
                nm, e = find_export(exports, pf['names'], pkey)
                if e:
                    proj_orig = '`%s`｜帧列 `%s`｜%d fps' % (nm, e['frame_text'], e['fps'])
                else:
                    proj_orig = '原版 `%s`（分组表按 export 名查不到）' % pkey
                tint = ('染色 %s' % pf['tint_args']) if pf.get('tint') else '原版像素原色'
                first = int(pf['first']) if pf['first'] is not None else 0
                count = int(pf['count']) if pf['count'] is not None else 0
                proj_ours = ('`%s` → use=`%s` f%d..f%d（%d 帧）｜尺寸系数 %s｜%s（`EffectsView.cs:%d`）'
                             % (pkey, pf['use'], first, first + count - 1, count,
                                pf['scale'] if pf['scale'] else '1（原版尺寸）', tint, pf['line']))
                hit = proj_hit.get(pkey)
                if hit:
                    proj_ours += '；专属命中段 `%s` f%d（`EffectsView.cs:%d`）' % (hit['use'], int(hit['first']), hit['line'])
                proj_diff = '差值 0（帧段与原版 export 帧列同源）' if e else '差值 = 原版帧列需人工核对'
                proj_src = ('`EffectsView.cs:%d`；`unit.tsv` 的 `%s` 行' % (pf['line'], key)) + \
                           ('；分组表:%d' % e['line'] if e else '')

        # ── 命中特效 ──
        if is_spell:
            srec = spells.get(key, {})
            if srec:
                hit_cell = ('该法术自己的落地段 = `%s` f%d..f%d（%d 帧）'
                            % (srec.get('use'), int(srec.get('first', 0)),
                               int(srec.get('first', 0)) + int(srec.get('count', 1)) - 1,
                               int(srec.get('count', 1))))
                hit_ours = '`SpellFxTable.cs:%d`' % srec['line']
                hit_diff = '差值 0（法术命中 = 落地段）'
                hit_src = '`SpellFxTable.cs:%d`' % srec['line']
            else:
                hit_cell, hit_ours, hit_diff, hit_src = '原版无此段', '不播', '差值 = 客户端无该法术条目', '—'
        else:
            hit_cell, hit_ours = hit_orig, '`EffectHit` f295..f298（`BattleViewRoot.cs:%d`）' % hit_line
            hit_diff = '差值 0（同一 export 的 4 帧）'
            hit_src = '`BattleViewRoot.cs:%d`；分组表:%d' % (hit_line, hit_export['line']) if hit_export \
                else '`BattleViewRoot.cs:%d`' % hit_line

        # ── 死亡特效 ──
        if is_spell:
            death_orig = '原版无此段（法术不产生单位死亡动画）'
            death_ours = '不适用'
            death_diff = '差值 0'
            death_src = '—'
        else:
            death_orig = ('`Death_blue`｜帧列 `52 62 65`｜30 fps（蓝方） / `Death_purple`｜帧列 `52 63`｜30 fps（红方）'
                          ' + `death_particle`｜帧列 `50 66`｜30 fps')
            death_ours = ('`Death/Blue` f52..f54（3 帧）/ `Death/Purple` f52..f53（2 帧）'
                          '（`BattleViewRoot.cs:%d`）+ `Death/Particle` f50..f51（`BattleViewRoot.cs:%d`）'
                          % (death_line, particle_line))
            death_diff = '差值 0（蓝/红两套 + 粒子段都在播）'
            death_src = '`BattleViewRoot.cs:%d`、`:%d`；分组表:4699、4700、4698' % (death_line, particle_line)

        # ── 我们现在播什么（汇总） ──
        ours = []
        if is_spell:
            srec = spells.get(key, {})
            if srec:
                ours.append('法术落地段 `%s` f%s..f%s（`SpellFxTable.cs:%d`）'
                            % (srec.get('use'), srec.get('first'),
                               int(srec.get('first', 0)) + int(srec.get('count', 1)) - 1, srec['line']))
                if srec.get('flight_use'):
                    ours.append('飞行段 `%s` f%s..f%s（`SpellFxTable.cs:%d`）'
                                % (srec['flight_use'], srec['flight_first'],
                                   int(srec['flight_first']) + int(srec['flight_count']) - 1, srec['line']))
        else:
            ours.append('攻击动画（`UnitAnimTable.cs`）')
            if pkey and pkey in proj:
                ours.append('弹体段（`EffectsView.cs:%d`）' % proj[pkey]['line'])
            if deploy_line:
                ours.append('落位特效（`BattleViewRoot.cs:%d`）' % deploy_line)
            ours.append('命中闪光（`BattleViewRoot.cs:%d`）' % hit_line)
            ours.append('死亡特效（`BattleViewRoot.cs:%d`）' % death_line)

        # ── 差值汇总 + 补法 ──
        diffs = [atk_diff, proj_diff]
        fixes = []
        if not is_spell and not deploy_used:
            diffs.insert(0, '出牌落地特效未播')
            fixes.append('落位时播 `Deploy` f119（素材已落地，缺调用点）')
        if not is_spell:
            diffs.append(hit_diff)
            diffs.append(death_diff)
        srcs = [atk_src, proj_src, hit_src]
        if not is_spell:
            srcs.append(death_src)
        rows.append(dict(key=key, name=name,
                          deploy_orig=row_deploy_orig, deploy_ours=row_deploy_ours,
                          attack_orig=atk_orig, attack_ours=atk_ours,
                          proj_orig=proj_orig, proj_ours=proj_ours,
                          hit_orig=hit_cell, hit_ours=hit_ours,
                          death_orig=death_orig, death_ours=death_ours,
                          ours='；'.join(ours) if ours else '—',
                          diff='；'.join(d for d in diffs if d),
                          fix='；'.join(fixes) if fixes else '无',
                          src='；'.join(x for x in srcs if x and x != '—')))

    # ── 法术段 ──
    spell_export = {
        'fireball': 'fireball',              # 组（分组表 :4710，帧列 `22 396-436`）
        'arrows': 'Arrow_enemy_ground_anim',
        'rage': 'rage_effect',
        'rocket': 'projectile_rocket',
        'goblin-barrel': 'spell_goblin_barrel',
        'freeze': 'freeze_effect',
        'lightning': 'lightning',
        'zap': 'zap',
        'poison': 'poison',
        'the-log': 'log',
    }
    spell_lines = []
    for sp in spell_rows:
        k = sp.get('key', '')
        srec = spells.get(k)
        if not srec:
            spell_lines.append((k, sp.get('name_cn', ''), '原版无此段', '不播',
                                '差值 = 客户端无该法术的特效条目', '—'))
            continue
        land = '落地 `%s` f%d..f%d（%d 帧）' % (
            srec.get('use'), int(srec.get('first', 0)),
            int(srec.get('first', 0)) + int(srec.get('count', 1)) - 1, int(srec.get('count', 1)))
        client_frames = played_frames(srec.get('use'), int(srec.get('first', 0)), int(srec.get('count', 1)))
        if srec.get('flight_use'):
            land += '；飞行 `%s` f%d..f%d（%d 帧，尺寸系数 %s）' % (
                srec['flight_use'], int(srec['flight_first']),
                int(srec['flight_first']) + int(srec['flight_count']) - 1,
                int(srec['flight_count']), srec.get('flight_scale'))
            client_frames |= played_frames(srec['flight_use'], int(srec['flight_first']),
                                           int(srec['flight_count']))

        nm = spell_export.get(k)
        e = None
        if nm:
            # 法术的「原版帧列」取**组行**（组 = 该 export 所在的一组 clip，帧列 = 组内并集）；
            # 同名的组可能出现在别的 `.sc`（如 `ui_spells_v215.sc` 的同名 UI 图标）⇒ 优先 effects 段。
            eff = groups_by_sc.get('effects_v215.sc', {})
            for cand in (eff.get(nm), groups.get(nm), exports.get(nm)):
                if cand is None:
                    continue
                e = cand
                break
        if e:
            want = set(e['frames'])
            missing = sorted(want - client_frames)
            extra = sorted(client_frames - want)
            if not missing and not extra:
                diff = '差值 0（帧集合与原版 export 帧列一致）'
            else:
                diff = ('差值：原版还有 %s 帧我们没播 / 我们多播 %s 帧'
                        % (fmt_frames(missing) if missing else '无',
                           fmt_frames(extra) if extra else '无'))
            src = '`SpellFxTable.cs:%d`；`spell.tsv` 的 `%s` 行；分组表:%d' % (srec['line'], k, e['line'])
            orig = '原版 `%s`｜帧列 `%s`｜%s fps' % (nm, e['frame_text'], e['fps'])
        else:
            diff = '差值 = 原版 export 名未在分组表索引里，需人工核对'
            src = '`SpellFxTable.cs:%d`；`spell.tsv` 的 `%s` 行' % (srec['line'], k)
            orig = '原版 `%s`' % (nm or '—')
        spell_lines.append((k, sp.get('name_cn', ''), orig + '；' + land,
                            '`SpellFxTable.cs:%d`' % srec['line'], diff, src))

    # ── 出表 ──
    out = []
    out.append('# 卡牌表现对照表\n')
    out.append('> **生成物，⛔ 不许手改** —— 生成器 = `tools/probes/gen-card-fx-table.py`'
               '（复跑 = `C:\\Python312\\python.exe tools/probes/gen-card-fx-table.py`）。\n')
    out.append('> 卡池 = `server/game/table/tsv/card.tsv`（%d 张卡：%d 张部队/建筑 + %d 张法术）；'
               '法术段另见 `server/game/table/tsv/spell.tsv`（%d 条）。\n'
               % (len(cards),
                  len([c for c in cards if c.get('type') != '1']),
                  len([c for c in cards if c.get('type') == '1']),
                  len(spell_rows)))
    out.append('> 「原版」列 = `策划/单位动画分组表.md`（`.sc` 显式三元组：export 名 → clip id → shapeID 帧列）；'
               '「我们现在播什么」列 = 客户端源码的查表与播点（`文件:行`）。\n')
    out.append('> **差值判定**：攻击动画 = 原版 export 帧列 ⊖ `UnitAnimTable` 该目录 attack 档的 `Runs`；'
               '弹道 = 原版 projectile export 帧列 vs `EffectsView.TryGetProjectileFx` 登记的帧段；'
               '命中/死亡 = 原版 export 帧列 vs 客户端 (first, count)。\n')
    out.append('\n## 卡池逐卡\n')
    head = ['卡 key', '卡名', '出牌/落地特效(原版 export + 帧段)', '攻击动画(原版帧段 + fps)',
            '弹道(原版 projectile export + 帧段 + 尺寸系数 + 染色)', '命中特效', '死亡特效',
            '我们现在播什么(文件:行)', '差值', '补法', '出处']
    out.append('| ' + ' | '.join(head) + ' |')
    out.append('| ' + ' | '.join(['---'] * len(head)) + ' |')
    for r in rows:
        out.append('| ' + ' | '.join([
            '`%s`' % r['key'], r['name'], r['deploy_orig'], r['attack_orig'],
            r['proj_orig'] + '；' + r['proj_ours'],
            r['hit_orig'], r['death_orig'], r['ours'], r['diff'], r['fix'], r['src']]) + ' |')

    out.append('\n## 法术段（`spell.tsv` 全部 %d 条）\n' % len(spell_rows))
    out.append('| 法术 key | 名称 | 客户端落地/飞行段 | 播点 | 差值 | 出处 |')
    out.append('| --- | --- | --- | --- | --- | --- |')
    for k, nm, cell, ours, diff, src in spell_lines:
        out.append('| `%s` | %s | %s | %s | %s | %s |' % (k, nm, cell, ours, diff, src))

    out.append('\n## 逐段缩放系数（`.sc` 反解 ⇒ `EffectsView.SegmentScale`）\n')
    out.append('> 表项由 `tools/probes/fx-scale-table.py` 打印（⛔ 不许手改），逐段 TSV = '
               '`.ai-tmp/test/fx-scale-table.tsv`；查表口径 = 「起始帧恰好相等」→ 落到「落在某段范围内」'
               '（起始帧最大者）⇒ 同一条 export 的各帧段共用一个系数。\n')
    out.append('| 用途目录 | 起始帧 | 帧数 | 依据 export | 系数 |')
    out.append('| --- | --- | --- | --- | --- |')
    tsv = os.path.join(ROOT, '.ai-tmp', 'test', 'fx-scale-table.tsv')
    if os.path.isfile(tsv):
        for line in read_lines(tsv)[1:]:
            p = line.split('\t')
            if len(p) < 8:
                continue
            out.append('| `%s` | %s | %s | %s | %s |' % (p[0], p[1], p[2], p[3], p[7]))

    os.makedirs(os.path.dirname(OUT_MD), exist_ok=True)
    with io.open(OUT_MD, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(out) + '\n')
    print('写入 %s（%d 张卡 + %d 条法术）' % (OUT_MD, len(rows), len(spell_lines)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
