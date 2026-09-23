// CR-U5 evidence probe (eval_file; ASCII only; Play mode required; always returns).
// One file, many modes, driven by .ai-tmp/test/U5-arg.txt (mode + optional params).
//
// A6 fixes applied by CR-U5R (2026-09-23, evidence-only, no product change):
//   * `sample`: header now matches the rows; the never-assigned `moved`/`spriteChanged` counters are removed.
//   * `units` : TSV header now matches the 8 columns actually written, and the drift column is named
//     `driftAnchorPx` to carry its semantics (rect.x + pivot.x == the canvas anchor every frame of a
//     directory must share).
//   * VOID DATA ALERT: the drift cell inside the ALREADY-WRITTEN `CR-U5-units-bind-*.tsv` and the
//     `pivotPix`/`maxDevFromFrame0` lines in `CR-U5-pdbg.out.txt` come from the FIRST formula, which
//     was wrong (it treated `Sprite.pivot` as a 0..1 ratio => 3479..25593 px of nonsense). The valid
//     readings are `CR-U5-drift.tsv` / `CR-U5-disp.out.txt` / the second `pdbg` run (all 0.000 px).
//     Those old files belong to the CR-U5 slice and are frozen, so they are NOT rewritten here.
// Why a probe: every criterion CR-U5 has to close is a RUNTIME reading - either a log line the
// product itself writes (L3) or the live component state it leaves behind (sprite/frames/pos).
// Nothing here changes product code; it only drives the production entry points and reads them back.
//
// Modes:
//   units:<n0>,<n1>  acquire UnitView for chr_*_out[n0..n1) under a grid root, log per-dir frame
//                    coverage + write CR-U5-units-bind.tsv (pos/frames/per-frame anchor drift)
//   sample           read _pos / renderer.sprite back for every acquired view -> CR-U5-units-sample.tsv
//   disp             per-dir per-frame anchor drift px (max/rms) over the LOADED runtime frames
//   fx:<kind>        emit the production battle event (kind 2=death/Hit 3=tower/Blast 0=playcard/Arrow)
//                    then read the live EffectsView nodes (sprite name + world pos)
//   fxread           read the live EffectsView nodes only
//   click:<name>     click a Button by GameObject name (optionally inside a panel: click:<Panel>/<Btn>)
//   emit:<event>     emit a production event: returnmain | aibattle | openpause | opensettings
//   state            one-line JSON-ish state dump (station / panels / drag / hand / camera / device / dt)
//   dragstate        _dragging + _handIds + ghost + status only (cheap, for guard tests)
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string ArgFile = Root + @"\.ai-tmp\test\U5-arg.txt";
const string OutFile = Root + @"\.ai-tmp\test\U5-out.txt";
const string TsvDir = Root + @"\.ai-tmp\test\";
const string UnitsRootName = "U5Units";
var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
       | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

System.Action<string> N = (t) => sb.Append(t).Append('\n');
string arg = "";
try { if (System.IO.File.Exists(ArgFile)) arg = System.IO.File.ReadAllText(ArgFile).Trim(); } catch { }
N("=== CR-U5 probe arg='" + arg + "' at " + System.DateTime.Now.ToString("HH:mm:ss.fff") + " ===");
if (!UnityEngine.Application.isPlaying) { N("PLAY=0"); try { System.IO.File.AppendAllText(OutFile, sb.ToString()); } catch { } return sb.ToString(); }

var uvT = typeof(CR.View.UnitView);
var animTblT = typeof(CR.View.UnitAnimTable);
var entryT = animTblT.GetNestedType("Entry");
var clipT = animTblT.GetNestedType("Clip");   // Clip is a SIBLING nested type of Entry, not a child
var tryGetM = animTblT.GetMethod("TryGet", BF);
var fAnim = uvT.GetField("_anim", BF);
var fClip = uvT.GetField("_clip", BF);
var fPos = uvT.GetField("_pos", BF);
var fDone = uvT.GetField("_animDone", BF);
var fRend = uvT.GetField("_renderer", BF);
var fFr = uvT.GetField("_frames", BF);
var fDir = uvT.GetField("_spriteDir", BF);
var mClip = uvT.GetMethod("ClipIndices", BF);
var mRef = uvT.GetMethod("RefreshSprite", BF);

System.Func<string, object[]> Tiers = (dir) =>
{
    var a = new object[] { dir, null };
    var ok = (bool)tryGetM.Invoke(null, a);
    if (!ok) return null;
    var e = a[1];
    return new object[] { entryT.GetField("Tiers").GetValue(e),
                          entryT.GetField("ScFps").GetValue(e),
                          entryT.GetField("HitSpeedMs").GetValue(e) };
};

