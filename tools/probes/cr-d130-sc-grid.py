# cr-d130-sc-grid.py -- 把 training_area_bg(clip48) 的 85 个实例按 (tx,ty) 整理成网格并推断 tile 尺寸。
# 复用 sc-layout.py 的 parse（口径同 策划/原版UI布局坐标.md §1）。只读。
import importlib.util, os, sys
for _s in ('stdout', 'stderr'):
    try: getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception: pass
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
spec = importlib.util.spec_from_file_location('sl', os.path.join(ROOT, 'tools', 'probes', 'sc-layout.py'))
sl = importlib.util.module_from_spec(spec); spec.loader.exec_module(sl)
raw = open(os.path.join(ROOT, '原版资源', 'sc', 'arena_training_v215.sc'), 'rb').read()
res = sl.parse(raw)

c = res['clips'][48]
rows = []
for (ci, mi, coi) in c['tris']:
    if ci >= len(c['children']): continue
    oid, nm, op = c['children'][ci]
    m = sl.matrix(res['mats'], mi)
    rows.append((ci, oid, m[0]/sl.SCALE_DIV, m[3]/sl.SCALE_DIV, m[4]/sl.TRANS_DIV, m[5]/sl.TRANS_DIV))

objs = sorted({r[1] for r in rows})
print("clip48 children=%d  distinct obj=%s" % (len(rows), objs))
scales = sorted({(round(r[2],4), round(r[3],4)) for r in rows})
print("distinct scale=%s" % scales)

TX = sorted({round(r[4],3) for r in rows})
TY = sorted({round(r[5],3) for r in rows})
print("distinct tx (%d) = %s" % (len(TX), TX))
print("distinct ty (%d) = %s" % (len(TY), TY))
print("tx step = %s" % [round(TX[i+1]-TX[i],3) for i in range(len(TX)-1)])
print("ty step = %s" % [round(TY[i+1]-TY[i],3) for i in range(len(TY)-1)])

print()
print("=== 网格图（行=ty 降序，列=tx 升序；X=有实例） ===")
hdr = "            " + "".join("%8.1f" % t for t in TX)
print(hdr)
grid = {}
for r in rows:
    grid[(round(r[4],3), round(r[5],3))] = grid.get((round(r[4],3), round(r[5],3)), 0) + 1
for ty in sorted(TY, reverse=True):
    line = "ty=%8.1f " % ty
    for tx in TX:
        line += "%8s" % (grid.get((tx, ty), ".") if grid.get((tx, ty), 0) == 1 else "x%d" % grid[(tx, ty)])
    print(line)

print()
print("=== 全部 85 条（按 ty 降序、tx 升序） ===")
for r in sorted(rows, key=lambda z: (-z[5], z[4])):
    print("  child=%-3d obj=%-3d sx=%.3f sy=%.3f tx=%8.3f ty=%8.3f" % r)
