package core

// AW1 judgement asset: the offline anim census that pins the `die` tier defect.
//
// WHY IT LIVES OUTSIDE `server/` AND NEEDS AN OVERLAY
// --------------------------------------------------
// `game/core` must stay a pure-Go, engine-free package, and a one-shot probe has
// no business sitting in the production tree (that is exactly the mess the AV1
// slice left behind with `server/game/core/zz_av1_probe_test.go`).  So the probe
// file lives here, under `tools/probes/`, and is injected into package core for
// the duration of one run with `go test -overlay`: nothing is ever written into
// `server/`.
//
// HOW TO RE-RUN (from the project root; seconds, no editor, no server)
// -------------------------------------------------------------------
//	# NOTE: -overlay must come BEFORE the package argument, or the injected
//	# file is silently ignored and go reports "no tests to run".
//	go -C server test -overlay <项目根>/tools/probes/AW1-anim-census/overlay.json \
//	    ./game/core/ -run TestAW1AnimCensus -count=1 -v
//
// It drives a whole 300 s AI-vs-AI match at the production cadence -- 20 Hz tick
// (MSecPerTick 50), AI decision every 10 ticks (logic.aiDecisionTicks), snapshot
// every 2 ticks (logic.snapshotEveryTicks) -- and counts which
// `EntitySnap.Anim` values the server actually puts on the wire, summed over
// every snapshot frame of that match:
//
//	fixed   = today's output (a casualty is held until one snapshot has carried
//	          its anim=Die frame, then compact() reaps it)
//	prefix  = the same run with every hp<=0 entity removed before counting
//	          == exactly what the server sent before the AW1 fix, because the
//	             pre-fix `compact()` reaped a casualty at the end of the tick it
//	             died while `Snapshot()` only iterates b.units
//
// Last measured (2026-09-22, seed 4242): 3000 snapshot frames, 104 deaths,
// fixed die=104, prefix die=0.
//
// The regression assertion that stays in the production tree is
// `TestSnapshotCarriesDeathFrame` (server/game/core/battle_test.go).

import (
	"fmt"
	"testing"
)

func TestAW1AnimCensus(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 4242)
	const ticks = (RegulationMs + OvertimeMs) / MSecPerTick // 6000

	var raw, aliveOnly [4]int
	frames, deaths, maxDeadPerFrame := 0, 0, 0

	for i := 0; i < ticks; i++ {
		b.Step()
		if i%10 == 0 {
			for _, team := range []Team{TeamBlue, TeamRed} {
				if id, x, y, ok := Decide(b, team); ok {
					_ = b.PlayCard(team, id, x, y)
				}
			}
		}
		for _, ev := range b.DrainEvents() {
			if ev.Kind == EvDeath {
				deaths++
			}
		}
		if i%2 != 0 {
			continue
		}
		s := b.Snapshot()
		frames++
		dead := 0
		for _, e := range s.Entities {
			if e.Anim < 0 || e.Anim >= 4 {
				t.Fatalf("tick %d: unknown anim=%d (id=%d)", i, e.Anim, e.ID)
			}
			raw[e.Anim]++
			if e.HP > 0 {
				aliveOnly[e.Anim]++
			} else {
				dead++
			}
		}
		if dead > maxDeadPerFrame {
			maxDeadPerFrame = dead
		}
	}

	line := fmt.Sprintf(
		"AW1_CENSUS frames=%d deaths=%d maxDeadPerFrame=%d fixed[idle,walk,attack,die]=%d,%d,%d,%d prefix[idle,walk,attack,die]=%d,%d,%d,%d",
		frames, deaths, maxDeadPerFrame,
		raw[0], raw[1], raw[2], raw[3],
		aliveOnly[0], aliveOnly[1], aliveOnly[2], aliveOnly[3])
	t.Log(line)
	fmt.Println(line)

	if deaths == 0 {
		t.Fatal("census ran a whole match with no death at all -- the probe proves nothing")
	}
	if raw[AnimDie] == 0 {
		t.Fatalf("no entity was ever sent with anim=AnimDie (%d) -- the death frame still never reaches the client", AnimDie)
	}
	if aliveOnly[AnimDie] != 0 {
		t.Fatalf("prefix emulation expects 0 death frames among hp>0 entities, got %d", aliveOnly[AnimDie])
	}
}
