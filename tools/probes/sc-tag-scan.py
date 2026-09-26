# -*- coding: utf-8 -*-
"""sc-tag-scan.py -- 判据资产：`原版资源/sc/` 全部 `.sc` 的**标签直方图**与四条结构断言。

用途
----
把 `.sc` 的 tag 流一次扫干净，并对「已判定的结构」做可复跑断言（`--verify`）。
不受版本批次影响：2.1.5 批是明文，1.0.0 批是 `SC` 前缀 + LZMA 壳。

复跑
----
  python tools/probes/sc-tag-scan.py                  # 打印直方图 + 逐文件表
  python tools/probes/sc-tag-scan.py --verify          # 只跑断言（exit 0/1）
  python tools/probes/sc-tag-scan.py --json <path>     # 原始统计落盘

== 格式结论（本脚本依赖，均有实测读数）==
A. tag 流框架 = `u8 tag` + `u32 LE payloadSize` + payload，循环到文件尾。
   自洽判据：每个文件「消耗字节数 == 文件字节数」（106/106，`trailing=0`）。
   `00` = 文件尾标记（同时是 `0c` payload 内嵌子块的结束标记）。
B. 各 tag 的 payload 长度是**定长或由名字长度决定**，不是任意值：
   `01`/`18` = 5（u8+u16+u16 纹理尺寸）｜`08` = 24（6×i32 矩阵）｜`09` = 7（颜色变换）
   |`12` = 变长（多边形）｜`0c` = 变长（动画）｜`00`/`17`/`1a`/`1e` = **0**
   |`07`/`0f`/`19` = 3 + 字体名长度 + 20 / 21 / 25。
C. tag 名的权威出处 = `scwmake/SupercellFlash` 的 `supercell-flash/source/flash/flash_tags.h`
   （在本工程的本地副本 `.ai-tmp/SupercellFlash/`，commit 见该目录 `git log -1`；同一份代码也是
   `sc-workshop/SupercellSWF-Animate` 的 `plugin/ThirdParty/SC` 子模块）。被本工程实测用到的是：
   `00`=TAG_END | `01`=TAG_TEXTURE | `07`=TAG_TEXT_FIELD | `08`=TAG_MATRIX_2x3 | `09`=TAG_COLOR_TRANSFORM
   | `0b`=TAG_MOVIE_CLIP_FRAME_2 | `0c`=TAG_MOVIE_CLIP_3 | `0f`=TAG_TEXT_FIELD_2 | `12`=TAG_SHAPE_2
   | `16`=TAG_SHAPE_DRAW_BITMAP_COMMAND_3（`12` payload 内嵌）| `17`=TAG_USE_LOW_RES_TEXTURE
   | `18`=TAG_TEXTURE_4 | `19`=TAG_TEXT_FIELD_5 | `1a`=TAG_USE_EXTERNAL_TEXTURE | `1e`=TAG_USE_MULTI_RES_TEXTURE。
   三个「USE_*」tag 在 `SupercellSWF.cpp` 里只置一个 bool（`use_low_resolution` /
   `use_multi_resolution` / `has_external_texture`），写入时用 `write_tag_flag()` = `u8 tag + i32 0`
   ⇒ 这正是实测到的「零长标记」；低分辨率与多分辨率互斥（同一份资产只用一种）。
D. `09` = ColorTransform，**恒 7 字节**，字段顺序（权威出处 = 同仓库 `flash/transform/ColorTransform.cpp`
   的 `ColorTransform::load()`）：`add.r, add.g, add.b, alpha, multiply.r, multiply.g, multiply.b`。
   `0c` 三元组第 3 元素 `ctId` = 该文件内 `09` 记录的**序号**；`65535` = 无颜色变换。
   越界判据：所有非 65535 的 `ctId` 必须 `< 该文件 ColorTransCount`。
E. `07` / `0f` / `19` = TAG_TEXT_FIELD / _2 / _5，是**同一记录的不同版本**：
   文件头 `TextFieldCount` == `count(07) + count(0f) + count(19)`（逐文件相等，0 例外）。
   字段顺序（权威出处 = 同仓库 `flash/display_object/TextField.cpp` 的 `load()`，`string` = `u8 len + bytes`）：
   `u16 id | string font_name | u32 font_color | bool bold | bool italic | bool multiline | bool unused
    | u8 align | u8 font_size | i16 left | i16 top | i16 right | i16 bottom | bool outlined | string text`
   之后按 tag 递增：`bool use_device_font`（`0f`/`19`）→ `u32 outline_color`（`19`）。
   实测 2282 条逐条 `consumed == payload`（0 例外）⇒ 结构成立。
F. `0c` payload 尾部 = 该 clip 的**帧表**：`frames` 条 `0b`(TAG_MOVIE_CLIP_FRAME_2) 记录，每条
   `u16 elements_count + string label`（label 长度 0xFF = 无标签），后跟 `00`(TAG_END) 结束。
   实测 ui_v215 的 2086 个 clip 全部 `0b 条数 == frames 字段`。
   `0c` 里 `instance_count × u8` 那一段（历史上被记作 opacity）= **blend_mode**
   （见同仓库 `flash/display_object/MovieClip.cpp::load()`）。
G. 未解（本脚本不解、也不猜）：`07`/`0f`/`19` 的 `left/top/right/bottom` 是**哪个坐标系的矩形**
   （量级远小于矩阵平移的 twips，无法与 `策划/原版UI布局坐标.md` 的屏/twips 数值对上）。
"""
import argparse
import collections
import json
import lzma
import os
import struct
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SC_DIR = os.path.join(ROOT, '原版资源', 'sc')
AUTHORITATIVE_SUFFIX = '_v215.sc'
FOCUS_TAGS = ('09', '0f', '17', '1a')

