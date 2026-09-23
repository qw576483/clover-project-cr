// CR-T6 driver -- REUSES the skeleton of .ai-tmp/drivers/CR-T1-drive.cs (Cap / Has / ClickGo / Flag /
// state machine); only the state machine and the output names/probes change (reuse the driver, do not rewrite).
//
// Goal (CR-T6, one Play chain): reach a live battle and capture TWO things at once --
//   1) a `source=camera` shot (no HUD)  -> .ai-tmp/screenshots/CR-T6-arena-nohud.png
//   2) the runtime node tree of the ARENA ART root (children of "Art"):
//      name / sprite / sprite rect / sortingOrder / world position / local scale / world size
//      -> .ai-tmp/test/CR-T6-evidence.txt  (L3 numeric evidence for the river & the two bridges)
// Chain: login -> main menu -> AI battle -> sample (done). Only onClick.Invoke(); ASCII only.
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string StepFile = Root + @"\.ai-tmp\test\CR-T6-step.txt";
const string OutFile = Root + @"\.ai-tmp\test\CR-T6-evidence.txt";
const string FlagFile = Root + @"\.ai-tmp\test\CR-T6-flags.txt";
const string ShotDir = Root + @"\.ai-tmp\screenshots";
const string Account = "cr_cli";
const string Password = "cr123456";

System.Action<string> Append = (text) =>
{
    try { System.IO.File.AppendAllText(OutFile, text); } catch { }
};
System.Func<string, bool> Flag = (k) =>
{
    try { return System.IO.File.Exists(FlagFile) && System.IO.File.ReadAllText(FlagFile).Contains(k); }
    catch { return false; }
};
System.Action<string> SetFlag = (k) =>
{
    try { System.IO.File.AppendAllText(FlagFile, "-" + k + "-"); } catch { }
};

const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Static
    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
    | System.Reflection.BindingFlags.Instance;

System.Func<string, string, string> Cap = (outPath, source) =>
{
    try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)); } catch { }
    int w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;
    try
    {
        var asm = typeof(UnityEditor.Editor).Assembly;
        var pmvType = asm.GetType("UnityEditor.PlayModeView");
        var m = pmvType == null ? null : pmvType.GetMethod("GetMainPlayModeViewTargetSize", BF);
        if (m != null)
        {
            var v = (UnityEngine.Vector2)m.Invoke(null, null);
            if (v.x >= 1f && v.y >= 1f) { w = (int)v.x; h = (int)v.y; }
        }
    }
    catch { }
    try
    {
        var t = System.Type.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands, Unity.Pipeline.Editor");
        if (t == null) return "captureType=NULL";
        var m = t.GetMethod("CaptureGameView", BF);
        if (m == null) return "CaptureGameView=NULL";
        var res = m.Invoke(null, new object[] { w, h, null, null, false, 0, source });
        var rt = res.GetType();
        var b64 = (string)rt.GetProperty("Base64").GetValue(res, null);
        if (string.IsNullOrEmpty(b64)) return "capture no base64";
        try { System.IO.File.Delete(outPath); } catch { }
        System.IO.File.WriteAllBytes(outPath, System.Convert.FromBase64String(b64));
        return "CAP " + System.IO.Path.GetFileName(outPath) + " " + w + "x" + h
             + " @" + System.DateTime.Now.ToString("HH:mm:ss");
    }
    catch (System.Exception ex)
    {
        var inner = ex.InnerException ?? ex;
        return "cap EX " + inner.GetType().Name + ": " + inner.Message;
    }
};

System.Func<string, UnityEngine.GameObject> F = (n) => UnityEngine.GameObject.Find(n);
System.Func<string, bool> Has = (n) => UnityEngine.GameObject.Find(n) != null;

System.Func<string, string> ClickGo = (n) =>
{
    var g = UnityEngine.GameObject.Find(n);
    if (g == null) return "NOTFOUND";
    var b = g.GetComponent<UnityEngine.UI.Button>();
    if (b == null) return "NO_BUTTON";
    if (!b.interactable) return "NOT_INTERACTABLE";
    b.onClick.Invoke();
    return "CLICKED";
};

