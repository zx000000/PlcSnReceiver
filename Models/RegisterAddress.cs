using System;
using System.Globalization;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Models;

/// <summary>
/// 寄存器地址解析：把 "D100" / "W20" / "H5" / "CIO100" 等字符串解析为
/// 内存区编码 + 字地址。欧姆龙 FINS 读取字地址时，bit 位填 0。
/// </summary>
public readonly struct RegisterAddress
{
    public MemoryAreaCode Area { get; }
    public ushort WordAddress { get; }

    public RegisterAddress(MemoryAreaCode area, ushort wordAddress)
    {
        Area = area;
        WordAddress = wordAddress;
    }

    /// <summary>解析地址字符串。支持前缀：D/DM、W/CIO、WR、H/HR、E/EM。</summary>
    public static RegisterAddress Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("寄存器地址不能为空");

        var s = text.Trim().ToUpperInvariant();
        MemoryAreaCode area;
        string numberPart;

        if (s.StartsWith("DM", StringComparison.Ordinal))
        { area = MemoryAreaCode.DM; numberPart = s[2..]; }
        else if (s.StartsWith("CIO", StringComparison.Ordinal))
        { area = MemoryAreaCode.CIO; numberPart = s[3..]; }
        else if (s.StartsWith("WR", StringComparison.Ordinal))
        { area = MemoryAreaCode.WR; numberPart = s[2..]; }
        else if (s.StartsWith("HR", StringComparison.Ordinal))
        { area = MemoryAreaCode.HR; numberPart = s[2..]; }
        else if (s.StartsWith("EM", StringComparison.Ordinal))
        { area = MemoryAreaCode.EM; numberPart = s[2..]; }
        else if (s.StartsWith("E", StringComparison.Ordinal))
        { area = MemoryAreaCode.EM; numberPart = s[1..]; }
        else if (s.StartsWith("H", StringComparison.Ordinal))
        { area = MemoryAreaCode.HR; numberPart = s[1..]; }
        else if (s.StartsWith("W", StringComparison.Ordinal))
        { area = MemoryAreaCode.CIO; numberPart = s[1..]; }
        else if (s.StartsWith("D", StringComparison.Ordinal))
        { area = MemoryAreaCode.DM; numberPart = s[1..]; }
        else
            throw new ArgumentException($"无法识别的寄存器地址格式：{text}");

        if (!ushort.TryParse(numberPart, NumberStyles.None, CultureInfo.InvariantCulture, out var addr))
            throw new ArgumentException($"寄存器地址数值无效：{text}");

        return new RegisterAddress(area, addr);
    }
}
