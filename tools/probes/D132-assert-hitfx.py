#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
D132 战斗特效链路的**能失败的断言**（离线读数，秒级；判据资产 ⇒ 落 tools/probes/）。

用法（项目根下）:
    python tools/probes/D132-assert-hitfx.py
    （读 .ai-tmp/test/D132-live-evidence.txt；把结论写 .ai-tmp/test/D132-assert-hitfx.out.txt；
      任一条 FAIL ⇒ exit 1）

判的具体是什么（判"过程"不判"结果"—— 数字是运行时由被测程序自己写的）：
  1. **每次被观察到的 hp 下降 ⇒ 恰好一次命中特效**
     `HitFxPlayed + HitFxSkipped == HpDropObserved` 且 `HpDropObserved > 0`
     （出处 = `client/Assets/Scripts/View/BattleViewRoot.cs` 的 `TrackCombatSignals` / `PlayHitFx`；
      它按 `BattleAudioView.OnSnapshot` 同源口径逐 id 比对前后两帧快照的 hp）。
  2. **出牌落地特效真的播出去了**：`DeployFxPlayed > 0`
     （出处 = `BattleViewRoot.PlayDeploy` ← `EvSpawn`，`server/game/core/combat.go:389-391`）。
  3. **战斗中开火弹道真的播出去了**：`BattleProjectileShots > 0`
     （出处 = `BattleViewRoot.PlayBattleShot`，anim==2 + `UnitAnimTable.HitSpeedMs` 节拍）。
  4. **特效层的失败率**：`FxSpawned / (FxSpawned + FxSkipped)` 必须能被读到（`EffectsView` 的两个计数
     至少要出现一次；`FxSpawned > 0`）—— 防"调用成功但资源目录是空的"这种静默失效。

⛔ 这几个量都只有跑起来才有 ⇒ 本脚本不能替代 Play，只判"跑完之后读数是否自洽"。

⚠️ 两条**读侧**的坑（team-lead 广播，2026-09-23）：
  * 本脚本只读 driver 的读数与 console 落盘，**不**读 `play-log.tsv`。若将来有人让本脚本按台账判
    "谁持有锁"，**一律按文件序**（append-only 的文件序才是真先后），⛔ 不要按时间戳排序 ——
    D132 自己就写过手写估算的未来时间戳（NOTE-2 戳 19:42:00 / 真实约 19:36）。
    正确形式 = 找最后一条该 tag 的 `START`，其后（文件序）无同 tag 的 `END` ⇒ 持有锁。
  * 另一个同族坑：**文字类证据只从 ASCII 段解**（`proj_speed=` / `card=` / `(x,y)` / `D48`），
    console 的中文可能是 mojibake（见 D132-fx-report §5.3）。
