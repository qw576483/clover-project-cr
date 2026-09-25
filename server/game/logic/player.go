package logic

import (
	"errors"
	"strings"
	"unicode/utf8"

	"clover-cr/game/datadef"
	"clover-cr/game/def"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"
	"github.com/qw576483/clover-server-engine/pkg/shared/proto"
	"github.com/qw576483/clover-server-engine/pkg/transport/event"
)

const (
	// maxNicknameRunes 昵称长度上限（字符数，不是字节数）。
	maxNicknameRunes = 16
	// playerIDPrefix 角色 ID 的前缀：本项目没有独立「创角」报文，
	// 角色 ID 由账号派生（见 playerIDOf），前缀把它和账号名区分开。
	playerIDPrefix = "p_"
)

// errNotLoggedIn 未登录连接发来的业务请求。网关登录门禁默认开启，
// 所以这条分支正常到不了 —— 到得了就说明装配有问题，必须留痕。
var errNotLoggedIn = errors.New("未登录")

// playerIDOf 返回本次请求的角色 ID，必要时把它绑定到连接上。
//
// 为什么由账号派生：冻结契约（docs/步骤文档.md §4.1）里**没有**创角报文
// （只有 MsgSetNickname），而引擎的 Ctx.PlayerID 是「创角 / 进入游戏后才有值」的
// —— 不绑定的话 PlayerSchema（OwnerType=OwnerPlayer）会以空 ID 为键，
// 所有玩家的昵称 / 卡组 / 战绩会互相串（典型的静默失败）。
// 引擎为此提供了正规入口 c.SetPlayerID（patterns/signup-login.md §要点：
// 「鉴权成功绑定玩家：c.SetPlayerID(playerID)」），绑定后引擎在后续每条消息前
// 自动恢复（RestorePlayerID），客户端无需传任何东西。
// 派生规则是 1:1（同账号永远同一个角色），所以数据跨重连 / 跨重启稳定。
func playerIDOf(c event.Ctx) string {
	if pid := c.PlayerID(); pid != "" {
		return pid
	}
	acc := c.Account()
	if acc == "" {
		return ""
	}
	pid := playerIDPrefix + acc
	c.SetPlayerID(pid)
	return pid
}

// pushTarget 返回网关（gwcore）可寻址的推送标识。
//
// 网关的 defaultRouter 先查 "p:"+target、再查 "a:"+target：`"p:"`（角色维度）索引只在
// 逻辑服发出 GWControlBind 时才建立（见 clover-server-engine 的 `Ctx.SetPlayerID`，同一连接
// 已登记过就不再发），`"a:"`（账号维度）索引则由网关在**每条登录回包**上重建。
// 本工程的角色 ID 由账号派生（playerIDOf），两者一一对应 ⇒ 推送按账号维度寻址。
// 未命中任何索引时网关不推也不报错（静默丢弃），所以这里必须选一条**每次登录都会重建**的键。
func pushTarget(pid string) string {
	return strings.TrimPrefix(pid, playerIDPrefix)
}

// loadPlayer 加载玩家档案，并把「结算时排队等待补记的战绩」补上。
//
// 战绩为什么不在结算时直接写：结算发生在对局 tick 里（定时器 goroutine），
// 那里没有 event.Ctx，而引擎的数据改动统一走 LoadStruct + handler 返回后自动 Commit。
// 所以结算只把结果排进 registry.pending，玩家下一次发消息（任意一条）时在这里补记 ——
// 不丢、可查（补记会打日志），代价只是"晚一条消息落库"。
func (l *gameLogic) loadPlayer(c event.Ctx, pid string) (*datadef.PlayerData, error) {
	var p datadef.PlayerData
	if err := l.g.LoadStruct(c, datadef.PlayerSchema, pid, &p); err != nil {
		return nil, err
	}
	if win, ok := l.rooms.takePendingResult(pid); ok {
		if win {
			p.Wins++
		} else {
			p.Losses++
		}
		logger.Infof("logic: 补记上一局战绩 player=%s win=%v wins=%d losses=%d", pid, win, p.Wins, p.Losses)
	}
	return &p, nil
}

// profile 是本层 handler 的统一入口：取角色 ID + 加载档案（顺带补记战绩）。
// ⛔ 同一个 handler 里对同一 key 只许调用一次（引擎的 identity map 会把待提交指针
// 转给最后一次加载的那个变量，第二次加载会让第一次的改动丢失）。
func (l *gameLogic) profile(c event.Ctx) (*datadef.PlayerData, string, error) {
	pid := playerIDOf(c)
	if pid == "" {
		logger.Warnf("logic: 未登录连接发来业务请求（msg=%d account=%q）", c.MsgID(), c.Account())
		return nil, "", errNotLoggedIn
	}
	p, err := l.loadPlayer(c, pid)
	if err != nil {
		return nil, pid, err
	}
	return p, pid, nil
}

// warnNotLoggedIn 处理「未登录 / 数据加载失败」这类统一失败：留痕 + 弹窗 + 回包。
func (l *gameLogic) warnNotLoggedIn(c event.Ctx, err error, replyErr string) {
	logger.Errorf("logic: 玩家档案不可用（msg=%d account=%q player=%q）: %v",
		c.MsgID(), c.Account(), c.PlayerID(), err)
	_ = l.g.Alert(c, &proto.EAlertNotify{Title: "提示", Content: replyErr, Level: "warning"})
}

// ---------------------------------------------------------------- handlers

// onSetNickname 设置昵称（也是本项目的「创角」动作：第一条业务消息时绑定角色 ID）。
func (l *gameLogic) onSetNickname(c event.Ctx) error {
	var req def.SetNicknameReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: SetNickname 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}

	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.SetNicknameReply{Err: err.Error()})
		return nil
	}

	nick := strings.TrimSpace(req.Nickname)
	if n := utf8.RuneCountInString(nick); n == 0 || n > maxNicknameRunes {
		logger.Warnf("logic: 昵称长度非法 player=%s runes=%d", pid, n)
		l.g.Reply(c, def.SetNicknameReply{OK: false, Err: "昵称需为 1~16 个字符"})
		return nil
	}

	p.Nickname = nick
	// 创角即落默认卡组：这是本项目的创角动作（见函数头注释），此后玩家不必先去
	// 编队页保存一次才能开局（见 deck.go::ensureDefaultDeck）。
	l.ensureDefaultDeck(p, pid)
	logger.Infof("logic: 设置昵称成功 player=%s nickname=%q", pid, nick)
	l.g.Reply(c, def.SetNicknameReply{OK: true, Nickname: nick})
	return nil
}

// onGetProfile 拉个人档案（昵称 / 胜场 / 负场 / 卡组）。
func (l *gameLogic) onGetProfile(c event.Ctx) error {
	var req def.GetProfileReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: GetProfile 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}

	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.GetProfileReply{})
		return nil
	}

	logger.Infof("logic: 拉取档案 player=%s nickname=%q deck=%d wins=%d losses=%d",
		pid, p.Nickname, len(p.Deck), p.Wins, p.Losses)
	l.g.Reply(c, def.GetProfileReply{
		Nickname: p.Nickname,
		Wins:     p.Wins,
		Losses:   p.Losses,
		Deck:     p.Deck,
	})
	return nil
}
