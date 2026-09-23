using System;
using CloverEngine;
using UnityEngine;

namespace CR.Module.Settings
{
    /// <summary>
    /// 设置（音量 / 画质 / 全屏）的**唯一**读写处：真改（当场生效）+ 真存（`Game.Setting.Save()`）。
    ///
    /// <para>
    /// <b>为什么用 <c>Game.Setting</c> 而不是 <c>PlayerPrefs</c></b>：引擎自带设置存储
    /// （`ISetting`，`Setting.cs`，原子写盘 + 目录不可用时退化为内存），而且 `PlayerPrefs` 是
    /// 全局硬规则明令禁止的（`tools/verify.ps1` 的 hard-rules 会报）。
    /// </para>
    /// <para>
    /// <b>为什么默认值不自己发明</b>：音量默认满（`1`，引擎 `GetVolume` 未设置时也返回 1，口径一致）；
    /// 画质**首次运行不写死档位**，而是先 `Game.Quality.AutoDetect()` 让引擎按设备能力判一次，
    /// 再把结果落盘 —— 写死一个档位等于替用户/设备做决定（也违反"写不出出处的值不许进工程"）。
    /// 全屏首次运行取 `Screen.fullScreen`（Unity 自己的默认）而不是写死 true/false。
    /// </para>
    /// <para>
    /// <b>谁改它</b>：本类**自己订阅** `Events.Settings.*Request`（在 <see cref="Init"/> 里），
    /// 收到后转调下面的 `Set*` —— 面板只发请求事件，⛔不直接 new 本类、也不⛔引 `CR.Module`。
    /// </para>
    /// <para>
    /// <b>V5 修复</b>：此前这些 `*Request` 事件**全无订阅者** ⇒ 设置面板的滑块/开关全不生效。
    /// 处理者放本类而不是 `AppFlow`：这条链是 `面板 --Emit(Request)--> 本类 --Set--> 引擎`，
    /// 本类的 `Set*` 只 Emit `*Changed`（不是 `*Request`），**不会自激**。
    /// 订阅在 <see cref="Init"/> 里做、由 `_subscribed` 保证幂等；实例随 `Bootstrap` 创建一次、跨场景存活。
    /// </para>
    /// <para>
    /// <b>CR-F2：`*Changed` 这条反向链的职责（审计问的"设计意图"）</b> —— 它是
    /// **"权威值已变更"的通知**，不是给本类自己用的：本类改完就地生效与落盘，不需要自己听。
    /// 它服务的是**显示方**（谁来显示这个值，谁就订阅它来保持与权威值同步）：
    /// <list type="bullet">
    /// <item>`BgmVolumeChanged` / `SfxVolumeChanged` / `QualityChanged` / `FullscreenChanged`
    /// ⇒ 订阅方 = <c>SettingsPanel</c>（它显示这四个值；订阅后事件一到就地刷新，见该面板的 `Subscribe`）。</item>
    /// <item>`VoiceVolumeChanged` ⇒ **今天在工程内无人订阅**，理由见 `Events.Settings` 段里该常量的注释
    /// （工程内没有"人声"显示项、也没有任何 voice 素材/播放点）。**这是登记过的设计状态，不是漏挂**。</item>
    /// </list>
    /// 另一条必须记住的分支：画质档位有**两个**来源，引擎侧的自动降档原先完全没有出口
    /// ⇒ 由 <see cref="HookEngineQuality"/> 把它翻译成同一条 `QualityChanged`，否则已打开的面板会显示旧值。
    /// </para>
    /// </summary>
    public sealed class SettingsManager
    {
        private const string Tag = "Settings";

        // ── 设置面板请求事件的订阅（V5 修复：这些 *Request 此前无人订阅 ⇒ 滑块/开关不生效） ──
        //    处理器必须是**实例字段**：事件总线按委托相等性去重 / 注销，走局部 lambda 会在
        //    「重复 Init」时被当成新 handler 叠加，且 Off 不掉（`Event.cs:310-328`）。
        private Action<float> _onBgmRequest;
        private Action<float> _onSfxRequest;
        private Action<float> _onVoiceRequest;
        private Action<int> _onQualityRequest;
        private Action<bool> _onFullscreenRequest;
        private bool _subscribed;

