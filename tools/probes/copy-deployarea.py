# -*- coding: utf-8 -*-
"""copy-deployarea.py -- land the original deploy-area frames used by CR.View.DeployAreaView.

Source atlas : <root>/原版资源/cr-assets-png/assets/sc/ui_out/ui_sprite_NNN.png
Destination  : <root>/client/Assets/Resources/Sprites/Effects/DeployArea/frame_NNN.png
               (read at runtime through ResPaths.EffectFrame(ResPaths.EffectDeployArea, N))

The cr-assets-png dump writes each shape onto its full sheet canvas (1663x2810), so every
frame is cropped to its visible bbox here -- the landed PNG is the piece itself, and
DeployAreaView places it by that rect (its pixel size is the piece's size).

Frame table (CR 2.1.5 `ui`, export list in 策划/原版UI素材名称索引.md:304-316; the number is
the `12` record's own index == the frame index in the source file name):

  203  tile_illegal                     1x1   (not used by DeployAreaView yet)
  241  deployArea_side_top             61x16
  242  deployArea_side_right           13x49
  243  deployArea_side_left            13x49
  244  deployArea_side_bottom          61x16
  245  deployArea_innerCorner_topLeft/topRight   14x16  (not wired)
  246  deployArea_innerCorner_bottomLeft/bottomRight 14x16 (not wired)
  247  deployArea_corner_rightTop      62x55
  248  deployArea_corner_rightBottom   62x50
  249  deployArea_corner_leftTop       61x55
  250  deployArea_corner_leftBottom    61x50
  251  deployArea_base                 1x1   RGBA(23,0,0,23)

Timer frames (clip 1245 `troopDeployTimer_player` / 1247 `troopDeployTimer_enemy`, group `spawn`;
`策划/原版UI素材名称索引.md:433-434`), destination Sprites/Effects/DeployTimer:

  252  body (grey clock face)            56x67
  253  sweep wedge, player (blue)        21x21
  255  stem / hand (grey)                 6x22
  256  sweep wedge, enemy                21x21

Usage:  python tools/probes/copy-deployarea.py
"""
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, '原版资源', 'cr-assets-png', 'assets', 'sc', 'ui_out')
DST = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Effects', 'DeployArea')
DST_TIMER = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Effects', 'DeployTimer')

FRAMES = [203, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251]
TIMER_FRAMES = [252, 253, 255, 256]


def land(frames, dst_dir):
    os.makedirs(dst_dir, exist_ok=True)
    for frame in frames:
        src = os.path.join(SRC, 'ui_sprite_%03d.png' % frame)
        dst = os.path.join(dst_dir, 'frame_%03d.png' % frame)
        if not os.path.isfile(src):
            # 非预期分支必须留痕（不许静默跳过）
            print('MISSING %s -- frame not in the dump, landed files are now incomplete' % src)
            continue
        with Image.open(src) as im:
            im = im.convert('RGBA')
            bbox = im.getbbox()
            crop = im.crop(bbox) if bbox else Image.new('RGBA', (1, 1), (0, 0, 0, 0))
            # dominant opaque colour: tells the tint/colour apart without eyeballing the shot
            from collections import Counter
            cnt = Counter(p for p in crop.getdata() if p[3] > 200)
            top = cnt.most_common(1)
        crop.save(dst)
        print('frame_%03d  bbox=%s  landed=%dx%d  dominant=%s  %dB'
              % (frame, bbox, crop.width, crop.height, top[0][0] if top else '-', os.path.getsize(dst)))
    print('dst:', dst_dir, 'files:', len(os.listdir(dst_dir)))


def main():
    land(FRAMES, DST)
    land(TIMER_FRAMES, DST_TIMER)


if __name__ == '__main__':
    main()
