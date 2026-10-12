using System.Collections.Generic;

namespace PlcSnReceiver.Models;

/// <summary>应用总配置（序列化到 config.json，启动自动加载）</summary>
public class AppConfig
{
    /// <summary>多通道配置列表（每通道一套 PLC 通信 + 触发 + 数据项 + 独立数据库）</summary>
    public List<ChannelConfig> Channels { get; set; } = new();

    /// <summary>是否自动保存接收记录到 SQLite（全局开关）</summary>
    public bool AutoSaveToDb { get; set; } = true;

    /// <summary>监控页数据列表最多保留的行数（全局，超出自动移除最旧，仅影响UI显示）</summary>
    public int MaxListRows { get; set; } = 1000;

    /// <summary>监控页日志框最多保留的行数（全局，超出自动裁剪最旧）</summary>
    public int MaxLogLines { get; set; } = 500;

    /// <summary>历史数据保留天数，超期自动删除并压缩数据库（防止数据库无限变大变慢）</summary>
    public int RetentionDays { get; set; } = 90;
}

/// <summary>单个采集通道：一套完整的 PLC 通信 + 触发 + 数据项 + 独立数据库</summary>
public class ChannelConfig
{
    /// <summary>通道显示名称（用作页签标题/查询下拉项）</summary>
    public string Name { get; set; } = "通道1";

    /// <summary>FINS UDP 通信参数</summary>
    public FinsUdpSetting FinsUdp { get; set; } = new();

    /// <summary>本通道全局触发寄存器地址（如 D100），上升沿触发读取全部数据项</summary>
    public string TriggerRegister { get; set; } = "D100";

    /// <summary>本通道轮询周期（毫秒）</summary>
    public int PollIntervalMs { get; set; } = 200;

    /// <summary>触发完成后是否回写复位触发寄存器为0</summary>
    public bool ResetTriggerAfterRead { get; set; } = true;

    /// <summary>本通道多采集数据项集合（SN1/SN2/数量等，可动态增删，UI表格列自动跟随）</summary>
    public List<DataItemConfig> DataItems { get; set; } = new();

    /// <summary>本通道独立数据库文件名（存放于程序运行目录，如 records.db / Channel2.db）</summary>
    public string DbFile { get; set; } = "records.db";
}

/// <summary>FINS UDP 通信设置</summary>
public class FinsUdpSetting
{
    public string PlcIp { get; set; } = "192.168.1.100";
    public int PlcFinsPort { get; set; } = 9600;
    public int LocalUdpListenPort { get; set; } = 5000;
    public byte LocalNode { get; set; } = 0x01;
    public byte PlcNode { get; set; } = 0x02;
}

/// <summary>每一条采集字段配置（SN1、SN2、批次号、数量等）</summary>
public class DataItemConfig
{
    /// <summary>字段名称（用作UI表格列名，如 SN1）</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>寄存器地址（如 D110 / W20 / H5）</summary>
    public string RegisterAddr { get; set; } = string.Empty;

    /// <summary>数据类型</summary>
    public DataTypeEnum DataType { get; set; } = DataTypeEnum.String;

    /// <summary>占用连续寄存器数量（字符串按寄存器个数，每个寄存器2字节）</summary>
    public int RegisterCount { get; set; } = 10;

    /// <summary>字符串编码方式</summary>
    public StringEncoding Encoding { get; set; } = StringEncoding.Ascii;
}
