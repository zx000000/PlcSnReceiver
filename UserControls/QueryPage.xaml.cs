using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PlcSnReceiver.Models;
using PlcSnReceiver.Services;

namespace PlcSnReceiver.UserControls;

public partial class QueryPage : UserControl
{
    public ObservableCollection<PlcRecord> QueryResultList { get; } = new();

    private SqliteDbService? _db;
    private AppConfig? _cfg;
    private System.Collections.Generic.List<PlcRecord> _lastResult = new();

    public QueryPage()
    {
        InitializeComponent();
        DgQueryResult.ItemsSource = QueryResultList;
        DpStart.SelectedDate = DateTime.Now.AddDays(-1);
        DpEnd.SelectedDate = DateTime.Now;

        BtnQuery.Click += (_, _) => DoQuery();
        BtnReset.Click += (_, _) => DoReset();
        BtnExportQuery.Click += (_, _) => ExportCsv();
    }

    public void BindService(SqliteDbService db, AppConfig cfg)
    {
        _db = db;
        _cfg = cfg;

        // 重建字段下拉框（跟随配置的数据项）
        CboField.Items.Clear();
        foreach (var di in cfg.DataItems)
            CboField.Items.Add(di.FieldName);
        if (CboField.Items.Count > 0)
            CboField.SelectedIndex = 0;

        BuildColumns();
    }

    /// <summary>配置保存后刷新引用并重建列（数据项可能变化）。</summary>
    public void UpdateConfig(AppConfig cfg)
    {
        _cfg = cfg;
        CboField.Items.Clear();
        foreach (var di in cfg.DataItems)
            CboField.Items.Add(di.FieldName);
        if (CboField.Items.Count > 0)
            CboField.SelectedIndex = 0;
        BuildColumns();
    }

    /// <summary>根据配置动态生成查询结果列。</summary>
    public void BuildColumns()
    {
        DgQueryResult.Columns.Clear();
        DgQueryResult.Columns.Add(new DataGridTextColumn
        {
            Header = "序号",
            Width = 55,
            Binding = new Binding("Id")
        });
        DgQueryResult.Columns.Add(new DataGridTextColumn
        {
            Header = "接收时间",
            Width = 175,
            Binding = new Binding("ReceiveTime") { StringFormat = "yyyy-MM-dd HH:mm:ss" }
        });
        if (_cfg != null)
        {
            foreach (var di in _cfg.DataItems)
            {
                DgQueryResult.Columns.Add(new DataGridTextColumn
                {
                    Header = di.FieldName,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    Binding = new Binding($"FieldValues[{di.FieldName}]")
                });
            }
        }
        DgQueryResult.Columns.Add(new DataGridTextColumn
        {
            Header = "备注",
            Width = 70,
            Binding = new Binding("Status")
        });
    }

    private void DoQuery()
    {
        if (_db == null || _cfg == null) return;

        var start = DpStart.SelectedDate ?? DateTime.Now.AddDays(-1);
        var end = DpEnd.SelectedDate ?? DateTime.Now;

        // 时间范围取整天：起始当天00:00，结束当天23:59:59
        var t1 = start.Date;
        var t2 = end.Date.AddDays(1).AddSeconds(-1);

        string? field = CboField.SelectedItem?.ToString();
        var keyword = string.IsNullOrWhiteSpace(TxtKeyword.Text) ? null : TxtKeyword.Text.Trim();

        try
        {
            _lastResult = _db.QueryByTime(t1, t2, field, keyword);
            QueryResultList.Clear();
            foreach (var r in _lastResult)
                QueryResultList.Add(r);
            TxtCount.Text = $"共 {_lastResult.Count} 条";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"查询失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DoReset()
    {
        TxtKeyword.Clear();
        CboField.SelectedIndex = CboField.Items.Count > 0 ? 0 : -1;
        DpStart.SelectedDate = DateTime.Now.AddDays(-1);
        DpEnd.SelectedDate = DateTime.Now;
        QueryResultList.Clear();
        _lastResult.Clear();
        TxtCount.Text = "共 0 条";
    }

    private void ExportCsv()
    {
        if (_cfg == null) return;
        if (_lastResult.Count == 0)
        {
            MessageBox.Show("当前查询结果为空，无可导出数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"查询结果_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            CsvExporter.Export(_lastResult, _cfg.DataItems, dlg.FileName);
            MessageBox.Show($"导出成功：{dlg.FileName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
