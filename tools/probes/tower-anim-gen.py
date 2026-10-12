#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 `client/Assets/Scripts/View/TowerAnimTable.cs`（⛔ 生成物，值只来自 `.sc` 解析）。

出处链：
  `原版资源/sc/building_tower_v215.sc`
    · clip **244** = RED 国王坐像 idle（`KingTower_red` 的 `cnt2` 里名 `king_idle` 的子件 245 → 245 的 `cnt2` = 244）
    · clip **251** = BLUE 国王坐像 idle（`KingTower_blue` 的 `king_idle` 子件 252 → 252 的 `cnt2` = 251）
  `原版资源/sc/chr_princess_v215.sc`
    · clip **981** = BLUE 公主塔乘员 idle（export `princess_tower_idle1_3`）
    · clip **753** = RED  公主塔乘员 idle（export `princess_tower_red_idle1_7`）

口径（与 `ArenaView.TowerLayer` / `tools/probes/sc-placement.py` 一致）：
  `0x08` 矩阵 = (a,b,c,d,tx,ty)：sx=a/1024、sy=d/1024、dxPx=tx/20、dyPx=ty/20（65535 = 无矩阵）。
  `childIndex` 索引该 clip 自己的 `cnt2` 列表 ⇒ shapeID = sids[childIndex]；子件 id 本身是 clip 时取它的 cnt2 首项。
逐帧分组只在 `cnt1 == frames`（每帧恰好 1 条 placement）时成立 —— 本脚本对该条件做**硬断言**，不成立即中止。
"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SCDIR = os.path.join(ROOT, '原版资源', 'sc')
OUT = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'View', 'TowerAnimTable.cs')


def _load(n, rel):
    s = importlib.util.spec_from_file_location(n, os.path.join(ROOT, rel))
    m = importlib.util.module_from_spec(s)
    s.loader.exec_module(m)
    return m


spc = _load('spc', r'tools\probes\sc-placement.py')
scai = _load('scai', r'tools\probes\sc-anim-index.py')


def build(scfile, clip_id, label):
    path = os.path.join(SCDIR, scfile)
    res = spc.parse_sc(path)
    res2 = scai.parse_unit(spc.load_sc(path))
    clip = next(c for c in res['clips'] if c['id'] == clip_id)
    sids = next(list(c['sids']) for c in res2['clips'] if c['id'] == clip_id)
    n = clip['frames']
    t = clip['triples']
    assert len(t) == n, '%s: clip %d 非 1 placement/帧（%d/%d）⇒ 逐帧分组无依据' % (label, clip_id, len(t), n)
    keys = []
    for (ci, mi, _ct) in t:
        shape = sids[ci] if ci < len(sids) else None
        sub = next((list(c['sids']) for c in res2['clips'] if c['id'] == shape), None)
        if sub:
            shape = sub[0]
        assert shape is not None and shape >= 0, '%s: child %d 无 shape' % (label, ci)
        sy, dy = 1.0, 0.0
        if mi != 65535:
            a, b, cc, d, tx, ty = res['matrices'][mi]
            sy, dy = d / 1024.0, ty / 20.0
        if keys and keys[-1][0] == shape and abs(keys[-1][2] - sy) < 1e-9 and abs(keys[-1][3] - dy) < 1e-9:
            keys[-1][1] += 1
        else:
            keys.append([shape, 1, sy, dy])
    print('%-30s clip=%-5d fps=%-3d frames=%-4d shapes=%-22s keys(RLE)=%d'
          % (label, clip_id, clip['fps'], n, str(sorted({k[0] for k in keys})), len(keys)))
    return clip['fps'], keys


