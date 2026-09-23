# Clover × Clash Royale

用 [Clover 客户端引擎](https://github.com/qw576483/clover-client-unity-engine) 复刻《皇室战争》的工程。

> **本工程尚未完成**：仍在开发中，功能与画面都在变 —— 下面的范围与说明只反映当前进度，不等于最终形态。

**复刻范围**：竖版（设计画布 **1080×1920**）的 1v1 实时对战，**权威服务器 + 房间制**（大厅 → 房间列表 → 对局）。
用户额外点名（原版没有）：**人机 PK**、**创建房间 + 房间列表加入**、**60 张典型卡**。
**明确不做**：卡牌升级 / 经验 / 金币 / 宝石 / 宝箱 / 天梯杯数 / 部落 / 锦标赛 / 卡牌解锁养成 / 赛季通行证。

## 工程结构

| 路径 | 内容 |
|---|---|
| `client/` | Unity 工程（复刻本体；`Library/` `Temp/` `Logs/` 等生成物不入库） |
| `server/` | Go 服务端（本工程是**联机**形态：权威服务器 + 房间，**不降级为单机**） |
| `策划/` | 复刻规格（`策划案/皇室战争参考规格.md`）、对照表、验收表、素材调研、原版 UI 素材索引、单位帧段表 |
| `tools/` | 判据与工具 |
| `docs/` | 客户端架构、客户端 API 参考、步骤文档与并行任务书（`agents/`） |
| `原版资源/` | 原版素材与逆向产物 |

## 声明

本项目**仅供技术交流与学习**，禁止用于任何商业用途。

## 相关仓库

| 仓库 | 说明 |
|---|---|
| [clover-client-unity-engine](https://github.com/qw576483/clover-client-unity-engine) | 客户端引擎（本工程的运行底座） |
| [clover-server-engine](https://github.com/qw576483/clover-server-engine) | 服务端引擎（`server/` 的底座） |
| [clover-tools](https://github.com/qw576483/clover-tools) | 打表工具 |
| [clover-ai-skill](https://github.com/qw576483/clover-ai-skill) | AI 交付 skill（本工程按它的规范做） |
| [clover-doc](https://github.com/qw576483/clover-doc) | 框架文档 |

> 从没用过 Clover？先看 [新手指南：用 AI 从零做一个 Clover 游戏](https://github.com/qw576483/clover-doc#beginner-guide)。