# 权威 tag 表：scwmake/SupercellFlash → supercell-flash/source/flash/flash_tags.h（本工程本地副本 .ai-tmp/SupercellFlash/）
TAG_NAMES = {
    '00': 'TAG_END', '01': 'TAG_TEXTURE', '07': 'TAG_TEXT_FIELD', '08': 'TAG_MATRIX_2x3',
    '09': 'TAG_COLOR_TRANSFORM', '0b': 'TAG_MOVIE_CLIP_FRAME_2', '0c': 'TAG_MOVIE_CLIP_3',
    '0f': 'TAG_TEXT_FIELD_2', '12': 'TAG_SHAPE_2', '16': 'TAG_SHAPE_DRAW_BITMAP_COMMAND_3',
    '17': 'TAG_USE_LOW_RES_TEXTURE', '18': 'TAG_TEXTURE_4', '19': 'TAG_TEXT_FIELD_5',
    '1a': 'TAG_USE_EXTERNAL_TEXTURE', '1e': 'TAG_USE_MULTI_RES_TEXTURE',
}

# TextField::load 的字段序列（同上 flash/display_object/TextField.cpp）
TF_BASE = (('id', 'u16'), ('font_name', 'string'), ('font_color', 'u32'),
           ('bold', 'bool'), ('italic', 'bool'), ('multiline', 'bool'), ('unused', 'bool'),
           ('align', 'u8'), ('font_size', 'u8'),
           ('left', 'i16'), ('top', 'i16'), ('right', 'i16'), ('bottom', 'i16'),
           ('outlined', 'bool'), ('text', 'string'))
TF_EXTRA = {'07': (), '0f': (('use_device_font', 'bool'),),
            '19': (('use_device_font', 'bool'), ('outline_color', 'u32'))}

COLOR_FIELDS = ('add.r', 'add.g', 'add.b', 'alpha', 'mul.r', 'mul.g', 'mul.b')


def lzma_hack(data):
    """supercell 的 lzma 头少 4 字节 uncompressed-size（在偏移 9 处补 0）。"""
    d = data[0:9] + b'\x00' * 4 + data[9:]
    return lzma.LZMADecompressor().decompress(d)