def turret_aim():
    """炮塔 18 个方向帧的朝向角 —— 帧号 = clip 247 的 `cnt2` sids（30..47）。

    **炮口方向 = 量到的"最远像素方向" + 180°**。为什么必须翻这 180°：
    `alpha 质心 → 最远 alpha 像素` 取到的是**炮管的远端**，而炮塔的**近端**才是判炮口/炮尾的依据 ——
    PNG 上 `f30/f31` 的近端是**光滑实心圆头 = 炮尾**（⇒ 炮口朝远/上）、`f46/f47` 的近端是
    **金环 + 暗色圆口 = 炮口**（⇒ 炮口朝下/近）；这与 `ArenaView.BlueKingTurretFrame`（蓝取 f30 = 炮口朝上）
    / `RedKingTurretFrame`（红取 f47 = 炮口朝下）两条既有常数一致。
    （裁图证据：`.ai-tmp/test/cr-fx-d/turret-zoom.png`、`ref-blue-king.png`、`ref-red-king-18.png`。）
    角度口径 = `atan2(-(row-cy), px-cx) + 180°`（归一到 ±180），即 0° = 世界 +x（屏幕右）、
    90° = 世界 +y（屏幕上）、指向 = **炮口朝向**；PNG 行号自上而下 ⇒ 取负换到 y 向上。
    """
    import math

    from PIL import Image
    out = []
    for f in range(30, 48):
        p = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Towers',
                         'building_tower_out', 'frame_%03d.png' % f)
        im = Image.open(p).convert('RGBA')
        al = im.split()[3].load()
        w, h = im.size
        n = 0
        sx = sy = 0.0
        pts = []
        for y in range(h):
            for x in range(w):
                if al[x, y] > 32:
                    n += 1
                    sx += x
                    sy += y
                    pts.append((x, y))
        assert n > 0, 'frame_%03d 全透明' % f
        cx, cy = sx / n, sy / n
        best, bd = pts[0], -1.0
        for (x, y) in pts:
            d = (x - cx) ** 2 + (y - cy) ** 2
            if d > bd:
                bd, best = d, (x, y)
        ang = math.degrees(math.atan2(-(best[1] - cy), best[0] - cx))
        ang += 180.0  # 远端像素 → 炮口方向（判据见函数文档）
        if ang > 180.0:
            ang -= 360.0
        out.append((f, ang))
    return out


def fnum(v, is_int=False):
    if is_int or abs(v - round(v)) < 1e-9:
        return '%d' % int(round(v))
    s = ('%.4f' % v).rstrip('0').rstrip('.')
    return s


def fflt(v):
    return fnum(v) + 'f'


