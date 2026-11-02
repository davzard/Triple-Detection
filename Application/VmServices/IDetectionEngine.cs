using System;
using System.Collections.Generic;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// 检测引擎抽象：隔离 UI 层与具体视觉平台 SDK（VisionMaster 4.x）。
    /// 有 SDK 时使用 VmIntegrationService，无 SDK 时使用 MockDetectionEngine。
    /// 结果事件在非 UI 线程触发，订阅方需自行切回 UI 线程。
    /// </summary>
    public interface IDetectionEngine
    {
        /// <summary>检测结果回调（非 UI 线程触发）</summary>
        event EventHandler<DetectionResult> DetectionResult;

        /// <summary>方案崩溃事件（非 UI 线程触发）。订阅方应尝试自动重载方案（次数上限，超限报警）。</summary>
        event EventHandler SolutionCrashed;

        /// <summary>方案是否已加载</summary>
        bool IsSolutionLoaded { get; }

        /// <summary>当前是否存在可用的检测流程</summary>
        bool HasProcedure { get; }

        /// <summary>是否处于连续运行状态</summary>
        bool IsContinuousRun { get; }

        /// <summary>加载检测方案</summary>
        void LoadSolution(string solPath);

        /// <summary>获取方案中的所有流程名称</summary>
        List<string> GetAllProcedureNames();

        /// <summary>切换当前检测流程</summary>
        void SetProcedure(string procedureName);

        /// <summary>设置当前任务上下文（用于结果入库）</summary>
        void SetCurrentTaskContext(int taskId, int productId, string batchNumber, DateTime? productionDate, DateTime? expirationDate);

        /// <summary>单次运行检测</summary>
        void RunOnce();

        /// <summary>设置连续运行开关</summary>
        void SetContinuousRun(bool enable);

        /// <summary>停止检测</summary>
        void Stop();

        /// <summary>向检测方案写入全局变量（三期信息 ToVM）</summary>
        void SetGlobalVariableString(string name, string value);

        /// <summary>从检测方案读取全局变量（三期信息 FromVM）</summary>
        string GetGlobalVariableString(string name);
    }
}
