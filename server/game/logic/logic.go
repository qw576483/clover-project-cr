// Package logic 是服务端业务 handler 层：只做「取参 → 调 core → 回包 / 推送」，
// ⛔ 不许写伤害 / 速度 / 射程 / 胜负等玩法规则分支（那些只在 game/core/）。
//
// 本文件是**唯一的装配点**：配表加载 → 配表适配 core.CardTable → 房间模块
// （引擎 room.Module 外壳 + 业务 room.Kernel 内核）→ handler 注册 → 断线回调。
// 各片 handler 分别在：
//
//	player.go  设昵称 / 拉档案
//	deck.go    卡池 / 我的卡组 / 存卡组
//	room.go    房间注册表 + room.Kernel + 建房/列表/加入/离开/准备/开打/设 AI
//	battle.go  出牌 / 投降 / 主动同步 + 对局 tick + 快照与结算推送
//	ai.go      主菜单「人机对战」入口 + 房间 AI 补位 + AI 卡组
//
// 契约出处：docs/步骤文档.md §4.1（消息号与协议）。
package logic

import (
	"fmt"
	"os"

	"clover-cr/game/def"
	"github.com/qw576483/clover-server-engine/pkg/app"
	"github.com/qw576483/clover-server-engine/pkg/domain/room"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"
)

// gameLogic 是业务层持有的宿主引用（handler 里用 l.g.Reply / l.g.Alert 等）。
type gameLogic struct {
	g       *app.Game
	cards   *cardTable
	rooms   *roomRegistry
	roomMod room.Module
	// aiDeckIDs 是 AI 对手的卡组，装配期按配表顺序取定（见 ai.go 的 buildAIDeck）。
	aiDeckIDs []int32
}

// G 全局业务入口。
var G = &gameLogic{}

func init() {
	app.Mount(app.RoleGame, func(g *app.Game) {
		G.g = g
		G.mount(g)
	})
}

// tableDirs 是配表目录的候选。运行目录是 server/（go run . -config configs/all），
// 但换一个工作目录（例如从项目根起）也不该静默失效 —— 三个候选逐个试，谁在就用谁。
var tableDirs = []string{
	"game/table/tsv",
	"server/game/table/tsv",
	"../game/table/tsv",
}

