using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI
{
    /// <summary>
    /// **可变长文本的显示截断**（D4 修复）。
    ///
    /// <para>
    /// <b>为什么需要它（根因，有出处）</b>：引擎建文本节点的唯一入口
    /// <c>UIFactory.CreateText</c>（`clover-client-unity-engine/Runtime/Presentation/UIWidgets.cs:151-152`）
    /// 刻意把溢出策略设成 <c>horizontalOverflow = Wrap</c> + <c>verticalOverflow = Overflow</c>
    /// —— 横排溢出会换行、**纵向溢出不受限**。于是"一行短标签"被喂进超长串时，文字换行后
    /// 继续往标签矩形外画，**盖住整个面板**（AI2 实测：主菜单昵称
    /// `preferredW=24576 > rectW=808`，整块面板被文字铺满）。
    /// uGUI 的 <c>HorizontalWrapMode</c> **只有 `Wrap` / `Overflow` 两个取值**，不存在
    /// "horizontalOverflow = Truncate" 这种写法 ⇒ "横向截断"只能自己把**字符串**裁短。
    /// </para>
    ///
    /// <para>
    /// <b>本类做什么</b>：给定标签与原始文本，若原始文本装得下（`preferredWidth &lt;= rect.width`）
    /// 就**原样写入**（⛔ 不无端加省略号、⛔ 不改字号 —— 改字号是"糊过去"，D4 明令禁止）；
    /// 装不下时用**二分**找出"最长的、加上省略号后仍能装进矩形的前缀"，写入标签并留一条 Info。
    /// 判据口径与 AI2 的实测断言同源（同一个 `Text.preferredWidth`），所以
    /// `Clamp` 之后 `preferredW &lt;= rectW` **恒成立**。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不放进引擎 / `CrUiStyle`</b>：引擎的共用文本创建点（`CreateText`）一旦全局改成
    /// 截断，会把**多行说明文案**（如房间规则两行、加载提示）也裁成一行 —— 那是另一类破坏。
    /// 截断只对"**内容长度由服务端数据决定、且必须单行显示**"的标签成立，所以做成**显式调用**：
    /// 由各面板在"把数据写进标签"的那一处调一次（见 `impact-radius.tsv` 的同族清单）。
    /// </para>
    /// </summary>
    public static class TextFit
    {
        /// <summary>省略号（U+2026）。⛔ 不用三个 ASCII 点：CJK 字体下宽度不一致。</summary>
        public const string Ellipsis = "\u2026";

        /// <summary>日志标签。</summary>
        public const string LogTag = "TextFit";

        /// <summary>
        /// 截断后写入 <paramref name="label"/>，返回**实际写进去的字符串**。
        /// <paramref name="label"/> 为 null 时原样返回（调用方不必判空）。
        /// </summary>
        /// <param name="label">目标标签（矩形宽 = 可用宽度）。</param>
        /// <param name="raw">服务端/数据源给的原文。</param>
        public static string Clamp(Text label, string raw)
        {
            var s = raw ?? string.Empty;
            if (label == null) return s;

            var limit = label.rectTransform.rect.width;
            if (limit <= 1f || s.Length == 0)
            {
                // 矩形宽度未知（布局还没算）或空串：原样写入，不做无法判定的裁剪。
                label.text = s;
                return s;
            }

            label.text = s;
            var natural = Measure(label, s);
            if (natural <= limit) return s; // 装得下 ⇒ 原样（不加省略号）

            // 二分：找最大的 k 使 (前 k 字 + 省略号) 装得下。二分对"宽度随长度单调不减"成立，
            // 结尾再向前退到**确实能装下**为止（换行点造成的轻微非单调由这一步兜住）。
            var lo = 0;
            var hi = s.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (Measure(label, s.Substring(0, mid) + Ellipsis) <= limit) lo = mid; else hi = mid - 1;
            }
            while (lo > 0 && Measure(label, s.Substring(0, lo) + Ellipsis) > limit) lo--;

            var kept = s.Substring(0, lo) + Ellipsis;
            label.text = kept;
            Game.Logger?.Info(LogTag,
                $"文本超宽已截断：标签={Path(label)} 原文={s.Length} 字 → 显示={lo} 字+省略号 " +
                $"(rectW={limit:0.0} 单行宽={natural:0.0})");
            return kept;
        }

        /// <summary>
        /// 量一串文本在**不受矩形宽度约束**时的单行像素宽（口径 = uGUI 的 `Text.preferredWidth`：
        /// `TextGenerator.GetPreferredWidth(text, GetGenerationSettings(Vector2.zero)) / pixelsPerUnit`）。
        ///
        /// <para>
        /// <b>为什么必须自己建 <see cref="TextGenerator"/> 量，而不是读 <c>label.preferredWidth</c></b>：
        /// uGUI 的 `preferredWidth` 走**缓存**的布局用生成器，刚 `label.text = 新值` 之后同一帧里读到的
        /// 还是**上一串文本**的宽度（本轮实测：3 字串 `rectW=58 / preferredW=44` 却被判超宽截成 1 字 +
        /// 省略号 —— 就是读到了旧值）。自建生成器每次现算 ⇒ 判定与裁剪都基于当前这串文本。
        /// </para>
        /// </summary>
        public static float Measure(Text label, string text)
        {
            if (label == null) return 0f;
            var gen = new TextGenerator();
            var settings = label.GetGenerationSettings(Vector2.zero);
            var ppu = label.pixelsPerUnit <= 0f ? 1f : label.pixelsPerUnit;
            return gen.GetPreferredWidth(text ?? string.Empty, settings) / ppu;
        }

        /// <summary>把标签**当前**文本按上面的规则裁一次（用于已在别处赋值的标签）。</summary>
        public static void ClampSelf(Text label)
        {
            if (label == null) return;
            var raw = label.text;
            var kept = Clamp(label, raw);
            if (kept != raw) label.text = kept;
        }

        private static string Path(Text label)
        {
            var t = label.transform;
            var p = t.name;
            var parent = t.parent;
            var guard = 0;
            while (parent != null && guard++ < 16)
            {
                p = parent.name + "/" + p;
                parent = parent.parent;
            }
            return p;
        }
    }
}
