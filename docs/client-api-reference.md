# 客户端引擎 API 摘要（**逐字来自源码，含 `文件:行`**）

> 出处：`c:\Work\Server\f-v2\clover-client-unity-engine`（下称 `<引擎>`），由 code-explorer 逐条核对。
> **所有客户端 agent 写代码前必须读本文**，⛔ 不许凭记忆写 API。
> 包内 `Samples~/LoginFlow/LoginFlow.cs` 是最完整的接入示例（`Game.Launch` → `CloverAuth` → `CloverNet.Init` →
> 订阅 `Game.Event` / `Game.Sync` 全流程），**先读它**。

## 0. ⛔ 已确认「不存在」的东西（别再写）

| 不存在的写法 | 正确的写法 |
|---|---|
| `Game.Data` | **`Game.Table`**（`IDataTable`，`Contracts.cs:229`） |
| `Tables.Default.Xxx.Get(id)` | **`CloverTable.Get<XxxRow>("Xxx", id)`**（`Runtime/Data/CloverTable.cs:241`） |
| `Game.Scene.Current` | **`Game.Scene.CurrentScene`**（`PresentationContracts.cs:197`） |
| `Game.Timer.Once(...)` | **`Game.Timer.After(delay, cb)`**（`Timer.cs:19`） |
| `Game.Net.Call(...)`（非泛型） | **`await Game.Net.Call<T>(msgID, req)`**（`Contracts.cs:354`） |
| `Game.Res.LoadSprite/LoadPrefab/LoadTexture` | **`Game.Res.LoadAsset<T>(path, cb)`**（`Contracts.cs:1027`），`T` = `Sprite`/`Texture2D`/`GameObject` |
| `Game.Res.Load<T>(path)`（同步加载） | 只有 **`T TryGet<T>(path)`**（仅取已驻留缓存，`Contracts.cs:1072`）+ 先 `Preload` |
| `Runtime/Presentation/UI/` 目录 | 不存在；UI 控件在 **`Runtime/Presentation/UIWidgets.cs` + `UIWidgetComponents.cs`** 的 `UIFactory` |
| `Game.Fsm.OnEnter(...)` | 不是方法；`OnEnter` 是 **`RegisterState` 的参数** |
| `Game.Input.PointerPosition` | **`Game.Input.MousePosition`**（`Vector3`，`Input.cs:130`）/ `MouseDelta`（`Vector2`，`:133`） |
| `Game.Res` 自动挂载 | 必须显式 **`CloverRes.Init(root)`**，否则 `Game.Res` 恒 null |

## 1. 启动顺序（**必须严格这个次序**）

```csharp
// 1) 引擎（不联网）
Game.Launch(new GameConfig {
    ServerAddr = Cfg.Server.addr,          // 网关 TCP 口（8002）
    WsAddr     = null,                     // 原生客户端走 TCP，不用 WS
    UseTls     = Cfg.Server.tls,           // 与服务端 gateway.tcp_tls_disabled 相反
    LogDir     = "logs",
    ResourceRoot = "",                     // ⚠️ 见下 (a)
    CallTimeoutSeconds = Cfg.Server.call_timeout,
    MaxReconnectCount  = Cfg.Server.max_reconnect_count,
});
// 2) 输入 + EventSystem（★ 必须在建 UI 之前；晚了按钮全点不动）
CloverInput.Init();
// 3) 网络（显式；Launch 不联网）
CloverNet.Init(Cfg.Server.addr, string.IsNullOrEmpty(Cfg.Server.udp_addr) ? null : Cfg.Server.udp_addr);
// 4) 资源（不调 ⇒ Game.Res 恒 null，模型永远加载不出来）
CloverRes.Init(string.Empty);              // ⚠️ 见下 (a)
// 5) ★ 本项目客户端**不加载本地 tsv**：卡池/卡组数据全部走服务端协议（见下 (b)）
// 6) 账号服地址（不设 ⇒ LoginAsync/SignupAsync 抛 InvalidOperationException）
CloverAuth.AuthAddr = Cfg.Server.auth_addr;
// 7) 登录成功后
Game.Net.SetupSession(account, null, Cfg.Account.line);   // ⚠️ 见下 (c)
```

