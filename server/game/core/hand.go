package core

// handSize is how many cards a player holds at once (参考规格 §7 S11: 8 卡组 /
// 4 手牌 / 下一张 / 循环).
const handSize = 4

// hand is the 8-card cycle behind one player's four visible cards.
//
// Classic Clash Royale rotation: a shuffled 8-card queue, the first four of
// which are the hand and the fifth of which is "next". Playing a card puts it at
// the **back** of the queue and hides its slot until the next-card cooldown
// expires; the next card then takes that slot -- which is why a full cycle puts
// the same four cards back in your hand in the order you played them.
type hand struct {
	// cards is the visible hand (handSize entries); a slot showing 0 is waiting
	// for its reveal (see pendingUntilMs).
	cards [handSize]int32
	// queue is the draw order; queue[0] is the "next card".
	queue []int32
	// pendingUntilMs[slot] is the battle clock at which that slot takes the queue
	// head; 0 = the slot is not waiting. A played card leaves its slot hidden for
	// the official next_spell_cooldown_ms (参考规格 §3 隐藏手牌（下一张）冷却,
	// 出处 battle_timelines.json Default.next_spell_cooldown_ms = [1000,500,350]),
	// which is why the visible hand can hold fewer than handSize playable cards
	// for that window.
	pendingUntilMs [handSize]int32
}

// newHand shuffles an 8-card deck deterministically with rng and deals the
// opening hand.
func newHand(deck []int32, rng *randSource) *hand {
	shuffled := make([]int32, len(deck))
	copy(shuffled, deck)
	rng.shuffleInt32(shuffled)

	h := &hand{}
	for i := 0; i < handSize; i++ {
		h.cards[i] = shuffled[i]
	}
	h.queue = make([]int32, 0, len(shuffled)-handSize)
	h.queue = append(h.queue, shuffled[handSize:]...)
	return h
}

// Hand returns a copy of the visible hand.
func (h *hand) Hand() []int32 {
	out := make([]int32, handSize)
	copy(out, h.cards[:])
	return out
}

// Next returns the next card to be drawn, or 0 if the queue is empty.
func (h *hand) Next() int32 {
	if len(h.queue) == 0 {
		return 0
	}
	return h.queue[0]
}

// Has reports whether a card id is currently in the hand.
func (h *hand) Has(cardID int32) bool {
	for _, c := range h.cards {
		if c == cardID {
			return true
		}
	}
	return false
}

// play removes a card from the hand, hides its slot for cooldownMs and puts the
// played card at the back of the queue. The slot takes the queue head when the
// cooldown expires (stepDraw) -- the rotation itself is unchanged, only the
// reveal is delayed. It returns false if the card is not in the hand.
func (h *hand) play(cardID int32, serverMs int32, cooldownMs int32) bool {
	slot := -1
	for i, c := range h.cards {
		if c == cardID {
			slot = i
			break
		}
	}
	if slot < 0 {
		return false
	}
	if len(h.queue) == 0 {
		// Cannot happen with an 8-card deck (4 hand + 4 queue, play returns
		// exactly one to the queue). Refuse rather than corrupt the rotation.
		return false
	}
	h.queue = append(h.queue, cardID)
	h.cards[slot] = 0
	if cooldownMs <= 0 {
		h.drawInto(slot)
		return true
	}
	h.pendingUntilMs[slot] = serverMs + cooldownMs
	return true
}

// stepDraw reveals every waiting slot whose cooldown has expired.
func (h *hand) stepDraw(serverMs int32) {
	for i := range h.pendingUntilMs {
		if h.pendingUntilMs[i] == 0 || serverMs < h.pendingUntilMs[i] {
			continue
		}
		h.drawInto(i)
	}
}

// drawInto takes the queue head into a waiting slot and clears its wait.
func (h *hand) drawInto(slot int) {
	if slot < 0 || slot >= handSize || len(h.queue) == 0 {
		return
	}
	h.cards[slot] = h.queue[0]
	h.queue = h.queue[1:]
	h.pendingUntilMs[slot] = 0
}
