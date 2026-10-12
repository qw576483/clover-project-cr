package core

// Victory conditions (参考规格 §3 胜负判定).
//
//  1. A destroyed king tower ends the match instantly: three crowns to the
//     attacker.
//  2. At 180 s (the end of regulation) the crowns are compared. If they differ
//     the match ends right there; if they are level it goes to overtime.
//  3. In overtime, the first tower destroyed wins outright (sudden death).
//  4. At 300 s (the end of overtime) the crowns are compared again; level
//     crowns fall to each side's remaining tower HP as a fraction of its own
//     maximum; still level is a genuine draw.

// Result reasons.
const (
	ReasonKingDestroyed = "king_destroyed"
	ReasonTimeUpCrowns  = "time_up_crowns"
	ReasonTimeUpHp      = "time_up_hp"
	ReasonSurrender     = "surrender"
	ReasonDraw          = "draw"
	// ReasonSuddenDeath is overtime's first tower kill: it ends the match with
	// the killer as the winner, before the clock runs out.
	ReasonSuddenDeath = "sudden_death"
)

// Result is the match outcome.
type Result struct {
	Ended            bool
	Winner           Team // valid only when Ended && !Draw
	Draw             bool
	CrownsA, CrownsB int32
	// Reason is one of the Reason* constants.
	Reason string
	// HpRateA / HpRateB are each side's remaining tower HP as ten-thousandths
	// of its own maximum.
	HpRateA, HpRateB int32
}

// finish ends the match with a winner.
func (b *Battle) finish(winner Team, reason string) {
	b.ended = true
	b.phase = PhaseEnded
	b.result = Result{
		Ended:   true,
		Winner:  winner,
		Draw:    false,
		CrownsA: b.crowns[TeamBlue],
		CrownsB: b.crowns[TeamRed],
		Reason:  reason,
		HpRateA: b.hpRate(TeamBlue),
		HpRateB: b.hpRate(TeamRed),
	}
	// The match is over: no snapshot follows, so the casualties compact() was
	// holding back for one death frame can go now (see compact).
	b.compact()
}

// finishDraw ends the match with no winner.
func (b *Battle) finishDraw(reason string) {
	b.ended = true
	b.phase = PhaseEnded
	b.result = Result{
		Ended:   true,
		Draw:    true,
		CrownsA: b.crowns[TeamBlue],
		CrownsB: b.crowns[TeamRed],
		Reason:  reason,
		HpRateA: b.hpRate(TeamBlue),
		HpRateB: b.hpRate(TeamRed),
	}
	b.compact() // as in finish(): no further snapshot can carry a death frame
}

// crownLeader returns whichever team has more crowns (ties return TeamBlue,
// which is only ever used where the caller has already checked they differ).
func (b *Battle) crownLeader() Team {
	if b.crowns[TeamRed] > b.crowns[TeamBlue] {
		return TeamRed
	}
	return TeamBlue
}

// scoredThisTick reports which side's crown count grew during the tick being
// resolved (see Battle.preTickCrowns).
func (b *Battle) scoredThisTick() (blueScored, redScored bool) {
	return b.crowns[TeamBlue] > b.preTickCrowns[TeamBlue],
		b.crowns[TeamRed] > b.preTickCrowns[TeamRed]
}

// checkVictory evaluates every end condition, in the order the rules rank.
func (b *Battle) checkVictory() {
	if b.ended {
		return
	}
	// (1) A destroyed king tower is an instant win in any period.
	for _, team := range []Team{TeamBlue, TeamRed} {
		k := b.kings[team]
		if k != nil && k.entity != nil && !k.entity.alive {
			b.finish(team.Other(), ReasonKingDestroyed)
			return
		}
	}
	// (2) The end of regulation (180 s), then sudden death during overtime.
	if b.serverMs >= RegulationMs && b.serverMs < RegulationMs+OvertimeMs {
		if b.phase == PhaseNormal {
			if b.crowns[TeamBlue] != b.crowns[TeamRed] {
				b.finish(b.crownLeader(), ReasonTimeUpCrowns)
				return
			}
			// Level crowns: play on into overtime. Elixir keeps advancing on the
			// same timeline, so the 3x segment starts at 240 s.
			b.phase = PhaseOvertime
			return
		}
		// Sudden death: the tick that destroys the first tower in overtime wins
		// outright, princess tower or king alike (a king death is already an
		// instant win above). It compares against the crowns as of the start of
		// the tick, so a tower that was already down when overtime began decides
		// nothing, and both sides scoring on the same tick is no sudden death.
		// 出处: cr-sim cr_sim/engine/battle.py:2722-2733 `_decide("sudden death")`.
		blueScored, redScored := b.scoredThisTick()
		if blueScored != redScored {
			if blueScored {
				b.finish(TeamBlue, ReasonSuddenDeath)
			} else {
				b.finish(TeamRed, ReasonSuddenDeath)
			}
		}
		return
	}
	// (3) The end of overtime (300 s).
	if b.serverMs >= RegulationMs+OvertimeMs {
		if b.crowns[TeamBlue] != b.crowns[TeamRed] {
			b.finish(b.crownLeader(), ReasonTimeUpCrowns)
			return
		}
		ra, rb := b.hpRate(TeamBlue), b.hpRate(TeamRed)
		switch {
		case ra > rb:
			b.finish(TeamBlue, ReasonTimeUpHp)
		case rb > ra:
			b.finish(TeamRed, ReasonTimeUpHp)
		default:
			b.finishDraw(ReasonDraw)
		}
	}
}
