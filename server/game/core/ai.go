package core

// AI 决策。
//
// 纯逻辑：零引擎依赖、不引 logger（core 的约定 —— 非预期分支用返回值表达）。
// 策略口径（够"像人"即可，不必最强）：
//
//	① 对手地面部队越过桥进入己方半场 ⇒ 防守：用能打到它的最便宜手牌，
//	   落点在自己塔前 / 桥头，离威胁 3~5 格；
//	② 否则圣水 ≥ 7（已进双倍 / 三倍段则 ≥ 6）⇒ 进攻：手牌里最贵的部队卡，落在我方桥头
//	   （蓝 y ≈ 13.5 格 / 红 y ≈ 18.5 格）威胁较小的那一路；
//	③ 法术只在能覆盖 ≥ 2 个敌方单位（或能补刀塔）时使用；
//	④ 无着 ⇒ ok=false，什么都不做。
//
// ★ AI 与真人共用**同一条**出牌路径：Decide 只挑卡与落点，落点必须过 arena.CanDeploy，
// 真正出手由调用方调 Battle.PlayCard（它内部会再校验一次手牌 / 圣水 / 部署区）。
// 所以 AI 没有任何后门，也没法落到非法位置。

const (
	// defendGapMilli 防守落点与威胁单位的距离（本项目口径 3.5 格，落在 3~5 格内）。
	defendGapMilli = 3500
	// spellMinTargets 法术至少覆盖的敌方单位数。
	spellMinTargets = 2
	// attackElixir / attackElixirFast 进攻所需圣水（整点）。
	attackElixir     = 7
	attackElixirFast = 6
	// laneThreatRangeMilli 判断"哪一路威胁小"时的横向搜索半径（3 格）。
	laneThreatRangeMilli = 3000
	// laneThreatDepthMilli 只统计靠近我方半场的敌人（还在后场的不算威胁）。
	laneThreatDepthMilli = 6000
)

// Decide 返回 AI 本回合的着法；ok=false 表示不出牌。
func Decide(b *Battle, team Team) (cardID int32, xMilli, yMilli int32, ok bool) {
	if b == nil || !team.Valid() || b.ended {
		return 0, 0, 0, false
	}
	if cid, x, y, hit := decideDefense(b, team); hit {
		return cid, x, y, true
	}
	if cid, x, y, hit := decideSpell(b, team); hit {
		return cid, x, y, true
	}
	if cid, x, y, hit := decideAttack(b, team); hit {
		return cid, x, y, true
	}
	return 0, 0, 0, false
}

// ---------------------------------------------------------------------------
// 共享底盘
// ---------------------------------------------------------------------------

// handDefs 手牌（保持手牌顺序 ⇒ 同一局面决策结果确定）。
func (b *Battle) handDefs(team Team) []*CardDef {
	ids := b.hands[team].Hand()
	out := make([]*CardDef, 0, len(ids))
	for _, id := range ids {
		if c, ok := b.cfg.Table.Card(id); ok && c != nil {
			out = append(out, c)
		}
	}
	return out
}

// canPay 手牌能不能负担（整点圣水）。
func (b *Battle) canPay(team Team, cost int32) bool {
	return b.elixir[team].canAfford(cost)
}

// bodyDef 卡召唤的战斗实体（法术返回 nil）。
func (b *Battle) bodyDef(card *CardDef) *UnitDef {
	if card == nil || card.Kind == CardTypeSpell {
		return nil
	}
	d, ok := b.cfg.Table.Unit(card.UnitKey)
	if !ok {
		return nil
	}
	return d
}

// canHitDef 与 core 自己的索敌硬过滤（canTarget）同口径：不能打的目标不选。
func canHitDef(def *UnitDef, target *entity) bool {
	if def == nil || target == nil || target.Def == nil || !def.Attackable() {
		return false
	}
	if target.flying {
		if !def.AtkAir {
			return false
		}
	} else if !def.AtkGround {
		return false
	}
	if def.OnlyBuildings && !target.Def.IsStructure() {
		return false
	}
	if def.OnlyTowers && target.Kind != KindTower {
		return false
	}
	if def.OnlyTroops && target.Kind != KindTroop {
		return false
	}
	return true
}

// deployable 与出牌路径用的是**同一个** arena.CanDeploy（AI 不许绕过校验）。
func (b *Battle) deployable(team Team, card *CardDef, x, y int32) bool {
	anywhere, onWater := false, false
	if card != nil && card.Kind == CardTypeSpell && card.Spell != nil {
		anywhere, onWater = card.Spell.Anywhere, card.Spell.OnWater
	}
	return b.arena.CanDeploy(team, x, y, anywhere, onWater, b.fallenEnemyPrincess(team))
}

// pickDeployPoint 依次试候选落点，返回第一个能合法落下的。
func (b *Battle) pickDeployPoint(team Team, card *CardDef, cands [][2]int32) (int32, int32, bool) {
	for _, c := range cands {
		x := ClampI32(c[0], 0, ArenaWMilli-1)
		y := ClampI32(c[1], 0, ArenaHMilli-1)
		if b.deployable(team, card, x, y) {
			return x, y, true
		}
	}
	return 0, 0, false
}