// All children of the arena ART root ("Art"): the ground segments, the base quad, the river and the bridges.
System.Func<System.Collections.Generic.List<UnityEngine.Transform>> ArtNodes = () =>
{
    var res = new System.Collections.Generic.List<UnityEngine.Transform>();
    var arr = UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None);
    for (int i = 0; i < arr.Length; i++)
    {
        var t = arr[i];
        if (t == null || t.parent == null) continue;
        if (t.parent.name == "Art") res.Add(t);
    }
    return res;
};

System.Func<System.Collections.Generic.List<UnityEngine.Transform>, string> DumpArt =
    (nodes) =>
{
    var s = new System.Text.StringBuilder();
    for (int i = 0; i < nodes.Count; i++)
    {
        var c = nodes[i];
        var r = c.GetComponent<UnityEngine.SpriteRenderer>();
        s.Append("ARTNODE ").Append(c.name)
         .Append(" sprite=").Append(r == null || r.sprite == null ? "NULL" : r.sprite.name)
         .Append(" rect=").Append(r == null || r.sprite == null ? "n/a"
            : ((int)r.sprite.rect.width + "x" + (int)r.sprite.rect.height))
         .Append(" tex=").Append(r == null || r.sprite == null ? "n/a"
            : (r.sprite.texture.width + "x" + r.sprite.texture.height))
         .Append(" ppu=").Append(r == null || r.sprite == null ? -1 : r.sprite.pixelsPerUnit)
         .Append(" flipY=").Append(r == null ? false : r.flipY)
         .Append(" order=").Append(r == null ? -9999 : r.sortingOrder)
         .Append(" worldPos=").Append(c.position.x.ToString("0.000")).Append(',')
         .Append(c.position.y.ToString("0.000"))
         .Append(" localScale=").Append(c.localScale.x.ToString("0.000")).Append(',')
         .Append(c.localScale.y.ToString("0.000"))
         .Append(" worldSize=").Append(r == null || r.sprite == null ? "n/a"
            : (r.bounds.size.x.ToString("0.000") + "x" + r.bounds.size.y.ToString("0.000")))
         .Append(" active=").Append(c.gameObject.activeInHierarchy)
         .Append('\n');
    }
    return s.ToString();
};

int step = 0;
try { if (System.IO.File.Exists(StepFile)) int.TryParse(System.IO.File.ReadAllText(StepFile).Trim(), out step); } catch { }
try { System.IO.File.WriteAllText(StepFile, (step + 1).ToString()); } catch { }

sb.Append("=== CR-T6 step ").Append(step)
  .Append(" at ").Append(System.DateTime.Now.ToString("HH:mm:ss")).Append(" ===\n");
