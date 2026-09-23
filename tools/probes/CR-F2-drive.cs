// CR-F2 driver -- REUSES the skeleton of .ai-tmp/drivers/CR-T6-drive.cs (Cap / Flag / SetFlag /
// ClickGo / one-action-per-eval state machine); only the states and the probes change
// (reuse the driver, do not rewrite).
//
// Goal (CR-F2, ONE Play chain, numeric L3 evidence for four fixes):
//   S3  settings panel has NO "voice" row        -> node existence + popup height (662)
//   #5  engine-side quality change reaches the OPEN panel -> panel text follows (低/中/高)
//   #1  Settings.*Changed subscribers really run -> emit BGM/Fullscreen changed, panel text follows
//   #4  deck save button is disabled in flight + N clicks => exactly ONE save request
//   #3  AI battle button N clicks => exactly ONE MsgAiBattleStart (log counters, tail-only)
// Chain: login -> main menu -> settings -> deck -> AI battle. Only onClick.Invoke(); ASCII only.
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string StepFile = Root + @"\.ai-tmp\test\CR-F2-step.txt";
const string OutFile = Root + @"\.ai-tmp\test\CR-F2-evidence.txt";
const string FlagFile = Root + @"\.ai-tmp\test\CR-F2-flags.txt";
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

System.Func<object, string, object> Field = (obj, name) =>
{
    try
    {
        if (obj == null) return null;
        var t = obj.GetType();
        while (t != null)
        {
            var f = t.GetField(name, BF);
            if (f != null) return f.GetValue(obj);
            t = t.BaseType;
        }
    }
    catch { }
    return null;
};

// NOTE (measured, not assumed): this driver CANNOT count the log itself. The eval sandbox runs
// outside the Editor process, and the engine keeps `client/logs/<date>.log` open with no sharing
// => every read (File.ReadAllText / FileInfo.Length of the data) throws IOException
// ("Sharing violation"). Only metadata calls such as File.Exists survive.
// => the counts are produced AFTER `editor_stop` (file closed) by
//    `tools/probes/cr-f2-count.ps1 -Since "<session launch time>"`, which asserts
//    SEND==1 / DUP-DROP==5 / SAVE-SEND==1 / SAVE-DUP==5 inside this session's window.

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

// N raw onClick invokes (bypasses `interactable` on purpose: that is exactly what a scripted
// "click 6 times" does, and it is what the in-flight guard -- not the button state -- must stop).
System.Func<string, int, string> ClickN = (n, times) =>
{
    var g = UnityEngine.GameObject.Find(n);
    if (g == null) return "NOTFOUND";
    var b = g.GetComponent<UnityEngine.UI.Button>();
    if (b == null) return "NO_BUTTON";
    for (int i = 0; i < times; i++) b.onClick.Invoke();
    return "CLICKEDx" + times;
};

System.Func<CR.UI.Panels.SettingsPanel> Panel = () =>
{
    var arr = UnityEngine.Object.FindObjectsByType<CR.UI.Panels.SettingsPanel>(UnityEngine.FindObjectsSortMode.None);
    return arr != null && arr.Length > 0 ? arr[0] : null;
};
System.Func<CR.UI.Panels.DeckEditPanel> Deck = () =>
{
    var arr = UnityEngine.Object.FindObjectsByType<CR.UI.Panels.DeckEditPanel>(UnityEngine.FindObjectsSortMode.None);
    return arr != null && arr.Length > 0 ? arr[0] : null;
};
System.Func<UnityEngine.UI.Text, string> TextOf = (t) => t == null ? "NULL" : (t.text ?? "null");
System.Func<CR.UI.Panels.SettingsPanel, int, string> TierName = (p, i) =>
{
    try
    {
        var f = typeof(CR.UI.Panels.SettingsPanel).GetField("TierNames", BF);
        var arr = f == null ? null : (string[])f.GetValue(null);
        if (arr == null || i < 0 || i >= arr.Length) return "?";
        return arr[i];
    }
    catch { return "?"; }
};

int step = 0;
try { if (System.IO.File.Exists(StepFile)) int.TryParse(System.IO.File.ReadAllText(StepFile).Trim(), out step); } catch { }
try { System.IO.File.WriteAllText(StepFile, (step + 1).ToString()); } catch { }

sb.Append("=== CR-F2 step ").Append(step)
  .Append(" at ").Append(System.DateTime.Now.ToString("HH:mm:ss")).Append(" ===\n");

if (!UnityEngine.Application.isPlaying)
{
    sb.Append("ABORT not in play mode\n");
    Append(sb.ToString());
    return sb.ToString();
}

