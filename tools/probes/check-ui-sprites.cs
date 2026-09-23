// T4c - IN-EDITOR check of every landed original-UI sprite.
//
// Run with (the CLI needs the project dir as cwd, and --project-path on every call):
//   cd <root>\client
//   unity command eval_file --file <root>\tools\probes\check-ui-sprites.cs `
//         --project-path <root>\client --timeout 180
//
// WHY THIS FILE EXISTS (judgement asset, committed):
//   `Core/ResPaths.cs` registers the original-UI frames that were landed under
//   `Sprites/Ui/<purpose>/<source atlas>/frame_NNN.png` (landed by
//   `tools/probes/copy-ui-assets.py`, which crops each frame to its element bbox).
//   Three things can only be
//   judged INSIDE the editor and are invisible on the file system:
//     1. whether such a path resolves to a loaded `Sprite` at all;
//     2. whether that texture exposes EXACTLY ONE sprite -- the original frames are
//        small elements on a huge transparent canvas, and Unity's automatic slicer
//        splits a frame whose element is made of disconnected pieces into several
//        sprites, which leaves the path pointing at only part of the element; and
//     3. whether that one sprite covers the WHOLE element (the non-transparent
//        bounding box), not just the slicer's largest island.
//   For (2)+(3) the authority is the frame's bbox in
//   `.ai-tmp/screenshots/ui-index-manifest.tsv`, which `tools/probes/ui-index.ps1`
//   measured with an alpha>8 scan.  The landed texture is CROPPED to that bbox by
//   `tools/probes/copy-ui-assets.py`, so the expected sprite rect is
//   `(0, 0, bboxW, bboxH)` and its pixel size must equal the bbox size -- a +-2 px
//   tolerance absorbs the alpha-threshold difference between the two scans.
//   (Measured, not assumed: with the raw uncropped frames copied in instead, the
//   sprites came back 27% too small -- `TextureImporter.maxTextureSize = 2048`
//   downscales the 1663x2810 `ui_out` canvas by 2048/2810 = 0.729.)
//   Delete this file and none of this can be re-judged => tools/probes/.
//
// The import settings are read BY REFLECTION on purpose: TextureImporter renames and
// removes members between Unity versions (`spriteMode` -> `spriteImportMode`,
// `spritesheet` removed, `spritePixelsToUnits` obsolete ...), and a hard reference to
// any of them would turn this probe into a compile error on the next editor upgrade.
// A member missing on both sides reads as "n/a" and so cannot report a false diff.
//
// Prints one line per key: NAME<TAB>path<TAB>spriteRect<TAB>spriteCount, then the
// summary, then every problem (null sprite / sprite count != 1 / rect != bbox /
// importer settings differing from Ui/loading_bg.png).

const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public
    | System.Reflection.BindingFlags.NonPublic
    | System.Reflection.BindingFlags.Instance
    | System.Reflection.BindingFlags.Static;

const int TOL = 2;
var sb = new System.Text.StringBuilder();

// ---- the manifest: frame -> (canvasH, bbox x,y,w,h) -------------------------
var repoRoot = System.IO.Directory.GetParent(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName).FullName;
var manifestPath = System.IO.Path.Combine(repoRoot, ".ai-tmp", "screenshots", "ui-index-manifest.tsv");
var bbox = new System.Collections.Generic.Dictionary<string, int[]>();
var manifestOk = System.IO.File.Exists(manifestPath);
if (manifestOk)
{
    var lines = System.IO.File.ReadAllLines(manifestPath, System.Text.Encoding.UTF8);
    for (var i = 0; i < lines.Length; i++)
    {
        var c = lines[i].Split('\t');
        if (c.Length < 10) { continue; }
        int f;
        if (!int.TryParse(c[1], out f)) { continue; }
        bbox[c[0] + "/" + f] = new int[]
        {
            int.Parse(c[4]), int.Parse(c[5]), int.Parse(c[6]),       // canvasH, bboxX, bboxY
            int.Parse(c[7]), int.Parse(c[8])                          // bboxW, bboxH
        };
    }
}

// ---- collect the keys through the public API (no hard-coded frame list) ------
var props = typeof(CR.ResPaths).GetProperties(F);
var names = new System.Collections.Generic.List<string>();
var paths = new System.Collections.Generic.List<string>();
foreach (var p in props)
{
    if (p.PropertyType != typeof(string)) { continue; }
    var acc = p.GetGetMethod(true);
    if (acc == null || !acc.IsStatic) { continue; }
    string v = null;
    try { v = (string)p.GetValue(null, null); } catch { }
    if (string.IsNullOrEmpty(v)) { continue; }
    if (!v.StartsWith("Sprites/Ui/")) { continue; }
    if (v.IndexOf("/frame_") < 0) { continue; }        // skip the folder constants
    names.Add(p.Name);
    paths.Add(v);
}

