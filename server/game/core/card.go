package core

import "strings"

// CardKind is the kind of a battle entity.
//
// Note the two different meanings in this file, both frozen by task doc §4.1:
//   - CardDef.Kind  : 0 = 部队 (troop), 1 = 法术 (spell), 2 = 建筑 (building)
//   - UnitDef.Kind  : 0 = 部队, 1 = 建筑, 2 = 塔
//   - EntitySnap.Kind (snapshot.go): 0 = 部队, 1 = 建筑, 2 = 塔
const (
	KindTroop      CardKind = 0
	KindBuilding   CardKind = 1
	KindTower      CardKind = 2
	KindProjectile CardKind = 3
)

// CardKind classifies a card or a battle entity.
type CardKind int32

// String is for diagnostics only.
func (k CardKind) String() string {
	switch k {
	case KindTroop:
		return "troop"
	case KindBuilding:
		return "building"
	case KindTower:
		return "tower"
	case KindProjectile:
		return "projectile"
	default:
		return "unknown"
	}
}

// Card-type ids used by CardDef.Kind (task doc §4.1).
const (
	CardTypeTroop    int32 = 0
	CardTypeSpell    int32 = 1
	CardTypeBuilding int32 = 2
)

// UnitDef is a battle entity's stats, read from the 战斗单位_cs table.
//
// Every number here comes from the table; core never hard-codes an HP, a
// damage, a speed, a range or a duration (task doc §7).
type UnitDef struct {
	ID     int32
	Key    string
	NameCN string
	Kind   CardKind

	HP, Damage, HitSpeedMs, LoadTimeMs int32
	// SpeedMilliPerSec is tiles-per-minute converted to milli-tiles/second
	// (units.go SpeedMilliPerSec).
	SpeedMilliPerSec int32
	// SpeedTilesPerMinute is the raw official `speed` (tiles/minute) the row was
	// generated from. It is kept next to SpeedMilliPerSec because that integer
	// conversion is lossy for speeds that are not multiples of 3 (550 -> 9166 ->
	// 549), and the client protocol carries the official value verbatim
	// (参考规格 §5 投射物).
	SpeedTilesPerMinute int32
	RangeMilli          int32
	SightMilli          int32
	DeployMs            int32
	RadiusMilli         int32
	Mass                int32

	AtkAir    bool
	AtkGround bool
	// OnlyBuildings / OnlyTowers / OnlyTroops are hard target filters, not
	// preferences: a unit that fails them cannot see the target at all
	// (参考规格 §5 索敌).
	OnlyBuildings bool
	OnlyTowers    bool
	OnlyTroops    bool

	ProjectileKey string

	// Death payload (亡语): spawn units and/or deal area damage on death.
	DeathSpawnKey       string
	DeathSpawnN         int32
	DeathDamage         int32
	DeathAoeRadiusMilli int32

	// Building lifetime and periodic spawner.
	LifeMs           int32
	SpawnKey         string
	SpawnN           int32
	SpawnRadiusMilli int32
	SpawnIntervalMs  int32
	SpawnLimit       int32

	SpriteDir string

	// Flying marks an air unit: it ignores terrain and water entirely and is
	// only attackable by units with AtkAir (参考规格 §5 移动 / 飞行).
	//
	// ⚠ CONTRACT NOTE: task doc §4.1's UnitDef field list has no flight flag,
	// yet §5 requires flight and §6 requires TestFlyingUnitIgnoresRiver. The
	// official data marks flight with `flying_height > 0`
	// (cards_stats_characters.json; 18 rows), which is not part of the frozen
	// column list either. This field is therefore a **backward-compatible
	// addition** (appending a field cannot break a named-field struct literal),
	// and isFlyingDef below falls back to the official flyer keys so the
	// 60-card pool behaves correctly even if the flag is never supplied.
	Flying bool
}

// officialFlyerKeys are the unit keys the official data marks as flying
// (`flying_height > 0`) among this project's 60-card pool, lower-cased for
// case-insensitive matching. Source: 原版资源/cr-api-data/docs/json/
// cards_stats_characters.json, rows Minion 1500 / Balloon 3000 / BabyDragon
// 3500 / LavaHound 4000 / LavaPups 3500 / MegaMinion 1500 / InfernoDragon 4000
// / Bat 2000 (plus rows outside the 60-card pool).
var officialFlyerKeys = map[string]bool{
	"minion":          true,
	"minions":         true,
	"minionhorde":     true,
	"minion-horde":    true,
	"balloon":         true,
	"babydragon":      true,
	"baby-dragon":     true,
	"lavahound":       true,
	"lava-hound":      true,
	"lavapups":        true,
	"mega-minion":     true,
	"megaminion":      true,
	"bat":             true,
	"bats":            true,
	"infernodragon":   true,
	"inferno-dragon":  true,
	"skeletonballoon": true,
}

// isFlyingDef reports whether a unit flies.
func isFlyingDef(def *UnitDef) bool {
	if def == nil {
		return false
	}
	if def.Flying {
		return true
	}
	return officialFlyerKeys[strings.ToLower(def.Key)]
}

