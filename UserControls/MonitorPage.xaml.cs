using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PlcSnReceiver.Models;
using PlcSnReceiver.Services;

namespace PlcSnReceiver.UserControls;

public partial class MonitorPage : UserControl
{
    /// <summary>一个通道的运行视图：独立列表、日志与控件引用。</summary>
    private class ChannelVm
    {
        public string Name = "";
        public FinsUdpService? Service;
        public SqliteDbService? Db;
        public ChannelConfig? Channel;
        public ObservableCollection<PlcRecord> List = new();
        public List<string> LogLines = new();

        public DataGrid? Grid;
        public TextBox? Log;
        public Ellipse? Dot;
        public TextBlock? TxtConn, TxtPlc, TxtCnt, TxtRun;
        public Button? BtnStart, BtnStop;
        public Ellipse? OverviewDot;
        public TextBlock? OverviewText;
    }

    private readonly List<ChannelVm> _channels = new();
    private AppConfig? _cfg;

    public MonitorPage()
    {
        InitializeComponent();
        BtnStart.Click += (_, _) => _ = StartAllAsync();
        BtnStop.Click += (_, _) => _ = StopAllAsync();
        BtnClear.Click += (_, _) => OnClearCurrent();
        BtnExportCsv.Click += (_, _) => ExportCurrentCsv();
    }

    /// <summary>由 MainWindow 注入所有通道服务并构建页签。</summary>
    public void BindService(List<FinsUdpService> services, AppConfig cfg)
    {
        _cfg = cfg;
        BuildChannels(services);
    }

    /// <summary>配置保存后重建页签与通道服务（通道数量/参数可能变化）。</summary>
    public void UpdateConfig(List<FinsUdpService> services, AppConfig cfg)
    {
        _cfg = cfg;
        BuildChannels(services);
    }

    /// <summary>为每个通道创建页签并订阅事件（重建时先清空旧页签与事件订阅）。</summary>
    private void BuildChannels(List<FinsUdpService> services)
    {
        foreach (var ch in _channels)
        {
            if (ch.Service != null)
            {
                ch.Service.OnLogReceived -= (m) => { };
                ch.Service.OnNewRecordReceived -= (r) => { };
                ch.Service.OnConnectionChanged -= () => { };
            }
        }
        _channels.Clear();
        ChTab.Items.Clear();
        ChStatusBar.Children.Clear();

        var index = 0;
        foreach (var svc in services)
        {
            var vm = new ChannelVm
            {
                Name = svc.ChannelName,
                Service = svc,
                Channel = _cfg!.Channels.Count > index ? _cfg.Channels[index] : null
            };
            if (vm.Channel != null)
            {
                vm.Db = new SqliteDbService(vm.Channel.DbFile);
            }
            _channels.Add(vm);
            BuildTabItem(vm);
            BuildOverviewItem(vm);

            svc.OnLogReceived += msg => OnLog(vm, msg);
            svc.OnNewRecordReceived += rec => OnNewRecord(vm, rec);
            svc.OnConnectionChanged += () => Dispatcher.Invoke(() => RefreshStatus(vm));

            index++;
        }

        if (_channels.Count > 0)
            ChTab.SelectedIndex = 0;
    }

