# CR-V1: 列出 t6/blue-candidates.json 里 arena/level 素材的蓝色区域候选
import json

p = r"C:\Work\Server\f-v2\clover-project-cr\.ai-tmp\test\t6\blue-candidates.json"
d = json.load(open(p, encoding="utf-8"))
ar = [e for e in d if "arena" in e["path"].lower() or e["path"].lower().startswith("level_")]
ar.sort(key=lambda e: -e["px"])
print("arena/level entries =", len(ar))
for e in ar[:40]:
    print("%-40s crop %5dx%-5d px=%-9d mean=%-16s y%4d..%-4d x%4d..%-4d"
          % (e["path"], e["w"], e["h"], e["px"], str(e["mean_rgb"]),
             e["rect_y0"], e["rect_y1"], e["rect_x0"], e["rect_x1"]))