sb.Append("play frame=").Append(UnityEngine.Time.frameCount)
  .Append(" scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('\n');

string action;

// ── 1. login ──
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
// ── 2. main menu -> settings ──
else if (Has("SettingsButton") && !Flag("settings_open"))
{
    action = "click.SettingsButton " + ClickGo("SettingsButton");
    if (action.EndsWith("CLICKED")) SetFlag("settings_open");
}
// ── 3. settings panel: voice row gone + row set + popup height ──
else if (Panel() != null && !Flag("settings_dump"))
{
    var p = Panel();
    var box = F("SettingsBox");
    var boxRt = box == null ? null : box.GetComponent<UnityEngine.RectTransform>();
    float boxH = boxRt == null ? -1f : boxRt.sizeDelta.y;

    bool voiceGone = !Has("VoiceLabel") && !Has("VoiceSlider") && !Has("VoiceValue");
    bool rowsOk = Has("BgmLabel") && Has("SfxLabel") && Has("QualityLabel") && Has("FullscreenLabel");

    var bgmText = TextOf((UnityEngine.UI.Text)Field(p, "_bgmValue"));
    var sfxText = TextOf((UnityEngine.UI.Text)Field(p, "_sfxValue"));
    var qualityText = TextOf((UnityEngine.UI.Text)Field(p, "_qualityValue"));
    var fullText = TextOf((UnityEngine.UI.Text)Field(p, "_fullscreenValue"));

    sb.Append("CHECK settings_popup boxH=").Append(boxH.ToString("0.0"))
      .Append(" (expect 662) rowsOk=").Append(rowsOk)
      .Append(" voiceRowGone=").Append(voiceGone).Append('\n');
    sb.Append("PANEL bgm=").Append(bgmText).Append(" sfx=").Append(sfxText)
      .Append(" quality=").Append(qualityText).Append(" fullscreen=").Append(fullText).Append('\n');
    bool ok = voiceGone && rowsOk && System.Math.Abs(boxH - 662f) < 0.5f;
    sb.Append("ASSERT settings_rows_ok=").Append(ok).Append('\n');
    SetFlag("settings_dump");
    action = "settings panel dumped ok=" + ok;
}
// ── 4. screenshot of the re-laid settings panel (visual criterion) ──
else if (Flag("settings_dump") && !Flag("settings_shot"))
{
    sb.Append("SHOT ").Append(Cap(ShotDir + @"\CR-F2-settings-panel.png", "screen")).Append('\n');
    SetFlag("settings_shot");
    action = "settings panel shot";
}
// ── 5. #5: ENGINE-side tier change must reach the OPEN panel ──
else if (Flag("settings_shot") && !Flag("quality_resync"))
{
    var p = Panel();
    var before = TextOf((UnityEngine.UI.Text)Field(p, "_qualityValue"));
    var lvl = CloverEngine.Game.Quality == null ? CloverEngine.QualityTier.Medium : CloverEngine.Game.Quality.Level;
    var target = lvl == CloverEngine.QualityTier.Low ? CloverEngine.QualityTier.Medium : CloverEngine.QualityTier.Low;
    sb.Append("QUALITY before=").Append(before).Append(" engine=").Append(lvl).Append(" -> target=").Append(target).Append('\n');
    CloverEngine.Game.Quality.SetLevel(target);   // engine-side path (what AutoDowngrade calls)
    var after = TextOf((UnityEngine.UI.Text)Field(p, "_qualityValue"));
    var expTarget = TierName(p, (int)target);
    bool ok1 = after == expTarget;
    sb.Append("QUALITY after SetLevel=").Append(after).Append(" expect=").Append(expTarget).Append(" ok=").Append(ok1).Append('\n');
    CloverEngine.Game.Quality.SetLevel(lvl);      // restore the player's tier (also fires the event)
    var back = TextOf((UnityEngine.UI.Text)Field(p, "_qualityValue"));
    var expBack = TierName(p, (int)lvl);
    bool ok2 = back == expBack;
    sb.Append("QUALITY restored=").Append(back).Append(" expect=").Append(expBack).Append(" ok=").Append(ok2).Append('\n');
    sb.Append("ASSERT quality_resync_ok=").Append(ok1 && ok2).Append('\n');
    SetFlag("quality_resync");
    action = "engine tier change reflected in open panel ok=" + (ok1 && ok2);
}
// ── 6. #1: Settings.BgmVolumeChanged really has a subscriber that redraws the panel ──
else if (Flag("quality_resync") && !Flag("bgm_emit"))
{
    var p = Panel();
    var before = TextOf((UnityEngine.UI.Text)Field(p, "_bgmValue"));
    float real = 1f;
    try { real = CloverEngine.Game.Setting.Get("audio.bgm", 1f); } catch { }
    CloverEngine.Game.Event.Emit(CR.Events.Settings.BgmVolumeChanged, 0.42f);
    var after = TextOf((UnityEngine.UI.Text)Field(p, "_bgmValue"));
    var slider = (UnityEngine.UI.Slider)Field(p, "_bgmSlider");
    float sv = slider == null ? -1f : slider.value;
    bool ok1 = after == "42%" && System.Math.Abs(sv - 0.42f) < 0.01f;
    sb.Append("BGMV before=").Append(before).Append(" afterEmit(0.42)=").Append(after)
      .Append(" sliderValue=").Append(sv.ToString("0.###")).Append(" ok=").Append(ok1).Append('\n');
    CloverEngine.Game.Event.Emit(CR.Events.Settings.BgmVolumeChanged, real);   // restore display
    var back = TextOf((UnityEngine.UI.Text)Field(p, "_bgmValue"));
    bool ok2 = back == ((int)System.Math.Round(real * 100f)).ToString() + "%";
    sb.Append("BGMV restored=").Append(back).Append(" (real=").Append(real.ToString("0.###")).Append(") ok=").Append(ok2).Append('\n');
    sb.Append("ASSERT changed_event_subscriber_ok=").Append(ok1 && ok2).Append('\n');
    SetFlag("bgm_emit");
    action = "BgmVolumeChanged subscriber ok=" + (ok1 && ok2);
}
// ── 7. #1: FullscreenChanged subscriber ──
else if (Flag("bgm_emit") && !Flag("full_emit"))
{
    var p = Panel();
    bool cur = UnityEngine.Screen.fullScreen;
    CloverEngine.Game.Event.Emit(CR.Events.Settings.FullscreenChanged, !cur);
    var t = TextOf((UnityEngine.UI.Text)Field(p, "_fullscreenValue"));
    bool ok = t == (!cur ? "开" : "关");
    sb.Append("FULL f=").Append(cur).Append(" afterEmit(").Append(!cur).Append(") text=").Append(t).Append(" ok=").Append(ok).Append('\n');
    CloverEngine.Game.Event.Emit(CR.Events.Settings.FullscreenChanged, cur);   // restore display
    var t2 = TextOf((UnityEngine.UI.Text)Field(p, "_fullscreenValue"));
    sb.Append("ASSERT fullscreen_changed_subscriber_ok=").Append(ok && t2 == (cur ? "开" : "关")).Append('\n');
    SetFlag("full_emit");
    action = "FullscreenChanged subscriber ok=" + ok;
}
// ── 8. close settings, open deck edit ──
else if (Flag("full_emit") && !Flag("settings_close"))
{
    action = "click.CloseButton " + ClickGo("CloseButton");
    if (action.EndsWith("CLICKED")) SetFlag("settings_close");
}
else if (Has("DeckButton") && !Flag("deck_open"))
{
    action = "click.DeckButton " + ClickGo("DeckButton");
    if (action.EndsWith("CLICKED")) SetFlag("deck_open");
}
// ── 9. #4: deck save button state + click storm ──
else if (Deck() != null && !Flag("deck_ready"))
{
    var d = Deck();
    var sel = Field(d, "_selected") as System.Collections.Generic.List<int>;
    int n = sel == null ? -1 : sel.Count;
    int max = (int)(Field(d, "_maxSelected") ?? -1);
    sb.Append("DECK selected=").Append(n).Append(" max=").Append(max).Append('\n');
    if (n == 0 && max > 0)
    {
        for (int i = 0; i < max; i++) ClickGo("Card" + i);
        var sel2 = Field(d, "_selected") as System.Collections.Generic.List<int>;
        sb.Append("DECK after 8 pool clicks selected=").Append(sel2 == null ? -1 : sel2.Count).Append('\n');
        action = "deck filled from pool";
    }
    else
    {
        SetFlag("deck_ready");
        action = "deck already complete";
    }
}
else if (Flag("deck_ready") && !Flag("deck_click"))
{
    var d = Deck();
    var btn = F("SaveButton") == null ? null : F("SaveButton").GetComponent<UnityEngine.UI.Button>();
    bool b0 = btn == null ? false : btn.interactable;
    var busRes = ClickN("SaveButton", 6);
    bool b1 = btn == null ? false : btn.interactable;
    bool busy = (bool)(Field(d, "_busy") ?? false);
    sb.Append("DECKBTN before.interactable=").Append(b0)
      .Append(" after6clicks.interactable=").Append(b1)
      .Append(" _busy=").Append(busy).Append('\n');
    bool ok = b0 && !b1 && busy;
    sb.Append("ASSERT save_button_disabled_in_flight=").Append(ok).Append(" (clicks=").Append(busRes).Append(")\n");
    SetFlag("deck_click");
    action = "save x6 done disabledInFlight=" + ok;
}
else if (Flag("deck_click") && !Flag("deck_verify"))
{
    var d = Deck();
    bool busy = (bool)(Field(d, "_busy") ?? false);
    var btn = F("SaveButton") == null ? null : F("SaveButton").GetComponent<UnityEngine.UI.Button>();
    bool bi = btn == null ? false : btn.interactable;
    if (busy) { action = "POLL save in flight"; }
    else
    {
        // The in-sandbox counters are impossible (log held without sharing) => the send/drop
        // counts come from tools/probes/cr-f2-count.ps1 after editor_stop. Here we only assert
        // the state this process can see: the button came back and the flag was cleared.
        sb.Append("DECKVERIFY interactableAfter=").Append(bi)
          .Append(" busyAfter=").Append(busy)
          .Append(" counts=external(cr-f2-count.ps1)\n");
        bool ok = bi && !busy;
        sb.Append("ASSERT deck_button_restored_ok=").Append(ok).Append('\n');
        SetFlag("deck_verify");
        action = "deck save verified (state) ok=" + ok;
    }
}
else if (Flag("deck_verify") && !Flag("deck_close"))
{
    action = "click.CancelButton " + ClickGo("CancelButton");
    if (action.EndsWith("CLICKED")) SetFlag("deck_close");
}
// ── 10. #3: AI battle click storm ──
else if (Flag("deck_close") && !Flag("ai_click"))
{
    var res = ClickN("AiBattleButton", 6);
    object inst = null;
    try { inst = CR.Module.Flow.AppFlow.Instance; } catch { }
    bool inflight = (bool)(Field(inst, "_aiBattleInFlight") ?? false);
    sb.Append("AIBTN clicks=").Append(res).Append(" gateInFlight=").Append(inflight).Append('\n');
    sb.Append("ASSERT ai_gate_held_after_storm=").Append(inflight).Append('\n');
    SetFlag("ai_click");
    action = "ai x6 done gateHeld=" + inflight;
}
else if (Flag("ai_click") && !Flag("ai_verify"))
{
    // Counts are external (see the NOTE at the top): here we assert the gate's state only --
    // a gate still held after the reply would mean the push took over (or never came).
    object inst = null;
    try { inst = CR.Module.Flow.AppFlow.Instance; } catch { }
    bool inflight = (bool)(Field(inst, "_aiBattleInFlight") ?? false);
    bool entering = (bool)(Field(inst, "_enteringBattle") ?? false);
    sb.Append("AIVERIFY stillInFlight=").Append(inflight).Append(" entering=").Append(entering)
      .Append(" counts=external(cr-f2-count.ps1)\n");
    // Either the push already handed the flow over to the scene-load gate, or the gate is
    // still held while we wait for that push -- both are legal at this instant.
    bool ok = inflight || entering || Has("PauseButton");
    sb.Append("ASSERT ai_flow_handoff_ok=").Append(ok).Append('\n');
    SetFlag("ai_verify");
    action = "ai verified (state) inflight=" + inflight + " entering=" + entering;
}
// ── 11. let the battle actually come up (proves the flow still completes) ──
else if (Flag("ai_verify") && !Flag("battle_up"))
{
    if (Has("PauseButton"))
    {
        sb.Append("BATTLE up (PauseButton present) frame=").Append(UnityEngine.Time.frameCount).Append('\n');
        SetFlag("battle_up");
        action = "battle up";
    }
    else
    {
        object inst = null;
        try { inst = CR.Module.Flow.AppFlow.Instance; } catch { }
        bool inflight = (bool)(Field(inst, "_aiBattleInFlight") ?? false);
        bool entering = (bool)(Field(inst, "_enteringBattle") ?? false);
        sb.Append("POLL battle inflight=").Append(inflight).Append(" entering=").Append(entering)
          .Append(" scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('\n');
        action = "POLL battle not up yet";
    }
}
else action = "POLL done " + (Flag("battle_up") ? "(battle up)" : "");

sb.Append("action=").Append(action).Append('\n');
bool done = Flag("battle_up");
sb.Append("END step ").Append(step).Append(done ? " DONE\n" : "\n");
Append(sb.ToString());
return sb.ToString();
