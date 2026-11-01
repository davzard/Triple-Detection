using System;
using Prism.Events;

namespace TripleDetection.Presentation.Events
{
    public class NavigationRequestedEventArgs : EventArgs
    {
        public string Target { get; }

        public NavigationRequestedEventArgs(string target)
        {
            Target = target;
        }
    }

    /// <summary>
    /// 导航请求事件：允许任意视图（如仪表盘快捷操作）触发主窗口的完整导航逻辑
    /// （Prism 区域导航 + 侧边栏高亮 + 标签页 + 状态栏同步）。
    /// </summary>
    public class NavigationRequestedEvent : PubSubEvent<NavigationRequestedEventArgs>
    {
    }
}
