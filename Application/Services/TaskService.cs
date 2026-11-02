using System;
using System.Collections.Generic;
using TripleDetection.Application.Security;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Domain.Repositories;

namespace TripleDetection.Application.Services;

public class TaskService : ITaskService
{
    private readonly IRepository<ProdTask> _repository;
    private readonly IAuditLogService _auditLog;
    private readonly IAuthorizationService? _authz;

    public TaskService(IRepository<ProdTask> repository, IAuditLogService auditLog, IAuthorizationService? authz = null)
    {
        _repository = repository;
        _auditLog = auditLog;
        _authz = authz;
    }

    public IEnumerable<ProdTask> GetAll() => _repository.GetAll();
    public IEnumerable<ProdTask> GetByStatus(TaskStatus status) => _repository.Find(x => x.Status == status);
    public ProdTask GetById(int id) => _repository.GetById(id);

    public void Create(ProdTask task, string createBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.TaskManage);
        task.CreateBy = createBy;
        task.UpdateBy = createBy;
        task.CreateAt = DateTime.Now;
        task.UpdateAt = DateTime.Now;
        task.IsDeleted = false;
        task.Status = TaskStatus.Pending;
        _repository.Add(task);
        _auditLog?.Log(currentUserId, "创建", "Task", task.Id, $"创建任务: {task.Name}");
    }

    public void Update(ProdTask task, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.TaskManage);
        task.UpdateBy = updateBy;
        task.UpdateAt = DateTime.Now;
        _repository.Update(task);
        _auditLog?.Log(currentUserId, "修改", "Task", task.Id, $"修改任务: {task.Name}");
    }

    public void Approve(int id, string reviewedBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.TaskApprove);
        var task = _repository.GetById(id);
        if (task != null && task.Status == TaskStatus.Pending)
        {
            task.Status = TaskStatus.Approved;
            task.ReviewedBy = reviewedBy;
            task.ReviewedAt = DateTime.Now;
            task.UpdateBy = reviewedBy;
            task.UpdateAt = DateTime.Now;
            _repository.Update(task);
            _auditLog?.Log(currentUserId, "审批", "Task", id, $"审批任务: {task.Name}");
        }
    }

    public void UpdateStatus(int id, TaskStatus status, string updateBy, int currentUserId)
    {
        // 审批走 TaskApprove，其余状态变更走 TaskManage
        _authz?.Require(status == TaskStatus.Approved ? AuthorizationAction.TaskApprove : AuthorizationAction.TaskManage);
        var task = _repository.GetById(id);
        if (task != null)
        {
            task.Status = status;
            task.UpdateBy = updateBy;
            task.UpdateAt = DateTime.Now;
            _repository.Update(task);
            _auditLog?.Log(currentUserId, "状态变更", "Task", id, $"任务状态变更为: {status}");
        }
    }

    public void Delete(int id, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.TaskManage);
        var task = _repository.GetById(id);
        if (task != null)
        {
            task.IsDeleted = true;
            task.UpdateBy = updateBy;
            task.UpdateAt = DateTime.Now;
            _repository.Update(task);
            _auditLog?.Log(currentUserId, "删除", "Task", id, $"删除任务ID: {id}");
        }
    }

    public IPagedResult<ProdTask> Query(PagedQuery query) => _repository.Query(query);
    public IPagedResult<ProdTask> Query(TaskQuery query) => _repository.Query(query);
}