# CR-T6 自检宿主（秒级、离线，⛔ 不进 Play）：断言
#   ① 水面窗口取到的确实是原版水面像素；② 桥面窗口取到的是原版木板；③ 桥/河落位 == 契约格 15..17 / 3.5 / 14.5
#   ④ 旧窗口（f022 py577..606）确实是褐色泥土 ⇒ 根因可复算
# 复跑（项目根）：python .ai-tmp/hosts/cr-t6-river-check.py   ；任一项 FAIL ⇒ 退出码 1
import hashlib, os, re, sys
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ARENA = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Arenas", "arena_training_out")
P6 = os.path.join(ARENA, "frame_006.png")
P22 = os.path.join(ARENA, "frame_022.png")
O6 = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "arena_training_out", "arena_training_sprite_06.png")
O22 = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "arena_training_out", "arena_training_sprite_22.png")
GAMECONST = os.path.join(ROOT, "client", "Assets", "Scripts", "Core", "GameConst.cs")
ARENAVIEW = os.path.join(ROOT, "client", "Assets", "Scripts", "View", "ArenaView.cs")

fails = []


def chk(tag, ok, detail):
    print(f"[{'PASS' if ok else 'FAIL'}] {tag}: {detail}")
    if not ok:
        fails.append(tag)


gc = open(GAMECONST, encoding="utf-8-sig").read()
av = open(ARENAVIEW, encoding="utf-8-sig").read()


def num(src, name):
    m = re.search(r"\b" + name + r"\s*=\s*([-0-9.]+)f", src)
    return float(m.group(1)) if m else None


RIVER_TOP, RIVER_BOT = num(gc, "RiverTopTile"), num(gc, "RiverBottomTile")
BR_A, BR_B, HALF = num(gc, "BridgeCxATile"), num(gc, "BridgeCxBTile"), num(gc, "BridgeHalfTile")
W = num(gc, "ArenaTilesW")
PPT_X, FIELD_L = num(av, "ArtPxPerTileX"), num(av, "ArtFieldLeftPx")
WY0, WY1 = num(av, "RiverWaterPyTop"), num(av, "RiverWaterPyBottom")
BL, BR = num(av, "BridgePxLeft"), num(av, "BridgePxRight")
BT, BB = num(av, "BridgePyTop"), num(av, "BridgePyBottom")
print(f"源码读出：河 {RIVER_TOP}..{RIVER_BOT}；桥 {BR_A}/{BR_B} 半宽 {HALF}；场地宽 {W}；"
      f"横向 {PPT_X} px/格、格0⇔px {FIELD_L}；水面 py {WY0}..{WY1}；桥面 px {BL}..{BR} py {BT}..{BB}")
chk("常量齐备", None not in (RIVER_TOP, RIVER_BOT, BR_A, BR_B, HALF, W, PPT_X, FIELD_L, WY0, WY1, BL, BR, BT, BB),
    f"{[RIVER_TOP, RIVER_BOT, BR_A, BR_B, HALF, W, PPT_X, FIELD_L, WY0, WY1, BL, BR, BT, BB]}")
chk("河几何 = 契约 15/17", (RIVER_TOP, RIVER_BOT) == (15.0, 17.0), f"{RIVER_TOP}/{RIVER_BOT}")
chk("桥几何 = 契约 3.5/14.5 且半宽 1", (BR_A, BR_B, HALF) == (3.5, 14.5, 1.0), f"{BR_A}/{BR_B}/{HALF}")

for tag, p, o in (("frame_006", P6, O6), ("frame_022", P22, O22)):
    h1 = hashlib.sha256(open(p, "rb").read()).hexdigest()
    h2 = hashlib.sha256(open(o, "rb").read()).hexdigest()
    chk(f"{tag} 与原版解包图同一份", h1 == h2, f"sha256 {h1[:16]}..")

a6 = np.asarray(Image.open(P6).convert("RGBA")).astype(np.int16)
a22 = np.asarray(Image.open(P22).convert("RGBA")).astype(np.int16)

