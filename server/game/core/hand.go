package core

// handSize is how many cards a player holds at once (参考规格 §7 S11: 8 卡组 /
// 4 手牌 / 下一张 / 循环).
const handSize = 4

// hand is the 8-card cycle behind one player's four visible cards.
//
// Classic Clash Royale rotation: a shuffled 8-card queue, the first four of
// which are the hand and the fifth of which is "next". Playing a card pulls the
// next card into the vacated slot and puts the played card at the **back** of
// the queue -- which is why a full cycle puts the same four cards back in your
// hand in the order you played them.
type hand struct {
	// cards is the visible hand (handSize entries).
	cards [handSize]int32
	// queue is the draw order; queue[0] is the "next card".
	queue []int32
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

// play removes a card from the hand, draws the next into its slot and puts the
// played card at the back of the queue. It returns false if the card is not in
// the hand.
func (h *hand) play(cardID int32) bool {
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
	drawn := h.queue[0]
	h.queue = append(h.queue[1:], cardID)
	h.cards[slot] = drawn
	return true
}