### ★ 启动顺序里四个**实测纠正**（agent-05 在引擎源码里逐条核对出来的）

**(a) `CloverRes.Init` 的参数是 `Resources` 下的子目录前缀，不是工程路径。**
`ResourceBackend` 内部按 `root + "/" + path` 拼（`Runtime/Resource/ResourceBackend.cs:227`）。
传 `"Assets/Resources"` 会让所有加载去找 `Resources/Assets/Resources/…` ⇒ **一个资源都命中不了**（且不报错）。
**本项目传 `string.Empty`**（`CloverRes.Init(string.Empty)`）。

**(b) `CloverData.InitDataTable(CloverTable.Dir)` 这条链不成立，本项目不接。**
`CloverTable.Dir` 在 `CloverTable.LoadAll(...)` 成功之前**恒为 null**（`Runtime/Data/CloverTable.cs:91`）；
且 `IDataTable` 要求行类实现 `IDataRow`，而打表产物**不实现**（`CloverTable.cs:5-11` 自己写明了）。
⇒ 客户端**不落地 tsv**，60 张卡的展示数据一律走服务端 `MsgDef.GetCardPool`。

**(c) `SetupSession` 的第二个参数（恢复凭证）在**登录时**必须传 `null`。**
登录回包里的 `session_key` 是**通道加密密钥**，不是恢复凭证；把它当恢复凭证交上去会让重连恢复会话
**token mismatch 被踢**（`Runtime/Core/Contracts.cs:304-314` + `Samples~/LoginFlow/LoginFlow.cs:206` 都写明）。

**(d) `EMsg.Login` 没有"登录成功事件"。**
`EMsg.Login` 是 C2S 请求，成败由 `await Game.Net.Call<ELoginReply>(EMsg.Login, new ELoginRequest{ token = … })`
的回包给出（`ELoginReply` 字段 `success` / `err` / `owner`，`Runtime/Network/Protocol.cs:42`）。
网络**生命周期**事件走 `CloverEvents.Net.*`（`OnConnected` / `OnDisconnected` / `OnKicked`，`Contracts.cs:390-399`）。

`GameConfig` 字段（`Game.cs:40-124`）：`ServerAddr` / `WsAddr` / `WsPath`(="/ws") / `UseTls`(=true) /
`LogDir` / `SettingDir` / `ResourceRoot` / `DataDir` / `HeartbeatIntervalMs`(=15000) /
`ConnectTimeoutMs`(=5000) / `CallTimeoutSeconds`(=10) / `MaxReconnectCount`(=5) / `UdpKeepaliveIntervalSeconds`(=10)。

## 2. `Game` 子模块（全部 `public static … { get; private set; }`，`Game.cs:149-331`）

`Logger:ILogger` · `Dispatcher:IDispatcher` · `Event:IEventBus` · `Timer:ITimer` · `Fsm:IFsm` ·
`Setting:ISetting` · `Net:INetwork` · `Http:IWebRequest` · `Sync:IWorldSync` · `CloverScene:ICloverScene` ·
`FrameRoom:IFrameRoom` · `Alert:IAlert` · `Schema:ISchemaRegistry` · `LanBrowser:ILanBrowser` ·
`Table:IDataTable` · `Localization:ILocalization` · `Res:IResourceManager` · `Entity:IEntityManager` ·
`Pool:IObjectPool` · `Map:IMapData` · `UI:IUIManager` · `Scene:ISceneManager` · `Atlas:ISpriteAtlasManager` ·
`Anim:IAnimationManager` · `Sound:ISoundManager` · `Camera:ICameraManager` · `Quality:IQualityManager` ·
`DeviceId:IDeviceIdProvider` · `Input:IInputManager`

