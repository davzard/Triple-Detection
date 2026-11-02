using System;
using System.IO;
using System.Linq;
using TripleDetection.Application.Security;
using TripleDetection.Application.Services;
using TripleDetection.Application.SettingsServices;
using TripleDetection.Domain;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Entities.Queries;
using TripleDetection.Domain.Enums;
using TripleDetection.Infrastructure.Persistence;
using TripleDetection.Infrastructure.Repositories;
using TripleDetection.Presentation.Models;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// M6 RBAC 测试（设计文档 §3.6）：
    /// ① 角色解析 ② 权限矩阵 ③ 系统上下文放行 ④ 服务层强制校验 + 越权审计 ⑤ 设置保存校验
    /// </summary>
    public class AuthorizationTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "td_authz_" + Guid.NewGuid().ToString("N"));

        public AuthorizationTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            SessionManager.Clear();
            try { Directory.Delete(_dir, true); } catch { /* 清理失败不影响测试结论 */ }
        }

        private LoggingService Log() => new LoggingService(Path.Combine(_dir, "logs"), null);

        /// <summary>真实 DB + 审计 + 授权 + 任务服务（全链路）</summary>
        private (TaskService tasks, AuditLogService audit, AuthorizationService authz) CreateTaskStack()
        {
            var dbPath = Path.Combine(_dir, "authz.db");
            DatabaseInitializer.EnsureDatabaseCreated(dbPath);
            var db = new TripleDetectionDbContext("Data Source=" + dbPath);
            var audit = new AuditLogService(new AuditLogRepository(db));
            var authz = new AuthorizationService(audit, Log());
            var tasks = new TaskService(new SqliteRepository<ProdTask>(db), audit, authz);
            return (tasks, audit, authz);
        }

        // ===== 角色解析 =====

        [Theory]
        [InlineData("admin", Role.Admin)]
        [InlineData("Admin", Role.Admin)]
        [InlineData("ADMIN", Role.Admin)]
        [InlineData("engineer", Role.Engineer)]
        [InlineData(" Engineer ", Role.Engineer)]
        [InlineData("operator", Role.Operator)]
        [InlineData("", Role.Operator)]
        [InlineData("SuperUser", Role.Operator)] // 未知角色 → 最低权限
        public void RoleParser_规范化(string input, Role expected)
        {
            Assert.Equal(expected, RoleParser.Parse(input));
        }

        [Fact]
        public void RoleParser_null_返回Operator()
        {
            Assert.Equal(Role.Operator, RoleParser.Parse(null));
        }

        // ===== 权限矩阵 =====

        [Fact]
        public void 矩阵_检测与开班_全员允许()
        {
            foreach (var role in new[] { Role.Operator, Role.Engineer, Role.Admin })
            {
                Assert.True(AuthorizationService.Can(role, AuthorizationAction.DetectionRun));
                Assert.True(AuthorizationService.Can(role, AuthorizationAction.ShiftManage));
            }
        }

        [Fact]
        public void 矩阵_任务产品设置审计查看_工程师及以上()
        {
            var engActions = new[]
            {
                AuthorizationAction.TaskManage,
                AuthorizationAction.TaskApprove,
                AuthorizationAction.ProductManage,
                AuthorizationAction.SettingsModify,
                AuthorizationAction.AuditView
            };
            foreach (var action in engActions)
            {
                Assert.False(AuthorizationService.Can(Role.Operator, action), $"Operator 不应有 {action}");
                Assert.True(AuthorizationService.Can(Role.Engineer, action), $"Engineer 应有 {action}");
                Assert.True(AuthorizationService.Can(Role.Admin, action), $"Admin 应有 {action}");
            }
        }

        [Fact]
        public void 矩阵_用户管理审计导出备份_仅管理员()
        {
            var adminActions = new[]
            {
                AuthorizationAction.UserManage,
                AuthorizationAction.AuditExport,
                AuthorizationAction.BackupRun
            };
            foreach (var action in adminActions)
            {
                Assert.False(AuthorizationService.Can(Role.Operator, action), $"Operator 不应有 {action}");
                Assert.False(AuthorizationService.Can(Role.Engineer, action), $"Engineer 不应有 {action}");
                Assert.True(AuthorizationService.Can(Role.Admin, action), $"Admin 应有 {action}");
            }
        }

        // ===== 系统上下文放行 =====

        [Fact]
        public void 未登录_系统上下文_放行()
        {
            SessionManager.Clear(); // CurrentUserId == 0
            var (_, _, authz) = CreateTaskStack();
            Assert.True(authz.Can(AuthorizationAction.UserManage));
            Assert.True(authz.Can(AuthorizationAction.BackupRun));
        }

        // ===== 服务层强制校验 + 越权审计 =====

        [Fact]
        public void 操作员_创建任务_被拒且写审计()
        {
            var (tasks, audit, _) = CreateTaskStack();
            SessionManager.SetCurrentUser(1, "op", "127.0.0.1", Role.Operator);

            var ex = Assert.Throws<AuthorizationException>(() =>
                tasks.Create(new ProdTask { Name = "T" }, "op", 1));
            Assert.Equal(AuthorizationAction.TaskManage, ex.Action);

            var denied = audit.Query(new AuditLogQuery { Action = "DENIED" }).Items
                .Any(a => a.Action == "DENIED");
            Assert.True(denied, "越权尝试应写入审计日志");
        }

        [Fact]
        public void 工程师_创建任务_允许()
        {
            var (tasks, _, _) = CreateTaskStack();
            SessionManager.SetCurrentUser(2, "eng", "127.0.0.1", Role.Engineer);

            tasks.Create(new ProdTask { Name = "T" }, "eng", 2); // 不应抛异常
        }

        [Fact]
        public void 操作员_审批任务_被拒()
        {
            var (tasks, _, _) = CreateTaskStack();
            SessionManager.SetCurrentUser(1, "op", "127.0.0.1", Role.Operator);

            Assert.Throws<AuthorizationException>(() => tasks.Approve(1, "op", 1));
        }

        [Fact]
        public void 管理员_审批任务_允许()
        {
            var (tasks, _, _) = CreateTaskStack();
            SessionManager.SetCurrentUser(3, "adm", "127.0.0.1", Role.Admin);

            tasks.Approve(1, "adm", 3); // 不应抛异常
        }

        // ===== 设置保存校验 =====

        [Fact]
        public void 操作员_保存设置_被拒()
        {
            SessionManager.SetCurrentUser(1, "op", "127.0.0.1", Role.Operator);
            var (_, _, authz) = CreateTaskStack();
            var svc = new SystemSettingsService(authz);

            Assert.Throws<AuthorizationException>(() =>
                svc.Save(new SystemSettings { FactoryCode = "F1" }));
        }

        [Fact]
        public void 工程师_保存设置_允许()
        {
            SessionManager.SetCurrentUser(2, "eng", "127.0.0.1", Role.Engineer);
            var (_, _, authz) = CreateTaskStack();
            var svc = new SystemSettingsService(authz);

            svc.Save(new SystemSettings { FactoryCode = "F1" }); // 不应抛异常
        }
    }
}
