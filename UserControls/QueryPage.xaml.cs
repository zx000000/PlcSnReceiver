using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PlcSnReceiver.Models;
using PlcSnReceiver.Services;

namespace PlcSnReceiver.UserControls;

public partial class QueryPage : UserControl
{
    /// <summary>通道信息：名称 + 独立库服务 + 配置（数据项随通道）</summary>
    private class ChannelInfo
    {
        public string Name = "";
        public SqliteDbService Db = null!;
        public ChannelConfig Channel = null!;
    }

    private readonly List<ChannelInfo> _channels = new();
    private AppConfig? _cfg;
    private List<PlcRecord> _lastResult = new();

    public ObservableCollection<PlcRecord> QueryResultList { get; } = new();

    public QueryPage()
    {
        InitializeComponent();
        DgQueryResult.ItemsSource = QueryResultList;
        DpStart.SelectedDate = DateTime.Now.AddDays(-1);
        DpEnd.SelectedDate = DateTime.Now;

        CboChannel.SelectionChanged += (_, _) => OnChannelChanged();
        BtnQuery.Click += (_, _) => DoQuery();
        BtnReset.Click += (_, _) => DoReset();
        BtnExportQuery.Click += (_, _) => ExportCsv();
    }

    /// <summary>由 MainWindow 注入所有通道的数据库服务。</summary>
    public void BindService(List<SqliteDbService> dbs, AppConfig cfg)
    {
        _cfg = cfg;
        RebuildChannels(dbs, cfg);
    }

    /// <summary>配置保存后重建通道下拉与列。</summary>
    public void UpdateConfig(List<SqliteDbService> dbs, AppConfig cfg)
    {
        _cfg = cfg;
        RebuildChannels(dbs, cfg);
    }

    private void RebuildChannels(List<SqliteDbService> dbs, AppConfig cfg)
    {
        _channels.Clear();
        for (int i = 0; i < cfg.Channels.Count && i < dbs.Count; i++)
        {
            _channels.Add(new ChannelInfo
            {
                Name = cfg.Channels[i].Name,
                Db = dbs[i],
                Channel = cfg.Channels[i]
            });
        }

        CboChannel.Items.Clear();
        foreach (var c in _channels)
            CboChannel.Items.Add(c.Name);
        if (_channels.Count > 0)
            CboChannel.SelectedIndex = 0;

        BuildColumns();
    }

    private ChannelInfo? Current =>
        CboChannel.SelectedIndex >= 0 && CboChannel.SelectedIndex < _channels.Count
            ? _channels[CboChannel.SelectedIndex]
            : null;

    /// <summary>切换通道时重建字段下拉框与结果列（各通道数据项可能不同）。</summary>
    private void OnChannelChanged()
    {
        var cur = Current;

        CboField.Items.Clear();
        if (cur != null)
        {
            foreach (var di in cur.Channel.DataItems)
                CboField.Items.Add(di.FieldName);
            if (CboField.Items.Count > 0)
                CboField.SelectedIndex = 0;
        }

        QueryResultList.Clear();
        _lastResult.Clear();
        TxtCount.Text = "共 0 条";
        BuildColumns();
    }

    /// <summary>根据当前选中通道的数据项动态生成查询结果列。</summary>
    public void BuildColumns()
    {
        DgQueryResult.Columns.Clear();
        DgQueryResult.Columns.Add(new DataGridTextColumn { Header = "序号", Width = 55, Binding = new Binding("Id") });
        DgQueryResult.Columns.Add(new DataGridTextColumn { Header = "接收时间", Width = 175, Binding = new Binding("ReceiveTime") { StringFormat = "yyyy-MM-dd HH:mm:ss" } });
        if (Current != null)
        {
            foreach (var di in Current.Channel.DataItems)
            {
                DgQueryResult.Columns.Add(new DataGridTextColumn
                {
                    Header = di.FieldName,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    Binding = new Binding($"FieldValues[{di.FieldName}]")
                });
            }
        }
        DgQueryResult.Columns.Add(new DataGridTextColumn { Header = "备注", Width = 70, Binding = new Binding("Status") });
    }

    private void DoQuery()
    {
        var cur = Current;
        if (cur == null) return;

        var start = DpStart.SelectedDate ?? DateTime.Now.AddDays(-1);
        var end = DpEnd.SelectedDate ?? DateTime.Now;

        // 时间范围取整天：起始当天00:00，结束当天23:59:59
        var t1 = start.Date;
        var t2 = end.Date.AddDays(1).AddSeconds(-1);

        string? field = CboField.SelectedItem?.ToString();
        var keyword = string.IsNullOrWhiteSpace(TxtKeyword.Text) ? null : TxtKeyword.Text.Trim();

        try
        {
            _lastResult = cur.Db.QueryByTime(t1, t2, field, keyword);
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
        var cur = Current;
        if (cur == null) return;
        if (_lastResult.Count == 0)
        {
            MessageBox.Show("当前查询结果为空，无可导出数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"查询结果_{cur.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            CsvExporter.Export(_lastResult, cur.Channel.DataItems, dlg.FileName);
            MessageBox.Show($"导出成功：{dlg.FileName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
