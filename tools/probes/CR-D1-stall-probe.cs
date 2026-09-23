// CR-D1 stall probe (eval_file; ASCII only; always returns).
// Purpose: make the "intermittent stall BEFORE the first panel" decidable from the running editor
// instead of guessing. The probe only READS state and drives the PRODUCT's own public entry points
// (UI panel open + Button.onClick + AppFlow's event path). No product code is modified.
//
// Why this probe exists (each reading below has no other observer):
//   1) AppFlow.Instance is a STATIC singleton; with "Enter Play Mode Options: Disable Domain Reload"
//      (ProjectSettings/EditorSettings.asset: m_EnterPlayModeOptionsEnabled=1, m_EnterPlayModeOptions=1)
//      it can survive a Play round. Nothing in the product log says whether it survived.
//   2) Its private _bus identifies WHICH engine round the surviving instance is bound to; comparing
//      it with Game.Event (ReferenceEquals) is exactly what RebindIfBusChanged() tests.
//   3) PanelFactory._installedBus / Bootstrap._bus are the same shape of state for the other two
//      components that are cleaned up by Bootstrap.OnApplicationQuit.
//
// Modes (arg file = <root>\.ai-tmp\test\D1-arg.txt):
//   snap      full static + station + panel snapshot (works in EDIT mode too: the statics are readable
//             after editor_stop, which is what makes the survivor readable between rounds)
//   login     force open LoginPanel, fill account/password, click LoginButton (drive toward battle)
//   aibattle  click AiBattleButton on the main menu (server builds the room and pushes PushBattleStart)
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string ArgFile = Root + @"\.ai-tmp\test\D1-arg.txt";
const string OutFile = Root + @"\.ai-tmp\test\D1-out.txt";
var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
       | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

System.Action<string> N = (t) => sb.Append(t).Append('\n');
string arg = "";
try { if (System.IO.File.Exists(ArgFile)) arg = System.IO.File.ReadAllText(ArgFile).Trim(); } catch { }
N("=== CR-D1 probe arg='" + arg + "' at " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " ===");

// ---- helper: describe a static field that holds an engine-round identity ----
System.Func<System.Type, string, string> BusOf = (t, fieldName) =>
{
    if (t == null) return "TYPE-NULL";
    var f = t.GetField(fieldName, BF);
    if (f == null) return "FIELD-MISSING(" + fieldName + ")";
    object v = null;
    try { v = f.GetValue(null); } catch (System.Exception e) { return "EX " + e.GetType().Name; }
    if (v == null) return "NULL";
    return "SET";
};

