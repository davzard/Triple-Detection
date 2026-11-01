using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Entities.Queries;
using TripleDetection.Domain.Repositories;

namespace TripleDetection.Presentation.Views.Audit
{
    public partial class AuditLogView : UserControl
    {
        private readonly string _dbPath;
        private AuditLogQuery _currentQuery = new AuditLogQuery { PageIndex = 0, PageSize = 20 };
        private IPagedResult<AuditLog> _currentResult;

        public AuditLogView()
        {
            // 必须在 InitializeComponent 之前赋值：XAML 中 cmbPageSize 的 SelectedIndex="0"
            // 会在解析阶段触发 SelectionChanged -> BindData()，此时 _dbPath 必须已就绪
            _dbPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Data",
                "tripledetection.db");

            InitializeComponent();

            LoadUsers();
            BindData();
        }

        private void LoadUsers()
        {
            cmbUser.Items.Clear();
            cmbUser.Items.Add(new ComboBoxItem { Content = "全部", Tag = "" });
            var users = GetAllUsers();
            foreach (var user in users)
            {
                cmbUser.Items.Add(new ComboBoxItem { Content = user.RealName ?? user.Username, Tag = user.Id.ToString() });
            }
            cmbUser.SelectedIndex = 0;
        }

        private List<User> GetAllUsers()
        {
            var users = new List<User>();
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM Users WHERE IsDeleted=0 ORDER BY Id";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            users.Add(ReadUser(reader));
                        }
                    }
                }
            }
            return users;
        }

        private User ReadUser(IDataRecord reader)
        {
            return new User
            {
                Id = Convert.ToInt32(reader["Id"]),
                Username = reader["Username"].ToString(),
                RealName = reader["RealName"] == DBNull.Value ? null : reader["RealName"].ToString(),
                Password = reader["Password"].ToString(),
                Role = reader["Role"].ToString(),
                IsEnabled = Convert.ToInt32(reader["IsEnabled"]) == 1,
                IsLocked = Convert.ToInt32(reader["IsLocked"]) == 1,
                LastLoginAt = reader["LastLoginAt"] == DBNull.Value ? (DateTime?)null : DateTime.Parse(reader["LastLoginAt"].ToString()),
                CreateAt = DateTime.Parse(reader["CreateAt"].ToString()),
                UpdateAt = DateTime.Parse(reader["UpdateAt"].ToString()),
                IsDeleted = Convert.ToInt32(reader["IsDeleted"]) == 1
            };
        }

        private void BindData()
        {
            _currentResult = QueryLogs(_currentQuery);
            dgAuditLog.ItemsSource = _currentResult.Items.ToList();
            UpdatePagination();
        }

        private IPagedResult<AuditLog> QueryLogs(AuditLogQuery query)
        {
            var logs = new List<AuditLog>();
            long total = 0;

            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                var where = BuildWhereClause(query);
                var orderCol = string.IsNullOrEmpty(query.SortBy) ? "CreateAt" : query.SortBy;
                var orderDir = query.SortDescending ? "DESC" : "ASC";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT COUNT(*) FROM AuditLogs WHERE {where}";
                    AddQueryParams(cmd, query);
                    total = Convert.ToInt64(cmd.ExecuteScalar());
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT * FROM AuditLogs WHERE {where} ORDER BY {orderCol} {orderDir} LIMIT @limit OFFSET @offset";
                    AddQueryParams(cmd, query);
                    cmd.Parameters.AddWithValue("@limit", query.PageSize);
                    cmd.Parameters.AddWithValue("@offset", query.PageIndex * query.PageSize);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            logs.Add(ReadAuditLog(reader));
                        }
                    }
                }
            }

            return new PagedResult<AuditLog>(logs, (int)total, query.PageIndex, query.PageSize);
        }

        private string BuildWhereClause(AuditLogQuery query)
        {
            var conditions = new List<string> { "IsDeleted=0" };
            if (query.StartDate.HasValue)
                conditions.Add("CreateAt>=@startDate");
            if (query.EndDate.HasValue)
                conditions.Add("CreateAt<=@endDate");
            if (query.UserId.HasValue)
                conditions.Add("UserId=@userId");
            if (!string.IsNullOrEmpty(query.Action))
                conditions.Add("Action=@action");
            if (!string.IsNullOrEmpty(query.ObjectType))
                conditions.Add("ObjectType=@objectType");
            if (!string.IsNullOrEmpty(query.Keyword))
                conditions.Add("Details LIKE @keyword");
            if (!string.IsNullOrEmpty(query.IpAddress))
                conditions.Add("IpAddress=@ipAddress");
            return string.Join(" AND ", conditions);
        }

        private void AddQueryParams(SQLiteCommand cmd, AuditLogQuery query)
        {
            if (query.StartDate.HasValue)
                cmd.Parameters.AddWithValue("@startDate", query.StartDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (query.EndDate.HasValue)
                cmd.Parameters.AddWithValue("@endDate", query.EndDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (query.UserId.HasValue)
                cmd.Parameters.AddWithValue("@userId", query.UserId.Value);
            if (!string.IsNullOrEmpty(query.Action))
                cmd.Parameters.AddWithValue("@action", query.Action);
            if (!string.IsNullOrEmpty(query.ObjectType))
                cmd.Parameters.AddWithValue("@objectType", query.ObjectType);
            if (!string.IsNullOrEmpty(query.Keyword))
                cmd.Parameters.AddWithValue("@keyword", $"%{query.Keyword}%");
            if (!string.IsNullOrEmpty(query.IpAddress))
                cmd.Parameters.AddWithValue("@ipAddress", query.IpAddress);
        }

        private AuditLog ReadAuditLog(IDataRecord reader)
        {
            return new AuditLog
            {
                Id = Convert.ToInt32(reader["Id"]),
                UserId = Convert.ToInt32(reader["UserId"]),
                UserName = reader["UserName"].ToString(),
                Action = reader["Action"].ToString(),
                ObjectType = reader["ObjectType"].ToString(),
                ObjectId = Convert.ToInt32(reader["ObjectId"]),
                Details = reader["Details"] == DBNull.Value ? null : reader["Details"].ToString(),
                IpAddress = reader["IpAddress"] == DBNull.Value ? null : reader["IpAddress"].ToString(),
                CreateAt = DateTime.Parse(reader["CreateAt"].ToString()),
                UpdateAt = DateTime.Parse(reader["UpdateAt"].ToString()),
                IsDeleted = Convert.ToInt32(reader["IsDeleted"]) == 1
            };
        }

        private void UpdatePagination()
        {
            txtTotalCount.Text = _currentResult.TotalCount.ToString();
            txtPageIndex.Text = (_currentResult.PageIndex + 1).ToString();
            txtTotalPages.Text = _currentResult.TotalPages.ToString();
            btnPrev.IsEnabled = _currentResult.HasPreviousPage;
            btnNext.IsEnabled = _currentResult.HasNextPage;
        }

        private AuditLogQuery BuildQuery()
        {
            var query = new AuditLogQuery
            {
                PageIndex = _currentQuery.PageIndex,
                PageSize = _currentQuery.PageSize,
                SortBy = "CreateAt",
                SortDescending = true
            };

            if (dpStartDate.SelectedDate.HasValue)
                query.StartDate = dpStartDate.SelectedDate.Value;
            if (dpEndDate.SelectedDate.HasValue)
                query.EndDate = dpEndDate.SelectedDate.Value.AddDays(1).AddSeconds(-1);

            if (cmbUser.SelectedItem is ComboBoxItem userItem && !string.IsNullOrEmpty(userItem.Tag?.ToString()))
                query.UserId = int.Parse(userItem.Tag.ToString());

            if (cmbAction.SelectedItem is ComboBoxItem actionItem && !string.IsNullOrEmpty(actionItem.Tag?.ToString()))
                query.Action = actionItem.Tag.ToString();

            if (cmbObjectType.SelectedItem is ComboBoxItem objItem && !string.IsNullOrEmpty(objItem.Tag?.ToString()))
                query.ObjectType = objItem.Tag.ToString();

            query.Keyword = txtKeyword.Text;

            return query;
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            _currentQuery = BuildQuery();
            _currentQuery.PageIndex = 0;
            BindData();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            _currentQuery.PageIndex--;
            BindData();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _currentQuery.PageIndex++;
            BindData();
        }

        private void CmbPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPageSize.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _currentQuery.PageSize = int.Parse(item.Tag.ToString());
                _currentQuery.PageIndex = 0;
                BindData();
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var query = BuildQuery();
            query.PageIndex = 0;
            query.PageSize = int.MaxValue;
            var allData = ExportLogs(query);

            var dialog = new SaveFileDialog();
            dialog.Filter = "CSV文件|*.csv";
            dialog.FileName = $"审计日志_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            if (dialog.ShowDialog() == true)
            {
                using (var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("时间,用户,操作,对象类型,对象ID,详情,IP地址");
                    foreach (var log in allData)
                    {
                        writer.WriteLine($"\"{log.CreateAt:yyyy-MM-dd HH:mm:ss}\",\"{log.UserName}\",\"{log.Action}\",\"{log.ObjectType}\",\"{log.ObjectId}\",\"{log.Details}\",\"{log.IpAddress}\"");
                    }
                }
                MessageBox.Show($"导出成功，共 {allData.Count()} 条记录", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private IEnumerable<AuditLog> ExportLogs(AuditLogQuery query)
        {
            var logs = new List<AuditLog>();
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                var where = BuildWhereClause(query);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT * FROM AuditLogs WHERE {where} ORDER BY CreateAt DESC";
                    AddQueryParams(cmd, query);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            logs.Add(ReadAuditLog(reader));
                        }
                    }
                }
            }
            return logs;
        }
    }
}