def emit(defs, turret_dirs):
    L = []
    A = L.append
    A('namespace CR.View')
    A('{')
    A('    /// <summary>')
    A('    /// **塔层逐帧动画表**：塔上在原版里逐帧变化的那几层（国王坐像 / 公主塔乘员）的帧序列与逐帧矩阵。')
    A('    /// <para>')
    A('    /// <b>为什么需要它</b>：<see cref="ArenaView"/> 的层配方（`BlueKingRecipe` 等）只给**第 1 帧**的静态叠放，')
    A('    /// 所以塔建出来以后一动不动；而这些层在原版里是**按 `.sc` 时间轴逐帧换 `frame_NNN` / 改矩阵**的。')
    A('    /// </para>')
    A('    /// <para>')
    A('    /// <b>出处（原版数据反解，⛔ 本表是生成物、不许手改）</b>：`原版资源/sc/building_tower_v215.sc` 的')
    A('    /// clip **244**（RED 国王 idle = `KingTower_red` 的 `king_idle` 子件 245 → 244）与 clip **251**')
    A('    /// （BLUE 国王 idle = `KingTower_blue` 的 `king_idle` 子件 252 → 251）；`原版资源/sc/chr_princess_v215.sc` 的')
    A('    /// clip **753**（RED 乘员 idle = export `princess_tower_red_idle1_7`）与 clip **981**')
    A('    /// （BLUE 乘员 idle = export `princess_tower_idle1_3`）。')
    A('    /// </para>')
    A('    /// <para>')
    A('    /// **逐帧口径**：`0x0C` 记录的 `cnt1` 条 `(childIndex, matrixId, ctId)` = 逐帧放置表；这四个 clip 实测')
    A('    /// `cnt1 == frames`（每帧恰好 1 条）⇒ 第 i 帧画的子件 = 第 i 条 placement 的 `childIndex` 所指的子件；')
    A('    /// 该子件 id 若是 shapeID 就是它，若是子 clip 则取它的 `cnt2` 首项。矩阵 `0x08` = `(a,b,c,d,tx,ty)`：')
    A('    /// `sy = d/1024`、`dyPx = ty/20`（`.sc` 画布 px，**y 向下为正**）；`65535` = 无矩阵（本表记 `sy=1 / dyPx=0`）。')
    A('    /// </para>')
    A('    /// <para>生成路径：`tools/probes/sc-placement.py` + `tools/probes/sc-anim-index.py` → `tools/probes/tower-anim-gen.py`。</para>')
    A('    /// </summary>')
    A('    public static class TowerAnimTable')
    A('    {')
    A('        /// <summary>一段：帧号（`frame_NNN` 的 NNN）+ 连续帧数 + 该段的画布 y 缩放与 y 偏移。</summary>')
    A('        public readonly struct Key')
    A('        {')
    A('            /// <summary>帧号（= `.sc` shapeID = `frame_NNN` 的 NNN）。</summary>')
    A('            public readonly int Frame;')
    A('            /// <summary>本段连续播放的帧数（同帧同矩阵合并；≥ 1）。</summary>')
    A('            public readonly int Repeat;')
    A('            /// <summary>该段矩阵的 y 缩放（`0x08` 的 `d/1024`；1 = 不缩放）。</summary>')
    A('            public readonly float Sy;')
    A('            /// <summary>该段矩阵的 y 偏移（画布 px，`.sc` 口径 **y 向下为正**；写到 Unity 要取负）。</summary>')
    A('            public readonly float DyPx;')
    A('            public Key(int frame, int repeat, float sy, float dyPx)')
    A('            {')
    A('                Frame = frame; Repeat = repeat; Sy = sy; DyPx = dyPx;')
    A('            }')
    A('        }')
    A('')
    A('        /// <summary>一条动画：`.sc` 时间轴 fps + 逐段键。</summary>')
    A('        public readonly struct Anim')
    A('        {')
    A('            /// <summary>`.sc` 时间轴 fps（原版按它播）。</summary>')
    A('            public readonly int Fps;')
    A('            /// <summary>逐段键（按时间序）。</summary>')
    A('            public readonly Key[] Keys;')
    A('            /// <summary>时间轴总帧数（= 各段 <see cref="Key.Repeat"/> 之和）。</summary>')
    A('            public readonly int FrameCount;')
    A('            public Anim(int fps, Key[] keys)')
    A('            {')
    A('                Fps = fps; Keys = keys;')
    A('                var n = 0;')
    A('                for (var i = 0; i < keys.Length; i++) n += keys[i].Repeat;')
    A('                FrameCount = n;')
    A('            }')
    A('        }')
    A('')
    A('        /// <summary>队伍常量（与契约 `EntitySnapshot.team` 同值：0=BLUE 1=RED）。</summary>')
    A('        public const int TeamBlue = 0;')
    A('        /// <summary>队伍常量（与契约 `EntitySnapshot.team` 同值）。</summary>')
    A('        public const int TeamRed = 1;')
    A('')
    A('        /// <summary>炮塔的一个方向帧：帧号 + 该帧**炮口**的朝向角。</summary>')
    A('        public readonly struct TurretDir')
    A('        {')
    A('            /// <summary>帧号（`frame_NNN` 的 NNN）。</summary>')
    A('            public readonly int Frame;')
    A('            /// <summary>**炮口朝向**角（度）：0 = 世界 +x（屏幕右）、90 = 世界 +y（屏幕上），与 `Mathf.Atan2` 同口径。</summary>')
    A('            public readonly float AngleDeg;')
    A('            public TurretDir(int frame, float angleDeg)')
    A('            {')
    A('                Frame = frame; AngleDeg = angleDeg;')
    A('            }')
    A('        }')
    A('')
    A('        /// <summary>')
    A('        /// 炮塔的 **18 个方向帧**（`building_tower_v215.sc` clip 247 的 `cnt2` shapeID = 30..47）。')
    A('        /// <para>')
    A('        /// <b>炮口 / 炮尾的判据（决定角度要不要翻 180°）</b>：`alpha 质心 → 最远 alpha 像素` 取到的是**炮管的远端**，')
    A('        /// 而 PNG 上判炮口要看**近端形态** —— `f30/f31` 近端是光滑实心圆头 = **炮尾**（⇒ 炮口朝远/上）、')
    A('        /// `f46/f47` 近端是金环 + 暗色圆口 = **炮口**（⇒ 炮口朝下/近）。')
    A('        /// 两条既有常数与之一致：`ArenaView.BlueKingTurretFrame` = 30（蓝方在屏幕下方、敌人在上 ⇒ 炮口朝上）、')
    A('        /// `RedKingTurretFrame` = 47（红方在上、敌人在下 ⇒ 炮口朝下）。')
    A('        /// 故本表角度 = `atan2(-(row-cy), px-cx) + 180°`（归一到 ±180）。')
    A('        /// 参考图裁图证据：`.ai-tmp/test/cr-fx-d/turret-zoom.png`（逐帧近端）、`ref-blue-king.png`（我方）、')
    A('        /// `ref-red-king-18.png`（敌方）。')
    A('        /// <para>')
    A('        /// **覆盖范围**：18 帧是一条以 0°（右）为中心的 **180° 弧**（`f30 = +85.3°` 到 `f47 = -95.2°`）。')
    A('        /// 右半（弧外）在原版没有帧 ⇒ 调用方用 `SpriteRenderer.flipX` 镜像同一族帧补出另外 180°。')
    A('        /// </para>')
    A('        /// </summary>')
    A('        public static readonly TurretDir[] TurretDirs = new[]')
    A('        {')
    for f, a in turret_dirs:
        A('            new TurretDir(%d, %s),' % (f, fflt(round(a, 1))))
    A('        };')
    for field, doc, fps, keys in defs:
        A('')
        A('        /// <summary>%s</summary>' % doc)
        A('        private static readonly Anim %s = new Anim(%d, new[]' % (field, fps))
        A('        {')
        for k in keys:
            A('            new Key(%d, %d, %s, %s),' % (k[0], k[1], fflt(k[2]), fflt(k[3])))
        A('        });')
    A('')
    A('        /// <summary>国王坐像的 idle 动画（`team` = 0 BLUE / 1 RED）。</summary>')
    A('        public static Anim KingIdle(int team)')
    A('        {')
    A('            return team == TeamRed ? KingRed : KingBlue;')
    A('        }')
    A('')
    A('        /// <summary>公主塔乘员的 idle 动画（`team` = 0 BLUE / 1 RED）。</summary>')
    A('        public static Anim OccupantIdle(int team)')
    A('        {')
    A('            return team == TeamRed ? OccupantRed : OccupantBlue;')
    A('        }')
    A('    }')
    A('}')
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8') as f:
        f.write('\n'.join(L) + '\n')
    print('-> %s' % OUT)


