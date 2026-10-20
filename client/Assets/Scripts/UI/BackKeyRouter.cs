using CloverEngine;
using CR.UI.Panels;
using UnityEngine;

namespace CR.UI
{
    /// <summary>
    /// 返回键（ESC）路由：把"回上一层"落到当前屏幕上最上面的那一层。
    ///
    /// <para>
    /// <b>为什么需要它</b>：面板之间没有统一的返回栈（Popup 层是互斥开关，不是栈），
    /// 而键盘 ESC 在工程内原先没有任何处理 —— 玩家开了一个弹窗 / 进了房间 / 进了对局后按 ESC
    /// 完全没有反应。这里按"屏幕层级 + 站点"给出唯一的一份路由，⛔ 不让每个面板自己读按键。
    /// </para>
    /// <para>
    /// <b>优先级</b>（从最上面一层往下）：
    /// 设置弹窗 → 注册页 → 房间列表 → 结算（不响应，必须做出选择）→ 主菜单覆盖层
    /// → 卡组编辑 → 创角页 → 房间站点（确认离开）→ 暂停（继续对局）→ 对局（打开暂停）。
    /// </para>
    /// <para>
    /// <b>来源</b>：按键走引擎的 <c>Game.Input.GetKeyDown(GameKey.Escape)</c>（不是裸 <c>Input</c>），
    /// 与引擎的键位重绑 / 输入锁同一口径。组件挂在 <c>DontDestroyOnLoad</c> 节点上，跨场景常驻。
    /// </para>
    /// </summary>
    public sealed class BackKeyRouter : MonoBehaviour
    {
        private const string Tag = "BackKeyRouter";

        /// <summary>启动钩子键（同 key 重复注册会覆盖，可安全重复登记）。</summary>
        private const string LaunchHookKey = "CR.UI.BackKey";

        /// <summary>常驻节点（停止 Play 后 Unity 认为已销毁 ⇒ 重新 Play 时会重建）。</summary>
        private static GameObject _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            Game.RegisterLaunchHook(LaunchHookKey, Install);
        }

        /// <summary>安装常驻返回键路由（幂等）。</summary>
        public static void Install()
        {
            if (_root != null) return;

            _root = new GameObject("[BackKeyRouter]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<BackKeyRouter>();
            Game.Logger?.Info(Tag, "返回键路由已安装（ESC：设置/注册/房间列表/覆盖层/卡组/房间/暂停/对局）");
        }

        private void Update()
        {
            if (Game.Input == null) return;
            if (!Game.Input.GetKeyDown(GameKey.Escape)) return;
            // 拖放中的 ESC = 取消本次拖放（HudPanel 自己处理，见其 PollDrag）。本帧不让位的话，
            // 同一帧会再走一遍"对局 → 打开暂停"，玩家看到的是取消拖放之外还多开了一个菜单。
            if (HudPanel.DraggingCard || HudPanel.EscapeConsumedFrame == Time.frameCount) return;
            HandleEscape();
        }

        private static void HandleEscape()
        {
            var ui = Game.UI;
            if (ui == null) return;

            // ① Popup 层（最上面的一层）
            if (ui.IsOpen<SettingsPanel>())
            {
                Game.Logger?.Info(Tag, "ESC → 关闭设置");
                ui.Close<SettingsPanel>();
                return;
            }
            if (ui.IsOpen<RegisterPanel>())
            {
                Game.Logger?.Info(Tag, "ESC → 注册页返回登录");
                Game.Event?.Emit(Events.Flow.BackToLoginRequest);
                return;
            }
            if (ui.IsOpen<RoomListPanel>())
            {
                Game.Logger?.Info(Tag, "ESC → 关闭房间列表");
                ui.Close<RoomListPanel>();
                return;
            }
            if (ui.IsOpen<ResultPanel>())
            {
                // 结算页必须由玩家选择「再来一局 / 回主菜单」；ESC 不替玩家做决定。
                Game.Logger?.Info(Tag, "ESC → 结算页不响应（必须选择再来一局或回主菜单）");
                return;
            }

            // ② 主菜单内的覆盖层（对战入口 / 玩家资料）
            var menu = ui.Get<MainMenuPanel>();
            if (menu != null && menu.HasOverlayOpen)
            {
                Game.Logger?.Info(Tag, "ESC → 关闭主菜单覆盖层");
                menu.TryCloseTopOverlay();
                return;
            }

            // ③ 整屏页面
            if (ui.IsOpen<DeckEditPanel>())
            {
                Game.Logger?.Info(Tag, "ESC → 关闭卡组编辑（等同取消）");
                ui.Close<DeckEditPanel>();
                return;
            }
            if (ui.IsOpen<NicknamePanel>())
            {
                Game.Logger?.Info(Tag, "ESC → 创角页返回登录");
                Game.Event?.Emit(Events.Flow.BackToLoginRequest);
                return;
            }

            // ④ 站点层
            var station = Game.Fsm != null ? Game.Fsm.Current : null;
            if (station == Stations.Room)
            {
                Game.Logger?.Info(Tag, "ESC → 房间站点：请求确认离开");
                ui.Confirm("离开房间", "确定要离开当前房间吗？", () =>
                {
                    Game.Logger?.Info(Tag, "已确认离开房间（ESC）");
                    Game.Event?.Emit(Events.Room.LeaveRequest);
                }, null, "离 开", "留 下");
                return;
            }
            if (station == Stations.Pause)
            {
                Game.Logger?.Info(Tag, "ESC → 暂停菜单：继续对局");
                ui.Close<PausePanel>();
                Game.Event?.Emit(Events.Flow.StationEnterRequest, Stations.Battle);
                return;
            }
            if (station == Stations.Battle)
            {
                Game.Logger?.Info(Tag, "ESC → 对局：打开暂停菜单");
                Game.Event?.Emit(Events.Flow.StationEnterRequest, Stations.Pause);
            }
        }
    }
}
