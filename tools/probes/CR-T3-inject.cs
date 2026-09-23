// CR-T3 scripted input injection (eval_file, ASCII only, Play mode only, must return).
// Why: the engine's active backend IS the new Input System (measured: Game.Input.BackendName == "InputSystem").
// OS-level SetCursorPos/mouse_event do NOT reach an unfocused Editor Game view (measured: Game.Input.MousePosition
// stayed (0,0)), so the press/drag/release chain is injected at the INPUT DEVICE level instead - the production
// path (InputSystem device -> engine InputManager -> Game.Input -> HudPanel.PollDrag) runs unchanged.
// Argument file T3-inject.txt:
//   "press:x,y"  queue pointer at (x,y) with the left button DOWN
//   "move:x,y"   queue pointer at (x,y) with the left button still DOWN
//   "release:x,y" queue pointer at (x,y) with the left button UP
//   "hold:x,y"   queue pointer at (x,y), buttons unchanged (0)
//   "direct"     fallback: call HudPanel.BeginDrag/MoveGhost/EndDrag directly (no input edge)
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string ArgFile = Root + @"\.ai-tmp\test\T3-inject.txt";
const string OutFile = Root + @"\.ai-tmp\test\T3-inject.out.txt";
var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
       | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

string arg = "";
try { if (System.IO.File.Exists(ArgFile)) arg = System.IO.File.ReadAllText(ArgFile).Trim(); } catch { }
sb.Append("inject '").Append(arg).Append("' at ").Append(System.DateTime.Now.ToString("HH:mm:ss.fff")).Append('\n');
if (!UnityEngine.Application.isPlaying) { sb.Append("PLAY=0\n"); System.IO.File.AppendAllText(OutFile, sb.ToString()); return sb.ToString(); }

var ui = CloverEngine.Game.UI;
var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
var input = CloverEngine.Game.Input;
var mouse = UnityEngine.InputSystem.Mouse.current;
sb.Append("hud=").Append(hud != null).Append(" backend=").Append(input == null ? "NULL" : input.BackendName)
  .Append(" mouseDev=").Append(mouse == null ? "NULL" : mouse.name)
  .Append(" mouseBefore=").Append(input == null ? "-" : input.MousePosition.x.ToString("0") + "," + input.MousePosition.y.ToString("0"))
  .Append('\n');

System.Func<string, string[]> Parts = (s) => s.Split(new char[] { ':' })[1].Split(',');

