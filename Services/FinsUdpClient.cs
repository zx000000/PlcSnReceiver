using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Services;

/// <summary>
/// Omron FINS/UDP 客户端（最小实现，协议与 SnCollector.Core / 配套模拟器一致）。
///
/// 帧格式（与桌面 sn-collector-4 项目、SnSimulator 模拟器完全一致）：
///   请求帧 = 10 字节 FINS 头 + 命令（无 "FINS" 标识、无长度字段）
///     FINS 头: ICF RSV GCT DNA DA1 DA2 SNA SA1 SA2 SID
///   响应帧 = 12 字节头 + 数据，MRES(命令结果) 在 resp[10]，
///     数据从 resp[12] 开始（兼容标准响应与 HSL Server 的差异偏移）。
///
/// 存储区：DM 字 = 0x82，DM 位 = 0x02。
/// 地址：支持 "D200"（字）与 "D100.00"（字.位）。
/// </summary>
public class FinsUdpClient : IDisposable
{
    private readonly IPEndPoint _plcEndPoint;
    private readonly byte _localNode;
    private readonly byte _plcNode;
    private UdpClient _udp;
    private int _sid;
    private bool _disposed;

    /// <summary>响应超时（毫秒）</summary>
    public int TimeoutMs { get; set; } = 1000;

    public FinsUdpClient(string plcIp, int plcPort, byte localNode, byte plcNode)
    {
        _plcEndPoint = new IPEndPoint(IPAddress.Parse(plcIp), plcPort);
        _localNode = localNode;
        _plcNode = plcNode;
        _udp = new UdpClient();
        _udp.Connect(_plcEndPoint);
        // Windows 特有：收到 ICMP 端口不可达时不抛 ConnectionReset
        if (OperatingSystem.IsWindows())
            _udp.Client.IOControl((IOControlCode)(-1744830452), new byte[] { 0, 0, 0, 0 }, null);
    }

    public void Connect()
    {
        // UdpClient 已在构造函数中 Connect；此处保持接口一致
    }

    public void Close()
    {
        if (_disposed) return;
        _disposed = true;
        _udp.Dispose();
    }

    public void Dispose() => Close();

    // ---------- 地址解析 ----------

    /// <summary>解析 "D200" / "D100.00" 为（区代码，字地址，位）。forBit=true 用位区码。</summary>
    private static (byte area, ushort addr, byte bit) ParseAddress(string address, bool forBit)
    {
        address = address.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(address))
            throw new ArgumentException("寄存器地址不能为空");

        char areaChar = address[0];
        string rest = address[1..];
        byte bit = 0;
        var dot = rest.IndexOf('.');
        if (dot >= 0)
        {
            bit = byte.Parse(rest[(dot + 1)..]);
            rest = rest[..dot];
        }
        if (!ushort.TryParse(rest, out var addr))
            throw new ArgumentException($"寄存器地址数值无效: {address}");

