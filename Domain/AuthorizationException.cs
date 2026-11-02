using System;
using TripleDetection.Domain.Enums;

namespace TripleDetection.Domain
{
    /// <summary>越权操作异常（M6 RBAC）：服务层强制校验失败时抛出，UI 层捕获并提示。</summary>
    public class AuthorizationException : Exception
    {
        public AuthorizationAction Action { get; }

        public AuthorizationException(AuthorizationAction action)
            : base($"无权限执行操作：{action}")
        {
            Action = action;
        }
    }
}
