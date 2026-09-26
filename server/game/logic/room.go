package logic

import (
	"encoding/json"
	"errors"
	"fmt"
	"sort"
	"strings"
	"sync"
	"time"

	"clover-cr/game/core"
	"clover-cr/game/def"
	"github.com/qw576483/clover-server-engine/pkg/domain/room"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"
	"github.com/qw576483/clover-server-engine/pkg/shared/proto"
	"github.com/qw576483/clover-server-engine/pkg/transport/event"
)

const (
	// maxRoomMembers 一个房间的座位数：2（1v1 对局）。
	maxRoomMembers = 2
	// lobbyWatcherName / lobbyWatcherInterval：大厅列表「变化即推 + 兜底自愈」的定时器。
	lobbyWatcherName     = "lobby-watch"
	lobbyWatcherInterval = 3 * time.Second
	// aiPlayerPrefix / aiNickname：AI 占座时用的假 ID 与昵称（客户端只渲染昵称）。
	aiPlayerPrefix = "ai:"
	aiNickname     = "训练师"
	aiRoomName     = "人机对战"
)

// ===========================================================================
// room.Kernel 的业务实现（引擎 room.Module 的插拔内核）
// ===========================================================================

// roomKernel 把引擎房间外壳的 8 个方法接到进程内注册表上。
// 房间的**业务元数据**（房名 / 房主 / 成员昵称与准备态 / ai_fill / 卡组 / 对局实例）
// 只有一份真身 —— G.rooms，内核只是它的转发，避免"外壳一份、业务一份"的状态漂移。
type roomKernel struct{}

// EnsureRoom 幂等：房间已存在直接返回 nil（重复建房同 ID 不报错）。
func (k *roomKernel) EnsureRoom(roomID string) error { return G.rooms.ensure(roomID) }

// Join 玩家进房（已在房内视为成功）。
func (k *roomKernel) Join(roomID, playerID string) error { return G.rooms.join(roomID, playerID) }

// Leave 玩家离房（不在房内视为成功）。
func (k *roomKernel) Leave(roomID, playerID string) error { return G.rooms.leave(roomID, playerID) }

// Destroy 销毁房间。
func (k *roomKernel) Destroy(roomID string) error { return G.rooms.destroy(roomID) }

// Players 返回房间内的真人玩家（外壳据此下发接管恢复包；AI 不算玩家）。
func (k *roomKernel) Players(roomID string) []string { return G.rooms.players(roomID) }

// Close 关闭内核（进程退出 / 房间子系统卸载时）。
func (k *roomKernel) Close() { logger.Infof("logic: 房间内核已关闭") }

// ExportState 导出可迁移态。
//
// 本项目不做跨节点房间迁移（单进程部署：server_type=all），所以只导出**大厅态**
// （房名 / 房主 / 成员 / 卡组 / ai_fill），对局实例不导出 —— 20 TPS 定点模拟的
// 完整状态没法用一段 JSON 无损搬运，硬搬只会得到一个"看起来搬过来了"的假房间。
// 导入后房间回到**未开打态**（成员与卡组保留），这一点在 ImportState 里也留了日志。
func (k *roomKernel) ExportState(roomID string) (room.ExportPack, error) {
	st, err := G.rooms.exportState(roomID)
	if err != nil {
		return room.ExportPack{}, err
	}
	raw, err := json.Marshal(st)
	if err != nil {
		logger.Errorf("logic: 导出房间态失败 room=%s: %v", roomID, err)
		return room.ExportPack{}, err
	}
	logger.Infof("logic: 导出房间态 room=%s（仅大厅态，对局实例不迁移）", roomID)
	return room.ExportPack{State: raw}, nil
}

// ImportState 导入可迁移态（房间不存在时先创建）。
func (k *roomKernel) ImportState(roomID string, state json.RawMessage) error {
	if err := G.rooms.importState(roomID, state); err != nil {
		logger.Errorf("logic: 导入房间态失败 room=%s: %v", roomID, err)
		return err
	}
	logger.Infof("logic: 导入房间态成功 room=%s（对局态不回填，房间回到未开打态）", roomID)
	return nil
}

// ===========================================================================
// 房间注册表（进程内唯一真身）
// ===========================================================================

// memberState 一个座位。座位号（切片下标）就是**队伍号**：0 = BLUE，1 = RED。
type memberState struct {
	PlayerID string
	Nickname string
	Ready    bool
	IsHost   bool
	IsAI     bool
	// Offline 对局进行中掉线的座位：不立刻摘掉（摘掉会让对手的队伍号错位，
	// 结算就会算错人），标记后由本局结算时统一清理（见 battle.go finishMatch）。
	Offline bool
}

// roomState 一个房间：大厅元数据 + 对局实例。
type roomState struct {
	id      string
	name    string
	host    string
	aiFill  bool
	started bool
	ended   bool
	members []*memberState
	decks   map[string][]int32
	// plays 本局逐人逐卡的出牌次数（真人出牌在 roomRegistry.playCard 里累加；
	// AI 不走这条路径 ⇒ 表里只有玩家自己出的牌）。开打时清空，结算时随战绩一起排队补记。
	plays map[string]map[int32]int32

	battle      *core.Battle
	lastSnap    core.Snapshot
	hasLastSnap bool
	seed        int64
	ticks       int64
	ticking     bool

	// entered 本局已报「进场」的座位号（客户端进战斗场景后发 MsgBattleSync）。
	// **只增不减**：中途掉线 / 重连都不清除 ⇒ 模拟一旦推进就不会退回等待态。
	entered map[int]bool
	// tickArmed 本局的模拟时钟是否已放行（entered 首次凑齐真人座位时置 true，只增不减）。
	tickArmed bool
}

