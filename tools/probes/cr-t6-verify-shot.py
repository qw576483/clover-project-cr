# CR-T6: 对**新**实机截图做像素断言（河道=水、桥=木、不再是泥土）+ 生成同机位并排对照图
# 复跑：python .ai-tmp/test/t6-verify-shot.py
import os, sys
import numpy as np
from PIL import Image, ImageDraw

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
REF = os.path.join(ROOT, r"策划\参考图\20_对局_1080x1920.jpg")
OUR = os.path.join(ROOT, r".ai-tmp\screenshots\CR-T6-arena-nohud.png")
SHOTS = os.path.join(ROOT, r".ai-tmp\screenshots")
fails = []


def chk(tag, ok, d):
    print(f"[{'PASS' if ok else 'FAIL'}] {tag}: {d}")
    if not ok:
        fails.append(tag)


our = np.asarray(Image.open(OUR).convert("RGB")).astype(np.int16)
print("新实机图", our.shape)
chk("新图是 1080x1920（与 ref20 同画布）", our.shape[:2] == (1920, 1080), str(our.shape[:2]))

# 我方标定：60 px/格，格0(后沿) ⇔ y1920；格 x0 ⇔ px0
def oy(tile):  # tile y -> 屏幕 y
    return int(round(1920 - tile * 60))


def ox(tile):
    return int(round(tile * 60))


# ① 河道带（格 y15..17 ⇒ py 900..1020）应以水（蓝）为主
band = our[oy(17):oy(15), ox(0):ox(18), :]
r, g, b = band[:, :, 0], band[:, :, 1], band[:, :, 2]
blue = (b > r + 30) & (b > 100)
water_frac = float(blue.mean())
chk("① 河道带以水（蓝）为主 (>=0.55)", water_frac >= 0.55,
    f"蓝占比 {water_frac:.3f}（水均色 RGB{tuple(int(band.reshape(-1,3)[blue.reshape(-1)][:,i].mean()) for i in range(3)) if blue.any() else '-'}）")
# 泥土判据只在水段上判（格 0..2.5 / 4.5..13.5 / 15.5..18 —— 扣掉两座桥占的 4 格，
# 桥面本来就是木色 R>B，混进来会让判据恒假/恒真）
water_mask = np.zeros(band.shape[:2], bool)
for x0t, x1t in ((0.0, 2.5), (4.5, 13.5), (15.5, 18.0)):
    water_mask[:, ox(x0t) - ox(0):ox(x1t) - ox(0)] = True
mud = ((r > b + 25) & (r > 110)) & water_mask
chk("① 水段（扣掉两座桥）里的褐色泥土占比 <0.08", float(mud.sum()) / float(water_mask.sum()) < 0.08,
    f"泥土占比 {float(mud.sum()) / float(water_mask.sum()):.4f}（水段 {int(water_mask.sum())} px）")

# ② 两座桥（格 x2.5..4.5 / 13.5..15.5）在河道带上应是木质（R>B）
for tag, a, c in (("左桥", 2.5, 4.5), ("右桥", 13.5, 15.5)):
    col = our[oy(17):oy(15), ox(a):ox(c), :]
    r2, b2, g2 = col[:, :, 0], col[:, :, 2], col[:, :, 1]
    wood = (r2 > b2 + 20) & (r2 > 120)
    chk(f"② {tag} 位以木质（R>B）为主", float(wood.mean()) > 0.3,
        f"木色占比 {float(wood.mean()):.3f}，均色 RGB{tuple(int(col.reshape(-1,3)[:,i].mean()) for i in range(3))}")

# ③ 旧版特征必须消失：河道带里不应再有大面积"灰石"（低饱和中灰）
rs, gs, bs = band.reshape(-1, 3)[:, 0], band.reshape(-1, 3)[:, 1], band.reshape(-1, 3)[:, 2]
hsv_like = (np.abs(rs - gs) < 18) & (np.abs(gs - bs) < 18) & (rs > 110) & (rs < 200)
chk("③ 灰石块占比 <0.10", float(hsv_like.mean()) < 0.10, f"灰占比 {float(hsv_like.mean()):.3f}")

# ── 同机位并排图（左 = 原版 ref20，右 = 我方新图）─────────────────────────────
RX0, RPPTX, RY0, RPPTY = 13.0, 58.6, 1611.0, 45.844
OURS_PPT, OUR_X0, OUR_Y0 = 60.0, 0.0, 1920.0


def pair(name, tx0, ty0, tx1, ty1, scale=2):
    rb = (int(RX0 + tx0 * RPPTX), int(RY0 - ty1 * RPPTY), int(RX0 + tx1 * RPPTX), int(RY0 - ty0 * RPPTY))
    ob = (int(OUR_X0 + tx0 * OURS_PPT), int(OUR_Y0 - ty1 * OURS_PPT), int(OUR_X0 + tx1 * OURS_PPT), int(OUR_Y0 - ty0 * OURS_PPT))
    a = Image.open(REF).convert("RGB").crop(rb)
    c = Image.open(OUR).convert("RGB").crop(ob).resize(a.size, Image.LANCZOS)
    W, H = a.size
    W2, H2 = int(W * scale), int(H * scale)
    a2, c2 = a.resize((W2, H2), Image.NEAREST), c.resize((W2, H2), Image.NEAREST)
    pad, lab = 8, 26
    sh = Image.new("RGB", (W2 * 2 + pad, H2 + lab), (25, 25, 25))
    sh.paste(a2, (0, lab)); sh.paste(c2, (W2 + pad, lab))
    d = ImageDraw.Draw(sh)
    d.text((4, 6), f"REF ref20 tile({tx0},{ty0})-({tx1},{ty1}) refpx{rb}", fill=(255, 220, 120))
    d.text((W2 + pad + 4, 6), f"OURS CR-T6-arena-nohud tile({tx0},{ty0})-({tx1},{ty1}) px{ob}", fill=(120, 220, 255))
    p = os.path.join(SHOTS, name)
    sh.save(p)
    print("->", p, sh.size)


pair("CR-T6-cmp2-river.png", 0.0, 14.0, 18.0, 18.0, 1)
pair("CR-T6-cmp2-bridge.png", 2.2, 13.5, 4.8, 18.5, 4)
pair("CR-T6-cmp2-ground.png", 5.0, 8.0, 13.0, 14.0, 2)
pair("CR-T6-cmp2-rear.png", 0.0, 0.0, 18.0, 4.0, 1)

print()
print(f"summary: FAIL={len(fails)}  {fails if fails else '(全部通过)'}")
sys.exit(1 if fails else 0)
