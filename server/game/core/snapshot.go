package core

// Event kinds, matching Event.Kind.
const (
	EvPlayCard       int32 = 0
	EvSpawn          int32 = 1
	EvDeath          int32 = 2
	EvTowerDestroyed int32 = 3
	EvElixirFull     int32 = 4
	EvTowerActivated int32 = 5

	// EvTowerShoot = 塔开火。
	//
	// 为什么必须有这条事件：塔不在快照的 entities 里（见 TowerSnap 的注释），所以客户端
	// **拿不到"这一帧某座塔开火了"** —— 没有它，塔的弹道/枪口特效一点都播不出来。
	//
// 载荷（复用 EvSpawn 那套字段，**只新增一个 `ProjSpeed`**）：
//   EntityID = 开火那座塔的 id（与 TowerSnap.ID 同源，客户端据此对号到塔视图）
//   XMilli/YMilli = **塔根**坐标（塔位置是固定几何，客户端另有炮口层的世界坐标，见
//                   ArenaView.TryTowerMuzzle）
//   Team     = 开火方
//   ProjSpeed = 该塔投射物的速度（格/分钟；见 Event.ProjSpeed 的注释）
//
// ⛔ 载荷里**没有目标**：服务端知道目标，但为此再加一个字段要动 `BattleEvent`
// （协议体）与客户端两侧；当前只闭合"塔开火了"这一半（单位开火仍未闭合）。
// 客户端用最近一帧快照的"最近合法敌方"近似目标。
	EvTowerShoot int32 = 6
)

// Event is one discrete, one-shot client-visible occurrence.
type Event struct {
	Kind     int32 // see the Ev* constants
	CardID   int32
	XMilli   int32
	YMilli   int32
	EntityID int32
	Team     int32
	Text     string

	// ProjSpeed 只在 EvTowerShoot 上有意义：该塔投射物的速度，单位 = **格/分钟**
	// （与卡池下发的 CardInfo.proj_speed 同一口径 —— 两边都来自 unit 表的
	// `SpeedTilesPerMinute`，见 core.ProjectileOf）。
	//
	// 为什么由服务端算好下发：塔**不是卡**，客户端的卡池索引（`_cards`）里没有它们的
	// 投射物行 ⇒ 客户端自己查不到速度，就推不出飞行时长。⛔ 客户端不许为此写一个
	// 自造的常量速度。
	ProjSpeed int32
}

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
// Kind is 0=部队 1=建筑 2=塔. Crown towers are *also* reported
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