// aiSeatLocked 返回 AI 的座位号（-1 = 本局没有 AI）。调用方须持锁。
func (r *roomState) aiSeatLocked() int {
	for i, m := range r.members {
		if m.IsAI {
			return i
		}
	}
	return -1
}

// allHumansEnteredLocked 每个真人座位是否都已报进场。调用方须持锁。
//
// 这是对局模拟的推进闸门：客户端进图（场景加载完 + HUD 打开）之前不推进，
// 否则服务端时钟先于玩家画面跑，对手（AI）会先出牌、先走动。
// AI 座位不需要进图；没有真人座位（不可能出现）时视为满足。
func (r *roomState) allHumansEnteredLocked() bool {
	for i, m := range r.members {
		if m.IsAI || r.entered[i] {
			continue
		}
		return false
	}
	return true
}

func (r *roomState) seatOf(playerID string) int {
	for i, m := range r.members {
		if m.PlayerID == playerID {
			return i
		}
	}
	return -1
}

// humanIDs 返回真人玩家（AI 不发推送）。
func (r *roomState) humanIDs() []string {
	out := make([]string, 0, len(r.members))
	for _, m := range r.members {
		if !m.IsAI {
			out = append(out, m.PlayerID)
		}
	}
	return out
}

// board 组装房间状态推送体。
func (r *roomState) board() def.RoomStateNotify {
	members := make([]def.RoomMember, 0, len(r.members))
	for _, m := range r.members {
		members = append(members, def.RoomMember{
			PlayerID: m.PlayerID,
			Nickname: m.Nickname,
			Ready:    m.Ready,
			IsHost:   m.IsHost,
			IsAI:     m.IsAI,
		})
	}
	return def.RoomStateNotify{
		RoomID:  r.id,
		Name:    r.name,
		Host:    r.host,
		AiFill:  r.aiFill,
		Started: r.started,
		Members: members,
	}
}

// info 组装房间列表的一行。
func (r *roomState) info() def.RoomInfo {
	return def.RoomInfo{
		RoomID:  r.id,
		Name:    r.name,
		Host:    r.host,
		Cur:     int32(len(r.members)),
		Max:     maxRoomMembers,
		AiFill:  r.aiFill,
		Started: r.started,
	}
}

// roomExport 是可迁移的大厅态。
type roomExport struct {
	RoomID  string             `json:"room_id"`
	Name    string             `json:"name"`
	Host    string             `json:"host"`
	AiFill  bool               `json:"ai_fill"`
	Started bool               `json:"started"`
	Members []def.RoomMember   `json:"members"`
	Decks   map[string][]int32 `json:"decks,omitempty"`
}

// matchResult 一局结算后待补记的战绩。
//
// 为什么要排队：结算发生在对局 tick 里（定时器 goroutine），那里没有 event.Ctx，而引擎的
// 数据改动统一走 LoadStruct + handler 返回后自动 Commit ⇒ 结算只能把结果存下来，
// 由玩家下一次发消息时在 loadPlayer 里补记（不丢、可查，代价是"晚一条消息落库"）。
type matchResult struct {
	// Win 本方是否获胜；Draw 为 true 时无意义。
	Win bool
	// Draw 平局（不改胜负场，但算打过一局 ⇒ 参赛场次 +1）。
	Draw bool
	// Crowns 本方王冠数（结算时的 CrownsA / CrownsB 按座位取，3 = 三冠）。
	Crowns int32
	// Plays 本局该玩家的逐卡出牌次数（空 = 本局一张都没出）。
	Plays map[int32]int32
}

// roomRegistry 是全部房间的进程内注册表。所有方法都在自己的锁内完成，
// 出锁后才做网络推送（推送放在锁内会把所有房间的操作串行到一条网络上）。
type roomRegistry struct {
	mu       sync.Mutex
	rooms    map[string]*roomState
	order    []string
	seq      int64
	version  int64
	watchers map[string]bool
	pushed   map[string]int64
	// pending 结算排队：pid → 待补记的局（一个玩家可能在一局还没落库前又打了一局，故按序累积）。
	pending map[string][]matchResult
}

func newRoomRegistry() *roomRegistry {
	return &roomRegistry{
		rooms:    make(map[string]*roomState),
		watchers: make(map[string]bool),
		pushed:   make(map[string]int64),
		pending:  make(map[string][]matchResult),
	}
}

// nextRoomID 生成房间 ID（时间前缀 + 进程内序号；单进程部署下唯一）。
func (r *roomRegistry) nextRoomID() string {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.seq++
	return fmt.Sprintf("r%d-%d", time.Now().UnixMilli(), r.seq)
}

// ensure 幂等建房（引擎 EnsureRoom 的语义）。
func (r *roomRegistry) ensure(roomID string) error {
	if strings.TrimSpace(roomID) == "" {
		return errors.New("房间 ID 不能为空")
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, ok := r.rooms[roomID]; ok {
		return nil // 幂等：已存在直接成功
	}
	r.rooms[roomID] = &roomState{id: roomID, name: roomID, decks: make(map[string][]int32)}
	r.order = append(r.order, roomID)
	r.version++
	return nil
}

// join 玩家进房（引擎 Kernel.Join 的语义：已在房内视为成功）。
func (r *roomRegistry) join(roomID, playerID string) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	if st.seatOf(playerID) >= 0 {
		return nil
	}
	if st.started {
		return fmt.Errorf("对局已开始")
	}
	if len(st.members) >= maxRoomMembers {
		return fmt.Errorf("房间已满（%d 个座位）", maxRoomMembers)
	}
	st.members = append(st.members, &memberState{PlayerID: playerID})
	r.version++
	return nil
}

