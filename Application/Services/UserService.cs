using System;
using System.Collections.Generic;
using System.Linq;
using TripleDetection.Application.Security;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Domain.Repositories;

namespace TripleDetection.Application.Services;

public class UserService : IUserService
{
    private readonly IRepository<User> _repository;
    private readonly IAuditLogService _auditLog;
    private readonly IPasswordHashService _hashService;
    private readonly IAuthorizationService? _authz;

    public UserService(IRepository<User> repository, IAuditLogService auditLog, IPasswordHashService hashService, IAuthorizationService? authz = null)
    {
        _repository = repository;
        _auditLog = auditLog;
        _hashService = hashService ?? new PasswordHashService();
        _authz = authz;
    }

    public User Authenticate(string username, string password)
    {
        var user = _repository.Find(u => u.Username == username && u.IsEnabled && !u.IsLocked).FirstOrDefault();
        if (user == null) return null;

        bool authenticated = false;
        bool needsMigration = false;

        if (!string.IsNullOrEmpty(user.PasswordHash) && !string.IsNullOrEmpty(user.PasswordSalt))
        {
            authenticated = _hashService.VerifyPassword(password, user.PasswordSalt, user.PasswordHash);
        }
        else
        {
            authenticated = user.Password == password;
            needsMigration = authenticated;
        }

        if (!authenticated) return null;

        if (needsMigration)
        {
            user.PasswordSalt = _hashService.GenerateSalt();
            user.PasswordHash = _hashService.ComputeHash(user.PasswordSalt, password);
            _repository.Update(user);
        }

        _auditLog?.Log(user.Id, "登录", "User", user.Id, $"用户登录: {username}");
        return user;
    }

    public IEnumerable<User> GetAll() => _repository.GetAll();
    public User GetByUsername(string username) => _repository.Find(u => u.Username == username).FirstOrDefault();
    public User GetById(int id) => _repository.GetById(id);

    public void Create(User user, string createBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.UserManage);
        if (string.IsNullOrEmpty(user.Username)) throw new ArgumentException("Username is required");
        var existing = GetByUsername(user.Username);
        if (existing != null) throw new InvalidOperationException($"User '{user.Username}' already exists");
        user.CreateBy = createBy;
        _repository.Add(user);
        _auditLog?.Log(currentUserId, "创建", "User", user.Id, $"创建用户: {user.Username}");
    }

    public void Update(User user, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.UserManage);
        var existing = GetByUsername(user.Username);
        if (existing == null) throw new InvalidOperationException($"User '{user.Username}' not found");
        user.Id = existing.Id;
        user.UpdateBy = updateBy;
        _repository.Update(user);
        _auditLog?.Log(currentUserId, "修改", "User", user.Id, $"修改用户: {user.Username}");
    }

    public void Delete(string username, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.UserManage);
        var existing = GetByUsername(username);
        if (existing == null) throw new InvalidOperationException($"User '{username}' not found");
        _repository.Delete(existing.Id);
        _auditLog?.Log(currentUserId, "删除", "User", existing.Id, $"删除用户: {username}");
    }

    public void Enable(string username, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.UserManage);
        var user = GetByUsername(username);
        if (user == null) throw new InvalidOperationException($"User '{username}' not found");
        user.IsEnabled = true;
        user.UpdateBy = updateBy;
        _repository.Update(user);
        _auditLog?.Log(currentUserId, "启用", "User", user.Id, $"启用用户: {username}");
    }

    public void Disable(string username, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.UserManage);
        var user = GetByUsername(username);
        if (user == null) throw new InvalidOperationException($"User '{username}' not found");
        user.IsEnabled = false;
        user.UpdateBy = updateBy;
        _repository.Update(user);
        _auditLog?.Log(currentUserId, "禁用", "User", user.Id, $"禁用用户: {username}");
    }

    public void Lock(string username, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.UserManage);
        var user = GetByUsername(username);
        if (user == null) throw new InvalidOperationException($"User '{username}' not found");
        user.IsLocked = true;
        user.UpdateBy = updateBy;
        _repository.Update(user);
        _auditLog?.Log(currentUserId, "锁定", "User", user.Id, $"锁定用户: {username}");
    }

    public void Unlock(string username, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.UserManage);
        var user = GetByUsername(username);
        if (user == null) throw new InvalidOperationException($"User '{username}' not found");
        user.IsLocked = false;
        user.UpdateBy = updateBy;
        _repository.Update(user);
        _auditLog?.Log(currentUserId, "解锁", "User", user.Id, $"解锁用户: {username}");
    }

    public IPagedResult<User> Query(UserQuery query) => _repository.Query(query);
}