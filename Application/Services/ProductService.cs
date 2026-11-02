using System;
using System.Collections.Generic;
using TripleDetection.Application.Security;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Domain.Repositories;

namespace TripleDetection.Application.Services;

public class ProductService : IProductService
{
    private readonly IRepository<Product> _repository;
    private readonly IAuditLogService _auditLog;
    private readonly IAuthorizationService? _authz;

    public ProductService(IRepository<Product> repository, IAuditLogService auditLog, IAuthorizationService? authz = null)
    {
        _repository = repository;
        _auditLog = auditLog;
        _authz = authz;
    }

    public IEnumerable<Product> GetAll() => _repository.GetAll();
    public Product GetById(int id) => _repository.GetById(id);

    public void Create(Product product, string createBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.ProductManage);
        product.CreateBy = createBy;
        product.UpdateBy = createBy;
        product.CreateAt = DateTime.Now;
        product.UpdateAt = DateTime.Now;
        product.IsDeleted = false;
        _repository.Add(product);
        _auditLog?.Log(currentUserId, "创建", "Product", product.Id, $"创建产品: {product.Name}");
    }

    public void Update(Product product, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.ProductManage);
        product.UpdateBy = updateBy;
        product.UpdateAt = DateTime.Now;
        _repository.Update(product);
        _auditLog?.Log(currentUserId, "修改", "Product", product.Id, $"修改产品: {product.Name}");
    }

    public void Delete(int id, string updateBy, int currentUserId)
    {
        _authz?.Require(AuthorizationAction.ProductManage);
        var product = _repository.GetById(id);
        if (product != null)
        {
            product.IsDeleted = true;
            product.UpdateBy = updateBy;
            product.UpdateAt = DateTime.Now;
            _repository.Update(product);
            _auditLog?.Log(currentUserId, "删除", "Product", id, $"删除产品ID: {id}");
        }
    }

    public IPagedResult<Product> Query(PagedQuery query) => _repository.Query(query);
    public IPagedResult<Product> Query(ProductQuery query) => _repository.Query(query);
}