// leave 玩家离房（不在房内视为成功）。房主离房 ⇒ 剩下的真人接任房主。
func (r *roomRegistry) leave(roomID, playerID string) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	i := st.seatOf(playerID)
	if i < 0 {
		return nil
	}
	r.removeMemberLocked(st, i)
	r.version++
	return nil
}

// removeMemberLocked 摘掉一个座位并处理房主移交（调用方须持锁）。
func (r *roomRegistry) removeMemberLocked(st *roomState, i int) {
	removed := st.members[i]
	st.members = append(st.members[:i:i], st.members[i+1:]...)
	delete(st.decks, removed.PlayerID)
	if removed.IsHost {
		st.host = ""
		for _, m := range st.members {
			if !m.IsAI {
				m.IsHost = true
				st.host = m.PlayerID
				break
			}
		}
	}
	// 座位号变了（= 队伍号变了）⇒ 准备态一律清零，避免"上位就绪"误开打。
	for _, m := range st.members {
		m.Ready = false
	}
}

// destroy 销毁房间（幂等）。
func (r *roomRegistry) destroy(roomID string) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, ok := r.rooms[roomID]; !ok {
		return nil
	}
	delete(r.rooms, roomID)
	for i, id := range r.order {
		if id == roomID {
			r.order = append(r.order[:i], r.order[i+1:]...)
			break
		}
	}
	r.version++
	return nil
}

// roomIDs 当前所有房间 ID 的快照（按创建顺序）。
func (r *roomRegistry) roomIDs() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]string, 0, len(r.order))
	return append(out, r.order...)
}

// players 房间内的真人玩家。
func (r *roomRegistry) players(roomID string) []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return nil
	}
	return st.humanIDs()
}

// configure 建房后的元数据（房名 + 房主标记 + 房主昵称）。
func (r *roomRegistry) configure(roomID, name, hostPID, nickname string) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	st.name = name
	st.host = hostPID
	if i := st.seatOf(hostPID); i >= 0 {
		st.members[i].IsHost = true
		st.members[i].Nickname = nickname
	}
	r.version++
	return nil
}

// bindMember 记录成员昵称与进房时的卡组快照。
func (r *roomRegistry) bindMember(roomID, playerID, nickname string, deck []int32) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	i := st.seatOf(playerID)
	if i < 0 {
		return fmt.Errorf("玩家不在房间内")
	}
	st.members[i].Nickname = nickname
	st.decks[playerID] = append([]int32(nil), deck...)
	r.version++
	return nil
}

// setDeck 覆盖某座位的卡组快照（房主开打前刷新自己刚存的卡组）。
func (r *roomRegistry) setDeck(roomID, playerID string, deck []int32) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	if st.seatOf(playerID) < 0 {
		return fmt.Errorf("玩家不在房间内")
	}
	st.decks[playerID] = append([]int32(nil), deck...)
	return nil
}

// setReady 准备 / 取消准备。
func (r *roomRegistry) setReady(roomID, playerID string, ready bool) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	i := st.seatOf(playerID)
	if i < 0 {
		return fmt.Errorf("玩家不在房间内")
	}
	if st.started {
		return fmt.Errorf("对局已开始")
	}
	st.members[i].Ready = ready
	r.version++
	return nil
}

// setAiFill 房主开关 AI 补位。
func (r *roomRegistry) setAiFill(roomID, playerID string, aiFill bool) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	if st.host != playerID {
		return fmt.Errorf("只有房主可以设置 AI 补位")
	}
	if st.started {
		return fmt.Errorf("对局已开始")
	}
	st.aiFill = aiFill
	r.version++
	return nil
}

// fillAI 单人房 + ai_fill ⇒ 补一个 AI 座位。返回是否补位。
func (r *roomRegistry) fillAI(roomID string, deck []int32) (bool, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return false, fmt.Errorf("房间不存在")
	}
	if !st.aiFill || st.started || len(st.members) >= maxRoomMembers {
		return false, nil
	}
	pid := aiPlayerPrefix + roomID
	st.members = append(st.members, &memberState{
		PlayerID: pid, Nickname: aiNickname, IsAI: true, Ready: true,
	})
	st.decks[pid] = append([]int32(nil), deck...)
	r.version++
	return true, nil
}

// checkStartable 开打前置校验（只查房间/座位状态，不查玩法规则）。
func (r *roomRegistry) checkStartable(roomID string) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	if st.started {
		return fmt.Errorf("对局已开始")
	}
	if len(st.members) != maxRoomMembers {
		return fmt.Errorf("人数不足：需要 %d 个座位（单人房请先开启 AI 补位）", maxRoomMembers)
	}
	for i, m := range st.members {
		if len(st.decks[m.PlayerID]) != deckSize {
			return fmt.Errorf("座位 %d（%s）还没有 %d 张卡组", i, m.Nickname, deckSize)
		}
		if m.IsAI || m.IsHost {
			continue
		}
		if !m.Ready {
			return fmt.Errorf("%s 还没有准备", m.Nickname)
		}
	}
	return nil
}

