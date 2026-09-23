#!/usr/bin/env python3
# CR-T3 contact sheet + objective pixel assertions for the "ghost follows the pointer / placement disc
# goes green-red" performance-class evidence. Reads the real-machine 1080x1920 captures, crops the
# pointer region and the disc region, and measures them (no eyeballing needed, but the sheet lets a
# human/AI see it at a glance).
#
# Usage:
#   python cr-t3-contact.py --out <sheet.png> --baseline <no-drag frame.png> \
#       --frame legal:<png>:<ptr_x>:<ptr_y>:<disc_x>:<disc_y> \
#       --frame illegal:<png>:<ptr_x>:<ptr_y>:<disc_x>:<disc_y>
# Notes: png pixel coords are bottom-left origin (Unity screen space); the script flips Y.
import argparse
from PIL import Image, ImageDraw, ImageChops, ImageStat

BOX = 320          # crop size (px) shown at 1:1 side by side
FRAME_W = 300      # scaled full-frame width in the sheet


def flipy(h, y):
    return int(round(h - 1 - y))


def crop_around(im, cx, cy, box):
    w, h = im.size
    left = max(0, min(w - box, int(cx - box / 2)))
    top = max(0, min(h - box, flipy(h, cy) - box // 2))
    return im.crop((left, top, left + box, top + box)), (left, top)


def mean_rgb(im):
    s = ImageStat.Stat(im.convert("RGB")).mean
    return tuple(round(v, 1) for v in s)


def diff_mean(a, b):
    d = ImageChops.difference(a.convert("RGB"), b.convert("RGB"))
    return round(sum(ImageStat.Stat(d).mean) / 3.0, 2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--baseline", required=True)
    ap.add_argument("--frame", action="append", default=[],
                    help="name:png:ptrx:ptry:discx:discy")
    a = ap.parse_args()

    base = Image.open(a.baseline).convert("RGB")
    base_gray = base.convert("L")
    print("baseline %s %s" % (a.baseline, base.size))

    rows = []
    for spec in a.frame:
        # name:png:ptrx,ptry:discx,discY -- split from the right, a Windows path carries its own ':'
        name, rest = spec.split(":", 1)
        png, ptr, disc = rest.rsplit(":", 2)
        px, py = ptr.split(",")
        dx, dy = disc.split(",")
        im = Image.open(png).convert("RGB")
        w, h = im.size
        ghost_crop, gpos = crop_around(im, float(px), float(py), BOX)
        disc_crop, dpos = crop_around(im, float(dx), float(dy), BOX)
        base_ghost = base.crop((gpos[0], gpos[1], gpos[0] + BOX, gpos[1] + BOX))
        base_disc = base.crop((dpos[0], dpos[1], dpos[0] + BOX, dpos[1] + BOX))
        gdiff = diff_mean(ghost_crop, base_ghost)
        ddiff = diff_mean(disc_crop, base_disc)
        # a card face is a high-contrast object: count pixels that changed a lot, and the peak change
        gd = ImageChops.difference(ghost_crop.convert("L"), base_ghost.convert("L"))
        ghist = gd.histogram()
        strong = sum(ghist[31:])
        gmax = max(i for i, c in enumerate(ghist) if c > 0)
        # the disc is only ~120 px across and semi-transparent (alpha 0.35): measure its 80x80 centre
        cbox = 80
        gxc, gyc = int(float(px)), flipy(h, float(py))
        dcrop = im.crop((max(0, int(float(dx)) - cbox // 2), max(0, gyc - cbox // 2),
                         max(0, int(float(dx)) - cbox // 2) + cbox, max(0, gyc - cbox // 2) + cbox))
        dcrop_base = base.crop((max(0, int(float(dx)) - cbox // 2), max(0, gyc - cbox // 2),
                                max(0, int(float(dx)) - cbox // 2) + cbox, max(0, gyc - cbox // 2) + cbox))
        drgb = mean_rgb(dcrop)
        dominant = "GREEN" if drgb[1] > drgb[0] else "RED"
        dcentre_diff = diff_mean(dcrop, dcrop_base)
        print("%-8s %s %dx%d ghost@%s meanDiff=%s strongPix(d>30)=%s peakDiff=%s | discCentre80 meanRGB=%s dominant=%s diff_vs_baseline=%s"
              % (name, png, w, h, gpos, gdiff, strong, gmax, drgb, dominant, dcentre_diff))
        rows.append((name, im, ghost_crop, disc_crop, gdiff, ddiff, drgb, dominant, float(px), float(py), float(dx), float(dy)))

    # ---- sheet ----
    row_h = BOX + 46
    sheet = Image.new("RGB", (FRAME_W + 2 * BOX + 40, row_h * len(rows) + 30), (24, 24, 28))
    d = ImageDraw.Draw(sheet)
    d.text((8, 8), "CR-T3 drag evidence: full frame | ghost crop @pointer | disc crop @drop  (baseline = %s)" % a.baseline, fill=(255, 255, 255))
    for i, (name, im, ghost_crop, disc_crop, gdiff, ddiff, drgb, dominant, px, py, dx, dy) in enumerate(rows):
        y0 = 30 + i * row_h
        thumb = im.resize((FRAME_W, int(im.size[1] * FRAME_W / im.size[0])))
        thumb = thumb.crop((0, 0, FRAME_W, BOX))
        sheet.paste(thumb, (8, y0 + 30))
        sheet.paste(ghost_crop, (16 + FRAME_W, y0 + 30))
        sheet.paste(disc_crop, (24 + FRAME_W + BOX, y0 + 30))
        d.text((8, y0 + 10), "%s  pointer=(%.0f,%.0f)  ghostCropDiff=%s   discCropMeanRGB=%s dominant=%s diff=%s"
               % (name, px, py, gdiff, drgb, dominant, ddiff), fill=(255, 230, 120))
        d.rectangle([16 + FRAME_W, y0 + 30, 16 + FRAME_W + BOX - 1, y0 + 30 + BOX - 1], outline=(0, 255, 0))
        d.rectangle([24 + FRAME_W + BOX, y0 + 30, 24 + FRAME_W + 2 * BOX - 1, y0 + 30 + BOX - 1], outline=(255, 80, 80))
    sheet.save(a.out)
    print("sheet -> %s %s" % (a.out, sheet.size))


if __name__ == "__main__":
    main()
