# -*- coding: utf-8 -*-
"""UI material pipeline (idempotent): land ONLY the referenced original-UI frames
into the Unity project, under `Sprites/Ui/<purpose>/<source atlas>/frame_NNN.png`.

WHY THIS FILE EXISTS
    Task T4 produced `策划/原版UI素材索引.md` = a frame-by-frame index of every
    original (A) UI atlas frame (frame number / size / what it looks like / what it
    is for).  Task T4c then lands *the frames that are actually going to be used*
    into the project.  The rule is "copy only the referenced ones" -- never the
    whole source directory (`assets/sc/` holds 21k+ frames, the UI atlases alone
    1342).

WHY THE LANDED FILE IS A CROP, NOT A RAW COPY (measured, not assumed)
    Every frame in these atlases is a SMALL element sitting on a HUGE fully
    transparent canvas (`ui_out` = 1663x2810).  Copying the raw frame into the
    project is wrong twice over, and both were measured before this script was
    changed:

      1. `TextureImporter.maxTextureSize = 2048` (this project's agreed UI setting,
         inherited from `Ui/loading_bg.png`) DOWNSCALES anything taller than 2048,
         so a raw `ui_out` frame lands at 2048/2810 = 72.9% of its pixels: frame 806
         came out 67x38 instead of 91x51, frame 69 (the gold title bar) 779x48
         instead of 1067x62.  A 1:1 material requirement cannot be met at 73%.
      2. In `spriteImportMode = Multiple` Unity's automatic slicer produces one
         sprite per CONNECTED alpha island, so a frame whose element is made of
         disconnected pieces yields several sprites and `Resources.Load<Sprite>`
         silently returns only one of them (frame 10 = left arc + right arc + top
         edge came out as two 1-piece sprites).

    Cropping each frame to its own non-transparent bounding box removes both
    problems at the root: the texture IS the element, so nothing is downscaled and
    the single auto-sliced sprite covers the whole element.  Nothing is resampled or
    recoloured -- the crop is a 1:1 region copy and every landed pixel is required
    to equal the corresponding original pixel (checked on every run, see
    `verified pixel-identical` in the report).

    The crop rectangle is not invented here: it is the `bbox_x/y/w/h` column of
    `.ai-tmp/screenshots/ui-index-manifest.tsv`, which `tools/probes/ui-index.ps1`
    measured with an alpha > 8 scan.  The same numbers are published per frame in
    the `图元尺寸(bbox)` column of `策划/原版UI素材索引.md`, so any landed file
    reverse-resolves to its original rectangle.

TWO FRAMES ARE DELIBERATELY NOT LANDED
    `ui_out` 10  (`ButtonWide` / 白色宽圆角框描边) and `ui_out` 76
    (`IconCrownBlackSmall` / 黑色皇冠（小）) are made of disconnected pieces, so the
    slicer returns 2 sprites for them.  Landing them would register a key that
    silently points at half an element; they stay out until someone decides their
    9-slice borders on purpose.  Use `PanelFrameWhiteInner`/`ButtonCapsule` and
    `IconCrownBlack` instead.  See ALSO_NOT_LANDED below.

PATHS
    source (read only) : <root>/原版资源/cr-assets-png/assets/sc/<dir>/*.png
    manifest           : <root>/.ai-tmp/screenshots/ui-index-manifest.tsv
    target             : <root>/client/Assets/Resources/Sprites/Ui/<purpose>/<dir>/frame_NNN.png

NAMING / WHY THE SOURCE DIR IS KEPT IN THE PATH
    `frame_NNN` where NNN = the frame index in the ORIGINAL source file name
    (`<dir>_sprite_<n>.png`), so any landed file reverse-resolves to its row in the
    index document.  The frame number is only unique *inside* one source directory,
    so the source directory name stays in the path -- `ui_out` 226 and
    `ui_battle_end_out` 226 are two different frames and must not collide.
    `Core/ResPaths.cs` mirrors exactly this layout: `UiFrame(purpose, srcDir, n)`.

WHERE THIS FILE LIVES / WHY IT IS NOT A THROWAWAY
    `tools/probes/` -- deleting it means the landing can no longer be reproduced or
    audited (which frames, cropped how, mapping to which key), so it is a judgement
    asset and is committed, next to T4's other pipeline scripts (`ui-index.ps1`
    builds the contact sheets, `build-ui-index.py` folds the notes into the index
    document).
    Verified by `tools/probes/check-ui-keys.ps1` (offline: registry <-> disk <->
    this file must agree, all three ways) and the offline sprite check
    (in-editor: every key must resolve to exactly one loaded Sprite whose rect is
    the frame's bbox, with import settings identical to `Ui/loading_bg.png`).

USAGE
    python tools/probes/copy-ui-assets.py --dry-run   # report only
    python tools/probes/copy-ui-assets.py             # land (idempotent)
"""
from __future__ import annotations