// roomIDOfPlayer 反查玩家当前所在房间；不在任何房间返回 "", false。
//
// 用途：保存卡组后同步刷新"房间里的座位卡组缓存"（见 deck.go onSaveDeck）。
// 房间的卡组缓存 `st.decks` 只在**加入那一刻**由 bindMember 写入，之后不跟随档案变化，
// 而 checkStartable 校验的是这份缓存 ⇒ 不刷新就会出现「先加入、后编辑卡组」这条最常见路径
// 下房主永远开不了打（实测见 deck.go 的注释）。
func (r *roomRegistry) roomIDOfPlayer(playerID string) (string, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	for id, st := range r.rooms {
		if st.seatOf(playerID) >= 0 {
			return id, true
		}
	}
	return "", false
}

// seatOf 玩家在房间里的座位号（= 队伍号），-1 表示不在房内。
func (r *roomRegistry) seatOf(roomID, playerID string) (int, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return -1, false
	}
	i := st.seatOf(playerID)
	return i, i >= 0
}

// hostOf 房间房主；ok=false 表示房间不存在。
func (r *roomRegistry) hostOf(roomID string) (string, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return "", false
	}
	return st.host, true
}

// started 房间是否已开打；ok=false 表示房间不存在。
func (r *roomRegistry) started(roomID string) (bool, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return false, false
	}
	return st.started, true
}

// aiFill 房间的 AI 补位开关；ok=false 表示房间不存在。
func (r *roomRegistry) aiFill(roomID string) (bool, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return false, false
	}
	return st.aiFill, true
}

// exportState 导出大厅态。
func (r *roomRegistry) exportState(roomID string) (roomExport, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return roomExport{}, fmt.Errorf("房间不存在")
	}
	board := st.board()
	decks := make(map[string][]int32, len(st.decks))
	for k, v := range st.decks {
		decks[k] = append([]int32(nil), v...)
	}
	return roomExport{
		RoomID: st.id, Name: st.name, Host: st.host, AiFill: st.aiFill,
		Started: st.started, Members: board.Members, Decks: decks,
	}, nil
}

// importState 导入大厅态（房间不存在则创建；对局实例不回填）。
func (r *roomRegistry) importState(roomID string, state json.RawMessage) error {
	var ex roomExport
	if len(state) > 0 {
		if err := json.Unmarshal(state, &ex); err != nil {
			return fmt.Errorf("房间态解析失败: %w", err)
		}
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		st = &roomState{id: roomID, name: roomID, decks: make(map[string][]int32)}
		r.rooms[roomID] = st
		r.order = append(r.order, roomID)
	}
	if ex.Name != "" {
		st.name = ex.Name
	}
	st.host = ex.Host
	st.aiFill = ex.AiFill
	st.started = false // 对局态不迁移
	st.ended = false
	st.battle = nil
	st.members = st.members[:0]
	for _, m := range ex.Members {
		st.members = append(st.members, &memberState{
			PlayerID: m.PlayerID, Nickname: m.Nickname, Ready: false,
			IsHost: m.IsHost, IsAI: m.IsAI,
		})
	}
	for pid, d := range ex.Decks {
		st.decks[pid] = append([]int32(nil), d...)
	}
	r.version++
	return nil
}

// takePendingResults 取走全部待补记的战绩（取走后本 map 项清空）。
func (r *roomRegistry) takePendingResults(playerID string) []matchResult {
	r.mu.Lock()
	defer r.mu.Unlock()
	list := r.pending[playerID]
	if len(list) > 0 {
		delete(r.pending, playerID)
	}
	return list
}

// pushResult 结算时排队待补记的战绩（平局也入队：它不改胜负场，但要算进参赛场次）。
func (r *roomRegistry) pushResult(playerID string, res matchResult) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.pending[playerID] = append(r.pending[playerID], res)
}

// playCard 出牌的统一入口（真人 / AI 都走这里，最终都落到 core 的同一个 PlayCard）。
func (r *roomRegistry) playCard(roomID, playerID string, cardID, xMilli, yMilli int32) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return fmt.Errorf("房间不存在")
	}
	seat := st.seatOf(playerID)
	if seat < 0 {
		return fmt.Errorf("你不在这个房间里")
	}
	if st.battle == nil {
		return fmt.Errorf("对局未进行")
	}
	if st.ended {
		return fmt.Errorf("对局已结束")
	}
	if err := st.battle.PlayCard(core.Team(seat), cardID, xMilli, yMilli); err != nil {
		return err
	}
	// 出牌成功才计数（被拒的牌不算"用过"）。AI 不走这里 ⇒ 统计的只是玩家自己出的牌。
	if st.plays == nil {
		st.plays = make(map[string]map[int32]int32)
	}
	if st.plays[playerID] == nil {
		st.plays[playerID] = make(map[int32]int32)
	}
	st.plays[playerID][cardID]++
	return nil
}

// surrender 玩家投降。
func (r *roomRegistry) surrender(roomID, playerID string) (bool, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return false, fmt.Errorf("房间不存在")
	}
	seat := st.seatOf(playerID)
	if seat < 0 {
		return false, fmt.Errorf("你不在这个房间里")
	}
	if st.battle == nil || st.ended {
		return false, fmt.Errorf("对局未进行")
	}
	st.battle.Surrender(core.Team(seat))
	return true, nil
}

// snapshotFor 取该玩家可见的快照（对局进行中取实时，结束后取最后一帧）。
func (r *roomRegistry) snapshotFor(roomID, playerID string) (core.Snapshot, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil || st.seatOf(playerID) < 0 {
		return core.Snapshot{}, false
	}
	if st.battle != nil {
		return st.battle.Snapshot(), true
	}
	if st.hasLastSnap {
		return st.lastSnap, true
	}
	return core.Snapshot{}, false
}

