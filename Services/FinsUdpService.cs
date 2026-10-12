using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Services;

/// <summary>
/// FINS UDP 服务编排：轮询触发寄存器，检测上升沿后读取全部配置数据项，
/// 生成 PlcRecord 并通过事件交给 UI 展示 / 入库。
/// </summary>
public class FinsUdpService
{
    private readonly object _cfgLock = new();
    private AppConfig _config;
    private FinsUdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _lastTrigger;

    public bool IsConnected { get; private set; }

    /// <summary>本次会话接收条数</summary>
    public int SessionReceiveCount { get; private set; }

    /// <summary>累计接收条数</summary>
    public long TotalReceiveCount { get; private set; }

    public event Action<string>? OnLogReceived;
    public event Action<PlcRecord>? OnNewRecordReceived;
    public event Action? OnConnectionChanged;

    public FinsUdpService(AppConfig config)
    {
        _config = config;
    }

    public void UpdateConfig(AppConfig cfg)
    {
        lock (_cfgLock)
        {
            _config = cfg;
        }
    }

    private AppConfig GetConfig()
    {
        lock (_cfgLock) return _config;
    }

    /// <summary>启动监听</summary>
    public async Task StartAsync()
    {
        if (IsConnected || _cts != null) return;

        var cfg = GetConfig();
        var client = new FinsUdpClient(cfg.FinsUdp.PlcIp, cfg.FinsUdp.PlcFinsPort,
                                       cfg.FinsUdp.LocalNode, cfg.FinsUdp.PlcNode);
        client.TimeoutMs = Math.Max(300, cfg.PollIntervalMs);

        try
        {
            await Task.Run(client.Connect);
        }
        catch (Exception ex)
        {
            client.Dispose();
            OnLogReceived?.Invoke($"❌ 连接 PLC 失败：{ex.Message}（请检查 IP/端口/节点号及网线）");
            return;
        }

        _client = client;
        IsConnected = true;
        OnConnectionChanged?.Invoke();
        OnLogReceived?.Invoke($"✅ FINS UDP 已连接 PLC：{cfg.FinsUdp.PlcIp}:{cfg.FinsUdp.PlcFinsPort}（本机节点 {cfg.FinsUdp.LocalNode}，PLC 节点 {cfg.FinsUdp.PlcNode}）");
        OnLogReceived?.Invoke($"🔍 开始轮询触发寄存器 {cfg.TriggerRegister}，周期 {cfg.PollIntervalMs}ms");

        _cts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_cts.Token));
    }

    /// <summary>停止监听</summary>
    public async Task StopAsync()
    {
        var cts = _cts;
        if (cts != null)
        {
            cts.Cancel();
            try { if (_pollTask != null) await _pollTask; } catch { /* ignore */ }
            cts.Dispose();
            _cts = null;
            _pollTask = null;
        }

        _client?.Dispose();
        _client = null;
        IsConnected = false;
        OnConnectionChanged?.Invoke();
        OnLogReceived?.Invoke("🛑 FINS 服务已停止");
    }

    /// <summary>测试连接（不进入轮询）</summary>
    public bool TestConnection()
    {
        var cfg = GetConfig();
        var client = new FinsUdpClient(cfg.FinsUdp.PlcIp, cfg.FinsUdp.PlcFinsPort,
                                       cfg.FinsUdp.LocalNode, cfg.FinsUdp.PlcNode);
        client.TimeoutMs = 1500;
        try
        {
            client.Connect();
            // 读一个字验证通信（D0）
            client.ReadWords("D0", 1);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            client.Dispose();
        }
    }

    private async void PollLoop(CancellationToken token)
    {
        var pollMs = Math.Max(50, GetConfig().PollIntervalMs);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var cfg = GetConfig();
                if (cfg.PollIntervalMs != pollMs)
                {
                    pollMs = cfg.PollIntervalMs;
                    OnLogReceived?.Invoke($"⚙️ 轮询周期已调整为 {pollMs}ms");
                }

                try
                {
                    if (_client == null) break;
                    var trigger = _client.ReadBool(cfg.TriggerRegister);
                    if (trigger && !_lastTrigger)
                    {
                        await ReadAllDataItemsAsync();
                    }
                    _lastTrigger = trigger;
                }
                catch (Exception ex)
                {
                    // 偶发超时/错误只记日志，不中断循环
                    OnLogReceived?.Invoke($"⚠️ 轮询异常：{ex.Message}");
                }

                try { await Task.Delay(pollMs, token); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    private async Task ReadAllDataItemsAsync()
    {
        var cfg = GetConfig();
        OnLogReceived?.Invoke($"🔔 检测到触发信号（{cfg.TriggerRegister}=1），读取全部数据项");

        var record = new PlcRecord
        {
            ReceiveTime = DateTime.Now,
            FieldValues = new Dictionary<string, string>(),
            Status = "正常"
        };

        if (_client == null) return;

        // 触发后稍等 PLC 数据稳定
        try { await Task.Delay(50); } catch { /* ignore */ }

        foreach (var item in cfg.DataItems)
        {
            try
            {
                var val = await Task.Run(() => _client.ReadDataItem(item));
                record.FieldValues[item.FieldName] = val;
                OnLogReceived?.Invoke($"   {item.FieldName} = {val}");
            }
            catch (Exception ex)
            {
                record.FieldValues[item.FieldName] = "读取失败";
                record.Status = "异常";
                OnLogReceived?.Invoke($"   ❌ 读取 {item.FieldName}({item.RegisterAddr}) 失败：{ex.Message}");
            }
        }

        SessionReceiveCount++;
        TotalReceiveCount++;
        OnNewRecordReceived?.Invoke(record);

        // 回写复位触发位（0102 写位命令）
        if (cfg.ResetTriggerAfterRead && _client != null)
        {
            try
            {
                _client.WriteBit(cfg.TriggerRegister, false);
                _lastTrigger = false;
                OnLogReceived?.Invoke($"↩️ 已复位触发位 {cfg.TriggerRegister} = OFF");
            }
            catch (Exception ex)
            {
                OnLogReceived?.Invoke($"⚠️ 复位触发位失败：{ex.Message}");
            }
        }
    }
}
