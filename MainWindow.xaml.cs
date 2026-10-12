using System;
using System.Windows;
using PlcSnReceiver.Models;
using PlcSnReceiver.Services;

namespace PlcSnReceiver;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly SqliteDbService _dbService = new();
    private readonly FinsUdpService _finsService;
    private AppConfig _appConfig;

    public MainWindow()
    {
        InitializeComponent();

        // 初始化数据库表
        _dbService.InitTable();

        // 启动自动加载配置（config.json 存在则加载，不存在则生成默认）
        _appConfig = _configService.LoadConfig();

        _finsService = new FinsUdpService(_appConfig);

        // 注入三个页面
        UcMonitor.BindService(_finsService, _dbService, _appConfig);
        UcQuery.BindService(_dbService, _appConfig);
        UcConfig.BindService(_configService, _appConfig);

        // 配置页保存成功后：更新内存配置、重建两个动态表格列
        UcConfig.Saved += OnConfigSaved;

        Closed += (_, _) => _ = _finsService.StopAsync();
    }

    private void OnConfigSaved(AppConfig newCfg)
    {
        _appConfig = newCfg;
        _finsService.UpdateConfig(newCfg);
        // 把新配置同步给监控页与查询页（上限值、数据项、列都随之刷新）
        UcMonitor.UpdateConfig(newCfg);
        UcQuery.UpdateConfig(newCfg);
        Title = $"PLC FINS UDP SN 接收工具 —— PLC：{newCfg.FinsUdp.PlcIp}:{newCfg.FinsUdp.PlcFinsPort}";
    }
}