        /// <summary>
        /// 引擎侧画质回调是否已挂（`Game.Quality.OnLevelChanged`）。幂等位：引擎**没有**反订阅接口
        /// （`Quality.cs:160-163` 只 append），重复挂会让一次改档走多遍 ⇒ 必须自己保证只挂一次。
        /// </summary>
        private bool _qualityHooked;

        /// <summary>
        /// 上一次**已对外广播**过的画质档位（-1 = 还没广播过）。用途 = 去重：
        /// 现在有两个来源会走到广播（① 本类的 `SetQuality`；② 引擎侧改档回调，见
        /// <see cref="OnEngineQualityChanged"/>），而 ① 里 `ApplyQuality` 会**同步**触发 ②
        /// ⇒ 不去重就会对同一次改档发两条 `QualityChanged`（面板会刷两遍、订阅方会被叫醒两次）。
        /// </summary>
        private int _lastNotifiedTier = -1;

        // ⛔ 键名只在本文件出现（"配置键的唯一定义处"）。前缀按 `audio.` / `video.` 分段，
        //    便于将来在 Setting 文件里一眼看出归属。
        private const string KeyBgm = "audio.bgm";
        private const string KeySfx = "audio.sfx";
        private const string KeyVoice = "audio.voice";

        /// <summary>
        /// 画质档位的**权威键** —— 必须与引擎逐字一致。
        ///
        /// <para>
        /// <b>为什么用引擎的键名而不是自己起一个</b>：引擎 <c>QualityManager</c>
        /// （`clover-client-unity-engine/Runtime/Presentation/Quality.cs:15`）在
        /// <c>SetLevel</c> → <c>SaveLevel</c>（同文件 `:279-284`）里写这个键，而
        /// **引擎自动降档**（fps 低于目标 70% 持续 3s，同文件 `:221-243`）走的也是
        /// <c>SetLevel</c> ⇒ 降档结果只落在**引擎键**上。项目若另用 <c>video.quality_tier</c>，
        /// 就成了"两个权威"：引擎降档后项目键仍是旧值，下次 <see cref="Init"/> 读旧值
        /// 再 <c>SetLevel</c> 回去 —— 玩家看到"自动降档不保持 / 设了档位重启又变回去"。
        /// 引擎是共享代码（本项目⛔不改），⇒ **统一到引擎键**：单一权威、无需双写。
        /// </para>
        /// </summary>
        private const string KeyQuality = "quality_level";

        /// <summary>
        /// 本项目旧版用过的画质键（`video.quality_tier` 时期）：**只读一次做迁移**，
        /// 避免老存档（盘上只有旧键）升级后画质档丢失 —— 与引擎对 `device_level` 的处置同一手法
        /// （`Quality.cs:16-17 / 291-302`）。
        /// </summary>
        private const string LegacyKeyQuality = "video.quality_tier";

        private const string KeyFullscreen = "video.fullscreen";

        /// <summary>BGM 音量，0~1。</summary>
        public float BgmVolume { get; private set; } = 1f;

        /// <summary>音效音量，0~1。</summary>
        public float SfxVolume { get; private set; } = 1f;

        /// <summary>人声音量，0~1。</summary>
        public float VoiceVolume { get; private set; } = 1f;

        /// <summary>当前画质档位（引擎取值，不是本类发明的）。</summary>
        public QualityTier QualityTier => Game.Quality != null ? Game.Quality.Level : QualityTier.Medium;

        /// <summary>当前是否全屏（直接读 Unity 的状态，避免本地缓存的影子值）。</summary>
        public bool Fullscreen => Screen.fullScreen;