// bridgeHeadYMilli 我方桥头（蓝 y = 13.5 格 / 红 y = 18.5 格）。
func bridgeHeadYMilli(team Team) int32 {
	if team == TeamBlue {
		return 13500
	}
	return 18500
}

// inMyHalf 该 y 是否已经进入 team 的己方半场（地面兵能到这 ⇒ 必然已过桥）。
func inMyHalf(team Team, yMilli int32) bool {
	if team == TeamBlue {
		return yMilli < RiverTopMilli
	}
	return yMilli >= RiverBottomMilli
}

// closerToMySide 比较两个 y 谁更靠近我方底线（越大 / 越小取决于阵营）。
func closerToMySide(team Team, y, cur int32) bool {
	if team == TeamBlue {
		return y < cur
	}
	return y > cur
}

func sign32(v int32) int32 {
	switch {
	case v < 0:
		return -1
	case v > 0:
		return 1
	default:
		return 0
	}
}

// pointToward 从 (ox,oy) 朝 (tx,ty) 走 dist 得到的点（整型，先乘后除）。
func pointToward(ox, oy, tx, ty, dist int32) (int32, int32, bool) {
	d := Distance(ox, oy, tx, ty)
	if d <= 0 {
		return 0, 0, false
	}
	return int32(int64(ox) + int64(tx-ox)*int64(dist)/int64(d)),
		int32(int64(oy) + int64(ty-oy)*int64(dist)/int64(d)), true
}

// totalDamage 法术的总伤害估计（瞬伤 + 一整段持续伤害）。仅用于「能不能补刀塔」的粗判。
func (sp *SpellDef) totalDamage() int32 {
	if sp == nil {
		return 0
	}
	d := sp.InstantDamage
	if sp.DurationMs > 0 && sp.TickMs > 0 && sp.DamagePerTick > 0 {
		d += sp.DamagePerTick * (sp.DurationMs / sp.TickMs)
	}
	return d
}

// ---------------------------------------------------------------------------
// ① 防守
// ---------------------------------------------------------------------------

// threatFor 选出最紧急的威胁：已进入我方半场的敌方非塔单位里，最靠近我方底线的那一个。
func (b *Battle) threatFor(team Team) *entity {
	var best *entity
	for _, e := range b.units { // b.units = 部队 + 建筑，按 id 升序（确定）
		if e == nil || !e.acquirable() || e.Team == team {
			continue
		}
		if !inMyHalf(team, e.yMilli) {
			continue
		}
		if best == nil || closerToMySide(team, e.yMilli, best.yMilli) {
			best = e
		}
	}
	return best
}

// myLaneAnchor 我方该车道的公主塔（倒了退到国王塔），作为"往后退"的方向锚点。
func (b *Battle) myLaneAnchor(team Team, xMilli int32) *entity {
	var princess, king *entity
	for _, t := range b.towers {
		if t == nil || !t.alive || t.Team != team || t.Kind != KindTower {
			continue
		}
		if t.Def != nil && isKingDef(t.Def) {
			king = t
			continue
		}
		if !b.arena.SameLane(t.xMilli, xMilli) {
			continue
		}
		if princess == nil || absI32(t.xMilli-xMilli) < absI32(princess.xMilli-xMilli) {
			princess = t
		}
	}
	if princess != nil {
		return princess
	}
	return king
}

func decideDefense(b *Battle, team Team) (int32, int32, int32, bool) {
	threat := b.threatFor(team)
	if threat == nil {
		return 0, 0, 0, false
	}

	// 能打到它、且负担得起的最便宜**部队卡**（法术留给 ③ 单独判：要覆盖 ≥2 个才划算）。
	var pick *CardDef
	for _, c := range b.handDefs(team) {
		def := b.bodyDef(c)
		if def == nil || def.SpeedMilliPerSec <= 0 {
			continue
		}
		if !canHitDef(def, threat) || !b.canPay(team, c.Elixir) {
			continue
		}
		if pick == nil || c.Elixir < pick.Elixir {
			pick = c
		}
	}
	if pick == nil {
		return 0, 0, 0, false
	}

	anchor := b.myLaneAnchor(team, threat.xMilli)
	ax, ay := threat.xMilli, threat.yMilli
	if anchor != nil {
		ax, ay = anchor.xMilli, anchor.yMilli
	}
	cands := make([][2]int32, 0, 8)
	// 理想落点：从威胁处朝我方塔退 3~5 格；逐个距离试，取第一个合法的。
	for _, gap := range [5]int32{defendGapMilli, 3000, 4000, 4500, 5000} {
		if x, y, ok := pointToward(threat.xMilli, threat.yMilli, ax, ay, gap); ok {
			cands = append(cands, [2]int32{x, y})
		}
	}
	// 兜底：威胁所在车道的我方桥头 + 我方公主塔前 3 格。
	laneX := b.arena.NearestBridgeX(threat.xMilli)
	cands = append(cands, [2]int32{laneX, bridgeHeadYMilli(team)})
	if anchor != nil {
		cands = append(cands, [2]int32{anchor.xMilli, anchor.yMilli + sign32(bridgeHeadYMilli(team)-anchor.yMilli)*3000})
	}
	if x, y, ok := b.pickDeployPoint(team, pick, cands); ok {
		return pick.ID, x, y, true
	}
	return 0, 0, 0, false
}

