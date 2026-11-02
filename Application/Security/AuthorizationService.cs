using TripleDetection.Application.Services;
using TripleDetection.Domain;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;

namespace TripleDetection.Application.Security
{
    /// <summary>
    /// 授权服务实现（M6 RBAC，设计文档 §3.6 权限矩阵）。
    /// </summary>
    public class AuthorizationService : IAuthorizationService
    {
        private readonly IAuditLogService _auditLog;
        private readonly LoggingService _logService;

        public AuthorizationService(IAuditLogService auditLog, LoggingService logService)
        {
            _auditLog = auditLog;
            _logService = logService;
        }

        public Role CurrentRole => SessionManager.CurrentRole;

        public bool Can(AuthorizationAction action)
        {
            // 未登录 = 系统内部调用（启动维护、后台任务、测试），不拦截
            if (SessionManager.CurrentUserId == 0) return true;
            return Can(SessionManager.CurrentRole, action);
        }

        public bool Can(User user, AuthorizationAction action) =>
            Can(RoleParser.Parse(user?.Role), action);

        /// <summary>权限矩阵（静态，可测试）。</summary>
        public static bool Can(Role role, AuthorizationAction action)
        {
            switch (action)
            {
                case AuthorizationAction.DetectionRun:
                case AuthorizationAction.ShiftManage:
                    return true; // 全员
                case AuthorizationAction.TaskManage:
                case AuthorizationAction.TaskApprove:
                case AuthorizationAction.ProductManage:
                case AuthorizationAction.SettingsModify:
                case AuthorizationAction.AuditView:
                    return role >= Role.Engineer;
                case AuthorizationAction.UserManage:
                case AuthorizationAction.AuditExport:
                case AuthorizationAction.BackupRun:
                    return role == Role.Admin;
                default:
                    return false;
            }
        }

        public void Require(AuthorizationAction action)
        {
            if (Can(action)) return;

            var userId = SessionManager.CurrentUserId;
            var role = SessionManager.CurrentRole;
            _auditLog?.Log(userId, "DENIED", "Authorization", 0,
                $"越权尝试：{action}（角色={role}）");
            _logService?.LogWarn("安全", $"越权尝试已拦截：{action}（用户={SessionManager.CurrentUserName}, 角色={role}）");
            throw new AuthorizationException(action);
        }
    }
}
