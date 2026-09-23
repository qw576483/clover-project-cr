// CR-T3 state reporter (eval_file, ASCII only, Play mode only, must return).
// Reads a label from T3-label.txt and appends one sample block to T3-mouse.txt.
// Every reading below is taken from the RUNNING app (L3): it is the app's own state, not a narration.
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string LabelFile = Root + @"\.ai-tmp\test\T3-label.txt";
const string OutFile = Root + @"\.ai-tmp\test\T3-mouse.txt";
var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
       | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

string label = "sample";
try { if (System.IO.File.Exists(LabelFile)) label = System.IO.File.ReadAllText(LabelFile).Trim(); } catch { }
if (string.IsNullOrEmpty(label)) label = "sample";

// Optional screenshot: T3-shot.txt holds the shot name; the png lands in <Root>\.ai-tmp\screenshots\CR-T3-<name>.png
const string ShotFile = Root + @"\.ai-tmp\test\T3-shot.txt";
string shot = "";
try { if (System.IO.File.Exists(ShotFile)) shot = System.IO.File.ReadAllText(ShotFile).Trim(); } catch { }
if (shot.Length > 0 && UnityEngine.Application.isPlaying)
{
    var dir = Root + @"\.ai-tmp\screenshots";
    System.IO.Directory.CreateDirectory(dir);
    var path = dir + @"\CR-T3-" + shot + ".png";
    try { System.IO.File.Delete(path); } catch { }
    UnityEngine.ScreenCapture.CaptureScreenshot(path);
    sb.Append("shot=").Append(path).Append('\n');
}

sb.Append("### ").Append(label).Append(" at ").Append(System.DateTime.Now.ToString("HH:mm:ss.fff")).Append('\n');
if (!UnityEngine.Application.isPlaying) { sb.Append("PLAY=0\n"); System.IO.File.AppendAllText(OutFile, sb.ToString()); return sb.ToString(); }

System.Func<object, string, object> FieldOf = (o, n) =>
{
    if (o == null) return null;
    var f = o.GetType().GetField(n, BF);
    return f == null ? null : f.GetValue(o);
};

var ui = CloverEngine.Game.UI;
var input = CloverEngine.Game.Input;
var fsm = CloverEngine.Game.Fsm;
var bvr = CR.View.BattleViewRoot.Instance;
var bm = CR.Module.Battle.BattleManager.Instance;
var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();

var mp = input == null ? UnityEngine.Vector3.zero : input.MousePosition;
sb.Append("station=").Append(fsm == null ? "NULL" : fsm.Current.ToString())
  .Append(" backend=").Append(input == null ? "NULL" : input.BackendName)
  .Append(" mouseGame=(").Append(mp.x.ToString("0")).Append(",").Append(mp.y.ToString("0")).Append(")")
  .Append(" screen=").Append(UnityEngine.Screen.width).Append('x').Append(UnityEngine.Screen.height)
  .Append(" frame=").Append(UnityEngine.Time.frameCount).Append('\n');

string gv = "?";
try
{
    var t = System.Type.GetType("UnityEditor.GameView,UnityEditor");
    var gw = t == null ? null : UnityEditor.EditorWindow.GetWindow(t);
    if (gw != null)
    {
        var r = gw.position;
        gv = r.x.ToString("0") + "," + r.y.ToString("0") + "," + r.width.ToString("0") + "x" + r.height.ToString("0");
    }
}
catch (System.Exception e) { gv = "ERR:" + e.GetType().Name; }
sb.Append("gameViewRect(screen coords, y from top)=").Append(gv)
  .Append(" timerSinceLoad=").Append(UnityEngine.Time.realtimeSinceStartup.ToString("0.0")).Append('\n');

if (hud == null) { sb.Append("hud=NULL\n"); System.IO.File.AppendAllText(OutFile, sb.ToString()); return sb.ToString(); }

var dragging = FieldOf(hud, "_dragging");
var dragCardId = FieldOf(hud, "_dragCardId");
var ghost = (UnityEngine.UI.Image)FieldOf(hud, "_ghost");
var statusText = (UnityEngine.UI.Text)FieldOf(hud, "_statusText");
var ids = (int[])FieldOf(hud, "_handIds");
var cards = (UnityEngine.UI.Image[])FieldOf(hud, "_handCards");
sb.Append("hud.dragging=").Append(dragging).Append(" dragCardId=").Append(dragCardId)
  .Append(" status='").Append(statusText == null ? "?" : statusText.text).Append("'\n");