// markEntered 记录某玩家已进对局场景（MsgBattleSync 的副作用）。返回是否**首次**记录该座位。
// 房间 / 座位不存在、或对局未进行时返回 false（只影响闸门，不影响 BattleSync 本身的回包语义）。
func (r *roomRegistry) markEntered(roomID, playerID string) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil || st.battle == nil || st.ended {
		return false
	}
	seat := st.seatOf(playerID)
	if seat < 0 {
		return false
	}
	if st.entered == nil {
		st.entered = make(map[int]bool, maxRoomMembers)
	}
	if st.entered[seat] {
		return false
	}
	st.entered[seat] = true
	return true
}

// surrenderOnDisconnect 掉线判负：对局进行中把该座位判负，返回是否生效。
func (r *roomRegistry) surrenderOnDisconnect(roomID string, seat int) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil || st.battle == nil || st.ended || seat < 0 || seat >= len(st.members) {
		return false
	}
	st.battle.Surrender(core.Team(seat))
	return true
}

// detach 断线清理：把玩家从房间摘掉；对局进行中只标记离线（保住座位号 = 队伍号）。
func (r *roomRegistry) detach(playerID string) (roomID string, seat int, running bool, ok bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	for _, id := range r.order {
		st := r.rooms[id]
		if st == nil {
			continue
		}
		i := st.seatOf(playerID)
		if i < 0 {
			continue
		}
		running = st.battle != nil && !st.ended
		if running {
			st.members[i].Offline = true
			st.members[i].Ready = false
			seat = i
			return id, seat, true, true
		}
		r.removeMemberLocked(st, i)
		r.version++
		return id, i, false, true
	}
	return "", -1, false, false
}

// dropOffline 清掉本局掉线的座位（在对局结算后调用）。返回是否已无人。
func (r *roomRegistry) dropOffline(roomID string) (empty bool, humans []string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return true, nil
	}
	kept := st.members[:0:0]
	for _, m := range st.members {
		if m.Offline {
			delete(st.decks, m.PlayerID)
			continue
		}
		kept = append(kept, m)
	}
	st.members = kept
	if st.host == "" {
		for _, m := range st.members {
			if !m.IsAI {
				m.IsHost = true
				st.host = m.PlayerID
				break
			}
		}
	}
	r.version++
	return len(st.humanIDs()) == 0, st.humanIDs()
}

// tidy 房间是否需要回收：没有成员，或只剩 AI 座位。
func (r *roomRegistry) tidy(roomID string) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return false
	}
	return len(st.humanIDs()) == 0
}

// watch 登记大厅观察者（房间列表变化时即时推送）；dropWatcher 注销。
func (r *roomRegistry) watch(playerID string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.watchers[playerID] = true
}

func (r *roomRegistry) dropWatcher(playerID string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	delete(r.watchers, playerID)
	delete(r.pushed, playerID)
}

// buildListLocked 组装房间列表（调用方须持锁）。
func (r *roomRegistry) buildListLocked() def.RoomListNotify {
	rooms := make([]def.RoomInfo, 0, len(r.order))
	for _, id := range r.order {
		if st := r.rooms[id]; st != nil {
			rooms = append(rooms, st.info())
		}
	}
	return def.RoomListNotify{Rooms: rooms}
}

// listNotify 组装房间列表 + 记录已推版本（返回要推给哪些观察者）。
func (r *roomRegistry) listNotify() (def.RoomListNotify, []string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	notify := r.buildListLocked()
	targets := make([]string, 0, len(r.watchers))
	for pid := range r.watchers {
		targets = append(targets, pid)
		r.pushed[pid] = r.version
	}
	sort.Strings(targets) // 排序只为日志与输出稳定
	return notify, targets
}

// flushLobby 兜底：观察者错过的房间列表变化，由定时器补推一次。
func (r *roomRegistry) flushLobby(playerID string) (def.RoomListNotify, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if !r.watchers[playerID] || r.pushed[playerID] >= r.version {
		return def.RoomListNotify{}, false
	}
	notify := r.buildListLocked()
	r.pushed[playerID] = r.version
	return notify, true
}

// stateNotify 组装房间状态推送体 + 推送目标。
func (r *roomRegistry) stateNotify(roomID string) (def.RoomStateNotify, []string, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	st := r.rooms[roomID]
	if st == nil {
		return def.RoomStateNotify{}, nil, false
	}
	return st.board(), st.humanIDs(), true
}

// ===========================================================================
// 推送（全部在锁外做网络 I/O）
// ===========================================================================

// pushRoomState 把房间成员/准备状态推给房内所有真人。
func (l *gameLogic) pushRoomState(roomID string) {
	notify, targets, ok := l.rooms.stateNotify(roomID)
	if !ok {
		return
	}
	for _, pid := range targets {
		// SelfPlayerID 是逐接收者的：同一个房间态推给不同玩家，「我是谁」不同。
		// notify 是结构体值，这里改的是本轮迭代的副本，不会污染其它目标。
		notify.SelfPlayerID = pid
		if err := l.g.PushToPlayer(pushTarget(pid), def.PushRoomState, notify); err != nil {
			logger.Warnf("logic: 推送房间状态失败 room=%s player=%s: %v", roomID, pid, err)
		}
	}
}

