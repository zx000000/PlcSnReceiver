namespace PlcSnReceiver.Models;

/// <summary>数据项的数据类型</summary>
public enum DataTypeEnum
{
    String,
    UInt16,
    Int16,
    UInt32,
    Int32
}

/// <summary>字符串编码方式（欧姆龙FINS DM区常见ASCII）</summary>
public enum StringEncoding
{
    Ascii,
    Unicode
}

/// <summary>寄存器所属内存区（欧姆龙常用区）</summary>
public enum MemoryAreaCode
{
    CIO = 0xB0,   // CIO/W 区（字）
    WR  = 0xB1,   // WR 工作区（字）
    HR  = 0xB2,   // HR 保持区（字）
    DM  = 0x82,   // DM 数据存储区（字）
    EM  = 0xA0    // EM 扩展区（字，8个bank）
}
