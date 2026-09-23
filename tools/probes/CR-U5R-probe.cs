// CR-U5R probe (eval_file; ASCII only; Play mode required; always returns).
// Why a SECOND probe file instead of reusing CR-U5-probe.cs:
//   1) CR-U5-probe.cs hard-codes ArgFile/OutFile = .ai-tmp/test/U5-{arg,out}.txt; those are the
//      lost slice's artifacts and this slice must not write to them (CR-U5R task book, iron rule).
//   2) A1 needs an anchor CR-U5-probe.cs never had: the ENGINE-SIDE DIRECT reading of
//      `Mouse.current.position` (its `mouse=` line is Game.Input.MousePosition, an equivalent only).
//   3) A2 needs to construct "station != Battle AND no popup" (the isolated station-guard context),
//      which needs a `closePanel:` mode (Game.UI.Close<T>()).
//   4) A6 fixes (sample counters were never assigned; the voided first-version drift cell) are
//      applied in CR-U5-probe.cs itself -- this file simply does not carry the broken columns.
//
// Modes (arg file = <root>\.ai-tmp\test\U5R-arg.txt):
//   state        station / panels / hud / hand / drag / camera / units / frame-time / device
//                + `input backend=.. mouse=.. mouseRaw=.. mouseDev=.. mousePressed=..`
//                (mouseRaw == UnityEngine.InputSystem.Mouse.current.position, read directly)
//   mousepos     detailed mouse device dump (position / pressed / all InputSystem devices)
//   dragstate    cheap: station / panels / drag / hand + mouseRaw
//   presshand[:n]  queue a REAL left press at hand slot n (default: first non-empty) + mouseRaw
//   moveat:x,y | releaseat:x,y | releasehand[:x,y]   queue a move (buttons=1) / release (buttons=0)
//   click:Name | click:Panel/Name   click a Button by GameObject name (optionally inside a panel)
//   emit:returnmain|aibattle|openpause|stationbattle
//   closePanel:PausePanel|SettingsPanel|ResultPanel|HudPanel   Game.UI.Close<T>() (no product change)
//   loginform    fill the login form and do NOT click (the driver clicks LoginButton)
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string ArgFile = Root + @"\.ai-tmp\test\U5R-arg.txt";
const string OutFile = Root + @"\.ai-tmp\test\U5R-out.txt";
var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
       | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

System.Action<string> N = (t) => sb.Append(t).Append('\n');
string arg = "";
try { if (System.IO.File.Exists(ArgFile)) arg = System.IO.File.ReadAllText(ArgFile).Trim(); } catch { }
N("=== CR-U5R probe arg='" + arg + "' at " + System.DateTime.Now.ToString("HH:mm:ss.fff") + " ===");
if (!UnityEngine.Application.isPlaying) { N("PLAY=0"); try { System.IO.File.AppendAllText(OutFile, sb.ToString()); } catch { } return sb.ToString(); }

// ── engine-side mouse readings: the direct one AND the engine's own sampled one ──
System.Func<string> MouseRaw = () =>
{
    var m = UnityEngine.InputSystem.Mouse.current;
    if (m == null) return "NULL";
    var p = m.position.ReadValue();
    return p.x.ToString("0.##") + "," + p.y.ToString("0.##")
         + " dev=" + m.name + " added=" + m.added
         + " leftPressed=" + (m.leftButton == null ? "?" : m.leftButton.isPressed.ToString());
};
System.Func<string> MouseDeviceList = () =>
{
    var ds = UnityEngine.InputSystem.InputSystem.devices;
    var s = new System.Text.StringBuilder();
    for (int i = 0; i < ds.Count; i++) { if (i > 0) s.Append(" | "); s.Append(ds[i].name).Append('/').Append(ds[i].GetType().Name).Append("/added=").Append(ds[i].added); }
    return s.ToString();
};

var ui = CloverEngine.Game.UI;
var hudT = typeof(CR.UI.Panels.HudPanel);

System.Action<int> NHand = (slot) =>
{
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    if (hud == null) { N("  hud=NULL"); return; }
    var ids = (int[])hudT.GetField("_handIds", BF).GetValue(hud);
    var cards = (UnityEngine.UI.Image[])hudT.GetField("_handCards", BF).GetValue(hud);
    var st = (UnityEngine.UI.Text)hudT.GetField("_statusText", BF).GetValue(hud);
    N("  hud _dragging=" + hudT.GetField("_dragging", BF).GetValue(hud)
      + " dragCard=" + hudT.GetField("_dragCardId", BF).GetValue(hud)
      + " hand=[" + (ids == null ? "-" : string.Join(",", System.Array.ConvertAll(ids, x => x.ToString()))) + "]"
      + " status='" + (st == null ? "?" : st.text) + "'");
    if (slot >= 0 && cards != null && slot < cards.Length && cards[slot] != null)
    {
        var rt = cards[slot].rectTransform;
        var cc = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        N("  slot" + slot + " center screen=(" + cc.x.ToString("0") + "," + cc.y.ToString("0") + ")");
    }
};

