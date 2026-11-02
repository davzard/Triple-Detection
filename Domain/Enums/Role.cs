namespace TripleDetection.Domain.Enums
{
    /// <summary>
    /// 用户角色（M6 RBAC，设计文档 §3.6 权限矩阵）。
    /// 现有 User.Role 字符串值（"Operator"/"Engineer"/"Admin"）经 <see cref="RoleParser"/> 规范化。
    /// </summary>
    public enum Role
    {
        Operator = 0,
        Engineer = 1,
        Admin = 2
    }

    public static class RoleParser
    {
        /// <summary>字符串 → 角色；空/未知一律按最低权限 Operator 处理（安全侧）。</summary>
        public static Role Parse(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return Role.Operator;
            switch (role.Trim().ToLowerInvariant())
            {
                case "admin": return Role.Admin;
                case "engineer": return Role.Engineer;
                default: return Role.Operator;
            }
        }

        public static string ToDisplay(this Role role) => role switch
        {
            Role.Admin => "管理员",
            Role.Engineer => "工程师",
            _ => "操作员"
        };
    }
}
