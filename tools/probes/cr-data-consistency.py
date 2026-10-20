"""跨端数据一致性检查：协议 / 配表 / 资源。

用法（项目根任意目录）：
    python tools/probes/cr-data-consistency.py

检查项：
  1. server/game/def/*.go 的消息号与结构体 vs client/Assets/Scripts/Def/{MsgDef,ProtoDef}.cs
     （常量编号、字段名 / 类型 / json tag、[Serializable]）。
  2. server/game/table/tsv/*.tsv 的表头列 vs 各表 Load 读取的列、行宽、整型值、主键唯一、
     以及 card → unit / spell、unit 的 projectile / summon / spawn / death 引用。
  3. client/Assets/Scripts/Core/ResPaths.cs 与 AudioPaths.cs 的每个落地路径是否存在；
     card.tsv 的 sprite_dir 目录；特效帧段（First/Count，按目录内帧号顺序取）。
     BattleViewRoot 的卡 → 精灵目录兜底表（非协议字段）。

exit 0 = 全部一致；exit 1 = 有差异（逐条打印）。
"""
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRV = os.path.join(ROOT, "server", "game")
RES = os.path.join(ROOT, "client", "Assets", "Resources")
SCRIPTS = os.path.join(ROOT, "client", "Assets", "Scripts")

problems = []


def bad(msg):
    problems.append(msg)


def read(p):
    return open(p, encoding="utf-8").read()


def body_after(text, start_brace):
    depth = 0
    for j in range(start_brace, len(text)):
        if text[j] == "{":
            depth += 1
        elif text[j] == "}":
            depth -= 1
            if depth == 0:
                return text[start_brace + 1:j]
    return ""


def cstype(t):
    t = t.strip()
    if t == "int32":
        return "int"
    if t == "int64":
        return "long"
    if t.startswith("[]"):
        return cstype(t[2:]) + "[]"
    return t


# ---------------------------------------------------------------- 1 协议
def check_protocol():
    go_src = {f: read(os.path.join(SRV, "def", f)) for f in ("msg.go", "push.go", "types.go")}
    go_structs = {}
    go_consts = {}
    for src in go_src.values():
        for m in re.finditer(r"type\s+(\w+)\s+struct\s*\{", src):
            body = body_after(src, m.end() - 1)
            fields = []
            for ln in body.splitlines():
                fm = re.match(r'\s*(\w+)\s+([\[\]\w\.]+)\s+`json:"([^"]*)"`', ln)
                if fm:
                    tag = fm.group(3).split(",")[0]
                    fields.append((fm.group(1), fm.group(2), tag))
                    if not re.fullmatch(r"[a-z][a-z0-9_]*", tag):
                        bad("协议 json tag 不是 snake_case: %s.%s -> %s" % (m.group(1), fm.group(1), tag))
            go_structs[m.group(1)] = fields
        for m in re.finditer(r"^\s*(\w+)\s*=\s*(\d+)", src, re.M):
            go_consts[m.group(1)] = int(m.group(2))

    cs_src = read(os.path.join(SCRIPTS, "Def", "ProtoDef.cs"))
    no_block = re.sub(r"/\*.*?\*/", "", cs_src, flags=re.S)
    no_line = re.sub(r"//[^\n]*", "", no_block)
    cs_structs = {}
    serializable = set()
    for m in re.finditer(r"public\s+class\s+(\w+)\s*\{", no_line):
        body = body_after(no_line, m.end() - 1)
        cs_structs[m.group(1)] = [(fm.group(2), fm.group(1))
                                  for fm in re.finditer(r"public\s+([\[\]\w]+)\s+(\w+)\s*;", body)]
        if re.search(r"\[Serializable\]\s*(?://[^\n]*\n\s*)?public\s+class\s+" + m.group(1) + r"\b", cs_src):
            serializable.add(m.group(1))

    for name in sorted(set(go_structs) & set(cs_structs)):
        g, c = go_structs[name], cs_structs[name]
        tags = [x[2] for x in g]
        names = [x[0] for x in c]
        if tags != names:
            bad("结构体 %s 字段序列不一致：go=%r cs=%r" % (name, tags, names))
        cmap = dict(c)
        for _, gt, tag in g:
            if tag not in cmap:
                bad("结构体 %s 客户端缺字段 %s" % (name, tag))
            elif cstype(gt) != cmap[tag]:
                bad("结构体 %s 字段 %s 类型 Go %s / C# %s" % (name, tag, gt, cmap[tag]))
    for name in cs_structs:
        if name in ("DeckRef",):
            continue
        if name not in serializable:
            bad("协议 DTO %s 缺 [Serializable]" % name)

    cs_msg = read(os.path.join(SCRIPTS, "Def", "MsgDef.cs"))
    cs_consts = {m.group(1): int(m.group(2))
                 for m in re.finditer(r"public\s+const\s+uint\s+(\w+)\s*=\s*(\d+);", cs_msg)}
    alias = {k: (k[3:] if k.startswith("Msg") else k) for k in go_consts}
    for gk, gv in go_consts.items():
        ck = alias[gk]
        if ck not in cs_consts:
            bad("消息号 %s 客户端缺常量 %s" % (gk, ck))
        elif cs_consts[ck] != gv:
            bad("消息号 %s 值不一致：go=%d cs=%d" % (gk, gv, cs_consts[ck]))
    for ck in cs_consts:
        if ck not in alias.values():
            bad("消息号客户端多出 %s" % ck)
    print("协议：结构体 %d / 客户端 %d；消息号 %d / %d；[Serializable] %d"
          % (len(go_structs), len(cs_structs), len(go_consts), len(cs_consts), len(serializable)))