var cam = UnityEngine.Camera.main;
if (cards != null) for (int i = 0; i < cards.Length; i++)
{
    var c = cards[i];
    if (c == null) continue;
    var rt = c.rectTransform;
    var sc = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
    sb.Append("  slot[").Append(i).Append("] id=").Append(ids == null ? "?" : ids[i].ToString())
      .Append(" centerScreen(null)=(").Append(sc.x.ToString("0")).Append(",").Append(sc.y.ToString("0")).Append(")\n");
}

if (ghost != null)
{
    var gp = ghost.rectTransform.position;
    var gs = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, gp);
    sb.Append("ghost.active=").Append(ghost.gameObject.activeInHierarchy)
      .Append(" ghostWorld=(").Append(gp.x.ToString("0.0")).Append(",").Append(gp.y.ToString("0.0")).Append(")")
      .Append(" ghostScreen=(").Append(gs.x.ToString("0")).Append(",").Append(gs.y.ToString("0")).Append(")")
      .Append(" ghostColor=").Append(ghost.color.a.ToString("0.00")).Append('\n');
}
else sb.Append("ghost=NULL\n");

// Reading for "the ghost card no longer carries a card name": after CR-T2c deleted HudPanel._ghostText
// there must be NO text node whose name mentions Ghost; the full active/inactive Text-node census under
// the panel root is printed too, so "no hand card name either" is a reading and not a narration.
{
    var rootRt = FieldOf(hud, "_root") as UnityEngine.RectTransform;
    var names = new System.Text.StringBuilder();
    int ghostNamed = 0;
    int total = 0;
    if (rootRt != null)
    {
        var ts = rootRt.GetComponentsInChildren<UnityEngine.UI.Text>(true);
        for (int i = 0; i < ts.Length; i++)
        {
            if (ts[i] == null) continue;
            total++;
            if (names.Length > 0) names.Append(',');
            names.Append(ts[i].gameObject.name);
            if (!ts[i].gameObject.activeInHierarchy) names.Append("(off)");
            if (ts[i].gameObject.activeInHierarchy &&
                ts[i].gameObject.name.IndexOf("Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0) ghostNamed++;
        }
    }
    sb.Append("ghostNameTextNodes=").Append(ghostNamed).Append(" textNodes=").Append(total)
      .Append(" rootTexts=").Append(names.ToString()).Append('\n');
}

if (bvr != null)
{
    var ind = (CR.View.PlacementIndicator)FieldOf(bvr, "_indicator");
    sb.Append("bvr.ready=").Append(bvr.Ready).Append(" liveUnits=").Append(bvr.LiveUnitCount)
      .Append(" snaps=").Append(bvr.SnapshotCount)
      .Append(" myTeam=").Append(bm == null ? "?" : bm.MyTeam.ToString()).Append('\n');
    if (ind != null)
    {
        var w = ind.transform.position;
        var r = ind.GetComponent<UnityEngine.SpriteRenderer>();
        var isc = cam == null ? UnityEngine.Vector3.zero : cam.WorldToScreenPoint(w);
        sb.Append("  indicator.visible=").Append(ind.Visible)
          .Append(" world=(").Append(w.x.ToString("0.00")).Append(",").Append(w.y.ToString("0.00")).Append(")")
          .Append(" screen=(").Append(isc.x.ToString("0")).Append(",").Append(isc.y.ToString("0")).Append(")")
          .Append(" color=").Append(r == null ? "?" : r.color.r.ToString("0.00") + "," + r.color.g.ToString("0.00") + "," + r.color.b.ToString("0.00"))
          .Append(" legalGreen=").Append(r == null ? "?" : (r.color.g > r.color.r).ToString()).Append('\n');
    }
    if (cam != null)
    {
        // where the current pointer would land, and whether the client-side pre-check says legal
        var tile = bvr.ScreenToTile(mp);
        sb.Append("  pointerTile=(").Append(tile.x.ToString("0.00")).Append(",").Append(tile.y.ToString("0.00")).Append(")")
          .Append(" clientLegal=").Append(bvr.IsDeployLegal(tile, false)).Append('\n');
    }
}
System.IO.File.AppendAllText(OutFile, sb.ToString());
return sb.ToString();