        // 字区码
        byte area = areaChar switch
        {
            'D' => 0x82,          // DM 字
            'W' => 0xB0,          // CIO/W 字
            'H' => 0xB2,          // HR 字
            'M' => 0xB1,          // WR 字
            _ => throw new ArgumentException($"不支持的存储区: {address}（支持 D/W/H）")
        };
        // 位区码
        if (forBit)
        {
            area = areaChar switch
            {
                'D' => 0x02,      // DM 位
                'W' => 0x30,      // CIO 位
                'H' => 0x32,      // HR 位
                _ => area
            };
        }
        return (area, addr, bit);
    }

    // ---------- 帧构建 ----------

    private byte[] BuildHeader(int sid) =>
        // ICF RSV GCT DNA DA1 DA2 SNA SA1 SA2 SID
        new byte[] { 0x80, 0x00, 0x02, 0x00, _plcNode, 0x00, 0x00, _localNode, 0x00, (byte)sid };

    private byte[] BuildPacket(int sid, byte[] command)
    {
        var packet = new byte[10 + command.Length];
        Array.Copy(BuildHeader(sid), 0, packet, 0, 10);
        Array.Copy(command, 0, packet, 10, command.Length);
        return packet;
    }

    private static byte[] BuildReadWordCommand(byte area, ushort addr, int count) =>
        new byte[]
        {
            0x01, 0x01, area,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            0x00,
            (byte)(count >> 8), (byte)(count & 0xFF)
        };

    private static byte[] BuildReadBitCommand(byte area, ushort addr, byte bit) =>
        new byte[]
        {
            0x01, 0x01, area,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            bit, 0x00, 0x01
        };

    private static byte[] BuildWriteBitCommand(byte area, ushort addr, byte bit, bool value) =>
        new byte[]
        {
            0x01, 0x02, area,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            bit, 0x00, 0x01, value ? (byte)0x01 : (byte)0x00
        };

    // ---------- 收发 ----------

    private byte[] SendReceive(byte[] command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sid = System.Threading.Interlocked.Increment(ref _sid) & 0xFF;
        var packet = BuildPacket(sid, command);

        lock (this)
        {
            _udp.Send(packet, packet.Length);
            _udp.Client.ReceiveTimeout = TimeoutMs;
            var ep = _plcEndPoint;
            var resp = _udp.Receive(ref ep);
            return ParseResponse(resp, command[0], command[1]);
        }
    }

    /// <summary>
    /// 解析 FINS/UDP 响应帧（与 SnCollector.Core 一致，兼容标准与 HSL Server）：
    ///   标准/模拟器: MRES 在 resp[10]、SRES 在 resp[11]，数据从 12 开始
    ///   HSL Server: MRES 在 resp[12]（位读时 SRES 缺省数据从 13 开始；
    ///     字读时 SRES 在 13 且为 0，数据从 14 开始）
    /// </summary>
    private static byte[] ParseResponse(byte[] resp, byte reqMrc, byte reqSrc)
    {
        if (resp.Length < 12)
            throw new InvalidOperationException("FINS 响应帧过短");

        if (resp[10] == 0x00 && resp[11] == 0x00)
            return resp[12..];

        // HSL 风格：MRES 在 12
        if (resp.Length >= 14 && resp[12] == 0x00)
            return (resp[13] == 0x00) ? resp[14..] : resp[13..];

        throw new InvalidOperationException($"FINS 返回错误: MRES={resp[10]:X2} SRES={resp[11]:X2}");
    }

    // ---------- 读取 ----------

    /// <summary>读 DM 区字数据（0101 命令），返回连续 N 个字（大端序）</summary>
    public ushort[] ReadWords(string address, int count)
    {
        if (count <= 0) throw new ArgumentException("读取字数必须大于0");
        var (area, addr, _) = ParseAddress(address, forBit: false);
        var data = SendReceive(BuildReadWordCommand(area, addr, count));
        if (data.Length < count * 2)
            throw new InvalidOperationException("FINS 读数返回数据不足");
        var words = new ushort[count];
        for (var i = 0; i < count; i++)
            words[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
        return words;
    }

    public ushort[] ReadWords(RegisterAddress addr, int count)
    {
        // 兼容接口：由 RegisterAddress 构造地址字符串
        var areaChar = addr.Area switch
        {
            MemoryAreaCode.DM => 'D',
            MemoryAreaCode.CIO => 'W',
            MemoryAreaCode.HR => 'H',
            MemoryAreaCode.WR => 'M',
            _ => 'D'
        };
        return ReadWords($"{areaChar}{addr.WordAddress}", count);
    }

    /// <summary>读取位状态（0101 命令，位区代码）。地址支持 "D100" 或 "D100.00"。</summary>
    public bool ReadBool(string address)
    {
        var (area, addr, bit) = ParseAddress(address, forBit: true);
        var data = SendReceive(BuildReadBitCommand(area, addr, bit));
        return data.Length > 0 && data[0] != 0x00;
    }

    /// <summary>写位（0102 命令）。地址支持 "D100" 或 "D100.00"。</summary>
    public void WriteBit(string address, bool value)
    {
        var (area, addr, bit) = ParseAddress(address, forBit: true);
        SendReceive(BuildWriteBitCommand(area, addr, bit, value));
    }

    /// <summary>按配置的数据项读取并解析值</summary>
    public string ReadDataItem(DataItemConfig item)
    {
        var count = Math.Max(1, item.RegisterCount);
        var words = ReadWords(item.RegisterAddr, count);
        return item.DataType switch
        {
            DataTypeEnum.String => DecodeAscii(words, item.Encoding),
            DataTypeEnum.UInt16 => words[0].ToString(),
            DataTypeEnum.Int16 => ((short)words[0]).ToString(),
            DataTypeEnum.UInt32 => ((uint)((words[0] << 16) | words[1])).ToString(),
            DataTypeEnum.Int32 => ((short)words[0] << 16 | words[1]).ToString(),
            // IEEE754 浮点：大端序（高字在前）。Float32=2寄存器，Float64=4寄存器
            DataTypeEnum.Float32 => BitConverter.UInt32BitsToSingle(((uint)words[0] << 16) | words[1]).ToString("G"),
            DataTypeEnum.Float64 => BitConverter.UInt64BitsToDouble(
                ((ulong)words[0] << 48) | ((ulong)words[1] << 32) | ((ulong)words[2] << 16) | words[3]).ToString("G"),
            _ => DecodeAscii(words, item.Encoding)
        };
    }

    private static string DecodeAscii(ushort[] words, StringEncoding encoding)
    {
        var bytes = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
        {
            // FINS 传输为大端序：每字高字节在前
            bytes[i * 2] = (byte)(words[i] >> 8);
            bytes[i * 2 + 1] = (byte)(words[i] & 0xFF);
        }
        string s;
        if (encoding == StringEncoding.Unicode)
        {
            var sb = new StringBuilder();
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                char c = (char)((bytes[i] << 8) | bytes[i + 1]);
                if (c == '\0') break;
                sb.Append(c);
            }
            s = sb.ToString();
        }
        else
        {
            s = Encoding.ASCII.GetString(bytes);
        }
        return s.Trim('\0', ' ', '\r', '\n', '\t');
    }
}
