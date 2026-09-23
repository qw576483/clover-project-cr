// AH1 driver BODY for `unity command eval_file file=<abs>`. ASCII-only. Play Mode only. Must `return`.
// Purpose: prove the画质 persistence key is unified with the engine key ("quality_level").
// Mode comes from the step file; each Play session runs exactly one mode:
//   mode 0 (session A): (1) T1 - set quality High through the REAL chain
//                       Emit(Events.Settings.QualityRequest,High)  => engine key must equal 2.
//                       (2) simulate the engine's own downgrade with the exact call its
//                       auto-downgrade makes (Quality.cs:242 SetLevel) => engine key 0 (Low).
//                       (3) inject the stale PROJECT key video.quality_tier=High.
//                           That is byte-for-byte the state the pre-fix code produced on disk
//                           (project wrote its own key on the user's choice; engine wrote only
//                           its own key on downgrade) => disk: new=0, old=2.
//   mode 1 (session B): restart. BEFORE must read Level=Low (the downgraded value) and must have
//                       deleted the stale project key => core criterion. Then build a legacy
//                       archive: delete the engine key, keep only video.quality_tier=Low.
//   mode 2 (session C): restart. Must have migrated (engine key=0, old key gone, Level=Low).
// Keys are read through the engine/project entry (Game.Setting), never raw PlayerPrefs.

var sb = new System.Text.StringBuilder();
const string StepFile = @"C:\Work\Server\f-v2\clover-project-cr\.ai-tmp\test\AH1-step.txt";
const string KNew = "quality_level";
const string KOld = "video.quality_tier";

int mode = -1;
try { if (System.IO.File.Exists(StepFile)) int.TryParse(System.IO.File.ReadAllText(StepFile).Trim(), out mode); }
catch (System.Exception ex) { sb.Append("stepfile-read EX ").Append(ex.Message).Append('\n'); }

sb.Append("=== AH1 mode ").Append(mode).Append(" ===\n");
if (!UnityEngine.Application.isPlaying) { sb.Append("ABORT not in play mode\n"); return sb.ToString(); }

var setting = CloverEngine.Game.Setting;
var quality = CloverEngine.Game.Quality;
if (setting == null) { sb.Append("ABORT Game.Setting NULL\n"); return sb.ToString(); }
if (quality == null) { sb.Append("ABORT Game.Quality NULL\n"); return sb.ToString(); }

System.Func<string> Dump = () =>
{
    int nq = setting.Get(KNew, -999);
    int no = setting.Get(KOld, -999);
    return "Level=" + quality.Level + "(" + (int)quality.Level + ")" +
           " engine[" + KNew + "]=" + nq +
           " legacy[" + KOld + "]=" + no;
};

sb.Append("dev=").Append(UnityEngine.SystemInfo.graphicsDeviceName)
  .Append(" scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
  .Append(" frame=").Append(UnityEngine.Time.frameCount).Append('\n');
sb.Append("BEFORE  ").Append(Dump()).Append('\n');

if (mode == 0)
{
    CloverEngine.Game.Event.Emit(CR.Events.Settings.QualityRequest, (int)CloverEngine.QualityTier.High);
    sb.Append("T1 Emit(QualityRequest,High) -> ").Append(Dump()).Append('\n');

    quality.SetLevel(CloverEngine.QualityTier.Low);
    sb.Append("SIM engine downgrade SetLevel(Low) -> ").Append(Dump()).Append('\n');

    setting.Set(KOld, (int)CloverEngine.QualityTier.High);
    setting.Save();
    sb.Append("INJECT stale legacy key -> ").Append(Dump()).Append('\n');
    sb.Append("PHASE_A_DONE\n");
}
else if (mode == 1)
{
    sb.Append("CORE_DOWNGRADE_SURVIVED=")
      .Append(quality.Level == CloverEngine.QualityTier.Low ? "YES" : "NO").Append('\n');
    sb.Append("STALE_LEGACY_KEY_REMOVED=")
      .Append(setting.Get(KOld, -999) < 0 ? "YES" : "NO").Append('\n');
    setting.Delete(KNew);
    setting.Set(KOld, (int)CloverEngine.QualityTier.Low);
    setting.Save();
    sb.Append("MADE_LEGACY ").Append(Dump()).Append('\n');
    sb.Append("PHASE_B_DONE\n");
}
else if (mode == 2)
{
    sb.Append("CORE_LEGACY_MIGRATED=")
      .Append((quality.Level == CloverEngine.QualityTier.Low && setting.Get(KNew, -999) == 0
               && setting.Get(KOld, -999) < 0) ? "YES" : "NO").Append('\n');
    sb.Append("PHASE_C_DONE\n");
}
return sb.ToString();