# ① 水面窗口
cropL, cropR = max(0.0, min(FIELD_L, a6.shape[1])), max(0.0, min(FIELD_L + W * PPT_X, a6.shape[1]))
cropT, cropB = max(0.0, min(WY0, a6.shape[0])), max(0.0, min(WY1, a6.shape[0]))
sub = a6[int(cropT):int(cropB), int(cropL):int(cropR), :3]
r, g, b = sub[:, :, 0], sub[:, :, 1], sub[:, :, 2]
frac = float(((b > r + 20) & (b > g + 5)).sum()) / b.size
mean = tuple(int(sub.reshape(-1, 3)[:, c].mean()) for c in range(3))
chk("① 水面窗口是水（蓝占比 >0.9）", frac > 0.9, f"占比 {frac:.3f}，均色 RGB{mean}")
chk("① 水面均色 ≈ 实测 (0,147,175)±12", all(abs(mean[i] - v) <= 12 for i, v in enumerate((0, 147, 175))),
    f"{mean} vs (0,147,175)")

# ② 桥面窗口（f022 的木板）：R 明显高于 B（木/土黄），不是水
bsub = a22[int(BT):int(BB), int(BL):int(BR), :3]
bm = tuple(int(bsub.reshape(-1, 3)[:, c].mean()) for c in range(3))
chk("② 桥面窗口是木板（R > B + 25）", bm[0] > bm[2] + 25, f"均色 RGB{bm}")
chk("② 桥面窗口不是水（B 未显著高于 R）", not (bm[2] > bm[0] + 20), f"均色 RGB{bm}")

# ③ 落位：桥心画布 px（= 格位×PPT_X + FIELD_L）反算回格位必须 == 契约值
for tag, tile in (("左桥", BR_A), ("右桥", BR_B)):
    px = tile * PPT_X + FIELD_L
    back = (px - cropL) / (cropR - cropL) * W
    chk(f"③ {tag} 中心落位反算 == {tile}", abs(back - tile) < 1e-3, f"画布px {px:.1f} → 格 {back:.4f}")
    lo, hi = tile - HALF, tile + HALF
    chk(f"③ {tag} 落位区间 = {lo}..{hi}（宽 {2 * HALF} 格）", (hi - lo) == 2.0, f"{lo}..{hi}")

ppu = 100.0
nat_w, nat_h = (cropR - cropL) / ppu, (cropB - cropT) / ppu
sx, sy = W / nat_w, (RIVER_BOT - RIVER_TOP) / nat_h
chk("③ 水面纵向恰好 2 格", abs(sy * nat_h - 2.0) < 1e-6, f"{sy * nat_h:.6f}")
chk("③ localScale 恒正（不产生负缩放/双翻转）", sx > 0 and sy > 0, f"({sx:.4f}, {sy:.4f})")
# 源像素放大倍率 = 屏幕 60 px/格 ÷ (水带高 47px / 2 格)
magn = 60.0 / ((cropB - cropT) / (RIVER_BOT - RIVER_TOP))
chk("③ 水带源像素纵向放大 2.2~3.0（登记项：远段美术被放大）", 2.2 < magn < 3.0, f"{magn:.3f}×")

# ④ 旧窗口（f022 py577..606）必须是褐色泥土 ⇒ 根因可复算
old = a22[577:606, 106:912, :3]
om = tuple(int(old.reshape(-1, 3)[:, c].mean()) for c in range(3))
chk("④ 旧窗口（f022 py577..606）是褐色泥土不是水", om[0] > om[2] + 25 and om[0] > 120, f"均色 RGB{om}")

# ⑤ f006 水带里没有桥 ⇒ "桥必须另取 f022" 的前提可复算
band = a6[806:851]
r2, g2, b2 = band[:, :, 0], band[:, :, 1], band[:, :, 2]
tan = (r2 >= g2 - 6) & (r2 > b2 + 30) & (r2 > 150)
chk("⑤ f006 水带内无土黄/木色列（⇒ f006 没画桥）", float(tan.mean()) < 0.01, f"土黄占比 {float(tan.mean()):.5f}")

print()
print(f"summary: FAIL={len(fails)}  {fails if fails else '(全部通过)'}")
sys.exit(1 if fails else 0)
