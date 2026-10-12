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
    public ObservableCollection<DataItemConfig> ItemList { get; } = new();

    private ConfigService? _configSvc;
    private AppConfig? _currentCfg;

    public ConfigPage()
    {
        InitializeComponent();
        DgDataItems.ItemsSource = ItemList;

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
        LoadConfigToUI();
    }

    /// <summary>把配置加载到界面控件。</summary>
    public void LoadConfigToUI(AppConfig? cfg = null)
    {
        if (cfg != null) _currentCfg = cfg;
        if (_currentCfg == null) return;

        TxtPlcIp.Text = _currentCfg.FinsUdp.PlcIp;
        TxtPlcPort.Text = _currentCfg.FinsUdp.PlcFinsPort.ToString();
        TxtLocalPort.Text = _currentCfg.FinsUdp.LocalUdpListenPort.ToString();
        TxtLocalNode.Text = _currentCfg.FinsUdp.LocalNode.ToString();
        TxtPlcNode.Text = _currentCfg.FinsUdp.PlcNode.ToString();
        TxtTriggerAddr.Text = _currentCfg.TriggerRegister;
        TxtPollMs.Text = _currentCfg.PollIntervalMs.ToString();
        ChkReset.IsChecked = _currentCfg.ResetTriggerAfterRead;
        ChkAutoSave.IsChecked = _currentCfg.AutoSaveToDb;
        TxtMaxListRows.Text = _currentCfg.MaxListRows.ToString();
        TxtMaxLogLines.Text = _currentCfg.MaxLogLines.ToString();

        ItemList.Clear();
        foreach (var item in _currentCfg.DataItems)
            ItemList.Add(Clone(item));
    }

    /// <summary>从界面控件读取配置（含校验）。校验失败抛异常。</summary>
    public AppConfig ReadConfigFromUI()
    {
        return new AppConfig
        {
            FinsUdp = new FinsUdpSetting
            {
                PlcIp = TxtPlcIp.Text.Trim(),
                PlcFinsPort = ParsePositiveInt(TxtPlcPort.Text, "PLC FINS 端口"),
                LocalUdpListenPort = ParsePositiveInt(TxtLocalPort.Text, "本机监听端口"),
                LocalNode = ParseByte(TxtLocalNode.Text, "本地节点号"),
                PlcNode = ParseByte(TxtPlcNode.Text, "PLC 节点号")
            },
            TriggerRegister = ParseAddress(TxtTriggerAddr.Text),
            PollIntervalMs = ParsePositiveInt(TxtPollMs.Text, "轮询周期"),
            ResetTriggerAfterRead = ChkReset.IsChecked == true,
            AutoSaveToDb = ChkAutoSave.IsChecked == true,
            MaxListRows = ParsePositiveInt(TxtMaxListRows.Text, "监控列表保留条数"),
            MaxLogLines = ParsePositiveInt(TxtMaxLogLines.Text, "日志保留行数"),
            DataItems = ItemList.Select(Clone).ToList()
        };
    }

    private void AddItem()
    {
        // 自动给新字段取一个未占用的名称
        string baseName = "新字段";
        string name = baseName;
        int n = 1;
        while (ItemList.Any(i => i.FieldName == name))
            name = $"{baseName}{++n}";

        ItemList.Add(new DataItemConfig
        {
            FieldName = name,
            RegisterAddr = "D200",
            DataType = DataTypeEnum.String,
            RegisterCount = 10,
            Encoding = StringEncoding.Ascii
        });
        DgDataItems.ScrollIntoView(ItemList[^1]);
    }

    private void DeleteItem()
    {
        if (DgDataItems.SelectedItem is DataItemConfig di)
            ItemList.Remove(di);
    }

    private void Save()
    {
        if (_configSvc == null)
        {
            MessageBox.Show("配置服务未初始化。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        try
        {
            var newCfg = ReadConfigFromUI();
            _configSvc.SaveConfig(newCfg);
            _currentCfg = newCfg;
            MessageBox.Show("配置已保存，并写入 config.json。\n重新启动/停止监听后生效。",
                            "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            Saved?.Invoke(newCfg);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"配置校验失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadDefault()
    {
        var def = ConfigService.CreateDefault();
        LoadConfigToUI(def);
    }

    private async System.Threading.Tasks.Task TestConnAsync()
    {
        AppConfig cfg;
        try
        {
            cfg = ReadConfigFromUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"配置校验失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var test = new FinsUdpService(cfg);
        bool ok = await System.Threading.Tasks.Task.Run(test.TestConnection);
        MessageBox.Show(ok
            ? $"✅ FINS 连接成功，PLC 通信正常。\nPLC：{cfg.FinsUdp.PlcIp}:{cfg.FinsUdp.PlcFinsPort}"
            : $"❌ FINS 连接失败。\n请检查 IP、端口、节点号、网线，及 PLC FINS 服务使能。",
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
