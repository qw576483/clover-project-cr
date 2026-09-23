using System;
using CloverEngine;
using CR.Def;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// BGM（背景音乐）播放器：把 <c>Core/Events.cs</c> 里**已有**的流程 / 对局事件翻成
    /// <c>Game.Sound.PlayBGM(clipName)</c>（引擎 <c>ISoundManager</c>）。
    ///
    /// <para>
    /// <b>路径口径（⛔ 别猜）</b>：引擎 <c>Runtime/Presentation/Sound.cs:263</c> 的 <c>PlayBGM</c> 内部
    /// <b>硬编码</b> <c>$"Sound/BGM/{clipName}"</c>（<c>PlaySFX</c> 同款，硬编码 <c>Sound/SFX/</c>）。
    /// 所以落地目录只能是 <c>Resources/Sound/BGM/</c>，clipName 见 <see cref="AudioPaths"/> 的 Bgm* 键。
    /// </para>
    ///
    /// <para>
    /// <b>事件 → BGM 映射（逐条对 <c>Core/Events.cs</c>）</b>：
    /// <list type="bullet">
    /// <item>站点切换（<c>Events.Flow.StationChanged</c>，参数 <c>(string station)</c>）：
    ///   菜单站点群（<c>Boot</c>/<c>Login</c>/<c>Nickname</c>/<c>MainMenu</c>/<c>Room</c>）⇒ <see cref="AudioPaths.BgmMenu"/>；
    ///   <c>Battle</c> ⇒ <see cref="AudioPaths.BgmBattle"/>；<c>Pause</c> ⇒ <b>不换曲</b>（保留对局曲）。</item>
    /// <item>对局开始（<c>Events.Battle.Started</c>）⇒ <see cref="AudioPaths.BgmBattle"/>。
    ///   <b>为什么要它、而不只靠站点</b>：<c>AppFlow.GoTo</c> 对"已经是当前站点"的请求会早退 ⇒
    ///   「再来一局」（同处 <c>Battle</c> 站点重开）**没有** StationChanged；而 <c>RequestEnterBattle</c>
    ///   每次都发 Started，靠它才能把 BGM 从结算曲切回对局曲。</item>
    /// <item>结算（<c>Events.Battle.Ended</c>）⇒ <see cref="AudioPaths.BgmResult"/>。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>怎么播（⛔ 只用 clover-engine 的声音 API）</b>：一律 <c>Game.Sound.PlayBGM</c> —— 它是双音源
    /// 交替 + 交叉淡入淡出，<b>会自动把上一曲淡出停掉</b>（"进图停菜单曲 / 回菜单停对局曲"就落在这里）。
    /// 本类**不自己 <c>AddComponent&lt;AudioSource&gt;</c>、不自建播放器**：那等于绕开引擎的音源管理。
    /// </para>
    ///
    /// <para>
    /// <b>音量</b>：本类**不写死音量** —— BGM 走 <c>SoundGroup.BGM</c> 分组，音量由
    /// <c>Module/Settings/SettingsManager</c> 经 <c>Game.Sound.SetVolume(SoundGroup.BGM, …)</c> 应用
    /// （V5 已接；引擎 <c>PlayBGM</c> 起播时读的就是该分组音量）。日志里把 <c>GetVolume(BGM)</c> 打出来
    /// 作为"音量档真的生效"的运行时判据（数值类证据 = 日志行，⛔ 不靠听感）。
    /// </para>
    ///
    /// <para>
    /// <b>挂点（⛔ 为什么是启动钩子）</b>：<c>App/Bootstrap.cs</c> 是冻结产出（不许改），所以照
    /// <c>UI/BattleUiHost</c> 的范式用 <c>Game.RegisterLaunchHook</c> 登记（同 key 覆盖 ⇒ 幂等）。
    /// 本类**只订阅事件、不在钩子内播音**：<c>Game.Sound</c> 由 <c>CloverPresentation.Init</c> 的另一条
    /// 启动钩子挂载，两条钩子同相位、**顺序不保证** ⇒ 钩子里 <c>Game.Sound</c> 可能还是 null。
    /// 首曲由第一次 StationChanged（<c>AppFlow.Start</c> → <c>GoTo(Boot)</c>）触发，那时 Sound 必已就绪。
    /// </para>
    /// </summary>
    public static class BgmView
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "BgmView";

        /// <summary>启动钩子的注册键（同 key 重复注册会覆盖，可安全重复登记；照 <c>UI/BattleUiHost</c>）。</summary>
        private const string LaunchHookKey = "CR.View.Bgm";

        /// <summary>BGM 交叉淡入时长（秒）。取引擎 <c>PlayBGM</c> 的默认值，显式写出便于日志与统一调参。</summary>
        private const float FadeSeconds = 0.5f;

        /// <summary>已装订阅的那条事件总线（判据 = 对象标识：每次 <c>Game.Launch</c> 都新建 <c>EventBus</c>）。</summary>
        private static IEventBus _bus;

        /// <summary>当前已请求的 BGM clipName。同一曲不重复起播（防"同站点二次触发"把曲子打断重来）。</summary>
        private static string _currentClip = string.Empty;

        private static Action<string> _onStationChanged;
        private static Action<BattleStartNotify> _onStarted;
        private static Action<BattleEndNotify> _onEnded;

        /// <summary><c>Game.Sound</c> 为空只报一次（表现域未挂载 ⇒ 全程无声，不该每次切站都刷屏）。</summary>
        private static bool _serviceWarned;

        /// <summary>BGM 资源缺失只报一次（引擎 <c>Sound.cs</c> 对同一路径也有 WarnOnce；这里是我们自己的探测）。</summary>
        private static bool _missingWarned;

        /// <summary>未识别的站点名只报一次（新增站点未接 BGM 时留一次痕，⛔ 不静默）。</summary>
        private static bool _unknownStationWarned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            Game.RegisterLaunchHook(LaunchHookKey, Install);
        }

        /// <summary>
        /// 装上事件订阅（幂等：同一条事件总线只装一次；引擎重新 Launch ⇒ 新总线 ⇒ 重装并复位当前曲）。
        /// </summary>
        public static void Install()
        {
            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：启动钩子早于 Launch 的核心初始化。留痕，否则表现为"全程没有 BGM"。
                Game.Logger?.Error(LogTag, "Game.Event 为空（启动钩子时机异常），BGM 不会有任何响应");
                return;
            }

            if (ReferenceEquals(bus, _bus)) return; // 同一条总线：已装过

            if (_bus != null)
            {
                Game.Logger?.Info(LogTag, "检测到新的事件总线（引擎重新 Launch），重装订阅并复位当前曲");
                _currentClip = string.Empty;
            }

            _bus = bus;

            _onStationChanged = OnStationChanged;
            _onStarted = OnStarted;
            _onEnded = OnEnded;

            bus.On<string>(Events.Flow.StationChanged, _onStationChanged);
            bus.On<BattleStartNotify>(Events.Battle.Started, _onStarted);
            bus.On<BattleEndNotify>(Events.Battle.Ended, _onEnded);

            Game.Logger?.Info(LogTag, "BGM 已装载：站点切换（菜单 / 对局）+ 对局开始 + 结算");
        }

        // ═════════════════════════ 事件 → BGM ═════════════════════════

        private static void OnStationChanged(string station)
        {
            if (string.IsNullOrEmpty(station))
            {
                // 非预期分支：发送方参数有误。留痕，⛔ 不静默。
                Game.Logger?.Warn(LogTag, "收到空的站点名（发送方参数有误？），BGM 不切换");
                return;
            }

            switch (station)
            {
                case Stations.MainMenu:
                case Stations.Login:
                case Stations.Nickname:
                case Stations.Boot:
                case Stations.Room:
                    Play(AudioPaths.BgmMenu, $"站点 {station}（菜单）");
                    break;

                case Stations.Battle:
                    Play(AudioPaths.BgmBattle, $"站点 {station}（对局）");
                    break;

                case Stations.Pause:
                    // 暂停不换曲：保留正在播的对局曲（与暂停菜单"只覆盖 UI、不真暂停对战"一致）。
                    break;

                default:
                    // 非预期分支：站点表扩了而本类没跟上。留痕一次（⛔ 不静默）。
                    if (!_unknownStationWarned)
                    {
                        _unknownStationWarned = true;
                        Game.Logger?.Warn(LogTag,
                            $"收到未知站点名 '{station}'（新增站点未接 BGM？）⇒ 保持当前曲（只报一次）");
                    }
                    break;
            }
        }

        private static void OnStarted(BattleStartNotify start)
        {
            // 覆盖"同站点再来一局"（见类注释：那种情况没有 StationChanged）。
            Play(AudioPaths.BgmBattle, "对局开始");
        }

        private static void OnEnded(BattleEndNotify result)
        {
            Play(AudioPaths.BgmResult, "对局结算");
        }

        // ═════════════════════════ 播放（只走 clover-engine API） ═════════════════════════

        /// <summary>
        /// 切一首 BGM：同一曲跳过；先确认引擎声音服务在、资源在，再交给 <c>Game.Sound.PlayBGM</c>。
        /// 每次真正切曲都留一条 Info（含 clipName + <c>GetVolume(BGM)</c>）—— 这是"哪一站真的响了 + 音量档生效"
        /// 的唯一运行时判据（数值类证据 = 日志行，⛔ 不靠截图 / 听感）。
        /// </summary>
        private static void Play(string clipName, string why)
        {
            if (string.IsNullOrEmpty(clipName)) return;
            if (clipName == _currentClip) return; // 同曲不重复起播

            var sound = Game.Sound;
            if (sound == null)
            {
                if (!_serviceWarned)
                {
                    _serviceWarned = true;
                    Game.Logger?.Warn(LogTag, "Game.Sound 为空（表现域未挂载）⇒ BGM 无法播放（只报一次）");
                }
                return;
            }

            if (!ClipExists(clipName)) return;

            var vol = sound.GetVolume(SoundGroup.BGM);
            _currentClip = clipName;
            Game.Logger?.Info(LogTag, $"播放 BGM {clipName}（{why}；SoundGroup.BGM 音量 {vol:F2}）");
            sound.PlayBGM(clipName, FadeSeconds);
        }

        /// <summary>
        /// BGM 资源是否落地。<c>Game.Res.Exists</c> 是 clover-engine 的按路径缓存探测
        /// （与 <c>View/BattleAudioView.ClipExists</c> 同一手法）。缺失时留痕一次并返回 false。
        /// 探测通道不可用（<c>Game.Res</c> 为空）⇒ 不拦，交给引擎自己按缺失处理（它也有 WarnOnce）。
        /// </summary>
        private static bool ClipExists(string clipName)
        {
            var res = Game.Res;
            if (res == null) return true;

            if (res.Exists(AudioPaths.BgmPath(clipName))) return true;

            if (!_missingWarned)
            {
                _missingWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"BGM 资源缺失（Resources/{AudioPaths.BgmPath(clipName)}）⇒ 本次及后续缺失曲目不播放（只报一次）");
            }
            return false;
        }
    }
}
