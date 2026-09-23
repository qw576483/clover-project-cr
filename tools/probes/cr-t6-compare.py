# CR-T6: 同机位并排图（左 = 原版 `20_对局_1080x1920.jpg`，右 = 我方实机 `CR-T1-towers-nohud.png`）
# 两图都是 1080x1920；原版 ~58.6x45.84 px/格（场地 x13..1067 / y144..1611），我方严格 60x60 px/格（铺满 18x32）。
# 复跑：python .ai-tmp/test/t6-compare.py
import os
from PIL import Image, ImageDraw

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
REF = os.path.join(ROOT, r"策划\参考图\20_对局_1080x1920.jpg")
OUR = os.path.join(ROOT, r".ai-tmp\screenshots\CR-T1-towers-nohud.png")
OUT = os.path.join(ROOT, r".ai-tmp\screenshots")

# 原版标定（量取，见 t6-measure/t6-refscan 输出）
REF_X0, REF_PPT_X = 13.0, 58.6      # 格 0 ⇔ px 13；58.6 px/格
REF_Y0, REF_PPT_Y = 1611.0, 45.844  # 格 0 ⇔ px 1611；45.844 px/格（y 向上 = 格 y 增大）
# 我方标定
OUR_PPT = 60.0
OUR_X0, OUR_Y0 = 0.0, 1920.0


def ref_box(tx0, ty0, tx1, ty1):
    x0 = REF_X0 + tx0 * REF_PPT_X
    x1 = REF_X0 + tx1 * REF_PPT_X
    y0 = REF_Y0 - ty1 * REF_PPT_Y  # 上边
    y1 = REF_Y0 - ty0 * REF_PPT_Y  # 下边
    return (int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1)))


def our_box(tx0, ty0, tx1, ty1):
    x0 = OUR_X0 + tx0 * OUR_PPT
    x1 = OUR_X0 + tx1 * OUR_PPT
    y0 = OUR_Y0 - ty1 * OUR_PPT
    y1 = OUR_Y0 - ty0 * OUR_PPT
    return (int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1)))


def pair(name, tx0, ty0, tx1, ty1, scale=2):
    r = Image.open(REF).convert("RGB").crop(ref_box(tx0, ty0, tx1, ty1))
    o = Image.open(OUR).convert("RGB").crop(our_box(tx0, ty0, tx1, ty1))
    o = o.resize(r.size, Image.LANCZOS)  # 统一到同一输出尺寸（同裁切框的格矩形）
    W, H = r.size
    W2, H2 = int(W * scale), int(H * scale)
    r = r.resize((W2, H2), Image.NEAREST)
    o = o.resize((W2, H2), Image.NEAREST)
    pad, lab = 8, 26
    sh = Image.new("RGB", (W2 * 2 + pad, H2 + lab), (25, 25, 25))
    sh.paste(r, (0, lab))
    sh.paste(o, (W2 + pad, lab))
    d = ImageDraw.Draw(sh)
    d.text((4, 6), f"REF  tile({tx0},{ty0})-({tx1},{ty1})  refpx{ref_box(tx0,ty0,tx1,ty1)}", fill=(255, 220, 120))
    d.text((W2 + pad + 4, 6), f"OURS tile({tx0},{ty0})-({tx1},{ty1})  px{our_box(tx0,ty0,tx1,ty1)}", fill=(120, 220, 255))
    p = os.path.join(OUT, name)
    sh.save(p)
    print("->", p, sh.size)


pair("CR-T6-cmp-ground.png", 5.0, 8.0, 13.0, 14.0, 2)      # 草地（无路）
pair("CR-T6-cmp-paving.png", 2.0, 7.5, 5.5, 14.0, 2)       # 铺装路 + 广场
pair("CR-T6-cmp-river.png", 0.0, 14.0, 18.0, 18.0, 1)      # 河道（全宽）
pair("CR-T6-cmp-bridge.png", 2.2, 13.5, 4.8, 18.5, 4)      # 左桥
pair("CR-T6-cmp-bridgeR.png", 13.2, 13.5, 15.8, 18.5, 4)   # 右桥
pair("CR-T6-cmp-rear.png", 0.0, 0.0, 18.0, 4.0, 1)         # 场地后沿（BLUE 后方）
pair("CR-T6-cmp-plaza.png", 1.0, 4.0, 6.0, 9.0, 2)         # 左公主塔广场
