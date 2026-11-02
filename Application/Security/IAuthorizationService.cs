using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;

namespace TripleDetection.Application.Security
{
    /// <summary>
    /// 授权服务（M6 RBAC，设计文档 §3.6）：
    /// 双层执行——ViewModel 层隐藏入口（体验）+ Service 层 <see cref="Require"/> 强制校验（安全）。
    /// 未登录（系统上下文：启动维护、后台任务、测试）不拦截；越权尝试写 AuditLog。
    /// </summary>
    public interface IAuthorizationService
    {
        /// <summary>当前用户角色（未登录为 Operator）</summary>
        Role CurrentRole { get; }

        /// <summary>当前用户能否执行操作（未登录 = 系统上下文，允许）</summary>
        bool Can(AuthorizationAction action);

        bool Can(User user, AuthorizationAction action);

        /// <summary>
        /// 强制校验：拒绝时写 AuditLog + 记日志并抛 <see cref="Domain.AuthorizationException"/>。
        /// 未登录（系统上下文）直接放行。
        /// </summary>
        void Require(AuthorizationAction action);
    }
}
