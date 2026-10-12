using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using PlcSnReceiver.Models;
using PlcSnReceiver.Services;

namespace PlcSnReceiver.UserControls;

public partial class MonitorPage : UserControl
{
    private readonly System.Collections.Generic.List<string> _logLines = new();

    public ObservableCollection<PlcRecord> RealTimeList { get; } = new();

    private FinsUdpService? _fins;
    private SqliteDbService? _db;
    private AppConfig? _cfg;

    public MonitorPage()
    {
        InitializeComponent();
        DgRecords.ItemsSource = RealTimeList;

        BtnStart.Click += (_, _) => _ = StartAsync();
        BtnStop.Click += (_, _) => _ = StopAsync();
        BtnClear.Click += (_, _) => OnClearList();
        BtnExportCsv.Click += (_, _) => ExportCsv();
    }

    /// <summary>由 MainWindow 注入服务，并重建动态列。</summary>
    public void BindService(FinsUdpService fins, SqliteDbService db, AppConfig cfg)
    {
        _fins = fins;
        _db = db;
        _cfg = cfg;

        _fins.OnLogReceived += OnLog;
        _fins.OnNewRecordReceived += OnNewRecord;
        _fins.OnConnectionChanged += OnConnChanged;

        BuildDynamicColumns();
        RefreshStatus();
    }

    /// <summary>配置保存后刷新引用，并按新上限立即裁剪现有列表与日志。</summary>
    public void UpdateConfig(AppConfig cfg)
    {
        _cfg = cfg;

        // 按新上限立即裁剪现有列表并重排序号
        var maxRows = cfg.MaxListRows > 0 ? cfg.MaxListRows : 1000;
        while (RealTimeList.Count > maxRows)
            RealTimeList.RemoveAt(0);
        for (int i = 0; i < RealTimeList.Count; i++)
            RealTimeList[i].Id = i + 1;

        // 按新上限裁剪日志缓冲
        var maxLines = cfg.MaxLogLines > 0 ? cfg.MaxLogLines : 500;
        if (_logLines.Count > maxLines)
            _logLines.RemoveRange(0, _logLines.Count - maxLines);

        BuildDynamicColumns();
    }

    /// <summary>根据配置的数据项动态生成 DataGrid 列。</summary>
    public void BuildDynamicColumns()
    {
        DgRecords.Columns.Clear();
        DgRecords.Columns.Add(new DataGridTextColumn
        {
            Header = "序号",
            Width = 55,
            Binding = new Binding("Id")
        });
        DgRecords.Columns.Add(new DataGridTextColumn
        {
            Header = "接收时间",
            Width = 175,
            Binding = new Binding("ReceiveTime") { StringFormat = "yyyy-MM-dd HH:mm:ss" }
        });
        if (_cfg != null)
        {
            foreach (var di in _cfg.DataItems)
            {
                DgRecords.Columns.Add(new DataGridTextColumn
                {
                    Header = di.FieldName,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    Binding = new Binding($"FieldValues[{di.FieldName}]")
                });
            }
        }
        DgRecords.Columns.Add(new DataGridTextColumn
        {
            Header = "状态标记",
            Width = 70,
            Binding = new Binding("Status")
        });
    }

    private void OnLog(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            var line = $"{DateTime.Now:HH:mm:ss} {msg}";
            _logLines.Add(line);
            TxtLog.AppendText(line + Environment.NewLine);

            // 超出上限时一次性裁剪为最近 MaxLogLines 行，避免 TextBox 无限增长卡顿
            var maxLines = _cfg?.MaxLogLines > 0 ? _cfg.MaxLogLines : 500;
            if (_logLines.Count > maxLines)
            {
                _logLines.RemoveRange(0, _logLines.Count - maxLines);
                TxtLog.Text = string.Join(Environment.NewLine, _logLines);
            }
            TxtLog.ScrollToEnd();
            RefreshStatus();
        });
    }

    private void OnNewRecord(PlcRecord rec)
    {
        Dispatcher.Invoke(() =>
        {
            // 序号：按当前列表行数递增，清空列表后自动重新从 1 开始
            rec.Id = RealTimeList.Count + 1;
            RealTimeList.Add(rec);

            // 数量上限：超出移除最旧行并重排序号（仅UI显示限制，已入库数据不受影响）
            var maxRows = _cfg?.MaxListRows > 0 ? _cfg.MaxListRows : 1000;
            if (RealTimeList.Count > maxRows)
            {
                RealTimeList.RemoveAt(0);
                for (int i = 0; i < RealTimeList.Count; i++)
                    RealTimeList[i].Id = i + 1;
            }

            // 自动保存到 SQLite（历史查询页读取这里的数据）
            if (_db != null && _cfg is { AutoSaveToDb: true })
            {
                try { _db.InsertRecord(rec); }
                catch (Exception ex) { OnLog($"⚠️ 入库失败：{ex.Message}"); }
            }
        });
    }

    private void OnClearList()
    {
        if (RealTimeList.Count == 0)
        {
            MessageBox.Show("当前列表为空，无需清空。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var r = MessageBox.Show($"确定清空当前 {RealTimeList.Count} 条记录吗？\n（仅清空监控页显示，数据库历史记录不受影响）",
            "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
            RealTimeList.Clear();
    }

    private void OnConnChanged()
    {
        Dispatcher.Invoke(RefreshStatus);
    }

    private void RefreshStatus()
    {
        if (_fins == null) return;
        var connected = _fins.IsConnected;
        DotState.Fill = connected ? Brushes.LimeGreen : Brushes.Red;
        TxtConnState.Text = connected ? "已连接" : "未连接";
        TxtConnState.Foreground = connected ? Brushes.Green : Brushes.Red;
        TxtPlcIp.Text = _cfg == null ? "--" : $"{_cfg.FinsUdp.PlcIp}:{_cfg.FinsUdp.PlcFinsPort}";
        TxtCnt.Text = $"{_fins.SessionReceiveCount} / {_fins.TotalReceiveCount}";
    }

    private async Task StartAsync()
    {
        if (_fins == null) return;
        await _fins.StartAsync();
    }

    private async Task StopAsync()
    {
        if (_fins == null) return;
        await _fins.StopAsync();
    }

    private void ExportCsv()
    {
        if (_fins == null || _cfg == null) return;
        if (RealTimeList.Count == 0)
        {
            MessageBox.Show("当前列表为空，无可导出数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"接收数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            CsvExporter.Export(RealTimeList, _cfg.DataItems, dlg.FileName);
            MessageBox.Show($"导出成功：{dlg.FileName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