def load(path):
    """明文（2.1.5 批）直接返回；`SC` 前缀 / 裸 LZMA 头（1.0.0 批）先解压。"""
    raw = open(path, 'rb').read()
    body = raw[10 + int.from_bytes(raw[6:10], 'big'):] if raw[:2] == b'\x53\x43' else raw
    try:
        return lzma_hack(body)
    except Exception:
        return raw


def parse(data):
    """返回 (header, export_ids, export_names, records)；records = [(tag_hex, size, tag_off, payload_off)]。"""
    o = 0
    keys = ('ShapeCount', 'TotalsAnim', 'TotalsTex', 'TextFieldCount', 'MatrixCount', 'ColorTransCount')
    hdr = {}
    for k in keys:
        hdr[k] = struct.unpack_from('<H', data, o)[0]
        o += 2
    o += 5
    ec = struct.unpack_from('<H', data, o)[0]
    o += 2
    exp_ids = list(struct.unpack_from('<%dH' % ec, data, o))
    o += 2 * ec
    exp_names = []
    for _ in range(ec):
        n = data[o]
        o += 1
        exp_names.append(data[o:o + n].decode('utf-8', 'replace'))
        o += n
    recs = []
    while len(data) - o > 0:
        tag = data[o:o + 1].hex()
        size = int.from_bytes(data[o + 1:o + 5], 'little')
        recs.append((tag, size, o, o + 5))
        o += 5 + size
    return hdr, exp_ids, exp_names, recs


def scan(path):
    data = load(path)
    hdr, exp_ids, exp_names, recs = parse(data)
    return data, hdr, exp_ids, exp_names, recs


def clip_triples(data):
    """取每个 `0c` 记录的三元组第 3 元素（ctId）序列。"""
    _, _, _, recs = parse(data)
    out = []
    for tag, size, _, poff in recs:
        if tag != '0c':
            continue
        pl = data[poff:poff + size]
        # 0c 布局：u16 id | u8 fps | u16 frames | i32 cnt1 | cnt1 × (u16,u16,u16)
        cid = struct.unpack_from('<H', pl, 0)[0]
        cnt1 = struct.unpack_from('<i', pl, 5)[0]
        for i in range(cnt1):
            out.append((cid, struct.unpack_from('<H', pl, 13 + i * 6)[0]))
    return out


def clip_children(data):
    """返回 {clip_id: (child_object_ids, child_names)}；名字长度 >= 255 记为空名。"""
    _, _, _, recs = parse(data)
    out = {}
    for tag, size, _, poff in recs:
        if tag != '0c':
            continue
        pl = data[poff:poff + size]
        o = 9
        cid = struct.unpack_from('<H', pl, 0)[0]
        cnt1 = struct.unpack_from('<i', pl, 5)[0]
        o += cnt1 * 6
        cnt2 = struct.unpack_from('<h', pl, o)[0]
        o += 2
        oids = list(struct.unpack_from('<%dh' % cnt2, pl, o))
        o += 2 * cnt2 + cnt2          # objectIds + opacities
        names = []
        for _ in range(cnt2):
            L = pl[o]
            o += 1
            if L >= 255:
                names.append(None)
            else:
                names.append(pl[o:o + L].decode('utf-8', 'replace'))
                o += L
        out[cid] = (oids, names)
    return out


def textfields(data):
    """返回全部 07/0f/19 记录：[(tag, id, fontName, payload_len)]。"""
    _, _, _, recs = parse(data)
    out = []
    for tag, size, _, poff in recs:
        if tag not in ('07', '0f', '19'):
            continue
        pl = data[poff:poff + size]
        oid = struct.unpack_from('<H', pl, 0)[0]
        L = pl[2]
        out.append((tag, oid, pl[3:3 + L].decode('utf-8', 'replace'), size))
    return out


