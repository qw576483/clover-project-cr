package core

// Event kinds, matching Event.Kind (task doc §4.2).
const (
	EvPlayCard       int32 = 0
	EvSpawn          int32 = 1
	EvDeath          int32 = 2
	EvTowerDestroyed int32 = 3
	EvElixirFull     int32 = 4
	EvTowerActivated int32 = 5
)

// TowerSnap is one crown tower's state.
//
// It carries no coordinates on purpose: every tower stands on a fixed arena
// position (参考规格 §2), so the client reads those from the arena constants and
// only needs HP here.
type TowerSnap struct {
	ID    int32
	Kind  int32 // 0=公主塔 1=国王塔
	HP    int32
	MaxHP int32
	Alive bool
}

// EntitySnap is one troop or building.
//
// Kind is 0=部队 1=建筑 2=塔 (task doc §4.2). Crown towers are *also* reported
// in Snapshot.TowersA/TowersB; Entities carries troops and buildings, which is
// what keeps a renderer walking Entities from drawing every tower twice.
type EntitySnap struct {
	ID       int32
	Kind     int32
	CardID   int32
	Team     int32
	XMilli   int32
	YMilli   int32
	HP       int32
	MaxHP    int32
	Anim     int32 // 0=idle 1=walk 2=attack 3=die
	Facing   int32 // -1 / 1
	DeployMs int32 // remaining deploy time
}

// Snapshot is the full client-visible state of the match.
type Snapshot struct {
	Seq      int32
	ServerMs int32
	Phase    int32 // 0=normal 1=overtime 2=ended

	ElixirA, ElixirB int32
	CrownsA, CrownsB int32

	TowersA, TowersB []TowerSnap
	Entities         []EntitySnap

	HandA, HandB []int32
	NextA, NextB int32
}

// Event is one discrete, client-visible thing that happened.
type Event struct {
	Kind     int32 // see the Ev* constants
	CardID   int32
	XMilli   int32
	YMilli   int32
	EntityID int32
	Team     int32
	Text     string
}

// Snapshot renders the current state.
func (b *Battle) Snapshot() Snapshot {
	s := Snapshot{
		Seq:      b.seq,
		ServerMs: b.serverMs,
		Phase:    b.phase,
		ElixirA:  b.elixir[TeamBlue].amount(),
		ElixirB:  b.elixir[TeamRed].amount(),
		CrownsA:  b.crowns[TeamBlue],
		CrownsB:  b.crowns[TeamRed],
		HandA:    b.hands[TeamBlue].Hand(),
		HandB:    b.hands[TeamRed].Hand(),
		NextA:    b.hands[TeamBlue].Next(),
		NextB:    b.hands[TeamRed].Next(),
	}
	// Towers come from the roster so a destroyed tower still appears, with
	// Alive=false and HP 0.
	for _, t := range b.roster {
		snap := TowerSnap{Kind: t.kind, MaxHP: t.maxHP}
		if t.ent != nil {
			snap.ID = t.ent.ID
			snap.HP = t.ent.hp
			snap.Alive = t.ent.alive
		}
		if t.team == TeamBlue {
			s.TowersA = append(s.TowersA, snap)
		} else {
			s.TowersB = append(s.TowersB, snap)
		}
	}
	// Entities are the troops and buildings, in id order. Iterating a slice
	// (never a map) is what keeps two runs of the same seed identical.
	//
	// A casualty is still in b.units on the tick it died (compact holds it
	// until this function has carried its death frame), so it goes out here
	// once with anim=AnimDie + HP 0 -- that frame is the only way the client
	// ever learns how the unit died (AV1: without it the unit just vanished).
	// Marking it sent is what lets compact drop it on a later tick.
	for _, e := range b.units {
		s.Entities = append(s.Entities, entitySnapOf(e))
		if !e.alive {
			e.deathReported = true
		}
	}
	return s
}

// entitySnapOf renders one entity.
func entitySnapOf(e *entity) EntitySnap {
	return EntitySnap{
		ID:       e.ID,
		Kind:     int32(e.Kind),
		CardID:   e.CardID,
		Team:     int32(e.Team),
		XMilli:   e.xMilli,
		YMilli:   e.yMilli,
		HP:       e.hp,
		MaxHP:    e.maxHP,
		Anim:     e.anim,
		Facing:   e.facing,
		DeployMs: e.deployMsLeft,
	}
}

// DrainEvents returns the events accumulated since the previous drain and
// clears the buffer.
func (b *Battle) DrainEvents() []Event {
	if len(b.events) == 0 {
		return nil
	}
	out := make([]Event, len(b.events))
	copy(out, b.events)
	b.events = b.events[:0]
	return out
}
