#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2b-frame-scan.py —— CR-T2b 找「原版金色卡框 / 卡顶徽章 / 手牌背板」用（只读；除联络图外不写盘）。

用法：
  python tools/probes/cr-t2b-frame-scan.py name <关键词正则>   # 在 §3.x 全量表里按 export 名筛
  python tools/probes/cr-t2b-frame-scan.py frame <帧号>        # 反查：某帧号被哪些 export 名用到
  python tools/probes/cr-t2b-frame-scan.py dirs                # 本地 frame_*.png 落盘分布
  python tools/probes/cr-t2b-frame-scan.py sheet 43,200,531    # 把指定帧号拼联络图（白底）
"""
import io
import os
import re
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
INDEX = os.path.join(ROOT, "策划", "原版UI素材名称索引.md")
OUT = os.path.join(ROOT, ".ai-tmp", "test")

ROW = re.compile(r"^\|\s*(\d+)\s*\|\s*`([^`]+)`\s*\|\s*([\d,\s]*)\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*`([\d,]+)`\s*\|\s*([^|]*)\|")


def rows():
    lines = io.open(INDEX, encoding="utf-8-sig", errors="replace").read().splitlines()
    sec = None
    for l in lines:
        if l.startswith("### 3."):
            sec = l.strip().strip("#").strip()
        m = ROW.match(l.strip())
        if m and sec:
            yield sec, m.group(2), m.group(3).strip(), m.group(6), m.group(7).strip()


def by_name(pat):
    rx = re.compile(pat, re.I)
    n = 0
    for sec, name, clips, frames, size in rows():
        if rx.search(name):
            print("%-42s frames=%-18s clips=%-12s size=%-24s sec=%s"
                  % (name, "`" + frames + "`", clips, size, sec))
            n += 1
    print("---- matched export names = %d ----" % n)


def by_frame(num):
    want = str(int(num))
    n = 0
    for sec, name, clips, frames, size in rows():
        if want in frames.split(","):
            print("frame_%-4s used by %-40s clips=%-12s size=%-22s sec=%s"
                  % (want, name, clips, size, sec))
            n += 1
    print("---- users of frame_%s = %d ----" % (want, n))


def by_clip(lo, hi, sec_filter="3.1"):
    """按 clip id 区间列 export（用来找"同一套 UI 元件"的邻居命名）。"""
    n = 0
    for sec, name, clips, frames, size in rows():
        if sec_filter and sec_filter not in sec:
            continue
        try:
            ids = [int(x) for x in clips.replace(" ", "").split(",") if x]
        except ValueError:
            continue
        if any(int(lo) <= c <= int(hi) for c in ids):
            print("%-40s clips=%-16s frames=%-18s size=%-24s" % (name, clips, "`" + frames + "`", size))
            n += 1
    print("---- clip in [%s,%s] = %d ----" % (lo, hi, n))


def dirs():
    tot = {}
    for base, _d, files in os.walk(os.path.join(ROOT, "client", "Assets")):
        n = [f for f in files if re.match(r"frame_\d+\.png$", f)]
        if n:
            tot[os.path.relpath(base, ROOT)] = sorted(int(re.match(r"frame_(\d+)\.png", f).group(1)) for f in n)
    for k in sorted(tot):
        print("%-62s %s" % (k, tot[k]))
    print("---- dirs = %d ----" % len(tot))


def find(num, where=""):
    """⚠️ 帧号**按源 sheet 各自编号**（`ui` / `ui_spells` / `effects` … 的 200 是三张不同的图）
    ⇒ 必须用 `where` 限定路径子串，否则会拿到别的 sheet 的同号帧。"""
    pats = ("frame_%03d.png" % int(num), "frame_%d.png" % int(num))
    for base, _d, files in os.walk(os.path.join(ROOT, "client", "Assets")):
        if where and where.replace("/", os.sep) not in base:
            continue
        for f in files:
            if f in pats:
                return os.path.join(base, f)
    return None


def sheet(where, nums, outname="CR-T2b-frames.png"):
    cells = []
    for s in nums.split(","):
        p = find(s.strip(), where)
        if not p:
            print("frame %s in %s: NOT EXPORTED locally" % (s, where))
            continue
        im = Image.open(p).convert("RGBA")
        h = 240
        w = max(1, int(im.size[0] * h / im.size[1]))
        r = im.resize((w, h), Image.LANCZOS)
        bg = Image.new("RGB", (w, h), (255, 255, 255))
        bg.paste(r, (0, 0), r)
        cells.append((s, bg))
    if not cells:
        return 1
    W = sum(c[1].size[0] + 10 for c in cells) + 10
    cv = Image.new("RGB", (W, 270), (30, 30, 34))
    x = 10
    for s, bg in cells:
        cv.paste(bg, (x, 24))
        x += bg.size[0] + 10
    out = os.path.join(OUT, outname)
    cv.save(out)
    print("saved %s %s cells=%s" % (out, cv.size, ",".join(s for s, _ in cells)))
    return 0


def main():
    if len(sys.argv) < 3 and (len(sys.argv) < 2 or sys.argv[1] not in ("dirs",)):
        print(__doc__)
        return 2
    cmd = sys.argv[1]
    if cmd == "name":
        by_name(sys.argv[2])
    elif cmd == "frame":
        by_frame(sys.argv[2])
    elif cmd == "clips":
        by_clip(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else "3.1")
    elif cmd == "dirs":
        dirs()
    elif cmd == "sheet":
        return sheet(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else "CR-T2b-frames.png")
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