> `Game.IsRunning`（`Game.cs:336`）。UI / Scene / Anim / Sound / Camera / Quality 由 `CloverPresentation`
> 在 Launch 钩子里**自动挂载** —— ⛔ 不要再手动 Init 它们。

## 3. 网络

```csharp
public bool IsConnected { get; }        // Contracts.cs:239
public bool IsQueued { get; }           // :250
public bool IsChannelEncrypted { get; } // :267
public bool SendEnabled { get; set; }   // :330  ← 断线/未进图时关掉上行闸门
public void Connect(string addr)                    // :280
public void Connect(string addr, string udpAddr)    // :288
public void Reconnect()                             // :293
public void Disconnect()                            // :298
public void SetupSession(string account, string resumeCredential, int line = 0) // :314
public void Send(uint msgID, object msg)            // :337
public void SendUnreliable(uint msgID, object msg)  // :344
public Task<T> Call<T>(uint msgID, object msg) where T : class  // :354
```

消息：
```csharp
public delegate void MsgHandler(NetCtx ctx);   // Contracts.cs:168
Game.OnMsg(uint msgID, MsgHandler handler);    // Game.cs:531
Game.OffMsg(uint msgID);                       // :566
public class NetCtx { uint MsgID; uint RequestID; string TraceID; byte[] Body; int BodyOffset; int BodyLength; } // :78
public T Bind<T>() where T : class;            // :115 —— 反序列化失败返回 null，不抛
```
⛔ **业务消息号必须 ≥ 10001**（`Game.InternalMsgMax = 10000`，`Game.cs:514`）；≤10000 会被拒并报 Error。

## 4. UI（★ 本项目最关键的一条：**UI 可以在代码里构建**）

```csharp
void Open<T>(object param = null) where T : class, IUIPanel   // PresentationContracts.cs:55
void Close<T>() where T : class, IUIPanel                     // :57
void CloseAll()                                               // :61
T Get<T>() where T : class, IUIPanel                          // :63
bool IsOpen<T>() where T : class, IUIPanel                     // :65
void Toast(string text, float duration = 2f)                   // :78
void FloatText(Vector3 worldPos, string text, Color? color = null, float duration = 1.2f) // :84
void ShowLoading(string text = null) / void HideLoading()      // :93 / :96
void Confirm(string title, string message, Action onConfirm, Action onCancel = null, string confirmText = null, string cancelText = null) // :111
void OnPanelOpened(Action<string> handler) / OnPanelClosed(Action<string>) // :67 / :69
```

面板基类：
```csharp
public abstract class UIPanel : MonoBehaviour, IUIPanel {  // PresentationContracts.cs:163
    public virtual string PanelName => GetType().Name;      // :171
    public virtual UILayer Layer => UILayer.Normal;         // :174
    public GameObject Root => gameObject;                   // :177
    public virtual void OnOpen(object param) { }            // :180
    public virtual void OnClose() { }                       // :183
    public virtual void OnUpdate(float dt) { }              // :186
}
public enum UILayer { Background=0, Normal=1, Popup=2, Top=3, System=4 }  // :20
```
默认预制体加载：`Resources.Load<GameObject>($"UI/{typeof(T).Name}")`（`Runtime/Presentation/UI.cs:119-127`），
**找不到就报 Error 且面板不打开**。可用 `CloverPresentation.PanelProvider`（`Runtime/Presentation/CloverPresentation.cs:69`）替换该加载逻辑。