import os
import re
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc")
DST = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Ui")
MANIFEST = os.path.join(ROOT, ".ai-tmp", "screenshots", "ui-index-manifest.tsv")

# ── purpose dir -> [ (source dir, source frame, ResPaths key) ] ───────────────
SPRITES = {
    "Panels": [
        ("ui_out", 806, "PanelPaper"),
        ("ui_out", 807, "PanelPaperCornerTr"),
        ("ui_out", 808, "PanelPaperCornerTl"),
        ("ui_out", 811, "PanelPaperCornerBl"),
        ("ui_out", 812, "PanelPaperCornerBig"),
        ("ui_out", 802, "PanelFrameOutline"),
        ("ui_out", 532, "CardFrameGlowLegendary"),
        ("ui_out", 592, "PanelFrameGrey"),
        ("ui_out", 505, "PanelCornerBlueGold"),
        ("ui_out", 506, "PanelEdgeBlueGold"),
        ("ui_battle_end_out", 108, "PanelFrameDark"),
        ("ui_out", 196, "HudScoreNamePlate"),
        ("ui_out", 193, "HudTopRightPlate"),
        # -- AV2: 193 is a HOLLOW rounded plate (only its outline is opaque); the original
        #    `HUD_topRight` also holds a 1x1 unnamed child (frame 177) that supplies the
        #    plate's SOLID fill. Landed so the timer板 can be drawn like the original
        #    (索引 §3.4: "HUD_topRight(15 帧) 的无名子件 | 1005 | 177(1×1) / 193(212×124)"). --
        ("ui_out", 177, "HudTimerPlateFill"),
        # -- AS2: battle-end (ui_battle_end_out) frames that AR2 landed but nobody had
        #    registered yet. Frames 002-006 = the 9-slice border corner/edge set, 009 = the
        #    near-black plate, 236 = the win/lose text plate. See AR2-结算帧辨认.md sec 3.4. --
        ("ui_battle_end_out", 2, "BattleEndBorderCornerLT"),
        ("ui_battle_end_out", 3, "BattleEndBorderCornerRT"),
        ("ui_battle_end_out", 4, "BattleEndBorderEdgeH"),
        ("ui_battle_end_out", 5, "BattleEndBorderEdgeHThin"),
        ("ui_battle_end_out", 6, "BattleEndBorderEdgeV"),
        ("ui_battle_end_out", 9, "BattleEndPlateDarkSquare"),
        ("ui_battle_end_out", 236, "BattleEndTextPlate"),
    ],
    "Buttons": [
        ("ui_out", 2, "ButtonCapsule"),
        ("ui_out", 4, "ButtonGreenCornerTl"),
        ("ui_out", 5, "ButtonGreenCorner"),
        ("ui_out", 165, "ButtonBlueCorner"),
        ("ui_out", 166, "ButtonBlueCornerAlt"),
        ("ui_out", 300, "ButtonGold"),
        ("ui_out", 357, "ButtonOrange"),
        ("ui_out", 359, "ButtonOrangeAlt"),
        ("ui_out", 476, "ButtonWhite"),
        ("ui_out", 14, "ButtonDarkGrey"),
        ("ui_out", 15, "ButtonDarkGreyAlt"),
        ("ui_out", 551, "ButtonOrangeWide"),
        ("ui_out", 163, "HudPauseButtonPlate"),
    ],
    "Bars": [
        ("ui_out", 69, "TitleBarGold"),
        ("ui_out", 70, "TitleBarWood"),
        ("ui_out", 155, "ElixirBarTrack"),
        ("ui_out", 158, "ElixirBarFrame"),
        ("ui_out", 157, "ElixirBarFill"),
        ("ui_out", 156, "ElixirBarTrackAlt"),
        ("ui_out", 160, "ElixirBarTick"),
        ("ui_out", 161, "ElixirRequirementTrack"),
        ("ui_out", 162, "ElixirRequirementEnd"),
        ("ui_battle_end_out", 201, "GoldSquarePlate"),
        ("ui_battle_end_out", 222, "BarWhite"),
        # -- AD1/D75: loading-screen progress-bar fill. Referenced by CrUiStyle.LoadingBarFill
        #    (built as ResPaths.UiFrame(ResPaths.UiBarsDir, "loading_out", 15)). AB3 deleted it
        #    because check-ui-keys.ps1 only scanned ResPaths.cs keys -> false negative (D75). --
        ("loading_out", 15, "LoadingBarFill"),
    ],
    "Slots": [
        ("ui_out", 43, "SlotCard"),
        ("ui_out", 54, "ChatBubble"),
        ("ui_out", 531, "SlotCardPlain"),
        ("ui_out", 11, "SlotCorner"),
        ("ui_out", 200, "HudHandSlot"),
        ("ui_out", 201, "HudHandSlotDraft"),
        # -- AS2: battle-end reward strip. 212 = the slot plate shared by all seven
        #    `battleEnd_loot_item_*` exports; 218-221 = the chest's white highlight pieces
        #    (same export); 224 = the challenge scroll. See AR2-结算帧辨认.md sec 3.2. --
        ("ui_battle_end_out", 212, "BattleEndRewardSlotBg"),
        ("ui_battle_end_out", 218, "BattleEndChestGlowArc"),
        ("ui_battle_end_out", 219, "BattleEndChestGlowBar"),
        ("ui_battle_end_out", 220, "BattleEndChestGlowArcThin"),
        ("ui_battle_end_out", 221, "BattleEndChestGlowArcAlt"),
        ("ui_battle_end_out", 224, "BattleEndRewardQuestScroll"),
    ],
    "Icons": [
        ("ui_out", 99, "IconElixirDrop"),
        ("ui_out", 50, "IconCrownGold"),
        ("ui_out", 75, "IconCrownBlack"),
        ("ui_out", 187, "IconCrownBlueGem"),
        ("ui_out", 188, "IconCrownRedGem"),
        ("ui_out", 187, "HudStarPlayer"),
        ("ui_out", 188, "HudStarEnemy"),
        ("ui_out", 878, "IconCrownFivePoint"),
        ("ui_out", 883, "IconCrownBig"),
        ("ui_out", 526, "IconChestGoldOpen"),
        ("ui_out", 527, "IconChestWoodClosed"),
        ("ui_out", 555, "IconChestHalfOpen"),
        ("ui_out", 279, "IconSearch"),
        ("ui_out", 280, "IconAttackGear"),
        ("ui_out", 281, "IconTournamentCreate"),
        ("ui_out", 292, "IconQuestion"),
        ("ui_out", 226, "IconBattle"),
        ("ui_out", 519, "IconArrowUp"),
        ("ui_out", 521, "IconPlus"),
        ("ui_out", 563, "IconGear"),
        ("ui_out", 570, "IconGamepad"),
        ("ui_out", 572, "IconChat"),
        ("ui_out", 597, "IconGemBlue"),
        ("ui_battle_end_out", 109, "IconTrophy"),
        ("ui_battle_end_out", 195, "IconTrophyLaurel"),
        ("ui_battle_end_out", 213, "IconMedal"),
        ("ui_battle_end_out", 214, "IconGemGreen"),
        ("ui_battle_end_out", 215, "IconCoin"),
        ("ui_battle_end_out", 216, "IconCoinPile"),
        ("ui_battle_end_out", 223, "IconCastle"),
        ("ui_battle_end_out", 225, "IconShieldBlue"),
        ("ui_battle_end_out", 226, "IconBanner"),
        ("ui_battle_end_out", 205, "IconSpellBook"),
        # -- AS2: the original battle-end crown. Two 80-frame animations (blue 027-106 /
        #    red 115-194); only the first frame of each is landed (no per-frame animation
        #    wiring in this slice). See AR2-结算帧辨认.md sec 3.4. --
        ("ui_battle_end_out", 27, "CrownBlue"),
        ("ui_battle_end_out", 115, "CrownRed"),
        ("ui_out", 42, "HudClockIcon"),
        ("ui_out", 170, "HudPauseIconPlay"),
        ("ui_out", 171, "HudPauseIconPause"),
        # -- AB4: the last 8 landed-but-unreferenced frames (see 策划/战斗HUD素材索引.md
        #    sec 3.1 / 3.3 / 3.4 / 3.5 for the per-frame original `.sc` evidence) --
        ("ui_out", 159, "IconElixirBarLeft"),      # elixir_bar/elixirBarLeft + elixirRegen  (sec 3.1 / 3.4)
        ("ui_out", 164, "IconQuitCross"),          # quit_button cross icon                (sec 3.5)
        ("ui_out", 197, "HudStarPlayerAlt"),       # printScore_player star, 2nd state     (sec 3.3)
        ("ui_out", 198, "HudStarEnemyAlt"),        # printScore_enemy  star, 2nd state     (sec 3.3)
        # -- AD1/D75: boot-screen "CLASH ROYALE" logo. Referenced by CrUiStyle.LogoOfficial
        #    (ResPaths.UiFrame(ResPaths.UiIconsDir, "loading_out", 28)). Same false negative. --
        ("loading_out", 28, "LogoOfficial"),
    ],
}

