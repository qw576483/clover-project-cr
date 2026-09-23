package core

import "strings"

// Crown towers.
//
// A princess tower is always active. A king tower is **lazy**: it joins the
// fight only after it is provoked -- one of its own princess towers falling, or
// the king itself being attacked -- and then only after KingActivationMs
// (3300 ms). Source: 参考规格 §4.1 / cr-sim engine/battle.py:140
// `KING_ACTIVATION_MS`, read from BUILDING.KingTower's action graph.

// towerSlot describes where a crown tower stands.
type towerSlot struct {
	kind   int32 // TowerKindPrincess / TowerKindKing
	x, y   int32
	defKey string
}

// towerSlotsFor returns the three tower slots of one team, in a fixed
// deterministic order (king first, then left, then right).
func towerSlotsFor(team Team) []towerSlot {
	if team == TeamBlue {
		return []towerSlot{
			{kind: TowerKindKing, x: BlueKingTowerPos[0], y: BlueKingTowerPos[1], defKey: KeyKingTower},
			{kind: TowerKindPrincess, x: BluePrincessLeftPos[0], y: BluePrincessLeftPos[1], defKey: KeyPrincessTower},
			{kind: TowerKindPrincess, x: BluePrincessRightPos[0], y: BluePrincessRightPos[1], defKey: KeyPrincessTower},
		}
	}
	return []towerSlot{
		{kind: TowerKindKing, x: RedKingTowerPos[0], y: RedKingTowerPos[1], defKey: KeyKingTower},
		{kind: TowerKindPrincess, x: RedPrincessLeftPos[0], y: RedPrincessLeftPos[1], defKey: KeyPrincessTower},
		{kind: TowerKindPrincess, x: RedPrincessRightPos[0], y: RedPrincessRightPos[1], defKey: KeyPrincessTower},
	}
}

// isKingKey reports whether a unit key denotes a king tower (case-insensitive,
// matching cr-sim's `"King" in name` test).
func isKingKey(key string) bool {
	return strings.Contains(strings.ToLower(key), "king")
}

// isKingDef reports whether a def is a king tower. The king is identified by
// its name, exactly as the reference implementation does.
func isKingDef(def *UnitDef) bool {
	if def == nil {
		return false
	}
	if isKingKey(def.Key) {
		return true
	}
	return isKingKey(def.NameCN)
}

// kingTower is a crown tower plus its lazy-activation bookkeeping.
type kingTower struct {
	*entity
	// active is true once the tower may fight.
	active bool
	// wakeMsLeft counts down the activation delay; it is armed (set to
	// KingActivationMs) the moment the tower is provoked.
	wakeMsLeft int32
	// armedAtMs / activeAtMs record the wall clock of both edges, so a test can
	// pin "exactly 3300 ms" rather than infer it from a damage event.
	armedAtMs  int32
	activeAtMs int32
	provoked   bool
}

// arm provokes the king tower, starting its activation countdown. Repeated
// provocations do not restart the countdown (the action graph runs once).
func (k *kingTower) arm(serverMs int32) {
	if k.provoked {
		return
	}
	k.provoked = true
	k.armedAtMs = serverMs
	k.wakeMsLeft = KingActivationMs
}

// tickActivation advances the countdown by one tick and returns true on the
// tick the tower becomes active.
func (k *kingTower) tickActivation() bool {
	if k.provoked && !k.active {
		k.wakeMsLeft -= MSecPerTick
		if k.wakeMsLeft <= 0 {
			k.wakeMsLeft = 0
			k.active = true
			return true
		}
	}
	return false
}

// canFight reports whether this tower may attack this tick.
func (k *kingTower) canFight() bool { return k.active }
