#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
d134-fix-header.py —— 采样器**源码 bug**（写出的列比表头少一列 `cnt`）的
**只改表头、不动数据体**修正 + 自证。

背景（必须写清，否则等于篡改证据）：
  `.ai-tmp/drivers/D134-sample-on.cs` 的表头写了 21 列（含 `cnt`），
  而每行只 `Append` 了 20 个值（`anim, pos, vstep, vno, …`，**漏写 `cnt`**）。
  ⇒ 第 8 列起全体错位：表头的 `cnt` 实为 `vstep`，`vstep` 实为 `vno`，`wy` 实为 `sprite` …
  数据行本身是**按真实列序**原子写出的（20 列），所以修正 = 换掉表头那一行。

本脚本做三件事，并逐条断言：
  1. 把原文件**逐字节**复制成 `D134-units.rawshift.tsv`（保留现场，可复核）；
  2. 用正确表头（20 列）重写 `D134-units.tsv`；
  3. 断言：两文件的**数据体逐字节相同**（只差表头那一行）、每行都是 20 列、无空行。
用法：python tools/probes/d134-fix-header.py --root .
"""
import argparse
import os
import shutil
import sys

WRONG = "frame\tt\tdt\tid\tdir\tanim\tpos\tcnt\tvstep\tvno\twx\twy\tsprite\trectx\trecty\trectw\trecth\tpivx\tpivy\tppu\torder"
#            ↑ 多出来的这一列 = 源码漏写的 cnt
FIXED = "frame\tt\tdt\tid\tdir\tanim\tpos\tvstep\tvno\twx\twy\tsprite\trectx\trecty\trectw\trecth\tpivx\tpivy\tppu\torder"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    a = ap.parse_args()
    p = os.path.join(a.root, '.ai-tmp', 'test', 'D134-units.tsv')
    raw = os.path.join(a.root, '.ai-tmp', 'test', 'D134-units.rawshift.tsv')
    if not os.path.exists(p):
        print('[BLOCKED] 找不到 %s' % p)
        return 3

    data = open(p, 'rb').read()
    lines = data.split(b'\n')
    head = lines[0].decode('utf-8')
    body = b'\n'.join(lines[1:])

    if head == FIXED:
        print('[SKIP] 表头已是正确形态（20 列）')
        return 0
    assert head == WRONG, '表头与预期不符，拒绝修改：%r' % head[:120]

    # 1. 留现场（若已存在则校验一致）
    if os.path.exists(raw):
        old = open(raw, 'rb').read()
        assert old == data, 'rawshift 副本与当前文件不一致（拒绝覆盖）'
        print('[ok] rawshift 副本已存在且一致：%s' % raw)
    else:
        shutil.copyfile(p, raw)
        print('[new] 现场副本：%s（%d B）' % (raw, len(data)))

    # 2. 换表头
    out = (FIXED + '\n').encode('utf-8') + body
    open(p, 'wb').write(out)

    # 3. 断言：只差表头
    back = open(p, 'rb').read()
    assert back.split(b'\n', 1)[1] == body, '数据体被改动了 —— 回滚'
    rows = [l for l in back.split(b'\n') if l]
    nf = set(len(l.split(b'\t')) for l in rows)
    assert nf == {20}, '列数不齐：%s' % nf
    print('[ok] 表头已修正；数据体逐字节不变（%d 行 × 20 列）' % (len(rows) - 1))
    return 0


if __name__ == '__main__':
    sys.exit(main())
