// CR-F1 geom probe (eval_file; ASCII only; Play mode required for the "shipped build" claim).
// Mode:  geom  -> call the SHIPPED CR.View.PlacementIndicator.IsLegalDeploy over the A/B witness set
//                 and compare it against an inline transliteration of server/game/core/arena.go CanDeploy.
// Why this exists (and why it is not a duplicate of the offline grid check):
//   .ai-tmp/hosts/CR-U2-geom-check.py  §4c judges a *python model* of the fix over the whole 0.25-tile
//   grid (that is where "diverge count == 0" is proven, exhaustively).  A model can drift from the
//   real DLL, so this probe reads the REAL compiled assembly at the witness points that discriminate
//   the two candidate shapes:
//     * BLUE on a bridge at y=15.0 with no pocket open  -> server false; the one-size `<=` shape gets
//       this WRONG (it would answer true), the shipped asymmetric shape must answer false.
//     * RED  at y=17.0 (own river bank)                 -> server true; the old `<` shape answered false.
//     * BLUE on a bridge inside an open left pocket      -> server true; the old water-only shape false.
// Nothing here changes product code; it only calls the shipped pure function.
var sb = new System.Text.StringBuilder();
const string Root = @"C:\Work\Server\f-v2\clover-project-cr";
const string ArgFile = Root + @"\.ai-tmp\test\F1-arg.txt";
const string OutFile = Root + @"\.ai-tmp\test\CR-F1-geom.out.txt";
System.Action<string> N = (t) => sb.Append(t).Append('\n');
string arg = "";
try { if (System.IO.File.Exists(ArgFile)) arg = System.IO.File.ReadAllText(ArgFile).Trim(); } catch { }
N("=== CR-F1 geom probe arg='" + arg + "' at " + System.DateTime.Now.ToString("HH:mm:ss.fff") + " ===");
N("PLAY=" + (UnityEngine.Application.isPlaying ? 1 : 0));
try
{
    // ---- inline server rule (transliteration of server/game/core/arena.go CanDeploy, milli-tiles) ----
    // enL / enR = "enemy left / right princess ALIVE" (the DeployInput flags); the pocket opens on the
    // lane whose enemy princess is dead. Tower y: enemy princess is at 25500 for BLUE, 6500 for RED.
    System.Func<int, int, int, bool, bool, bool> srv =
        (team, xm, ym, enL, enR) =>
        {
            if (xm < 0 || xm >= 18000 || ym < 0 || ym >= 32000) return false;
            bool blockedKing =
                (xm >= 7500 && xm < 10500) &&
                ((ym >= 1500 && ym < 4500) || (ym >= 27500 && ym < 30500));
            if (blockedKing) return false;
            bool inRiver = ym >= 15000 && ym < 17000;
            bool onBridge = (xm >= 2500 && xm < 4500) || (xm >= 13500 && xm < 15500);
            if (inRiver && !onBridge) return false;          // water (no spells in this witness set)
            int low = team == 0 ? 0 : 17000;
            int high = team == 0 ? 15000 : 32000;
            // nearest bridge x (ties resolve left, like arena.go NearestBridgeX)
            bool xLeftLane = System.Math.Abs(xm - 3500) <= System.Math.Abs(xm - 14500);
            bool fallenLane = xLeftLane ? !enL : !enR;
            if (fallenLane)
            {
                if (team == 0) { int ty = 25500; if (ty > high) high = ty; }
                else { int ty = 6500; if (ty + 1 < low) low = ty + 1; }
            }
            return ym >= low && ym < high;
        };

    // witness set: (label, team, xTile, yTile, enemyLAlive, enemyRAlive)
    // truth table for the server side is derived from arena.go; see the file header for why each row matters.
    var rows = new object[][]
    {
        new object[] { "A1 BLUE left bridge, river band, left pocket OPEN",  0, 3.5f, 15.5f, false, true  },
        new object[] { "A2 BLUE left bridge, river band, no pocket",         0, 3.5f, 15.5f, true,  true  },
        new object[] { "A3 BLUE left bridge, y=15.0, left pocket OPEN",      0, 3.5f, 15.0f, false, true  },
        new object[] { "A4 BLUE left bridge, y=15.0, no pocket (<= counter)",0, 3.5f, 15.0f, true,  true  },
        new object[] { "A5 BLUE right bridge, river band, right pocket OPEN",0, 14.5f, 16.5f, true, false },
        new object[] { "A6 BLUE right bridge, river band, no pocket",        0, 14.5f, 16.5f, true, true  },
        new object[] { "W1 BLUE mid river y=16.0 (water), no pocket",        0, 9.0f, 16.0f, true,  true  },
        new object[] { "W2 BLUE mid river y=16.0 (water), both pockets OPEN",0, 9.0f, 16.0f, false, false },
        new object[] { "H1 BLUE own half y=14.5",                            0, 9.0f, 14.5f, true,  true  },
        new object[] { "H2 BLUE own river bank y=15.0 off bridge (water)",   0, 9.0f, 15.0f, true,  true  },
        new object[] { "B1 RED own river bank y=17.0 (the B defect)",        1, 9.0f, 17.0f, true,  true  },
        new object[] { "B2 RED y=17.0, own princess row check stays legal",  1, 14.5f, 17.0f, true, true },
        new object[] { "W3 RED river band y=16.999 (water)",                 1, 9.0f, 16.999f, true, true },
        new object[] { "W4 RED river band y=15.0 off bridge (water)",        1, 9.0f, 15.0f, true,  true  },
        new object[] { "A7 RED left bridge, river band, BLUE-left dead",     1, 3.5f, 15.5f, false, true  },
        new object[] { "A8 RED left bridge, river band, no pocket",          1, 3.5f, 15.5f, true,  true  },
        new object[] { "H3 RED own half y=18.0",                             1, 9.0f, 18.0f, true,  true  },
    };

    int bad = 0, n = 0;
    var pit = typeof(CR.View.PlacementIndicator);
    var dit = pit.GetNestedType("DeployInput");
    var m = pit.GetMethod("IsLegalDeploy",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    N("probe: method=" + (m != null) + " DeployInput=" + (dit != null));
    foreach (var r in rows)
    {
        var label = (string)r[0];
        int team = (int)r[1]; float xt = (float)r[2]; float yt = (float)r[3];
        bool enL = (bool)r[4]; bool enR = (bool)r[5];

        var input = System.Activator.CreateInstance(dit);
        dit.GetField("MyTeam").SetValue(input, team);
        dit.GetField("EnemyLeftPrincessAlive").SetValue(input, enL);
        dit.GetField("EnemyRightPrincessAlive").SetValue(input, enR);
        dit.GetField("OwnLeftPrincessAlive").SetValue(input, false);   // 己方塔占格：本表要判的是几何（C 类差异另算）
        dit.GetField("OwnRightPrincessAlive").SetValue(input, false);

        bool client = (bool)m.Invoke(null, new object[] { input, xt, yt, false });
        bool expect = srv(team, (int)System.Math.Round(xt * 1000.0), (int)System.Math.Round(yt * 1000.0), enL, enR);
        n++;
        if (client != expect) bad++;
        N(string.Format("  [{0}] {1} | team={2} tile=({3},{4}) enL={5} enR={6} client={7} server={8}",
            client == expect ? "PASS" : "FAIL", label, team, xt, yt, enL, enR, client, expect));
    }
    N("GEOM SUMMARY rows=" + n + " mismatches=" + bad);
    if (bad > 0) N("GEOM RESULT=FAIL (shipped IsLegalDeploy disagrees with the server rule at the witness points)");
    else N("GEOM RESULT=PASS (every witness point matches server/game/core/arena.go)");
}
catch (System.Exception ex)
{
    N("MODE EX " + ex.GetType().FullName + ": " + ex.Message);
    N("STACK " + ex.StackTrace);
}
try { System.IO.File.AppendAllText(OutFile, sb.ToString()); } catch { }
return sb.ToString();
