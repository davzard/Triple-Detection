using System;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 相机心跳源：提供最近一次出图时间戳，供 <see cref="CameraHealthMonitor"/> 判定相机离线（Alarm 2001）。
    /// Mock 引擎用定时器模拟出图；VM 引擎在采集/结果回调中刷新时间戳。
    /// </summary>
    public interface ICameraHeartbeat
    {
        /// <summary>最近一次出图时间（从未出图为 null）</summary>
        DateTime? LastFrameTime { get; }
    }
}
