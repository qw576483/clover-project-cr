using UnityEngine;
using UnityEngine.UI;

namespace CR.UI
{
    /// <summary>
    /// **可变长文本的显示截断**（项目侧调用口径）：本类转调引擎件 <see cref="CloverEngine.TextFit"/>
    /// （`clover-client-unity-engine/Runtime/Presentation/TextFit.cs`；出处 =
    /// `client/Assets/Scripts/UI/TextFit.cs:36-128`），保留项目侧类型名、常量与方法签名 ⇒ 调用点零改动。
    ///
    /// <para>
    /// <b>为什么需要它（有出处）</b>：引擎建文本节点的唯一入口
    /// <c>UIFactory.CreateText</c>（`clover-client-unity-engine/Runtime/Presentation/UIWidgets.cs:151-152`）
    /// 把溢出策略设成 <c>horizontalOverflow = Wrap</c> + <c>verticalOverflow = Overflow</c>
    /// —— 横排溢出会换行、**纵向溢出不受限**。于是"一行短标签"被喂进超长串时，文字换行后
    /// 继续往标签矩形外画，**盖住整个面板**（实测：主菜单昵称
    /// `preferredW=24576 > rectW=808`，整块面板被文字铺满）。
    /// uGUI 的 <c>HorizontalWrapMode</c> **只有 `Wrap` / `Overflow` 两个取值**，不存在
    /// "horizontalOverflow = Truncate" 这种写法 ⇒ "横向截断"只能自己把**字符串**裁短。
    /// </para>
    ///
    /// <para>
    /// <b>行为口径（引擎件内实现）</b>：原始文本装得下（`preferredWidth &lt;= rect.width`）
    /// 就**原样写入**（⛔ 不无端加省略号、⛔ 不改字号 —— 改字号是"糊过去"，不是截断）；
    /// 装不下时用**二分**找出"最长的、加上省略号后仍能装进矩形的前缀"，写入标签并留一条 Info。
    /// 判据口径与调用方的断言同源（同一个 `Text.preferredWidth`），所以
    /// `Clamp` 之后 `preferredW &lt;= rectW` **恒成立**。
    /// </para>
    ///
    /// <para>
    /// <b>调用口径</b>：截断只对"**内容长度由服务端数据决定、且必须单行显示**"的标签成立，
    /// 所以做成**显式调用**：由各面板在"把数据写进标签"的那一处调一次
    /// （`MainMenuPanel` 昵称 / `RoomPanel`·`RoomListPanel` 房名 / `ResultPanel`·`PausePanel`·`HudPanel` 状态行）。
    /// ⛔ 引擎的共用文本创建点（`CreateText`）不全局改成截断：那会把**多行说明文案**
    /// （如房间规则两行、加载提示）也裁成一行。
    /// </para>
    /// </summary>
    public static class TextFit
    {
        /// <summary>省略号（U+2026）。⛔ 不用三个 ASCII 点：CJK 字体下宽度不一致。</summary>
        public const string Ellipsis = CloverEngine.TextFit.Ellipsis;

        /// <summary>日志标签（与引擎件 <see cref="CloverEngine.TextFit"/> 的日志标签同值）。</summary>
        public const string LogTag = CloverEngine.TextFit.Tag;

        /// <summary>
        /// 截断后写入 <paramref name="label"/>，返回**实际写进去的字符串**。
        /// <paramref name="label"/> 为 null 时原样返回（调用方不必判空）。
        /// </summary>
        /// <param name="label">目标标签（矩形宽 = 可用宽度）。</param>
        /// <param name="raw">服务端/数据源给的原文。</param>
        public static string Clamp(Text label, string raw)
        {
            return CloverEngine.TextFit.Clamp(label, raw);
        }

        /// <summary>
        /// 量一串文本在**不受矩形宽度约束**时的单行像素宽（口径 = uGUI 的 `Text.preferredWidth`：
        /// `TextGenerator.GetPreferredWidth(text, GetGenerationSettings(Vector2.zero)) / pixelsPerUnit`）。
        /// </summary>
        public static float Measure(Text label, string text)
        {
            return CloverEngine.TextFit.Measure(label, text);
        }

        /// <summary>把标签**当前**文本按同规则裁一次（用于已在别处赋值的标签）。</summary>
        public static void ClampSelf(Text label)
        {
            CloverEngine.TextFit.ClampSelf(label);
        }
    }
}