// IsTower reports whether this def describes a crown tower.
func (u *UnitDef) IsTower() bool { return u != nil && u.Kind == KindTower }

// IsStructure reports whether this def is a building or a tower (the two kinds
// a building-targeting troop can see).
func (u *UnitDef) IsStructure() bool {
	return u != nil && (u.Kind == KindBuilding || u.Kind == KindTower)
}

// Attackable reports whether this def can attack at all (a positive range and
// at least one target layer).
func (u *UnitDef) Attackable() bool {
	return u != nil && u.Damage > 0 && u.RangeMilli > 0 && (u.AtkAir || u.AtkGround)
}

// SpellDef describes a spell card's payload (法术_cs).
type SpellDef struct {
	ID     int32
	Key    string
	NameCN string
	Elixir int32

	RadiusMilli      int32
	InstantDamage    int32
	DurationMs       int32
	TickMs           int32
	DamagePerTick    int32
	HealPerSecond    int32
	SpawnKey         string
	SpawnN           int32
	SpawnRadiusMilli int32
	Pushback         int32
	// Anywhere: may be cast on the enemy half (every spell may).
	Anywhere bool
	// OnWater: may be cast over the river.
	OnWater bool
}

// CardDef is one card of the 60-card pool (卡牌_cs).
type CardDef struct {
	ID     int32
	Key    string
	NameCN string
	// Kind: 0 = 部队, 1 = 法术, 2 = 建筑 (task doc §4.1).
	Kind int32
	// Rarity: 0 = 普通, 1 = 稀有, 2 = 史诗, 3 = 传说.
	Rarity int32
	Elixir int32
	// UnitKey is the battle entity this card summons (troop / building).
	UnitKey string
	// UnitN is how many bodies one card deploys (1 = single).
	UnitN int32
	// UnitRadiusMilli is the ring radius the bodies are spread over.
	UnitRadiusMilli int32
	DeployDelayMs   int32
	// Spell is non-nil only for spell cards.
	Spell *SpellDef
}

// CardTable is the read-only view of the generated tables that core needs.
//
// core defines this interface itself so it never imports any generated code
// (task doc §3): game/logic/ adapts the table output to it and hands it to
// NewBattle.
type CardTable interface {
	// Card returns the card with this id.
	Card(id int32) (*CardDef, bool)
	// Unit returns the battle entity with this key (troops, buildings and
	// towers all live in the same table).
	Unit(key string) (*UnitDef, bool)
}

// Tower table keys. The official data names the two crown towers
// `PrincessTower` / `KingTower` (cards_stats_building.json), which is what the
// 战斗单位_cs table is generated from (步骤文档 §4.2 lists 塔 as rows of that
// table, so Table.Unit must resolve them).
const (
	KeyPrincessTower = "PrincessTower"
	KeyKingTower     = "KingTower"
)

// towerKeyAliases is the fallback order used to resolve a crown tower's def.
//
// The table is generated from the official JSON, whose `name` field is
// CamelCase; the project's *card* keys are kebab-case (参考规格 §6.1). Rather
// than betting on one spelling, NewBattle tries the official name first and
// then the kebab/lower forms, and reports exactly what it tried if none hit.
var towerKeyAliases = map[string][]string{
	KeyPrincessTower: {KeyPrincessTower, "princess-tower", "princesstower"},
	KeyKingTower:     {KeyKingTower, "king-tower", "kingtower"},
}

// resolveTowerDef looks a crown tower's def up through the alias chain.
func resolveTowerDef(table CardTable, canonical string) (*UnitDef, bool) {
	for _, key := range towerKeyAliases[canonical] {
		if def, ok := table.Unit(key); ok && def != nil {
			return def, true
		}
	}
	return nil, false
}

// ProjectileOf reports a card's projectile: whether it attacks from range and how
// fast the shot flies.
//
// 参考规格 §5 投射物: a non-empty `projectile` means the card is ranged, and the
// ballistic speed is that projectile's own official `speed`. Projectiles are rows
// of their own in 战斗单位_cs (kind = 3), so both the key and the speed are read
// back through the table -- core never hard-codes either (project skill rule 5).
//
// The body that fights is CardDef.UnitKey (the group-card body), which is what
// actually carries the projectile, so the key is read off that unit. A card with
// no body unit (a spell) or a melee body returns ("", 0).
//
// speedTilesPerMinute is the raw official `speed` (tiles/minute), i.e. the value
// the client protocol needs; it is exact (SpeedMilliPerSec alone would round a
// 550-speed shot back to 549).
func ProjectileOf(table CardTable, card *CardDef) (key string, speedTilesPerMinute int32) {
	if table == nil || card == nil {
		return "", 0
	}
	body := card.UnitKey
	if body == "" {
		body = card.Key
	}
	u, ok := table.Unit(body)
	if !ok || u == nil || u.ProjectileKey == "" {
		return "", 0
	}
	if p, ok := table.Unit(u.ProjectileKey); ok && p != nil {
		return u.ProjectileKey, p.SpeedTilesPerMinute
	}
	// The projectile row is missing: the card is still ranged (the key is the
	// answer), it just has no speed to report.
	return u.ProjectileKey, 0
}
