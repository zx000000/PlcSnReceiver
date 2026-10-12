using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PlcSnReceiver.Models;
using PlcSnReceiver.Services;

namespace PlcSnReceiver.UserControls;

public partial class ConfigPage : UserControl
{
    private ConfigService? _configSvc;
    private AppConfig? _currentCfg;

    public ObservableCollection<ChannelConfig> ChannelList { get; } = new();
    private readonly ObservableCollection<DataItemConfig> _editingItems = new();

    public ConfigPage()
    {
        InitializeComponent();
        LsChannels.ItemsSource = ChannelList;
        DgDataItems.ItemsSource = _editingItems;

        LsChannels.SelectionChanged += (_, _) => LoadSelectedChannelToUI();
        BtnAddChannel.Click += (_, _) => AddChannel();
        BtnDelChannel.Click += (_, _) => DeleteChannel();
        BtnAddItem.Click += (_, _) => AddItem();
        BtnDelItem.Click += (_, _) => DeleteItem();
        BtnSave.Click += (_, _) => Save();
        BtnDefault.Click += (_, _) => LoadDefault();
        BtnTestConn.Click += async (_, _) => await TestConnAsync();
    }

    public void BindService(ConfigService configSvc, AppConfig cfg)
    {
        _configSvc = configSvc;
        _currentCfg = cfg;
        RebuildChannelList();
        LoadGlobalToUI();
    }

    /// <summary>配置保存后由 MainWindow 重新注入，并刷新通道列表。</summary>
    public void ReloadFromConfig(AppConfig cfg)
    {
        _currentCfg = cfg;
        RebuildChannelList();
        LoadGlobalToUI();
    }

    private void RebuildChannelList()
    {
        if (_currentCfg == null) return;
        ChannelList.Clear();
        foreach (var ch in _currentCfg.Channels)
            ChannelList.Add(ch);
        if (ChannelList.Count > 0)
            LsChannels.SelectedIndex = 0;
    }

    private void LoadGlobalToUI()
    {
        if (_currentCfg == null) return;
        ChkAutoSave.IsChecked = _currentCfg.AutoSaveToDb;
        TxtMaxListRows.Text = _currentCfg.MaxListRows.ToString();
        TxtMaxLogLines.Text = _currentCfg.MaxLogLines.ToString();
        TxtRetentionDays.Text = _currentCfg.RetentionDays.ToString();
    }

    /// <summary>把选中通道的参数加载到右侧表单。</summary>
    private void LoadSelectedChannelToUI()
    {
        if (LsChannels.SelectedItem is not ChannelConfig ch) return;

        TxtChannelName.Text = ch.Name;
        TxtPlcIp.Text = ch.FinsUdp.PlcIp;
        TxtPlcPort.Text = ch.FinsUdp.PlcFinsPort.ToString();
        TxtLocalPort.Text = ch.FinsUdp.LocalUdpListenPort.ToString();
        TxtLocalNode.Text = ch.FinsUdp.LocalNode.ToString();
        TxtPlcNode.Text = ch.FinsUdp.PlcNode.ToString();
        TxtTriggerAddr.Text = ch.TriggerRegister;
        TxtPollMs.Text = ch.PollIntervalMs.ToString();
        ChkReset.IsChecked = ch.ResetTriggerAfterRead;

        _editingItems.Clear();
        foreach (var item in ch.DataItems)
            _editingItems.Add(Clone(item));
    }

    private void AddChannel()
    {
        if (_currentCfg == null) return;
        int n = _currentCfg.Channels.Count + 1;
        string name = $"通道{n}";
        while (ChannelList.Any(c => c.Name == name))
        {
            n++;
            name = $"通道{n}";
        }

        var ch = new ChannelConfig
        {
            Name = name,
            FinsUdp = new FinsUdpSetting(),
            TriggerRegister = "D100",
            PollIntervalMs = 200,
            ResetTriggerAfterRead = true,
            DbFile = $"Channel{n}.db",
            DataItems = new System.Collections.Generic.List<DataItemConfig>
            {
                new() { FieldName = "SN1", RegisterAddr = "D110", DataType = DataTypeEnum.String, RegisterCount = 10, Encoding = StringEncoding.Ascii },
                new() { FieldName = "SN2", RegisterAddr = "D120", DataType = DataTypeEnum.String, RegisterCount = 10, Encoding = StringEncoding.Ascii },
                new() { FieldName = "生产数量", RegisterAddr = "D130", DataType = DataTypeEnum.UInt16, RegisterCount = 1, Encoding = StringEncoding.Ascii }
            }
        };
        _currentCfg.Channels.Add(ch);
        ChannelList.Add(ch);
        LsChannels.SelectedItem = ch;
    }

