#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
d129c-apply-view.py -- 把 D129c 的「朝向 → 视角」接线落进 `client/Assets/Scripts/View/UnitView.cs`
====================================================================================================
为什么用脚本而不是手改（本片纪律）：每一次替换都**先断言原文唯一命中**，命中数 ≠ 1 就**整体不落盘**
（`--apply` 是全量原子写）⇒ ① 目标文件被别的并行片改过时**立刻报错**而不是错改；
② `--revert` 能把改动**逐字撤回**（改完立刻 `--revert` 再 `--apply` 一次，可自证脚本可逆）。

落的是什么（4 处，全部**加法式**：任一时刻都不破编译）
--------------------------------------------------------
P1  `UnitView` 新增 5 个字段：`_viewStep`（16 档朝向档）/ `_viewStepSet` / `_viewNo`（1..9）/
    `_lastWorldPos` + `_hasPos` / `_moveDir` + `_hasMoveDir`。
P2  `Apply()`：原来的 `_renderer.flipX = e.facing < 0` 换成 `UpdateFacing(worldPos, e.facing)`；
    视角号变了就**重算 `_clip`**（保 `_pos`，只夹到新序列长度内 —— 转身是同一姿态换角度，
    ⛔ 不许把走路从头播）+ `flipX = UnitAnimTable.StepFlip(_viewStep)`。
P3  `ClipIndices(dir, anim)` → `ClipIndices(dir, anim, view)`：帧段取
    `UnitAnimTable.RunsForView(clip, view) ?? clip.Runs`（该视角无素材 ⇒ 回落默认视角），
    缓存键加 `view`。
P4  `FpsFor(dir, anim)` → `FpsFor(dir, anim, view)`：帧数按当前视角算（attack 的 fps 随之缩放
    ⇒ 「攻击段播完一遍 == hit_speed_ms」对任何视角都成立）。
另加 `UpdateFacing()` / `Wrap16()` 两个私有方法（朝向档的滞回见方法注释）。

方向的语义（⛔ 不是猜 —— 出处 `tools/probes/d129c-view-map.tsv`，由 `d129c-yaw-verify.py` 断言）
--------------------------------------------------------------------------------------------
`φ = atan2(dy, dx)`（度；从 **+x 轴**起算、**+y = 远离镜头/上场方向**）
`step = round((90° − φ) / 22.5°) mod 16`；`view = StepToView[step]`；`flip = step > 8`。
⇒ 蓝方往上走（+y）看到的是**背身**（`_1`），往下走看到**正面**（`_9`），左右走是侧身（`_5` + 镜像）。

复跑
----
  /c/Python312/python tools/probes/d129c-apply-view.py --root . --check    # 只断言，不落盘
  /c/Python312/python tools/probes/d129c-apply-view.py --root . --apply    # 落盘（原子写）
  /c/Python312/python tools/probes/d129c-apply-view.py --root . --revert   # 逐字撤回
