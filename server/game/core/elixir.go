package core

// elixirBar is one player's elixir, held in milli-elixir (1 elixir = 1000).
//
// Both the rate and the segment boundaries come out of the shared match
// timeline rather than being hard-coded per period: the 2x segment starts a
// minute *before* regulation ends and continues a minute into overtime, so
// "double elixir in overtime" would be wrong in both directions
// (cr-sim engine/elixir.py:1).
//
// The partial progress is carried in `accNum` -- a numerator expressed in
// "milliseconds x milli-elixir" -- so a rate change mid-regeneration keeps the
// fraction already earned instead of restarting it, and no float ever touches
// the bar.
type elixirBar struct {
	amountMilli int32
	accNum      int32
}

// newElixirBar creates a bar holding startMilli elixir.
func newElixirBar(startMilli int32) *elixirBar {
	return &elixirBar{amountMilli: ClampI32(startMilli, 0, MaxElixirMilli)}
}

// amount returns the bar's current milli-elixir, capped at MaxElixirMilli.
func (b *elixirBar) amount() int32 { return b.amountMilli }

// units returns whole elixir points available to spend.
func (b *elixirBar) units() int32 { return b.amountMilli / MilliTilePerTile }

// full reports whether the bar is at its cap.
func (b *elixirBar) full() bool { return b.amountMilli >= MaxElixirMilli }

// regenerate advances one tick of elixir at the rate in force at elapsedMs.
func (b *elixirBar) regenerate(elapsedMs int32) {
	msPerUnit := ElixirMsPerUnitAt(elapsedMs)
	if msPerUnit <= 0 {
		return
	}
	b.accNum += MSecPerTick * MilliTilePerTile
	gain := b.accNum / msPerUnit
	b.accNum %= msPerUnit
	if gain <= 0 {
		return
	}
	b.amountMilli += gain
	if b.amountMilli >= MaxElixirMilli {
		b.amountMilli = MaxElixirMilli
		// A full bar keeps no partial progress, exactly like the in-game bar.
		b.accNum = 0
	}
}

// canAfford reports whether the bar can pay cost whole elixir points.
func (b *elixirBar) canAfford(cost int32) bool { return b.units() >= cost }

// spend deducts cost whole elixir points. It returns false (and changes
// nothing) when the bar cannot afford it.
func (b *elixirBar) spend(cost int32) bool {
	if !b.canAfford(cost) {
		return false
	}
	b.amountMilli -= cost * MilliTilePerTile
	return true
}