try
{
    var gameType = typeof(CloverEngine.Game);
    var evProp = gameType.GetProperty("Event", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    var isRunProp = gameType.GetProperty("IsRunning", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    object curEvent = evProp == null ? null : evProp.GetValue(null);
    object isRunning = isRunProp == null ? null : isRunProp.GetValue(null);

    N("play=" + UnityEngine.Application.isPlaying
      + " activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
      + " frame=" + UnityEngine.Time.frameCount);

    // ---------- the three statics that Bootstrap.OnApplicationQuit is supposed to clear ----------
    var afType = typeof(CR.Module.Flow.AppFlow);
    var instProp = afType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    object afInst = instProp == null ? null : instProp.GetValue(null);
    N("AppFlow.Instance=" + (afInst == null ? "NULL" : "ALIVE"));
    if (afInst != null)
    {
        var busF = afType.GetField("_bus", BF);
        var startedF = afType.GetField("_started", BF);
        object afBus = busF == null ? null : busF.GetValue(afInst);
        N("  AppFlow._started=" + (startedF == null ? "?" : startedF.GetValue(afInst).ToString())
          + " AppFlow._bus=" + (afBus == null ? "NULL" : "SET")
          + " _bus==Game.Event: " + (afBus == null || curEvent == null ? "n/a" : ReferenceEquals(afBus, curEvent).ToString()));
    }

    var pfType = typeof(CR.UI.PanelFactory);
    var pfF = pfType.GetField("_installedBus", BF);
    object pfBus = pfF == null ? null : pfF.GetValue(null);
    N("PanelFactory._installedBus=" + (pfBus == null ? "NULL" : "SET")
      + " _installedBus==Game.Event: " + (pfBus == null || curEvent == null ? "n/a" : ReferenceEquals(pfBus, curEvent).ToString()));
    var provProp = typeof(CloverEngine.CloverPresentation).GetProperty("PanelProvider",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    object prov = provProp == null ? null : provProp.GetValue(null);
    N("CloverPresentation.PanelProvider=" + (prov == null ? "NULL" : "SET"));

    var bsType = typeof(CR.App.Bootstrap);
    var bsF = bsType.GetField("_bus", BF);
    object bsBus = bsF == null ? null : bsF.GetValue(null);
    N("Bootstrap._bus=" + (bsBus == null ? "NULL" : "SET")
      + " _bus==Game.Event: " + (bsBus == null || curEvent == null ? "n/a" : ReferenceEquals(bsBus, curEvent).ToString()));

    N("Game.IsRunning=" + isRunning + " Game.Event=" + (curEvent == null ? "NULL" : "SET")
      + " Game.Fsm=" + (CloverEngine.Game.Fsm == null ? "NULL" : "SET")
      + " Game.UI=" + (CloverEngine.Game.UI == null ? "NULL" : "SET")
      + " Game.Net=" + (CloverEngine.Game.Net == null ? "NULL" : "SET")
      + " Game.Scene=" + (CloverEngine.Game.Scene == null ? "NULL" : "SET"));
    // The two readings that decide the stall: the station the FSM sits in, and which panels exist.
    N("station=" + (CloverEngine.Game.Fsm == null ? "NULL" : CloverEngine.Game.Fsm.Current.ToString()));
    var ui = CloverEngine.Game.UI;
    if (ui != null)
    {
        N("panels boot=" + ui.IsOpen<CR.UI.Panels.BootPanel>()
          + " login=" + ui.IsOpen<CR.UI.Panels.LoginPanel>()
          + " register=" + ui.IsOpen<CR.UI.Panels.RegisterPanel>()
          + " nickname=" + ui.IsOpen<CR.UI.Panels.NicknamePanel>()
          + " mainmenu=" + ui.IsOpen<CR.UI.Panels.MainMenuPanel>()
          + " hud=" + ui.IsOpen<CR.UI.Panels.HudPanel>()
          + " pause=" + ui.IsOpen<CR.UI.Panels.PausePanel>());
        N("panelsInst boot=" + (ui.Get<CR.UI.Panels.BootPanel>() != null)
          + " login=" + (ui.Get<CR.UI.Panels.LoginPanel>() != null)
          + " mainmenu=" + (ui.Get<CR.UI.Panels.MainMenuPanel>() != null)
          + " hud=" + (ui.Get<CR.UI.Panels.HudPanel>() != null)
          + " hudInst=" + (ui.Get<CR.UI.Panels.HudPanel>() == null ? "NULL" : "OK"));
    }
    else N("panels UI=NULL");

    // ---------- INTERVENTION (team-lead-ordered causal experiment; works in EDIT mode too) ----------
    // Single variable: set the static AppFlow.Instance to null through its PRIVATE SETTER (reflection).
    // Product code is not touched. If the next Play round then reaches Boot->Login, the survivor is the
    // cause; if it still stalls, the hypothesis is NOT sufficient and must be reported, not papered over.
    if (arg == "nullinstance")
    {
        var afT2 = typeof(CR.Module.Flow.AppFlow);
        var ip = afT2.GetProperty("Instance", System.Reflection.BindingFlags.Public
                                  | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object before = ip == null ? null : ip.GetValue(null);
        string beforeBus = "n/a", beforeStarted = "n/a";
        if (before != null)
        {
            var bf = afT2.GetField("_bus", BF); var sf = afT2.GetField("_started", BF);
            beforeBus = bf == null ? "?" : (bf.GetValue(before) == null ? "NULL" : "SET");
            beforeStarted = sf == null ? "?" : sf.GetValue(before).ToString();
        }
        N("nullinstance BEFORE AppFlow.Instance=" + (before == null ? "NULL" : "ALIVE")
          + " (_started=" + beforeStarted + " _bus=" + beforeBus + ")");
        if (ip == null) N("nullinstance: property Instance NOT FOUND -> intervention impossible");
        else if (before != null)
        {
            var setter = ip.GetSetMethod(true);
            if (setter == null) N("nullinstance: NO SETTER (neither public nor private) -> intervention impossible");
            else
            {
                setter.Invoke(null, new object[] { null });
                N("nullinstance: setter invoked (private setter via reflection; no product code changed)");
            }
        }
        var after = ip == null ? null : ip.GetValue(null);
        N("nullinstance AFTER AppFlow.Instance=" + (after == null ? "NULL" : "ALIVE"));
        // deliberately NOT touched - proves the intervention is single-variable
        N("nullinstance UNTOUCHED PanelFactory._installedBus="
          + (typeof(CR.UI.PanelFactory).GetField("_installedBus", BF).GetValue(null) == null ? "NULL" : "SET")
          + " Bootstrap._bus="
          + (typeof(CR.App.Bootstrap).GetField("_bus", BF).GetValue(null) == null ? "NULL" : "SET"));
    }

    // ---------- drive: the product's own entry points, nothing else ----------
    if (arg == "login" && UnityEngine.Application.isPlaying)
    {
        if (ui == null) N("login: UI=NULL, cannot drive");
        else
        {
            if (!ui.IsOpen<CR.UI.Panels.LoginPanel>() && !ui.IsOpen<CR.UI.Panels.MainMenuPanel>())
            {
                ui.Open<CR.UI.Panels.LoginPanel>(null);
                N("login: opened LoginPanel by hand (station was " + (CloverEngine.Game.Fsm == null ? "NULL" : CloverEngine.Game.Fsm.Current.ToString()) + ")");
            }
            var lp = ui.Get<CR.UI.Panels.LoginPanel>();
            if (lp == null) N("login: LoginPanel instance NULL");
            else
            {
                var t = typeof(CR.UI.Panels.LoginPanel);
                var fa = t.GetField("_account", BF);
                var fp = t.GetField("_password", BF);
                var acct = CR.Cfg.Account.name_prefix + CR.Cfg.Account.name_suffix;
                var pw = CR.Cfg.Account.password;
                if (fa != null) { var fld = fa.GetValue(lp) as UnityEngine.UI.InputField; if (fld != null) { fld.text = acct; N("login: account='" + acct + "'"); } else N("login: _account is not an InputField"); }
                else N("login: field _account missing");
                if (fp != null) { var fld = fp.GetValue(lp) as UnityEngine.UI.InputField; if (fld != null) { fld.text = pw; N("login: password set (" + pw.Length + " chars)"); } else N("login: _password is not an InputField"); }
                else N("login: field _password missing");
                int hit = 0;
                var btns = lp.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                for (int i = 0; i < btns.Length; i++)
                {
                    if (btns[i].gameObject.name != "LoginButton") continue;
                    N("login: LoginButton interactable=" + btns[i].interactable);
                    if (btns[i].interactable) { btns[i].onClick.Invoke(); hit++; }
                }
                N("login: LoginButton invoked=" + hit);
            }
        }
    }
    else if (arg == "aibattle" && UnityEngine.Application.isPlaying)
    {
        if (ui == null) N("aibattle: UI=NULL, cannot drive");
        else
        {
            var mp = ui.Get<CR.UI.Panels.MainMenuPanel>();
            if (mp == null) N("aibattle: MainMenuPanel instance NULL (station=" + (CloverEngine.Game.Fsm == null ? "NULL" : CloverEngine.Game.Fsm.Current.ToString()) + ")");
            else
            {
                int hit = 0;
                var btns = mp.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                for (int i = 0; i < btns.Length; i++)
                {
                    if (btns[i].gameObject.name != "AiBattleButton") continue;
                    N("aibattle: AiBattleButton interactable=" + btns[i].interactable);
                    if (btns[i].interactable) { btns[i].onClick.Invoke(); hit++; }
                }
                N("aibattle: AiBattleButton invoked=" + hit);
            }
        }
    }
    else if (arg != "snap" && UnityEngine.Application.isPlaying) N("unknown arg '" + arg + "'");
}
catch (System.Exception ex)
{
    N("MODE EX " + ex.GetType().FullName + ": " + ex.Message);
    N("STACK " + ex.StackTrace);
}

try { System.IO.File.AppendAllText(OutFile, sb.ToString()); } catch { }
return sb.ToString();