// pushRoomList 把最新房间列表推给所有大厅观察者。
func (l *gameLogic) pushRoomList() {
	notify, targets := l.rooms.listNotify()
	for _, pid := range targets {
		if err := l.g.PushToPlayer(pushTarget(pid), def.PushRoomList, notify); err != nil {
			logger.Warnf("logic: 推送房间列表失败 player=%s: %v", pid, err)
		}
	}
}

// ensureLobbyWatcher 为这条连接挂一个「大厅列表兜底推送」定时器。
//
// ★ 定时器 scope 的选取是本项目的一条硬约束（依据 = 引擎 StopTimerGroup 的语义）：
//   - 随掉线自动清理的任务 ⇒ scope 必须**等于连接级 owner**（这里就是 c.Account()，
//     引擎在硬掉线时调 StopTimerGroup(owner)，恰好把这条 watcher 一起清掉）；
//   - 不能随掉线停的任务（对局 tick：对手还要接着打）⇒ 用带前缀的独立 scope
//     "battle:"+roomID（见 battle.go），显式 StopTimerGroup 收尾。
//
// 两者的 scope 不同，所以掉线绝不会误伤正在跑的对局。
func (l *gameLogic) ensureLobbyWatcher(c event.Ctx) {
	owner := c.Account()
	if owner == "" {
		logger.Warnf("logic: 空 owner，跳过大厅观察者定时器（player=%q）", c.PlayerID())
		return
	}
	l.g.Timer.TimerGroup(owner).Every(lobbyWatcherName, lobbyWatcherInterval, func() {
		pid := playerIDPrefix + owner
		if notify, ok := l.rooms.flushLobby(pid); ok {
			if err := l.g.PushToPlayer(pushTarget(pid), def.PushRoomList, notify); err != nil {
				logger.Warnf("logic: 大厅列表兜底推送失败 player=%s: %v", pid, err)
			}
		}
	})
}

// ===========================================================================
// handlers
// ===========================================================================

// onRoomCreate 建房（房主自动占 0 号座位）。
func (l *gameLogic) onRoomCreate(c event.Ctx) error {
	var req def.RoomCreateReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: RoomCreate 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.RoomCreateReply{Err: err.Error()})
		return nil
	}
	// 一个人只能在一间房里：先退掉旧房，避免出现"幽灵占座"。
	l.leaveRoomsOf(pid)

	id := l.rooms.nextRoomID()
	if err := l.roomMod.EnsureRoom(id); err != nil {
		logger.Errorf("logic: 建房失败（EnsureRoom）room=%s player=%s: %v", id, pid, err)
		l.g.Reply(c, def.RoomCreateReply{Err: "建房失败：" + err.Error()})
		return nil
	}
	if _, switched, joinErr := l.roomMod.JoinRoom(c.ConnID(), id, pid); joinErr != nil {
		logger.Errorf("logic: 房主进房失败 room=%s player=%s: %v", id, pid, joinErr)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.RoomCreateReply{Err: "进房失败：" + joinErr.Error()})
		return nil
	} else if switched {
		// 单进程部署下不应发生；发生即部署异常，必须留痕（否则客户端会停在"建了房但进不去"）。
		logger.Warnf("logic: 房主连接被迁移到其它节点 room=%s player=%s", id, pid)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.RoomCreateReply{Err: "房间被路由到其它节点"})
		return nil
	}

	name := strings.TrimSpace(req.Name)
	if name == "" {
		if strings.TrimSpace(p.Nickname) != "" {
			name = p.Nickname + "的房间"
		} else {
			name = "未命名房间"
		}
	}
	if err := l.rooms.configure(id, name, pid, p.Nickname); err != nil {
		logger.Errorf("logic: 建房元数据失败 room=%s: %v", id, err)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.RoomCreateReply{Err: err.Error()})
		return nil
	}
	if len(p.Deck) == deckSize {
		if err := l.rooms.setDeck(id, pid, p.Deck); err != nil {
			logger.Warnf("logic: 记录房主卡组失败 room=%s player=%s: %v", id, pid, err)
		}
	}

	logger.Infof("logic: 建房成功 room=%s name=%q host=%s", id, name, pid)
	l.g.Reply(c, def.RoomCreateReply{OK: true, RoomID: id})
	l.pushRoomState(id)
	l.pushRoomList()
	return nil
}

// onRoomList 拉房间列表（成为大厅观察者，列表变化即时推送）。
func (l *gameLogic) onRoomList(c event.Ctx) error {
	var req def.RoomListReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: RoomList 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	_, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.RoomListReply{})
		return nil
	}
	l.rooms.watch(pid)
	l.ensureLobbyWatcher(c)
	notify, _ := l.rooms.listNotify()
	logger.Infof("logic: 下发房间列表 player=%s rooms=%d", pid, len(notify.Rooms))
	l.g.Reply(c, def.RoomListReply{Rooms: notify.Rooms})
	return nil
}

