using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Entities.Queries;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.Views.Detection
{
    public partial class DetectionHistoryView : UserControl
    {
        private readonly string _dbPath;
        private DetectionRecordQuery _currentQuery = new DetectionRecordQuery { PageIndex = 0, PageSize = 20 };
        private IPagedResult<DetectionRecord> _currentResult;

        public DetectionHistoryView()
        {
            // 在 InitializeComponent 之前赋值，防止 XAML 解析阶段的事件回调访问到未初始化的路径
            _dbPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Data",
                "tripledetection.db");

            InitializeComponent();

            BindData();
        }

        private void BindData()
        {
            _currentResult = QueryRecords(_currentQuery);
            dgDetectionRecords.ItemsSource = _currentResult.Items.ToList();
            UpdatePagination();
        }

        private IPagedResult<DetectionRecord> QueryRecords(DetectionRecordQuery query)
        {
            var records = new List<DetectionRecord>();
            long total = 0;

            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                var where = BuildWhereClause(query);
                var orderCol = string.IsNullOrEmpty(query.SortBy) ? "DetectionTime" : query.SortBy;
                var orderDir = query.SortDescending ? "DESC" : "ASC";

                // COUNT
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT COUNT(*) FROM DetectionRecords WHERE {where}";
                    AddQueryParams(cmd, query);
                    total = Convert.ToInt64(cmd.ExecuteScalar());
                }

                // Data
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT * FROM DetectionRecords WHERE {where} ORDER BY {orderCol} {orderDir} LIMIT @limit OFFSET @offset";
                    AddQueryParams(cmd, query);
                    cmd.Parameters.AddWithValue("@limit", query.PageSize);
                    cmd.Parameters.AddWithValue("@offset", query.PageIndex * query.PageSize);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            records.Add(ReadRecord(reader));
                        }
                    }
                }
            }

            return new PagedResult<DetectionRecord>(records, (int)total, query.PageIndex, query.PageSize);
        }

        private string BuildWhereClause(DetectionRecordQuery query)
        {
            var conditions = new List<string> { "IsDeleted=0" };
            if (query.StartDate.HasValue)
                conditions.Add("DetectionTime>=@startDate");
            if (query.EndDate.HasValue)
                conditions.Add("DetectionTime<=@endDate");
            if (query.TaskId.HasValue)
                conditions.Add("TaskId=@taskId");
            if (query.ProductId.HasValue)
                conditions.Add("ProductId=@productId");
            if (!string.IsNullOrEmpty(query.BatchNumber))
                conditions.Add("BatchNumber LIKE @batchNumber");
            if (query.IsOK.HasValue)
                conditions.Add("IsOK=@isOK");
            return string.Join(" AND ", conditions);
        }

        private void AddQueryParams(SQLiteCommand cmd, DetectionRecordQuery query)
        {
            if (query.StartDate.HasValue)
                cmd.Parameters.AddWithValue("@startDate", query.StartDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (query.EndDate.HasValue)
                cmd.Parameters.AddWithValue("@endDate", query.EndDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (query.TaskId.HasValue)
                cmd.Parameters.AddWithValue("@taskId", query.TaskId.Value);
            if (query.ProductId.HasValue)
                cmd.Parameters.AddWithValue("@productId", query.ProductId.Value);
            if (!string.IsNullOrEmpty(query.BatchNumber))
                cmd.Parameters.AddWithValue("@batchNumber", $"%{query.BatchNumber}%");
            if (query.IsOK.HasValue)
                cmd.Parameters.AddWithValue("@isOK", query.IsOK.Value ? 1 : 0);
        }

        private DetectionRecord ReadRecord(SQLiteDataReader reader)
        {
            return new DetectionRecord
            {
                Id = Convert.ToInt32(reader["Id"]),
                TaskId = Convert.ToInt32(reader["TaskId"]),
                ProductId = Convert.ToInt32(reader["ProductId"]),
                BatchNumber = reader["BatchNumber"] == DBNull.Value ? null : reader["BatchNumber"].ToString(),
                IsOK = Convert.ToInt32(reader["IsOK"]) == 1,
                ProductionDate = reader["ProductionDate"] == DBNull.Value ? null : reader["ProductionDate"].ToString(),
                ExpirationDate = reader["ExpirationDate"] == DBNull.Value ? null : reader["ExpirationDate"].ToString(),
                ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"].ToString(),
                ElapsedMs = Convert.ToInt64(reader["ElapsedMs"]),
                DetectionTime = DateTime.Parse(reader["DetectionTime"].ToString()),
                CreateAt = DateTime.Parse(reader["CreateAt"].ToString()),
                UpdateAt = DateTime.Parse(reader["UpdateAt"].ToString()),
                IsDeleted = Convert.ToInt32(reader["IsDeleted"]) == 1
            };
        }

        private void UpdatePagination()
        {
            txtTotalCount.Text = _currentResult.TotalCount.ToString();
            txtPageIndex.Text = (_currentResult.PageIndex + 1).ToString();
            btnPrev.IsEnabled = _currentResult.HasPreviousPage;
            btnNext.IsEnabled = _currentResult.HasNextPage;
        }

        private DetectionRecordQuery BuildQuery()
        {
            var query = new DetectionRecordQuery
            {
                PageIndex = _currentQuery.PageIndex,
                PageSize = _currentQuery.PageSize,
                SortBy = "DetectionTime",
                SortDescending = true
            };

            if (dpStartDate.SelectedDate.HasValue)
                query.StartDate = dpStartDate.SelectedDate.Value;
            if (dpEndDate.SelectedDate.HasValue)
                query.EndDate = dpEndDate.SelectedDate.Value.AddDays(1).AddSeconds(-1);
            if (!string.IsNullOrWhiteSpace(txtBatchNumber.Text))
                query.BatchNumber = txtBatchNumber.Text;
            if (cmbResult.SelectedItem is ComboBoxItem item && !string.IsNullOrEmpty(item.Tag?.ToString()))
                query.IsOK = bool.Parse(item.Tag.ToString());

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

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var query = BuildQuery();
            query.PageIndex = 0;
            query.PageSize = int.MaxValue;
            var allData = ExportRecords(query);

            var dialog = new SaveFileDialog();
            dialog.Filter = "CSV文件|*.csv";
            dialog.FileName = $"检测记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            if (dialog.ShowDialog() == true)
            {
                using (var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("检测时间,批次号,结果,生产日期,有效期至,耗时(ms),图像路径");
                    foreach (var r in allData)
                    {
                        writer.WriteLine($"\"{r.DetectionTime:yyyy-MM-dd HH:mm:ss}\",\"{r.BatchNumber}\",\"{(r.IsOK ? "OK" : "NG")}\",\"{r.ProductionDate}\",\"{r.ExpirationDate}\",\"{r.ElapsedMs}\",\"{r.ImagePath}\"");
                    }
                }
                MessageBox.Show($"导出成功，共 {allData.Count()} 条记录", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private IEnumerable<DetectionRecord> ExportRecords(DetectionRecordQuery query)
        {
            var records = new List<DetectionRecord>();
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                conn.Open();
                var where = BuildWhereClause(query);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT * FROM DetectionRecords WHERE {where} ORDER BY DetectionTime DESC";
                    AddQueryParams(cmd, query);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            records.Add(ReadRecord(reader));
                        }
                    }
                }
            }
            return records;
        }
    }
}