# ---------------------------------------------------------------- 2 配表
def load_tsv(name):
    lines = [l for l in read(os.path.join(SRV, "table", "tsv", name)).split("\n") if l.strip()]
    return lines[0].split("\t"), [l.split("\t") for l in lines[1:]]


REQUIRE = {
    "card": "card.go",
    "unit": "unit.go",
    "spell": "spell.go",
}
TEXT_COLS = {"key", "name_cn", "name_en", "icon", "sprite_dir", "deploy_fx",
             "death_spawn_key", "projectile_key", "spawn_key", "summon_key"}


def check_tables():
    loaded = {}
    for tn, fn in REQUIRE.items():
        header, rows = load_tsv(tn + ".tsv")
        loaded[tn] = (header, rows)
        src = read(os.path.join(SRV, "table", fn))
        m = re.search(r"c\.require\((.*?)\);", src, re.S)
        cols = re.findall(r'"([a-z0-9_]+)"', m.group(1)) if m else []
        miss = [c for c in cols if c not in header]
        if miss:
            bad("%s.tsv 表头缺代码读取的列: %s" % (tn, miss))
        for i, r in enumerate(rows, 2):
            if len(r) != len(header):
                bad("%s.tsv 第 %d 行列数 %d != 表头 %d" % (tn, i, len(r), len(header)))
            for j, v in enumerate(r[:len(header)]):
                col = header[j]
                v = v.strip()
                if v == "" or col in TEXT_COLS:
                    continue
                try:
                    n = int(v)
                except ValueError:
                    bad("%s.tsv 第 %d 行列 %s 不是整数: %r" % (tn, i, col, v))
                    continue
                if n < 0:
                    bad("%s.tsv 第 %d 行列 %s 为负数: %d" % (tn, i, col, n))
        ids = set()
        for i, r in enumerate(rows, 2):
            if r[0] in ids:
                bad("%s.tsv 重复 id %s（第 %d 行）" % (tn, r[0], i))
            ids.add(r[0])
        if "key" in header:
            kj = header.index("key")
            keys = set()
            for i, r in enumerate(rows, 2):
                if r[kj] in keys:
                    bad("%s.tsv 重复 key %s（第 %d 行）" % (tn, r[kj], i))
                keys.add(r[kj])
        print("配表：%s.tsv %d 行 × %d 列" % (tn, len(rows), len(header)))

    hc, rc = loaded["card"]
    hu, ru = loaded["unit"]
    hs, rs = loaded["spell"]
    unit_by_key = {r[hu.index("key")]: r for r in ru}
    card_keys = [r[hc.index("key")] for r in rc]
    spell_by_key = {r[hs.index("key")]: r for r in rs}

    for r in rc:
        key, t = r[hc.index("key")], int(r[hc.index("type")])
        if t == 1:
            if key not in spell_by_key:
                bad("卡 %s 是法术，spell.tsv 无同 key 行" % key)
        elif key not in unit_by_key:
            bad("卡 %s 在 unit.tsv 无同 key 行" % key)
    for r in ru:
        key = r[hu.index("key")]
        for col in ("projectile_key", "summon_key", "spawn_key", "death_spawn_key"):
            v = r[hu.index(col)].strip()
            if v and v not in unit_by_key:
                bad("unit %s 的 %s=%s 在 unit.tsv 找不到" % (key, col, v))
            if col == "projectile_key" and v:
                tgt = unit_by_key.get(v)
                if tgt and tgt[hu.index("kind")] != "3":
                    bad("unit %s 的 projectile_key=%s 不是 kind=3 投射物行" % (key, v))
    for r in rs:
        key = r[hs.index("key")]
        if key not in card_keys:
            print("提示：spell.tsv 的 %s 没有对应卡（法术全集超集，运行期不可达）" % key)
        v = r[hs.index("spawn_key")].strip()
        if v and v not in unit_by_key:
            bad("spell %s 的 spawn_key=%s 在 unit.tsv 找不到" % (key, v))