        /// <summary>
        /// 读取已存设置并**应用**（Bootstrap 在进 Boot 站点之前调一次）。
        /// 首次运行（没有任何键）走上面"为什么默认值不自己发明"那段的三个来源。
        /// </summary>
        public void Init()
        {
            if (Game.Setting == null)
            {
                // 非预期分支：Launch 才会建 Setting ⇒ 走到这里说明调用时机错了。留痕，别静默。
                Game.Logger?.Error(Tag, "Game.Setting 为空（SettingsManager.Init 早于 Game.Launch？），设置不会持久化");
                return;
            }

            BgmVolume = Clamp01(Game.Setting.Get(KeyBgm, 1f));
            SfxVolume = Clamp01(Game.Setting.Get(KeySfx, 1f));
            VoiceVolume = Clamp01(Game.Setting.Get(KeyVoice, 1f));

            ApplyVolume(SoundGroup.BGM, BgmVolume);
            ApplyVolume(SoundGroup.SFX, SfxVolume);
            ApplyVolume(SoundGroup.Voice, VoiceVolume);

            // 画质：权威值 = 引擎键 KeyQuality（`quality_level`），见该常量的注释。
            // 用 -1 作"没存过"的哨兵：存储里没有该键时 Get 返回哨兵本身。
            var storedTier = Game.Setting.Get(KeyQuality, -1);
            if (storedTier < 0)
            {
                // 老存档迁移：只有项目旧键（`video.quality_tier`）时把值搬到引擎键，并删掉旧键
                // （不删则会留下一个永远不再被读的僵尸键，下次排查时误导人）。
                var legacyTier = Game.Setting.Get(LegacyKeyQuality, -1);
                if (legacyTier >= (int)QualityTier.Low && legacyTier <= (int)QualityTier.High)
                {
                    storedTier = legacyTier;
                    Game.Setting.Set(KeyQuality, legacyTier);
                    Game.Setting.Delete(LegacyKeyQuality);
                    Game.Setting.Save();
                    Game.Logger?.Info(Tag,
                        $"画质键迁移：{LegacyKeyQuality}={legacyTier} → {KeyQuality}（旧键已删除，老存档画质档未丢）");
                }
            }

            if (storedTier < 0)
            {
                if (Game.Quality != null)
                {
                    Game.Quality.AutoDetect();
                    Game.Setting.Set(KeyQuality, (int)Game.Quality.Level);
                    Game.Setting.Save();
                    Game.Logger?.Info(Tag, $"首次运行：画质由 AutoDetect 定为 {Game.Quality.Level} 并落盘");
                }
                else
                {
                    Game.Logger?.Warn(Tag, "Game.Quality 为空（表现域未挂载？），画质档位未初始化");
                }
            }
            else
            {
                ApplyQuality((QualityTier)storedTier);

                // 两个键同时存在（本轮统一之前的老盘：项目键 + 引擎键各写过一次，值可能已经不一致）
                // ⇒ 删掉遗留的项目键。它本轮起不再被读，留着只会让排查的人以为"还有第二个权威"。
                if (Game.Setting.Get(LegacyKeyQuality, -1) >= 0)
                {
                    Game.Setting.Delete(LegacyKeyQuality);
                    Game.Setting.Save();
                    Game.Logger?.Info(Tag, $"清理遗留画质键 {LegacyKeyQuality}（权威键已是 {KeyQuality}={storedTier}）");
                }
            }

            ApplyFullscreen(Game.Setting.Get(KeyFullscreen, Screen.fullScreen));

            // 接上"面板改动"这条链：收 `Events.Settings.*Request` → 转调本类 `Set*`（真改 + 真存）。
            Subscribe();

            Game.Logger?.Info(Tag,
                $"设置已应用：bgm={BgmVolume:0.##} sfx={SfxVolume:0.##} voice={VoiceVolume:0.##} " +
                $"quality={QualityTier} fullscreen={Fullscreen}");
        }