// onRoomJoin 加入房间。
func (l *gameLogic) onRoomJoin(c event.Ctx) error {
	var req def.RoomJoinReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: RoomJoin 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.RoomJoinReply{Err: err.Error()})
		return nil
	}
	// 先给可读的拒绝理由（真正的并发安全校验在 registry.join 里再做一次）。
	aiFill, ok := l.rooms.aiFill(req.RoomID)
	if !ok {
		logger.Warnf("logic: 加入不存在的房间 room=%s player=%s", req.RoomID, pid)
		l.g.Reply(c, def.RoomJoinReply{Err: "房间不存在"})
		return nil
	}
	if started, _ := l.rooms.started(req.RoomID); started {
		logger.Warnf("logic: 房间已开打，拒绝加入 room=%s player=%s", req.RoomID, pid)
		l.g.Reply(c, def.RoomJoinReply{Err: "对局已开始"})
		return nil
	}
	l.leaveRoomsOf(pid)

	if _, switched, joinErr := l.roomMod.JoinRoom(c.ConnID(), req.RoomID, pid); joinErr != nil {
		logger.Warnf("logic: 加入房间失败 room=%s player=%s: %v", req.RoomID, pid, joinErr)
		l.g.Reply(c, def.RoomJoinReply{Err: joinErr.Error()})
		return nil
	} else if switched {
		logger.Warnf("logic: 玩家连接被迁移到其它节点 room=%s player=%s", req.RoomID, pid)
		l.g.Reply(c, def.RoomJoinReply{Err: "房间被路由到其它节点"})
		return nil
	}
	if err := l.rooms.bindMember(req.RoomID, pid, p.Nickname, p.Deck); err != nil {
		logger.Warnf("logic: 记录房间成员失败 room=%s player=%s: %v", req.RoomID, pid, err)
		l.g.Reply(c, def.RoomJoinReply{Err: err.Error()})
		return nil
	}

	logger.Infof("logic: 加入房间成功 room=%s player=%s nickname=%q deck=%d", req.RoomID, pid, p.Nickname, len(p.Deck))
	l.g.Reply(c, def.RoomJoinReply{OK: true, RoomID: req.RoomID, AiFill: aiFill})
	l.pushRoomState(req.RoomID)
	l.pushRoomList()
	return nil
}

// onRoomLeave 离开房间。
func (l *gameLogic) onRoomLeave(c event.Ctx) error {
	var req def.RoomLeaveReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: RoomLeave 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	_, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.RoomLeaveReply{OK: false})
		return nil
	}
	// 对局进行中不许直接退房（退房会让队伍号错位）—— 先投降，结算后房间自动回到未开打态。
	if started, ok := l.rooms.started(req.RoomID); !ok {
		logger.Warnf("logic: 离开不存在的房间 room=%s player=%s", req.RoomID, pid)
		l.g.Reply(c, def.RoomLeaveReply{OK: false})
		return nil
	} else if started {
		logger.Warnf("logic: 对局进行中拒绝退房 room=%s player=%s（请先投降）", req.RoomID, pid)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "提示", Content: "对局进行中，请先投降再退房", Level: "warning"})
		l.g.Reply(c, def.RoomLeaveReply{OK: false})
		return nil
	}
	if err := l.roomMod.LeaveRoom(req.RoomID, pid); err != nil {
		logger.Warnf("logic: 退房失败 room=%s player=%s: %v", req.RoomID, pid, err)
		l.g.Reply(c, def.RoomLeaveReply{OK: false})
		return nil
	}
	logger.Infof("logic: 退房成功 room=%s player=%s", req.RoomID, pid)
	l.g.Reply(c, def.RoomLeaveReply{OK: true})
	l.tidyRoom(req.RoomID)
	l.pushRoomState(req.RoomID)
	l.pushRoomList()
	return nil
}

// onRoomReady 准备 / 取消准备。
func (l *gameLogic) onRoomReady(c event.Ctx) error {
	var req def.RoomReadyReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: RoomReady 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.RoomReadyReply{OK: false})
		return nil
	}
	if err := l.rooms.setReady(req.RoomID, pid, req.Ready); err != nil {
		logger.Warnf("logic: 准备失败 room=%s player=%s ready=%v: %v", req.RoomID, pid, req.Ready, err)
		l.g.Reply(c, def.RoomReadyReply{OK: false})
		return nil
	}
	// 准备的同时刷新卡组快照（开打时用的是这份快照）。
	if len(p.Deck) == deckSize {
		if err := l.rooms.setDeck(req.RoomID, pid, p.Deck); err != nil {
			logger.Warnf("logic: 刷新卡组快照失败 room=%s player=%s: %v", req.RoomID, pid, err)
		}
	}
	logger.Infof("logic: 准备状态更新 room=%s player=%s ready=%v", req.RoomID, pid, req.Ready)
	l.g.Reply(c, def.RoomReadyReply{OK: true})
	l.pushRoomState(req.RoomID)
	l.pushRoomList()
	return nil
}

// onRoomSetAi 房主开关 AI 补位。
func (l *gameLogic) onRoomSetAi(c event.Ctx) error {
	var req def.RoomSetAiReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: RoomSetAi 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	_, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.RoomSetAiReply{OK: false})
		return nil
	}
	if err := l.rooms.setAiFill(req.RoomID, pid, req.AiFill); err != nil {
		logger.Warnf("logic: 设置 AI 补位失败 room=%s player=%s ai_fill=%v: %v", req.RoomID, pid, req.AiFill, err)
		l.g.Reply(c, def.RoomSetAiReply{OK: false})
		return nil
	}
	logger.Infof("logic: AI 补位已%s room=%s host=%s", map[bool]string{true: "开启", false: "关闭"}[req.AiFill], req.RoomID, pid)
	l.g.Reply(c, def.RoomSetAiReply{OK: true})
	l.pushRoomState(req.RoomID)
	l.pushRoomList()
	return nil
}