if (!UnityEngine.Application.isPlaying)
{
    sb.Append("ABORT not in play mode\n");
    Append(sb.ToString());
    return sb.ToString();
}
sb.Append("play frame=").Append(UnityEngine.Time.frameCount)
  .Append(" scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('\n');
var open = new System.Text.StringBuilder("open:");
foreach (var nm in new string[] { "LoginButton", "AiBattleButton", "Hand0", "PauseButton" })
    if (Has(nm)) open.Append(' ').Append(nm);
sb.Append(open).Append('\n');

string action;

if (Has("LoginButton"))
{
    if (!Flag("login_filled"))
    {
        var f = F("AccountInput"); var p = F("PasswordInput");
        if (f != null) f.GetComponent<UnityEngine.UI.InputField>().text = Account;
        if (p != null) p.GetComponent<UnityEngine.UI.InputField>().text = Password;
        SetFlag("login_filled");
        action = "fill login f=" + (f != null) + " p=" + (p != null);
    }
    else if (!Flag("login_clicked"))
    {
        action = "click.LoginButton " + ClickGo("LoginButton");
        if (action.EndsWith("CLICKED")) SetFlag("login_clicked");
    }
    else action = "POLL login in flight";
}
else if (ArtNodes().Count > 0 && !Flag("arena_dumped"))
{
    // Gate on the ARENA ART ROOT existing (children of "Art"), NOT on a HUD node name: the arena is what
    // this probe is about, and anchoring on a HUD node made step 3..21 spin with an empty "open:" list.
    // NOTE: no Thread.Sleep here and only ONE action per eval -- the pipeline kills a main-thread eval
    // after ~5 s, and a long eval (sleep + two captures) was what re-booted the play session in run #2.

    // -- numeric evidence: the arena art node tree (this is what proves WHICH pixels got laid) --
    var nodes = ArtNodes();
    sb.Append("ART_ROOT_CHILDREN=").Append(nodes.Count).Append('\n');
    sb.Append(DumpArt(nodes));

    System.Func<string, UnityEngine.Transform> FindArt = (n) =>
    {
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].name == n) return nodes[i];
        return null;
    };
    var water = FindArt("RiverWater");
    var bl = FindArt("BridgeLeft");
    var br = FindArt("BridgeRight");
    var stomped = FindArt("RiverBand");   // the previous (wrong) brown band + plank bridges must be GONE

    sb.Append("CHECK RiverWater.exists=").Append(water != null)
      .Append(" BridgeLeft.exists=").Append(bl != null)
      .Append(" BridgeRight.exists=").Append(br != null)
      .Append(" oldRiverBand.exists=").Append(stomped != null).Append('\n');

    bool ok = water != null && bl != null && br != null && stomped == null;
    var wr = water == null ? null : water.GetComponent<UnityEngine.SpriteRenderer>();
    var blr = bl == null ? null : bl.GetComponent<UnityEngine.SpriteRenderer>();
    if (wr != null && wr.sprite != null)
        sb.Append("RiverWater spriteRect=").Append((int)wr.sprite.rect.width).Append('x')
          .Append((int)wr.sprite.rect.height)
          .Append(" srcTex=").Append(wr.sprite.texture.width).Append('x').Append(wr.sprite.texture.height)
          .Append(" order=").Append(wr.sortingOrder).Append('\n');
    if (bl != null && br != null)
        sb.Append("BridgeX left=").Append(bl.position.x.ToString("0.000"))
          .Append(" right=").Append(br.position.x.ToString("0.000"))
          .Append(" (contract: 3.5-9=-5.500 / 14.5-9=+5.500)\n");
    if (water != null && bl != null && br != null)
        ok = ok
            && System.Math.Abs(water.position.x) < 0.01
            && System.Math.Abs(water.position.y) < 0.01
            && System.Math.Abs(bl.position.x + 5.5) < 0.01
            && System.Math.Abs(br.position.x - 5.5) < 0.01
            && blr != null;
    sb.Append("ASSERT arena_nodes_ok=").Append(ok).Append('\n');

    if (!ok)
    {
        action = "REFUSE arena capture: arena node checks failed (see above)";
    }
    else
    {
        SetFlag("arena_dumped");
        action = "arena node tree dumped";
    }
}
else if (!Flag("arena_cam") && ArtNodes().Count > 0)
{
    sb.Append("SHOT ").Append(Cap(ShotDir + @"\CR-T6-arena-nohud.png", "camera")).Append('\n');
    SetFlag("arena_cam");
    action = "camera-only shot done";
}
else if (Flag("arena_cam") && !Flag("arena_shot"))
{
    sb.Append("SHOT2 ").Append(Cap(ShotDir + @"\CR-T6-arena-mine.png", "screen")).Append('\n');
    SetFlag("arena_shot");
    action = "screen shot done";
}
else if (Has("AiBattleButton"))
{
    if (!Flag("arena_shot")) action = "click.AiBattleButton " + ClickGo("AiBattleButton");
    else action = "POLL: shot done, staying put";
}
else if (Has("PauseButton")) action = "POLL battle running";
else action = "POLL state unknown scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

sb.Append("action=").Append(action).Append('\n');
bool done = Flag("arena_shot");
sb.Append("END step ").Append(step).Append(done ? " DONE\n" : "\n");
Append(sb.ToString());
return sb.ToString();