if (arg.StartsWith("hold") || arg.StartsWith("press") || arg.StartsWith("move") || arg.StartsWith("release"))
{
    if (mouse == null) { sb.Append("NO MOUSE DEVICE\n"); System.IO.File.AppendAllText(OutFile, sb.ToString()); return sb.ToString(); }
    var p = Parts(arg);
    float px = float.Parse(p[0]); float py = float.Parse(p[1]);
    ushort btn = 0;
    if (arg.StartsWith("press")) btn = 1;
    if (arg.StartsWith("release")) btn = 0;
    if (arg.StartsWith("move")) btn = 1;
    var st = new UnityEngine.InputSystem.LowLevel.MouseState();
    st.position = new UnityEngine.Vector2(px, py);
    st.delta = UnityEngine.Vector2.zero;
    st.buttons = btn;
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, st);
    sb.Append("queued position=(").Append(px.ToString("0")).Append(",").Append(py.ToString("0"))
      .Append(") buttons=").Append(btn).Append('\n');
}
else if (arg.StartsWith("ind:"))
{
    // Render-only: show the placement disc at a legal / illegal point and capture it.
    // (PollDrag aborts any drag that is not backed by a really-pressed mouse button, so the
    //  green/red disc has to be shown through the very same View entry point MoveGhost uses.)
    var p = arg.Split(':')[1].Split(',');
    float x = float.Parse(p[0]); float y = float.Parse(p[1]);
    var bvr = CR.View.BattleViewRoot.Instance;
    var cam = UnityEngine.Camera.main;
    var tile = bvr.ScreenToTile(new UnityEngine.Vector3(x, y, 0f));
    bvr.ShowPlacement(tile, 3f, false);
    var ind = (CR.View.PlacementIndicator)bvr.GetType().GetField("_indicator", BF).GetValue(bvr);
    var sr = ind == null ? null : ind.GetComponent<UnityEngine.SpriteRenderer>();
    System.Threading.Thread.Sleep(120);
    UnityEngine.ScreenCapture.CaptureScreenshot(Root + @"\.ai-tmp\screenshots\CR-T3-" + (arg.Split(':')[1].Replace(",", "_")) + ".png");
    sb.Append("ind: tile=(").Append(tile.x.ToString("0.00")).Append(",").Append(tile.y.ToString("0.00"))
      .Append(") legal=").Append(bvr.IsDeployLegal(tile, false))
      .Append(" visible=").Append(ind == null ? "?" : ind.Visible.ToString())
      .Append(" color=").Append(sr == null ? "?" : sr.color.r.ToString("0.00") + "," + sr.color.g.ToString("0.00") + "," + sr.color.b.ToString("0.00"))
      .Append(" camScreen=").Append(cam == null ? "?" : cam.pixelRect.width.ToString("0")).Append('\n');
}
else if (arg.StartsWith("pose:"))
{
    // Freeze a REAL drag pose on screen so a frame can be captured BEFORE EndDrag.
    // begin -> move (this is what draws the ghost + the placement disc), then disarm PollDrag's
    // auto-cancel by clearing _dragging: PollDrag's !_dragging branch just waits for a real
    // GetMouseButtonDown(0) and touches nothing, so the ghost/disc stay rendered for the next frames.
    // Everything rendered is still produced by the production path (BeginDrag/MoveGhost -> ShowPlacement).
    var pp = arg.Split(':')[1].Split(',');
    float px = float.Parse(pp[0]); float py = float.Parse(pp[1]);
    var t = hud.GetType();
    var ids = (int[])t.GetField("_handIds", BF).GetValue(hud);
    var cards = (UnityEngine.UI.Image[])t.GetField("_handCards", BF).GetValue(hud);
    int slot = -1;
    for (int i = 0; i < ids.Length; i++) if (ids[i] != 0) { slot = i; break; }
    if (slot < 0) sb.Append("NO CARD IN HAND\n");
    else
    {
        var rt = cards[slot].rectTransform;
        var cc = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        t.GetMethod("BeginDrag", BF).Invoke(hud, new object[] { ids[slot], slot, new UnityEngine.Vector3(cc.x, cc.y, 0f) });
        t.GetMethod("MoveGhost", BF).Invoke(hud, new object[] { new UnityEngine.Vector3(px, py, 0f) });
        var ghost = (UnityEngine.UI.Image)t.GetField("_ghost", BF).GetValue(hud);
        var gp = ghost == null ? UnityEngine.Vector3.zero : ghost.rectTransform.position;
        var bvr = CR.View.BattleViewRoot.Instance;
        var ind = bvr == null ? null : (CR.View.PlacementIndicator)bvr.GetType().GetField("_indicator", BF).GetValue(bvr);
        sb.Append("pose: pointer=(").Append(px.ToString("0")).Append(",").Append(py.ToString("0"))
          .Append(") press=(").Append(cc.x.ToString("0")).Append(",").Append(cc.y.ToString("0")).Append(") slot=").Append(slot)
          .Append(" card=").Append(ids[slot])
          .Append(" ghostWorld=(").Append(gp.x.ToString("0.0")).Append(",").Append(gp.y.ToString("0.0")).Append(")")
          .Append(" ghostActive=").Append(ghost != null && ghost.gameObject.activeInHierarchy)
          .Append(" ghostAlpha=").Append(ghost == null ? "?" : ghost.color.a.ToString("0.00"))
          .Append(" indicatorVisible=").Append(ind == null ? "?" : ind.Visible.ToString())
          .Append(" indicatorColor=").Append(ind == null ? "?" : ind.GetComponent<UnityEngine.SpriteRenderer>().color.r.ToString("0.00") + "," + ind.GetComponent<UnityEngine.SpriteRenderer>().color.g.ToString("0.00") + "," + ind.GetComponent<UnityEngine.SpriteRenderer>().color.b.ToString("0.00"))
          .Append("\n");
        // disarm the auto-cancel so the pose survives the next frames (see comment above)
        t.GetField("_dragging", BF).SetValue(hud, false);
        sb.Append("pose: _dragging disarmed (screenshot may now be taken before EndDrag)\n");
    }
}
else if (arg.StartsWith("end:"))
{
    var pp = arg.Split(':')[1].Split(',');
    float ex = float.Parse(pp[0]); float ey = float.Parse(pp[1]);
    var t = hud.GetType();
    t.GetMethod("EndDrag", BF).Invoke(hud, new object[] { new UnityEngine.Vector3(ex, ey, 0f) });
    var ghost2 = (UnityEngine.UI.Image)t.GetField("_ghost", BF).GetValue(hud);
    var st2 = (UnityEngine.UI.Text)t.GetField("_statusText", BF).GetValue(hud);
    sb.Append("end: releaseScreen=(").Append(ex.ToString("0")).Append(",").Append(ey.ToString("0"))
      .Append(") ghostActive=").Append(ghost2 != null && ghost2.gameObject.activeInHierarchy)
      .Append(" status='").Append(st2 == null ? "?" : st2.text).Append("'\n");
}
else if (arg.StartsWith("direct-one:"))
{
    // Whole gesture in ONE main-thread call: begin -> move -> (read back) -> end.
    // Nothing can cancel it in between because no frame runs while this statement block executes.
    var p = arg.Split(':')[1].Split(',');
    float dx = float.Parse(p[0]); float dy = float.Parse(p[1]);
    float mx = p.Length > 2 ? float.Parse(p[2]) : 540f;
    float my = p.Length > 3 ? float.Parse(p[3]) : 900f;
    var t = hud.GetType();
    var ids = (int[])t.GetField("_handIds", BF).GetValue(hud);
    var cards = (UnityEngine.UI.Image[])t.GetField("_handCards", BF).GetValue(hud);
    int slot = -1;
    for (int i = 0; i < ids.Length; i++) if (ids[i] != 0) { slot = i; break; }
    var bvr0 = CR.View.BattleViewRoot.Instance;
    if (slot < 0) sb.Append("NO CARD IN HAND\n");
    else
    {
        var rt = cards[slot].rectTransform;
        var cc = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        var cardId = ids[slot];
        t.GetMethod("BeginDrag", BF).Invoke(hud, new object[] { cardId, slot, new UnityEngine.Vector3(cc.x, cc.y, 0f) });
        t.GetMethod("MoveGhost", BF).Invoke(hud, new object[] { new UnityEngine.Vector3(mx, my, 0f) });
        var ghost = (UnityEngine.UI.Image)t.GetField("_ghost", BF).GetValue(hud);
        var gp = ghost == null ? UnityEngine.Vector3.zero : ghost.rectTransform.position;
        var ind2 = bvr0 == null ? null : (CR.View.PlacementIndicator)bvr0.GetType().GetField("_indicator", BF).GetValue(bvr0);
        var sr2 = ind2 == null ? null : ind2.GetComponent<UnityEngine.SpriteRenderer>();
        var tileM = bvr0 == null ? UnityEngine.Vector2.zero : bvr0.ScreenToTile(new UnityEngine.Vector3(mx, my, 0f));
        sb.Append("STEP1 press=(").Append(cc.x.ToString("0")).Append(",").Append(cc.y.ToString("0")).Append(") slot=").Append(slot)
          .Append(" card=").Append(cardId)
          .Append(" dragging=").Append(t.GetField("_dragging", BF).GetValue(hud))
          .Append(" ghostWorld=(").Append(gp.x.ToString("0.0")).Append(",").Append(gp.y.ToString("0.0")).Append(")")
          .Append(" ghostActive=").Append(ghost != null && ghost.gameObject.activeInHierarchy).Append('\n');
        sb.Append("STEP2 moveScreen=(").Append(mx.ToString("0")).Append(",").Append(my.ToString("0"))
          .Append(") tile=(").Append(tileM.x.ToString("0.00")).Append(",").Append(tileM.y.ToString("0.00")).Append(")")
          .Append(" indicatorVisible=").Append(ind2 == null ? "?" : ind2.Visible.ToString())
          .Append(" indicatorColor=").Append(sr2 == null ? "?" : sr2.color.r.ToString("0.00") + "," + sr2.color.g.ToString("0.00") + "," + sr2.color.b.ToString("0.00"))
          .Append(" legal=").Append(bvr0 == null ? "?" : bvr0.IsDeployLegal(tileM, false).ToString()).Append('\n');
        t.GetMethod("EndDrag", BF).Invoke(hud, new object[] { new UnityEngine.Vector3(dx, dy, 0f) });
        sb.Append("STEP3 releaseScreen=(").Append(dx.ToString("0")).Append(",").Append(dy.ToString("0"))
          .Append(") draggingAfter=").Append(t.GetField("_dragging", BF).GetValue(hud))
          .Append(" statusAfter='").Append(((UnityEngine.UI.Text)t.GetField("_statusText", BF).GetValue(hud)) == null ? "?" : ((UnityEngine.UI.Text)t.GetField("_statusText", BF).GetValue(hud)).text).Append("'")
          .Append(" liveUnits=").Append(bvr0 == null ? -1 : bvr0.LiveUnitCount).Append('\n');
    }
}
else if (arg.StartsWith("direct-begin"))
{
    // fallback path: drive the very same HudPanel methods a real gesture reaches
    var t = hud.GetType();
    var ids = (int[])t.GetField("_handIds", BF).GetValue(hud);
    var cards = (UnityEngine.UI.Image[])t.GetField("_handCards", BF).GetValue(hud);
    var slot = -1;
    for (int i = 0; i < ids.Length; i++) if (ids[i] != 0) { slot = i; break; }
    if (slot < 0) { sb.Append("NO CARD IN HAND\n"); }
    else
    {
        var rt = cards[slot].rectTransform;
        var c = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        t.GetMethod("BeginDrag", BF).Invoke(hud, new object[] { ids[slot], slot, new UnityEngine.Vector3(c.x, c.y, 0f) });
        t.GetMethod("MoveGhost", BF).Invoke(hud, new object[] { new UnityEngine.Vector3(540f, 900f, 0f) });
        sb.Append("direct-begin: slot=").Append(slot).Append(" card=").Append(ids[slot])
          .Append(" pressScreen=(").Append(c.x.ToString("0")).Append(",").Append(c.y.ToString("0")).Append(")\n");
    }
}
else
{
    var pp = arg.Split(':')[1].Split(',');
    float ex = float.Parse(pp[0]); float ey = float.Parse(pp[1]);
    var t2 = hud.GetType();
    t2.GetMethod("MoveGhost", BF).Invoke(hud, new object[] { new UnityEngine.Vector3(ex, ey, 0f) });
    t2.GetMethod("EndDrag", BF).Invoke(hud, new object[] { new UnityEngine.Vector3(ex, ey, 0f) });
    sb.Append("direct-end: dropScreen=(").Append(ex.ToString("0")).Append(",").Append(ey.ToString("0")).Append(")\n");
}
System.IO.File.AppendAllText(OutFile, sb.ToString());
return sb.ToString();