"""
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
EV = os.path.join(ROOT, ".ai-tmp", "test", "D132-live-evidence.txt")
OUT = os.path.join(ROOT, ".ai-tmp", "test", "D132-assert-hitfx.out.txt")

COUNTER_RE = re.compile(r"COUNTERS(.*)")
KV_RE = re.compile(r"(\w+)=([-\w.?]+)")


def last_counters(text):
    """取**最后一条** COUNTERS 行（= 本局结束时的累计值）。"""
    got = None
    for line in text.splitlines():
        m = COUNTER_RE.search(line)
        if m:
            got = dict(KV_RE.findall(m.group(1)))
    return got


def last_assert(text):
    got = None
    for line in text.splitlines():
        if "ASSERT hitfx-per-hpdrop" in line:
            got = line.strip()
    return got


def find_shotexpect(text):
    """driver 在注入"远程单位攻击档"那一帧时写下的机器可读期望行。"""
    got = None
    for line in text.splitlines():
        if "SHOTEXPECT" in line:
            got = line.strip()
    return got


def fx_dumps(text):
    """所有 FX 快照行（before/after）。"""
    return [ln for ln in text.splitlines() if " FX[" in ln]


def main():
    if not os.path.exists(EV):
        print("BLOCKED: 读数文件不存在 %s" % EV)
        return 2
    with open(EV, "r", encoding="utf-8", errors="replace") as f:
        text = f.read()

    c = last_counters(text)
    a = last_assert(text)
    lines = []
    fails = []

    if c is None:
        fails.append("没有任何 COUNTERS 行（driver 没跑到对局里？）")
    if a is None:
        fails.append("没有任何 ASSERT hitfx-per-hpdrop 行（OnSnapshot/TrackCombatSignals 没被调到？）")

    if c is not None:
        def num(k):
            try:
                return int(c.get(k, "?"))
            except ValueError:
                return None

        hp = num("HpDropObserved")
        ok = num("HitFxPlayed")
        sk = num("HitFxSkipped")
        dep = num("DeployFxPlayed")
        shot = num("BattleProjectileShots")
        cshot = num("CardProjectileShots")
        fxsp = num("FxSpawned")

        lines.append("读数（最后一条 COUNTERS）：HpDropObserved=%s HitFxPlayed=%s HitFxSkipped=%s "
                     "DeployFxPlayed=%s BattleProjectileShots=%s FxSpawned=%s FxSkipped=%s"
                     % (hp, ok, sk, dep, shot, fxsp, c.get("FxSkipped")))

        if None in (hp, ok, sk):
            fails.append("命中三计数缺失（HpDropObserved/HitFxPlayed/HitFxSkipped 有一个读不到）")
        else:
            if hp <= 0:
                fails.append("HpDropObserved=%d（本局一次 hp 下降都没观察到 ⇒ 判据没有生效，不是 PASS）" % hp)
            if ok + sk != hp:
                fails.append("命中次数不一致：played(%d)+skipped(%d)=%d != observed(%d)" % (ok, sk, ok + sk, hp))
            else:
                lines.append("命中一致性 PASS：played(%d)+skipped(%d)==observed(%d)" % (ok, sk, hp))

        if dep is None or dep <= 0:
            fails.append("DeployFxPlayed=%s（出牌落地特效一次都没播出去）" % dep)
        else:
            lines.append("落地特效 PASS：DeployFxPlayed=%d" % dep)

        if shot is None or shot <= 0:
            fails.append("BattleProjectileShots=%s（战斗中开火一次弹道都没播出去）" % shot)
        else:
            lines.append("战斗弹道 PASS：BattleProjectileShots=%d" % shot)

        # 任务 3 点名的 `:805-808 / 847 / 870-874` 是**出牌**那条路径（`PlayProjectileFlight`）。
        # 它有两个早退分支会让 `CardProjectileShots` 不增：卡不在池里 / `proj_speed<=0` / `dist==0`
        # （后者见 `PlayProjectileFlight`：落点压在施法者身上 ⇒ 退化为落点命中闪光）。
        # 所以 `>0` 才证明"真的飞了一条"，而不是被某个早退分支吞掉。
        if cshot is None or cshot <= 0:
            fails.append("CardProjectileShots=%s（出牌弹道一次都没真的飞起来 ⇒ 可能全被 早退分支 吞了）" % cshot)
        else:
            lines.append("出牌弹道 PASS：CardProjectileShots=%d" % cshot)

        if fxsp is None or fxsp <= 0:
            fails.append("FxSpawned=%s（EffectsView 一次都没成功建出节点 ⇒ 特效资源目录/上限有问题）" % fxsp)

    # 5. **弹道必须从攻击者位置生成**（任务 3 的核心：不许再从国王塔中心飞出）
    #    用**距离**判、不用字符串相等判：特效节点的世界坐标会随 `Update` 极慢地往前挪
    #    （0.24s 的航程在 timeScale=0.02 下 = 12s 真实时间 ⇒ 截图之间挪一点点是正常的），
    #    所以判据 = "离攻击者 < 1.5 格" 且 "离两座国王塔中心都 > 3 格"。
    #    两座国王塔世界坐标出处：`GameConst.TileToWorld(9, MirrorTileYForTeam(3, team))`
    #    = team0 (0,-13) / team1 (0,13)（`Core/GameConst.cs:61-64, 97-100, 124-127`）。
    TOWERS = ((0.0, -13.0), (0.0, 13.0))
    exp = find_shotexpect(text)
    if exp is None:
        fails.append("没有 SHOTEXPECT 行（driver 第 4 步没跑到？）")
    else:
        lines.append("注入期望行：%s" % exp)
        m = re.search(r"attacker_world=\(([-\d.]+),([-\d.]+)\)", exp)
        ms = re.search(r"expect_sprite=(\w+)", exp)
        if not m or not ms:
            fails.append("SHOTEXPECT 行解析不出 attacker_world / expect_sprite：%s" % exp)
        else:
            ax, ay = float(m.group(1)), float(m.group(2))
            want_sprite = ms.group(1)
            pos_re = re.compile(r"sprite=(\w+)\s+world=\(([-\d.]+),([-\d.]+)\)")
            seen = []
            for ln in fx_dumps(text):
                for sp, xs, ys in pos_re.findall(ln):
                    # 精灵名带 `_0` 子图后缀（`LoadViaTexture` 造出来的叫 `gen_frame_440_0`），
                    # 期望行里写的是不带后缀的 `gen_frame_440` ⇒ 前缀匹配。
                    if sp == want_sprite or sp.startswith(want_sprite + "_"):
                        seen.append((float(xs), float(ys), ln.strip()))
            if not seen:
                fails.append("弹道起点不成立：FX 快照里一次都没看到 %s（弹道没播出去，或特效层取不到 Arrow 帧）"
                             % want_sprite)
            else:
                near_atk = [(x, y, ln) for (x, y, ln) in seen
                            if ((x - ax) ** 2 + (y - ay) ** 2) ** 0.5 < 1.5]
                if near_atk:
                    x, y, ln = near_atk[-1]
                    lines.append("弹道起点 PASS：%s 出现在 (%0.2f,%0.2f)，距攻击者 (%0.2f,%0.2f) = %0.2f 格（<1.5）"
                                 % (want_sprite, x, y, ax, ay, ((x - ax) ** 2 + (y - ay) ** 2) ** 0.5))
                    lines.append("  raw: %s" % ln[:200])
                else:
                    worst = min(seen, key=lambda t: ((t[0] - ax) ** 2 + (t[1] - ay) ** 2))
                    fails.append("弹道起点不成立：%s 各次出现都离攻击者 (%0.2f,%0.2f) ≥1.5 格，最近的一次 = (%0.2f,%0.2f) "
                                 "⇒ 不是从攻击者位置生成" % (want_sprite, ax, ay, worst[0], worst[1]))
                for tx, ty in TOWERS:
                    bad = [t for t in seen if ((t[0] - tx) ** 2 + (t[1] - ty) ** 2) ** 0.5 < 3.0]
                    if bad:
                        fails.append("弹道起点错误：%s 出现在国王塔中心 (%0.2f,%0.2f) 附近 %0.2f 格（D48 的降级值，任务 3 要求修掉）"
                                     % (want_sprite, tx, ty, ((bad[0][0] - tx) ** 2 + (bad[0][1] - ty) ** 2) ** 0.5))

    # 7. **降级必须留痕**：自然对战里 `EvPlayCard` 会先于「该卡的单位进入快照」到达
    #    （`server/game/core/battle.go:219-221` 又不带施法者 id，= D48）⇒ `CasterWorld` 找不到本方单位、
    #    退回国王塔中心。这条路径**允许存在**，但**必须 Warn 一次**（⛔ 不许静默）。
    #    只读 console 的 **ASCII** 片段（中文是 mojibake，但 `proj_speed=` / `card=` / `(x,y)` / `D48` 都在
    #    ASCII 段里 ⇒ 与编码无关，将来修好编码也照样能判）。
    #    判据：① 至少有一条弹道从**非塔位置**起飞（否则"修好了"不成立）；② 从塔中心起飞的条数
    #    必须**等于** Warn 留痕条数（否则就是静默降级）。
    TOWER_NEAR = 0.6
    # 首选 console 落盘；它若被覆盖/缺失（本项目真的发生过：一次 UTF-8 重取脚本参数没传进去，
    # 把 1.8 MB 日志覆盖成了 6.7 KB 的 `unity` usage 文本），**回落到 run.out.txt** ——
    # `D132-run.ps1` 会把同一段文本同时 W 进 run.out.txt，两份内容同源同编码。
    cs = ""
    used = None
    for cand in (".ai-tmp/test/D132-console.log.txt", ".ai-tmp/test/D132-run.out.txt"):
        cp = os.path.join(ROOT, *cand.split("/"))
        if not os.path.exists(cp):
            continue
        with open(cp, "r", encoding="utf-8", errors="replace") as f:
            t = f.read()
        if "proj_speed=" in t:
            cs, used = t, cand
            break
    cpath = used
    if not cs:
        lines.append("弹道起点分布：跳过（console 落盘与 run.out.txt 里都没有 `proj_speed=` —— "
                     "只差「人读日志」这一层，不影响上面的判据）")
    else:
        lines.append("弹道起点分布：数据源 = %s" % cpath)
        segs = [x for x in cs.split("[BattleViewRoot]") if "proj_speed=" in x]
        coord = re.compile(r"\(([-\d.]+),([-\d.]+)\)")
        origins = []
        for seg in segs:
            head = seg[:260]
            cs2 = coord.findall(head)
            if len(cs2) >= 2:
                origins.append((float(cs2[0][0]), float(cs2[0][1])))
        # ⚠️ 只用 `D48` 这个 ASCII 标记，⛔ 不要用 `team=`：mojibake 会把全角冒号和 `t` 并成一个字
        #    （`：t` 的 UTF-8 是 EF BC 9A 74，按 GBK 两两切成 锛 + 歵）⇒ 文本里根本不存在 `team=` 子串。
        #    `D48` 三字母相邻、不带前后全角字符，两种编码下都完好；且这个 token 只出现在该 Warn 里。
        warns = sum(1 for x in cs.split('"level":"warn"') if "D48" in x[:600])
        n_tower = sum(1 for (x, y) in origins
                      if min(((x - tx) ** 2 + (y - ty) ** 2) ** 0.5 for tx, ty in TOWERS) < TOWER_NEAR)
        n_unit = len(origins) - n_tower
        lines.append("弹道起点分布（console 的 ASCII 段）：共 %d 条出牌弹道，其中从**单位位置**起飞 %d 条 / "
                     "从**国王塔中心**起飞（降级）%d 条；降级 Warn 留痕 %d 条" % (len(origins), n_unit, n_tower, warns))
        if not origins:
            fails.append("弹道起点分布：一条出牌弹道日志都没有（这一局没有出牌，或 Info 没打出来）⇒ 判不了")
        elif n_unit == 0:
            fails.append("弹道起点分布：%d 条**全部**从国王塔中心起飞 ⇒ 任务 3 的修复没有生效" % n_tower)
        elif n_tower != warns:
            fails.append("弹道起点分布：从塔中心起飞 %d 条，但降级 Warn 只有 %d 条 ⇒ 存在**静默降级**"
                         % (n_tower, warns))

    if a is not None:
        lines.append("driver 内联断言行：%s" % a)
        n_all = sum(1 for ln in text.splitlines() if "ASSERT hitfx-per-hpdrop" in ln)
        n_fail = sum(1 for ln in text.splitlines() if "ASSERT hitfx-per-hpdrop FAIL" in ln)
        if n_fail > 0:
            lines.append("内联断言共 %d 次、其中 FAIL %d 次（FAIL 出现在「本局还没发生过 hp 下降」的早期步 ⇒ "
                         "说明这条断言**是能被证伪的**，不是恒真）" % (n_all, n_fail))
        else:
            # 本局恰好第一帧就有 hp 下降 ⇒ 这一次没出现 FAIL。证伪性由更早的几轮留痕：
            # run4/run5 的同一条断言在 observed=0 的早期步判过 FAIL（见 D132-run.out-run4/run5.txt）。
            lines.append("内联断言共 %d 次、FAIL 0 次（本轮第一帧起就有 hp 下降，条件 observed>0 从未落空）。"
                         "该断言的**可证伪性**由 run4/run5 留痕：同一条断言在 observed=0 的早期步判过 FAIL"
                         "（D132-run.out-run4.txt / -run5.txt 各 2 次）" % n_all)
        if "FAIL" in a:
            fails.append("driver 内联断言判 FAIL：%s" % a)

    verdict = "PASS" if not fails else "FAIL"
    out = ["=== D132 hitfx / deploy / projectile assert ==="]
    out += lines
    for f_ in fails:
        out.append("FAIL: " + f_)
    out.append("VERDICT %s" % verdict)
    body = "\n".join(out) + "\n"
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)
    print(body)
    return 0 if verdict == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