        /// <summary>
        /// 订阅设置面板发出的改动请求（音量 ×3 / 画质 / 全屏）。幂等：`_subscribed` 保证只挂一次。
        /// <para>
        /// 载荷形状取自 <c>Core/Events.cs</c> 的 `Settings` 段与本文件既有的 `Emit` 口径：
        /// 音量 = `(float)`、画质 = `(int)`、全屏 = `(bool)`。类型必须一致 —— 事件总线用
        /// `DynamicInvoke` 派发（`Event.cs:413-494`），类型不匹配会在派发时抛异常而不是编译期报错。
        /// </para>
        /// </summary>
        private void Subscribe()
        {
            if (_subscribed) return;

            if (Game.Event == null)
            {
                // 非预期分支：`Game.Event` 是引擎核心总线，Launch 之后必然非空；走到这里说明 Init 时机错了。
                Game.Logger?.Warn(Tag, "Game.Event 为空，设置面板的改动请求将无人接收（滑块/开关不生效）");
                return;
            }

            _onBgmRequest = OnBgmVolumeRequest;
            _onSfxRequest = OnSfxVolumeRequest;
            _onVoiceRequest = OnVoiceVolumeRequest;
            _onQualityRequest = OnQualityRequest;
            _onFullscreenRequest = OnFullscreenRequest;

            Game.Event.On(Events.Settings.BgmVolumeRequest, _onBgmRequest);
            Game.Event.On(Events.Settings.SfxVolumeRequest, _onSfxRequest);
            Game.Event.On(Events.Settings.VoiceVolumeRequest, _onVoiceRequest);
            Game.Event.On(Events.Settings.QualityRequest, _onQualityRequest);
            Game.Event.On(Events.Settings.FullscreenRequest, _onFullscreenRequest);

            HookEngineQuality();

            _subscribed = true;
            Game.Logger?.Info(Tag, "已订阅设置面板的改动请求（音量×3 / 画质 / 全屏）+ 引擎侧画质变更");
        }

        /// <summary>
        /// 接上**引擎侧**的画质变更（CR-F2 修复的第二根线）。
        ///
        /// <para>
        /// <b>为什么必须有它</b>：画质档位有两个改动来源 —— ① 本类的 `SetQuality`（玩家在设置面板点 ◀▶），
        /// ② **引擎自己的自动降档**（`Quality.cs:221-243` 的 `CheckAutoDowngrade`：平均帧率低于该档目标 70%
        /// 持续 3s ⇒ `SetLevel(当前档−1)`，并写引擎键 `quality_level`）。② 发生时本类**不知情**，
        /// 于是 `Events.Settings.QualityChanged` 根本不会被发出 ⇒ 已打开的设置面板一直显示降档前的旧值
        /// （面板只读"打开那一刻"的快照）。实测记录 = `.ai-tmp/test/CR-F2-证据.md` 的 S2 行。
        /// </para>
        /// <para>
        /// ② 的唯一可订阅出口就是引擎这个 `OnLevelChanged`（`Quality.cs:112-119` 在 `SetLevel` 里逐个回调）
        /// ⇒ 挂上它，把引擎侧的改档**翻译成同一条项目事件**，让"They 谁改的"对订阅方不可见
        /// （面板只认权威值，不关心是谁改的）。
        /// </para>
        /// </summary>
        private void HookEngineQuality()
        {
            if (_qualityHooked) return;

            if (Game.Quality == null)
            {
                // 非预期分支：表现域未挂载时画质档位本就无从谈起，留痕（别静默）。
                Game.Logger?.Warn(Tag, "Game.Quality 为空，引擎侧自动降档不会同步到本项目事件（面板可能显示旧值）");
                return;
            }

            Game.Quality.OnLevelChanged(OnEngineQualityChanged);
            _qualityHooked = true;
            Game.Logger?.Info(Tag, "已订阅引擎画质变更（Game.Quality.OnLevelChanged）⇒ 自动降档会广播 QualityChanged");
        }

        // ───────────────────────── 事件处理：面板改动 → 真改 + 真存 ─────────────────────────
        //    每条都打"改前 → 改后"的运行时判据（数值类证据 = 运行时日志行，⛔ 不靠截图）。