def color_refs(fn):
    """按 `09` 记录的 7 字节值聚合引用：{payload_bytes: (refs, Counter(元件名), Counter(导出名))}。"""
    path = os.path.join(SC_DIR, fn)
    data, _, exp_ids, exp_names, recs = scan(path)
    bank = [data[poff:poff + size] for tag, size, _, poff in recs if tag == '09']
    name_by_clip = dict(zip(exp_ids, exp_names))
    kids = clip_children(data)
    agg = {}
    for tag, size, _, poff in recs:
        if tag != '0c':
            continue
        pl = data[poff:poff + size]
        cid = struct.unpack_from('<H', pl, 0)[0]
        cnt1 = struct.unpack_from('<i', pl, 5)[0]
        names = kids.get(cid, ([], []))[1]
        ex = name_by_clip.get(cid, 'clip_%d' % cid)
        for i in range(cnt1):
            # 三元组 = (childIdx, matrixIdx, colorIdx)，起点 9，各占 u16
            child_idx = struct.unpack_from('<H', pl, 9 + i * 6)[0]
            ct = struct.unpack_from('<H', pl, 13 + i * 6)[0]
            if ct == 65535 or ct >= len(bank) or child_idx >= len(names):
                continue
            slot = agg.setdefault(bank[ct], [0, collections.Counter(), collections.Counter()])
            slot[0] += 1
            slot[1][names[child_idx] or '<无名>'] += 1
            slot[2][ex] += 1
    return agg


def report_colors(files, top=24):
    total = collections.defaultdict(lambda: [0, collections.Counter(), collections.Counter()])
    for fn in files:
        for h, v in color_refs(fn).items():
            t = total[h]
            t[0] += v[0]
            t[1].update(v[1])
            t[2].update(v[2])
    print('payload            refs  ' + ' '.join('%-5s' % f for f in COLOR_FIELDS) + '  同现导出名 / 元件名')
    for h, v in sorted(total.items(), key=lambda kv: -kv[1][0])[:top]:
        print('%-16s %6d  %s  %s | %s' % (
            h.hex(), v[0], ' '.join('%-5d' % b for b in h),
            [e for e, _ in v[2].most_common(3)], [n for n, _ in v[1].most_common(3)]))
    print()
    print('引用次数完全相等的 payload 组（≥1000 次时可疑「成对配色」）：')
    by_n = collections.defaultdict(list)
    for h, v in total.items():
        by_n[v[0]].append(h.hex())
    for n in sorted((n for n in by_n if n >= 1000 and len(by_n[n]) > 1), reverse=True):
        print('  n=%d : %s' % (n, by_n[n]))


def parse_textfield(pl, tag):
    """按 TextField::load 的字段序列解一条 07/0f/19；返回 (字段 dict, 消费字节数)。"""
    o = 0

    def take(kind):
        nonlocal o
        if kind == 'u8':
            v = pl[o]; o += 1
        elif kind == 'u16':
            v = struct.unpack_from('<H', pl, o)[0]; o += 2
        elif kind == 'i16':
            v = struct.unpack_from('<h', pl, o)[0]; o += 2
        elif kind == 'u32':
            v = struct.unpack_from('<I', pl, o)[0]; o += 4
        elif kind == 'bool':
            v = pl[o] != 0; o += 1
        elif kind == 'string':
            n = pl[o]; o += 1
            v = None if n == 0xFF else pl[o:o + n].decode('utf-8', 'replace')
            if n != 0xFF:
                o += n
        return v

    out = {}
    for name, kind in TF_BASE + TF_EXTRA[tag]:
        out[name] = take(kind)
    return out, o


def histograms(files):
    tag_stats = collections.defaultdict(lambda: dict(count=0, files={}, lens=collections.Counter()))
    per_file = {}
    for fn in files:
        path = os.path.join(SC_DIR, fn)
        data, hdr, exp_ids, exp_names, recs = scan(path)
        consumed = (recs[-1][2] + 5 + recs[-1][1]) if recs else 0
        per_file[fn] = dict(size=len(data), consumed=consumed, trailing=len(data) - consumed,
                            header=hdr, exports=len(exp_names), tag_seq=[r[0] for r in recs])
        for tag, size, _, _ in recs:
            st = tag_stats[tag]
            st['count'] += 1
            st['files'][fn] = st['files'].get(fn, 0) + 1
            st['lens'][size] += 1
    return tag_stats, per_file


