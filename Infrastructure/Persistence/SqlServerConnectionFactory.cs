// SQL Server 连接工厂（预留功能，当前未启用）。
// 启用方式：在 csproj 中添加 <PackageReference Include="Microsoft.Data.SqlClient" />，
// 并定义编译常量 USE_SQLSERVER（<DefineConstants>$(DefineConstants);USE_SQLSERVER</DefineConstants>）。
#if USE_SQLSERVER
using System;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace TripleDetection.Infrastructure.Persistence;

public class SqlServerConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlServerConnectionFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public DbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}
    #endif