        private void OnBgmVolumeRequest(float volume)
        {
            var before = Game.Sound != null ? Game.Sound.GetVolume(SoundGroup.BGM) : float.NaN;
            SetBgmVolume(volume);
            var after = Game.Sound != null ? Game.Sound.GetVolume(SoundGroup.BGM) : float.NaN;
            Game.Logger?.Info(Tag, $"[BGM] Game.Sound.GetVolume(BGM) {before:0.##} → {after:0.##}（请求 {volume:0.##}）");
        }

        private void OnSfxVolumeRequest(float volume)
        {
            var before = Game.Sound != null ? Game.Sound.GetVolume(SoundGroup.SFX) : float.NaN;
            SetSfxVolume(volume);
            var after = Game.Sound != null ? Game.Sound.GetVolume(SoundGroup.SFX) : float.NaN;
            Game.Logger?.Info(Tag, $"[SFX] Game.Sound.GetVolume(SFX) {before:0.##} → {after:0.##}（请求 {volume:0.##}）");
        }

        private void OnVoiceVolumeRequest(float volume)
        {
            var before = Game.Sound != null ? Game.Sound.GetVolume(SoundGroup.Voice) : float.NaN;
            SetVoiceVolume(volume);
            var after = Game.Sound != null ? Game.Sound.GetVolume(SoundGroup.Voice) : float.NaN;
            Game.Logger?.Info(Tag, $"[Voice] Game.Sound.GetVolume(Voice) {before:0.##} → {after:0.##}（请求 {volume:0.##}）");
        }

        private void OnQualityRequest(int tier)
        {
            if (tier < (int)QualityTier.Low || tier > (int)QualityTier.High)
            {
                // 非预期分支：面板只会发 0/1/2（`SettingsPanel.TierNames` 的合法下标）。
                Game.Logger?.Warn(Tag,
                    $"收到未知画质档位 {tier}（有效 {QualityTier.Low}({(int)QualityTier.Low})~" +
                    $"{QualityTier.High}({(int)QualityTier.High})），已忽略");
                return;
            }

            var before = QualityTier;
            SetQuality((QualityTier)tier);
            Game.Logger?.Info(Tag, $"[Quality] Game.Quality.Level {before} → {QualityTier}（请求 {tier}）");
        }

        private void OnFullscreenRequest(bool fullscreen)
        {
            var before = Screen.fullScreen;
            SetFullscreen(fullscreen);
            Game.Logger?.Info(Tag, $"[Fullscreen] Screen.fullScreen {before} → {Screen.fullScreen}（请求 {fullscreen}）");
        }

        /// <summary>设置 BGM 音量（0~1，越界会被夹到范围内，与引擎 `SetVolume` 的口径一致）。</summary>
        public void SetBgmVolume(float volume)
        {
            BgmVolume = Clamp01(volume);
            ApplyVolume(SoundGroup.BGM, BgmVolume);
            Persist(KeyBgm, BgmVolume);
            Game.Event?.Emit(Events.Settings.BgmVolumeChanged, BgmVolume);
        }

        /// <summary>设置音效音量。</summary>
        public void SetSfxVolume(float volume)
        {
            SfxVolume = Clamp01(volume);
            ApplyVolume(SoundGroup.SFX, SfxVolume);
            Persist(KeySfx, SfxVolume);
            Game.Event?.Emit(Events.Settings.SfxVolumeChanged, SfxVolume);
        }

        /// <summary>设置人声音量。</summary>
        public void SetVoiceVolume(float volume)
        {
            VoiceVolume = Clamp01(volume);
            ApplyVolume(SoundGroup.Voice, VoiceVolume);
            Persist(KeyVoice, VoiceVolume);
            Game.Event?.Emit(Events.Settings.VoiceVolumeChanged, VoiceVolume);
        }

