package core

import "testing"

// TestCardProjectile pins the ranged / ballistic-speed derivation
// (参考规格 §4 投射物: `projectile` 非空 ⇒ 远程，弹道速度 = `projectile.speed`).
//
// The numbers are the official ones (原版资源/cr-api-data/docs/json/
// cards_stats_projectile.json `speed`), which the table chain lands in
// 战斗单位_cs's projectile rows (kind = 3):
//
//	MusketeerProjectile 1000   ArcherArrow 600   AxeManProjectile 550
//
// The table here is built inline so the assertion is hermetic and touches no
// shared fixture.
func TestCardProjectile(t *testing.T) {
	tbl := &fakeTable{cards: map[int32]*CardDef{}, units: map[string]*UnitDef{}}

	// A troop that fights through a projectile (Musketeer, speed 60).
	tbl.units["Musketeer"] = &UnitDef{
		ID: 1, Key: "Musketeer", Kind: KindTroop,
		SpeedMilliPerSec: SpeedMilliPerSec(60), SpeedTilesPerMinute: 60,
		ProjectileKey: "MusketeerProjectile",
	}
	tbl.units["MusketeerProjectile"] = &UnitDef{
		ID: 2, Key: "MusketeerProjectile", Kind: KindProjectile,
		SpeedMilliPerSec: SpeedMilliPerSec(1000), SpeedTilesPerMinute: 1000,
	}
	// A projectile whose official speed is not a multiple of 3: the raw value
	// must survive unchanged (550 -> SpeedMilliPerSec 9166 -> back to 549).
	tbl.units["Executioner"] = &UnitDef{
		ID: 3, Key: "Executioner", Kind: KindTroop,
		SpeedMilliPerSec: SpeedMilliPerSec(60), SpeedTilesPerMinute: 60,
		ProjectileKey: "AxeManProjectile",
	}
	tbl.units["AxeManProjectile"] = &UnitDef{
		ID: 4, Key: "AxeManProjectile", Kind: KindProjectile,
		SpeedMilliPerSec: SpeedMilliPerSec(550), SpeedTilesPerMinute: 550,
	}
	// A group card: the bodies (Archer) are what fight, so the projectile comes
	// off the body unit, not off the card's "落点召唤" row.
	tbl.units["Archer"] = &UnitDef{
		ID: 5, Key: "Archer", Kind: KindTroop,
		SpeedMilliPerSec: SpeedMilliPerSec(60), SpeedTilesPerMinute: 60,
		ProjectileKey: "ArcherArrow",
	}
	tbl.units["ArcherArrow"] = &UnitDef{
		ID: 6, Key: "ArcherArrow", Kind: KindProjectile,
		SpeedMilliPerSec: SpeedMilliPerSec(600), SpeedTilesPerMinute: 600,
	}
	// A melee troop (Knight): no projectile key at all.
	tbl.units["Knight"] = &UnitDef{
		ID: 7, Key: "Knight", Kind: KindTroop,
		SpeedMilliPerSec: SpeedMilliPerSec(60), SpeedTilesPerMinute: 60,
	}

	tbl.cards[10] = &CardDef{ID: 10, Key: "musketeer", Kind: CardTypeTroop, UnitKey: "Musketeer", UnitN: 1}
	tbl.cards[11] = &CardDef{ID: 11, Key: "executioner", Kind: CardTypeTroop, UnitKey: "Executioner", UnitN: 1}
	tbl.cards[12] = &CardDef{ID: 12, Key: "archers", Kind: CardTypeTroop, UnitKey: "Archer", UnitN: 2}
	tbl.cards[13] = &CardDef{ID: 13, Key: "knight", Kind: CardTypeTroop, UnitKey: "Knight", UnitN: 1}
	tbl.cards[14] = &CardDef{ID: 14, Key: "fireball", Kind: CardTypeSpell, Spell: &SpellDef{ID: 1, Key: "fireball"}}

	cases := []struct {
		name    string
		cardID  int32
		wantKey string
		wantSpd int32
	}{
		{"ranged troop", 10, "MusketeerProjectile", 1000},
		{"non-multiple-of-3 speed is exact", 11, "AxeManProjectile", 550},
		{"group card uses its body's projectile", 12, "ArcherArrow", 600},
		{"melee troop", 13, "", 0},
		{"spell", 14, "", 0},
	}
	for _, c := range cases {
		card, ok := tbl.Card(c.cardID)
		if !ok {
			t.Fatalf("%s: card %d missing from table", c.name, c.cardID)
		}
		key, spd := ProjectileOf(tbl, card)
		if key != c.wantKey || spd != c.wantSpd {
			t.Errorf("%s: ProjectileOf = (%q,%d), want (%q,%d)", c.name, key, spd, c.wantKey, c.wantSpd)
		}
	}

	// Defensive: a nil card / nil table must not panic.
	if k, s := ProjectileOf(nil, nil); k != "" || s != 0 {
		t.Errorf("ProjectileOf(nil,nil) = (%q,%d), want (empty,0)", k, s)
	}
}
