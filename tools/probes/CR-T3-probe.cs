// CR-T3 probe driver for `unity command eval_file --file <abs>`. ASCII only. Play mode only. Must `return`.
// Job: login -> MainMenu -> AiBattle -> battle; then a READ-ONLY numeric diagnosis of the
// "press a hand card but slot stays -1 / card cannot be deployed" chain.
// Chain reuse: step-file login/battle-entry shape lifted from AU1-go.cs (same project, same chain).
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string StepFile = Root + @"\.ai-tmp\test\T3-step.txt";
const string OutFile = Root + @"\.ai-tmp\test\T3-diag.txt";

System.Action<string> Append = (t) => { try { System.IO.File.AppendAllText(OutFile, t); } catch { } };
int step = 0;
try { if (System.IO.File.Exists(StepFile)) { int.TryParse(System.IO.File.ReadAllText(StepFile).Trim(), out step); } } catch { }
try { System.IO.File.WriteAllText(StepFile, (step + 1).ToString()); } catch { }

sb.Append("=== T3 step ").Append(step).Append(" at ").Append(System.DateTime.Now.ToString("HH:mm:ss")).Append(" ===\n");
if (!UnityEngine.Application.isPlaying) { sb.Append("ABORT not in play mode\n"); Append(sb.ToString()); return sb.ToString(); }
UnityEngine.Application.runInBackground = true;

var ui = CloverEngine.Game.UI;
var fsm = CloverEngine.Game.Fsm;
var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
       | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

System.Func<string> Station = () => (fsm == null ? "NULL" : fsm.Current.ToString());
System.Func<string, UnityEngine.UI.InputField> FieldOf = (name) =>
{
    var g = UnityEngine.GameObject.Find(name);
    return g == null ? null : g.GetComponent<UnityEngine.UI.InputField>();
};
System.Func<string, string> Click = (name) =>
{
    var g = UnityEngine.GameObject.Find(name);
    if (g == null) return "NOTFOUND";
    var b = g.GetComponent<UnityEngine.UI.Button>();
    if (b == null) return "NO_BUTTON";
    if (!b.interactable) return "NOT_INTERACTABLE";
    b.onClick.Invoke();
    return "CLICKED";
};