# ── deliberately NOT landed (see the module docstring) ───────────────────────
ALSO_NOT_LANDED = [
    ("ui_out", 10, "ButtonWide", "自动切片切成 2 个 sprite（左弧+右弧+上边是断开的），"
                                 "Resources.Load 只会拿到其中一块；要用得先明确它的九宫格边框"),
    ("ui_out", 76, "IconCrownBlackSmall", "自动切片切成 2 个 sprite（皇冠与底座断开），同上；"
                                          "已有 IconCrownBlack（ui_out 75）覆盖同一用途"),
]

IDX_RE = re.compile(r"_sprite_(\d+)\.png$")


def read_manifest() -> dict[tuple[str, int], tuple[int, int, int, int]]:
    """{(dir, frame): (bbox_x, bbox_y, bbox_w, bbox_h)} -- top-left origin."""
    out = {}
    with open(MANIFEST, "r", encoding="utf-8") as fh:
        fh.readline()
        for line in fh:
            c = line.rstrip("\r\n").split("\t")
            if len(c) < 10:
                continue
            out[(c[0], int(c[1]))] = (int(c[5]), int(c[6]), int(c[7]), int(c[8]))
    return out


def source_index(src_dir: str) -> dict[int, str]:
    d = os.path.join(SRC, src_dir)
    if not os.path.isdir(d):
        return {}
    out = {}
    for name in os.listdir(d):
        m = IDX_RE.search(name)
        if m:
            out[int(m.group(1))] = os.path.join(d, name)
    return out


