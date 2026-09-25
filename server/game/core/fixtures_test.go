package core

import "testing"

// fakeTable is an in-memory CardTable for the offline assertions.
//
// Every number below is copied from the official static data, so the tests
// assert the *rules* against real values rather than against made-up ones.
// Sources (all under 原版资源/cr-api-data/docs/json/):
//
//	Knight        cards_stats_characters.json  HP 1766, damage 202, hit_speed 1200,
//	              load_time 700, speed 60, range 1200, sight 5500, radius 500,
//	              mass 6, deploy_time 1000  (cross-checked in anchors.json)
//	PrincessTower cards_stats_building.json    HP 3584, hit_speed 800, load_time 0,
//	              range 7500, sight 7500, radius 1000
//	KingTower     cards_stats_building.json    HP 6144, hit_speed 1000, load_time 500,
//	              range 7000, sight 7000, radius 1400
//	TowerPrincessProjectile cards_stats_projectile.json speed 600, damage 128
//	KingProjectile           cards_stats_projectile.json speed 1000, damage 128
//	Minion        cards_stats_characters.json  HP 230, hit_speed 1000, load_time 500,
//	              speed 90, range 1600, sight 5500, radius 500, mass 2,
//	              flying_height 1500 (= flying)
//	Giant         cards_stats_characters.json  HP 4940, damage 307, hit_speed 1500,
//	              load_time 1000, speed 45, range 1200, sight 7500, radius 750,
//	              mass 18, target_only_buildings
//	Fireball      anchors.json                 damage 688, radius 2500, elixir 4
type fakeTable struct {
	cards map[int32]*CardDef
	units map[string]*UnitDef
}

func (t *fakeTable) Card(id int32) (*CardDef, bool) {
	c, ok := t.cards[id]
	return c, ok
}

func (t *fakeTable) Unit(key string) (*UnitDef, bool) {
	u, ok := t.units[key]
	return u, ok
}

// Card / unit ids used by the fixtures.
const (
	cardKnight      = int32(1)
	cardGiant       = int32(2)
	cardMinion      = int32(3)
	cardSkeleton    = int32(4)
	cardArcher      = int32(5)
	cardGiantTwo    = int32(6)
	cardFireball    = int32(7)
	cardArrows      = int32(8)
	cardKnightCheap = int32(9)
	cardKnightDear  = int32(10)
)

// troopDeck is 8 troop cards (any hand card is deployable-bound), used by the
// deploy-zone assertions.
var troopDeck = []int32{cardKnight, cardGiant, cardMinion, cardSkeleton, cardArcher, cardGiantTwo, cardKnightCheap, cardKnightDear}

// fullDeck is 8 distinct cards including two spells.
var fullDeck = []int32{cardKnight, cardGiant, cardMinion, cardSkeleton, cardArcher, cardGiantTwo, cardFireball, cardArrows}

