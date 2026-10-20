namespace CR.View
{
    /// <summary>
    /// **塔层逐帧动画表**：塔上在原版里逐帧变化的那几层（国王坐像 / 公主塔乘员）的帧序列与逐帧矩阵。
    /// <para>
    /// <b>为什么需要它</b>：<see cref="ArenaView"/> 的层配方（`BlueKingRecipe` 等）只给**第 1 帧**的静态叠放，
    /// 所以塔建出来以后一动不动；而这些层在原版里是**按 `.sc` 时间轴逐帧换 `frame_NNN` / 改矩阵**的。
    /// </para>
    /// <para>
    /// <b>出处（原版数据反解，⛔ 本表是生成物、不许手改）</b>：`原版资源/sc/building_tower_v215.sc` 的
    /// clip **244**（RED 国王 idle = `KingTower_red` 的 `king_idle` 子件 245 → 244）与 clip **251**
    /// （BLUE 国王 idle = `KingTower_blue` 的 `king_idle` 子件 252 → 251）；`原版资源/sc/chr_princess_v215.sc` 的
    /// clip **753**（RED 乘员 idle = export `princess_tower_red_idle1_7`）与 clip **981**
    /// （BLUE 乘员 idle = export `princess_tower_idle1_3`）。
    /// </para>
    /// <para>
    /// **逐帧口径**：`0x0C` 记录的 `cnt1` 条 `(childIndex, matrixId, ctId)` = 逐帧放置表；这四个 clip 实测
    /// `cnt1 == frames`（每帧恰好 1 条）⇒ 第 i 帧画的子件 = 第 i 条 placement 的 `childIndex` 所指的子件；
    /// 该子件 id 若是 shapeID 就是它，若是子 clip 则取它的 `cnt2` 首项。矩阵 `0x08` = `(a,b,c,d,tx,ty)`：
    /// `sy = d/1024`、`dyPx = ty/20`（`.sc` 画布 px，**y 向下为正**）；`65535` = 无矩阵（本表记 `sy=1 / dyPx=0`）。
    /// </para>
    /// <para>生成路径：`tools/probes/sc-placement.py` + `tools/probes/sc-anim-index.py` → `tools/probes/tower-anim-gen.py`。</para>
    /// </summary>
    public static class TowerAnimTable
    {
        /// <summary>一段：帧号（`frame_NNN` 的 NNN）+ 连续帧数 + 该段的画布 y 缩放与 y 偏移。</summary>
        public readonly struct Key
        {
            /// <summary>帧号（= `.sc` shapeID = `frame_NNN` 的 NNN）。</summary>
            public readonly int Frame;
            /// <summary>本段连续播放的帧数（同帧同矩阵合并；≥ 1）。</summary>
            public readonly int Repeat;
            /// <summary>该段矩阵的 y 缩放（`0x08` 的 `d/1024`；1 = 不缩放）。</summary>
            public readonly float Sy;
            /// <summary>该段矩阵的 y 偏移（画布 px，`.sc` 口径 **y 向下为正**；写到 Unity 要取负）。</summary>
            public readonly float DyPx;
            public Key(int frame, int repeat, float sy, float dyPx)
            {
                Frame = frame; Repeat = repeat; Sy = sy; DyPx = dyPx;
            }
        }

        /// <summary>一条动画：`.sc` 时间轴 fps + 逐段键。</summary>
        public readonly struct Anim
        {
            /// <summary>`.sc` 时间轴 fps（原版按它播）。</summary>
            public readonly int Fps;
            /// <summary>逐段键（按时间序）。</summary>
            public readonly Key[] Keys;
            /// <summary>时间轴总帧数（= 各段 <see cref="Key.Repeat"/> 之和）。</summary>
            public readonly int FrameCount;
            public Anim(int fps, Key[] keys)
            {
                Fps = fps; Keys = keys;
                var n = 0;
                for (var i = 0; i < keys.Length; i++) n += keys[i].Repeat;
                FrameCount = n;
            }
        }

        /// <summary>队伍常量（与契约 `EntitySnapshot.team` 同值：0=BLUE 1=RED）。</summary>
        public const int TeamBlue = 0;
        /// <summary>队伍常量（与契约 `EntitySnapshot.team` 同值）。</summary>
        public const int TeamRed = 1;

        /// <summary>炮塔的一个方向帧：帧号 + 该帧**炮口**的朝向角。</summary>
        public readonly struct TurretDir
        {
            /// <summary>帧号（`frame_NNN` 的 NNN）。</summary>
            public readonly int Frame;
            /// <summary>**炮口朝向**角（度）：0 = 世界 +x（屏幕右）、90 = 世界 +y（屏幕上），与 `Mathf.Atan2` 同口径。</summary>
            public readonly float AngleDeg;
            public TurretDir(int frame, float angleDeg)
            {
                Frame = frame; AngleDeg = angleDeg;
            }
        }

        /// <summary>
        /// 炮塔的 **18 个方向帧**（`building_tower_v215.sc` clip 247 的 `cnt2` shapeID = 30..47）。
        /// <para>
        /// <b>炮口 / 炮尾的判据（决定角度要不要翻 180°）</b>：`alpha 质心 → 最远 alpha 像素` 取到的是**炮管的远端**，
        /// 而 PNG 上判炮口要看**近端形态** —— `f30/f31` 近端是光滑实心圆头 = **炮尾**（⇒ 炮口朝远/上）、
        /// `f46/f47` 近端是金环 + 暗色圆口 = **炮口**（⇒ 炮口朝下/近）。
        /// 两条既有常数与之一致：`ArenaView.BlueKingTurretFrame` = 30（蓝方在屏幕下方、敌人在上 ⇒ 炮口朝上）、
        /// `RedKingTurretFrame` = 47（红方在上、敌人在下 ⇒ 炮口朝下）。
        /// 故本表角度 = `atan2(-(row-cy), px-cx) + 180°`（归一到 ±180）。
        /// 参考图裁图证据：`.ai-tmp/test/cr-fx-d/turret-zoom.png`（逐帧近端）、`ref-blue-king.png`（我方）、
        /// `ref-red-king-18.png`（敌方）。
        /// <para>
        /// **覆盖范围**：18 帧是一条以 0°（右）为中心的 **180° 弧**（`f30 = +85.3°` 到 `f47 = -95.2°`）。
        /// 右半（弧外）在原版没有帧 ⇒ 调用方用 `SpriteRenderer.flipX` 镜像同一族帧补出另外 180°。
        /// </para>
        /// </summary>
        public static readonly TurretDir[] TurretDirs = new[]
        {
            new TurretDir(30, 85.3f),
            new TurretDir(31, 77.8f),
            new TurretDir(32, 67.2f),
            new TurretDir(33, 56.5f),
            new TurretDir(34, 48.3f),
            new TurretDir(35, 37.9f),
            new TurretDir(36, 22.2f),
            new TurretDir(37, 16.2f),
            new TurretDir(38, 10.9f),
            new TurretDir(39, 4.2f),
            new TurretDir(40, -9.1f),
            new TurretDir(41, -13.8f),
            new TurretDir(42, -25.3f),
            new TurretDir(43, -35.6f),
            new TurretDir(44, -50.6f),
            new TurretDir(45, -59.8f),
            new TurretDir(46, -83.2f),
            new TurretDir(47, -95.2f),
        };

        /// <summary>RED 国王坐像 idle（`KingTower_red` 的 `king_idle`，clip 244，13 个姿态帧）。</summary>
        private static readonly Anim KingRed = new Anim(30, new[]
        {
            new Key(16, 15, 1f, 0f),
            new Key(17, 2, 1f, 0f),
            new Key(18, 36, 1f, 0f),
            new Key(19, 2, 1f, 0f),
            new Key(20, 102, 1f, 0f),
            new Key(21, 2, 1f, 0f),
            new Key(22, 2, 1f, 0f),
            new Key(23, 2, 1f, 0f),
            new Key(24, 2, 1f, 0f),
            new Key(25, 66, 1f, 0f),
            new Key(26, 2, 1f, 0f),
            new Key(27, 2, 1f, 0f),
            new Key(28, 134, 1f, 0f),
            new Key(16, 4, 1f, 0f),
            new Key(17, 2, 1f, 0f),
            new Key(18, 2, 1f, 0f),
            new Key(19, 2, 1f, 0f),
            new Key(20, 87, 1f, 0f),
            new Key(19, 2, 1f, 0f),
            new Key(18, 2, 1f, 0f),
            new Key(17, 2, 1f, 0f),
            new Key(16, 4, 1f, 0f),
            new Key(28, 45, 1f, 0f),
            new Key(27, 2, 1f, 0f),
            new Key(26, 2, 1f, 0f),
            new Key(25, 82, 1f, 0f),
            new Key(24, 2, 1f, 0f),
            new Key(23, 2, 1f, 0f),
            new Key(22, 2, 1f, 0f),
            new Key(21, 2, 1f, 0f),
            new Key(20, 113, 1f, 0f),
            new Key(19, 2, 1f, 0f),
            new Key(18, 90, 1f, 0f),
            new Key(17, 2, 1f, 0f),
            new Key(16, 2, 1f, 0f),
        });

        /// <summary>BLUE 国王坐像 idle（`KingTower_blue` 的 `king_idle`，clip 251，13 个姿态帧）。</summary>
        private static readonly Anim KingBlue = new Anim(30, new[]
        {
            new Key(101, 65, 1f, 0f),
            new Key(102, 2, 1f, 0f),
            new Key(103, 30, 1f, 0f),
            new Key(104, 2, 1f, 0f),
            new Key(105, 69, 1f, 0f),
            new Key(106, 2, 1f, 0f),
            new Key(107, 2, 1f, 0f),
            new Key(108, 2, 1f, 0f),
            new Key(109, 2, 1f, 0f),
            new Key(110, 32, 1f, 0f),
            new Key(111, 2, 1f, 0f),
            new Key(112, 2, 1f, 0f),
            new Key(113, 71, 1f, 0f),
            new Key(101, 4, 1f, 0f),
            new Key(102, 2, 1f, 0f),
            new Key(103, 2, 1f, 0f),
            new Key(104, 2, 1f, 0f),
            new Key(105, 88, 1f, 0f),
            new Key(104, 2, 1f, 0f),
            new Key(103, 2, 1f, 0f),
            new Key(102, 42, 1f, 0f),
            new Key(101, 4, 1f, 0f),
            new Key(113, 2, 1f, 0f),
            new Key(112, 2, 1f, 0f),
            new Key(111, 2, 1f, 0f),
            new Key(110, 50, 1f, 0f),
            new Key(109, 2, 1f, 0f),
            new Key(108, 2, 1f, 0f),
            new Key(107, 2, 1f, 0f),
            new Key(106, 76, 1f, 0f),
            new Key(105, 2, 1f, 0f),
            new Key(104, 2, 1f, 0f),
            new Key(103, 2, 1f, 0f),
            new Key(102, 2, 1f, 0f),
            new Key(101, 108, 1f, 0f),
        });

        /// <summary>RED 公主塔乘员 idle（export `princess_tower_red_idle1_7`，clip 753，单姿态 + 逐帧 squash 矩阵）。</summary>
        private static readonly Anim OccupantRed = new Anim(24, new[]
        {
            new Key(2, 1, 0.9893f, 0.05f),
            new Key(2, 1, 0.9854f, 0.05f),
            new Key(2, 1, 0.9814f, 0.05f),
            new Key(2, 1, 0.9785f, 0.05f),
            new Key(2, 1, 0.9746f, 0.1f),
            new Key(2, 1, 0.9717f, 0.1f),
            new Key(2, 1, 0.9678f, 0.1f),
            new Key(2, 1, 0.9648f, 0.15f),
            new Key(2, 1, 0.9619f, 0.15f),
            new Key(2, 1, 0.959f, 0.15f),
            new Key(2, 1, 0.9561f, 0.15f),
            new Key(2, 1, 0.9541f, 0.2f),
            new Key(2, 1, 0.9521f, 0.2f),
            new Key(2, 1, 0.9502f, 0.2f),
            new Key(2, 1, 0.9492f, 0.2f),
            new Key(2, 1, 0.9502f, 0.2f),
            new Key(2, 1, 0.9521f, 0.2f),
            new Key(2, 1, 0.9531f, 0.2f),
            new Key(2, 1, 0.9561f, 0.15f),
            new Key(2, 1, 0.958f, 0.15f),
            new Key(2, 1, 0.9609f, 0.15f),
            new Key(2, 1, 0.9639f, 0.15f),
            new Key(2, 1, 0.9668f, 0.1f),
            new Key(2, 1, 0.9697f, 0.1f),
            new Key(2, 1, 0.9727f, 0.1f),
            new Key(2, 1, 0.9756f, 0.1f),
            new Key(2, 1, 0.9795f, 0.05f),
            new Key(2, 1, 0.9824f, 0.05f),
            new Key(2, 1, 0.9863f, 0.05f),
            new Key(2, 1, 0.9893f, 0f),
        });

        /// <summary>BLUE 公主塔乘员 idle（export `princess_tower_idle1_3`，clip 981，单姿态 + 逐帧 squash 矩阵）。</summary>
        private static readonly Anim OccupantBlue = new Anim(24, new[]
        {
            new Key(502, 1, 0.9893f, 0.05f),
            new Key(502, 1, 0.9854f, 0.05f),
            new Key(502, 1, 0.9814f, 0.05f),
            new Key(502, 1, 0.9785f, 0.05f),
            new Key(502, 1, 0.9746f, 0.1f),
            new Key(502, 1, 0.9717f, 0.1f),
            new Key(502, 1, 0.9678f, 0.1f),
            new Key(502, 1, 0.9648f, 0.15f),
            new Key(502, 1, 0.9619f, 0.15f),
            new Key(502, 1, 0.959f, 0.15f),
            new Key(502, 1, 0.9561f, 0.15f),
            new Key(502, 1, 0.9541f, 0.2f),
            new Key(502, 1, 0.9521f, 0.2f),
            new Key(502, 1, 0.9502f, 0.2f),
            new Key(502, 1, 0.9492f, 0.2f),
            new Key(502, 1, 0.9502f, 0.2f),
            new Key(502, 1, 0.9521f, 0.2f),
            new Key(502, 1, 0.9531f, 0.2f),
            new Key(502, 1, 0.9561f, 0.15f),
            new Key(502, 1, 0.958f, 0.15f),
            new Key(502, 1, 0.9609f, 0.15f),
            new Key(502, 1, 0.9639f, 0.15f),
            new Key(502, 1, 0.9668f, 0.1f),
            new Key(502, 1, 0.9697f, 0.1f),
            new Key(502, 1, 0.9727f, 0.1f),
            new Key(502, 1, 0.9756f, 0.1f),
            new Key(502, 1, 0.9795f, 0.05f),
            new Key(502, 1, 0.9824f, 0.05f),
            new Key(502, 1, 0.9863f, 0.05f),
            new Key(502, 1, 0.9893f, 0f),
        });

        /// <summary>BLUE 公主塔乘员攻击档（下标 = 视角号 − 1；`princess_tower_attack1_N`）。</summary>
        private static readonly Anim[] OccupantAttackBlue = new[]
        {
            new Anim(24, new[] { new Key(504, 2, 1f, 0f), new Key(569, 2, 1f, 0f), new Key(570, 2, 1f, 0f), new Key(571, 2, 1f, 0f), new Key(572, 2, 1f, 0f), new Key(573, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(503, 2, 1f, 0f), new Key(561, 2, 1f, 0f), new Key(562, 2, 1f, 0f), new Key(563, 2, 1f, 0f), new Key(564, 2, 1f, 0f), new Key(565, 2, 1f, 0f), new Key(566, 2, 1f, 0f), new Key(567, 2, 1f, 0f), new Key(568, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(502, 2, 1f, 0f), new Key(553, 2, 1f, 0f), new Key(554, 2, 1f, 0f), new Key(555, 2, 1f, 0f), new Key(556, 2, 1f, 0f), new Key(557, 2, 1f, 0f), new Key(558, 2, 1f, 0f), new Key(559, 2, 1f, 0f), new Key(560, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(501, 2, 1f, 0f), new Key(545, 2, 1f, 0f), new Key(546, 2, 1f, 0f), new Key(547, 2, 1f, 0f), new Key(548, 2, 1f, 0f), new Key(549, 2, 1f, 0f), new Key(550, 2, 1f, 0f), new Key(551, 2, 1f, 0f), new Key(552, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(500, 2, 1f, 0f), new Key(537, 2, 1f, 0f), new Key(538, 2, 1f, 0f), new Key(539, 2, 1f, 0f), new Key(540, 2, 1f, 0f), new Key(541, 2, 1f, 0f), new Key(542, 2, 1f, 0f), new Key(543, 2, 1f, 0f), new Key(544, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(499, 2, 1f, 0f), new Key(529, 2, 1f, 0f), new Key(530, 2, 1f, 0f), new Key(531, 2, 1f, 0f), new Key(532, 2, 1f, 0f), new Key(533, 2, 1f, 0f), new Key(534, 2, 1f, 0f), new Key(535, 2, 1f, 0f), new Key(536, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(498, 2, 1f, 0f), new Key(521, 2, 1f, 0f), new Key(522, 2, 1f, 0f), new Key(523, 2, 1f, 0f), new Key(524, 2, 1f, 0f), new Key(525, 2, 1f, 0f), new Key(526, 2, 1f, 0f), new Key(527, 2, 1f, 0f), new Key(528, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(497, 2, 1f, 0f), new Key(513, 2, 1f, 0f), new Key(514, 2, 1f, 0f), new Key(515, 2, 1f, 0f), new Key(516, 2, 1f, 0f), new Key(517, 2, 1f, 0f), new Key(518, 2, 1f, 0f), new Key(519, 2, 1f, 0f), new Key(520, 2, 1f, 0f) }),
            new Anim(24, new[] { new Key(496, 2, 1f, 0f), new Key(505, 2, 1f, 0f), new Key(506, 2, 1f, 0f), new Key(507, 2, 1f, 0f), new Key(508, 2, 1f, 0f), new Key(509, 2, 1f, 0f), new Key(510, 2, 1f, 0f), new Key(511, 2, 1f, 0f), new Key(512, 2, 1f, 0f) }),
        };

        /// <summary>RED 公主塔乘员攻击档（下标 = 视角号 − 1；`princess_tower_red_attack1_N`）。</summary>
        private static readonly Anim[] OccupantAttackRed = new[]
        {
            new Anim(24, new[] { new Key(8, 3, 1f, 0f), new Key(105, 3, 1f, 0f), new Key(106, 3, 1f, 0f), new Key(107, 3, 1f, 0f), new Key(108, 3, 1f, 0f), new Key(109, 3, 1f, 0f), new Key(110, 3, 1f, 0f), new Key(112, 3, 1f, 0f), new Key(114, 3, 1f, 0f), new Key(115, 3, 1f, 0f), new Key(116, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(7, 3, 1f, 0f), new Key(93, 3, 1f, 0f), new Key(94, 3, 1f, 0f), new Key(95, 3, 1f, 0f), new Key(96, 3, 1f, 0f), new Key(97, 3, 1f, 0f), new Key(98, 3, 1f, 0f), new Key(100, 3, 1f, 0f), new Key(102, 3, 1f, 0f), new Key(103, 3, 1f, 0f), new Key(104, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(6, 3, 1f, 0f), new Key(81, 3, 1f, 0f), new Key(82, 3, 1f, 0f), new Key(83, 3, 1f, 0f), new Key(84, 3, 1f, 0f), new Key(85, 3, 1f, 0f), new Key(86, 3, 1f, 0f), new Key(88, 3, 1f, 0f), new Key(90, 3, 1f, 0f), new Key(91, 3, 1f, 0f), new Key(92, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(5, 3, 1f, 0f), new Key(69, 3, 1f, 0f), new Key(70, 3, 1f, 0f), new Key(71, 3, 1f, 0f), new Key(72, 3, 1f, 0f), new Key(73, 3, 1f, 0f), new Key(74, 3, 1f, 0f), new Key(76, 3, 1f, 0f), new Key(78, 3, 1f, 0f), new Key(79, 3, 1f, 0f), new Key(80, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(4, 3, 1f, 0f), new Key(57, 3, 1f, 0f), new Key(58, 3, 1f, 0f), new Key(59, 3, 1f, 0f), new Key(60, 3, 1f, 0f), new Key(61, 3, 1f, 0f), new Key(62, 3, 1f, 0f), new Key(64, 3, 1f, 0f), new Key(66, 3, 1f, 0f), new Key(67, 3, 1f, 0f), new Key(68, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(3, 3, 1f, 0f), new Key(45, 3, 1f, 0f), new Key(46, 3, 1f, 0f), new Key(47, 3, 1f, 0f), new Key(48, 3, 1f, 0f), new Key(49, 3, 1f, 0f), new Key(50, 3, 1f, 0f), new Key(52, 3, 1f, 0f), new Key(54, 3, 1f, 0f), new Key(55, 3, 1f, 0f), new Key(56, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(2, 3, 1f, 0f), new Key(33, 3, 1f, 0f), new Key(34, 3, 1f, 0f), new Key(35, 3, 1f, 0f), new Key(36, 3, 1f, 0f), new Key(37, 3, 1f, 0f), new Key(38, 3, 1f, 0f), new Key(40, 3, 1f, 0f), new Key(42, 3, 1f, 0f), new Key(43, 3, 1f, 0f), new Key(44, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(1, 3, 1f, 0f), new Key(21, 3, 1f, 0f), new Key(22, 3, 1f, 0f), new Key(23, 3, 1f, 0f), new Key(24, 3, 1f, 0f), new Key(25, 3, 1f, 0f), new Key(26, 3, 1f, 0f), new Key(28, 3, 1f, 0f), new Key(30, 3, 1f, 0f), new Key(31, 3, 1f, 0f), new Key(32, 3, 1f, 0f) }),
            new Anim(24, new[] { new Key(0, 3, 1f, 0f), new Key(9, 3, 1f, 0f), new Key(10, 3, 1f, 0f), new Key(11, 3, 1f, 0f), new Key(12, 3, 1f, 0f), new Key(13, 3, 1f, 0f), new Key(14, 3, 1f, 0f), new Key(16, 3, 1f, 0f), new Key(18, 3, 1f, 0f), new Key(19, 3, 1f, 0f), new Key(20, 3, 1f, 0f) }),
        };

        /// <summary>国王坐像的 idle 动画（`team` = 0 BLUE / 1 RED）。</summary>
        public static Anim KingIdle(int team)
        {
            return team == TeamRed ? KingRed : KingBlue;
        }

        /// <summary>公主塔乘员的 idle 动画（`team` = 0 BLUE / 1 RED）。</summary>
        public static Anim OccupantIdle(int team)
        {
            return team == TeamRed ? OccupantRed : OccupantBlue;
        }

        /// <summary>
        /// 公主塔乘员的**攻击档**（开火时播一遍，见 `ArenaView.TowerView.PlayAttack`）。
        /// <para>`view` = 视角号（1..9）；越界或该视角无帧 ⇒ 返回 `FrameCount == 0` 的动画（调用方保持 idle）。</para>
        /// <para>
        /// 帧来源见生成器 `tower-anim-gen.py` 的 `ATTACK_BLUE` / `ATTACK_RED`：
        /// `策划/单位动画分组表.md` 的 `princess_tower_attack1_N` / `princess_tower_red_attack1_N`，
        /// 已剔除导出里夹带的另一队配色帧。
        /// </para>
        /// </summary>
        public static Anim OccupantAttack(int team, int view)
        {
            var all = team == TeamRed ? OccupantAttackRed : OccupantAttackBlue;
            var i = view - 1;
            return i >= 0 && i < all.Length ? all[i] : default(Anim);
        }
    }
}
