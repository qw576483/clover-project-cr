import os
import numpy as np
from PIL import Image
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
A = np.asarray(Image.open(os.path.join(ROOT, r"策划\参考图\18_对局HUD_1080x1920.jpg")).convert("RGB")).astype(np.int16)
print("竖剖 x=300（车道之间）：")
for y in range(740, 1000, 8):
    print(f"  y={y}  {tuple(int(A[y,300,i]) for i in range(3))}   {tuple(int(A[y,540,i]) for i in range(3))}   {tuple(int(A[y,700,i]) for i in range(3))}")
print("横剖 y=880：")
print("  ", [ (x, tuple(int(A[880,x,i]) for i in range(3))) for x in range(0, 1080, 80) ])
print("横剖 y=860：")
print("  ", [ (x, tuple(int(A[860,x,i]) for i in range(3))) for x in range(0, 1080, 80) ])
# 青水判据（收紧）：g 明显大于 r 且 b 明显大于 r
cy = (A[:,:,1] > A[:,:,0] + 45) & (A[:,:,2] > A[:,:,0] + 45) & (A[:,:,2] > 150)
rows = cy.sum(axis=1)
hit = np.where(rows > 120)[0]
print("收紧判据青水行(>120px):", (hit.min(), hit.max()) if hit.size else "-", f"共 {hit.size} 行")
if hit.size:
    y0, y1 = int(hit.min()), int(hit.max())
    m = cy[y0:y1+1]
    xs = np.where(m.any(axis=0))[0]
    print(f"  水带 y {y0}..{y1} 厚 {y1-y0+1}  x {xs.min()}..{xs.max()}")
    print("  均色 =", tuple(int(A[y0:y1+1].reshape(-1,3)[m.reshape(-1)][:,i].mean()) for i in range(3)))
    mid=(y0+y1)//2
    row=cy[mid]; segs=[]; c=None
    for x in range(1080):
        if row[x]:
            c=[x,x] if c is None else [c[0],x]
        else:
            if c and c[1]-c[0]>8: segs.append((c[0],c[1]))
            c=None
    if c and c[1]-c[0]>8: segs.append((c[0],c[1]))
    print(f"  中点行 y={mid} 水段={segs}")
    for yy in range(y0, y1+1, 6):
        print(f"    y={yy} 行均色 {tuple(int(A[yy,:,i].mean()) for i in range(3))} 水像素 {int(cy[yy].sum())} 金像素 {int(((A[yy,:,0]>225)&(A[yy,:,1]>165)&(A[yy,:,2]<165)).sum())}")
