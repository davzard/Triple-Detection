namespace TripleDetection.Domain.Enums
{
    /// <summary>
    /// 受控操作项（M6 RBAC，设计文档 §3.6 权限矩阵）：
    /// Detection.Run / Shift.Start 全员；Task/Product/Settings/Audit.View 工程师及以上；
    /// User.Manage / Audit.Export / Backup.Run 仅管理员。
    /// </summary>
    public enum AuthorizationAction
    {
        /// <summary>检测（查看/启动/暂停/复位）</summary>
        DetectionRun,
        /// <summary>开班/下班/自动运行</summary>
        ShiftManage,
        /// <summary>任务创建/编辑/状态变更/删除</summary>
        TaskManage,
        /// <summary>任务审批</summary>
        TaskApprove,
        /// <summary>产品管理</summary>
        ProductManage,
        /// <summary>参数设置（PLC/设备/系统）</summary>
        SettingsModify,
        /// <summary>用户管理</summary>
        UserManage,
        /// <summary>审计日志查看</summary>
        AuditView,
        /// <summary>审计日志导出/删除</summary>
        AuditExport,
        /// <summary>数据库备份/恢复</summary>
        BackupRun
    }
}