func newTestTable() *fakeTable {
	t := &fakeTable{cards: map[int32]*CardDef{}, units: map[string]*UnitDef{}}

	t.units["Knight"] = &UnitDef{
		ID: 101, Key: "Knight", NameCN: "骑士", Kind: KindTroop,
		HP: 1766, Damage: 202, HitSpeedMs: 1200, LoadTimeMs: 700,
		SpeedMilliPerSec: SpeedMilliPerSec(60), RangeMilli: 1200, SightMilli: 5500,
		DeployMs: 1000, RadiusMilli: 500, Mass: 6, AtkGround: true,
	}
	t.units["Giant"] = &UnitDef{
		ID: 102, Key: "Giant", NameCN: "巨人", Kind: KindTroop,
		HP: 4940, Damage: 307, HitSpeedMs: 1500, LoadTimeMs: 1000,
		SpeedMilliPerSec: SpeedMilliPerSec(45), RangeMilli: 1200, SightMilli: 7500,
		DeployMs: 1000, RadiusMilli: 750, Mass: 18, AtkGround: true, OnlyBuildings: true,
	}
	t.units["Minion"] = &UnitDef{
		ID: 103, Key: "Minion", NameCN: "亡灵", Kind: KindTroop,
		HP: 230, Damage: 0, HitSpeedMs: 1000, LoadTimeMs: 500,
		SpeedMilliPerSec: SpeedMilliPerSec(90), RangeMilli: 1600, SightMilli: 5500,
		DeployMs: 1000, RadiusMilli: 500, Mass: 2, AtkAir: true, AtkGround: true,
		Flying: true, // flying_height 1500 in the official row
	}
	t.units["Skeleton"] = &UnitDef{
		ID: 104, Key: "Skeleton", NameCN: "骷髅兵", Kind: KindTroop,
		HP: 81, Damage: 81, HitSpeedMs: 1000, LoadTimeMs: 500,
		SpeedMilliPerSec: SpeedMilliPerSec(90), RangeMilli: 500, SightMilli: 5500,
		DeployMs: 1000, RadiusMilli: 500, Mass: 1, AtkGround: true,
	}
	t.units["Archer"] = &UnitDef{
		ID: 105, Key: "Archer", NameCN: "弓箭手", Kind: KindTroop,
		HP: 304, Damage: 116, HitSpeedMs: 900, LoadTimeMs: 400,
		SpeedMilliPerSec: SpeedMilliPerSec(60), RangeMilli: 5000, SightMilli: 5500,
		DeployMs: 1000, RadiusMilli: 500, Mass: 3, AtkAir: true, AtkGround: true,
		ProjectileKey: "ArcherArrow",
	}
	t.units["PrincessTower"] = &UnitDef{
		ID: 201, Key: "PrincessTower", NameCN: "公主塔", Kind: KindTower,
		HP: 3584, Damage: 128, HitSpeedMs: 800, LoadTimeMs: 0,
		SpeedMilliPerSec: 0, RangeMilli: 7500, SightMilli: 7500,
		DeployMs: 0, RadiusMilli: 1000, Mass: 0, AtkAir: true, AtkGround: true,
		ProjectileKey: "TowerPrincessProjectile",
	}
	t.units["KingTower"] = &UnitDef{
		ID: 202, Key: "KingTower", NameCN: "国王塔", Kind: KindTower,
		HP: 6144, Damage: 128, HitSpeedMs: 1000, LoadTimeMs: 500,
		SpeedMilliPerSec: 0, RangeMilli: 7000, SightMilli: 7000,
		DeployMs: 0, RadiusMilli: 1400, Mass: 0, AtkAir: true, AtkGround: true,
		ProjectileKey: "KingProjectile",
	}
	// Projectile rows: core reads only the flight speed from them (the damage
	// stays on the attacking unit). Every shot is homing,
	// which is the documented simplification.
	t.units["TowerPrincessProjectile"] = &UnitDef{
		ID: 301, Key: "TowerPrincessProjectile", Kind: KindProjectile,
		SpeedMilliPerSec: SpeedMilliPerSec(600),
	}
	t.units["KingProjectile"] = &UnitDef{
		ID: 302, Key: "KingProjectile", Kind: KindProjectile,
		SpeedMilliPerSec: SpeedMilliPerSec(1000),
	}
	t.units["ArcherArrow"] = &UnitDef{
		ID: 303, Key: "ArcherArrow", Kind: KindProjectile,
		SpeedMilliPerSec: SpeedMilliPerSec(900),
	}

	troop := func(id int32, key string, elixir int32) *CardDef {
		return &CardDef{ID: id, Key: key, NameCN: key, Kind: CardTypeTroop, Elixir: elixir, UnitKey: key, UnitN: 1}
	}
	t.cards[cardKnight] = troop(cardKnight, "Knight", 3)
	t.cards[cardGiant] = troop(cardGiant, "Giant", 5)
	t.cards[cardMinion] = troop(cardMinion, "Minion", 3)
	t.cards[cardSkeleton] = troop(cardSkeleton, "Skeleton", 1)
	t.cards[cardArcher] = troop(cardArcher, "Archer", 3)
	t.cards[cardGiantTwo] = troop(cardGiantTwo, "Giant", 5)
	t.cards[cardKnightCheap] = troop(cardKnightCheap, "Knight", 2)
	t.cards[cardKnightDear] = troop(cardKnightDear, "Knight", 4)
	t.cards[cardFireball] = &CardDef{
		ID: cardFireball, Key: "fireball", NameCN: "火球", Kind: CardTypeSpell, Elixir: 4,
		Spell: &SpellDef{ID: 1, Key: "fireball", Elixir: 4, RadiusMilli: 2500, InstantDamage: 688, Anywhere: true, OnWater: true},
	}
	t.cards[cardArrows] = &CardDef{
		ID: cardArrows, Key: "arrows", NameCN: "万箭齐发", Kind: CardTypeSpell, Elixir: 3,
		Spell: &SpellDef{ID: 2, Key: "arrows", Elixir: 3, RadiusMilli: 3500, InstantDamage: 122, Anywhere: true, OnWater: true},
	}
	return t
}

// mustBattle builds a battle or fails the test.
func mustBattle(t *testing.T, deckA, deckB []int32, seed int64) *Battle {
	t.Helper()
	b, err := NewBattle(Config{Seed: seed, Table: newTestTable(), DeckA: deckA, DeckB: deckB})
	if err != nil {
		t.Fatalf("NewBattle: %v", err)
	}
	return b
}

// towerOf finds a team's tower by kind, taking the left/right princess tower by
// its x position.
func towerOf(t *testing.T, b *Battle, team Team, kind int32, xMilli int32) *entity {
	t.Helper()
	for _, entry := range b.roster {
		if entry.team != team || entry.kind != kind {
			continue
		}
		if kind == TowerKindKing || entry.x == xMilli {
			return entry.ent
		}
	}
	t.Fatalf("no tower team=%v kind=%d x=%d", team, kind, xMilli)
	return nil
}

// setElixir forces a bar's amount, for tests that are not about elixir.
func setElixir(b *Battle, team Team, milli int32) {
	b.elixir[team].amountMilli = milli
	b.elixir[team].accNum = 0
}

// runSteps advances n ticks.
func runSteps(b *Battle, n int) {
	for i := 0; i < n; i++ {
		b.Step()
	}
}