System.Action<System.Text.StringBuilder, string> AppendTier = (b, dir) =>
{
    var ti = Tiers(dir);
    if (ti == null) { b.Append("TBL=not-listed"); return; }
    var tiers = ti[0] as System.Array;
    string[] nm = { "idle", "walk", "attack", "die" };
    for (int i = 0; i < nm.Length; i++)
    {
        bool known = false; int cnt = 0;
        if (tiers != null && i < tiers.Length)
        {
            var c = tiers.GetValue(i);
            var ct = c == null ? clipT : c.GetType();
            known = (bool)ct.GetField("Known").GetValue(c);
            cnt = (int)ct.GetField("Count").GetValue(c);
        }
        b.Append(' ').Append(nm[i]).Append('=').Append(known ? cnt.ToString() + "f" : "Unknown");
    }
    b.Append(" ScFps=").Append(ti[1]).Append(" hitMs=").Append(ti[2]);
};

// per-frame anchor drift: deviation of each frame's pivot-in-texture from the frame-set mean,
// expressed in canvas pixels. 0.000 == every frame shares ONE texture anchor (no hop on frame swap).
System.Func<UnityEngine.Sprite[], string> Drift = (frames) =>
{
    if (frames == null || frames.Length == 0) return "noFrames";
    // ⚠️ MEASURED SEMANTICS (pdbg, 4 dirs x 6 frames): the value `Sprite.pivot` returns here is the
    // pivot in **rect-local pixels**, not a 0..1 normalized ratio (e.g. chr_knight_out f0:
    // rect=(27,33,101,86), pivot=(66.5,57.5) -> 27+66.5 = 93.5 = the anchor printed by the
    // product's own `统一锚点：…锚点(纹理像素)=(93.5,90.5)` log line). So the canvas pixel a frame's
    // pivot maps to is `rect.position + pivot`, and the criterion is: that pixel is the SAME for
    // every frame of the directory (=> swapping frames cannot move the character).
    double sx = 0, sy = 0; int n = 0; int distinct = 0;
    var seen = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < frames.Length; i++)
    {
        var s = frames[i]; if (s == null || s.texture == null) continue;
        var r = s.rect;
        var px = r.x + s.pivot.x;
        var py = r.y + s.pivot.y;
        sx += px; sy += py; n++;
        if (seen.Add(px.ToString("F3") + "/" + py.ToString("F3"))) distinct++;
    }
    if (n == 0) return "noFrames";
    double mx = sx / n, my = sy / n, max = 0, ss = 0;
    for (int i = 0; i < frames.Length; i++)
    {
        var s = frames[i]; if (s == null || s.texture == null) continue;
        var r = s.rect;
        var px = r.x + s.pivot.x;
        var py = r.y + s.pivot.y;
        double d = System.Math.Sqrt((px - mx) * (px - mx) + (py - my) * (py - my));
        if (d > max) max = d;
        ss += d * d;
    }
    return "max=" + max.ToString("F3") + "px rms=" + System.Math.Sqrt(ss / n).ToString("F3")
         + "px frames=" + n + " distinctAnchors=" + distinct;
};

System.Func<UnityEngine.GameObject> GetUnitsRoot = () =>
{
    var go = UnityEngine.GameObject.Find(UnitsRootName);
    if (go == null) { go = new UnityEngine.GameObject(UnitsRootName); }
    return go;
};

System.Func<float> DtMs = () => UnityEngine.Time.deltaTime * 1000f;