def report_tag_stats(tag_stats):
    print('%-5s %-34s %8s %6s %s' % ('tag', '权威名', 'count', 'files', 'payload 长度[min,max,种数]'))
    for tag in sorted(tag_stats, key=lambda t: -tag_stats[t]['count']):
        st = tag_stats[tag]
        lens = sorted(st['lens'])
        print('%-5s %-34s %8d %6d [%d,%d,%d]' % (tag, TAG_NAMES.get(tag, '?'), st['count'],
                                                 len(st['files']), lens[0], lens[-1], len(lens)))


def verify(tag_stats, per_file):
    ok = True

    def chk(cond, msg):
        nonlocal ok
        print(('  PASS  ' if cond else '  FAIL  ') + msg)
        if not cond:
            ok = False

    print('---- 1. tag 流框架自洽（消耗字节 == 文件字节）----')
    bad = [fn for fn, st in per_file.items() if st['trailing'] != 0]
    chk(not bad, '106 个 .sc 全部 trailing==0（例外=%s）' % (bad[:5] or '无'))

    print('---- 2. 定长 tag 的 payload 长度 ----')
    for tag, want in (('08', 24), ('09', 7), ('01', 5), ('18', 5), ('00', 0), ('1a', 0), ('1e', 0), ('17', 0)):
        got = sorted(tag_stats[tag]['lens']) if tag in tag_stats else []
        chk(got == [want], 'tag %s 的长度集合 == [%d]（实际 %s）' % (tag, want, got))

    print('---- 3. TextFieldCount == count(07)+count(0f)+count(19)（2.1.5 批逐文件）----')
    bad = []
    for fn, st in per_file.items():
        if not fn.endswith(AUTHORITATIVE_SUFFIX):
            continue
        seq = collections.Counter(st['tag_seq'])
        s = seq.get('07', 0) + seq.get('0f', 0) + seq.get('19', 0)
        if s != st['header']['TextFieldCount']:
            bad.append((fn, st['header']['TextFieldCount'], s))
    chk(not bad, '89 个 *_v215.sc 逐文件相等（例外=%s）' % (bad[:3] or '无'))

    print('---- 4. ctId 越界校验（ctId < 该文件 ColorTransCount）----')
    oob = 0
    ref = 0
    for fn, st in per_file.items():
        data = load(os.path.join(SC_DIR, fn))
        n = st['header']['ColorTransCount']
        for _cid, ct in clip_triples(data):
            if ct == 65535:
                continue
            ref += 1
            if ct >= n:
                oob += 1
    chk(oob == 0, '有效 ctId 引用 %d 次，越界 %d 次' % (ref, oob))

    print('---- 5. 1a 是零长标记（每文件恰 1 条）----')
    bad = [fn for fn, st in per_file.items() if collections.Counter(st['tag_seq']).get('1a', 0) != 1]
    chk(not bad, '106 个 .sc 各恰 1 条 1a（例外=%s）' % (bad[:5] or '无'))

    print('---- 6. menu_bottom_tab_bg(clip 4441) 的 bg_height/gap_height 是 0x19 文本域 ----')
    data, _, exp_ids, exp_names, _ = scan(os.path.join(SC_DIR, 'ui_v215.sc'))
    cid = exp_ids[exp_names.index('menu_bottom_tab_bg')] if 'menu_bottom_tab_bg' in exp_names else None
    chk(cid == 4441, 'menu_bottom_tab_bg -> clip 4441（实际 %s）' % cid)
    oids, names = clip_children(data).get(cid, ([], []))
    chk(names == ['tutorial_grad', None, None, 'bg_height', 'gap_height'],
        'clip 4441 子件名 == 原版语义名（实际 %s）' % names)
    tf_ids = {oid for tag, oid, _fn, _s in textfields(data) if tag == '19'}
    chk({2819, 2820} <= tf_ids, 'oid 2819/2820 在 0x19 文本域记录里（19 记录数=%d）' % len(tf_ids))
    chk(not (set(oids) & tf_ids - {2819, 2820}), '同 clip 的 0x19 命中只来自这两个名字（否则归因不成立）')

    print('---- 7. 实到的 tag 全部在权威 tag 表里有名字 ----')
    unknown = sorted(t for t in tag_stats if t not in TAG_NAMES)
    chk(not unknown, '13 个实到 tag 均有权威名（未知名=%s）' % (unknown or '无'))

    print('---- 8. 07/0f/19 逐条按权威字段序列解得干净（consumed == payload）----')
    bad = []
    n_tf = 0
    for fn in sorted(f for f in os.listdir(SC_DIR) if f.endswith('.sc')):
        data = load(os.path.join(SC_DIR, fn))
        _, _, _, recs = parse(data)
        for tag, size, _, poff in recs:
            if tag not in TF_EXTRA:
                continue
            _f, used = parse_textfield(data[poff:poff + size], tag)
            n_tf += 1
            if used != size:
                bad.append((fn, tag, used, size))
    chk(not bad, '%d 条文本域记录 consumed==payload（例外=%s）' % (n_tf, bad[:3] or '无'))

    print('---- 结论: %s ----' % ('全部 PASS' if ok else '存在 FAIL'))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--verify', action='store_true')
    ap.add_argument('--json', default=None)
    ap.add_argument('--textfields', action='store_true',
                    help='打印全部 07/0f/19 记录（tag / id / 字体名 / payload 长度）')
    ap.add_argument('--colors', action='store_true',
                    help='按 09 记录的 7 字节值聚合引用（含同现导出名 / 元件名）')
    args = ap.parse_args()
    files = sorted(f for f in os.listdir(SC_DIR) if f.endswith('.sc'))
    if args.colors:
        report_colors(files)
        return 0
    tag_stats, per_file = histograms(files)
    print('SC_DIR=%s  FILES=%d' % (SC_DIR, len(files)))
    if args.textfields:
        fonts = collections.Counter()
        sizes = collections.Counter()
        texts = []
        for fn in files:
            data = load(os.path.join(SC_DIR, fn))
            _, _, _, recs = parse(data)
            rows = []
            for tag, size, _, poff in recs:
                if tag not in TF_EXTRA:
                    continue
                f, _used = parse_textfield(data[poff:poff + size], tag)
                rows.append((tag, f))
                fonts[f['font_name']] += 1
                sizes[f['font_size']] += 1
                if f['text']:
                    texts.append((fn, tag, f['id'], f['text']))
            if rows:
                print('%-34s %s' % (fn, ' | '.join(
                    '%s id=%d %s sz=%d align=%d LTRB=%d/%d/%d/%d' %
                    (t, f['id'], f['font_name'], f['font_size'], f['align'],
                     f['left'], f['top'], f['right'], f['bottom']) for t, f in rows[:6])))
                if len(rows) > 6:
                    print('%-34s …共 %d 条' % ('', len(rows)))
        print()
        print('字体名全量计数:')
        for nm, n in fonts.most_common():
            print('  %6d x %s' % (n, nm))
        print('font_size 取值分布: %s' % dict(sorted(sizes.items())))
        print('非空 text 字段: %s' % (texts or '无'))
        return 0
    if not args.verify:
        report_tag_stats(tag_stats)
        print()
        for tag in FOCUS_TAGS:
            st = tag_stats[tag]
            print('tag %s: %d 条 / %d 个文件' % (tag, st['count'], len(st['files'])))
            print('   ' + ', '.join('%s:%d' % kv for kv in
                                   sorted(st['files'].items(), key=lambda kv: (-kv[1], kv[0]))[:80]))
            print('   长度分布: %s' % dict(sorted(st['lens'].items())))
        if args.json:
            with open(args.json, 'w', encoding='utf-8') as f:
                json.dump({t: dict(count=s['count'], files=s['files'], lens=dict(s['lens']))
                           for t, s in tag_stats.items()}, f, ensure_ascii=False, indent=1)
            print('已写 %s' % args.json)
        return 0
    return verify(tag_stats, per_file)


if __name__ == '__main__':
    sys.exit(main())