        /// <summary>设置画质档位（玩家侧入口）。</summary>
        public void SetQuality(QualityTier tier)
        {
            ApplyQuality(tier);
            Persist(KeyQuality, (int)tier);
            // ⛔ 不在这里直接 Emit：`ApplyQuality` 会同步触发引擎的 `OnLevelChanged`
            //    （= 下面的 `OnEngineQualityChanged`）⇒ 两处都发就会重复。统一走 `NotifyQualityChanged`（幂等）。
            NotifyQualityChanged((int)tier);
        }

        /// <summary>
        /// 引擎侧画质档位变更（自动降档 / 任何绕过本类直接调 `Game.Quality.SetLevel` 的路径）。
        /// 见 <see cref="HookEngineQuality"/> 的根因说明。
        /// </summary>
        private void OnEngineQualityChanged(QualityTier tier)
        {
            // 这条日志是"引擎自动降档"的运行时锚点（数值类 L3）：引擎自己那条 Warn 只写引擎日志前缀，
            // 而这一条落在项目日志里、与随后广播的 QualityChanged 同源，便于把"谁改的"对上。
            Game.Logger?.Warn(Tag,
                $"[Quality] engine-level-change → {tier}({(int)tier})（自动降档 / 外部 SetLevel），将广播 {Events.Settings.QualityChanged}");
            NotifyQualityChanged((int)tier);
        }

        /// <summary>
        /// 广播"画质档位已变更"的**唯一出口**。幂等：档位与上次广播过的一致时不重复发
        /// （两个来源会在同一次改档里先后调用本方法，见 <see cref="_lastNotifiedTier"/>）。
        /// </summary>
        private void NotifyQualityChanged(int tier)
        {
            if (_lastNotifiedTier == tier) return;
            _lastNotifiedTier = tier;
            Game.Event?.Emit(Events.Settings.QualityChanged, tier);
        }

        /// <summary>设置全屏。</summary>
        public void SetFullscreen(bool fullscreen)
        {
            ApplyFullscreen(fullscreen);
            Persist(KeyFullscreen, fullscreen);
            Game.Event?.Emit(Events.Settings.FullscreenChanged, fullscreen);
        }

        /// <summary>当前值快照（打开设置面板时传给面板，见 <see cref="SettingsSnapshot"/>）。</summary>
        public SettingsSnapshot Snapshot()
        {
            return new SettingsSnapshot
            {
                BgmVolume = BgmVolume,
                SfxVolume = SfxVolume,
                VoiceVolume = VoiceVolume,
                QualityTier = (int)QualityTier,
                Fullscreen = Fullscreen,
            };
        }

        // ───────────────────────── 内部：应用 / 落盘 ─────────────────────────

        private static float Clamp01(float v)
        {
            return Mathf.Clamp01(v);
        }

        private static void ApplyVolume(SoundGroup group, float volume)
        {
            if (Game.Sound == null)
            {
                Game.Logger?.Warn(Tag, $"Game.Sound 为空，{group} 音量未应用（表现域未挂载？）");
                return;
            }
            Game.Sound.SetVolume(group, volume);
        }

        private void ApplyQuality(QualityTier tier)
        {
            if (Game.Quality == null)
            {
                Game.Logger?.Warn(Tag, $"Game.Quality 为空，画质档位 {tier} 未应用");
                return;
            }
            Game.Quality.SetLevel(tier);
        }

        private static void ApplyFullscreen(bool fullscreen)
        {
            // `Screen.fullScreen` 是 Unity 自己的开关（本项目约定：全屏直接用它，不经引擎）。
            Screen.fullScreen = fullscreen;
        }

        /// <summary>
        /// 写进 `Game.Setting` 并**立刻落盘**（"真改真存"：不依赖退出时统一保存，
        /// 因为崩溃 / 强杀时那次保存不会发生，玩家会看到"设置自己变回去了"）。
        /// </summary>
        private static void Persist<T>(string key, T value)
        {
            if (Game.Setting == null)
            {
                Game.Logger?.Error(Tag, $"Game.Setting 为空，{key} 未落盘");
                return;
            }
            Game.Setting.Set(key, value);
            Game.Setting.Save();
        }
    }
}
