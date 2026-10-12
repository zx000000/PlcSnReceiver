using System;
using System.Collections.Generic;
using System.Windows;
using PlcSnReceiver.Models;
using PlcSnReceiver.Services;

namespace PlcSnReceiver;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly List<SqliteDbService> _dbServices = new();
    private readonly List<FinsUdpService> _finsServices = new();
    private AppConfig _appConfig;

    public MainWindow()
    {
        InitializeComponent();

        // 启动自动加载配置（config.json 存在则加载，旧版单通道配置自动迁移；不存在则生成默认）
        _appConfig = _configService.LoadConfig();

        // 为每个通道创建独立数据库服务与 FINS 服务
        BuildServices(_appConfig);

        // 注入三个页面
        UcMonitor.BindService(_finsServices, _appConfig);
        UcQuery.BindService(_dbServices, _appConfig);
        UcConfig.BindService(_configService, _appConfig);

        // 配置页保存成功后：重建所有通道服务、刷新各页面
        UcConfig.Saved += OnConfigSaved;

        Closed += OnWindowClosed;
    }

    /// <summary>为每个通道创建：独立数据库（建表 + 清理超期历史并压缩）与 FINS 采集服务。</summary>
    private void BuildServices(AppConfig cfg)
    {
        _dbServices.Clear();
        _finsServices.Clear();
        foreach (var ch in cfg.Channels)
        {
            var db = new SqliteDbService(ch.DbFile);
            db.InitTable();
            db.CleanupExpired(cfg.RetentionDays); // 启动/保存后即清理超期数据，防止数据库无限变大
            _dbServices.Add(db);
            _finsServices.Add(new FinsUdpService(ch));
        }
    }

    private void OnConfigSaved(AppConfig newCfg)
    {
        // 停止旧的采集服务，按新配置重建
        foreach (var s in _finsServices) _ = s.StopAsync();
        _appConfig = newCfg;
        BuildServices(newCfg);

        // 刷新监控（通道页签）与查询（通道下拉）
        UcMonitor.UpdateConfig(_finsServices, newCfg);
        UcQuery.UpdateConfig(_dbServices, newCfg);
    }

    private async void OnWindowClosed(object? sender, EventArgs e)
    {
        foreach (var s in _finsServices)
            await s.StopAsync();
    }
}
