# CR-V1: 列出所有 level_*_arena_v215.sc / arena_training_v215.sc 的导出名，找「桥/河/水面/地面」类部件
# 复跑: python .ai-tmp/test/cr-v1-dump-arena-exports.py
import os, sys, importlib.util, re
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
spec = importlib.util.spec_from_file_location("sai", os.path.join(ROOT, "tools", "probes", "sc-anim-index.py"))
sai = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sai)
SC = os.path.join(ROOT, "原版资源", "sc")

KEY = re.compile(r"bridge|river|water|ground|floor|tile|lane|bank|stone|path|road|arena|wall|fence|deco", re.I)

files = sorted(f for f in os.listdir(SC) if f.endswith(".sc") and ("arena" in f))
print("sc files =", files)
for f in files:
    raw = open(os.path.join(SC, f), "rb").read()
    d = sai.parse_unit(raw)
    names = list(zip(d.get("exp_names", []), d.get("exp_ids", [])))
    print("\n=== %s  exports=%d shapenum=%s ===" % (f, len(names), d.get("ShapeCount")))
    for nm, cid in names:
        mark = "  <== " if KEY.search(nm) else ""
        print("   clip=%-6s %s%s" % (cid, nm, mark))
