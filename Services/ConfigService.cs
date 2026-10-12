using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Services;

/// <summary>
/// 配置读写服务：config.json 存放到程序运行目录，启动自动加载，保存自动持久化。
/// </summary>
public class ConfigService
{
    private readonly string _configPath;

    public ConfigService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public string ConfigPath => _configPath;

    /// <summary>加载配置；若文件不存在则生成默认配置并保存。</summary>
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
            TriggerRegister = "D100",
            PollIntervalMs = 200,
            ResetTriggerAfterRead = true,
            AutoSaveToDb = true,
            MaxListRows = 1000,
            MaxLogLines = 500,
            DataItems = new System.Collections.Generic.List<DataItemConfig>
            {
                new()
                {
                    FieldName = "SN1",
                    RegisterAddr = "D110",
                    DataType = DataTypeEnum.String,
                    RegisterCount = 10,
                    Encoding = StringEncoding.Ascii
                },
                new()
                {
                    FieldName = "SN2",
                    RegisterAddr = "D120",
                    DataType = DataTypeEnum.String,
                    RegisterCount = 10,
                    Encoding = StringEncoding.Ascii
                },
                new()
                {
                    FieldName = "生产数量",
                    RegisterAddr = "D130",
                    DataType = DataTypeEnum.UInt16,
                    RegisterCount = 1,
                    Encoding = StringEncoding.Ascii
                }
            }
        };
    }
}