try
{
if (arg.StartsWith("units"))
{
    var pp = arg.Contains(":") ? arg.Split(':')[1].Split(',') : new string[] { "0", "8" };
    int n0 = int.Parse(pp[0]), n1 = int.Parse(pp[1]);
    var unitsPath = UnityEngine.Application.dataPath + "/Resources/Sprites/Units";
    var all = System.IO.Directory.GetDirectories(unitsPath, "chr_*_out");
    System.Array.Sort(all);
    // filter to directories that really hold PNG frames (== "landed")
    var landed = new System.Collections.Generic.List<string>();
    for (int i = 0; i < all.Length; i++)
        if (System.IO.Directory.GetFiles(all[i], "*.png").Length > 0) landed.Add(System.IO.Path.GetFileName(all[i]));
    N("UNITS all=" + all.Length + " landed=" + landed.Count + " slice=[" + n0 + "," + n1 + ")");
    var rootGo = GetUnitsRoot();
    // A6 fix (CR-U5R): header used to declare 14 columns (idle/walk/attack/die/ScFps/hitMs each their
    // own) while the rows wrote 8 - the tier numbers live INSIDE the `tbl` field. Header now = rows,
    // and the last column is named for its semantics: the per-directory anchor must be ONE canvas pixel.
    var tsv = new System.Text.StringBuilder("dir\tframes\tpng\ttbl\tclipWalkLen\tpos0\tsprite0\tdriftAnchorPx\n");
    int done = 0;
    for (int i = n0; i < n1 && i < landed.Count; i++)
    {
        var dir = landed[i];
        var v = CR.View.UnitView.Acquire(rootGo.transform, dir, false);
        v.transform.position = new UnityEngine.Vector3(-9f + (i % 8) * 2.6f, 6f - (i / 8) * 3.4f, 0f);
        v.transform.localScale = new UnityEngine.Vector3(1.6f, 1.6f, 1f);
        var frames = fFr.GetValue(v) as UnityEngine.Sprite[];
        int flen = frames == null ? 0 : frames.Length;
        int png = System.IO.Directory.GetFiles(unitsPath + "/" + dir, "*.png").Length;
        // switch to the walk tier through the same field/method the production Apply() uses
        int clipLen = -1;
        try
        {
            fAnim.SetValue(v, 1);
            var clip = mClip.Invoke(v, new object[] { dir, 1 }) as int[];
            fClip.SetValue(v, clip);
            fDone.SetValue(v, false);
            fPos.SetValue(v, 0);
            mRef.Invoke(v, null);
            clipLen = clip == null ? 0 : clip.Length;
        }
        catch (System.Exception ex) { N("units EX " + dir + " " + ex.Message); }
        var rend = fRend.GetValue(v) as UnityEngine.SpriteRenderer;
        var tb = new System.Text.StringBuilder();
        AppendTier(tb, dir);
        var tsvTb = new System.Text.StringBuilder();
        tsvTb.Append(dir).Append('\t').Append(flen).Append('\t').Append(png).Append('\t').Append(tb.ToString().Replace(" ", "|"))
             .Append('\t').Append(clipLen).Append('\t').Append(fPos.GetValue(v))
             .Append('\t').Append(rend == null || rend.sprite == null ? "NULL" : rend.sprite.name)
             .Append('\t').Append(Drift(frames));
        tsv.Append(tsvTb.ToString()).Append('\n');
        N("U " + dir + " frames=" + flen + " png=" + png + tb.ToString()
          + " clipWalk=" + clipLen + " pos0=" + fPos.GetValue(v)
          + " sprite0=" + (rend == null || rend.sprite == null ? "NULL" : rend.sprite.name));
        done++;
    }
    try { System.IO.File.WriteAllText(TsvDir + "CR-U5-units-bind-" + n0 + ".tsv", tsv.ToString()); } catch { }
    N("units done=" + done);
}
else if (arg == "sample")
{
    var rootGo = UnityEngine.GameObject.Find(UnitsRootName);
    // A6 fix (CR-U5R, 2026-09-23): the old header declared 7 columns while the rows wrote 4, and
    // `moved`/`spriteChanged` were declared and never assigned => the summary line printed
    // "moved=0 spriteChanged=0" which reads as "nothing changed" but actually meant "never computed"
    // (a criterion that says the opposite of the truth). Header now matches the rows; the bogus
    // counters are gone. The pos/sprite values ARE the reading: compare them across samples.
    var tsv = new System.Text.StringBuilder("dir\tpos\tsprite\tclipLen\n");
    int n = 0;
    if (rootGo != null)
    {
        var views = rootGo.GetComponentsInChildren<CR.View.UnitView>(false);
        for (int i = 0; i < views.Length; i++)
        {
            var v = views[i];
            var dir = (string)fDir.GetValue(v);
            var rend = fRend.GetValue(v) as UnityEngine.SpriteRenderer;
            var clip = fClip.GetValue(v) as int[];
            N("S " + dir + " pos=" + fPos.GetValue(v) + " sprite=" + (rend == null || rend.sprite == null ? "NULL" : rend.sprite.name)
              + " clipLen=" + (clip == null ? -1 : clip.Length) + " animDone=" + fDone.GetValue(v));
            tsv.Append(dir).Append('\t').Append(fPos.GetValue(v)).Append('\t')
               .Append(rend == null || rend.sprite == null ? "NULL" : rend.sprite.name).Append('\t')
               .Append(clip == null ? -1 : clip.Length).Append('\n');
            n++;
        }
    }
    try { System.IO.File.WriteAllText(TsvDir + "CR-U5-units-sample.tsv", tsv.ToString()); } catch { }
    N("sample views=" + n + " (pos/sprite are RAW readings - the old moved/spriteChanged counters were"
      + " never assigned and are REMOVED (A6); compare pos/sprite across samples to see progress)");
}
else if (arg == "disp")
{
    var rootGo = UnityEngine.GameObject.Find(UnitsRootName);
    var tsv = new System.Text.StringBuilder("dir\tframes\tdrift\n");
    int n = 0;
    if (rootGo != null)
    {
        var views = rootGo.GetComponentsInChildren<CR.View.UnitView>(false);
        for (int i = 0; i < views.Length; i++)
        {
            var v = views[i];
            var dir = (string)fDir.GetValue(v);
            var frames = fFr.GetValue(v) as UnityEngine.Sprite[];
            var d = Drift(frames);
            N("D " + dir + " frames=" + (frames == null ? 0 : frames.Length) + " " + d);
            tsv.Append(dir).Append('\t').Append(frames == null ? 0 : frames.Length).Append('\t').Append(d).Append('\n');
            n++;
        }
    }
    try { System.IO.File.WriteAllText(TsvDir + "CR-U5-drift.tsv", tsv.ToString()); } catch { }
    N("disp views=" + n);
}
else if (arg.StartsWith("fx") || arg == "fxread")
{
    var bvr = CR.View.BattleViewRoot.Instance;
    N("bvr=" + (bvr != null));
    if (bvr != null)
    {
        var bvrT = bvr.GetType();
        var fx = bvrT.GetField("_effects", BF).GetValue(bvr) as CR.View.EffectsView;
        if (fx == null) N("fx layer NULL");
        else
        {
            var liveF = typeof(CR.View.EffectsView).GetField("_live", BF);
            var before = ((System.Collections.ICollection)liveF.GetValue(fx)).Count;
            if (arg.StartsWith("fx:"))
            {
                int kind = int.Parse(arg.Split(':')[1]);
                int cardId = 0; string pkey = ""; int pspeed = 0;
                var cardsD = bvrT.GetField("_cards", BF).GetValue(bvr) as System.Collections.IDictionary;
                if (cardsD != null && kind == 0)
                {
                    foreach (System.Collections.DictionaryEntry de in cardsD)
                    {
                        var c = de.Value; var pk = (string)c.GetType().GetField("projectile_key").GetValue(c);
                        if (!string.IsNullOrEmpty(pk)) { cardId = (int)de.Key; pkey = pk; pspeed = (int)c.GetType().GetField("proj_speed").GetValue(c); break; }
                    }
                }
                var ev = new CR.Def.BattleEvent();
                ev.kind = kind; ev.card_id = cardId; ev.team = 1;
                ev.entity_id = 0; ev.x_milli = 18000; ev.y_milli = 12000; ev.text = "";
                var notify = new CR.Def.BattleEventNotify();
                notify.events = new CR.Def.BattleEvent[] { ev };
                // production path: same bus + same handler the server push goes through
                CloverEngine.Game.Event.Emit(CR.Events.Battle.Events, notify);
                N("fx emitted kind=" + kind + " card=" + cardId + " pkey=" + pkey + " pspeed=" + pspeed + " liveBefore=" + before);
            }
            // EffectsView.Update runs on the next frame -> read now + again after a short spin
            var liveList = liveF.GetValue(fx) as System.Collections.IList;
            N("liveCount=" + liveList.Count);
            for (int i = 0; i < liveList.Count; i++)
            {
                var node = liveList[i];
                var nt = node.GetType();
                var go = nt.GetField("Go").GetValue(node) as UnityEngine.GameObject;
                var sr = nt.GetField("Renderer").GetValue(node) as UnityEngine.SpriteRenderer;
                var frames = nt.GetField("Frames").GetValue(node) as UnityEngine.Sprite[];
                var start = (int)nt.GetField("Start").GetValue(node);
                var cnt = (int)nt.GetField("Count").GetValue(node);
                var fi = (int)nt.GetField("FrameIndex").GetValue(node);
                var dur = (float)nt.GetField("Duration").GetValue(node);
                N("FX node=" + i + " sprite=" + (sr == null || sr.sprite == null ? "NULL" : sr.sprite.name)
                  + " spriteNull=" + (sr == null || sr.sprite == null)
                  + " active=" + (go != null && go.activeInHierarchy)
                  + " world=" + (go == null ? "-" : go.transform.position.x.ToString("F2") + "," + go.transform.position.y.ToString("F2"))
                  + " framesLen=" + (frames == null ? -1 : frames.Length) + " start=" + start
                  + " count=" + cnt + " frameIdx=" + fi + " dur=" + dur.ToString("F2")
                  + " sortOrder=" + (sr == null ? -1 : sr.sortingOrder));
            }
        }
    }
}
else if (arg.StartsWith("click"))
{
    var spec = arg.Split(':')[1];              // "Name" or "PanelType/Name"
    var parts = spec.Split('/');
    UnityEngine.GameObject host = null;
    if (parts.Length == 2)
    {
        var ui = CloverEngine.Game.UI;
        var pt = typeof(CR.UI.Panels.HudPanel).Assembly.GetType("CR.UI.Panels." + parts[0]);
        var m = ui.GetType().GetMethod("Get", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var panel = pt == null ? null : m.MakeGenericMethod(pt).Invoke(ui, null) as UnityEngine.Component;
        host = panel == null ? null : panel.gameObject;
        N("click host=" + parts[0] + " found=" + (host != null));
    }
    else host = UnityEngine.GameObject.Find(parts[0]);
    if (host == null) { N("click NOTFOUND " + spec); }
    else
    {
        var btns = host.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        int hit = 0;
        for (int i = 0; i < btns.Length; i++)
        {
            if (btns[i].gameObject.name != parts[parts.Length - 1]) continue;
            N("click '" + btns[i].gameObject.name + "' interactable=" + btns[i].interactable
              + " activeInHierarchy=" + btns[i].gameObject.activeInHierarchy);
            if (btns[i].interactable) { btns[i].onClick.Invoke(); hit++; }
        }
        N("click invoked=" + hit);
    }
}
else if (arg.StartsWith("emit"))
{
    var what = arg.Contains(":") ? arg.Split(':')[1] : "";
    if (what == "returnmain") { CloverEngine.Game.Event.Emit(CR.Events.Battle.ReturnToMainMenuRequest); N("emitted ReturnToMainMenuRequest"); }
    else if (what == "aibattle") { CloverEngine.Game.Event.Emit(CR.Events.Battle.AiBattleRequest); N("emitted AiBattleRequest"); }
    else if (what == "openpause") { CloverEngine.Game.Event.Emit(CR.Events.Flow.StationEnterRequest, CR.Stations.Pause); N("emitted StationEnterRequest(Pause)"); }
    else if (what == "stationbattle") { CloverEngine.Game.Event.Emit(CR.Events.Flow.StationEnterRequest, CR.Stations.Battle); N("emitted StationEnterRequest(Battle)"); }
    else N("emit unknown '" + what + "'");
}
else if (arg.StartsWith("pdbg"))
{
    // Raw dump of the anchor geometry for one dir: what the loaded runtime frames really carry.
    var dir = arg.Split(':')[1];
    var unitsPath = UnityEngine.Application.dataPath + "/Resources/Sprites/Units";
    var rootGo2 = GetUnitsRoot();
    var v = CR.View.UnitView.Acquire(rootGo2.transform, dir, false);
    var frames = fFr.GetValue(v) as UnityEngine.Sprite[];
    N("pdbg dir=" + dir + " frames=" + (frames == null ? -1 : frames.Length));
    int show = frames == null ? 0 : System.Math.Min(6, frames.Length);
    for (int i = 0; i < show; i++)
    {
        var s = frames[i]; if (s == null) { N("  [" + i + "] NULL"); continue; }
        N("  [" + i + "] name=" + s.name + " tex=" + s.texture.width + "x" + s.texture.height
          + " rect=(" + s.rect.x.ToString("F1") + "," + s.rect.y.ToString("F1") + "," + s.rect.width.ToString("F1") + "," + s.rect.height.ToString("F1") + ")"
          + " pivot=(" + s.pivot.x.ToString("F4") + "," + s.pivot.y.ToString("F4") + ")"
          + " anchorPx=(" + (s.rect.x + s.pivot.x).ToString("F1") + "," + (s.rect.y + s.pivot.y).ToString("F1") + ")"
          + " ppu=" + s.pixelsPerUnit.ToString("F0"));
    }
    // deviation of pivotPix from the FIRST frame's pivotPix (the anchor the node actually uses)
    double m = 0; int np = 0;
    if (frames != null && frames.Length > 0)
    {
        var r0 = frames[0].rect; var a0x = r0.x + frames[0].pivot.x; var a0y = r0.y + frames[0].pivot.y;
        for (int i = 0; i < frames.Length; i++)
        {
            var s = frames[i]; if (s == null) continue;
            var r = s.rect; var px = r.x + s.pivot.x; var py = r.y + s.pivot.y;
            double d = System.Math.Sqrt((px - a0x) * (px - a0x) + (py - a0y) * (py - a0y));
            if (d > m) m = d; np++;
        }
    }
    N("pdbg maxDevFromFrame0=" + m.ToString("F3") + "px over " + np + " frames");
    var rend = fRend.GetValue(v) as UnityEngine.SpriteRenderer;
    N("pdbg boundCenterMinusPos=" + (rend == null ? "NO_RENDERER"
        : (rend.bounds.center.x - v.transform.position.x).ToString("F4") + "," + (rend.bounds.center.y - v.transform.position.y).ToString("F4")));
}
else if (arg.StartsWith("presshand"))
{
    // Queue a REAL left-button press at the centre of the first non-empty hand card (device level,
    // i.e. the same path a user's mouse takes: InputSystem -> engine InputManager -> HudPanel.PollDrag).
    var ui = CloverEngine.Game.UI;
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    if (hud == null) N("presshand hud=NULL");
    else
    {
        var hudT = typeof(CR.UI.Panels.HudPanel);
        var ids = (int[])hudT.GetField("_handIds", BF).GetValue(hud);
        var cards = (UnityEngine.UI.Image[])hudT.GetField("_handCards", BF).GetValue(hud);
        int slot = -1;
        if (arg.Contains(":")) { slot = int.Parse(arg.Split(':')[1]); }
        else for (int i = 0; i < ids.Length; i++) if (ids[i] != 0) { slot = i; break; }
        if (slot < 0 || slot >= ids.Length) N("presshand bad slot");
        else
        {
            var rt = cards[slot].rectTransform;
            var cc = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            var mouse = UnityEngine.InputSystem.Mouse.current;
            N("presshand slot=" + slot + " card=" + ids[slot] + " screen=(" + cc.x.ToString("0") + "," + cc.y.ToString("0")
              + ") mouseDev=" + (mouse == null ? "NULL" : mouse.name));
            if (mouse != null)
            {
                var st = new UnityEngine.InputSystem.LowLevel.MouseState();
                st.position = new UnityEngine.Vector2(cc.x, cc.y);
                st.delta = UnityEngine.Vector2.zero;
                st.buttons = 1;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, st);
                N("presshand queued buttons=1");
            }
        }
    }
}
else if (arg.StartsWith("releasehand") || arg.StartsWith("releaseat") || arg.StartsWith("moveat"))
{
    var mouse = UnityEngine.InputSystem.Mouse.current;
    float rx = 540f, ry = 900f;
    if (arg.Contains(":")) { var p = arg.Split(':')[1].Split(','); rx = float.Parse(p[0]); ry = float.Parse(p[1]); }
    if (mouse != null)
    {
        var st = new UnityEngine.InputSystem.LowLevel.MouseState();
        st.position = new UnityEngine.Vector2(rx, ry);
        st.delta = UnityEngine.Vector2.zero;
        st.buttons = arg.StartsWith("moveat") ? (ushort)1 : (ushort)0;
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, st);
        N(arg + " queued pos=(" + rx.ToString("0") + "," + ry.ToString("0") + ") buttons=" + st.buttons);
    }
}
else if (arg.StartsWith("setempty"))
{
    // Construct the "empty hand slot" context the guard tests need (probe-only mutation of the view
    // state; the production guard branch is what we then observe).
    var ui = CloverEngine.Game.UI;
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    if (hud == null) N("setempty hud=NULL");
    else
    {
        var what = arg.Split(':')[1]; // "all" or index
        var hudT = typeof(CR.UI.Panels.HudPanel);
        var ids = (int[])hudT.GetField("_handIds", BF).GetValue(hud);
        var cards = (UnityEngine.UI.Image[])hudT.GetField("_handCards", BF).GetValue(hud);
        if (what == "all") { for (int i = 0; i < ids.Length; i++) ids[i] = 0; }
        else { int k = int.Parse(what); if (k >= 0 && k < ids.Length) ids[k] = 0; }
        N("setempty " + what + " -> hand=[" + string.Join(",", System.Array.ConvertAll(ids, x => x.ToString())) + "]");
        // remember a screen point for the emptied slot (the card Image rect still exists)
        int t = what == "all" ? 0 : int.Parse(what);
        if (t >= 0 && t < cards.Length && cards[t] != null)
        {
            var rt = cards[t].rectTransform;
            var cc = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            N("setempty slot" + t + " screen=(" + cc.x.ToString("0") + "," + cc.y.ToString("0") + ")");
        }
    }
}
else if (arg.StartsWith("emptytest"))
{
    // G4/G5: zero the hand slot(s) AND queue the press in the SAME call, so the next frame's
    // PollDrag observes the empty hand before the 10 Hz snapshot can refill it.
    var ui = CloverEngine.Game.UI;
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    if (hud == null) N("emptytest hud=NULL");
    else
    {
        var what = arg.Split(':')[1];
        var hudT = typeof(CR.UI.Panels.HudPanel);
        var ids = (int[])hudT.GetField("_handIds", BF).GetValue(hud);
        var cards = (UnityEngine.UI.Image[])hudT.GetField("_handCards", BF).GetValue(hud);
        int t = 0;
        if (what == "all") { for (int i = 0; i < ids.Length; i++) ids[i] = 0; t = 0; }
        else { t = int.Parse(what); ids[t] = 0; }
        var rt = cards[t].rectTransform;
        var cc = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            var st = new UnityEngine.InputSystem.LowLevel.MouseState();
            st.position = new UnityEngine.Vector2(cc.x, cc.y); st.delta = UnityEngine.Vector2.zero; st.buttons = 1;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, st);
        }
        N("emptytest " + what + " hand=[" + string.Join(",", System.Array.ConvertAll(ids, x => x.ToString()))
          + "] pressScreen=(" + cc.x.ToString("0") + "," + cc.y.ToString("0") + ") queued buttons=1");
    }
}
else if (arg == "camoff")
{
    // G7 "camera missing": drive the production BeginDrag/EndDrag with Camera.main disabled.
    var ui = CloverEngine.Game.UI;
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    var cam = UnityEngine.Camera.main;
    N("camoff cam=" + (cam == null ? "NULL" : cam.name + " enabled=" + cam.enabled));
    if (hud == null) N("camoff hud=NULL");
    else
    {
        var hudT = typeof(CR.UI.Panels.HudPanel);
        var ids = (int[])hudT.GetField("_handIds", BF).GetValue(hud);
        int slot = -1; for (int i = 0; i < ids.Length; i++) if (ids[i] != 0) { slot = i; break; }
        var saved = cam == null ? false : cam.enabled;
        if (cam != null) cam.enabled = false;
        N("camoff Camera.main now=" + (UnityEngine.Camera.main == null ? "NULL" : "still-set"));
        if (slot >= 0)
        {
            var sc = new UnityEngine.Vector3(540f, 900f, 0f);
            hudT.GetMethod("BeginDrag", BF).Invoke(hud, new object[] { ids[slot], slot, sc });
            hudT.GetMethod("EndDrag", BF).Invoke(hud, new object[] { sc });
            var st = (UnityEngine.UI.Text)hudT.GetField("_statusText", BF).GetValue(hud);
            N("camoff status='" + (st == null ? "?" : st.text) + "'");
        }
        if (cam != null) cam.enabled = saved;
        N("camoff restored cam.enabled=" + (cam == null ? "-" : cam.enabled.ToString()));
    }
}
else if (arg == "setdrag")
{
    // G8 "button edge lost": put the panel into the dragging state WITHOUT a live button, exactly
    // the situation the hardening branch is for - then let PollDrag run its next frame.
    var ui = CloverEngine.Game.UI;
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    if (hud == null) N("setdrag hud=NULL");
    else
    {
        var hudT = typeof(CR.UI.Panels.HudPanel);
        var ids = (int[])hudT.GetField("_handIds", BF).GetValue(hud);
        int slot = -1; for (int i = 0; i < ids.Length; i++) if (ids[i] != 0) { slot = i; break; }
        hudT.GetField("_dragCardId", BF).SetValue(hud, ids[slot]);
        hudT.GetField("_dragging", BF).SetValue(hud, true);
        N("setdrag armed dragging=True card=" + ids[slot] + " (no live mouse button)");
    }
}
else if (arg == "loginform"){
    // Fill the login form the way the proven CR-T3 probe did (fields exist on LoginPanel).
    var ui = CloverEngine.Game.UI;
    N("ui=" + (ui != null));
    if (ui != null)
    {
        if (!ui.IsOpen<CR.UI.Panels.LoginPanel>() && !ui.IsOpen<CR.UI.Panels.MainMenuPanel>() && !ui.IsOpen<CR.UI.Panels.NicknamePanel>())
            ui.Open<CR.UI.Panels.LoginPanel>(null);
        var lp = ui.Get<CR.UI.Panels.LoginPanel>();
        N("loginPanel=" + (lp != null));
        if (lp != null)
        {
            var t = typeof(CR.UI.Panels.LoginPanel);
            var fa = t.GetField("_account", BF); var fp = t.GetField("_password", BF);
            N("fields _account=" + (fa != null) + " _password=" + (fp != null));
            var acct = CR.Cfg.Account.name_prefix + CR.Cfg.Account.name_suffix;
            var pw = CR.Cfg.Account.password;
            if (fa != null)
            {
                var fld = fa.GetValue(lp) as UnityEngine.UI.InputField;
                if (fld != null) { fld.text = acct; N("account set to '" + acct + "'"); }
            }
            if (fp != null)
            {
                var fld = fp.GetValue(lp) as UnityEngine.UI.InputField;
                if (fld != null) { fld.text = pw; N("password set (" + pw.Length + " chars)"); }
            }
        }
    }
}
else if (arg == "state" || arg == "dragstate")
{
    var fsm = CloverEngine.Game.Fsm;
    var ui = CloverEngine.Game.UI;
    N("station=" + (fsm == null ? "NULL" : fsm.Current.ToString()));
    N("panels pause=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.PausePanel>().ToString())
      + " settings=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.SettingsPanel>().ToString())
      + " result=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.ResultPanel>().ToString())
      + " hud=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.HudPanel>().ToString())
      + " hudInst=" + (ui == null || ui.Get<CR.UI.Panels.HudPanel>() == null ? "NULL" : "OK"));
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    var hudT = typeof(CR.UI.Panels.HudPanel);
    if (hud != null)
    {
        var ids = (int[])hudT.GetField("_handIds", BF).GetValue(hud);
        var ghost = (UnityEngine.UI.Image)hudT.GetField("_ghost", BF).GetValue(hud);
        var st = (UnityEngine.UI.Text)hudT.GetField("_statusText", BF).GetValue(hud);
        N("hud _dragging=" + hudT.GetField("_dragging", BF).GetValue(hud)
          + " dragCard=" + hudT.GetField("_dragCardId", BF).GetValue(hud)
          + " hand=[" + (ids == null ? "-" : string.Join(",", System.Array.ConvertAll(ids, x => x.ToString()))) + "]"
          + " ghostActive=" + (ghost == null ? "NULL" : ghost.gameObject.activeInHierarchy.ToString())
          + " status='" + (st == null ? "?" : st.text) + "'");
    }
    var bvr = CR.View.BattleViewRoot.Instance;
    N("bvr=" + (bvr == null) + " liveUnits=" + (bvr == null ? -1 : bvr.LiveUnitCount));
    var cam = UnityEngine.Camera.main;
    N("camera=" + (cam == null ? "NULL" : cam.name));
    if (arg == "state")
    {
        var inp = CloverEngine.Game.Input;
        N("input backend=" + (inp == null ? "NULL" : inp.BackendName)
          + " mouse=" + (inp == null ? "-" : inp.MousePosition.x.ToString("0") + "," + inp.MousePosition.y.ToString("0")));
        N("frame=" + UnityEngine.Time.frameCount
          + " dtMs=" + UnityEngine.Time.deltaTime.ToString("F2")
          + " dtSmoothMs=" + (UnityEngine.Time.smoothDeltaTime * 1000f).ToString("F2")
          + " fpsSmooth=" + (1f / UnityEngine.Time.smoothDeltaTime).ToString("F1")
          + " unscaledMs=" + (UnityEngine.Time.unscaledDeltaTime * 1000f).ToString("F2"));
        N("device=" + UnityEngine.SystemInfo.graphicsDeviceName
          + " devType=" + UnityEngine.SystemInfo.graphicsDeviceType
          + " screen=" + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height
          + " targetFps=" + UnityEngine.Application.targetFrameRate
          + " vSync=" + UnityEngine.QualitySettings.vSyncCount);
        N("activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
else N("unknown arg '" + arg + "'");
}
catch (System.Exception ex)
{
    N("MODE EX " + ex.GetType().FullName + ": " + ex.Message);
    N("STACK " + ex.StackTrace);
}

try { System.IO.File.AppendAllText(OutFile, sb.ToString()); } catch { }
return sb.ToString();
