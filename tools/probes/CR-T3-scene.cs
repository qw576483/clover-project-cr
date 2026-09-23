// CR-T3: report the active scene and (edit mode only) open Assets/Scenes/Boot.unity.
// Reason: Bootstrap lives in Boot.unity. Entering Play while the editor sits on Battle01/Main
// gives a running play session with NO engine (Game.Fsm == null, Game.UI == null) - the observed
// "eval cannot reach the running app" state. Edit mode only; ASCII only.
var sb = new System.Text.StringBuilder();
var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.Append("active=").Append(active.name).Append(" path=").Append(active.path).Append('\n');
if (UnityEngine.Application.isPlaying)
{
    sb.Append("inPlay=true (scene not changed)\n");
}
else
{
    var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
    sb.Append("opened=").Append(s.name).Append(" isLoaded=").Append(s.isLoaded)
      .Append(" nowActive=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('\n');
}
return sb.ToString();
