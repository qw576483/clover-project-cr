#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
check-card-art-frames.py  ——  卡面帧号表闸门（离线，秒级，不依赖 Unity）

判什么（CR-T2 的三条硬判据）：
  1. **只有一处定义**：`client/Assets/Scripts/**` 里出现「卡 key → 帧号」表的定义处**只允许 1 处**
     （= `CrUiStyle.CardArtFrameTable`）。判**过程**不判结果：它拦的是"又抄一份副本"这个动作，
     而不是"某张卡能不能查到帧号"。
  2. **卡池全覆盖**：`server/game/table/tsv/card.tsv` 的每个 `key` 在表里都有登记
     （或登记为已知缺口哨兵）⇒ 没有"卡池有这张卡、表里没有"的漏网。
  3. **素材在位**：每个帧号对应的 `Resources/Sprites/Cards/ui_spells_out/frame_NNN.png` 真在盘上。

用法： python tools/probes/check-card-art-frames.py [--scripts DIR] [--tsv FILE] [--frames DIR]
      （`--scripts` 用来做**负样本自检**：指向一棵故意放了"第二份副本"的目录 ⇒ 闸门必须 FAIL）
退出码：0 = 全 PASS；1 = 有 FAIL（附逐条原因）。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

ARGS = sys.argv[1:]


def arg(name, default):
    if name in ARGS:
        return ARGS[ARGS.index(name) + 1]
    return default


CARDS_TSV = arg("--tsv", os.path.join(ROOT, "server", "game", "table", "tsv", "card.tsv"))
SCRIPTS = arg("--scripts", os.path.join(ROOT, "client", "Assets", "Scripts"))
STYLE = os.path.join(SCRIPTS, "UI", "CrUiStyle.cs")
FRAME_DIR = arg("--frames", os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Cards", "ui_spells_out"))

FAILS = []


def fail(msg):
    FAILS.append(msg)
    print("FAIL  " + msg)


def ok(msg):
    print("PASS  " + msg)


def read(path):
    return io.open(path, encoding="utf-8-sig").read()


def parse_table(text):
    """从 CrUiStyle.cs 的 CardArtFrameTable 里取出 (key, frame) 列表。"""
    m = re.search(r"CardArtFrameTable\s*=\s*new Dictionary<string,\s*int>\s*\{(.*?)\};",
                  text, re.S)
    if not m:
        return None
    body = m.group(1)
    out = []
    for km in re.finditer(r'\{\s*"([^"]+)"\s*,\s*([A-Za-z0-9_]+)\s*\}', body):
        key, val = km.group(1), km.group(2)
        out.append((key, -1 if val == "CardArtFrameMissing" else int(val)))
    return out


def main():
    print("== CR-T2 卡面帧号表闸门 ==")
    style = read(STYLE)
    table = parse_table(style)
    if table is None:
        fail("CrUiStyle.cs 里找不到 CardArtFrameTable 的定义")
        return 1
    pairs = dict(table)
    print("CrUiStyle.CardArtFrameTable 登记 %d 条" % len(table))

    # ── 判据 1：全工程只有一处「卡 key → 帧号」表 ──
    frame_decl_re = re.compile(r"Dictionary<string,\s*int>\s+(ArtFrames|CardArtFrameTable)\b")
    crop_decl_re = re.compile(r"Dictionary<int,\s*float>\s+(ArtCropX|CardArtCropOffsetXTable)\b")
    frame_decls, crop_decls = [], []
    for dirpath, _dirs, files in os.walk(SCRIPTS):
        for fn in files:
            if not fn.endswith(".cs"):
                continue
            p = os.path.join(dirpath, fn)
            rel = os.path.relpath(p, ROOT).replace("\\", "/")
            t = read(p)
            for m in frame_decl_re.finditer(t):
                frame_decls.append((rel, m.group(1)))
            for m in crop_decl_re.finditer(t):
                crop_decls.append((rel, m.group(1)))
    expected = [d for d in frame_decls if d[1] == "CardArtFrameTable"]
    extra = [d for d in frame_decls if d[1] != "CardArtFrameTable"]
    if len(expected) == 1 and not extra:
        ok("帧号表定义处唯一：%s（%s）" % (expected[0][1], expected[0][0]))
    else:
        fail("帧号表定义处不唯一/有残留副本：%s" % (frame_decls,))
    if len(crop_decls) == 1 and crop_decls[0][1] == "CardArtCropOffsetXTable":
        ok("逐帧裁剪偏移例外表定义处唯一：%s（%s）" % (crop_decls[0][1], crop_decls[0][0]))
    else:
        fail("逐帧裁剪偏移例外表不唯一/有残留副本：%s" % (crop_decls,))

    # 逐面板确认不再自存：HudPanel / DeckEditPanel 都不得出现字典字面量里的卡 key
    for rel in ("UI/Panels/HudPanel.cs", "UI/Panels/DeckEditPanel.cs"):
        t = read(os.path.join(SCRIPTS, rel)) if os.path.exists(os.path.join(SCRIPTS, rel)) else ""
        m = re.search(r'"(knight|archers|goblins)"\s*,\s*\d+', t)
        if m:
            fail("%s 里仍有写死的「key → 帧号」字面量（%s）" % (rel, m.group(0)))
        else:
            ok("%s 无写死的卡 key → 帧号字面量" % rel)

    # ── 判据 2：卡池全覆盖 ──
    keys = []
    for line in read(CARDS_TSV).splitlines():
        c = line.split("\t")
        if len(c) >= 3 and c[0] != "id" and c[1]:
            keys.append(c[1])
    missing = [k for k in keys if k not in pairs]
    if missing:
        fail("卡池 %d 张里 %d 张没登记帧号：%s" % (len(keys), len(missing), missing))
    else:
        ok("卡池 %d 张全部有登记（其中已知缺口 %d 张：%s）" % (
            len(keys),
            sum(1 for k in keys if pairs.get(k) == -1),
            [k for k in keys if pairs.get(k) == -1]))

    # ── 判据 3：素材在位 ──
    gone = []
    for k, f in table:
        if f < 0:
            continue
        p = os.path.join(FRAME_DIR, "frame_%03d.png" % f)
        if not os.path.exists(p):
            gone.append((k, f))
    if gone:
        fail("%d 个帧号在盘上没有 PNG：%s" % (len(gone), gone))
    else:
        ok("全部帧号的 PNG 在盘上（%s）" % FRAME_DIR)

    print()
    if FAILS:
        print("SUMMARY FAIL=%d" % len(FAILS))
        return 1
    print("SUMMARY FAIL=0")
    return 0


if __name__ == "__main__":
    sys.exit(main())
