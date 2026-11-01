// 全局 using 指令。
// 项目经历多次 DDD 命名空间重构，大量文件缺失 using 指令导致编译失败，
// 在此统一补充常用命名空间，避免逐文件修改。
//
// 注意：刻意不包含 TripleDetection.Domain.Entities —— 其中类型与
// System.Threading.Tasks 等常用类型存在命名冲突风险，需要实体类型的文件
// 应显式 using TripleDetection.Domain.Entities;

global using System;
global using System.Collections.Generic;
global using System.Linq;
// SessionManager（当前登录用户上下文）
global using TripleDetection.Domain;
global using Microsoft.Data.Sqlite;
// 注意：Prism 9 中 Prism.Regions 命名空间已重命名为 Prism.Navigation.Regions
global using Prism.Navigation.Regions;
global using TripleDetection.Domain.Entities.Queries;
global using TripleDetection.Domain.Enums;
global using TripleDetection.Domain.Repositories;
global using TripleDetection.Infrastructure.Persistence;
global using TripleDetection.Presentation.Models;
