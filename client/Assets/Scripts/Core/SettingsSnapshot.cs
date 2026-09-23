namespace CR
{
    /// <summary>
    /// 设置面板打开时携带的当前值快照（纯数据）。
    ///
    /// <para>
    /// <b>为什么放在 `Core/`（而不是 `Module/Settings/`）</b>：面板⛔不许引 `CR.Module`（契约 §1），
    /// 而它需要"打开时知道当前音量/画质"才能把滑块摆在正确位置。
    /// 契约允许 `UI → Core`，因此这个只承载数据的结构放 `Core` 是唯一能同时满足两边的落点。
    /// </para>
    /// <para>
    /// 生命周期：`AppFlow` 在打开 `SettingsPanel` 前 new 一个现读的快照，
    /// 经 `Game.UI.Open&lt;SettingsPanel&gt;(snapshot)` 传进去；面板**只读**它，
    /// 改动一律走 `Events.Settings.*Request` 事件（⛔ 面板不许自己改状态，那是 Flow/SettingsManager 的事）。
    /// </para>
    /// </summary>
    public sealed class SettingsSnapshot
    {
        /// <summary>BGM 音量，0~1。</summary>
        public float BgmVolume = 1f;

        /// <summary>音效音量，0~1。</summary>
        public float SfxVolume = 1f;

        /// <summary>人声音量，0~1。</summary>
        public float VoiceVolume = 1f;

        /// <summary>画质档位（`QualityTier` 的 int 值：0=Low 1=Medium 2=High）。</summary>
        public int QualityTier;

        /// <summary>是否全屏。</summary>
        public bool Fullscreen;
    }
}