★ **`UIFactory`（`Runtime/Presentation/UIWidgets.cs` + `UIWidgetComponents.cs`，`public static partial class`）—— 代码里直接造 UI：**
```csharp
Font DefaultFont()                                                  // UIWidgets.cs:44
RectTransform CreateNode(string name, Transform parent)             // :93
void Stretch(RectTransform rt)                                      // :103
RectTransform CreateCentered(string name, Transform parent, Vector2 size, Vector2 pos)  // :112
Image CreatePanel(string name, Transform parent, Color color, bool raycastTarget)       // :123
Text  CreateText(string name, Transform parent, string content, int fontSize, TextAnchor alignment, Color color, bool raycastTarget = false)  // :140
Image CreateButton(string name, Transform parent, string label, Vector2 size, Vector2 pos, Color bg, Action onClick)  // :169
Camera UICamera()                                                   // :190
void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)  // UIWidgetControls.cs:164
void AnchoredTopLeft(RectTransform rt, Vector2 pos, Vector2 size)   // :175
void AnchoredBottom(RectTransform rt, Vector2 pos, Vector2 size, TextAnchor anchor = TextAnchor.LowerCenter)  // :193
Text  CreateBottomLabel(string name, Transform parent, string content, int fontSize, Vector2 pos, Vector2 size, Color color)  // :206
Image CreateBoxRect(string name, Transform parent, Vector2 pos, Vector2 size, Color color, bool raycast = false)  // :215
Text  CreateLabel(string name, Transform parent, string content, int fontSize, Vector2 pos, Vector2 size, TextAnchor anchor, Color color)  // :224
void SetBarWidth(RectTransform fill, float progress01)              // :238
Slider CreateSlider(string name, Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, Action<float> onValueChanged, WidgetSliderStyle style)  // :257
InputField CreateInputField(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, string placeholderText, int characterLimit, WidgetInputFieldStyle style)  // :318
Selector CreateSelector(string name, Transform parent, string label, Vector2 pos, float labelWidth, float arrowWidth, float valueWidth, Action onPrev, Action onNext, WidgetSelectorStyle style)  // :385
ToggleRow CreateToggleRow(string name, Transform parent, string label, Vector2 pos, float labelWidth, float buttonWidth, WidgetToggleRowStyle style)  // :411
// 风格结构体：WidgetButtonStyle / WidgetSliderStyle / WidgetInputFieldStyle / WidgetSelectorStyle / WidgetToggleRowStyle
// 句柄：ToggleRow { Button Button; Text Value; SetText(string) } / Selector { Text Value; Button Prev; Button Next; SetText(string) }
// ★ 世界空间血条：public sealed class WorldHpBar : MonoBehaviour   // UIWidgets.cs:1018
//     static WorldHpBar Create(Transform target, float width=1f, float height=0.12f, float yOffset=2.15f, string tag="HpBar")  // :1067
//     void SetHp(float hp, float maxHp) :1131 / SetRatio(float) :1139 / SetVisible(bool) :1146 / SetYOffset(float) :1157
```

## 5. 其余子系统

