# CR-V1: acceptance checker for the mechanical refusals in cr-v1-calib-tex_.py:
#   (1) ORIENTATION  -- ALWAYS emits exactly one line (incl. no-water and the success path), so that
#                       "no ORIENTATION line" can only mean "the tool did not get this far".
#                       Vertical / neither => NOT-JUDGED; horizontal => comparable to the frame.
#   (2) RIVER WINDOW -- thickness outside [0.1, 6.0] tiles => NOT-JUDGED, never a number.
# It imports the tool's own orientation_note() / river_thickness() (ONE implementation each).
# ASCII only; writes nothing.
import os, glob, sys
import numpy as np
import importlib.util
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")
ROOT = os.getcwd()
spec = importlib.util.spec_from_file_location("cal", os.path.join(ROOT, "tools", "probes", "cr-v1-calib-tex_.py"))
cal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cal)


def canvas(w=200, h=200):
    b = np.zeros((h, w, 3), dtype=np.uint8)
    b[:, :] = cal.C_GRASS
    return b


def main():
    ok = True

    real = None
    c = glob.glob(os.path.join(ROOT, "*", "cr-assets-png", "assets", "sc", "arena_training_tex_.png"))
    if len(c) == 1:
        real = np.asarray(Image.open(c[0]).convert("RGB")).astype(np.int16)

    print("A) orientation note is ALWAYS emitted (this is the item-B invariant)")
    hor = canvas(); hor[80:110, :] = cal.C_WATER
    ver = canvas(); ver[:, 80:110] = cal.C_WATER
    non = canvas()                                   # <- the bone_ case: no water at all
    blob = canvas(); blob[80:110, 80:110] = cal.C_WATER
    cases = [("no water (bone_ case)", non, "NOT APPLICABLE"),
             ("horizontal band", hor, "HORIZONTAL band"),
             ("vertical strip", ver, "VERTICAL strip"),
             ("blob => neither", blob, "neither")]
    if real is not None:
        cases.insert(0, ("REAL training tex_", real, "VERTICAL strip"))
    for tag, img, want in cases:
        _t, _g, water = cal.masks(img)
        note = cal.orientation_note(water, img.shape[1], img.shape[0])
        nonempty = bool(note) and len(note) > 20          # <= the invariant that was missing
        contains = (want in note)
        good = nonempty and contains
        ok &= good
        print("   %-22s non-empty=%-5s contains '%-15s' %s" % (tag, nonempty, want, "PASS" if good else "FAIL"))

    print("B) river-thickness window (via the tool's own river_thickness, ppt=10, nbw=200)")
    ppt, nbw = 10.0, 200
    for tag, rows, want_number in [("in range 3.0 tile", 30, True), ("too thick 8.0 tile", 80, False),
                                   ("too thin 0.0 tile", 0, False), ("edge 6.0 tile", 60, True),
                                   ("edge 6.1 tile", 61, False)]:
        img = canvas()
        if rows:
            img[0:rows, :] = cal.C_WATER
        _t, _g, water = cal.masks(img)
        t, wrows = cal.river_thickness(water, ppt, nbw)
        got = (t is not None)
        good = (got == want_number) and (wrows == rows)
        ok &= good
        print("   %-20s rows=%-4d => %-11s want_number=%-5s %s"
              % (tag, wrows, ("%.3f tile" % t) if got else "NOT-JUDGED", want_number, "PASS" if good else "FAIL"))

    print("C) the real training texture must NOT get a river number (vertical strip => 0 rows)")
    if real is not None:
        _t, _g, water = cal.masks(real)
        t, wrows = cal.river_thickness(water, 43.727, real.shape[1])
        good = (t is None)
        ok &= good
        print("   rows=%-4d => %-11s want=NOT-JUDGED  %s"
              % (wrows, ("%.3f tile" % t) if t is not None else "NOT-JUDGED", "PASS" if good else "FAIL"))

    print("ORIENT+RIVER GUARD RESULT: %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