# ---------------------------------------------------------------- 3 资源
def ls(rel):
    p = os.path.join(RES, rel.replace("/", os.sep))
    return (os.path.isdir(p), sorted(os.listdir(p)) if os.path.isdir(p) else [])


def check_resources():
    hc, rc = load_tsv("card.tsv")
    for r in rc:
        key, sd, t = r[hc.index("key")], r[hc.index("sprite_dir")].strip(), int(r[hc.index("type")])
        if not sd:
            bad("卡 %s 的 sprite_dir 为空" % key)
            continue
        base = "Sprites/Cards" if t == 1 else ("Sprites/Buildings" if t == 2 else "Sprites/Units")
        for d in [x.strip() for x in sd.split(";") if x.strip()]:
            ok, files = ls(base + "/" + d)
            if not ok:
                bad("卡 %s 的素材目录不存在: %s/%s" % (key, base, d))
            elif not any(f.endswith(".png") for f in files):
                bad("卡 %s 的素材目录没有 PNG: %s/%s" % (key, base, d))

    rp = read(os.path.join(SCRIPTS, "Core", "ResPaths.cs"))
    raw = {m.group(1): m.group(2).strip()
           for m in re.finditer(r"public\s+const\s+string\s+(\w+)\s*=\s*([^;]+);", rp)}

    def resolve(name, depth=0):
        if name not in raw or depth > 8:
            return None
        expr = raw[name]
        for mm in list(re.finditer(r"\b([A-Z]\w*)\b", expr)):
            sub = resolve(mm.group(1), depth + 1)
            if sub is not None:
                expr = expr.replace(mm.group(0), '"%s"' % sub)
        parts = re.findall(r'"([^"]*)"', expr)
        rest = re.sub(r'"[^"]*"', "", expr).replace("+", "").strip()
        return "".join(parts) if not rest else None

    for name in raw:
        p = resolve(name)
        if p is None or not (p.startswith("Sprites/") or p.startswith("Sound/")):
            continue
        full = os.path.join(RES, p.replace("/", os.sep))
        if not (os.path.isfile(full + ".png") or os.path.isdir(full)):
            bad("ResPaths.%s 路径不存在: %s" % (name, p))
    consts = {k: resolve(k) for k in raw}
    for m in re.finditer(r'public\s+static\s+string\s+(\w+)\s*\{\s*get\s*\{\s*return\s+UiFrame\((\w+),\s*(\w+),\s*(\d+)\)', rp):
        name, pd, sd, n = m.group(1), consts.get(m.group(2)) or m.group(2), consts.get(m.group(3)) or m.group(3), int(m.group(4))
        path = "Sprites/Ui/%s/%s/frame_%03d.png" % (pd, sd, n)
        if not os.path.isfile(os.path.join(RES, path.replace("/", os.sep))):
            bad("ResPaths.%s 路径不存在: %s" % (name, path))

    ivals = {m.group(1): int(m.group(2))
             for m in re.finditer(r"public\s+const\s+int\s+(\w+)\s*=\s*(\d+);", rp)}
    seg = [("Arrow", "EffectArrowFirst", "EffectArrowCount"), ("Blast", "EffectBlastFirst", "EffectBlastCount"),
           ("Hit", "EffectHitFirst", "EffectHitCount"),
           ("Death/Blue", "EffectDeathBlueFirst", "EffectDeathBlueCount"),
           ("Death/Purple", "EffectDeathPurpleFirst", "EffectDeathPurpleCount"),
           ("Death/Ground", "EffectDeathGroundFirst", "EffectDeathGroundCount"),
           ("Death/Particle", "EffectDeathParticleFirst", "EffectDeathParticleCount"),
           ("Spell", "EffectArrowsFirst", "EffectArrowsCount"), ("Spell", "EffectRageFirst", "EffectRageCount"),
           ("Spell", "EffectRocketFirst", "EffectRocketCount"), ("Spell", "EffectFreezeFirst", "EffectFreezeCount"),
           ("Spell", "EffectLightningFirst", "EffectLightningCount"), ("Spell", "EffectZapFirst", "EffectZapCount"),
           ("Spell", "EffectPoisonFirst", "EffectPoisonCount"), ("Spell", "EffectTheLogFirst", "EffectTheLogCount"),
           ("Spell", "EffectFireballBlastFirst", "EffectFireballBlastCount"),
           ("SpellFlight", "EffectFireballFlightFirst", "EffectFireballFlightCount"),
           ("Spear", "EffectSpearFirst", "EffectSpearCount"), ("Cannonball", "EffectCannonballFirst", "EffectCannonballCount"),
           ("Bowler", "EffectBowlerFirst", "EffectBowlerCount"), ("Axe", "EffectAxeFirst", "EffectAxeCount"),
           ("Catapult", "EffectCatapultFirst", "EffectCatapultCount"), ("Bomb", "EffectBombFirst", "EffectBombCount"),
           ("IceWizard", "EffectIceWizardFirst", "EffectIceWizardCount"),
           ("IceSpirit", "EffectIceSpiritFirst", "EffectIceSpiritCount"),
           ("FireSpirit", "EffectFireSpiritFirst", "EffectFireSpiritCount"),
           ("Dragon", "EffectDragonFirst", "EffectDragonCount"),
           ("HitCloud", "EffectHitCloudFirst", "EffectHitCloudCount"),
           ("IceWizHit", "EffectIceWizHitFirst", "EffectIceWizHitCount")]
    for use, fk, ck in seg:
        ok, files = ls("Sprites/Effects/" + use)
        if not ok:
            bad("特效目录不存在: Effects/%s" % use)
            continue
        nums = sorted(int(x) for x in re.findall(r"frame_(\d+)\.png", " ".join(files)))
        first, count = ivals.get(fk), ivals.get(ck)
        if first is None or count is None:
            bad("特效常量缺失: %s / %s" % (fk, ck))
            continue
        if len([n for n in nums if n >= first][:count]) != count:
            bad("特效帧段不足: Effects/%s 从 f%d 起需要 %d 帧" % (use, first, count))

    ap = read(os.path.join(SCRIPTS, "Core", "AudioPaths.cs"))
    sfx = [(m.group(1), m.group(2)) for m in re.finditer(r'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)";', ap)]
    for name, v in sfx:
        if name in ("SfxRoot", "BgmRoot") or name.startswith("Bgm") or v.startswith("Sound/"):
            continue
        p = os.path.join(RES, "Sound", "SFX", v.replace("/", os.sep))
        if not any(os.path.isfile(p + e) for e in (".ogg", ".wav", ".mp3")):
            bad("AudioPaths.%s 音频不存在: Sound/SFX/%s" % (name, v))
    for m in re.finditer(r'public\s+const\s+string\s+Bgm(Menu|Battle|Result)\s*=\s*"([^"]+)";', ap):
        p = os.path.join(RES, "Sound", "BGM", m.group(2).replace("/", os.sep))
        if not any(os.path.isfile(p + e) for e in (".ogg", ".wav", ".mp3")):
            bad("AudioPaths.Bgm%s 音频不存在: Sound/BGM/%s" % (m.group(1), m.group(2)))
    print("资源：ResPaths 常量 %d 个 / 特效段 %d 段 / SFX %d 条" % (len(raw), len(seg), len(sfx)))


def main():
    check_protocol()
    check_tables()
    check_resources()
    if problems:
        print("\n差异 %d 条：" % len(problems))
        for p in problems:
            print("  -", p)
        return 1
    print("\n全部一致（协议 / 配表 / 资源）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
