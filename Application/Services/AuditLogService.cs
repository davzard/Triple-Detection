using System;
using System.Collections.Generic;
using System.Linq;
using TripleDetection.Application.Security;
using TripleDetection.Domain;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Domain.Repositories;

namespace TripleDetection.Application.Services;

public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly IAuthorizationService? _authz;

    public AuditLogService(IAuditLogRepository repository, IAuthorizationService? authz = null)
    {
        _repository = repository;
        _authz = authz;
    }

    public void Log(int userId, string action, string objectType, int objectId, string details)
    {
        Log(userId, action, objectType, objectId, details, SessionManager.CurrentIpAddress);
    }

    public void Log(int userId, string action, string objectType, int objectId, string details, string ipAddress)
    {
        try
        {
            var log = new AuditLog
            {
                UserId = userId,
                UserName = SessionManager.CurrentUserName,
                Action = action,
                ObjectType = objectType,
                ObjectId = objectId,
                Details = details,
                IpAddress = string.IsNullOrEmpty(ipAddress) ? SessionManager.CurrentIpAddress : ipAddress,
                CreateAt = DateTime.Now
            };
            _repository.Add(log);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AuditLog 保存失败: {ex.Message}");
        }
    }

    public IPagedResult<AuditLog> Query(AuditLogQuery query) => _repository.Query(query);
    public IEnumerable<AuditLog> Export(AuditLogQuery query)
    {
        _authz?.Require(AuthorizationAction.AuditExport);
        return _repository.Export(query);
    }

    public IEnumerable<AuditLog> GetByUserId(int userId)
    {
        return _repository.Find(x => x.UserId == userId && !x.IsDeleted).OrderByDescending(x => x.CreateAt);
    }
}