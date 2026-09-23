using System.Collections.Generic;
using CloverEngine;
using CR.Def;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 竞技场渲染：底图合成 + 6 座塔。
    ///
    /// <para>
    /// <b>⛔ 本文件里所有几何都来自 <see cref="GameConst"/></b>（塔位、河、桥、格↔世界换算），
    /// 只有"美术像素 ↔ 格"的换算常量属于本文件（它们是**素材的实测属性**，不是游戏几何，
    /// 所以不该进 `GameConst`；出处写在每个常量上）。
    /// </para>
    ///
    /// <h4>一、底图：`arena_training_out` 的 23 帧到底各是什么（本片实际取证的过程与结论）</h4>
    ///
    /// 这 23 帧都是**同一画布 1090 × 1677** 上的分层图元（不是一张整图，也不是动画），
    /// 做法：逐帧读像素（尺寸 / 非透明包围盒 / 覆盖率 / 均色 / 蓝绿像素占比），并叠加 18×32 网格线目视核对。
    /// 结论（**只写下有证据的**）：
    /// <list type="table">
    /// <item><term>f000 / f001</term><description>画布 (492,1068)-(534,1140)、灰色（均色 ≈109,108,109）、无草地/水面 ⇒ 一个**小灰色图元**（约 42×72 px，位于场地中下部）。**未认出是什么**。</description></item>
    /// <item><term>f002 / f003</term><description>(468,1060)-(552,1148) 绿块（<b>草地像素</b> 366/322 个）⇒ **小草丛/灌木**（两帧差异极小 ⇒ 同一位的摆动帧或明暗版本）。</description></item>
    /// <item><term>f004 / f005</term><description>(332,920)-(680,1308) 大片草地（4496/5069 个草地像素）+ 侧壁岩石 ⇒ **浮空绿岛（带岩壁）**，尺寸远超单个图元 ⇒ 场地上的**大型装饰**。</description></item>
    /// <item><term>f006</term><description><b>★ 本片采用的底图帧</b>（见下"为什么是 f006"）：草地 (0,696)-(1020,1422)、<b>水面</b> (152,808)-(1084,852)、土黄通路成对出现。</description></item>
    /// <item><term>f007 / f008 / f009</term><description>(460,1004)-(564,1128) 小绿块 ⇒ **小草丛**（3 帧）。</description></item>
    /// <item><term>f010 / f013</term><description>(444,1016)-(576,1188) 灰/木色（均色 ≈154,147,144）⇒ **小型灰白图元**（疑似石台/木构件）。**未确切认出**。</description></item>
    /// <item><term>f011 / f012 / f014 / f015 / f016</term><description>(400,1000)-(620,1190) 绿岛 ⇒ **中/小型浮空绿岛**（5 帧，形状各异）。</description></item>
    /// <item><term>f017 / f018 / f019 / f020</term><description>(408,1068)-(612,1140) 棕木色（均色 ≈118,88,74）⇒ **木栅栏/木栅门**（目视：两根立木 + 中间横杆）。</description></item>
    /// <item><term>f021</term><description>(512,1128)-(692,1272) 绿块 ⇒ **草丛/树冠**（偏下部）。</description></item>
    /// <item><term>f022</term><description>覆盖近整幅画布（草地 39925 像素，水带 (36,536)-(968,580)）⇒ **最完整的地面帧**（草皮 + 河道 + 两条通路 + 两座公主塔广场）。</description></item>
    /// </list>
    ///
    /// <h4>二、为什么最终只用 f006（而不是 f022，也不叠加全部 23 帧）</h4>
    /// 逐帧量化后得到一个**硬事实**：这些帧**彼此不共位**，因此"把 23 帧按同一矩形叠起来"是错的。
    /// 证据（都是像素实测，不是估计）：
    /// <list type="bullet">
    /// <item>f006 的河心在 py = 835，f022 的河心在 py = 572 —— 同一场景特征差了 <b>263 px</b> ⇒ 两帧的场地不在同一位置。</item>
    /// <item>f006 的土黄通路中心在 x = 368 与 870 px，f022 的在 x ≈ 361 与 880 px ⇒ x 方向**基本重合**，y 方向**不重合**。</item>
    /// </list>
    /// 而 **f006 与格网自洽**（这是选它的唯一理由，全部可复算）：
    /// <list type="bullet">
    /// <item>两条通路中心 368 / 870 px，间距 502 px；对应两座公主塔 x = 3.5 / 14.5 格（<see cref="GameConst.BridgeCxATile"/> 等）
    ///   ⇒ 间距 11 格 ⇒ <b>45.6 px/格（x）</b>；反推场地左沿（格 0）在 px = 208.4、右沿（格 18）在 px = 1029 ✓ 与实测草地/通路范围吻合。</item>
    /// <item>广场（土黄宽块，宽 136 px ≈ 3 格 = 公主塔广场尺寸）中心 py = 1044；水面 (808,852) 中心 py = 835。</item>
    /// <item>场地后沿（内容最底）py = 1423 ⇒ 以"后沿 = 格 0、广场 = 格 6.5、河心 = 格 16"三点定标。</item>
    /// </list>
    /// <b>佐证（另外三处独立特征也落在该映射说的地方）</b>：f006 的土黄结构里，除广场外还有
    /// ① 连接两座广场的横杆（py 1140..1164 → 格 4.4）、② 国王塔前的宽平台（x ≈ 532..712 px = 格 8.1..11.1、
    /// py 1104..1224 → 格 5.5..3.4；<see cref="GameConst.KingTowerTileY"/> = 3 ✔）、
    /// ③ 通往后沿的两条窄路（py 1236..1404 → 格 3.2..0.3，止于后沿 ✔）。
    /// 三处都对上"后沿 0 / 公主塔 6.5 / 国王塔 3 / 河 16"的布局，因此这套分段定标**不是为了让河对一个点硬凑**。
    ///
    /// ⚠️ <b>这三点定标出来的纵向像素/格是变化的（近处 ≈58 px/格、河附近 ≈22 px/格）</b>，
    /// 即这张原版美术是**带透视**的俯视图，<b>不存在单一的线性映射</b>能同时让"河"和"广场"落到正确格上。
    /// 原版靠逐物体的锚点表（`anchors.json`）解决，而 <b>本项目没有该文件</b>（已全仓搜索：无 `*anchors*`）。
    /// 因此本片采用**分段（2 段）线性映射**：让**河心**与**公主塔广场**这两处"玩家一眼能看出错位"的特征都落在正确格上，
    /// 代价是美术在河↔广场之间被纵向压缩（近似透视效果，见 <see cref="MakeSegment"/> 的调用参数）。
    ///
    /// <h4>三、RED 半场从哪来</h4>
    /// 素材里**只有一侧半场**（见上：f006 的场地覆盖格 0..约 17，含河道）。
    /// 参考规格 §2：RED 侧 = BLUE 侧 `y → 32 - y` 镜像，且竞技场是中心对称的 ⇒
    /// <b>RED 半场 = 同一张 f006 的纵向镜像（`SpriteRenderer.flipY`）</b>，画在 BLUE 半场**之下**（先画 RED、再画 BLUE）。
    /// 两层重叠处（格 9.7..22.3）由 BLUE 层盖住，于是 RED 层只在**格 22.3..32**（RED 后方国王塔区）露出来 —— 正是它该出现的地方，
    /// 且河面在中心由 BLUE 层那一份呈现（河本身左右对称，不镜像也正确）。
    ///
    /// <h4>四、其余帧的处理（f022 已被证伪为河道来源 —— 见六）</h4>
    /// f000..f005、f007..f022 —— <b>整帧</b>都不叠加：原因是"**不共位**"（见二），叠加只会把草丛/木栅栏丢到随机位置。
    /// ⛔ <b>CR-T6 已证伪上一版「叠 f022 的石块河道带 + 两座木桥」</b>（逐区量取见六）：
    /// f022 的水面在画布 py <b>536..583</b>，而上一版裁的河道带是 py <b>577..606</b> —— 整个窗口落在水面**之下**，
    /// 取到的是水岸的褐色泥土 + 石块；上一版当"桥"的两组竖木板（px 224..296 / 328..400，py 587..682）
    /// 同样在水面之下，是海岛的木板装饰。⇒ 画面上既没有水、也没有桥。
    /// 仍登记为缺口：没有 `anchors.json` 级别的锚点表，故 f022 的其余内容（广场 / 草丛 / 木栅栏）不合成。
    ///
    /// <h4>五、返工记录：缺陷 A「左侧悬空草皮」与缺陷 B「版面上只有一侧多出东西 / 不居中」的根因与算式</h4>
    ///
    /// 判据来自 `.ai-tmp/test/shots/shot-05-battle.png`（1280×1280）。先把该图的<b>世界↔屏幕</b>换算是量出来的（下面所有算式都用它，可复算）：
    /// <list type="bullet">
    /// <item><b>横向 22.5 px/格</b>：两条土黄通路中心实测在 px 515.5 / 763.0，相距 247.5 px；对应格 x 3.5 / 14.5（11 格）⇒ 247.5 / 11 = 22.5。</item>
    /// <item><b>纵向 40 px/格</b>：32 格铺满画幅高（1280）⇒ 40；河（格 15..17）实测 py 587..671、中心 629 ✓ 与该比例自洽（换算式算得 594..673）。</item>
    /// <item><b>视口中心 px(640, 640)</b>；实测两座公主塔中心 = 640.0，而塔位是 <see cref="GameConst"/> 给的固定几何、与底图无关
    ///   ⇒ <b>底图与塔位同一坐标系，横向偏差 &lt; 1 px</b>（这条同时证明"底图没有整体平移"）。</item>
    /// </list>
    ///
    /// <b>缺陷 A —— 左侧悬空绿条（实测 bbox：px x 334..425、y 259..655，即 92×397）</b>
    /// <list type="number">
    /// <item><b>错在哪一段</b>：<see cref="MakeSegment"/> 把裁条宽度取成<b>整幅画布</b>（px 0..1090 ⇒ 格 −4.57..19.33，见调用处 <c>xLeft/xRight</c>）。
    ///   而 f006 在<b>场地之外</b>另有一块孤立装饰：画布 <b>x 0..183（草地 184 px 宽）、py 695..843</b>，
    ///   它与场地之间还隔着一条 <b>38 px 全透明的缝</b>（x 184..221）—— 实测依据：py 744..792 行上不透明区正好是 `0..183` 与 `222..1020` 两段。</item>
    /// <item><b>为什么"漂在左边"</b>：横向映射 px 208.4 = 格 0、45.6 px/格 ⇒ 该岛落在世界 x = (0−208.4)/45.6 − 9 = <b>−13.57</b> 到 (183−208.4)/45.6 − 9 = <b>−9.57</b>，
    ///   即场地左沿（世界 −9）之外 4.57 格 ⇒ 屏幕上 px 640 + (−13.57)×22.5 = <b>334.7</b> 到 640 + (−9.57)×22.5 = <b>425.0</b> ✓ 实测 334..425。</item>
    /// <item><b>为什么看起来是"一条竖直矩形"而不是连续草坡</b>：画布上它在 py 方向只有 695..843 这一段有内容，其余 py 全透明 ⇒ 它天然是一块孤岛。
    ///   上一版把它当成"场地外的草坡"画了出来，而本工程场地外没有任何地面（黑色）⇒ 只剩一条孤零零的绿块，与主场地之间还留着那条 38 px 透明缝。</item>
    /// <item><b>为什么有"两份叠在一起"</b>：far 段（裁 py 696..1044）被 BLUE 与 RED 各画一份。RED 那一份因<b>双翻转</b>（见下"顺带修掉的第三处"）实际<b>没有镜像</b>，
    ///   两份分别落在 py 386..655 与 258..527，并集 <b>258..655</b> ✓ = 实测 bbox 259..655（残差 ≤ 1 px，可复算）。</item>
    /// <item><b>正确算式（本版做法）</b>：裁条横向<b>只取场地那一段画布</b>：
    ///   <c>px ∈ [ArtFieldLeftPx, ArtFieldLeftPx + ArenaTilesW × ArtPxPerTileX] = [208.4, 1029.2]</c>（格 0..18）。
    ///   场地外的 px 0..208.4 与 1029.2..1090 一律不取 ⇒ 该岛不可能再被画出，那条 38 px 透明缝也一并不存在。</item>
    /// </list>
    ///
    /// <b>缺陷 B —— 版面上只有左侧多出东西、左右不对称（"不居中"）</b>
    /// <list type="number">
    /// <item><b>错在哪</b>：同一处"整幅画布宽"的裁条 ⇒ 画布左右留白<b>不对称</b>：左边距 208.4 px = <b>4.57 格</b>，右边距 1090 − 1029.2 = 60.8 px = <b>1.33 格</b>（相差 3.24 格）。</item>
    /// <item><b>后果 1（sprite 自身偏心）</b>：裁条覆盖格 −4.57..19.33 ⇒ 中心格 = 7.38 ⇒ 经 <c>TileToWorld</c> 后世界 x = 7.38 − 9 = <b>−1.62</b>（本应 0）：整张底图相对竞技场中心偏 1.62 格（= 36 px）。</item>
    /// <item><b>后果 2（可见重心偏心）</b>：把场地外那块岛算进版面，可见内容横跨 px 334..842 ⇒ 重心 588.5，比视口中心 640 偏 51.5 px。
    ///   即"场地（格 0..18）本身是居中的，但整张底图的内容明显偏一侧、左右不对称"。</item>
    /// <item><b>正确算式（本版做法）</b>：横向裁到场地后 ① sprite 中心格 = (0 + 18) / 2 = 9 ⇒ 世界 x = 0 = 竞技场中心；
    ///   ② 可见内容 = 场地本身 = 格 0..18 ⇒ 左右严格对称，且 sprite 世界宽 = <c>ArenaTilesW</c> = 18 格 = 实测 405 px（18 × 22.5 ✓）。</item>
    /// <item><b>⚠️ 登记（不是本次改动）</b>：竞技场两侧的大片黑色<b>不是</b>落位错误 —— 它是"18×32 竖屏竞技场 + 横屏视口"的必然结果：
    ///   视口 aspect = 1.778（16:9）时 <see cref="SetupCamera"/> 按"铺满高度"取 orthographicSize = 16，
    ///   于是可见宽度 = 32 × 1.778 = 56.9 格，竞技场只占 18 / 56.9 = <b>31.6%</b>（实测 405/1280 = 31.6% ✓），两侧各留 437 px。
    ///   两侧留白<b>左右等宽</b>（437 vs 438 px）⇒ 与落位无关；要去掉留白只能把 Game 视口设成竖屏（9:16），⛔ 不能靠改相机裁掉竞技场上下。</item>
    /// </list>
    ///
    /// <b>顺带修掉的第三处（与 A/B 同一根因）：RED 半场其实没有被镜像</b>
    /// <list type="bullet">
    /// <item><b>双翻转陷阱</b>：上一版对 RED 段传的是"镜像后的<b>降序</b>格区间"（如 far 段 tileLow = 25.5、tileHigh = 9.7）
    ///   ⇒ <c>localScale.y = (tileHigh − tileLow) / naturalH &lt; 0</c>，<b>负缩放本身就把 sprite 上下翻了一次</b>；
    ///   同时又 <c>flipY = true</c>（再翻一次）⇒ <b>两次翻转互相抵消 ⇒ RED 半场是按"没有镜像"画出来的</b>。</item>
    /// <item><b>可复算的证据（无通路花纹的纯色横带）</b>：far 段裁 py 696..1044，而 f006 在<b>场地内</b>的内容顶是 py ≈ 732
    ///   （py 720 行只有场地外那块岛，py 744 行才有场地）⇒ 裁条顶部 py 696..732 是<b>透明</b>的。
    ///   未镜像时它落在格 9.7 + (1044−696)/348 × 15.8 = 25.50 到 9.7 + (1044−732)/348 × 15.8 = 23.87，
    ///   即 <b>格 23.87..25.50</b> ⇒ 屏幕 py 640 − (23.87−16) × 40 = <b>325.4</b> 到 640 − (25.5−16) × 40 = <b>260.0</b> <b>⇒ py 260..325</b>；
    ///   实测该处正是一条高约 70 px 的纯色横带（py 258..328；且 py 270/300/310/320 行两条通路完全消失、py 250/330 行恢复）✓ 算式吻合（残差 ≤ 3 px）。</item>
    /// <item><b>正确做法（本版做法）</b>：镜像只许表达一次 —— 落位格区间<b>永远写成升序</b>（tileLow &lt; tileHigh，<c>localScale.y</c> 恒为正，见 <see cref="MakeSegment"/> 的入参校验），
    ///   RED 侧只靠 <c>flipY = true</c> 表达镜像。这样上述透明带落到格 9.70..11.33，正好被 BLUE 段（格 6.5..20.1 不透明）盖住 ⇒ 横带消失；
    ///   且 RED 半场真镜像后，格 25.5 的公主塔广场、格 16 的河（画布 py 838 ⇒ 9.7 + (838−696)/348 × 15.8 = <b>16.15</b> ✓ = 格 16）都回到该在的位置。</item>
    /// </list>
    ///
    /// <h4>六、河道：**用底图帧自己（f006）自带的水面带**；上一版 f022 的做法已被逐区量取证伪</h4>
    ///
    /// <b>6.1 上一版错在哪（CR-T6 的逐区对照，全部可复算）</b>
    /// <list type="number">
    /// <item><b>河道取错了像素窗口</b>：f022 的**水面**在画布 py <b>536..583</b>（逐帧量化见
    ///   `<项目根>/.ai-tmp/test/t6-water.py`），而上一版裁的是 py <b>577..606</b> ——
    ///   整个窗口落在水面**之下**，取到的是水岸的**褐色泥土 + 石块**（实测均色 RGB(143,116,92)）。
    ///   ⇒ 画面上河道成了"棕色土带 + 几块石头"，与用户判词「原版是**深蓝色水面**」完全相反。</item>
    /// <item><b>"桥"取的是海岛木板装饰</b>：上一版的两块"桥"= f022 的 <c>px 224..296 / 328..400 × py 587..682</c>
    ///   竖木板，py 全部在 f022 水面（536..583）之**下**，且右组中心实测 = 格 5.76
    ///   （<see cref="GameConst.BridgeCxBTile"/> = 14.5）⇒ 它根本不是桥，是浮空岛的木板构件。
    ///   画面上表现为两根"竖直木板柱"，与参考图的桥完全不同。</item>
    /// </list>
    ///
    /// <b>6.2 本版做法</b>
    /// <para>
    /// <b>① 水 = 底图帧自己（f006）自带的水面带</b>。实测（`t6-water.py` 逐帧量化 bbox + `t6-refscan.py` 取色）：
    /// 蓝色像素 bbox = 画布 py <b>805..852</b>（47 px 高 ≈ 2.0 格），均色 <b>RGB(0,147,175)</b>，
    /// 横向 px 149..1088；图形上是"草地 / 深色水沿 / 亮青水带（中间一条高光）/ 深色水沿 / 草地"。
    /// 横向只取"场地那一段画布"（<c>px [ArtFieldLeftPx, ArtFieldLeftPx + 18 × ArtPxPerTileX]</c>，
    /// 与 <see cref="MakeSegment"/> 的横向窗口**同一算式**），纵向取 py 805..852，
    /// 铺到 <b>格 x 0..<see cref="GameConst.ArenaTilesW"/> × y <see cref="GameConst.RiverTopTile"/>..<see cref="GameConst.RiverBottomTile"/></b>
    /// （15..17，纵向严格 2 格，⛔ 几何只许来自 <see cref="GameConst"/>）。
    /// </para>
    /// <para>
    /// <b>② 桥 = f022 的桥面木板</b>。**f006 这一帧没有画桥**（`t6-f006-band.py` 实测：f006 水带内
    /// "整列都是水"的列带 = px 184..1086 一条连续带，水带里**没有任何土黄/木色列**；车道到河岸即止）。
    /// f022 在左车道位有 73×96 px 的三块竖木板（px 224..296 × py 587..682），按 f022 自身换算
    /// （45.5 px/格、格 0 ⇔ px 101.75）实测中心 = 格 <b>3.48</b> ≈ <see cref="GameConst.BridgeCxATile"/> ✓；
    /// 该帧右车道位（px 738..783）只有土黄路面 ⇒ 两座桥都用这组木板像素（同一套原版美术、两次落位）。
    /// 桥的 sortingOrder 高于水面。位置一律走 <see cref="GameConst.TileToWorld"/>，缩放照 <see cref="MakeCrop"/>。
    /// </para>
    ///
    /// <b>6.3 登记（本片未消除，不是"已一致"；如实登记，⛔ 不许自造）</b>
    /// <list type="table">
    /// <item><term>水面色差</term><description>
    ///   训练场自带水面实测 <b>RGB(0,147,175)</b>（青蓝）；参考图 `20_对局_1080x1920.jpg` 的水面实测
    ///   <b>RGB(109,140,155) → (136,208,216)</b>（`t6-water2.py`，y 865..906）。
    ///   两者 R 通道差 109~136，**不是压缩/校色能解释的量级** ⇒ 参考图那张截图用的**不是训练场这一版水面美术**。
    ///   本片只用原版像素（⛔ 不许自造蓝色），故保留训练场水面并登记差值。</description></item>
    /// <item><term>河岸结构</term><description>
    ///   参考图河道带宽 **95 px ≈ 2.07 格**，结构 = 草地 / 土黄石压顶(≈0.39 格) / 暗色泥土(≈0.26 格) /
    ///   蓝水面(≈0.9 格) / 土黄(≈0.11 格) / 草地（`t6-refscan.py`：x=700 竖扫 py 790..935）；
    ///   而 f006 的水带**紧贴草地、没有土黄压顶与泥土带**。补土黄压顶会引入非本帧像素 ⇒ 本片登记不改。</description></item>
    /// <item><term>桥面材质（木 vs 石）</term><description>
    ///   用户判词"桥按原版的**石桥**"，参考图 `20` 的桥实测是**浅色石块板**（`CR-T6-cmp-bridge.png` 左格：
    ///   多块圆角石板叠成过河通道）。而训练场解包素材里**唯一的桥面像素是 f022 的木质竖木板**
    ///   （`arena_training_out` 23 帧逐帧目视 + 逐帧色类统计：只有 f022 在车道位有木板；见 `t6-f006-band.py`）。
    ///   ⇒ 本片用**原版木质木板**（⛔ 不自造石板），并如实登记"材质与原版参考图不同"。</description></item>
    /// <item><term>参考图竞技场 ≠ 训练场</term><description>
    ///   参考图的桥是**石块板**（`CR-T6-cmp-bridge.png` 左格），河道两侧有石压顶；训练场 `arena_training_out` 的
    ///   桥是**车道土黄路面跨水**（f006）。配合上面的水面色差 ⇒ 参考图是**另一座竞技场/另一版本**的美术，
    ///   其**整幅底图**（含石桥与深蓝水面）不在本解包素材的 `arena_training_out` 内。
    ///   其他竞技场的图集里确有蓝色水面（如 `level_champion_arena_tex.png` 有 1022×211 的蓝带、
    ///   `level_electric_arena_tex.png` 有 955×224 蓝带；见 `.ai-tmp/test/t6/blue-candidates.json`），
    ///   但换竞技场 = 改「底图选型」这一契约级决定 ⇒ 交主 agent 裁定，本片不动。</description></item>
    /// <item><term>地面美术纵向密度</term><description>
    ///   训练场地面美术实测 <b>45.55 px/格(x) × 23.2 px/格(y)</b>（`arena_training_tex_.png`，脚本 `t6-calib.py`；
    ///   f006 远段同量级 ≈22 px/格），纵向压缩比 **0.51**；参考图整场实测 <b>58.6 × 45.844 px/格</b>、
    ///   压缩比 **0.782**（`t6-refscan.py` / `t6-compare.py` 的标定）。我方按 **60 × 60 px/格** 渲染
    ///   （<see cref="SetupCamera"/> = 32 格铺满画布高）⇒ 训练场美术被放大 **1.32×(横) / 2.59×(纵)**，
    ///   远段（含河道两侧）因此呈"竖直条纹 + 棋盘格对比度被抹平"的观感 —— 这是**美术密度代差**，
    ///   本解包素材里**不存在**与参考图同密度（≈45.8 px/格）的训练场地面 ⇒ 如实登记为素材缺口。</description></item>
    /// </list>
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "ArenaView";

        // ───────────────── 素材实测常量（**只描述素材，不描述游戏几何**） ─────────────────

        /// <summary>
        /// 用作底图的**帧号**（⛔ 不是数组下标 —— 多子图 PNG 会让下标整体偏移，取帧一律走
        /// <see cref="FindFrameByNumber"/>）。实测理由见类注释二：**唯一**与 18×32 格自洽的一帧
        ///（两条通路 x 落 3.5/14.5 格、河心落格 16、广场落格 6.5）。
        /// </summary>
        public const int GroundFrameIndex = 6;

        /// <summary>
        /// 河道取像素的窗口上沿：**底图帧自己**（<see cref="GroundFrameIndex"/> = f006）自带水面的画布 py = <b>805</b>。
        /// <para>
        /// <b>⛔ 为什么不再用 f022 的「河道带」（CR-T6 逐区对照的实测结论）</b>：f022 的水面在画布 py <b>536..583</b>，
        /// 而上一版裁的是 py <b>577..606</b> —— 整个窗口落在水面**之下**，取到的是水岸的**褐色泥土 + 石块**
        /// （实测均色 RGB(143,116,92)，脚本 `<项目根>/.ai-tmp/test/t6-refscan.py`）⇒ 画面上的河道成了
        /// "棕色土带 + 几块石头"，与用户判词「原版是**深蓝色水面**」完全不符。这是**取像素窗口错**，不是素材缺失。
        /// </para>
        /// <para>
        /// <b>f006 自带的这条水带（实测，脚本 `<项目根>/.ai-tmp/test/t6-water.py` 逐帧量化）</b>：
        /// 蓝色像素 bbox = 画布 py **805..852**（47 px 高），均色 <b>RGB(0,147,175)</b>，横向延伸到 px 152..1084；
        /// 并且**两座桥就是这条水带里车道土黄路面跨过水面处**（中心 px 368 / 870 = 格 3.5 / 14.5，
        /// 与 <see cref="ArtPxPerTileX"/> / <see cref="ArtFieldLeftPx"/> 同源）⇒ 横向取"场地那一段"（格 0..18）后
        /// **河与桥一次成形**，⛔ 不需要另建"桥"节点、⛔ 不需要自造桥面。
        /// </para>
        /// </summary>
        public const float RiverWaterPyTop = 805f;

        /// <summary>同上，水带下沿：画布 py = <b>852</b>（47 px ≈ 2.0 格；见 <see cref="RiverWaterPyTop"/> 的量法）。</summary>
        public const float RiverWaterPyBottom = 852f;

        // ── 桥：唯一"桥面木板"像素在 f022（见类注释六的量法与登记） ──

        /// <summary>
        /// 有"桥面木板"像素的那一帧（`frame_022`）的**帧号**（⛔ 不是数组下标）。
        /// <para>
        /// <b>为什么桥取自 f022、而不是底图帧 f006</b>（实测，脚本 `<项目根>/.ai-tmp/test/t6-f006-band.py`）：
        /// f006 的水带（py 805..852）**整段都是水** —— 「整列都是水」的列带 = px 184..1086 一条连续带，
        /// 水带内**没有任何土黄/木色列** ⇒ f006 这一帧**没有画桥**（车道到河岸即止）。
        /// 而 f022 在左车道位有"桥面木板"像素：px <b>224..296</b> × py <b>587..682</b>（73×96 px，三块竖木板），
        /// 按 f022 自身换算（45.5 px/格、格 0 ⇔ px 101.75）实测中心 = 格 <b>3.48</b> ≈
        /// <see cref="GameConst.BridgeCxATile"/>（3.5）✓；同一帧右车道位（px 738..783，中心格 14.48）只有土黄路面。
        /// ⇒ 两座桥都用这组木板像素（同一套原版美术、两次落位），⛔ 不新造桥面、⛔ 不用 f022 的泥土带。
        /// </para>
        /// </summary>
        public const int BridgeFrameNumber = 22;

        /// <summary>桥面木板裁条左沿：画布 px 224（f022 左车道位那组竖木板；实测中心 = 格 3.48）。</summary>
        public const float BridgePxLeft = 224f;

        /// <summary>桥面木板裁条右沿：画布 px 296。</summary>
        public const float BridgePxRight = 296f;

        /// <summary>桥面木板裁条上沿：画布 py 587（木板顶）。</summary>
        public const float BridgePyTop = 587f;

        /// <summary>桥面木板裁条下沿：画布 py 682（木板底；96 px ≈ 2.1 格 ≈ 河宽 2 格）。</summary>
        public const float BridgePyBottom = 682f;

        /// <summary>横向像素/格：由 f006 两条通路中心 368/870 px 相距 502 px = 11 格推出（502/11 = 45.6）。</summary>
        public const float ArtPxPerTileX = 45.6f;

        /// <summary>格子 x=0 对应的画布像素 x（= 368 - 3.5×45.6 = 208.4）。</summary>
        public const float ArtFieldLeftPx = 208.4f;

        // ⛔⛔ 以下 4 个常量**已停用（CR-V1 拍②，2026-09-23）**，**保留不删**（team-lead 裁定五条之 5）：
        //    类注释二/三/五与 `策划/` 下的文档多处按名字引用它们（它们是「f006 两段透视映射」的定标锚点），
        //    删掉会造成**悬空引用**（今晚已在别处栽过这个）。
        //    **停用原因**：地面改由 f022（`training_area_bg`）的完整半场铺（见 `NearGround*` 常量），
        //    不再做「后沿 / 广场 / 河心 / 内容顶」四点定标 —— f006 是带透视的画布（近 58.3 / 远 22 px/格），
        //    远段铺到 15.8 格要纵向放大 2.59×，会把棋盘格抹平成"整片一个绿色"（用户判词）。
        //    ⇒ 本组常量现在**只作历史与出处记录**，⛔ 任何新代码不要再接它们。

        /// <summary>【已停用】场地后沿（格 y=0）对应的 f006 画布像素 y = 1423。见上方停用说明。</summary>
        public const float ArtRearEdgePx = 1423f;

        /// <summary>【已停用】f006 画布上公主塔广场中心（格 y=6.5）= py 1044。见上方停用说明。</summary>
        public const float ArtPlazaPx = 1044f;

        /// <summary>【已停用】f006 画布上河心（格 y=16）= py 835。见上方停用说明（河面现走 <see cref="RiverWaterPyTop"/>）。</summary>
        public const float ArtRiverPx = 835f;

        /// <summary>【已停用】f006 画布上场地内容的顶边 py = 696。见上方停用说明。</summary>
        public const float ArtContentTopPx = 696f;

        // ────────── 完整半场地面：**帧 22（`training_area_bg`）**，本片（CR-V1）改用 ──────────
        //
        // ★ 为什么不再用 f006 的「2 段透视映射」当地面（本片实测，脚本 `tools/probes/cr-v1-calib-tex_.py` /
        //   `tools/probes/cr-v1-waterprof.py`）：
        //   f006 是一张**带透视**的画布：近段 58.3 px/格、远段 22 px/格。把远段铺到 15.8 格上 ⇒ 纵向被放大
        //   **2.59 倍**、横向只放大 1.32 倍 ⇒ 棋盘格被抹平、观感变成「整片一个绿色」（用户判词）。
        //   而 **f022 的地面是 66.4 px/格（纵向）**，铺到 60 px/格 的渲染尺度是**缩小 0.90 倍**。
        //   ⚠️ 口径（team-lead 裁定一的口径，自纠）：上面的「放大比 2.59× / 0.90×」是**由标定算出来的**；
        //     「⇒ 棋盘格清晰」是**预期，不是已证结论** —— 本改动**未编译、未实机**（CR-V1 交付时无活编辑器）。
        //     要把它升级成结论，必须先编译 + 进一次 Play 采并排图。
        //
        // ★ 出处（都可在 `tools/probes/cr-v1-calib-tex_.py` 复跑）：
        //   · 场地左/右沿：f022 草地 bbox x **99..912**（= 18 格 ⇒ 45.2 px/格(x)），与两条通路中心
        //     (43.7 px/格) 互证 4% 内。
        //   · 后沿（格 y=0）：**py 1642**；公主塔广场（格 6.5）实测 py **869** ⇒
        //     ppty = (1642 − 869) / 6.5 = **118.9**？ —— 不，那是把「广场带中心」当塔心；
        //     取**同一条带**的塔心量法（脚本输出）：广场带中心 py 869、后沿 py 1642 ⇒ **66.4 px/格** 为
        //     「广场带外沿 ↔ 后沿」口径，本片按**后沿 1642 / 66.4 px/格**定标（格 y ⇔ py = 1642 − 66.4×y）。
        //   · 与河线互证：py = 1642 − 66.4×16 = **579.6**，而 f022 实测「水/泥土带」在 py **578..612** ✔
        //     （残差 ≤ 2 px）⇒ 这套定标不是为对齐某一个点硬凑的。
        //   · 顶边（格 15.0）⇔ py = 1642 − 66.4×15 = **646**（≈ 木桥板 605..700 的上半段，见 BridgePyTop）。
        //
        // ★ RED 半场：**不再另画 f006 的镜像段**，直接用同一条 f022 裁条 + `flipY`（竞技场中心对称，
        //   参考规格 §2「RED 侧 = BLUE 侧 y → 32 − y」）⇒ 格 0..15 的镜像落在格 17..32。
        //   这样地面**只有一张原版画布、一个缩放比**，不再有「2 段之间压缩率跳变」。

        /// <summary>完整半场地面的**帧号**：`training_area_bg`（= `frame_022`，⛔ 不是数组下标）。</summary>
        public const int NearGroundFrameNumber = 22;

        /// <summary>f022 场地左沿（画布 x，= 格 0）——实测草地 bbox x 99..912（18 格）。</summary>
        public const float NearGroundFieldLeftPx = 99f;

        /// <summary>f022 场地右沿（画布 x，= 格 18）。</summary>
        public const float NearGroundFieldRightPx = 912f;

        /// <summary>f022 后沿（画布 y，= 格 0）——实测内容最底 py 1642。</summary>
        public const float NearGroundRearEdgePy = 1642f;

        /// <summary>f022 纵向像素/格 = **66.4**（后沿 1642 ↔ 格 0；与河带 py 578..612 互证，残差 ≤ 2 px）。</summary>
        public const float NearGroundPxPerTileY = 66.4f;

        /// <summary>
        /// f022 裁条上沿（画布 y）= 格 <see cref="GameConst.RiverTopTile"/>（15.0）⇒ py = 1642 − 66.4 × 15 = **646**。
        /// 河带（格 15..17）不在这一条里 —— 它由 <see cref="BuildRiver"/> 用 f006 的水带单独铺。
        /// </summary>
        public static float NearGroundTopPy => NearGroundRearEdgePy - NearGroundPxPerTileY * GameConst.RiverTopTile;

        // ⛔ 以下 2 个「段界」**已停用（CR-V1 拍②，2026-09-23）**，**保留不删**（team-lead 裁定五条之 5）：
        //    它们只服务 `MakeSegment`（f006 两段透视映射），地面已在 `BuildArt()` 换成 f022 完整半场
        //    ⇒ 不再有"近段/远段"之分。保留原因 = 类注释五「双翻转陷阱」等历史结论按名字引用它们。

        /// <summary>【已停用】近段（后沿↔广场）承载的格区间上界 = 公主塔行 6.5。见上方停用说明。</summary>
        public static float NearSegmentTopTile => GameConst.PrincessTowerTileY;

        /// <summary>【已停用】远段（广场↔内容顶）承载的格区间上界 = 22.3（由 f006 的 22 px/格 外推）。见上方停用说明。</summary>
        public const float FarSegmentTopTile = 22.3f;

        // ───────────────── 塔的精灵帧（CR-T1：逐帧目视 + `.sc` Export 表解析，见回报） ─────────────────
        //
        // ★ 原版塔**不是一张图** —— `策划/塔与建筑动画表.md` §52-53 已写：
        //   「塔为多 shape 合成（塔体 + 国王人物 + 炮塔），单取一帧只得塔体层」。
        //   本片用 `tools/probes/cr-tower-compose.py`（复跑命令见该文件头）解析 `building_tower_v215.sc`
        //   的 **Export 表（显式 id 引用）**，得到权威配方（记录序 = `frame_NNN`）：
        //     · `KingTower_blue`(clip 308) = turret(clip 247 → rec 30-47) + king_idle(clip 252 → rec 101-113) + **rec 213**
        //     · `KingTower_red` (clip 307) = rec 15 + king_idle(clip 245 → rec 16-28) + rec 30-97 + **rec 211 + rec 212**
        //     · `kingtower_goldrush_01/02` = rec 203 / 201（**活动皮肤**，不是常态塔 ⇒ 本片不再用）
        //     · 公主塔乘员 = `chr_princess_v215.sc` 的 `princess_tower_idle1_N`（rec 504 = 视角 1，公主 + 弩）
        //   ⇒ 常态塔体 = **rec 213（蓝）/ rec 211（红）**；把 201/203（金币狂欢皮肤）当常态塔是 AU2 的误判。
        //   ⛔ 上面这些层**共用同一张 407×471 画布** ⇒ 同 `localPosition` 叠放即为原版对位，本文件不自造偏移；
        //      唯一例外是公主塔乘员（来自另一个 `.sc`，画布 268×180，且**画布框跟塔体不同**）
        //      ⇒ 偏移不能是常量，要在运行时按各自 `sprite.rect`（裁剪框）反算，见 `PrincessOccupantLocalPx`
        //        与 `PrincessFootLinePx`（CR-T1j：按画布中心对齐会让公主飘在塔顶外）。

        // ★ 「红塔为何红、蓝塔为何蓝」（CR-T1b 复算，`tools/probes/cr-t1b-occupant.py` 输出）：
        //   `KingTower_red` = clip **307** → rec 15-28 / 30-97 / **211-212**；
        //   `KingTower_blue` = clip **308** → rec 30-47 / 99-161 / **213**。
        //   两方是**各自不同的 shape 记录**，不是"同一张灰度图 + 运行时 ColorTransform"——
        //   同一套像素统计口径下：rec **211** = 偏红 **11462** / 偏蓝 89（均色 RGB(138,115,105)）；
        //   rec **213** = 偏红 3634 / 偏蓝 **5880**（均色 RGB(112,112,115)）；rec 212 = 偏红 133 / 偏蓝 0（灰）。
        //   两张图统计**完全不同** ⇒ 阵营色**烘焙在各自贴图里**。⛔ 因此不许给塔体加 tint/换色，
        //   取对帧号即得到正确阵营色。

        /// <summary>BLUE 塔体（常态皮肤）：`KingTower_blue` 的 rec 213（白石垛口 + **蓝壁板** + 蓝门）。</summary>
        public const int BlueTowerBodyFrame = 213;

        /// <summary>RED 塔体（常态皮肤）：`KingTower_red` 的 rec 211（与 213 同形，**红壁板** + 金冠徽记）。</summary>
        public const int RedTowerBodyFrame = 211;

        /// <summary>
        /// RED 塔体**加层**：`KingTower_red` 的 rec 212（灰石垛口环）。
        /// 蓝方配方（`KingTower_blue`）没有这一层 ⇒ 只红方叠（⛔ 不为了"对称"给蓝方也加，那是自造）。
        /// </summary>
        public const int RedTowerBodyTopFrame = 212;

        // ★ CR-T1i：**公主塔的塔体不是王塔那套 art** —— 原版公主塔垛口宽 1.85~1.91 格、王塔 2.88~2.93 格
        //   （`策划/参考图/03_对局_1320x2868.jpg`，91.5 px/格，量法见 `tools/probes/cr-t1h_scale.py`），
        //   而王塔 art（rec 213/211）宽 170/171 px ⇒ 拿它画公主塔会**宽出约 65%**。
        //   正确 art = `building_tower_v215.sc` 的 `StarTower_base_*`（export 表见 `cr-t1i-hunt.py`）：
        //     · `StarTower_base_blue`(clip 236) = rec **10**（白石垛口 + 蓝壁板 + 金冠徽记 + 木地板 + 梯）
        //     · `StarTower_base_red` (clip 235) = rec **9**（同形、红壁板）
        //   形状判据（逐格目视，对照图由 `tools/probes/cr-t1i-princess.py` 出）：与原版公主塔逐项同构，
        //   塔腔里的深色内景 / 木地板 / 阵营壁板**就在这张 art 里** ⇒ 上一轮为公主塔补的 `BackA/BackB`
        //   （那是**王塔**的内景层）对公主塔是错的，本片撤掉。
        /// <summary>BLUE 公主塔塔体（常态皮肤）：`StarTower_base_blue`(clip 236) = rec **10**。</summary>
        public const int BluePrincessBodyFrame = 10;

        /// <summary>RED 公主塔塔体（常态皮肤）：`StarTower_base_red`(clip 235) = rec **9**。</summary>
        public const int RedPrincessBodyFrame = 9;

        /// <summary>
        /// 公主塔的**前墙层**（排在乘员**之后** ⇒ 压住公主下半身，与王塔"前墙(212)压住王"同一条原版规则）：
        /// `StarTower_top_blue`(clip 234) = rec **8**、`StarTower_top_red`(clip 233) = rec **7**。
        /// <para>
        /// <b>为什么它是"前墙"而不是"塔顶"</b>（判据 = 画布 bbox 位置）：这两个 shape 在 **407×471 画布**里的
        /// 非透明 bbox = `(138,148)-(262,218)`（蓝）/ `(139,148)-(264,219)`（红）—— 落在塔体的**塔腔前区**
        /// （塔体 rec 10 bbox = (126,87)-(275,286)，塔腔木地板带 y=137~160），**不是塔体上方**；
        /// 且原版图里公主下半身被前垛口挡住 ⇒ 它是**前墙**，且必须画在乘员之后。
        /// 量法见 `tools/probes/cr-t1i-princess.py` 的 bbox 输出。
        /// </para>
        /// </summary>
        public const int BluePrincessTopFrame = 8;

        /// <summary>同上（红方）：`StarTower_top_red`(clip 233) = rec 7。</summary>
        public const int RedPrincessTopFrame = 7;

        /// <summary>BLUE 国王（塔上坐着的人物）：`KingTower_blue` 的 `king_idle`(clip 252) 首帧 rec 101。</summary>
        public const int BlueKingSeatFrame = 101;

        /// <summary>RED 国王：`KingTower_red` 的 `king_idle`(clip 245) 首帧 rec 16。</summary>
        public const int RedKingSeatFrame = 16;

        /// <summary>
        /// 我方（BLUE）国王塔的炮塔帧 —— 原版 `turret`，clip 247 = rec 30-47 共 **18 个转角帧**。
        /// <para>
        /// <b>为什么是 rec 30</b>：把 18 帧全部放大逐帧看过（本片联络图 `CR-T1b-turret18.png`，每格带帧号），
        /// 只有 **rec 30 / 31** 是"**炮口背对镜头**"（近端是封闭的炮尾、炮管向远侧延伸）；
        /// rec 41-47 是"正对镜头看进炮口"；rec 32-40 是横向。参考图 `03_对局` 里**我方（蓝）国王塔**
        /// 看到的就是炮尾（炮管向远去、近端带金环），⇒ 取 rec 30。
        /// </para>
        /// </summary>
        public const int BlueKingTurretFrame = 30;

        /// <summary>
        /// 公主塔乘员所在目录：`Sprites/Units/chr_princess_out`（源 `chr_princess_v215.sc`）。
        /// 该目录**已在工程里**（748 帧，见 <see cref="ResPaths.UnitFrame"/>），⛔ 本片不新复制素材。
        /// </summary>
        public const string PrincessOccupantSpriteDir = "chr_princess_out";

        /// <summary>
        /// 我方（BLUE）公主塔乘员：`princess_tower_idle1_7`（clip 973）= rec **498**（公主 + 弩，**视角 7**）。
        /// <para>
        /// <b>两套 + 9 视角</b>（`chr_princess_v215.sc` 的 Export 表，`tools/probes/cr-t1b-occupant.py` 复跑）：
        /// `princess_tower_idle1_1..9` = rec **504,503,502,501,500,499,498,497,496**；
        /// `princess_tower_red_idle1_1..9` = rec **8,7,6,5,4,3,2,1,0**（逐帧配色统计证明蓝/红两套，见下）。
        /// </para>
        /// <para>
        /// <b>为什么取视角 7（CR-T1e 收敛）</b>：上一轮用视角 1 = **斜躺/侧朝**（主 agent 读图判"斜躺"）。
        /// 本轮把 18 张出成带帧号的联络图并与原版 G2/G5 大图对照（`.ai-tmp\test\CR-T1e-idle-views.png` +
        /// `CR-T1e-pick.png`），按原版的三个特征核：**头在上 / 身在下 / 弩横在身前** ⇒ 视角 7 = rec 498（蓝）、
        /// rec 2（红）；视角 1（rec 504/8）那两张的弩朝侧后、身子横过来，与参考图不符。
        /// </para>
        /// <para>
        /// ⛔ 颜色仍必须取对套：rec 498 属蓝套（`princess_tower_idle1_7`），rec 2 属红套
        /// （`princess_tower_red_idle1_7`）；两套的"偏红/偏蓝"计数见 `cr-t1b-occupant.py` 输出。
        /// </para>
        /// </summary>
        public const int BluePrincessOccupantFrame = 498;

        /// <summary>
        /// 敌方（RED）公主塔乘员：`princess_tower_red_idle1_7`（clip 973 的对位）= rec **2**（红衣公主 + 弩，
        /// **视角 7**）。与蓝方取同一视角（见 <see cref="BluePrincessOccupantFrame"/> 的判定）。
        /// <para>
        /// <b>为什么是视角 7</b>：9 个视角是**绕塔的 9 个朝向**，把 18 张（两套各 9）出成带帧号的联络图
        /// （`.ai-tmp\test\CR-T1e-idle-views.png`）+ 与原版 G2/G5 的大图对照（`CR-T1e-pick.png`）后，逐张核
        /// "头在上、身在下、弩横在身前"这一组特征：视角 1（rec 504/8）是**侧朝/斜躺**（上一轮主 agent 判"斜躺"的原因），
        /// 视角 7（rec 498/2，bbox 145,66,235,164）才是正朝镜头的那一张。
        /// </para>
        /// </summary>
        public const int RedPrincessOccupantFrame = 2;

        // ───────────────── 层级配方：来自 `.sc` 的 placement（CR-T1c 解出，⛔ 不许手改顺序）─────────────────
        //
        // **判定链**（`tools/probes/cr-t1c-placement.py` 可复跑，输出见回报）：
        //   `.sc` 里 `0x08` 记录 = **24 字节 = 6 × i32 = (a, b, c, d, tx, ty)** 的仿射矩阵
        //   （`a/d` 是 1/1024 定点缩放、`b/c` 是旋转错切、`tx/ty` 单位 = **twips = 1/20 画布像素**；
        //    证据：1244 条矩阵里 `a=d=1024`(=1.0) 出现 379 次为最多、且 95% 的条目 `b=c=0` ⇒ 1024=1.0）。
        //   `0x0c` 记录里 `cnt1` 条 **(childIndex, matrixId, ctId)** 三元组 = **逐帧的放置表**，
        //   同一帧内**按列表顺序绘制**（后画在上）⇒ 这就是**层序**。65535 = 无矩阵 / 无颜色变换。
        //
        // **红王塔**（`KingTower_red` = clip 307）第 1 帧的 5 个放置（顺序 = 绘制顺序）：
        //   ① rec 211（塔体）无矩阵   ② rec 15 无矩阵   ③ king_idle(clip 245→rec 16) matrix 89 = 0.5×、dy −25px
        //   ④ rec 212（灰垛口/前墙）无矩阵   ⑤ turret(clip 247→rec 30) matrix 91 = 1.0×、dy −33px、ct=恒等
        //   ⇒ **前墙(212)画在王之后** ⇒ 原版是"前墙压住王的下半身"，上一轮把 212 画在王之前 ⇒ 王像贴在木板上。
        // **蓝王塔**（`KingTower_blue` = clip 308）第 1 帧：① rec 213（塔体）② rec 99(1.0×, d(−1.5,+0.5))
        //   ③ turret(rec 30) matrix 184 = 1.0×、dy −57px ④ rec 100 ⑤ king_idle(clip 252→rec 101) matrix 185 = 0.5×、dy −42px
        //   ⇒ **王画在最后**（盖住炮管近端）—— 参考图蓝王塔"金冠压在炮尾上"的成因就是这条。
        // ⚠️ 本项目的 Unity 侧按"层的 localPosition 平移 + localScale 缩放"复现上面的矩阵（旋转 b/c 都是 0）。

        /// <summary>国王塔的层配方（元素 = 帧号 / 缩放 / 画布像素偏移 dx,dy / 层名）。⛔ 顺序即绘制顺序。</summary>
        private struct LayerSpec
        {
            public int Frame;
            public float Scale;
            public float Dx;
            public float Dy;
            public string Name;

            public LayerSpec(int frame, float scale, float dx, float dy, string name)
            {
                Frame = frame; Scale = scale; Dx = dx; Dy = dy; Name = name;
            }
        }

        /// <summary>
        /// 蓝方国王塔 = `KingTower_blue`(clip 308) 第 1 帧的放置表（见上面类注释的逐条出处）。
        /// </summary>
        private static readonly LayerSpec[] BlueKingRecipe =
        {
            new LayerSpec(BlueTowerBodyFrame, 1f, 0f, 0f, "Body"),            // child 214 → rec 213，无矩阵
            new LayerSpec(99, 1f, -1.5f, 0.5f, "BackA"),                      // child 99，matrix 261
            new LayerSpec(BlueKingTurretFrame, 1f, 0f, -57f, "Turret"),       // child 247，matrix 184
            new LayerSpec(100, 1f, 0f, 0f, "BackB"),                          // child 100，无矩阵
            new LayerSpec(BlueKingSeatFrame, KingLayerScale, 0f, -42f, "King"),// child 252，matrix 185
        };

        /// <summary>
        /// 国王层的**有效缩放** = **1.05**（不是矩阵里那个 512/1024=0.5）。
        /// <para>
        /// <b>为什么不是 0.5</b>（CR-T1d 定标收敛轮，判据 = 原版图比例）：
        /// ① 递归解到底：`KingTower_red`(307) → child 245(matrix 89 = 0.5) → child 244(matrix 31 = 1.052/0.947)
        ///    → child 16（**无矩阵**）⇒ `.sc` 的字面有效缩放 = 0.5 × 1.052 = **0.526**；
        /// ② 但按原版对局图量，0.5× 渲染出来的王**只有原版的 0.48 倍**：
        ///    同一掩膜口径下「王冠+金饰宽 / 垛口结构宽」——原版 `03_对局` G1 = **0.586**，我方 = **0.279**
        ///    ⇒ 需要 0.5 × (0.586/0.279) = **1.050**；
        /// ③ 交叉验证（另一层）：炮塔按矩阵 1.0× 渲染出的「炮管可见宽 / 塔宽」= 0.283，与原版量得的 0.28 一致
        ///    ⇒ **炮塔层 1.0 是对的**，说明这不是"全局定标"问题，而是**国王这一层的值**问题（与主 agent 的预判一致）。
        /// </para>
        /// <para>
        /// ⚠️ 登记：`.sc` 字面值(0.526) 与图上比例(1.050) 差 2.0 倍。可能原因是形状记录（`0x12`）里还有
        /// 本项目解析器未读的字段（例如 shape 自带缩放），或这两个王 shape 的导出像素是 2×。
        /// 本片按**图上比例**取值（主 agent 明确：两个比例无法同时对上时逐层按原版比例给值）。
        /// </para>
        /// </summary>
        public const float KingLayerScale = 1.05f;

        /// <summary>
        /// 红方国王塔 = `KingTower_red`(clip 307) 第 1 帧的放置表。
        /// 注意 **212 排在王之后**（前墙压住王下半身）；**炮塔照原版保留**（主 agent 裁定：不许因为
        /// 参考图那一帧它背对镜头就删掉一个原版真实存在的部件）。
        /// <para>
        /// ⚠️ <b>本表与 `.sc` 顺序的**唯一一处偏离**（已登记）</b>：`.sc` 把 turret 排在**最后**（画在最上层），
        /// 但那样红王会被炮管整个盖住 —— 而参考图 `03` 里敌方红王是**完整可见**的（头/躯干/披风/金徽全在，
        /// 身前没有任何炮管）。⇒ 本表把 Turret 排在 King **之前**（炮在王身后、被王遮住），既保留了这个
        /// 部件、又与参考图画面一致。若主 agent 判定必须以 `.sc` 顺序为准，把这两行的位置换回来即可。
        /// </para>
        /// </summary>
        private static readonly LayerSpec[] RedKingRecipe =
        {
            new LayerSpec(RedTowerBodyFrame, 1f, 0f, 0f, "Body"),             // child 212 → rec 211，无矩阵
            new LayerSpec(15, 1f, 0f, 0f, "BackA"),                           // child 15，无矩阵
            new LayerSpec(RedKingSeatFrame, KingLayerScale, 0f, -25f, "King"),// child 245，matrix 89
            new LayerSpec(RedTowerBodyTopFrame, 1f, 0f, 0f, "BodyTop"),       // child 213 → rec 212，无矩阵
            // ⛔ 本格**不画炮塔层**（`.sc` 里 child 247 仍在，本表只是不接它）。判定（CR-T1e，主 agent 指定口径）：
            //   把 18 个炮塔帧逐个离线合成（塔体+15+王+前墙+该帧炮塔），与原版 `03_对局` G1 那一格按同一
            //   结构包围盒归一化后算**平均绝对差**（脚本 `.ai-tmp\test\cr_t1e_offline.py`）：
            //   18 帧 MAE 全落在 71.09~72.53，"不画" = 73.00 ⇒ **没有任何一帧能对上原版**（差异极不显著、
            //   且"不画"并不比加一帧差多少）⇒ 按主 agent 的退路判定：**原版那一刻这一格不画炮塔**
            //   （炮塔随瞄准/激活切换；参考图抓到的正是"不可见"那一态）。
            // ⚠️ 已知缺口：红王塔炮塔的**可见性规则未解出**；此处是复刻参考图那一帧的状态，
            //   **不是删部件** —— 蓝王塔那一格照 `.sc` 正常画炮塔（见 BlueKingRecipe）。
        };

        /// <summary>
        /// 公主塔配方（原版 `.sc` 里**没有**公主塔的合成 clip ⇒ 塔体 + 乘员 + 前墙；乘员的偏移是量取值，
        /// 见 <see cref="PrincessOccupantLocalPx"/>）。前墙排在乘员**之后** = 与王塔同一条原版规则（前墙压住乘员下半身）。
        /// </summary>
        private static readonly LayerSpec[] BluePrincessRecipe =
        {
            new LayerSpec(BluePrincessBodyFrame, 1f, 0f, 0f, "Body"),                    // clip 236 = rec 10
            new LayerSpec(BluePrincessOccupantFrame, PrincessOccupantScale, 0f, 0f, "Princess"),   // 偏移运行时反算（PrincessOccupantLocalPx）
            new LayerSpec(BluePrincessTopFrame, 1f, 0f, 0f, "FrontWall"),                // clip 234 = rec 8：前墙压住公主下半身
        };

        private static readonly LayerSpec[] RedPrincessRecipe =
        {
            new LayerSpec(RedPrincessBodyFrame, 1f, 0f, 0f, "Body"),                     // clip 235 = rec 9
            new LayerSpec(RedPrincessOccupantFrame, PrincessOccupantScale, 0f, 0f, "Princess"),   // 偏移运行时反算（PrincessOccupantLocalPx）
            new LayerSpec(RedPrincessTopFrame, 1f, 0f, 0f, "FrontWall"),                 // clip 233 = rec 7
        };

        // （CR-T1g 曾为公主塔接过 `BackA/BackB`（rec 99/100/15）—— 那是**王塔**的内景层。
        //   CR-T1i 确认公主塔有自己的 art（`StarTower_base_*`），塔腔内容都在 art 里 ⇒ 这三个常量与三行
        //   配方一并撤掉，本处只留记录，⛔ 不要因为它们"看起来像塔腔内容"再捡回来。）

        /// <summary>
        /// 公主塔乘员相对**塔画布中心**的偏移（单位 = 画布像素；x 右为正、y **下**为正，与 `.sc` 矩阵口径一致）。
        /// <para>
        /// <b>为什么这一层不能从 `.sc` 取</b>：乘员（`princess_tower_idle1_N` / `princess_tower_red_idle1_N`）
        /// 是 `chr_princess_v215.sc` 的**顶层 export**，`building_tower_v215.sc` 里**没有任何 clip 引用它**
        /// ⇒ 两个文件之间**不存在 placement 记录**，只能量。
        /// </para>
        /// <para>
        /// <b>CR-T1j：偏移不是常量 —— 必须按"裁剪框锚点"在运行时反算</b>（见
        /// <see cref="PrincessOccupantLocalPx"/>）。CR-T1i 那版把偏移写成"画布中心对画布中心"的常量
        /// （−66.5 / −123），实机就是**公主飘在塔顶外面**：因为工程里的 PNG 是 **Sprite Mode = Multiple
        /// + 自动切片** 导入，运行时 `sprite.rect` = 每张图自己的 **alpha 裁剪框**（实测
        /// `frame_009_0 rect=173x198`，而 PNG 本体 407×471），**Unity 画的锚点是裁剪框中心**。
        /// 同画布的层（塔体裁剪框中心 ≈ 前墙）误差只有几 px，但乘员来自另一份 `.sc`（画布 268×180）⇒
        /// 按画布中心对齐会整块偏 ≈0.6 格。
        /// </para>
        /// </summary>
        public const float PrincessCavityCxPx = 202.5f;

        /// <summary>
        /// 公主的**落脚线**（塔体画布 y，**自上而下**计，单位 = 画布 px）。
        /// <para>
        /// <b>出处（CR-T1j 重定，按裁剪框锚点口径）</b>：候选扫描（`tools/probes/cr-t1i-occ.py --sweep
        /// --foots 165,185`，产物 `.ai-tmp\test\CR-T1j-occ.png`，每格带落脚值与算出的 localPos）与原版
        /// `03_对局` 裁切**同尺度并排**逐格看 ⇒ **185** 这一档下：公主的头冠顶与白石垛口上沿齐平、
        /// 躯干在塔腔内、弩横在身前，下半身由 `FrontWall`(rec 7/8) 压住 —— 与原版一致；165 那一档她整块
        /// 偏高（弩压在前垛口上）。
        /// </para>
        /// 说明：塔体 art 的塔腔木地板带在 y=134~160（阈值量取），落脚线落在板带**下沿之外**是因为
        /// 乘员精灵的 bbox 底边是**举起的弩的下缘**、不是她的脚 ⇒ 用"bbox 底心贴地板"会把整体抬高。
        /// </summary>
        public const float PrincessFootLinePx = 185f;

        /// <summary>
        /// 公主乘员层的缩放。**1.13**（原为 1.0）。
        /// <para>
        /// <b>出处（CR-T1f 量取）</b>：同一掩膜口径下「乘员框高 / 垛口结构宽」——原版 `03_对局` G2 = **0.704**、
        /// 我方(1.0×) = **0.624**（脚本 `.ai-tmp\test\cr_t1f_measure.py`；分母用**结构宽**是因为两个 crop 的高
        /// 不同、宽都完整在画面内）⇒ 缩放 = 0.704/0.624 = **1.128 ⇒ 取 1.13**。
        /// ⚠️ 这条是**图上比例**收敛（乘员与塔之间没有 `.sc` placement，只能量），非原版数据。
        /// </para>
        /// <para>
        /// <b>CR-T1i 复核（塔体换成 `StarTower_base_*` 之后）</b>：本值的分母是"垛口结构宽"，换 art 后
        /// 我方分母 = 115 px（白件宽）× `PrincessTowerScale` 1.5 = **172 px**，与原版量到的 172 px **相等**
        /// ⇒ 分母口径仍与原版对齐，不必跟着缩。乘员侧换成工程实际用的 `princess_tower_idle1_7`
        /// （rec 498 / 2，bbox 高 97 px）后，同塔尺并排逐格看 **0.85 / 1.00 / 1.13** 三档
        /// （`.ai-tmp\test\CR-T1i-osweep.png`）：1.00~1.13 最接近原版、0.85 明显偏小 ⇒ **保持 1.13**
        /// （⛔ 不为了"看着更准"改成自定值）。
        /// </para>
        /// </summary>
        public const float PrincessOccupantScale = 1.13f;

        /// <summary>
        /// **国王塔**的缩放。**1.7**（原值 1.6 是 CR-T1h 的口径错误值，见下）。
        /// <para>
        /// <b>出处（两侧同口径 + 统一换算成"格"，`tools/probes/cr-t1i-fit.py` 可复跑）</b>：
        /// 口径 = 「**白件宽**」= 两边同一掩膜 `r&gt;205 &amp; g&gt;195 &amp; b&gt;185`：
        ///   · 原版 `03_对局`：国王塔 **268(RED) / 264(BLUE) px**，除以本片实测的 **91.5 px/格**
        ///     （桥心距 1019.5 px ÷ 11 格 = 92.7、顶部两公主塔中心距 993 px ÷ 11 格 = 90.3，两锚互证 3%）
        ///     ⇒ **2.93 / 2.89 格**，与 `策划/策划案/皇室战争参考规格.md:59`「国王塔占地 3×3 格」一致 ✔
        ///   · 我方 art（导入 PPU = 100 ⇒ 1 格 = 100 art px）：rec **211/213** = **171 / 170 px** = 1.71 / 1.70 格
        ///   ⇒ scale = 2.93/1.71 = **1.713**、2.89/1.70 = **1.697** ⇒ 取 **1.7**。
        /// </para>
        /// <para>
        /// ⚠️ <b>为什么把 1.6 改成 1.7（本片自纠）</b>：CR-T1h 那个 1.6 是拿**我方 art 的 bbox 全宽**
        /// （191 px）去比**原版的白件宽**（3.06 格）算的 —— 两侧口径不同，且当时用的 64.8 px/格 是
        /// 未成立的场宽拟合值。同口径重算后 1.6 会让塔体白件只有 **2.73 格**（比原版 2.9 格小 6%）。
        /// 本片按同口径重算值给 1.7 ⇒ 渲染白件 2.90 格 ✔。
        /// 口径散布（登记）：全轮廓口径给 1.78（art bbox 184 px = 1.84 格，原版轮廓 ≈300 px = 3.28 格）
        /// ⇒ 两口径 [1.70, 1.78]，误差带 ±15~20%（原版整屏 px/格不恒定，见 CR-T1h 回报）内。
        /// </para>
        /// </summary>
        public const float TowerScale = 1.7f;

        /// <summary>
        /// **公主塔**的缩放。**1.65**（与国王塔 1.7 不是同一个数；两者用**同一条口径**分别量取）。
        /// <para>
        /// <b>出处（两侧同口径 + 统一换算成"格"，`tools/probes/cr-t1i-fit.py` 可复跑）</b>：
        /// 口径 = 「白件宽」（掩膜同上）：
        ///   · 原版 `03_对局`：公主塔 **169 / 175 / 172 px** ÷ 91.5 px/格 = **1.88 格**
        ///     （≲ 公主塔"半径 1 格 = 直径 2 格"的占地 ✔ 与原版王塔"白件宽 ≈ 占地"同一规律）
        ///   · 我方 art rec **10/9**（`StarTower_base_blue/red`）白件宽 = **115 px** = **1.15 格**
        ///   ⇒ scale = 1.88 ÷ 1.15 = **1.635 ⇒ 取 1.65**（渲染白件 1.90 格 ✔）。
        /// </para>
        /// <para>
        /// ⚠️ <b>与本片早期 1.5 的差别（自纠）</b>：1.5 是把 172 px ÷ 115 px 直接比出来的，漏了
        /// "原版 91.5 px/格 vs 我方 100 px/格"这一步换算；1.5 会让塔体白件只有 1.73 格（比原版小 8%）。
        /// 口径散布（登记）：全轮廓口径给 1.46（原版轮廓 ≈200~205 px = 2.2 格，art bbox 151 px = 1.51 格）
        /// ⇒ 两口径 [1.46, 1.65]，即本值是**两口径的上界侧**；主 agent 若要在两口径间取中，取 1.55。
        /// </para>
        /// <para>
        /// <b>art 相对占地的"外扩"</b>：1.65 下 art bbox = 151×1.65 = 2.49 格（占地 2 格，外扩 25%）；
        /// 原版自己的外扩 ≈ 2.2 格（+10%）。⇒ 外扩量偏大，是本值的已知偏差（登记，未采用第三种缩放）。
        /// </para>
        /// </summary>
        public const float PrincessTowerScale = 1.65f;

        /// <summary>底图兜底色（取自 f022 的草地均色 ≈(154,182,85)）—— 美术取不到时的地板色。</summary>
        public static readonly Color BaseGrassColor = new Color(154f / 255f, 182f / 255f, 85f / 255f, 1f);

        /// <summary>塔帧断言日志只打一次（6 座塔会走 6 遍建塔路径）。</summary>
        private static bool _towerFrameAssertLogged;

        /// <summary>河道（底图帧自带水带）断言日志只打一次（重建竞技场会重走）。</summary>
        private static bool _riverAssertLogged;

        private readonly List<TowerView> _towers = new List<TowerView>();
        private readonly List<Sprite> _generated = new List<Sprite>();
        private Transform _artRoot;
        private Transform _towerRoot;

        /// <summary>当前建出来的塔数（自检/日志用）。</summary>
        public int TowerCount => _towers.Count;

        /// <summary>
        /// 在 <paramref name="parent"/> 下建出竞技场（底图 + 6 座塔）。可重复调用（先拆旧的）。
        /// </summary>
        public static ArenaView Create(Transform parent)
        {
            var go = new GameObject(LogTag);
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<ArenaView>();
            view.Build();
            return view;
        }

        /// <summary>合成底图 + 摆 6 座塔。</summary>
        public void Build()
        {
            ClearGenerated();
            _artRoot = new GameObject("Art").transform;
            _artRoot.SetParent(transform, false);
            _towerRoot = new GameObject("Towers").transform;
            _towerRoot.SetParent(transform, false);

            BuildBaseQuad();
            BuildArt();
            BuildTowers();
        }

        /// <summary>
        /// 用快照里的塔状态刷新 6 座塔（血条 / 摧毁）。
        /// <para>
        /// <b>数组下标的语义来自服务端</b>：`server/game/core/tower.go:22-35` 的 `towerSlotsFor` 按
        /// **[国王塔, 左公主塔, 右公主塔]** 顺序建 roster，快照按同一顺序下发（`snapshot.go:87-94`）。
        /// 保险起见这里**不硬依赖下标**：先用 `Kind` 判国王/公主，公主之间再按出现顺序定左右。
        /// </para>
        /// </summary>
        public void ApplyTowers(TowerState[] teamA, TowerState[] teamB)
        {
            ApplyTowerTeam(0, teamA);
            ApplyTowerTeam(1, teamB);
        }

        /// <summary>相机设置：竞技场 18×32 格、竖屏构图。</summary>
        /// <remarks>
        /// <b>为什么这样取</b>：世界单位 = 格、竞技场中心在原点、y 大 = RED 后方（`GameConst` 的坐标约定）。
        /// 正交尺寸 = 视野半高。要**完整**看到 32 格高 ⇒ 半高 ≥ 16；要看到 18 格宽 ⇒ 半高 ≥ 9/aspect。
        /// 取两者的大者，于是竖屏（aspect &lt; 0.5625）时铺满高度、横屏时退化为左右留白（露出竞技场外的草地，不裁单位）。
        /// <b>⛔ 不走 `Game.Camera`</b>：`docs/client-api-reference.md` 的 §2/§5 只列出 `Game.Camera` 的类型，
        /// **没有成员签名**，照"不猜 API"的规矩不用它，直接摆 `Camera`（场景里已有一台正交相机，见 `SceneBuilder`）。
        /// </remarks>
        public static void SetupCamera(Camera cam)
        {
            if (cam == null) return;
            cam.orthographic = true;
            var aspect = cam.aspect > 0.01f ? cam.aspect : 0.5625f; // 0.5625 = 9:16 竖屏
            cam.orthographicSize = Mathf.Max(GameConst.ArenaTilesH * 0.5f,
                GameConst.ArenaTilesW * 0.5f / aspect);
            cam.transform.rotation = Quaternion.identity;
            if (cam.transform.position.z >= 0f)
                cam.transform.position = new Vector3(0f, 0f, -10f);
        }

        // ─────────────────────────── 底图 ───────────────────────────

        private void BuildBaseQuad()
        {
            // 纯色底图：**兜底**，保证"美术取不到"时玩家仍能看到一块场地（而不是一片清屏色）。
            // 它同时把 18×32 的场地边界"画实"，比透明背景更容易发现坐标错位。
            var white = SpriteBank.WhiteSprite();
            var go = new GameObject("Base");
            go.transform.SetParent(_artRoot, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = white;
            r.color = BaseGrassColor;
            r.sortingOrder = SortingOrder.ArenaBase;
            // WhiteSprite 是 1×1、PPU=1 ⇒ 天然 1 世界单位；直接缩放到 18×32 格。
            go.transform.position = GameConst.TileToWorld(GameConst.ArenaTilesW * 0.5f, GameConst.ArenaTilesH * 0.5f);
            go.transform.localScale = new Vector3(GameConst.ArenaTilesW, GameConst.ArenaTilesH, 1f);
        }

        private void BuildArt()
        {
            var frames = SpriteBank.LoadDir(ResPaths.ArenaRoot);
            if (frames.Length == 0)
            {
                Game.Logger?.Warn(LogTag, "竞技场美术一帧都没取到 ⇒ 只有纯色底图");
                return;
            }

            // ★ 按**帧号**取，不是数组下标（与 D1 修过的塔帧同族缺陷）：
            //   多子图 PNG 会让下标整体偏移，见 FindFrameByNumber 的注释。
            var src = FindFrameByNumber(frames, GroundFrameIndex);
            if (src == null || src.texture == null)
            {
                Game.Logger?.Warn(LogTag,
                    $"底图帧按帧号取不到（帧号 {GroundFrameIndex}，共 {frames.Length} 个 Sprite）⇒ 只有纯色底图");
                return;
            }

            // RED 层先画（sortingOrder 小 = 在下），BLUE 层后画（盖住重叠区）。理由见类注释三。
            const int orderRed = SortingOrder.ArenaBase + 1;
            const int orderBlue = SortingOrder.ArenaBase + 2;

            // ★★ CR-V1：地面改由 **帧 22（`training_area_bg`）的完整半场**铺（出处 / 标定见 NearGround* 常量上方的长注释）★★
            //   · 为什么换：f006 是带透视的画布（近段 58.3 px/格、远段 22 px/格），把远段铺到 15.8 格上 ⇒ 纵向放大
            //     **2.59×** ⇒ 棋盘格被抹平，观感是"整片一个绿色"（用户判词「地面是纯色草地」）。
            //     f022 的地面是 **66.4 px/格**，铺到 60 px/格 是**缩小 0.90×**。
            //     ⚠️ 「2.59× / 0.90×」是**算出来的**；「棋盘格清晰」是**预期** —— 本改动**未编译、未实机**
            //     （CR-V1 交付时无活编辑器）⇒ ⛔ 不许把这一行读成"已观感验证"。
            //   · RED 半场不再另画 f006 的镜像段，而是**同一条 f022 裁条 + flipY**（竞技场中心对称，参考规格 §2）
            //     ⇒ 地面只有一张原版画布、一个缩放比，不再有"2 段之间压缩率跳变"。
            //   · ⛔ 镜像只许表达一次（类注释五「双翻转陷阱」）：落位格区间**永远升序**（localScale.y 恒为正），
            //     镜像只由 flipY 表达。
            var ground = FindFrameByNumber(frames, NearGroundFrameNumber);
            if (ground == null || ground.texture == null)
            {
                Game.Logger?.Warn(LogTag,
                    $"完整地面帧按帧号取不到（帧号 {NearGroundFrameNumber}，共 {frames.Length} 个 Sprite）⇒ 地面只由纯色底图承担");
            }
            else
            {
                // 横向只取 f022 的**场地那一段**画布（格 0..18 = px 99..912）；⛔ 不取场地外的装饰与留白。
                MakeCrop("GroundNear", ground, NearGroundFieldLeftPx, NearGroundTopPy, NearGroundFieldRightPx, NearGroundRearEdgePy,
                    0f, GameConst.ArenaTilesW, 0f, GameConst.RiverTopTile, orderBlue);
                // RED 远半场：同一条裁条 + flipY（格 y → 32 − y ⇒ 格 0..15 落在格 17..32）。
                MakeCrop("GroundFar", ground, NearGroundFieldLeftPx, NearGroundTopPy, NearGroundFieldRightPx, NearGroundRearEdgePy,
                    0f, GameConst.ArenaTilesW, GameConst.RiverBottomTile, GameConst.ArenaTilesH, orderRed, true);
            }

            Game.Logger?.Info(LogTag,
                $"竞技场底图合成完成：地面帧号={NearGroundFrameNumber} 源={Name(ground)} 半场=2" +
                $"（BLUE 格0..{GameConst.RiverTopTile} / RED 格{GameConst.RiverBottomTile}..{GameConst.ArenaTilesH} flipY）" +
                $" 裁条=画布 x {NearGroundFieldLeftPx}..{NearGroundFieldRightPx} py {NearGroundTopPy:F0}..{NearGroundRearEdgePy}" +
                $"（{NearGroundPxPerTileY}px/格(y)，渲染 60px/格 ⇒ 纵向 {60f / NearGroundPxPerTileY:F2}×）" +
                $" | 河面帧号={GroundFrameIndex} 源={src.name} 见 BuildRiver");

            // 河道 = **底图帧自己（f006）自带的水面带**；桥 = **f022 的桥面木板**（f006 没画桥）。见类注释六。
            BuildRiver(src, frames);
        }

        // ────────── 河道：底图帧自带的水面带（原版像素；见类注释六） ──────────

        /// <summary>
        /// 铺河道与两座桥：**水**取自底图帧自己（<see cref="GroundFrameIndex"/> = f006）自带的水面带，
        /// **桥面木板**取自 <see cref="BridgeFrameNumber"/>（f022，见该常量的注释）。
        /// 两者都铺满格 <c>0..<see cref="GameConst.ArenaTilesW"/></c>（水）/ 桥心 ± 半宽，
        /// 纵向严格 <c><see cref="GameConst.RiverTopTile"/>..<see cref="GameConst.RiverBottomTile"/></c>。
        /// <para>
        /// <b>上一版错在哪（见类注释六）</b>：河道裁的是 f022 的 py 577..606 —— 那是 f022 的**褐色泥土岸**
        /// （实测均色 RGB(148,124,91)），画面因此成了"棕色土带 + 几块石头"。
        /// </para>
        /// </summary>
        /// <param name="ground">底图帧（<see cref="GroundFrameIndex"/> 对应的 Sprite，整幅画布）。</param>
        /// <param name="frames">整个竞技场目录的帧（用来按帧号取 <see cref="BridgeFrameNumber"/>）。</param>
        private void BuildRiver(Sprite ground, Sprite[] frames)
        {
            // 河的纵向 = 河本身（格 15..17，2 格高）：⛔ 只许来自 GameConst。
            var riverTop = GameConst.RiverTopTile;
            var riverBottom = GameConst.RiverBottomTile;
            var orderRiver = SortingOrder.ArenaBase + 3; // 高过底图两段（ArenaBase+1/+2），低过 ArenaDeco/Tower
            var orderBridge = orderRiver + 1;            // 桥画在河上

            // ① 水：横向窗口与 MakeSegment **同一算式**（场地那一段画布，格 0..18）。
            var pxLeft = ArtFieldLeftPx;
            var pxRight = ArtFieldLeftPx + GameConst.ArenaTilesW * ArtPxPerTileX;
            if (ground == null || ground.texture == null)
            {
                Game.Logger?.Warn(LogTag, "河道：底图帧为空 ⇒ 不铺水面");
            }
            else
            {
                MakeCrop("RiverWater", ground, pxLeft, RiverWaterPyTop, pxRight, RiverWaterPyBottom,
                    0f, GameConst.ArenaTilesW, riverTop, riverBottom, orderRiver);
            }

            // ② 两座桥：中心 x 与宽度只许来自 GameConst（桥心 3.5 / 14.5，宽 = 2 × 半宽 = 2 格）；
            //    裁剪窗口 = 该帧左车道位那组木板（见 BridgeFrameNumber）。
            var half = GameConst.BridgeHalfTile;
            var bridge = FindFrameByNumber(frames, BridgeFrameNumber);
            if (bridge == null || bridge.texture == null)
            {
                Game.Logger?.Warn(LogTag,
                    $"桥帧按帧号取不到（帧号 {BridgeFrameNumber}，共 {(frames == null ? 0 : frames.Length)} 个 Sprite）" +
                    " ⇒ 只铺水面、不铺桥（原版桥面木板像素就在该帧，见类注释六）");
            }
            else
            {
                MakeCrop("BridgeLeft", bridge, BridgePxLeft, BridgePyTop, BridgePxRight, BridgePyBottom,
                    GameConst.BridgeCxATile - half, GameConst.BridgeCxATile + half, riverTop, riverBottom, orderBridge);
                MakeCrop("BridgeRight", bridge, BridgePxLeft, BridgePyTop, BridgePxRight, BridgePyBottom,
                    GameConst.BridgeCxBTile - half, GameConst.BridgeCxBTile + half, riverTop, riverBottom, orderBridge);
            }

            // 断言（只报一次）：水与桥各自的原版来源 + 落位格。
            if (!_riverAssertLogged)
            {
                _riverAssertLogged = true;
                Game.Logger?.Info(LogTag,
                    $"河道断言：水面 源={Name(ground)}（帧号 {GroundFrameIndex}）画布px x{pxLeft}..{pxRight}" +
                    $" py{RiverWaterPyTop}..{RiverWaterPyBottom} → 格 x0..{GameConst.ArenaTilesW} y{riverTop}..{riverBottom}（order={orderRiver}）" +
                    $" | 桥面 源={Name(bridge)}（帧号 {BridgeFrameNumber}）px x{BridgePxLeft}..{BridgePxRight} py{BridgePyTop}..{BridgePyBottom}" +
                    $" → 左桥中心格 {GameConst.BridgeCxATile} / 右桥中心格 {GameConst.BridgeCxBTile}，各宽 {2f * half} 格（order={orderBridge}）");
            }
        }

        /// <summary>
        /// 从 <paramref name="src"/> 裁出画布矩形 <c>px [pxLeft, pxRight] × py [pyTop, pyBottom]</c>
        /// （px = 画布横坐标、py = 画布纵坐标且 **y 向下**），铺到格矩形
        /// <c>[tileXLeft, tileXRight] × [tileYLow, tileYHigh]</c>（升序）。
        /// <para>
        /// 与 <see cref="MakeSegment"/> 同为"裁条"，区别两点：① 横向**也**由参数给定（叠层只取河道/桥那一截画布，
        /// 不是整幅场地宽）；② 不做镜像（叠层只在 BLUE/RED 之间那份河上画一次，河左右对称）。
        /// <b>缩放算式与 <see cref="MakeSegment"/> 完全一致</b>：<c>localScale = 目标格数 × ppu ÷ 裁切像素宽/高</c>。
        /// </para>
        /// <para>⛔ 位置一律走 <see cref="GameConst.TileToWorld"/>，本方法内不自造世界坐标算式。</para>
        /// </summary>
        /// <param name="name">节点/Sprite 名（也是日志里的标识）。</param>
        /// <param name="src">源帧（整幅画布）。</param>
        /// <param name="pxLeft">裁条左沿（画布像素）。</param>
        /// <param name="pyTop">裁条上沿（画布像素，y 向下）。</param>
        /// <param name="pxRight">裁条右沿（画布像素）。</param>
        /// <param name="pyBottom">裁条下沿（画布像素，y 向下）。</param>
        /// <param name="tileXLeft">落位格区间左界。</param>
        /// <param name="tileXRight">落位格区间右界。</param>
        /// <param name="tileYLow">落位格区间下界。</param>
        /// <param name="tileYHigh">落位格区间上界。</param>
        /// <param name="sortingOrder">渲染层级。</param>
        /// <param name="flipY">
        /// true ⇒ 内容纵向镜像（RED 半场用）。语义与 <see cref="MakeSegment"/> 的同一参数一致：
        /// 裁条的 <c>pyBottom</c> 端落在 <paramref name="tileYHigh"/>（画布 py 向下、RED 侧格 y 与 BLUE 反向）。
        /// ⛔ 落位格区间一律**升序**传入（负 localScale.y 会与 flipY 互相抵消 —— 见 <see cref="MakeSegment"/> 的双翻转陷阱）。
        /// </param>
        private void MakeCrop(string name, Sprite src, float pxLeft, float pyTop, float pxRight, float pyBottom,
            float tileXLeft, float tileXRight, float tileYLow, float tileYHigh, int sortingOrder, bool flipY = false)
        {
            var texH = src.texture.height;
            var texW = src.texture.width;
            var ppu = src.pixelsPerUnit > 0.01f ? src.pixelsPerUnit : SpriteBank.FallbackPixelsPerUnit;

            var cropL = Mathf.Clamp(pxLeft, 0f, texW);
            var cropR = Mathf.Clamp(pxRight, 0f, texW);
            var cropT = Mathf.Clamp(pyTop, 0f, texH);
            var cropB = Mathf.Clamp(pyBottom, 0f, texH);
            var cropW = cropR - cropL;
            var cropH = cropB - cropT;
            if (cropW <= 1f || cropH <= 1f || tileXRight <= tileXLeft || tileYHigh <= tileYLow)
            {
                Game.Logger?.Warn(LogTag,
                    $"叠层裁条参数非法，跳过 {name}：画布px x{pxLeft}..{pxRight} py{pyTop}..{pyBottom} 贴图={texW}x{texH}" +
                    $" 落位格 x{tileXLeft}..{tileXRight} y{tileYLow}..{tileYHigh}");
                return;
            }
            if (pxLeft < 0f || pxRight > texW || pyTop < 0f || pyBottom > texH)
            {
                // 非预期分支（换帧 / 换素材）——必须留痕，否则只表现为"叠层被轻微拉伸"。
                Game.Logger?.Warn(LogTag,
                    $"叠层裁条超出画布被夹紧：{name} 需要 px x{pxLeft}..{pxRight} py{pyTop}..{pyBottom}，贴图={texW}x{texH}，" +
                    $"实际 x{cropL}..{cropR} py{cropT}..{cropB}");
            }

            // 画布 py 向下 ↔ 贴图 y 向上：翻转一次（与 MakeSegment 同）。
            var rect = new Rect(cropL, texH - cropB, cropW, cropH);
            var sp = Sprite.Create(src.texture, rect, new Vector2(0.5f, 0.5f), ppu);
            sp.name = name;
            _generated.Add(sp);

            var go = new GameObject(name);
            go.transform.SetParent(_artRoot, false); // 与底图同一父节点（类注释六）
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.flipY = flipY;
            r.sortingOrder = sortingOrder;
            go.transform.position = GameConst.TileToWorld((tileXLeft + tileXRight) * 0.5f, (tileYLow + tileYHigh) * 0.5f);

            // Sprite 自然世界尺寸 = 裁切像素 / PPU ⇒ 缩放比 = 目标格数 / 自然（与 MakeSegment 同式）。
            go.transform.localScale = new Vector3((tileXRight - tileXLeft) / (cropW / ppu),
                (tileYHigh - tileYLow) / (cropH / ppu), 1f);
        }

        /// <summary>
        /// ⛔ <b>已停用（CR-V1 拍②，2026-09-23）：本方法当前没有任何调用点，保留不删</b>
        ///（team-lead 裁定五条之 5）—— 类注释二/三/五「双翻转陷阱」「缺陷 A/B 根因」等结论按名字引用它，
        /// 删掉会造成**悬空引用**。
        /// <para>
        /// <b>停用原因</b>：地面已改由 <see cref="NearGroundFrameNumber"/>（f022 `training_area_bg`）的
        /// **完整半场**铺（见 `BuildArt()` 与本文件 `NearGround*` 常量的长注释）—— 不再需要"把带透视的
        /// f006 切成两段、各用一个缩放比铺到 15.8 格上"。f006 现在**只**用于河面（见 <see cref="BuildRiver"/>）。
        /// ⛔ 新代码不要再调用本方法。
        /// </para>
        /// 原用途：造一段底图 —— 从 <paramref name="src"/> 裁出画布上的横条
        /// <c>[pyTop, pyBottom] × [格0, 格18]</c>，铺到格区间 <c>[tileLow, tileHigh]</c>（<b>升序</b>）。
        /// <para>
        /// <b>为什么裁条而不是缩放整图</b>：这张原版美术带透视（近处 ≈58 px/格、河附近 ≈22 px/格），
        /// 单块线性缩放必然让"河"或"广场"之一落错格（会直接误导玩家：河面位置 = 可部署边界）。
        /// 分成 2 段后，**四个特征点（后沿 0 / 广场 6.5 / 河心 16 / 内容顶 22.3）全部落在正确格上**，
        /// 代价只是两段之间有一处纵向压缩率变化（美术上是透视感，不是裂缝）。
        /// </para>
        /// <para>
        /// <b>横向为什么只取格 0..18（而不是整幅画布）</b>：f006 在场地之外还画着一块孤立装饰
        /// （画布 px x 0..183、py 695..843，与场地之间隔着 38 px 全透明缝）。按整幅画布裁条会
        /// ① 把它画到世界 x −13.57..−9.57（场地左沿之外）当"悬空草皮"、② 使 sprite 中心落到世界 x = −1.62。
        /// 取 <c>px [ArtFieldLeftPx, ArtFieldLeftPx + 18 × ArtPxPerTileX] = [208.4, 1029.2]</c> 后
        /// sprite 中心格 = (0+18)/2 = 9 ⇒ 世界 x = 0，左右严格对称。详见类注释五。
        /// </para>
        /// <para>
        /// <b>⛔ 双翻转陷阱</b>：<paramref name="flipY"/> 已经是"翻一次"。若同时把格区间写成降序
        /// （tileHigh &lt; tileLow），<c>localScale.y</c> 会变成负数、把 sprite 再翻一次 ⇒ 两次抵消，
        /// 结果与该镜像的恰好相反（上一版 RED 半场就是这样没镜像的）。故本方法<b>要求升序</b>，
        /// 发现降序就换序并记一条 Warn（不允许静默产生负缩放）。
        /// </para>
        /// </summary>
        /// <param name="src">源帧（整幅画布）。</param>
        /// <param name="pyTop">裁条上沿（画布像素，y 向下）。</param>
        /// <param name="pyBottom">裁条下沿（画布像素，y 向下）。</param>
        /// <param name="tileLow">落位格区间下界（升序、含）。</param>
        /// <param name="tileHigh">落位格区间上界（升序、含）。</param>
        /// <param name="flipY">
        /// true ⇒ 内容纵向镜像（RED 半场）。注意与落位端点的关系（本方法内是唯一判定依据，务必按此核对调用）：
        /// <c>flipY = false</c> 时裁条的 <paramref name="pyBottom"/> 端落在 <paramref name="tileLow"/>；
        /// <c>flipY = true</c> 时它落在 <paramref name="tileHigh"/>（因为画布 py 向下、而 RED 侧格 y 与 BLUE 反向）。
        /// </param>
        /// <param name="sortingOrder">渲染层级。</param>
        private void MakeSegment(Sprite src, float pyTop, float pyBottom, float tileLow, float tileHigh,
            bool flipY, int sortingOrder)
        {
            var texH = src.texture.height;
            var texW = src.texture.width;
            var ppu = src.pixelsPerUnit > 0.01f ? src.pixelsPerUnit : SpriteBank.FallbackPixelsPerUnit;

            var rectH = pyBottom - pyTop;
            if (rectH <= 1f || texW <= 1 || texH <= 1)
            {
                Game.Logger?.Warn(LogTag, $"底图裁条参数非法，跳过：pyTop={pyTop} pyBottom={pyBottom} tex={texW}x{texH}");
                return;
            }

            if (tileHigh < tileLow)
            {
                // 不允许静默产生负 localScale.y（= 双翻转，见 summary 的陷阱说明）。
                Game.Logger?.Warn(LogTag,
                    $"底图格区间写成降序（{tileLow}..{tileHigh}）—— 会自动换序；" +
                    $"负 localScale.y 会与 flipY 抵消（本文件类注释五的缺陷 A/B 根因）");
                var t = tileLow;
                tileLow = tileHigh;
                tileHigh = t;
            }

            // 横向：只取场地那一段画布（格 0 ⇔ px 208.4，格 18 ⇔ px 208.4 + 18 × 45.6 = 1029.2）。
            // 画布两侧的场地外留白（左 4.57 格 / 右 1.33 格）不取 —— 它们不对称，且左侧那块是孤立装饰。
            var needLeft = ArtFieldLeftPx;
            var needRight = ArtFieldLeftPx + GameConst.ArenaTilesW * ArtPxPerTileX;
            var cropPxLeft = Mathf.Clamp(needLeft, 0f, texW);
            var cropPxRight = Mathf.Clamp(needRight, 0f, texW);
            var cropPxW = cropPxRight - cropPxLeft;
            if (cropPxW <= 1f)
            {
                Game.Logger?.Warn(LogTag,
                    $"底图横向裁条越界，跳过：画布宽={texW} 需要 px {needLeft}..{needRight}" +
                    $"（格0..{GameConst.ArenaTilesW}）；检查 ArtFieldLeftPx / ArtPxPerTileX 与素材是否匹配");
                return;
            }
            if (needLeft < 0f || needRight > texW)
            {
                // 非预期分支（素材尺寸/常量改了）——必须留痕，否则只会表现为"底图被轻微拉伸"。
                Game.Logger?.Warn(LogTag,
                    $"底图横向裁条超出画布被夹紧：需要 px {needLeft}..{needRight}，画布宽={texW}，" +
                    $"实际取 {cropPxLeft}..{cropPxRight}（格 0..{GameConst.ArenaTilesW} 会被拉伸 {GameConst.ArenaTilesW * ArtPxPerTileX / cropPxW:F3}×）");
            }

            // 画布 y 向下 ↔ 贴图 y 向上：翻转一次。
            var rect = new Rect(cropPxLeft, texH - pyBottom, cropPxW, rectH);
            var seg = Sprite.Create(src.texture, rect, new Vector2(0.5f, 0.5f), ppu);
            seg.name = "ArenaSeg";
            _generated.Add(seg);

            // 该裁条横向覆盖的格区间 = 场地本身（格 0..18）⇒ 中心格 9 ⇒ 世界 x = 0。
            var xLeft = 0f;
            var xRight = GameConst.ArenaTilesW;
            var yLow = tileLow;
            var yHigh = tileHigh;

            var go = new GameObject(flipY ? "SegF" : "Seg");
            go.transform.SetParent(_artRoot, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = seg;
            r.flipY = flipY;
            r.sortingOrder = sortingOrder;
            go.transform.position = GameConst.TileToWorld((xLeft + xRight) * 0.5f, (yLow + yHigh) * 0.5f);

            // 目标世界尺寸 = 格数；Sprite 自然世界尺寸 = 裁条像素 / PPU ⇒ 缩放比 = 目标 / 自然。
            // ⛔ 这里 (yHigh − yLow) 恒为正（入参已保证升序）⇒ 不会再与 flipY 抵消。
            var naturalW = cropPxW / ppu;
            var naturalH = rectH / ppu;
            go.transform.localScale = new Vector3((xRight - xLeft) / naturalW, (yHigh - yLow) / naturalH, 1f);
        }

        // ─────────────────────────── 塔 ───────────────────────────

        private void BuildTowers()
        {
            var frames = SpriteBank.LoadDir(ResPaths.TowersRoot);
            if (frames.Length == 0)
            {
                Game.Logger?.Warn(LogTag, "塔美术一帧都没取到 ⇒ 6 座塔只画血条（场地仍在）");
            }

            // 公主塔乘员来自**另一个** `.sc`（`chr_princess_v215.sc`，源目录已在工程里，见 ResPaths.UnitDir）。
            // 取不到 ⇒ 公主塔只有塔体（会 Warn，不当成"已完成"）。
            var occupantFrames = SpriteBank.LoadDir(ResPaths.UnitDir(PrincessOccupantSpriteDir));
            if (occupantFrames.Length == 0)
                Game.Logger?.Warn(LogTag,
                    $"公主塔乘员动画一帧都没取到（{ResPaths.UnitDir(PrincessOccupantSpriteDir)}）" +
                    " ⇒ 公主塔只画塔体（原版塔上是有公主 + 弩的，见回报）");

            // 塔位**只许**来自 GameConst（契约 D7）；RED 侧走 IsMirroredForTeam 的 y → 32 - y。
            for (var team = 0; team < 2; team++)
            {
                var mirrored = GameConst.IsMirroredForTeam(team);
                AddTower(team, true, GameConst.KingTowerTileX, GameConst.KingTowerTileY, mirrored, frames, occupantFrames);
                AddTower(team, false, GameConst.BridgeCxATile, GameConst.PrincessTowerTileY, mirrored, frames, occupantFrames);
                AddTower(team, false, GameConst.BridgeCxBTile, GameConst.PrincessTowerTileY, mirrored, frames, occupantFrames);
            }

            if (_towers.Count != 6)
                Game.Logger?.Warn(LogTag, $"塔数不是 6（实得 {_towers.Count}）—— 检查 GameConst 塔位常量");
        }

        private void AddTower(int team, bool isKing, float xTile, float yTileBlue, bool mirrored, Sprite[] frames,
            Sprite[] princessOccupantFrames)
        {
            var yTile = mirrored ? GameConst.ArenaTilesH - yTileBlue : yTileBlue;
            var go = new GameObject(isKing ? "KingTower" : "PrincessTower");
            go.transform.SetParent(_towerRoot, false);
            go.transform.position = GameConst.TileToWorld(xTile, yTile);
            // ★ 两种塔**各有自己的 art 也各有自己的缩放**（出处分别见 TowerScale / PrincessTowerScale 常量）：
            //   王塔 art 171 px 白件 ↔ 1.6；公主塔 art 115 px 白件 ↔ 1.5。⛔ 不是一个数套两座塔。
            var towerScale = isKing ? TowerScale : PrincessTowerScale;
            go.transform.localScale = new Vector3(towerScale, towerScale, 1f);

            // ★ 层序 + 每层的缩放/偏移**一律照配方表**（出处见 LayerSpec 上面的长注释：`.sc` 的
            //   0x08 矩阵 + 0x0c 三元组）。⛔ 不许在本方法里自排顺序、自调偏移。
            // ★ 逐帧一律按**帧号**取，不是数组下标：见 FindFrameByNumber 的类注释。
            var recipe = isKing ? (team == 0 ? BlueKingRecipe : RedKingRecipe)
                                : (team == 0 ? BluePrincessRecipe : RedPrincessRecipe);

            var n = 0;
            Sprite body = null;
            var sb = new List<string>();
            for (var i = 0; i < recipe.Length; i++)
            {
                var spec = recipe[i];
                // 乘员来自**另一个目录**（`chr_princess_out`），其余来自塔目录。
                var isOccupant = spec.Name == "Princess";
                var sp = FindFrameByNumber(isOccupant ? princessOccupantFrames : frames, spec.Frame);
                if (spec.Name == "Body") body = sp;
                var ppu = SpriteBank.FallbackPixelsPerUnit;
                if (sp != null && sp.pixelsPerUnit > 0.01f) ppu = sp.pixelsPerUnit;
                // 画布像素偏移（画布 y 向下、Unity y 向上 ⇒ 取负）；**除以 PPU 的事交给 TowerLayer**，
                // ⛔ 不要在这里先除一次（CR-T1c 第一版就是两处都除 ⇒ 偏移被缩小 100 倍，肉眼看不出来）。
                var off = new Vector2(spec.Dx, -spec.Dy);
                // ★ 公主乘员：**偏移不能是常量**（它来自另一份 `.sc`、画布框与塔体不同，而 Unity 的锚点是
                //   每张图自己的**裁剪框**中心）⇒ 按塔体/乘员的 `sprite.rect` 在运行时反算，见
                //   PrincessOccupantLocalPx 的算式与出处。
                if (isOccupant && body != null && sp != null)
                    off = PrincessOccupantLocalPx(body, sp, spec.Scale);
                n += TowerLayer(go.transform, spec.Name, sp, SortingOrder.Tower + i, off, spec.Scale, ppu) ? 1 : 0;
                sb.Add(spec.Name + "=" + Name(sp));
            }

            if (body == null)
                Game.Logger?.Warn(LogTag,
                    $"塔体帧取不到（帧号 {BodyFrameOf(team, isKing)}，共 {frames.Length} 个 Sprite）：" +
                    $"team={team} isKing={isKing} ⇒ 该塔只画血条");

            if (!_towerFrameAssertLogged)
            {
                _towerFrameAssertLogged = true;
                Game.Logger?.Info(LogTag,
                    $"塔层合成（断言）：layer={n}/{recipe.Length} 顺序[{string.Join(" ", sb)}] scale={towerScale} " +
                    $"bodyRect={(body == null ? "-" : body.rect.width + "×" + body.rect.height)}");
            }

            // 塔的血条：宽 1.6 格、离地 2.4 格（国王塔的塔顶比公主塔高，血条统一取高处，避免压在塔身上）。
            var bar = WorldHpBar.Create(go.transform, isKing ? 1.8f : 1.4f, 0.16f, isKing ? 2.6f : 2.0f, LogTag + (isKing ? ".KingHp" : ".PrinHp"));
            _towers.Add(new TowerView(team, isKing, xTile, yTile, FindLayerRenderer(go.transform), bar));
        }

        /// <summary>
        /// 给一座塔叠一层精灵。**所有 `building_tower_out` 的层共用一张 407×471 画布** ⇒ 除公主塔乘员外
        /// <paramref name="offsetPx"/> 都是 <see cref="Vector2.zero"/>，即"同 localPosition 叠放 = 原版对位"。
        /// </summary>
        /// <param name="offsetPx">相对画布中心的偏移（画布像素；x 右为正、y **上**为正）。</param>
        /// <param name="scale">该层的额外缩放（= `.sc` 矩阵的 a/d ÷ 1024；1 = 不缩放）。</param>
        /// <param name="ppu">该 Sprite 的 PPU（画布像素 → 世界单位的换算基准）。</param>
        /// <returns>该层是否真的建出来了（false = 帧取不到，已留痕）。</returns>
        private static bool TowerLayer(Transform parent, string name, Sprite sprite, int sortingOrder,
            Vector2 offsetPx, float scale, float ppu)
        {
            if (sprite == null) return false;
            var layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            if (ppu < 0.01f) ppu = SpriteBank.FallbackPixelsPerUnit;
            layer.transform.localPosition = new Vector3(offsetPx.x / ppu, offsetPx.y / ppu, 0f);
            layer.transform.localScale = new Vector3(scale, scale, 1f);
            var r = layer.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = sortingOrder;
            return true;
        }

        /// <summary>塔体层（`Body`）的渲染器 —— 血条 / 染色都作用在它上面。</summary>
        private static SpriteRenderer FindLayerRenderer(Transform tower)
        {
            var t = tower.Find("Body");
            return t == null ? null : t.GetComponent<SpriteRenderer>();
        }

        private static string Name(Sprite s)
        {
            return s == null ? "-" : s.name;
        }

        /// <summary>
        /// 公主乘员层的偏移（画布 px，y **上**为正）—— ⛔ **不是常量**，必须按裁剪框反算。
        /// <para>
        /// <b>为什么</b>：工程里塔/乘员的 PNG 是 **Sprite Mode = Multiple + 自动切片** 导入的 ⇒ 运行时
        /// `sprite.rect` = 每张图自己的 **alpha 裁剪框**（实测 `frame_009_0 rect=173x198`、`frame_010_0`
        /// `173x210`、乘员 `93x100`；而 PNG 本体是 407×471 / 268×180），**Unity 画的锚点 = 裁剪框中心**。
        /// 同画布的层（塔体 / 前墙）裁剪框位置只差几 px；但乘员来自**另一份 `.sc`**（画布 268×180），
        /// 它的裁剪框中心离自己的画布中心很远 ⇒ 若按"画布中心对画布中心"给常量偏移，整块会偏 ≈0.6 格
        /// （CR-T1i 实机就是"公主飘在塔顶外面"）。
        /// </para>
        /// <para>
        /// <b>算式</b>：画布点 P 被画到 <c>localPos + scale × (P − 该层裁剪框中心)</c>（缩放绕锚点做）；
        /// 要求乘员**裁剪框的底心**落在塔体画布的 (塔腔中心 x, 落脚线 y)：
        /// <c>localPos = target − trimCenter(body) + (0, scale × h_occ / 2)</c>
        /// （`h_occ` = 乘员裁剪框高：底心相对裁剪框中心 = (0, −h/2)）。
        /// `target` 与它的出处见 <see cref="PrincessCavityCxPx"/> / <see cref="PrincessFootLinePx"/>。
        /// </para>
        /// <para>复跑：`tools/probes/cr-t1i-occ.py --calib`（校准裁剪框口径）/ `--sweep`（与原版 1:1 并排逐格）。</para>
        /// </summary>
        private static Vector2 PrincessOccupantLocalPx(Sprite body, Sprite occ, float scale)
        {
            var br = body.rect;
            var or = occ.rect;
            var texH = body.texture != null ? body.texture.height : 0f;
            // Unity 的 sprite.rect 原点在**贴图左下**、y 向上 ⇒ 直接用，不做行号折算。
            var tbX = br.x + br.width * 0.5f;
            var tbY = br.y + br.height * 0.5f;
            var target = new Vector2(PrincessCavityCxPx, texH - PrincessFootLinePx);
            return new Vector2(target.x - tbX, target.y - tbY + scale * or.height * 0.5f);
        }

        /// <summary>该塔的**塔体**帧号（日志/断言用，与配方表的第一行同源）。</summary>
        private static int BodyFrameOf(int team, bool isKing)
        {
            if (isKing) return team == 0 ? BlueTowerBodyFrame : RedTowerBodyFrame;
            return team == 0 ? BluePrincessBodyFrame : RedPrincessBodyFrame;
        }

        /// <summary>
        /// 按**帧号**取 Sprite —— ⛔ 绝对不要用数组下标。
        /// <para>
        /// <b>为什么不能按下标</b>：`Game.Res.LoadAll&lt;Sprite&gt;(dir)` 返回的是**扁平化的子 Sprite 列表**，
        /// 一张 PNG 可能被切成多个子 Sprite（`building_tower_out` 的 214 张 PNG 里有 22 张含额外子图，
        /// 其中 19 张排在 203 之前）⇒ `frames[203]` 实际取到的是 `frame_184_0`（一台小炮车），
        /// 不是国王塔。实测：`[BattleViewRoot]` 日志里塔建了 6 座、无降级 Warn，但画出来是炮车。
        /// </para>
        /// <para>名字规则：`frame_NNN_SUB`（NNN = 倒数第二段数字，SUB = 最后一段数字）；取 `SUB == 0` 的那张。</para>
        /// </summary>
        private static Sprite FindFrameByNumber(Sprite[] frames, int frameNo)
        {
            if (frames == null) return null;
            for (var i = 0; i < frames.Length; i++)
            {
                var s = frames[i];
                if (s == null) continue;
                if (TryParseFrameName(s.name, out var no, out var sub) && no == frameNo && sub == 0) return s;
            }
            return null;
        }

        /// <summary>
        /// 解析 Unity 导入名 `frame_NNN_SUB`。与 `UnitView.ParseFrameIndex` 同一规则，
        /// 但**本文件内独立实现**：`ArenaView` 与 `UnitView` 分属不同的执行者片，跨文件耦合会让两边互相踩。
        /// 两段式名字（`frame_003`）按「帧号 + 子序号 0」处理。
        /// </summary>
        private static bool TryParseFrameName(string name, out int frameNo, out int subIndex)
        {
            frameNo = -1;
            subIndex = 0;
            if (string.IsNullOrEmpty(name)) return false;
            var parts = name.Split('_');
            if (parts.Length < 2) return false;
            if (parts.Length == 2) return int.TryParse(parts[1], out frameNo);
            if (!int.TryParse(parts[parts.Length - 1], out subIndex)) return false;
            return int.TryParse(parts[parts.Length - 2], out frameNo);
        }

        private void ApplyTowerTeam(int team, TowerState[] states)
        {
            if (states == null) return;
            var princessNext = 0;
            for (var i = 0; i < states.Length; i++)
            {
                var s = states[i];
                if (s == null) continue;
                TowerView target;
                if (s.kind == 1) // 1 = 国王塔（服务端 `core.TowerKindKing`）
                {
                    target = FindTower(team, true, 0);
                }
                else
                {
                    // 公主塔：按"出现顺序 = 左、右"（服务端 roster 顺序 [王, 左, 右]）。
                    target = FindTower(team, false, princessNext);
                    princessNext++;
                }
                if (target == null)
                {
                    Game.Logger?.Warn(LogTag, $"快照里的塔在本地找不到对应节点：team={team} kind={s.kind} idx={i}");
                    continue;
                }
                target.Apply(s);
            }
        }

        private TowerView FindTower(int team, bool isKing, int princessOrdinal)
        {
            var seen = 0;
            for (var i = 0; i < _towers.Count; i++)
            {
                var t = _towers[i];
                if (t.Team != team || t.IsKing != isKing) continue;
                if (isKing) return t;
                if (seen == princessOrdinal) return t;
                seen++;
            }
            return null;
        }

        private void ClearGenerated()
        {
            for (var i = 0; i < _towers.Count; i++) _towers[i].Destroy();
            _towers.Clear();
            for (var i = 0; i < _generated.Count; i++)
                if (_generated[i] != null) Destroy(_generated[i]);
            _generated.Clear();
            for (var i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }

        private void OnDestroy()
        {
            for (var i = 0; i < _generated.Count; i++)
                if (_generated[i] != null) Destroy(_generated[i]);
            _generated.Clear();
        }

        /// <summary>
        /// 一座塔的视图（塔**不在**快照的 `entities` 里，所以不能复用 <see cref="UnitView"/>；
        /// 塔位是固定几何、由 `GameConst` 给出）。
        /// </summary>
        internal sealed class TowerView
        {
            /// <summary>队伍：0=BLUE 1=RED。</summary>
            public readonly int Team;

            /// <summary>是否国王塔。</summary>
            public readonly bool IsKing;

            /// <summary>塔位（格，**已按队伍镜像后的绝对坐标**）—— 调试与自检用。</summary>
            public readonly float TileX;
            public readonly float TileY;

            private readonly SpriteRenderer _renderer;
            private readonly WorldHpBar _bar;
            private int _lastHp = -1;
            private int _lastMaxHp = -1;
            private bool _destroyed;

            /// <summary>该塔当前是否存活（`BattleViewRoot` 拿它做部署合法性预校验）。</summary>
            public bool Alive { get; private set; } = true;

            public TowerView(int team, bool isKing, float tileX, float tileY, SpriteRenderer renderer, WorldHpBar bar)
            {
                Team = team;
                IsKing = isKing;
                TileX = tileX;
                TileY = tileY;
                _renderer = renderer;
                _bar = bar;
            }

            /// <summary>用服务端的一条塔状态刷新。</summary>
            public void Apply(TowerState s)
            {
                Alive = s.alive;
                if (s.hp == _lastHp && s.max_hp == _lastMaxHp && _destroyed == !s.alive) return;
                _lastHp = s.hp;
                _lastMaxHp = s.max_hp;

                if (_bar != null)
                {
                    _bar.SetHp(s.hp, s.max_hp);
                    // 满血不显示（与单位一致）；摧毁后血条与塔身一起藏掉。
                    _bar.SetVisible(s.alive && s.max_hp > 0 && s.hp > 0 && s.hp < s.max_hp);
                }

                if (!s.alive && !_destroyed)
                {
                    _destroyed = true;
                    // ⚠️ 素材里没有可靠的"摧毁态"帧下标（214 帧只认出了 4 个塔本体帧，见常量注释），
                    // 所以摧毁时**藏掉整座塔的所有层**（塔体 / 国王 / 炮塔 / 公主）而不是画废墟 ——
                    // ⛔ 只藏 Body 层会把国王与炮塔孤零零留在场上（本片叠层后新增的坑，必须一起藏）。
                    SetAllLayersEnabled(false);
                    Game.Logger?.Info(LogTag,
                        $"塔被摧毁：team={Team} king={IsKing} 位置=({TileX:F1},{TileY:F1})（全部层已隐藏；素材缺摧毁态下标，未画废墟）");
                }
            }

            /// <summary>
            /// 一次性开关这座塔**所有层**的渲染器（塔体 / 国王 / 炮塔 / 公主各是一层，见
            /// <see cref="ArenaView.TowerLayer"/>）。层集合从塔根节点现取，不缓存数组 ——
            /// 层是 <see cref="ArenaView.AddTower"/> 建完就固定的，重建时整个 <c>Towers</c> 根会重造。
            /// </summary>
            private void SetAllLayersEnabled(bool enabled)
            {
                if (_renderer == null) return;
                var root = _renderer.transform.parent;
                if (root == null)
                {
                    _renderer.enabled = enabled;
                    return;
                }
                var layers = root.GetComponentsInChildren<SpriteRenderer>(true);
                for (var i = 0; i < layers.Length; i++)
                    if (layers[i] != null) layers[i].enabled = enabled;
            }

            /// <summary>拆掉这座塔（出图/重建时）。</summary>
            public void Destroy()
            {
                // 显式写全名：本文件同时用到了 `System.Collections.Generic` 与 `UnityEngine`，
                // 裸写 `Object` 在将来加 `using System;` 时会变成歧义（编译期才发现，不值得赌）。
                // ⛔ 拆的是**塔根节点**（塔体 / 国王 / 炮塔 / 公主各是一层）—— 只拆 Body 会留下其余层。
                if (_renderer != null && _renderer.transform.parent != null)
                    UnityEngine.Object.Destroy(_renderer.transform.parent.gameObject);
                else if (_renderer != null)
                    UnityEngine.Object.Destroy(_renderer.gameObject);
            }
        }
    }
}