// mount 完成全部装配。任一步失败都打 Errorf 留痕：
// 静默降级会变成「服起来了但一局也开不了」，那种故障无从排查。
func (l *gameLogic) mount(g *app.Game) {
	dir, err := findTableDir()
	if err != nil {
		logger.Errorf("logic: %v", err)
	} else if ct, names, terr := loadCardTable(dir); terr != nil {
		logger.Errorf("logic: 配表适配 core.CardTable 失败: %v", terr)
	} else {
		l.cards = ct
		l.aiDeckIDs = buildAIDeck(ct)
		app.PublishTableLoaded(g, names)
		logger.Infof("logic: 配表适配完成（数据源 = 生成物 table.Default，目录 %s）card=%d unit=%d；AI 卡组=%v；table.Loaded=%v",
			dir, ct.CardCount(), ct.UnitCount(), l.aiDeckIDs, names)
	}

	l.rooms = newRoomRegistry()
	// ★ MasterCaller 刻意留 nil（单进程部署：server_type=all，node_id 未配、etcd 不用，
	// 引擎自己就打印 "cross-node object transfer disabled (single-node only)"）。
	//
	// ⚠️ **旧结论已失效（2026-09-24 复核）**：本注释原先逐字写着「本工程唯一可用的形态就是
	// 不接 master」，理由②是「注册 6001 必 panic（business message id must be > 10000）」。
	// **引擎已修** —— 三条逐处复核成立（均为 clover-server-engine 内的实际代码）：
	//   ① `internal/domain/room/master_handlers.go` 的 `registerRoomMsg(...)` 已改走
	//      `mg.InternalOnMsg(msgID, handler)` —— **保留号内部路径**，不再经业务的 `OnMsg`；
	//      该处注释逐字写着「6001..6004 是引擎内建号，经业务路径会被『业务消息号必须 > 10000』的守卫拒绝」；
	//   ② `internal/app/facade.go` 提供 `MasterGameFacade.InternalOnMsg(msgID, h)`（门面侧的内部号入口），
	//      `MasterHandlerGame` 接口亦含 `InternalOnMsg`；
	//   ③ `pkg/domain/room/README.md` 的「MasterHandlers」段给了正式接线：业务侧在 master 角色挂一次
	//      `room.NewMasterHandlers(mg)`，并在 `room.Config` 里传 `MasterCaller: g`
	//      （不传 = 整条跨节点接管链路被跳过）。
	//
	// **现在成立的真实约束（不是"接不上"，而是"用不上"）**：本工程是**单进程部署**
	// （server_type=all、node_id 未配、etcd 不用）⇒ 根本不存在「跨节点寻房主」这件事，
	// 接 master 只会平白多一条注册 / 查询链路与一个 master 角色，收益为 0。
	// 外壳里每一处 master 交互（EnsureRoom 的 RegisterRoom / JoinRoom 的 EnsureOwner /
	// DestroyRoom 的 UnregisterRoom / Takeover 构造）都由 `if m.Owner != nil` /
	// `if cfg.MasterCaller != nil` 守卫 ⇒ MasterCaller=nil 时全部跳过，
	// 房间生命周期与内核行为**完全不受影响**。
	// 后果：跨节点接管（takeover）不启用 —— 单进程部署本来也用不上；
	// roomKernel.ExportState / ImportState 仍按 Kernel 契约实现（未被调用）。
	// ⇒ 将来若改为多节点部署：把这里的 `MasterCaller` 换成 `g`，并在 master 角色挂
	// `room.NewMasterHandlers(mg)`（口径见上面 ③ 的 README 段）。
	l.roomMod = room.NewModule(room.Config{
		MasterCaller: nil,
		Pusher:       func(pid string, msgID uint32, v any) error { return g.PushToPlayer(pushTarget(pid), msgID, v) },
		NodeAddr:     g.Addr(),
		Kernel:       &roomKernel{}, // ★ 业务侧内核（房间元数据 + 对局实例都在 registry 里）
	})
	logger.Infof("logic: 房间模块已挂载（room.Module + 业务 Kernel，单进程无 master）node=%s", g.Addr())

	g.OnMsg(def.MsgSetNickname, l.onSetNickname)
	g.OnMsg(def.MsgGetProfile, l.onGetProfile)
	g.OnMsg(def.MsgGetCardPool, l.onGetCardPool)
	g.OnMsg(def.MsgGetDeck, l.onGetDeck)
	g.OnMsg(def.MsgSaveDeck, l.onSaveDeck)
	g.OnMsg(def.MsgRoomCreate, l.onRoomCreate)
	g.OnMsg(def.MsgRoomList, l.onRoomList)
	g.OnMsg(def.MsgRoomJoin, l.onRoomJoin)
	g.OnMsg(def.MsgRoomLeave, l.onRoomLeave)
	g.OnMsg(def.MsgRoomReady, l.onRoomReady)
	g.OnMsg(def.MsgRoomStart, l.onRoomStart)
	g.OnMsg(def.MsgRoomSetAi, l.onRoomSetAi)
	g.OnMsg(def.MsgBattlePlayCard, l.onBattlePlayCard)
	g.OnMsg(def.MsgBattleSurrender, l.onBattleSurrender)
	g.OnMsg(def.MsgBattleSync, l.onBattleSync)
	g.OnMsg(def.MsgAiBattleStart, l.onAiBattleStart)
	g.OnDisconnect(l.onDisconnect)
	logger.Infof("logic: handler 注册完成：C2S 16 条 + 断线回调 1 条")
}

// findTableDir 定位 tsv 目录。
func findTableDir() (string, error) {
	for _, cand := range tableDirs {
		if st, err := os.Stat(cand); err == nil && st.IsDir() {
			return cand, nil
		}
	}
	return "", fmt.Errorf("找不到配表目录（试过 %v）", tableDirs)
}

// 配表装配入口在 cardtable.go 的 loadCardTable：
// table.NewTables() + LoadAll(dir) 把生成物填进 table.Default，再适配成 core.CardTable，
// 逐表行数（card=60 …）由 mount 广播给引擎的 table.Loaded 事件。