def main() -> int:
    dry = "--dry-run" in sys.argv
    if not os.path.isdir(SRC):
        print("source dir missing: " + SRC)
        return 2
    if not os.path.isfile(MANIFEST):
        print("manifest missing: " + MANIFEST + "  (run tools/probes/ui-index.ps1 first)")
        return 2

    bbox = read_manifest()
    cache: dict[str, dict[int, str]] = {}
    written = skipped = 0
    mismatched_pixels = 0
    reset_meta = 0
    rows = []
    bad = 0

    for purpose in sorted(SPRITES):
        for src_dir, frame, key in SPRITES[purpose]:
            if src_dir not in cache:
                cache[src_dir] = source_index(src_dir)
            src = cache[src_dir].get(frame)
            if src is None:
                print("!! source frame missing: %s/%s_sprite_%d.png" % (SRC, src_dir, frame))
                bad += 1
                continue
            box = bbox.get((src_dir, frame))
            if box is None:
                print("!! no manifest row for %s frame %d" % (src_dir, frame))
                bad += 1
                continue
            if box[2] <= 0 or box[3] <= 0:
                print("!! frame is fully transparent: %s %d" % (src_dir, frame))
                bad += 1
                continue

            with Image.open(src) as im:
                im = im.convert("RGBA")
                crop = im.crop((box[0], box[1], box[0] + box[2], box[1] + box[3]))

            rel = "%s/%s/frame_%03d.png" % (purpose, src_dir, frame)
            dst = os.path.join(DST, rel.replace("/", os.sep))
            if os.path.exists(dst):
                try:
                    with Image.open(dst) as old:
                        same = old.convert("RGBA").tobytes() == crop.tobytes()
                except Exception:
                    same = False
                if same:
                    skipped += 1
                    rows.append((purpose, src_dir, frame, key, rel, box))
                    continue
            if not dry:
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                crop.save(dst, format="PNG")
                # The .meta pins the sprite rects Unity computed for the PREVIOUS
                # texture. Rewriting the PNG changes its size, so those rects fall
                # outside the new bounds and Unity resolves them to ZERO sprites
                # (measured: after re-cropping without resetting the meta, all 65
                # keys came back `Resources.Load<Sprite> == null`, while the
                # importer itself still existed and its settings still matched).
                # Dropping the meta forces a fresh import + fresh automatic slicing.
                # GUIDs change, which is harmless here: nothing references these
                # sprites by GUID yet (they are reached by path through ResPaths).
                stale = dst + ".meta"
                if os.path.exists(stale):
                    os.remove(stale)
                    reset_meta += 1
                # read back and prove the landed pixels are the original pixels
                with Image.open(dst) as chk:
                    if chk.convert("RGBA").tobytes() != crop.tobytes():
                        mismatched_pixels += 1
                        print("!! pixel mismatch after write: " + rel)
                with Image.open(dst) as fin:
                    if fin.size != (box[2], box[3]):
                        print("!! size mismatch after write: " + rel)
            written += 1
            rows.append((purpose, src_dir, frame, key, rel, box))

    if bad:
        print("!! %d frame(s) could not be resolved - nothing else trusted" % bad)
        return 3
    if mismatched_pixels:
        print("!! %d landed file(s) differ from the original pixels" % mismatched_pixels)
        return 4

    print("purpose dirs : %d" % len(SPRITES))
    print("referenced   : %d frames (all cropped to their manifest bbox, 1:1, no resample)" % len(rows))
    print("this run     : written=%d  already identical=%d  stale .meta dropped=%d%s"
          % (written, skipped, reset_meta, "  (dry-run, nothing written)" if dry else ""))
    print("pixel check  : every landed file re-read and compared against the original crop; "
          "mismatches=%d" % mismatched_pixels)
    print()
    print("| purpose | source dir | src frame | ResPaths key | crop x,y,w,h | project path (Assets/Resources/Sprites/Ui/...) |")
    print("|---|---|---|---|---|---|")
    for purpose, src_dir, frame, key, rel, box in rows:
        print("| %s | %s | %d | %s | %d,%d,%d,%d | %s |"
              % (purpose, src_dir, frame, key, box[0], box[1], box[2], box[3], rel))
    print()
    print("NOT landed (see ALSO_NOT_LANDED in this file):")
    for src_dir, frame, key, why in ALSO_NOT_LANDED:
        print("  - %s %d / %s : %s" % (src_dir, frame, key, why))
    return 0


if __name__ == "__main__":
    sys.exit(main())
