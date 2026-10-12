using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Services;

/// <summary>
/// 配置读写服务：config.json 存放到程序运行目录，启动自动加载，保存自动持久化。
/// 兼容旧版单通道配置：读取到旧结构时自动迁移为一个通道（沿用 records.db，保留历史数据）。
/// </summary>
public class ConfigService
{
    private readonly string _configPath;

    public ConfigService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public string ConfigPath => _configPath;

    /// <summary>加载配置；若文件不存在则生成默认配置并保存；旧版配置自动迁移为多通道结构。</summary>
    public AppConfig LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            var defaultCfg = CreateDefault();
            SaveConfig(defaultCfg);
            return defaultCfg;
        }

        try
        {
            var json = File.ReadAllText(_configPath, Encoding.UTF8);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json);
            if (cfg == null)
            {
                var fallback = CreateDefault();
                SaveConfig(fallback);
                return fallback;
            }

            // 旧版单通道配置迁移：Channels 为空时，从旧顶层字段恢复为一个通道
            if (cfg.Channels == null || cfg.Channels.Count == 0)
            {
                var legacy = JsonConvert.DeserializeObject<LegacyConfigDto>(json);
                if (legacy != null && HasLegacyFields(legacy))
                {
                    cfg.Channels = new List<ChannelConfig>
                    {
                        new()
                        {
                            Name = "通道1",
                            FinsUdp = legacy.FinsUdp ?? new FinsUdpSetting(),
                            TriggerRegister = string.IsNullOrEmpty(legacy.TriggerRegister) ? "D100" : legacy.TriggerRegister,
                            PollIntervalMs = legacy.PollIntervalMs > 0 ? legacy.PollIntervalMs : 200,
                            ResetTriggerAfterRead = legacy.ResetTriggerAfterRead,
                            DataItems = legacy.DataItems ?? new List<DataItemConfig>(),
                            // 沿用旧库文件名，保留已有历史数据
                            DbFile = "records.db"
                        }
                    };
                }
                else
                {
                    cfg.Channels = CreateDefault().Channels;
                }
                SaveConfig(cfg);
            }

            return cfg;
        }
        catch (Exception)
        {
            // 配置文件损坏时退回默认，不崩溃
            var fallback = CreateDefault();
            SaveConfig(fallback);
            return fallback;
        }
    }

    public void SaveConfig(AppConfig config)
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(_configPath, json, Encoding.UTF8);
    }

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            AutoSaveToDb = true,
            MaxListRows = 1000,
            MaxLogLines = 500,
            RetentionDays = 90,
            Channels = new List<ChannelConfig>
            {
                new()
                {
                    Name = "通道1",
                    FinsUdp = new FinsUdpSetting(),
                    TriggerRegister = "D100",
                    PollIntervalMs = 200,
                    ResetTriggerAfterRead = true,
                    DbFile = "records.db",
                    DataItems = new List<DataItemConfig>
                    {
                        new() { FieldName = "SN1", RegisterAddr = "D110", DataType = DataTypeEnum.String, RegisterCount = 10, Encoding = StringEncoding.Ascii },
                        new() { FieldName = "SN2", RegisterAddr = "D120", DataType = DataTypeEnum.String, RegisterCount = 10, Encoding = StringEncoding.Ascii },
                        new() { FieldName = "生产数量", RegisterAddr = "D130", DataType = DataTypeEnum.UInt16, RegisterCount = 1, Encoding = StringEncoding.Ascii }
                    }
                }
            }
        };
    }

    /// <summary>旧版单通道配置的读取模型（用于迁移）。</summary>
    private class LegacyConfigDto
    {
        public FinsUdpSetting? FinsUdp { get; set; }
        public string? TriggerRegister { get; set; }
        public int PollIntervalMs { get; set; }
        public bool ResetTriggerAfterRead { get; set; }
        public List<DataItemConfig>? DataItems { get; set; }
    }

    private static bool HasLegacyFields(LegacyConfigDto d) =>
        d.FinsUdp != null || d.DataItems is { Count: > 0 } || !string.IsNullOrEmpty(d.TriggerRegister);
}