    /// <summary>在顶部「各通道状态一览」为该通道创建一个状态标签（圆点+名称+状态）。</summary>
    private void BuildOverviewItem(ChannelVm vm)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 2), VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.Red, VerticalAlignment = VerticalAlignment.Center };
        var txt = new TextBlock { Margin = new Thickness(6, 0, 0, 0), Text = $"{vm.Name}：已停止", VerticalAlignment = VerticalAlignment.Center };
        item.Children.Add(dot);
        item.Children.Add(txt);
        ChStatusBar.Children.Add(item);
        vm.OverviewDot = dot;
        vm.OverviewText = txt;
    }

    /// <summary>动态构建一个通道页签：状态行 + 日志 + 数据列表。</summary>
    private void BuildTabItem(ChannelVm vm)
    {
        var tab = new TabItem { Header = vm.Name };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 状态行
        var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
        var dot = new Ellipse { Width = 12, Height = 12, Fill = Brushes.Red, VerticalAlignment = VerticalAlignment.Center };
        var txtConn = new TextBlock { Margin = new Thickness(8, 0, 0, 0), Text = "未连接", Foreground = Brushes.Red, FontWeight = FontWeights.Bold };
        var txtPlc = new TextBlock { Margin = new Thickness(20, 0, 0, 0), Text = "PLC地址：--", VerticalAlignment = VerticalAlignment.Center };
        var txtCnt = new TextBlock { Margin = new Thickness(20, 0, 0, 0), Text = "会话/累计：0 / 0", VerticalAlignment = VerticalAlignment.Center };
        var txtRun = new TextBlock { Margin = new Thickness(20, 0, 0, 0), Text = "采集：已停止", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
        var btnStart = new Button { Content = "启动", Width = 70, Margin = new Thickness(20, 0, 6, 0), Padding = new Thickness(0, 2, 0, 2) };
        btnStart.Click += async (_, _) => { if (vm.Service != null) await vm.Service.StartAsync(); };
        var btnStop = new Button { Content = "停止", Width = 70, Padding = new Thickness(0, 2, 0, 2) };
        btnStop.Click += async (_, _) => { if (vm.Service != null) await vm.Service.StopAsync(); };
        statusPanel.Children.Add(dot);
        statusPanel.Children.Add(txtConn);
        statusPanel.Children.Add(txtPlc);
        statusPanel.Children.Add(txtCnt);
        statusPanel.Children.Add(txtRun);
        statusPanel.Children.Add(btnStart);
        statusPanel.Children.Add(btnStop);
        vm.Dot = dot; vm.TxtConn = txtConn; vm.TxtPlc = txtPlc; vm.TxtCnt = txtCnt; vm.TxtRun = txtRun;
        vm.BtnStart = btnStart; vm.BtnStop = btnStop;
        Grid.SetRow(statusPanel, 0);
        root.Children.Add(statusPanel);

        // 日志框
        var logBox = new TextBox { IsReadOnly = true, Height = 120, Margin = new Thickness(4), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap };
        vm.Log = logBox;
        Grid.SetRow(logBox, 1);
        root.Children.Add(logBox);

        // 数据列表
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, Margin = new Thickness(4), SelectionMode = DataGridSelectionMode.Extended, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)) };
        grid.ItemsSource = vm.List;
        vm.Grid = grid;
        Grid.SetRow(grid, 2);
        root.Children.Add(grid);

        tab.Content = root;
        ChTab.Items.Add(tab);

        BuildDynamicColumns(vm);
    }

    /// <summary>根据通道配置的动态数据项生成该通道表格列。</summary>
    private void BuildDynamicColumns(ChannelVm vm)
    {
        if (vm.Grid == null) return;
        vm.Grid.Columns.Clear();
        vm.Grid.Columns.Add(new DataGridTextColumn { Header = "序号", Width = 55, Binding = new Binding("Id") });
        vm.Grid.Columns.Add(new DataGridTextColumn { Header = "接收时间", Width = 175, Binding = new Binding("ReceiveTime") { StringFormat = "yyyy-MM-dd HH:mm:ss" } });
        if (vm.Channel != null)
        {
            foreach (var di in vm.Channel.DataItems)
            {
                vm.Grid.Columns.Add(new DataGridTextColumn
                {
                    Header = di.FieldName,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    Binding = new Binding($"FieldValues[{di.FieldName}]")
                });
            }
        }
        vm.Grid.Columns.Add(new DataGridTextColumn { Header = "状态标记", Width = 70, Binding = new Binding("Status") });
    }

    private void OnLog(ChannelVm vm, string msg)
    {
        Dispatcher.Invoke(() =>
        {
            var line = $"{DateTime.Now:HH:mm:ss} {msg}";
            vm.LogLines.Add(line);
            if (vm.Log != null) vm.Log.AppendText(line + Environment.NewLine);

            var maxLines = _cfg?.MaxLogLines > 0 ? _cfg.MaxLogLines : 500;
            if (vm.LogLines.Count > maxLines)
            {
                vm.LogLines.RemoveRange(0, vm.LogLines.Count - maxLines);
                if (vm.Log != null) vm.Log.Text = string.Join(Environment.NewLine, vm.LogLines);
            }
            vm.Log?.ScrollToEnd();
            RefreshStatus(vm);
        });
    }

    private void OnNewRecord(ChannelVm vm, PlcRecord rec)
    {
        Dispatcher.Invoke(() =>
        {
            rec.Id = vm.List.Count + 1;
            vm.List.Add(rec);

            var maxRows = _cfg?.MaxListRows > 0 ? _cfg.MaxListRows : 1000;
            if (vm.List.Count > maxRows)
            {
                vm.List.RemoveAt(0);
                for (int i = 0; i < vm.List.Count; i++)
                    vm.List[i].Id = i + 1;
            }

            // 自动保存到该通道独立库
            if (vm.Db != null && _cfg is { AutoSaveToDb: true })
            {
                try { vm.Db.InsertRecord(rec); }
                catch (Exception ex) { OnLog(vm, $"⚠️ 入库失败：{ex.Message}"); }
            }
        });
    }

    private void RefreshStatus(ChannelVm vm)
    {
        if (vm.Service == null) return;
        var connected = vm.Service.IsConnected;
        if (vm.Dot != null) vm.Dot.Fill = connected ? Brushes.LimeGreen : Brushes.Red;
        if (vm.TxtConn != null)
        {
            vm.TxtConn.Text = connected ? "已连接" : "未连接";
            vm.TxtConn.Foreground = connected ? Brushes.Green : Brushes.Red;
        }
        if (vm.TxtPlc != null && vm.Channel != null)
            vm.TxtPlc.Text = $"PLC地址：{vm.Channel.FinsUdp.PlcIp}:{vm.Channel.FinsUdp.PlcFinsPort}";
        if (vm.TxtCnt != null)
            vm.TxtCnt.Text = $"会话/累计：{vm.Service.SessionReceiveCount} / {vm.Service.TotalReceiveCount}";
        if (vm.TxtRun != null)
        {
            vm.TxtRun.Text = connected ? "采集：运行中" : "采集：已停止";
            vm.TxtRun.Foreground = connected ? Brushes.Green : Brushes.Gray;
        }
        // 顶部「各通道状态一览」
        if (vm.OverviewDot != null)
            vm.OverviewDot.Fill = connected ? Brushes.LimeGreen : Brushes.Red;
        if (vm.OverviewText != null)
            vm.OverviewText.Text = $"{vm.Name}：{(connected ? "运行中" : "已停止")}";
    }

    private ChannelVm? CurrentVm =>
        ChTab.SelectedItem is TabItem && ChTab.SelectedIndex >= 0 && ChTab.SelectedIndex < _channels.Count
            ? _channels[ChTab.SelectedIndex]
            : (_channels.Count > 0 ? _channels[0] : null);

    private async Task StartAllAsync()
    {
        foreach (var vm in _channels)
        {
            if (vm.Service != null) await vm.Service.StartAsync();
        }
    }

    private async Task StopAllAsync()
    {
        foreach (var vm in _channels)
        {
            if (vm.Service != null) await vm.Service.StopAsync();
        }
    }

    private void OnClearCurrent()
    {
        var vm = CurrentVm;
        if (vm == null) return;
        if (vm.List.Count == 0)
        {
            MessageBox.Show("当前通道列表为空，无需清空。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var r = MessageBox.Show($"确定清空「{vm.Name}」当前 {vm.List.Count} 条记录吗？\n（仅清空监控页显示，数据库历史记录不受影响）",
            "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
            vm.List.Clear();
    }

    private void ExportCurrentCsv()
    {
        var vm = CurrentVm;
        if (vm == null || vm.Channel == null) return;
        if (vm.List.Count == 0)
        {
            MessageBox.Show("当前通道列表为空，无可导出数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"接收数据_{vm.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            CsvExporter.Export(vm.List, vm.Channel.DataItems, dlg.FileName);
            MessageBox.Show($"导出成功：{dlg.FileName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
