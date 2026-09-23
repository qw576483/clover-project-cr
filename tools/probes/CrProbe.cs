// 一次性探针（run_script 用；放在 client/Temp 下，⛔ 不在 Assets 里，不进 Unity 编译）。
//
// 两条实测教训（都踩过）：
//  1) 不要用 `unity command eval --code`：那段代码要经 PowerShell → CLI → HTTP 三层引号，
//     双引号与方括号在里面必被吃掉（实测 `--timeout 需要 Int32，但收到 [UI]`）。
//  2) 也不要用 `run_script --args`：数组参数同样过不了引号层（实测 `--args 需要 JArray，但收到 [LoginButton]`）。
//  ⇒ 结论：**每个驱动动作写成一个无参静态方法**，用 `--entry CRProbe.Xxx` 点名调用。
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public static class CRProbe
{
    // ───────────────────────────── 诊断 ─────────────────────────────

    /// <summary>
    /// 时钟探针：**判断玩家循环到底有没有在走**。
    /// 实测：`Application.runInBackground=false` + 编辑器不在前台 ⇒ 循环**完全冻结**
    /// （`frameCount=1`、`time=0.00`），`Boot` 站点永不推进且无任何报错。
    /// </summary>
    public static string DumpClock()
    {
        var sb = new StringBuilder();
        sb.Append("frameCount=").Append(Time.frameCount)
          .Append(" time=").Append(Time.time.ToString("F2"))
          .Append(" timeScale=").Append(Time.timeScale)
          .Append(" runInBackground=").Append(Application.runInBackground)
          .Append(" isPlaying=").Append(Application.isPlaying).Append('\n');
        var fsm = CloverEngine.Game.Fsm;
        sb.Append("Fsm.Current=").Append(fsm == null ? "null" : fsm.Current)
          .Append(" IsRunning=").Append(CloverEngine.Game.IsRunning).Append('\n');
        sb.Append("Open: ");
        foreach (var n in new[] { "BootPanel", "LoginPanel", "RegisterPanel", "NicknamePanel",
                                  "MainMenuPanel", "DeckEditPanel", "RoomListPanel", "RoomPanel",
                                  "LoadingPanel", "HudPanel", "PausePanel", "ResultPanel", "SettingsPanel" })
            sb.Append(n).Append('=').Append(FindPanel(n) != null).Append(' ');
        return sb.ToString();
    }

    /// <summary>场景根对象 / 相机 / Canvas / 启动面板的状态（"画面空白"的原因集合）。</summary>
    public static string DumpScene()
    {
        var sb = new StringBuilder();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        sb.Append("SCENE=").Append(scene.name).Append('\n');
        sb.Append("ROOTS=");
        foreach (var go in scene.GetRootGameObjects())
            sb.Append(go.name).Append(go.activeInHierarchy ? "" : "(inactive)").Append(' ');
        sb.Append('\n');
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            sb.Append("CAM ").Append(c.name).Append(" ortho=").Append(c.orthographic)
              .Append(" size=").Append(c.orthographicSize).Append(" pos=").Append(c.transform.position)
              .Append(" enabled=").Append(c.enabled).Append(" clear=").Append(c.clearFlags).Append('\n');
        foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            sb.Append("CANVAS ").Append(cv.name).Append(" mode=").Append(cv.renderMode)
              .Append(" order=").Append(cv.sortingOrder).Append(" active=").Append(cv.gameObject.activeInHierarchy).Append('\n');
            var sc = cv.GetComponent<CanvasScaler>();
            if (sc != null) sb.Append("  scaler=").Append(sc.uiScaleMode).Append(" ref=").Append(sc.referenceResolution)
                .Append(" match=").Append(sc.matchWidthOrHeight).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>运行时 UI 节点树（核验 `by clover-engine` 真的挂在启动画面上）。</summary>
    public static string DumpUi()
    {
        var sb = new StringBuilder();
        var root = GameObject.Find("[UI]");
        if (root == null) return "NO ROOT '[UI]'";
        foreach (Transform layer in root.transform)
        {
            sb.Append('[').Append(layer.name).Append("] ");
            foreach (Transform panel in layer)
            {
                sb.Append(panel.name).Append(" { ");
                foreach (Transform child in panel)
                {
                    sb.Append(child.name).Append(' ');
                    foreach (Transform grand in child) sb.Append('(').Append(grand.name).Append(") ");
                }
                sb.Append("} ");
            }
        }
        return sb.ToString();
    }

    /// <summary>当前打开面板上的可交互控件（按钮/输入框名字），供"点名"驱动。</summary>
    public static string ListControls()
    {
        var sb = new StringBuilder();
        var root = GameObject.Find("[UI]");
        if (root == null) return "NO [UI] ROOT";
        foreach (Transform layer in root.transform)
        {
            foreach (Transform panel in layer)
            {
                var buttons = panel.GetComponentsInChildren<Button>(true);
                var fields = panel.GetComponentsInChildren<InputField>(true);
                if (buttons.Length == 0 && fields.Length == 0) continue;
                sb.Append("PANEL ").Append(panel.name).Append(" active=").Append(panel.gameObject.activeInHierarchy).Append('\n');
                foreach (var b in buttons)
                    sb.Append("  BTN ").Append(b.name).Append(" interactable=").Append(b.interactable).Append('\n');
                foreach (var f in fields)
                    sb.Append("  FIELD ").Append(f.name).Append(" text=\"").Append(f.text ?? "").Append("\"\n");
            }
        }
        return sb.ToString();
    }

    // ───────────────────────────── 冻结 / 强制 ─────────────────────────────

    /// <summary>
    /// 给 6 条业务推送都挂一个"到达即留痕"的处理器 —— 用来把问题**二元切开**：
    /// 是"推送根本没到客户端"，还是"到了但没人处理"。
    /// 同时打印连接状态（`IsConnected` / `IsChannelEncrypted`），排除"其实已经断了"。
    /// </summary>
    public static string TracePushes()
    {
        var net = CloverEngine.Game.Net;
        var ids = new uint[]
        {
            CR.Def.MsgDef.PushRoomList, CR.Def.MsgDef.PushRoomState, CR.Def.MsgDef.PushBattleStart,
            CR.Def.MsgDef.PushBattleSnapshot, CR.Def.MsgDef.PushBattleEvent, CR.Def.MsgDef.PushBattleEnd,
        };
        foreach (var id in ids)
        {
            var captured = id;
            CloverEngine.Game.OnMsg(captured, ctx =>
            {
                CloverEngine.Game.Logger?.Info("Probe",
                    "PUSH 到达 msgID=" + captured + " len=" + ctx.BodyLength);
            });
        }
        return "tracers installed=6  IsConnected=" + (net != null && net.IsConnected)
             + " IsChannelEncrypted=" + (net != null && net.IsChannelEncrypted)
             + " SendEnabled=" + (net != null && net.SendEnabled)
             + " Session=" + (net != null && net.Session != null ? net.Session.Account : "null");
    }

    /// <summary>
    /// 直接经**引擎 API**（不经 UI）请求一次人机对战，用来排除"面板那条路"的问题。
    /// 走 `MsgDef.AiBattleStart`，与 `AppFlow.RequestStartAiBattle` 同一条 C2S。
    /// </summary>
    public static string SendAiBattleDirect()
    {
        var net = CloverEngine.Game.Net;
        if (net == null) return "Net null";
        var deck = new CR.Def.GetDeckReply();
        var t = net.Call<CR.Def.AiBattleStartReply>(CR.Def.MsgDef.AiBattleStart,
            new CR.Def.AiBattleStartReq { deck = new int[0] });
        return "sent AiBattleStart (deck=empty → 服务端回落档案卡组)";
    }

    /// <summary>
    /// 对局渲染诊断：**"逻辑已建、画面空白"** 只能由这几种原因之一造成，一次全取。
    /// 逐条给：层次位置（是不是建到了别的场景/父节点）、active、SpriteRenderer 的
    /// enabled / sprite 是否为 null / 世界包围盒 / sortingOrder / 颜色 / 缩放、以及相机参数。
    /// </summary>
    public static string DumpBattle()
    {
        var sb = new StringBuilder();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        sb.Append("SCENE=").Append(scene.name).Append('\n');

        sb.Append("--- ROOT OBJECTS ---\n");
        foreach (var go in scene.GetRootGameObjects())
        {
            sb.Append("  ").Append(go.name).Append(" active=").Append(go.activeInHierarchy)
              .Append(" children=").Append(go.transform.childCount)
              .Append(" pos=").Append(go.transform.position).Append('\n');
            for (var i = 0; i < go.transform.childCount && i < 12; i++)
            {
                var ch = go.transform.GetChild(i);
                sb.Append("      - ").Append(ch.name).Append(" active=").Append(ch.gameObject.activeInHierarchy)
                  .Append(" pos=").Append(ch.position).Append(" scale=").Append(ch.lossyScale).Append('\n');
            }
        }

        var ddo = GameObject.Find("[DontDestroyOnLoad]");
        sb.Append("DontDestroyOnLoad root found=").Append(ddo != null).Append('\n');

        sb.Append("--- SPRITE RENDERERS ---\n");
        var srs = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        sb.Append("  count=").Append(srs.Length).Append('\n');
        var shown = 0;
        foreach (var sr in srs)
        {
            if (shown++ >= 20) { sb.Append("  ...(truncated)\n"); break; }
            var b = sr.bounds;
            sb.Append("  ").Append(sr.name)
              .Append(" enabled=").Append(sr.enabled)
              .Append(" active=").Append(sr.gameObject.activeInHierarchy)
              .Append(" sprite=").Append(sr.sprite == null ? "NULL" : sr.sprite.name)
              .Append(" order=").Append(sr.sortingOrder)
              .Append(" layer=").Append(sr.sortingLayerName)
              .Append(" color=").Append(sr.color)
              .Append(" pos=").Append(sr.transform.position)
              .Append(" boundsC=").Append(b.center).Append(" boundsS=").Append(b.size)
              .Append('\n');
        }

        sb.Append("--- CAMERA ---\n");
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            sb.Append("  ").Append(c.name).Append(" ortho=").Append(c.orthographic)
              .Append(" size=").Append(c.orthographicSize)
              .Append(" pos=").Append(c.transform.position)
              .Append(" depth=").Append(c.depth)
              .Append(" cullingMask=").Append(c.cullingMask)
              .Append(" enabled=").Append(c.enabled)
              .Append(" clear=").Append(c.clearFlags)
              .Append(" bg=").Append(c.backgroundColor)
              .Append(" pixelRect=").Append(c.pixelRect)
              .Append('\n');
        }

        // 屏幕上一共几个 Canvas（Overlay 会盖在世界之上，但它是透明的话世界该露出来）
        sb.Append("--- CANVAS ---\n");
        foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            sb.Append("  ").Append(cv.name).Append(" mode=").Append(cv.renderMode)
              .Append(" order=").Append(cv.sortingOrder)
              .Append(" active=").Append(cv.gameObject.activeInHierarchy).Append('\n');

        return sb.ToString();
    }

    /// <summary>
    /// **隔离测序列化层**（把网络与业务完全排除，只看 `JsonUtility` 能不能解我们的 DTO）。
    ///
    /// 背景：客户端 `Serializer.Deserialize&lt;T&gt;` 走的是 `JsonUtility.FromJson&lt;T&gt;`（`Contracts.cs:145-148`）。
    /// `JsonUtility` 对**没有 `[System.Serializable]` 的普通类**会**静默**返回一个字段全默认的对象
    /// （不抛异常）⇒ 表现就是"服务端明明下发了 60 张，客户端说卡池为空"。
    /// 这里同时打印 `Type.IsSerializable`，把"是不是缺特性"一次性钉死。
    /// </summary>
    public static string TestJson()
    {
        var sb = new StringBuilder();

        sb.Append("Type.IsSerializable: GetCardPoolReply=").Append(typeof(CR.Def.GetCardPoolReply).IsSerializable)
          .Append(" CardInfo=").Append(typeof(CR.Def.CardInfo).IsSerializable)
          .Append(" SetNicknameReply=").Append(typeof(CR.Def.SetNicknameReply).IsSerializable)
          .Append(" BattleSnapshot=").Append(typeof(CR.Def.BattleSnapshot).IsSerializable)
          .Append(" EntitySnapshot=").Append(typeof(CR.Def.EntitySnapshot).IsSerializable)
          .Append('\n');

        const string json = "{\"cards\":[{\"id\":7,\"key\":\"knight\",\"name_cn\":\"骑士\",\"name_en\":\"Knight\",\"type\":1,\"rarity\":0,\"elixir\":3,\"arena\":0,\"icon\":\"i\"}]}";
        var r1 = JsonUtility.FromJson<CR.Def.GetCardPoolReply>(json);
        sb.Append("FromJson<GetCardPoolReply>(1 card): obj=").Append(r1 == null ? "NULL" : "ok")
          .Append(" cards=").Append(r1 == null || r1.cards == null ? "NULL" : r1.cards.Length.ToString());
        if (r1 != null && r1.cards != null && r1.cards.Length > 0)
            sb.Append(" [0].id=").Append(r1.cards[0].id).Append(" [0].name_cn=").Append(r1.cards[0].name_cn);
        sb.Append('\n');

        var r2 = JsonUtility.FromJson<CR.Def.SetNicknameReply>("{\"ok\":true,\"nickname\":\"x\",\"err\":\"\"}");
        sb.Append("FromJson<SetNicknameReply>: ok=").Append(r2 == null ? "NULL" : r2.ok.ToString())
          .Append(" nick=").Append(r2 == null ? "NULL" : r2.nickname)
          .Append('\n');

        // 顺便把"实际收到的卡池回包"也拿一次（区分：是 DTO 问题还是真的没收到）
        try
        {
            var t = CloverEngine.Game.Net.Call<CR.Def.GetCardPoolReply>(CR.Def.MsgDef.GetCardPool, new CR.Def.GetCardPoolReq());
            if (t.Wait(5000))
            {
                var rep = t.Result;
                sb.Append("LIVE Call<GetCardPoolReply>: null=").Append(rep == null)
                  .Append(" cards=").Append(rep == null || rep.cards == null ? "NULL" : rep.cards.Length.ToString());
            }
            else sb.Append("LIVE Call<GetCardPoolReply>: TIMEOUT 5s");
        }
        catch (System.Exception e)
        {
            sb.Append("LIVE Call<GetCardPoolReply> threw: ").Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 通道加密（AES-GCM）平台能力探针。
    ///
    /// 背景：实机日志里有 `[Crypto] channel encrypt failed: Operation is not supported on this platform`
    /// 和 `IsChannelEncrypted=False`，但**登录链路正常**。要判定的是「到底能不能用 AES-GCM」：
    /// 引擎的 `SessionCrypto.IsSupported` 是一次真加解密自检（不支持就**不声明** encrypt ⇒ 走明文），
    /// 而错误又出现在真正的 Encrypt 里 —— 两者矛盾。这里直接对 .NET 的 AesGcm 做同一件事，
    /// 把"平台到底支不支持"从引擎封装里剥出来单独看。
    /// </summary>
    public static string TestCrypto()
    {
        var sb = new StringBuilder();
        var net = CloverEngine.Game.Net;
        sb.Append("net.IsChannelEncrypted=").Append(net != null && net.IsChannelEncrypted)
          .Append(" net.IsConnected=").Append(net != null && net.IsConnected).Append('\n');

        // .NET 的 AesGcm 自检（与引擎 SessionCrypto.IsSupported 同一手法）
        try
        {
            var key = new byte[32];
            var nonce = new byte[12];
            var plain = new byte[] { 1, 2, 3, 4 };
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using (var gcm = new System.Security.Cryptography.AesGcm(key))
                gcm.Encrypt(nonce, plain, cipher, tag);
            sb.Append("AesGcm.Encrypt: OK（本平台支持）\n");
        }
        catch (System.Exception e)
        {
            sb.Append("AesGcm.Encrypt: THREW ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }

        // 反射问一问引擎的 IsSupported（internal，只能反射拿）
        try
        {
            var t = System.Type.GetType("CloverEngine.SessionCrypto, CloverEngine.Network")
                 ?? System.Type.GetType("CloverEngine.SessionCrypto");
            if (t == null) sb.Append("SessionCrypto type: NOT FOUND by name\n");
            else
            {
                var p = t.GetProperty("IsSupported", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                sb.Append("SessionCrypto.IsSupported=").Append(p == null ? "no such property" : p.GetValue(null).ToString()).Append('\n');
            }
        }
        catch (System.Exception e) { sb.Append("reflect IsSupported threw: ").Append(e.Message).Append('\n'); }

        sb.Append("RuntimeInformation=").Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)
          .Append(" OSArch=").Append(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
          .Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// 第二个玩家用的「协议级」驱动入口（不经 UI，直接走 C2S + 等回包），
    /// 用来在没有第二个客户端实例时，把"两个真人在同一房间"这条链跑起来。
    /// 每个动作都是**无参**方法（`--args` 的引号过不了 PowerShell 那层）。
    /// </summary>
    public static string RoomCreateNamed()
    {
        var net = CloverEngine.Game.Net;
        if (net == null) return "Net null";
        var t = net.Call<CR.Def.RoomCreateReply>(CR.Def.MsgDef.RoomCreate,
            new CR.Def.RoomCreateReq { name = RoomNameForProbe });
        t.ContinueWith(r =>
        {
            var rep = r.IsFaulted ? null : r.Result;
            CloverEngine.Game.Logger?.Info("Probe",
                "RoomCreate 回包 ok=" + (rep != null ? rep.ok.ToString() : "EXC") +
                " room_id=" + (rep != null ? rep.room_id : r.Exception?.GetBaseException().Message));
        });
        return "RoomCreate sent (name=" + RoomNameForProbe + ")";
    }

    /// <summary>探针建房用的房名。</summary>
    public const string RoomNameForProbe = "探针房";

    // ── 房间链路的真 UI 路径驱动（按钮名取自 RoomListPanel/RoomPanel 的源码）──

    /// <summary>主菜单 → 房间列表。</summary>
    public static string OpenRoomList() { return Click("RoomListButton"); }

    /// <summary>房间列表 → 点「创建」。</summary>
    public static string ClickCreateRoom() { return Click("CreateButton"); }

    /// <summary>房间列表 → 点「刷新」。</summary>
    public static string ClickRefreshRooms() { return Click("RefreshButton"); }

    /// <summary>房间内 → 点「准备 / 取消准备」。</summary>
    public static string ClickReady() { return Click("ReadyButton"); }

    /// <summary>房间内（房主）→ 点「开始对战」。</summary>
    public static string ClickStart() { return Click("StartButton"); }

    /// <summary>房间内 → 点「离开」。</summary>
    public static string ClickLeaveRoom() { return Click("LeaveButton"); }

    /// <summary>拉房间列表（结果打到日志，供比对面板显示）。</summary>
    public static string RoomListNow()
    {
        var net = CloverEngine.Game.Net;
        if (net == null) return "Net null";
        net.Call<CR.Def.RoomListReply>(CR.Def.MsgDef.RoomList, new CR.Def.RoomListReq())
            .ContinueWith(r =>
            {
                if (r.IsFaulted) { CloverEngine.Game.Logger?.Error("Probe", "RoomList 异常: " + r.Exception?.GetBaseException().Message); return; }
                var rep = r.Result;
                var n = rep != null && rep.rooms != null ? rep.rooms.Length : 0;
                var sb = new StringBuilder();
                for (var i = 0; i < n; i++)
                    sb.Append('[').Append(rep.rooms[i].room_id).Append('/').Append(rep.rooms[i].name)
                      .Append('/').Append(rep.rooms[i].cur).Append("人] ");
                CloverEngine.Game.Logger?.Info("Probe", "RoomList 回包 " + n + " 个房间: " + sb);
            });
        return "RoomList sent";
    }

    /// <summary>解冻玩家循环（见 DumpClock 注释里的实测）。</summary>
    public static string Unpause()
    {
        Application.runInBackground = true;
        return "runInBackground=" + Application.runInBackground;
    }

    /// <summary>把 FSM 直接推到某站点（`Boot` 卡住时的兜底）。</summary>
    public static string ForceStation(string station)
    {
        var fsm = CloverEngine.Game.Fsm;
        if (fsm == null) return "Fsm null";
        var before = fsm.Current;
        fsm.Force(station);
        return "Fsm " + before + " -> " + fsm.Current;
    }

    // ───────────────────────────── 驱动动作（每个都无参） ─────────────────────────────

    /// <summary>点登录（面板已按 `Cfg.Account` 预填账号密码，直接点即可）。</summary>
    public static string ClickLogin() { return Click("LoginButton"); }

    /// <summary>创角：填昵称并提交（`NicknamePanel` 的 `NicknameInput` + `SubmitButton`）。</summary>
    public static string SetNickAndSubmit()
    {
        var f = Fill("NicknameInput", NickToUse);
        var c = Click("SubmitButton");
        return f + " | " + c;
    }

    /// <summary>创角用的昵称（本项目自定；服务端上限 16 字符，见 `NicknamePanel`）。</summary>
    public const string NickToUse = "克洛弗";

    /// <summary>点注册（注册成功后按面板逻辑直接登录）。</summary>
    public static string ClickRegister() { return Click("RegisterButton"); }

    /// <summary>主菜单：进人机对战（用户点名的入口之一）。</summary>
    public static string ClickAiBattle() { return Click("AiBattleButton"); }

    /// <summary>主菜单：进房间列表（用户点名的入口）。</summary>
    public static string ClickRoomList() { return Click("RoomListButton"); }

    /// <summary>主菜单：进卡组编辑（真实按钮名是 `DeckButton`）。</summary>
    public static string ClickDeckEdit() { return Click("DeckButton"); }

    /// <summary>
    /// 卡组编辑整条真 UI 路径：打开面板 → 点前 8 张卡格（`Card0`..`Card59`）→ 点保存（`SaveButton`）。
    /// 走的是面板自己的 `onClick`，与玩家手点是同一条路径（校验、发 C2S、失败提示都照跑）。
    /// </summary>
    public static string SelectEightAndSave()
    {
        var sb = new StringBuilder();
        sb.Append(Click("DeckButton")).Append(" | ");
        for (var i = 0; i < 8; i++) sb.Append(Click("Card" + i)).Append("; ");
        sb.Append(" | ").Append(Click("SaveButton"));
        return sb.ToString();
    }

    /// <summary>点一个按钮（按名称在整个 UI 树里找），走它自己的 `onClick.Invoke()`。</summary>
    public static string Click(string buttonName)
    {
        var root = GameObject.Find("[UI]");
        if (root == null) return "NO [UI] ROOT";
        foreach (var b in root.GetComponentsInChildren<Button>(true))
        {
            if (b.name != buttonName) continue;
            if (!b.interactable) return "FOUND but NOT interactable: " + buttonName;
            b.onClick.Invoke();
            return "CLICKED " + buttonName;
        }
        return "BUTTON NOT FOUND: " + buttonName;
    }

    /// <summary>填第一个名字匹配的输入框。</summary>
    public static string Fill(string fieldName, string text)
    {
        var root = GameObject.Find("[UI]");
        if (root == null) return "NO [UI] ROOT";
        foreach (var f in root.GetComponentsInChildren<InputField>(true))
        {
            if (f.name != fieldName) continue;
            f.text = text;
            return "FILLED " + fieldName + " = " + text;
        }
        return "FIELD NOT FOUND: " + fieldName;
    }

    // ───────────────────────────── 内部 ─────────────────────────────

    private static GameObject FindPanel(string typeName)
    {
        var root = GameObject.Find("[UI]");
        if (root == null) return null;
        foreach (Transform layer in root.transform)
            foreach (Transform panel in layer)
                if (panel.name == typeName || panel.name == typeName + "(Clone)") return panel.gameObject;
        return null;
    }
}