// ---------------------------------------------------------------------------
// ③ 法术
// ---------------------------------------------------------------------------

// bestSpellPoint 以每个敌方单位为圆心试一次，返回覆盖数最多的那个圆心。
func (b *Battle) bestSpellPoint(team Team, sp *SpellDef) (int32, int32, int) {
	bestN, bx, by := 0, int32(0), int32(0)
	r2 := int64(sp.RadiusMilli) * int64(sp.RadiusMilli)
	for _, c := range b.units {
		if c == nil || !c.acquirable() || c.Team == team {
			continue
		}
		n := 0
		for _, o := range b.units {
			if o == nil || !o.acquirable() || o.Team == team {
				continue
			}
			if DistanceSq(c.xMilli, c.yMilli, o.xMilli, o.yMilli) <= r2 {
				n++
			}
		}
		if n > bestN {
			bestN, bx, by = n, c.xMilli, c.yMilli
		}
	}
	return bx, by, bestN
}

func decideSpell(b *Battle, team Team) (int32, int32, int32, bool) {
	var pick *CardDef
	var px, py int32
	for _, c := range b.handDefs(team) {
		if c.Kind != CardTypeSpell || c.Spell == nil || !b.canPay(team, c.Elixir) {
			continue
		}
		sp := c.Spell
		if sp.RadiusMilli <= 0 {
			continue
		}
		// (a) 覆盖 ≥2 个敌方单位
		if x, y, n := b.bestSpellPoint(team, sp); n >= spellMinTargets && b.deployable(team, c, x, y) {
			pick, px, py = c, x, y
			break
		}
		// (b) 能补刀塔（塔剩余血量 ≤ 法术总伤害）
		total := sp.totalDamage()
		if total <= 0 {
			continue
		}
		for _, t := range b.towers {
			if t == nil || !t.alive || t.Team == team || t.Kind != KindTower || t.hp > total {
				continue
			}
			if b.deployable(team, c, t.xMilli, t.yMilli) {
				pick, px, py = c, t.xMilli, t.yMilli
				break
			}
		}
		if pick != nil {
			break
		}
	}
	if pick == nil {
		return 0, 0, 0, false
	}
	return pick.ID, px, py, true
}

// ---------------------------------------------------------------------------
// ② 进攻
// ---------------------------------------------------------------------------

// laneThreats 统计某一路桥附近的敌方单位（离得远的后场兵不算威胁）。
func (b *Battle) laneThreats(team Team, bridgeX int32) int {
	n := 0
	for _, e := range b.units {
		if e == nil || !e.acquirable() || e.Team == team {
			continue
		}
		if absI32(e.xMilli-bridgeX) > laneThreatRangeMilli {
			continue
		}
		if team == TeamBlue {
			if e.yMilli > RiverBottomMilli+laneThreatDepthMilli {
				continue
			}
		} else if e.yMilli < RiverTopMilli-laneThreatDepthMilli {
			continue
		}
		n++
	}
	return n
}

// saferLaneX 挑威胁较少的一路桥（并列取左桥）。
func (b *Battle) saferLaneX(team Team) int32 {
	na := b.laneThreats(team, BridgeAxMilli)
	nb := b.laneThreats(team, BridgeBxMilli)
	if nb < na {
		return BridgeBxMilli
	}
	return BridgeAxMilli
}

func decideAttack(b *Battle, team Team) (int32, int32, int32, bool) {
	need := int32(attackElixir)
	// 「已到双倍 / 三倍段」按**圣水时间轴**判（120 s 起进双倍段），不是按 phase。
	if ElixirPhaseAt(b.serverMs) >= 1 {
		need = attackElixirFast
	}
	if b.elixir[team].units() < need {
		return 0, 0, 0, false
	}

	// 手牌里最贵的**部队卡**（法术不在进攻分支里用）。
	var pick *CardDef
	for _, c := range b.handDefs(team) {
		if c.Kind != CardTypeTroop {
			continue
		}
		def := b.bodyDef(c)
		if def == nil || def.SpeedMilliPerSec <= 0 || !b.canPay(team, c.Elixir) {
			continue
		}
		if pick == nil || c.Elixir > pick.Elixir {
			pick = c
		}
	}
	if pick == nil {
		return 0, 0, 0, false
	}

	laneX := b.saferLaneX(team)
	y := bridgeHeadYMilli(team)
	cands := [][2]int32{
		{laneX, y},
		{laneX, y - 1000},
		{laneX, y + 1000},
		{laneX - 1000, y},
		{laneX + 1000, y},
		{laneX - 2000, y},
		{laneX + 2000, y},
	}
	if x, yy, ok := b.pickDeployPoint(team, pick, cands); ok {
		return pick.ID, x, yy, true
	}
	return 0, 0, 0, false
}