    private void DeleteChannel()
    {
        if (_currentCfg == null || LsChannels.SelectedItem is not ChannelConfig ch) return;
        if (_currentCfg.Channels.Count <= 1)
        {
            MessageBox.Show("至少需要保留一个通道。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var r = MessageBox.Show($"确定删除通道「{ch.Name}」吗？\n其历史数据库文件 {ch.DbFile} 仍保留在磁盘（不再被本程序关联）。",
            "确认删除通道", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        _currentCfg.Channels.Remove(ch);
        ChannelList.Remove(ch);
        if (ChannelList.Count > 0)
            LsChannels.SelectedIndex = 0;
    }

    private void AddItem()
    {
        string baseName = "新字段";
        string name = baseName;
        int n = 1;
        while (_editingItems.Any(i => i.FieldName == name))
            name = $"{baseName}{++n}";

        _editingItems.Add(new DataItemConfig
        {
            FieldName = name,
            RegisterAddr = "D200",
            DataType = DataTypeEnum.String,
            RegisterCount = 10,
            Encoding = StringEncoding.Ascii
        });
        DgDataItems.ScrollIntoView(_editingItems[^1]);
    }

    private void DeleteItem()
    {
        if (DgDataItems.SelectedItem is DataItemConfig di)
            _editingItems.Remove(di);
    }

    private void Save()
    {
        if (_configSvc == null || _currentCfg == null)
        {
            MessageBox.Show("配置服务未初始化。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        try
        {
            var ch = LsChannels.SelectedItem as ChannelConfig
                     ?? throw new FormatException("请先选择要保存的通道");

            // 通道名称：可自定义/修改，校验非空且唯一
            var newName = TxtChannelName.Text.Trim();
            if (string.IsNullOrEmpty(newName))
                throw new FormatException("通道名称不能为空");
            if (_currentCfg.Channels.Any(c => !ReferenceEquals(c, ch) && c.Name == newName))
                throw new FormatException($"通道名称「{newName}」已存在，请换一个");
            ch.Name = newName;

            // 写入选中的通道参数
            ch.FinsUdp = new FinsUdpSetting
            {
                PlcIp = TxtPlcIp.Text.Trim(),
                PlcFinsPort = ParsePositiveInt(TxtPlcPort.Text, "PLC FINS 端口"),
                LocalUdpListenPort = ParsePositiveInt(TxtLocalPort.Text, "本机监听端口"),
                LocalNode = ParseByte(TxtLocalNode.Text, "本地节点号"),
                PlcNode = ParseByte(TxtPlcNode.Text, "PLC 节点号")
            };
            ch.TriggerRegister = ParseAddress(TxtTriggerAddr.Text);
            ch.PollIntervalMs = ParsePositiveInt(TxtPollMs.Text, "轮询周期");
            ch.ResetTriggerAfterRead = ChkReset.IsChecked == true;
            ch.DataItems = _editingItems.Select(Clone).ToList();

            // 全局设置
            _currentCfg.AutoSaveToDb = ChkAutoSave.IsChecked == true;
            _currentCfg.MaxListRows = ParsePositiveInt(TxtMaxListRows.Text, "监控列表保留条数");
            _currentCfg.MaxLogLines = ParsePositiveInt(TxtMaxLogLines.Text, "日志保留行数");
            _currentCfg.RetentionDays = ParsePositiveInt(TxtRetentionDays.Text, "历史数据保留天数");

            _configSvc.SaveConfig(_currentCfg);
            LsChannels.Items.Refresh(); // 刷新通道列表显示新名称
            MessageBox.Show("配置已保存，并写入 config.json。\n各页面已按新配置重建（监控/查询页立即生效）。",
                            "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            Saved?.Invoke(_currentCfg);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"配置校验失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadDefault()
    {
        if (_currentCfg == null) return;
        var def = ConfigService.CreateDefault();
        _currentCfg.Channels.Clear();
        foreach (var ch in def.Channels)
            _currentCfg.Channels.Add(ch);
        _currentCfg.AutoSaveToDb = def.AutoSaveToDb;
        _currentCfg.MaxListRows = def.MaxListRows;
        _currentCfg.MaxLogLines = def.MaxLogLines;
        _currentCfg.RetentionDays = def.RetentionDays;
        RebuildChannelList();
        LoadGlobalToUI();
    }

    private async System.Threading.Tasks.Task TestConnAsync()
    {
        ChannelConfig ch;
        try
        {
            ch = LsChannels.SelectedItem as ChannelConfig
                 ?? throw new FormatException("请先选择要测试的通道");
            // 先用界面当前值测试
            ch.FinsUdp = new FinsUdpSetting
            {
                PlcIp = TxtPlcIp.Text.Trim(),
                PlcFinsPort = ParsePositiveInt(TxtPlcPort.Text, "PLC FINS 端口"),
                LocalUdpListenPort = ParsePositiveInt(TxtLocalPort.Text, "本机监听端口"),
                LocalNode = ParseByte(TxtLocalNode.Text, "本地节点号"),
                PlcNode = ParseByte(TxtPlcNode.Text, "PLC 节点号")
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show($"配置校验失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var test = new FinsUdpService(ch);
        bool ok = await System.Threading.Tasks.Task.Run(test.TestConnection);
        MessageBox.Show(ok
            ? $"✅ 通道「{ch.Name}」FINS 连接成功，PLC 通信正常。\nPLC：{ch.FinsUdp.PlcIp}:{ch.FinsUdp.PlcFinsPort}"
            : $"❌ 通道「{ch.Name}」FINS 连接失败。\n请检查 IP、端口、节点号、网线，及 PLC FINS 服务使能。",
            "测试连接", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static DataItemConfig Clone(DataItemConfig src) =>
        new()
        {
            FieldName = src.FieldName,
            RegisterAddr = src.RegisterAddr,
            DataType = src.DataType,
            RegisterCount = src.RegisterCount,
            Encoding = src.Encoding
        };

    private static int ParsePositiveInt(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out var v) || v <= 0)
            throw new FormatException($"{name} 必须为正整数");
        return v;
    }

    private static byte ParseByte(string text, string name)
    {
        if (!byte.TryParse(text, out var v))
            throw new FormatException($"{name} 必须为 0~255 的整数");
        return v;
    }

    private static string ParseAddress(string text)
    {
        // 校验地址格式是否可解析
        RegisterAddress.Parse(text);
        return text.Trim();
    }

    /// <summary>保存成功事件，MainWindow 订阅后同步给其它页面。</summary>
    public event Action<AppConfig>? Saved;
}