// onRoomStart 房主开打。
func (l *gameLogic) onRoomStart(c event.Ctx) error {
	var req def.RoomStartReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: RoomStart 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.RoomStartReply{Err: err.Error()})
		return nil
	}
	host, ok := l.rooms.hostOf(req.RoomID)
	if !ok {
		logger.Warnf("logic: 开打失败：房间不存在 room=%s player=%s", req.RoomID, pid)
		l.g.Reply(c, def.RoomStartReply{Err: "房间不存在"})
		return nil
	}
	if host != pid {
		logger.Warnf("logic: 非房主开打被拒 room=%s player=%s host=%s", req.RoomID, pid, host)
		l.g.Reply(c, def.RoomStartReply{Err: "只有房主可以开打"})
		return nil
	}
	if started, _ := l.rooms.started(req.RoomID); started {
		logger.Warnf("logic: 重复开打被拒 room=%s player=%s", req.RoomID, pid)
		l.g.Reply(c, def.RoomStartReply{Err: "对局已开始"})
		return nil
	}
	// 房主卡组用"此刻最新的"（客户可能刚在卡组页改过）。
	if len(p.Deck) == deckSize {
		if err := l.rooms.setDeck(req.RoomID, pid, p.Deck); err != nil {
			logger.Warnf("logic: 刷新房主卡组失败 room=%s player=%s: %v", req.RoomID, pid, err)
		}
	}
	// 单人房 + ai_fill ⇒ 由 AI 顶上 1 号座位。
	if filled, ferr := l.rooms.fillAI(req.RoomID, l.aiDeck()); ferr != nil {
		logger.Warnf("logic: AI 补位失败 room=%s: %v", req.RoomID, ferr)
		l.g.Reply(c, def.RoomStartReply{Err: ferr.Error()})
		return nil
	} else if filled {
		logger.Infof("logic: 单人房已由 AI 补位 room=%s deck=%v", req.RoomID, l.aiDeck())
	}
	if err := l.rooms.checkStartable(req.RoomID); err != nil {
		logger.Warnf("logic: 开打前置校验失败 room=%s player=%s: %v", req.RoomID, pid, err)
		l.g.Reply(c, def.RoomStartReply{Err: err.Error()})
		return nil
	}
	if err := l.startMatch(req.RoomID); err != nil {
		logger.Errorf("logic: 开打失败 room=%s player=%s: %v", req.RoomID, pid, err)
		l.g.Reply(c, def.RoomStartReply{Err: err.Error()})
		return nil
	}
	l.g.Reply(c, def.RoomStartReply{OK: true})
	return nil
}

// leaveRoomsOf 让玩家先退出它所在的其它房间（一人只在一间房）。
//
// ⛔ 旧房**还在跑对局**时不许"跳过搬座位"就完事（那是跨房间快照污染的根因）：
// 旧房不结束 ⇒ 它的 tick 继续跑、继续给**同一个 player** 推快照，而旧房的 seq
// 比新房大 ⇒ 客户端按 seq 判"倒退"把新房帧全丢掉 ⇒ HUD 停在旧房数值。
// 所以先把旧房那一局就地结束（判该玩家负，等同掉线判负的路径），
// 再由对局 tick 在 50 ms 内推结算、停 tick（`stopBattleTimer`）并回收房间。
func (l *gameLogic) leaveRoomsOf(playerID string) {
	for _, id := range l.rooms.roomIDs() {
		if seat, in := l.rooms.seatOf(id, playerID); in && seat >= 0 {
			if started, _ := l.rooms.started(id); started {
				if l.rooms.surrenderOnDisconnect(id, seat) {
					logger.Warnf("logic: 旧房对局仍在进行，玩家已开新局 ⇒ 判其负并结束本局 room=%s player=%s seat=%d",
						id, playerID, seat)
				} else {
					// 非预期分支：房内标记"已开打"却没有可结束的对局实例，留痕别静默。
					logger.Warnf("logic: 旧房标记为对局中但没有可结束的对局实例 room=%s player=%s seat=%d",
						id, playerID, seat)
				}
			}
			if err := l.roomMod.LeaveRoom(id, playerID); err != nil {
				logger.Warnf("logic: 清理旧房失败 room=%s player=%s: %v", id, playerID, err)
				continue
			}
			logger.Infof("logic: 已退出旧房 room=%s player=%s", id, playerID)
			l.tidyRoom(id)
			l.pushRoomState(id)
		}
	}
}

// tidyRoom 回收空房（没有真人座位）并停掉它的对局 tick。
func (l *gameLogic) tidyRoom(roomID string) {
	if !l.rooms.tidy(roomID) {
		return
	}
	if err := l.roomMod.DestroyRoom(roomID); err != nil {
		logger.Warnf("logic: 销毁房间失败 room=%s: %v", roomID, err)
	}
	l.rooms.destroy(roomID)
	l.stopBattleTimer(roomID)
	logger.Infof("logic: 房间已回收 room=%s", roomID)
}

// onDisconnect 断线清理（引擎已先按 owner 清掉「随连接走」的定时器）。
func (l *gameLogic) onDisconnect(owner string) {
	pid := playerIDPrefix + owner
	l.rooms.dropWatcher(pid)

	roomID, seat, running, ok := l.rooms.detach(pid)
	if !ok {
		return
	}
	if running {
		// 硬掉线 = 判负（对手胜），由对局 tick 在 50 ms 内推结算。
		if l.rooms.surrenderOnDisconnect(roomID, seat) {
			logger.Warnf("logic: 玩家掉线判负 room=%s player=%s seat=%d", roomID, pid, seat)
		}
	}
	logger.Infof("logic: 断线清理 player=%s room=%s running=%v", pid, roomID, running)
	l.tidyRoom(roomID)
	l.pushRoomState(roomID)
	l.pushRoomList()
}
