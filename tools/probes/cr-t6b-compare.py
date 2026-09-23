# CR-T6b: 基线换成 18 的同格并排图（左 = 18 原版，右 = 我方 CR-T6-arena-nohud）
# 18 标定（本片实测）：车道金带中心 x 255/850 ⇒ pptx 54.1、格0 ⇔ x 65.6；河心（格16）y 872.5、ppty ≈ 43.0
# 我方标定：60 px/格，格0 ⇔ (x0, y1920)
# 复跑：python .ai-tmp/test/t6b-compare.py
import os
from PIL import Image, ImageDraw

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
B18 = os.path.join(ROOT, r"策划\参考图\18_对局HUD_1080x1920.jpg")
OUR = os.path.join(ROOT, r".ai-tmp\screenshots\CR-T6-arena-nohud.png")
SHOTS = os.path.join(ROOT, r".ai-tmp\screenshots")

B_X0, B_PPTX = 65.6, 54.1
B_RIVER_Y, B_PPTY = 872.5, 43.0
O_PPT, O_X0, O_Y0 = 60.0, 0.0, 1920.0


def b18_box(tx0, ty0, tx1, ty1):
    return (int(B_X0 + tx0 * B_PPTX), int(B_RIVER_Y - (ty1 - 16) * B_PPTY),
            int(B_X0 + tx1 * B_PPTX), int(B_RIVER_Y - (ty0 - 16) * B_PPTY))


def our_box(tx0, ty0, tx1, ty1):
    return (int(O_X0 + tx0 * O_PPT), int(O_Y0 - ty1 * O_PPT), int(O_X0 + tx1 * O_PPT), int(O_Y0 - ty0 * O_PPT))


def pair(name, tx0, ty0, tx1, ty1, scale=2, lab=""):
    rb, ob = b18_box(tx0, ty0, tx1, ty1), our_box(tx0, ty0, tx1, ty1)
    a = Image.open(B18).convert("RGB").crop(rb)
    c = Image.open(OUR).convert("RGB").crop(ob).resize(a.size, Image.LANCZOS)
    W, H = a.size
    W2, H2 = int(W * scale), int(H * scale)
    a2, c2 = a.resize((W2, H2), Image.NEAREST), c.resize((W2, H2), Image.NEAREST)
    pad, head = 8, 30
    sh = Image.new("RGB", (W2 * 2 + pad, H2 + head), (25, 25, 25))
    sh.paste(a2, (0, head)); sh.paste(c2, (W2 + pad, head))
    d = ImageDraw.Draw(sh)
    d.text((4, 4), f"BASELINE 18  tile({tx0},{ty0})-({tx1},{ty1}) px{rb}", fill=(255, 220, 120))
    d.text((4, 16), f"{lab}", fill=(200, 200, 200))
    d.text((W2 + pad + 4, 4), f"OURS CR-T6-arena-nohud tile({tx0},{ty0})-({tx1},{ty1}) px{ob}", fill=(120, 220, 255))
    p = os.path.join(SHOTS, name)
    sh.save(p)
    print("->", p, sh.size)


pair("CR-T6b-cmp-river.png", 0.0, 14.2, 18.0, 17.8, 1, "Z-river: full width, tiles y14.2..17.8")
pair("CR-T6b-cmp-bridgeL.png", 2.3, 14.0, 4.7, 18.0, 4, "Z-bridge left: centre tile 3.5, width 2")
pair("CR-T6b-cmp-ground.png", 6.0, 9.0, 12.0, 14.0, 2, "Z-ground: no lane, tiles x6..12 y9..14")
pair("CR-T6b-cmp-rear.png", 2.0, 26.0, 16.0, 32.0, 1, "Z-rear: enemy half rear, tiles y26..32")
pair("CR-T6b-cmp-plaza.png", 1.0, 4.5, 6.0, 9.0, 2, "Z-plaza: player left princess plaza")
