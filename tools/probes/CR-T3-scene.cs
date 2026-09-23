// CR-T3 / CR-V1: report the active scene and (edit mode only) open the BOOTSTRAP scene, then Play.
// !! SCENE CHANGED Boot.unity -> Main.unity by CR-V1 (2026-09-23; AS-OF 11:27:09; CR.dll sha256_16
//    = 0F4D2C8A75D5D318). Read the evidence below before changing it back. !!
// Evidence: entering Play while Boot.unity was active STALLED BEFORE ANY PANEL - CR-V1 chain #1
// (11:20:28-11:23:16) polled 22 times and every single poll returned "scene=Boot" with NO
// LoginButton / AiBattleButton / PauseButton present, while client/logs/2026-09-23.log had ZERO
// lines between 11:20:39.534 ([AppFlow] network connected) and 11:23:16.515 (engine shutting down).
// The SAME chain with Main.unity active SUCCEEDED in one pass (login -> AI battle -> arena node
// dump -> 2 shots @1080x1920).
// !! ATTRIBUTION IS STILL OPEN: both scene files contain a Bootstrap component, so the difference is
//    NOT proven to be "which scene" alone. The observation is what is encoded here.
// Boot.unity was correct for CR-T3's build (2026-09-22); it is stale/harmful for this one.
// Edit mode only; ASCII only.
//
// NOTE (2026-09-23 11:48 by CR-V1): the "Boot vs Main" attribution above is REFUTED by the chain at
//   11:45:42 (same assembly 0F4D2C8A75D5D318, scene=Main, 22 polls, NO panel ever appeared, roots
//   were EventSystem|[Sound]|[CloverEngine]|BattleViewRoot|[UI]|Main Camera|Bootstrap).
//   Boot and Main BOTH can stall. The section above is kept as-is because it is what was observed
//   at 11:20-11:27; only its ATTRIBUTION is withdrawn.
//   Current best hypothesis (team-lead): a cross-session static AppFlow.Instance survives (domain
//   reload off) => Bootstrap takes the "bus changed, re-subscribe only" branch and never GoTo(Boot)
//   => stuck before panel #1. NOT proven yet; a diagnosis piece is queued to read AppFlow.fsm and
//   whether Instance survives across Play rounds.
// !! AND IT IS INTERMITTENT, NOT INEVITABLE: CR-V2 entered Play successfully 41 s after that failure
//   (its chain START 11:49:17 -> play entered 11:50:08, battle reached). So "Main always stalls" is
//   ALSO wrong. Cheapest self-rescue per team-lead: stop+play once before each chain, then report
//   honestly if it still stalls -- do NOT retry in a loop. Also note entry point 11:20:28 may not
//   have had "Main" active internally; the scene open call goes through this file either way.
var sb = new System.Text.StringBuilder();
var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.Append("active=").Append(active.name).Append(" path=").Append(active.path).Append('\n');
if (UnityEngine.Application.isPlaying)
{
    sb.Append("inPlay=true (scene not changed)\n");
}
else
{
    var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
    sb.Append("opened=").Append(s.name).Append(" isLoaded=").Append(s.isLoaded)
      .Append(" nowActive=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('\n');
}
return sb.ToString();
