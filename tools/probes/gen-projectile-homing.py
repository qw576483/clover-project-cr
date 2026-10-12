# -*- coding: utf-8 -*-
"""生成 `client/Assets/Scripts/View/ProjectileHomingTable.cs`：**投射物名 → 是否制导**。

数据源：`server/game/table/tsv/unit.tsv` 里 **`kind == 3`（投射物本体）** 的行 ——
  那一行的 `key` 就是发射者行 `projectile_key` 列的值（例：发射者 `archers` 的
  `projectile_key=ArcherArrow`，投射物行 `key=ArcherArrow`），`homing` 列 = 1/0。

⛔ **不读发射者行**：按表约定「非投射物行 `homing` 恒 0」，读发射者行会得出"全部不制导"的错结论。
⛔ **不在客户端硬编码投射物名**：名单全部由本生成器从表里取。

复跑：
  python tools/probes/gen-projectile-homing.py --root .
  python tools/probes/gen-projectile-homing.py --root . --verify    # 只校验盘上的产物，不生成
"""
import argparse
import collections
import os
import re
import sys

TSV = os.path.join('server', 'game', 'table', 'tsv', 'unit.tsv')
OUT = os.path.join('client', 'Assets', 'Scripts', 'View', 'ProjectileHomingTable.cs')


def load_homing(root):
    """{投射物名: bool} —— 只取 `kind == 3` 的行。"""
    p = os.path.join(root, TSV)
    if not os.path.isfile(p):
        return None, 'unit.tsv 不在：%s' % p
    lines = open(p, encoding='utf-8').read().splitlines()
    if not lines:
        return None, 'unit.tsv 是空的'
    hdr = lines[0].split('\t')
    ui = {k: i for i, k in enumerate(hdr)}
    for col in ('kind', 'key', 'homing'):
        if col not in ui:
            return None, 'unit.tsv 缺列：%s' % col
    out = collections.OrderedDict()
    for ln in lines[1:]:
        f = ln.split('\t')
        if len(f) < len(hdr):
            continue
        if f[ui['kind']] != '3':
            continue
        key = f[ui['key']]
        hv = f[ui['homing']]
        if not key:
            return None, 'kind==3 的行 key 为空：%s' % ln[:80]
        if hv not in ('0', '1'):
            return None, 'kind==3 的 homing 不是 0/1：key=%s homing=%r' % (key, hv)
        if key in out:
            return None, 'kind==3 的 key 重复：%s' % key
        out[key] = (hv == '1')
    if not out:
        return None, 'unit.tsv 里没有任何 kind==3 的行'
    return out, None


HEADER = '''// ⚠️ 本文件**由生成器产出**，不要手改（手改会被下次重跑丢掉）。
//   生成器：`tools/probes/gen-projectile-homing.py`（用法见该文件头部）。
//
// 投射物名 → 是否**制导**（`homing`）。出处 = `server/game/table/tsv/unit.tsv` 里
// **`kind == 3`（投射物本体）** 那一行的 `homing` 列；该行的 `key` 就是发射者行
// `projectile_key` 列的值（例：`archers.projectile_key = ArcherArrow` ↔ 投射物行 `key = ArcherArrow`）。
// ⛔ 发射者行按约定 `homing` 恒 0，读那些行会得出"全部不制导"的错结论。
//
// 用法（`BattleViewRoot` 的攻击排期）：`homing == false` 的投射物飞向**起手时刻锁定的目标点**
// （⛔ 不逐帧重瞄），`true` 的每帧对准目标当前位置。
namespace CR.View
{
    /// <summary>投射物名 → 是否制导（见文件头出处）。</summary>
    public static class ProjectileHomingTable
    {
        /// <summary>投射物名 → `homing`。键 = 发射者行 `projectile_key` 的取值。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, bool> Table =
            new System.Collections.Generic.Dictionary<string, bool>
            {
'''

FOOTER = '''            };

        /// <summary>表里收录的投射物条数（自检用）。</summary>
        public static int Count { get { return Table.Count; } }

        /// <summary>取某投射物的 `homing`；表里没有返回 false。**表外的一律按制导处理**（⛔ 不猜）。</summary>
        public static bool IsHoming(string projectileKey, out bool known)
        {
            bool v = false;
            known = !string.IsNullOrEmpty(projectileKey) && Table.TryGetValue(projectileKey, out v);
            return known ? v : true;
        }
    }
}
'''


def emit(root, homing):
    lines = [HEADER]
    for k, v in homing.items():
        lines.append('                { "%s", %s },\n' % (k, 'true' if v else 'false'))
    lines.append(FOOTER)
    path = os.path.join(root, OUT)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, 'w', encoding='utf-8', newline='\n').write(''.join(lines))
    return path


def verify(root, homing):
    """复验：产物的条目数 / 取值 / 抽样必须与数据源一致。"""
    path = os.path.join(root, OUT)
    if not os.path.isfile(path):
        return False, ['产物不存在：%s' % path], 0
    txt = open(path, encoding='utf-8').read()
    found = dict((m.group(1), m.group(2) == 'true')
                 for m in re.finditer(r'\{ "([^"]+)", (true|false) \}', txt))
    fails, n = [], 0
    n += 1
    if len(found) != len(homing):
        fails.append('条目数 %d != 数据源 %d' % (len(found), len(homing)))
    for k, v in homing.items():
        n += 1
        if found.get(k, None) != v:
            fails.append('%s：产物 %r != 源 %r' % (k, found.get(k, '<缺>'), v))
    n += 1
    if 'namespace CR.View' not in txt or 'class ProjectileHomingTable' not in txt:
        fails.append('产物缺少命名空间 / 类名')
    return (not fails), fails, n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--verify', action='store_true', help='只校验盘上的产物，不生成')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    homing, err = load_homing(root)
    if err:
        print('[FAIL] 读表：%s' % err)
        return 2
    trues = [k for k, v in homing.items() if v]
    falses = [k for k, v in homing.items() if not v]
    print('unit.tsv: kind==3 投射物 %d 条 -> homing=true %d / false %d'
          % (len(homing), len(trues), len(falses)))
    print('  homing=false 的投射物：%s' % (', '.join(falses) if falses else '（无）'))

    if a.verify:
        ok, fails, n = verify(root, homing)
        print('[复验] 产物断言：%s（检查 %d 项，失败 %d）' % ('PASS' if ok else 'FAIL', n, len(fails)))
        for f in fails[:20]:
            print('   FAIL %s' % f)
        return 0 if ok else 1

    path = emit(root, homing)
    print('写出 -> %s' % path)
    ok, fails, n = verify(root, homing)
    print('[复验] 新文件断言：%s（检查 %d 项，失败 %d）' % ('PASS' if ok else 'FAIL', n, len(fails)))
    for f in fails[:20]:
        print('   FAIL %s' % f)
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