| 子系统 | 关键签名 | 出处 |
|---|---|---|
| `Game.Fsm` | `RegisterState(string, Action onEnter, Action<float> onTick, Action onExit)` · `Transition(string)` · `AddTransition(string trigger, string to)` · `Trigger(string)` · `Force(string)` · `Current` | `Fsm.cs:24-64` |
| `Game.Scene` | `CurrentScene` · `Load(string, Action<float> progress, Action onDone)` · `Unload(string, Action onDone)` · `OnSceneLoaded/OnSceneUnloaded` | `PresentationContracts.cs:194-205` |
| `Game.Event` | `On(string,Action)` / `On<T>` / `On<T1,T2>` / `Once*` / `Off*` / `OffAll` / `Emit` / `Emit<T>` / `Emit<T1,T2>` | `Event.cs:29-128` |
| `Game.Timer` | `After(float,Action)` / `After(float,Action,string scope)` / `Every` / `AfterUnscaled` / `EveryUnscaled` / `AfterName` / `EveryName` / `Stop(long)` / `StopNamed` / `StopScope` / `StopAll` | `Timer.cs:19-112` |
| `Game.Sound` | `PlayBGM(string,float fadeTime=0.5f)` · `StopBGM` · `PlaySFX(string)` · `PlaySFXAt(string,Vector3)` · `PlayVoice` · `StopAll` · `SetVolume(SoundGroup,float)` · `GetVolume` · `SetMute`；`enum SoundGroup{BGM,SFX,Voice}` | `PresentationContracts.cs:292-395` |
| `Game.Input` | `State`(`InputState`: `MoveDirection`,`Skill1-4Down`,`JumpDown`,`DodgeDown`,`InteractDown`,`TouchDown`) · `GetKey/GetKeyDown/GetKeyUp(GameKey)` · `GetMouseButton*` · `MousePosition(Vector3)` · `MouseDelta(Vector2)` · `GetAxis(string,bool)` · `Lock()/Unlock()`；`enum GameKey`（A-Z/Num0-9/方向/`MouseLeft`…） | `Input.cs:14-187` |
| `Game.Res` | `LoadAsset<T>(path, cb)` / `LoadAsset<T>(path, progress, cb)` / `TryGet<T>(path)` / `Exists(path)` / `LoadAll<T>(path)` / `Release` / `UnloadAll` / `Preload(List<string>, onDone, progress)` / `CachedBytes` / `CacheWatermark` | `Contracts.cs:1019-1137` |
| `Game.Table` | `CloverTable.Dir` / `CloverTable.LoadAll(streamingAssetsDir, dataDir)`（成功返 null） / `CloverTable.Get<T>(tableName, id)` / `Get<T>(tableName, key)` / `RequiredTables`；行类要求「public 无参构造 + public 字段」，**不实现 `IDataRow`** | `Runtime/Data/CloverTable.cs:69-258` |
| `Game.Logger` | `Debug/Info/Warn(tag,msg)` · `Error(tag,msg,Exception ex=null)` · `Fatal(...)`；`enum LogLevel` | `Logger.cs:52-94` |
| `Game.Pool` | `Spawn(string key, Transform parent=null, string group=null)`（`key` = `Resources` 下的预制体路径） / `Despawn` / `Preload` / `Clear*` / `GetActiveCount` / `GetInactiveCount` / `TrimIdle`；`ReferencePool.Acquire<T>()/Release<T>()` | `EntityPool.cs:100-163, 321-397` |
| `Game.Entity` | `Create(long objectID,int typeID,string group)` / `Destroy` / `Get` / `GetAll` / `GetByGroup` / `BindView(long,GameObject)` / `GetView` / `DestroyGroup` / `ClearAll`；`EntityInfo{ObjectID,TypeID,SceneGroup}` | `EntityPool.cs:57-93` |
| `Game.Anim` | `CreateAnimator(GameObject, RuntimeAnimatorController) → IAnimPlayer` / `Destroy`；`IAnimPlayer`: `Play(string,float normalizedTime=0)` / `CrossFade` / `SetBool` / `SetFloat` / `SetInteger` / `SetTrigger` / `OnComplete(Action)` | `PresentationContracts.cs:256-284` |
| `Game.Setting` | `Get<T>(string,T defaultValue=default)` / `Set<T>` / `Save()` / `Load()` / `Delete` / `DeleteAll` | `Setting.cs:21-49` |
| `Game.Quality` | `Level` / `Config` / `IsThrottling` / `CurrentFPS` / `SetLevel(QualityTier)` / `AutoDetect()` / `OnLevelChanged` / `OnThrottling`；`enum QualityTier{Low,Medium,High}`；`QualityConfig{ResolutionScale,ShadowEnabled,LODLevel,ParticleDensity,MaxSameScreenCount,TargetFrameRate}` | `PresentationContracts.cs:447-514` |
| `CloverAuth` | `AuthAddr{get;set;}` / `Enabled` / `Task<string> LoginAsync(account,password)` / `Task<string> SignupAsync(account,password)` / `Task<string> ChannelLoginAsync(channel,ticket)` | `CloverAuth.cs:39-64` |

## 6. 引擎自带的唯一示例

`<引擎>/Samples~/LoginFlow/LoginFlow.cs`（`public class LoginFlow : MonoBehaviour`，:33）+
`CloverEngine.Samples.LoginFlow.asmdef` —— `Game.Launch` → `CloverAuth` → `CloverNet.Init` →
订阅 `Game.Event` / `Game.Sync`。**写任何客户端网络/启动代码前先读它。**
