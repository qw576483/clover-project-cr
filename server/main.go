// Command clover-cr 是《皇室战争》核心对局复刻项目的服务端入口。
//
// 启动：
//
//	go run . -config configs/all
//
// 消息号 / 协议契约见 server/game/def/，对局内核见 server/game/core/（纯 Go）。
package main

import (
	"flag"
	"os"

	"github.com/qw576483/clover-server-engine/pkg/app"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"

	_ "clover-cr/game/logic" // 触发 logic 包 init() 完成 handler 挂载
)

func main() {
	cfgPath := flag.String("config", "configs/all", "path to config dir or file")
	flag.Parse()

	if err := app.Run(*cfgPath); err != nil {
		// 起服失败是必须留痕的非预期分支：走引擎包级 logger，不用 log.Printf。
		logger.Errorf("app start failed (config=%s): %v", *cfgPath, err)
		os.Exit(1)
	}
}
