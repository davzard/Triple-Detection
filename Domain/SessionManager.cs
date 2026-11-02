using System;
using TripleDetection.Domain.Enums;

namespace TripleDetection.Domain;

public static class SessionManager
{
    public static int CurrentUserId { get; private set; } = 0;
    public static string CurrentUserName { get; private set; } = "system";
    public static string CurrentIpAddress { get; private set; } = "127.0.0.1";
    public static DateTime LoginTime { get; private set; } = DateTime.Now;
    /// <summary>当前用户角色（M6 RBAC；未登录为 Operator）</summary>
    public static Role CurrentRole { get; private set; } = Role.Operator;

    public static void SetCurrentUser(Domain.Entities.User user)
    {
        if (user == null) return;
        CurrentUserId = user.Id;
        CurrentUserName = user.RealName ?? user.Username;
        LoginTime = DateTime.Now;
        CurrentRole = RoleParser.Parse(user.Role);
    }

    public static void SetCurrentUser(int userId, string userName, string ipAddress = "127.0.0.1", Role role = Role.Operator)
    {
        CurrentUserId = userId;
        CurrentUserName = userName;
        CurrentIpAddress = ipAddress;
        LoginTime = DateTime.Now;
        CurrentRole = role;
    }

    public static void Clear()
    {
        CurrentUserId = 0;
        CurrentUserName = "system";
        CurrentIpAddress = "127.0.0.1";
        CurrentRole = Role.Operator;
    }
}