if __name__ == '__main__':
    tow = 'building_tower_v215.sc'
    pri = 'chr_princess_v215.sc'
    fps_kr, keys_kr = build(tow, 244, 'RED king idle (clip 244)')
    fps_kb, keys_kb = build(tow, 251, 'BLUE king idle (clip 251)')
    fps_ob, keys_ob = build(pri, 981, 'BLUE occupant idle (clip 981)')
    fps_or, keys_or = build(pri, 753, 'RED occupant idle (clip 753)')
    print('fps: king red=%d blue=%d | occupant blue=%d red=%d' % (fps_kr, fps_kb, fps_ob, fps_or))
    print('red king keys head = %s' % keys_kr[:5])
    print('blue king keys head = %s' % keys_kb[:5])
    print('blue occ keys head = %s' % keys_ob[:4])
    print('red occ keys head = %s' % keys_or[:4])
    tdirs = turret_aim()
    print('turret dirs (clip 247 sids 30..47): %s' % [(f, round(a, 1)) for f, a in tdirs])
    emit([
        ('KingRed', 'RED 国王坐像 idle（`KingTower_red` 的 `king_idle`，clip 244，13 个姿态帧）。', fps_kr, keys_kr),
        ('KingBlue', 'BLUE 国王坐像 idle（`KingTower_blue` 的 `king_idle`，clip 251，13 个姿态帧）。', fps_kb, keys_kb),
        ('OccupantRed', 'RED 公主塔乘员 idle（export `princess_tower_red_idle1_7`，clip 753，单姿态 + 逐帧 squash 矩阵）。', fps_or, keys_or),
        ('OccupantBlue', 'BLUE 公主塔乘员 idle（export `princess_tower_idle1_3`，clip 981，单姿态 + 逐帧 squash 矩阵）。', fps_ob, keys_ob),
    ], tdirs)