// ---- importer settings to compare, by name ---------------------------------
var settingNames = new string[]
{
    "textureType", "spriteImportMode", "spriteMode", "filterMode", "maxTextureSize",
    "alphaIsTransparency", "mipmapEnabled", "wrapMode", "isReadable", "npotScale",
    "compressionQuality", "textureCompression", "spritePixelsPerUnit",
    "spriteMeshType", "spriteExtrude", "alphaUsage"
};
var tips = typeof(UnityEditor.TextureImporter);
var info = new System.Collections.Generic.Dictionary<string, System.Reflection.PropertyInfo>();
foreach (var s in settingNames)
{
    var pi = tips.GetProperty(s, F);
    if (pi != null && pi.CanRead) { info[s] = pi; }
}
System.Func<object, string> read = delegate (object o)
{
    var b = new System.Text.StringBuilder();
    foreach (var s in settingNames)
    {
        System.Reflection.PropertyInfo pi;
        if (!info.TryGetValue(s, out pi)) { b.Append(s).Append("=n/a "); continue; }
        object val;
        try { val = pi.GetValue(o, null); } catch { val = null; }
        b.Append(s).Append('=').Append(val == null ? "null" : val.ToString()).Append(' ');
    }
    return b.ToString();
};

var refImp = UnityEditor.AssetImporter.GetAtPath("Assets/Resources/Sprites/Ui/loading_bg.png") as UnityEditor.TextureImporter;
var refSet = refImp == null ? "" : read(refImp);

// ---- walk every key --------------------------------------------------------
var loaded = 0;
var nulls = new System.Collections.Generic.List<string>();
var multi = new System.Collections.Generic.List<string>();
var rectBad = new System.Collections.Generic.List<string>();
var noBbox = new System.Collections.Generic.List<string>();
var diffs = new System.Collections.Generic.List<string>();
var noImp = new System.Collections.Generic.List<string>();

for (var i = 0; i < names.Count; i++)
{
    var key = names[i];
    var path = paths[i];
    var assetPath = "Assets/Resources/" + path + ".png";
    var sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>(path);
    var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
    var spriteCount = 0;
    foreach (var o in all) { if (o is UnityEngine.Sprite) { spriteCount++; } }
    var rect = sprite == null ? "-" : ((int)sprite.rect.x + "," + (int)sprite.rect.y + ","
               + (int)sprite.rect.width + "x" + (int)sprite.rect.height);
    if (sprite == null) { nulls.Add(key + " (" + path + ")"); } else { loaded++; }
    if (spriteCount != 1) { multi.Add(key + "=" + spriteCount); }

    // expected rect, straight from the manifest
    var seg = path.Split('/');                       // Sprites,Ui,<purpose>,<srcDir>,frame_NNN
    var srcDir = seg[3];
    var frame = int.Parse(seg[4].Substring("frame_".Length));
    var mk = srcDir + "/" + frame;
    if (!bbox.ContainsKey(mk)) { noBbox.Add(key + " (" + mk + ")"); }
    else if (sprite != null)
    {
        // the landed file is the crop itself => origin must be 0,0 and the size must
        // be the frame's bbox size
        var b = bbox[mk];
        var bad = System.Math.Abs((int)sprite.rect.x) > TOL
               || System.Math.Abs((int)sprite.rect.y) > TOL
               || System.Math.Abs((int)sprite.rect.width - b[3]) > TOL
               || System.Math.Abs((int)sprite.rect.height - b[4]) > TOL;
        if (bad)
        {
            rectBad.Add(key + " got=" + rect + " want=0,0," + b[3] + "x" + b[4]
                        + " (bbox in the original canvas: " + b[1] + "," + b[2] + ")");
        }
    }

    var imp = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
    if (imp == null) { noImp.Add(key); }
    else if (read(imp) != refSet) { diffs.Add(key); }

    sb.Append(key).Append('\t').Append(path).Append('\t').Append(rect).Append('\t').Append(spriteCount).Append('\n');
}

sb.Append('\n');
sb.Append("SUMMARY keys=").Append(names.Count)
  .Append(" loadedAsSprite=").Append(loaded)
  .Append(" nullSprite=").Append(nulls.Count)
  .Append(" spriteCountNotOne=").Append(multi.Count)
  .Append(" rectNotEqualToBbox=").Append(rectBad.Count)
  .Append(" noManifestRow=").Append(noBbox.Count)
  .Append(" importerSettingDiffs=").Append(diffs.Count)
  .Append(" noImporter=").Append(noImp.Count)
  .Append(" manifest=").Append(manifestOk).Append('\n');
sb.Append("reference Ui/loading_bg.png: ").Append(refSet).Append('\n');
if (nulls.Count > 0) { sb.Append("NULL-SPRITE: ").Append(string.Join(" | ", nulls.ToArray())).Append('\n'); }
if (multi.Count > 0) { sb.Append("SPRITE-COUNT!=1: ").Append(string.Join(" | ", multi.ToArray())).Append('\n'); }
if (rectBad.Count > 0) { sb.Append("RECT!=BBOX: ").Append(string.Join(" | ", rectBad.ToArray())).Append('\n'); }
if (noBbox.Count > 0) { sb.Append("NO-MANIFEST-ROW: ").Append(string.Join(" | ", noBbox.ToArray())).Append('\n'); }
if (noImp.Count > 0) { sb.Append("NO-IMPORTER: ").Append(string.Join(" | ", noImp.ToArray())).Append('\n'); }
for (var i = 0; i < diffs.Count; i++) { sb.Append("IMPDIFF ").Append(diffs[i]).Append('\n'); }

return sb.ToString();
