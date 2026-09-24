# cr-d130-ref20-river.py -- 把 策划/参考图/20_对局_1080x1920.jpg 的河区放大若干倍，用于人工判读
# 「河的两岸各有什么」（泥带 / 木栏 / 灰石 是不是两岸都有）。只读。
from PIL import Image

SRC = r"C:\Work\Server\f-v2\clover-project-cr\策划\参考图\20_对局_1080x1920.jpg"
OUT = r"C:\Work\Server\f-v2\clover-project-cr\.ai-tmp\test\d130x\ref20_river_zoom.png"
OUT2 = r"C:\Work\Server\f-v2\clover-project-cr\.ai-tmp\test\d130x\ref20_river_native.png"

im = Image.open(SRC).convert("RGB")
W, H = im.size
print("ref20", W, H)
# 参考图 20 的河大约在 y 440..500（目视）；取宽一点 y 400..540 全宽
box = (60, 395, W - 60, 545)
crop = im.crop(box)
crop.resize((crop.width * 3, crop.height * 3), Image.NEAREST).save(OUT)
crop.save(OUT2)
print("wrote", OUT, crop.size, "->x3")
print("wrote", OUT2, crop.size)