"""
import argparse
import hashlib
import io
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

REL = ('client', 'Assets', 'Scripts', 'View', 'UnitView.cs')

NEW_FIELDS = '''        private bool _barVisible = true;

        // ───────────────── D129c：朝向 → 视角（9 视角 / 16 档朝向）─────────────────
        /// <summary>朝向档（0..15，见 <see cref="UnitAnimTable.StepToView"/>）。初值 4 = 侧身朝右（φ=0°）。</summary>
        private int _viewStep = 4;
        /// <summary>是否已由**实际移动方向**定过朝向（false ⇒ 首帧用契约 `facing` 的左右兜底）。</summary>
        private bool _viewStepSet;
        /// <summary>当前视角号（1..9）= `UnitAnimTable.StepToView[_viewStep]`；变号时必须重算 `_clip`。</summary>
        private int _viewNo = 5;
        /// <summary>上一帧的世界坐标（求移动方向）；`_hasPos == false` = 还没有上一帧。</summary>
        private Vector2 _lastWorldPos;
        private bool _hasPos;
        /// <summary>最近一次**有效**移动方向（未归一化；站桩时保持上一次朝向）。</summary>
        private Vector2 _moveDir = new Vector2(1f, 0f);
        private bool _hasMoveDir;
'''

NEW_FACING = '''            // 朝向 → 视角（D129c）：用**前后两帧世界坐标的位移**求朝向极角 φ，再查映射表选视角
            //（`UnitAnimTable.StepToView`，出处 `tools/probes/d129c-view-map.tsv`，由
            // `tools/probes/d129c-yaw-verify.py` 的断言 A1–A6 + 负控保证）。
            // 契约 `facing` ∈ {-1, 1} 只作为"一次都还没动过"时的左右兜底（与旧行为一致）。
            if (UpdateFacing(worldPos, e.facing))
            {
                // 转身只是**换角度**、不是换动作 ⇒ 保持 `_pos`（只夹到新序列长度内），
                // ⛔ 不许把走路/攻击从头播（那会让转身看起来像"卡了一下"）。
                _clip = ClipIndices(_spriteDir, _anim, _viewNo);
                if (_clip != null && _clip.Length > 0) _pos = Mathf.Clamp(_pos, 0, _clip.Length - 1);
                RefreshSprite();
            }
            if (_renderer != null) _renderer.flipX = UnitAnimTable.StepFlip(_viewStep);
'''

NEW_BIND_RESET = '''            // D129c：换宿主必须把"已定朝向"复位 —— 新单位的第一帧还没有移动方向，
            // 否则会**继承上一个宿主的朝向**（池复用的静默串味）。
            _viewStep = 4;                     // 4 = 侧身朝右（φ=0°）= 旧行为（不翻）
            _viewStepSet = false;
            _hasPos = false;
            _hasMoveDir = false;
            _viewNo = UnitAnimTable.StepToView[_viewStep];
            _clip = ClipIndices(_spriteDir, _anim, _viewNo);
            _pos = 0;
'''

NEW_FACING_METHODS = '''        /// <summary>朝向档切换的**滞回半宽**（单位 = 档；0.25 档 = 5.625°）—— 避免在档边界上左右跳。</summary>
        private const float ViewHysteresisSteps = 0.25f;

        /// <summary>
        /// 由**移动方向**更新朝向档；返回 `_viewNo` 是否变化（变化时调用方必须重算 `_clip`）。
        /// <para>
        /// 算式（出处 `tools/probes/d129c-view-map.tsv`）：`φ = atan2(dy, dx)`（度，+x 起算、+y = 远离镜头）
        /// ⇒ `u = (90° − φ) / 22.5°`（连续档位，0 = 背身）⇒ `step = round(u) mod 16`。
        /// 站桩（位移 ≈ 0）保持上一次朝向；一次都没动过 ⇒ 用 `facing` 的左右兜底（φ = 0° 或 180°）。
        /// </para>
        /// </summary>
        private bool UpdateFacing(Vector2 worldPos, int facing)
        {
            if (_hasPos)
            {
                var dx = worldPos.x - _lastWorldPos.x;
                var dy = worldPos.y - _lastWorldPos.y;
                if (dx * dx + dy * dy > 1e-8f)   // 1e-4 格：远小于任何真实位移，只滤掉浮点噪声
                {
                    _moveDir = new Vector2(dx, dy);
                    _hasMoveDir = true;
                }
            }
            _lastWorldPos = worldPos;
            _hasPos = true;

            var dirv = _hasMoveDir ? _moveDir : new Vector2(facing >= 0 ? 1f : -1f, 0f);
            var u = (90f - Mathf.Atan2(dirv.y, dirv.x) * Mathf.Rad2Deg) / 22.5f;

            var step = _viewStep;
            if (!_viewStepSet)
            {
                _viewStepSet = true;
                step = Wrap16(Mathf.RoundToInt(u));      // 首次直接吸附（不做滞回）
            }
            else
            {
                var diff = u - _viewStep;
                diff -= Mathf.Round(diff / 16f) * 16f;   // 折到 [-8, 8]（环绕）
                if (Mathf.Abs(diff) > 0.5f + ViewHysteresisSteps) step = Wrap16(Mathf.RoundToInt(u));
            }

            if (step == _viewStep) return false;
            _viewStep = step;
            _viewNo = UnitAnimTable.StepToView[step];

            int[] cnt;
            if (!ViewSwitchCount.TryGetValue(_spriteDir ?? string.Empty, out cnt))
            {
                cnt = new int[9];
                ViewSwitchCount[_spriteDir ?? string.Empty] = cnt;
            }
            cnt[_viewNo - 1]++;
            return true;
        }

        private static int Wrap16(int v)
        {
            v %= 16;
            return v < 0 ? v + 16 : v;
        }

'''

NEW_DIAG = '''        /// <summary>当前 `anim` 档位。</summary>
        public int Anim => _anim;

        /// <summary>当前**视角号**（1..9）—— D129c 的朝向选择结果，供实机采样/回归脚本读取。</summary>
        public int ViewNo => _viewNo;

        /// <summary>当前**朝向档**（0..15）—— `UnitAnimTable.StepToView` 的下标。</summary>
        public int ViewStep => _viewStep;

        /// <summary>最近一次有效的移动方向（未归一化；`_hasMoveDir == false` 时是 `facing` 的左右兜底）。</summary>
        public Vector2 MoveDir => _moveDir;

        /// <summary>
        /// D129c 回归计数器：`dir → 长度 9 的数组`（下标 = 视角号 − 1，值 = 该视角被**切入**的次数）。
        /// 判据出处 = D129b 的「苍蝇海 1 秒 9 次视角」回归：修好后每个单位的视角切换次数应远少于
        /// 每秒 1 次（同向移动的单位切一次就稳定）。
        /// </summary>
        private static readonly Dictionary<string, int[]> ViewSwitchCount = new Dictionary<string, int[]>();

        /// <summary>视角切换计数的汇总（每个目录一行 `dir total=N [_1=a _2=b …]`）。</summary>
        public static string ViewSwitchReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in ViewSwitchCount)
            {
                var total = 0;
                for (var i = 0; i < kv.Value.Length; i++) total += kv.Value[i];
                sb.Append(kv.Key).Append(" total=").Append(total).Append(" [");
                for (var i = 0; i < kv.Value.Length; i++)
                    sb.Append(i == 0 ? "" : " ").Append('_').Append(i + 1).Append('=').Append(kv.Value[i]);
                sb.Append("] ; ");
            }
            return sb.Length == 0 ? "(无视角切换)" : sb.ToString();
        }

        /// <summary>清空视角切换计数（驱动脚本在每个采样段开始时调，便于分段统计）。</summary>
        public static void ResetViewSwitchCount() { ViewSwitchCount.Clear(); }
'''

# (名字, 原文, 新文) —— 每条的原文必须在文件里**恰好出现一次**，否则整体放弃
REPLACEMENTS = [
    ('P1 字段',
     '        private bool _barVisible = true;\n',
     NEW_FIELDS),
    ('P2 Apply 朝向',
     '            // 朝向：契约 `facing` ∈ {-1, 1}；只有"向左"才翻。\n'
     '            if (_renderer != null) _renderer.flipX = e.facing < 0;\n',
     NEW_FACING),
    ('P2b 朝向方法',
     '        /// <summary>\n'
     '        /// 当前档位的**帧下标序列**（把帧段表的**帧号**经 <see cref="SpriteBank.FrameNumberMap"/> 换算成下标）。\n',
     NEW_FACING_METHODS
     + '        /// <summary>\n'
       '        /// 当前档位的**帧下标序列**（把帧段表的**帧号**经 <see cref="SpriteBank.FrameNumberMap"/> 换算成下标）。\n'),
    ('P3 Bind 复位',
     '            _clip = ClipIndices(_spriteDir, _anim);\n'
     '            _pos = 0;\n',
     NEW_BIND_RESET),
    ('P3b 换档',
     '                _clip = ClipIndices(_spriteDir, _anim);\n',
     '                _clip = ClipIndices(_spriteDir, _anim, _viewNo);\n'),
    ('P5 Update fps',
     '            var fps = FpsFor(_spriteDir, _anim);\n',
     '            var fps = FpsFor(_spriteDir, _anim, _viewNo);\n'),
    ('P6 公开诊断口',
     '        /// <summary>当前 `anim` 档位。</summary>\n'
     '        public int Anim => _anim;\n',
     NEW_DIAG),
    ('P3c ClipIndices 签名',
     '        private int[] ClipIndices(string dir, int anim)\n',
     '        private int[] ClipIndices(string dir, int anim, int view)\n'),
    ('P3d ClipIndices 缓存键',
     '            var key = (int)_pivotMode + "|" + _spritePath + "|" + anim;\n',
     '            var key = (int)_pivotMode + "|" + _spritePath + "|" + anim + "|" + view;\n'),
    ('P3e ClipIndices 取段',
     '                var clip = entry.Tiers[anim];\n'
     '                var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);\n'
     '                var list = new List<int>(clip.Count);\n'
     '                var runs = clip.Runs;\n',
     '                var clip = entry.Tiers[anim];\n'
     '                // D129c：优先取**当前朝向对应视角**的帧段（`ViewRuns[view-1]`，出处\n'
     '                // `.ai-tmp/test/D129b-clip-segments.tsv`）；该视角无素材 ⇒ 回落 `clip.Runs`（默认视角）。\n'
     '                var runs = UnitAnimTable.RunsForView(clip, view) ?? clip.Runs;\n'
     '                var want = UnitAnimTable.CountOfRuns(runs);\n'
     '                var map = SpriteBank.FrameNumberMap(_spritePath, _pivotMode);\n'
     '                var list = new List<int>(want);\n'),
    ('P3f ClipIndices 告警',
     '段表帧数={clip.Count} frames.Length={_frames.Length}");',
     'view={view} 段表帧数={want} frames.Length={_frames.Length}");'),
    ('P4 FpsFor 签名',
     '        private static float FpsFor(string dir, int anim)\n',
     '        private static float FpsFor(string dir, int anim, int view)\n'),
    ('P4b FpsFor 取段',
     '                var clip = entry.Tiers[anim];\n'
     '                if (anim == AnimIdle) return 0f;                                  // 静止帧：不播\n'
     '                if (anim == AnimAttack && entry.HitSpeedMs > 0)\n'
     '                    return clip.Count / (entry.HitSpeedMs / 1000f);               // = 攻击段帧数 ÷ 秒\n',
     '                var clip = entry.Tiers[anim];\n'
     '                // D129c：帧数按**当前视角**算（各视角帧数可能不同；attack 的 fps 随之缩放 ⇒\n'
     '                // 「攻击段播完一遍 == hit_speed_ms」这条对任何视角都成立）。\n'
     '                var runs = UnitAnimTable.RunsForView(clip, view) ?? clip.Runs;\n'
     '                if (anim == AnimIdle) return 0f;                                  // 静止帧：不播\n'
     '                if (anim == AnimAttack && entry.HitSpeedMs > 0)\n'
     '                    return UnitAnimTable.CountOfRuns(runs) / (entry.HitSpeedMs / 1000f);   // = 攻击段帧数 ÷ 秒\n'),
]


def sha16(b):
    return hashlib.sha256(b).hexdigest()[:16].upper()


def read_keep_eol(path):
    """读文件并**保留原始换行**（⛔ `io.open` 默认会把 CRLF 归一成 LF；若写回时不还原，
    整个文件 1153 行的换行都会被改掉 —— 既产生整文件噪声 diff，又毁掉字节锚点）。"""
    src = io.open(path, encoding='utf-8', newline='').read()
    return src, ('\r\n' if src.count('\r\n') * 2 > src.count('\n') else '\n')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--check', action='store_true', help='只断言替换命中数，不落盘')
    ap.add_argument('--apply', action='store_true', help='落盘（原子写）')
    ap.add_argument('--revert', action='store_true', help='逐字撤回')
    a = ap.parse_args()
    if not (a.check or a.apply or a.revert):
        ap.error('必须给 --check / --apply / --revert 之一')

    path = os.path.join(os.path.abspath(a.root), *REL)
    src, eol = read_keep_eol(path)
    before = sha16(src.encode('utf-8'))
    print('目标 %s\n  sha256[:16] = %s｜换行 = %s｜行数 = %d'
          % (path, before, 'CRLF' if eol == '\r\n' else 'LF', src.count('\n')))

    def conv(s):
        """把脚本里以 LF 书写的模板转成目标文件实际使用的换行。"""
        return s if eol == '\n' else s.replace('\n', eol)

    if a.revert:
        pairs = [(n, conv(new), conv(old)) for n, old, new in REPLACEMENTS]
        verb = '撤回'
    else:
        pairs = [(n, conv(old), conv(new)) for n, old, new in REPLACEMENTS]
        verb = '落盘'

    text, fails = src, []
    for name, old, new in pairs:
        n = text.count(old)
        if n != 1:
            fails.append('%s：原文命中 %d 次（必须恰好 1 次）' % (name, n))
            continue
        text = text.replace(old, new)
        print('  ✓ %s' % name)
    if fails:
        print('\n!! %s 被放弃（%d 条未命中）：' % (verb, len(fails)))
        for f in fails:
            print('   - %s' % f)
        return 2

    after = sha16(text.encode('utf-8'))
    if a.check:
        print('\n[--check] 全部 %d 条命中；预计 sha256[:16] %s -> %s（**未落盘**）'
              % (len(pairs), before, after))
        return 0
    io.open(path, 'w', encoding='utf-8', newline='').write(text)
    print('\n[%s] 已写入；sha256[:16] %s -> %s（%d 条；换行仍为 %s）'
          % (verb, before, after, len(pairs), 'CRLF' if eol == '\r\n' else 'LF'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