string action = "none";
if (step == 0)
{
    try { System.IO.File.WriteAllText(OutFile, "=== T3 diag ===\n"); } catch { }
    action = "env.device=" + UnityEngine.SystemInfo.graphicsDeviceName
        + " screen=" + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height
        + " station=" + Station();
    if (ui != null)
    {
        if (!ui.IsOpen<CR.UI.Panels.LoginPanel>() && !ui.IsOpen<CR.UI.Panels.MainMenuPanel>() && !ui.IsOpen<CR.UI.Panels.NicknamePanel>())
            ui.Open<CR.UI.Panels.LoginPanel>(null);
        var f = FieldOf("AccountInput");
        if (f != null) f.text = CR.Cfg.Account.name_prefix + CR.Cfg.Account.name_suffix;
        var p = FieldOf("PasswordInput");
        if (p != null) p.text = CR.Cfg.Account.password;
        action += " | login form filled";
    }
}
else if (step == 1)
{
    action = (ui != null && ui.IsOpen<CR.UI.Panels.LoginPanel>()) ? ("click.LoginButton " + Click("LoginButton")) : ("LoginPanel not open; station=" + Station());
}
else if (step == 2)
{
    if (ui != null && ui.IsOpen<CR.UI.Panels.NicknamePanel>())
    {
        var nf = FieldOf("NicknameInput");
        if (nf != null) nf.text = new string(new char[] { (char)0x514B, (char)0x6D1B, (char)0x5F17 });
        action = "nick set; click.SubmitButton " + Click("SubmitButton");
    }
    else if (ui != null && ui.IsOpen<CR.UI.Panels.MainMenuPanel>()) action = "click.AiBattleButton " + Click("AiBattleButton");
    else action = "wait station=" + Station();
}
else if (step == 3)
{
    if (ui != null && ui.IsOpen<CR.UI.Panels.MainMenuPanel>()) action = "click.AiBattleButton " + Click("AiBattleButton") + " station=" + Station();
    else action = "station=" + Station();
}
else
{
    // ---- battle wait + read-only diagnosis ----
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    var bvr = CR.View.BattleViewRoot.Instance;
    var bm = CR.Module.Battle.BattleManager.Instance;
    var handOk = false;
    var hudType = hud == null ? null : hud.GetType();
    System.Func<string, object> Fld = (n) =>
    {
        if (hudType == null) return null;
        var f = hudType.GetField(n, BF);
        return f == null ? null : f.GetValue(hud);
    };
    var ids = hud == null ? null : (int[])Fld("_handIds");
    if (ids != null) for (int i = 0; i < ids.Length; i++) if (ids[i] != 0) handOk = true;

    var ready = bvr != null && bvr.Ready && bvr.LiveUnitCount > 0 && handOk;
    action = "station=" + Station()
        + " hudOpen=" + (hud != null)
        + " bvrReady=" + (bvr != null && bvr.Ready)
        + " liveUnits=" + (bvr != null ? bvr.LiveUnitCount : -1)
        + " snaps=" + (bvr != null ? bvr.SnapshotCount : -1)
        + " hand=" + (ids == null ? "?" : ids[0] + "," + ids[1] + "," + ids[2] + "," + ids[3]);

    if (ready)
    {
        var cam = UnityEngine.Camera.main;
        var uiCam = CloverEngine.UIFactory.UICamera();
        var canvas = hud.transform.GetComponentInParent<UnityEngine.Canvas>();
        sb.Append("--- DIAG ---\n");
        sb.Append("canvas=").Append(canvas == null ? "NULL" : canvas.renderMode + " scaleFactor=" + canvas.scaleFactor)
          .Append(" worldCamera=").Append(canvas == null || canvas.worldCamera == null ? "NULL" : canvas.worldCamera.name).Append('\n');
        sb.Append("screen=").Append(UnityEngine.Screen.width).Append('x').Append(UnityEngine.Screen.height).Append('\n');
        var input = CloverEngine.Game.Input;
        sb.Append("input.backend=").Append(input == null ? "NULL" : input.BackendName)
          .Append(" available=").Append(input == null ? "?" : input.Available.ToString())
          .Append(" mouseNow=").Append(input == null ? "-" : input.MousePosition.x.ToString("0") + "," + input.MousePosition.y.ToString("0")).Append('\n');
        sb.Append("camera.main=").Append(cam == null ? "NULL" : cam.name)
          .Append(" ortho=").Append(cam == null ? "?" : cam.orthographic.ToString())
          .Append(" size=").Append(cam == null ? "?" : cam.orthographicSize.ToString("0.00"))
          .Append(" pos=").Append(cam == null ? "?" : cam.transform.position.x.ToString("0.0") + "," + cam.transform.position.y.ToString("0.0") + "," + cam.transform.position.z.ToString("0.0"))
          .Append(" aspect=").Append(cam == null ? "?" : cam.aspect.ToString("0.000"))
          .Append(" pixelRect=").Append(cam == null ? "?" : cam.pixelRect.x.ToString("0") + "," + cam.pixelRect.y.ToString("0") + "," + cam.pixelRect.width.ToString("0") + "x" + cam.pixelRect.height.ToString("0")).Append('\n');
        sb.Append("uiCam=").Append(uiCam == null ? "NULL" : uiCam.name)
          .Append(" sameAsMain=").Append(cam == uiCam).Append('\n');
        sb.Append("bvr.cam=").Append(bvr == null ? "?" : (bvr.GetType().GetField("_cam", BF) == null ? "?" : ((UnityEngine.Camera)bvr.GetType().GetField("_cam", BF).GetValue(bvr) == null ? "NULL" : "ok"))).Append('\n');
        sb.Append("myTeam=").Append(bm == null ? "?" : bm.MyTeam.ToString()).Append('\n');

        // hand card rects -> screen points, and the two RectTransformUtility judgements
        var cards = (UnityEngine.UI.Image[])Fld("_handCards");
        var hit = hudType.GetMethod("HitTestHand", BF);
        for (int i = 0; i < (cards == null ? 0 : cards.Length); i++)
        {
            var card = cards[i];
            if (card == null) { sb.Append("slot[").Append(i).Append("]=<null image>\n"); continue; }
            var rt = card.rectTransform;
            var wc = rt.TransformPoint(rt.rect.center);
            var pNull = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, wc);
            var pCam = uiCam == null ? UnityEngine.Vector2.zero : UnityEngine.RectTransformUtility.WorldToScreenPoint(uiCam, wc);
            var vNull = new UnityEngine.Vector2(pNull.x, pNull.y);
            var vCam = uiCam == null ? UnityEngine.Vector2.zero : new UnityEngine.Vector2(pCam.x, pCam.y);
            var cNull = UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(rt, vNull, null);
            var cCam = uiCam == null ? false : UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(rt, vNull, uiCam);
            var hitNull = -99;
            var hitCam = -99;
            if (hit != null)
            {
                hitNull = (int)hit.Invoke(hud, new object[] { new UnityEngine.Vector3(pNull.x, pNull.y, 0f) });
                if (uiCam != null) hitCam = (int)hit.Invoke(hud, new object[] { new UnityEngine.Vector3(pCam.x, pCam.y, 0f) });
            }
            sb.Append("slot[").Append(i).Append("] cardId=").Append(ids == null ? "?" : ids[i].ToString())
              .Append(" rectWorld=").Append(rt.position.x.ToString("0")).Append(",").Append(rt.position.y.ToString("0"))
              .Append(" size=").Append(rt.rect.width.ToString("0")).Append("x").Append(rt.rect.height.ToString("0"))
              .Append(" centerScreen(null)=").Append(vNull.x.ToString("0")).Append(",").Append(vNull.y.ToString("0"))
              .Append(" centerScreen(uiCam)=").Append(vCam.x.ToString("0")).Append(",").Append(vCam.y.ToString("0"))
              .Append(" contains(cam)=").Append(cCam)
              .Append(" contains(null)=").Append(cNull)
              .Append(" HitTestHand(ptNull)=").Append(hitNull)
              .Append(" HitTestHand(ptCam)=").Append(hitCam)
              .Append(" active=").Append(card.gameObject.activeInHierarchy).Append('\n');
        }

        // what a real drop at a given screen point maps to, and whether it reads legal
        System.Func<UnityEngine.Vector2, string> DropAt = (sp) =>
        {
            var tile = bvr.ScreenToTile(new UnityEngine.Vector3(sp.x, sp.y, 0f));
            var legal = bvr.IsDeployLegal(tile, false);
            return "screen(" + sp.x.ToString("0") + "," + sp.y.ToString("0") + ") -> tile(" + tile.x.ToString("0.00") + "," + tile.y.ToString("0.00") + ") legal=" + legal;
        };
        var probes = new UnityEngine.Vector2[]
        {
            new UnityEngine.Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.35f),
            new UnityEngine.Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.45f),
            new UnityEngine.Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.60f),
            new UnityEngine.Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.75f)
        };
        for (int i = 0; i < probes.Length; i++) sb.Append("dropProbe ").Append(i).Append(": ").Append(DropAt(probes[i])).Append('\n');

        // target tile -> screen (so a scripted drag knows where to release)
        var team = bm == null ? 0 : bm.MyTeam;
        var ty = team == 0 ? 12f : 20f;
        var wDrop = CR.GameConst.TileToWorld(9f, ty);
        var sDrop = cam == null ? UnityEngine.Vector3.zero : cam.WorldToScreenPoint(new UnityEngine.Vector3(wDrop.x, wDrop.y, 0f));
        sb.Append("targetTile(9,").Append(ty.ToString("0")).Append(") world=").Append(wDrop.x.ToString("0.00")).Append(",").Append(wDrop.y.ToString("0.00"))
          .Append(" screen=").Append(sDrop.x.ToString("0")).Append(",").Append(sDrop.y.ToString("0"))
          .Append(" legal=").Append(bvr.IsDeployLegal(new UnityEngine.Vector2(9f, ty), false)).Append('\n');
        sb.Append("_dragging=").Append(Fld("_dragging")).Append('\n');
        sb.Append("END DIAG\n");
        Append(sb.ToString());
        action += " | DIAG DONE";
    }
}
sb.Append("action=").Append(action).Append('\n');
sb.Append("END step ").Append(step).Append('\n');
Append(sb.ToString());
return sb.ToString();