try
{
if (arg == "state" || arg == "dragstate")
{
    var fsm = CloverEngine.Game.Fsm;
    N("station=" + (fsm == null ? "NULL" : fsm.Current.ToString()));
    N("panels pause=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.PausePanel>().ToString())
      + " settings=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.SettingsPanel>().ToString())
      + " result=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.ResultPanel>().ToString())
      + " hud=" + (ui == null ? "?" : ui.IsOpen<CR.UI.Panels.HudPanel>().ToString())
      + " hudInst=" + (ui == null || ui.Get<CR.UI.Panels.HudPanel>() == null ? "NULL" : "OK"));
    NHand(-1);
    // Elixir readout: the A1 re-verification casts twice in one chain, and a rejection for
    // "not enough elixir" would be a FALSE negative for a geometry test - so read it every time.
    var hudE = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    string elx = "?";
    if (hudE != null)
    {
        var mE = hudT.GetMethod("CurrentElixirMilli", BF);
        if (mE != null) { try { elx = mE.Invoke(hudE, null).ToString(); } catch { elx = "EX"; } }
    }
    N("  elixirMilli=" + elx + " (1/1000; card cost = CardInfo.elixir x 1000)");
    var bvr = CR.View.BattleViewRoot.Instance;
    N("bvr=" + (bvr == null) + " liveUnits=" + (bvr == null ? -1 : bvr.LiveUnitCount));
    var cam = UnityEngine.Camera.main;
    N("camera=" + (cam == null ? "NULL" : cam.name));
    if (arg == "state")
    {
        var inp = CloverEngine.Game.Input;
        N("input backend=" + (inp == null ? "NULL" : inp.BackendName)
          + " mouse=" + (inp == null ? "-" : inp.MousePosition.x.ToString("0") + "," + inp.MousePosition.y.ToString("0"))
          + " mouseRaw=" + MouseRaw());
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
else if (arg == "mousepos")
{
    N("mouseRaw=" + MouseRaw());
    N("devices=" + MouseDeviceList());
    var inp2 = CloverEngine.Game.Input;
    N("engine backend=" + (inp2 == null ? "NULL" : inp2.BackendName)
      + " MousePosition=" + (inp2 == null ? "-" : inp2.MousePosition.x.ToString("0.##") + "," + inp2.MousePosition.y.ToString("0.##")));
}
else if (arg.StartsWith("presshand"))
{
    var hud = ui == null ? null : ui.Get<CR.UI.Panels.HudPanel>();
    if (hud == null) N("presshand hud=NULL");
    else
    {
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
              + ") mouseDev=" + (mouse == null ? "NULL" : mouse.name) + " mouseRawBefore=" + MouseRaw());
            if (mouse != null)
            {
                var st = new UnityEngine.InputSystem.LowLevel.MouseState();
                st.position = new UnityEngine.Vector2(cc.x, cc.y);
                st.delta = UnityEngine.Vector2.zero;
                st.buttons = 1;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, st);
                N("presshand queued pos=(" + cc.x.ToString("0") + "," + cc.y.ToString("0") + ") buttons=1");
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
        N(arg + " queued pos=(" + rx.ToString("0") + "," + ry.ToString("0") + ") buttons=" + st.buttons + " mouseRawBefore=" + MouseRaw());
    }
}
else if (arg.StartsWith("click"))
{
    var spec = arg.Split(':')[1];
    var parts = spec.Split('/');
    UnityEngine.GameObject host = null;
    if (parts.Length == 2)
    {
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
else if (arg.StartsWith("closePanel"))
{
    var what = arg.Contains(":") ? arg.Split(':')[1] : "";
    N("closePanel " + what + " before: pause=" + ui.IsOpen<CR.UI.Panels.PausePanel>()
      + " settings=" + ui.IsOpen<CR.UI.Panels.SettingsPanel>()
      + " result=" + ui.IsOpen<CR.UI.Panels.ResultPanel>()
      + " hud=" + ui.IsOpen<CR.UI.Panels.HudPanel>()
      + " station=" + (CloverEngine.Game.Fsm == null ? "NULL" : CloverEngine.Game.Fsm.Current.ToString()));
    if (what == "PausePanel") ui.Close<CR.UI.Panels.PausePanel>();
    else if (what == "SettingsPanel") ui.Close<CR.UI.Panels.SettingsPanel>();
    else if (what == "ResultPanel") ui.Close<CR.UI.Panels.ResultPanel>();
    else if (what == "HudPanel") ui.Close<CR.UI.Panels.HudPanel>();
    else N("closePanel unknown '" + what + "'");
    N("closePanel after: pause=" + ui.IsOpen<CR.UI.Panels.PausePanel>()
      + " settings=" + ui.IsOpen<CR.UI.Panels.SettingsPanel>()
      + " result=" + ui.IsOpen<CR.UI.Panels.ResultPanel>()
      + " hud=" + ui.IsOpen<CR.UI.Panels.HudPanel>()
      + " station=" + (CloverEngine.Game.Fsm == null ? "NULL" : CloverEngine.Game.Fsm.Current.ToString()));
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
else if (arg == "loginform")
{
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
            if (fa != null) { var fld = fa.GetValue(lp) as UnityEngine.UI.InputField; if (fld != null) { fld.text = acct; N("account set to '" + acct + "'"); } }
            if (fp != null) { var fld = fp.GetValue(lp) as UnityEngine.UI.InputField; if (fld != null) { fld.text = pw; N("password set (" + pw.Length + " chars)"); } }
        }
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
