# PLC FINS UDP SN 接收工具

欧姆龙 PLC FINS/UDP 上位机接收工具，.NET 10 + WPF，用于接收 PLC 推送的 SN 码/多组数据。

## 三个页面

| 页面 | 功能 |
|------|------|
| 状态监控 | 显示 FINS UDP 连接状态、PLC 地址、会话/累计接收计数、实时日志、接收数据动态表格；可启动/停止监听、清空、导出 CSV |
| 历史查询 | 按起止日期 + 可选字段关键词，查询 SQLite 历史记录并导出 CSV |
| 参数配置 | 配置 FINS UDP 通信参数、全局触发寄存器、可动态增删的采集数据项（SN1/SN2/数量等），本地持久化，启动自动加载 |

## 核心特性
- **自实现 FINS UDP 客户端**（无需第三方库），协议与桌面的 `sn-collector-4` 项目及配套 `SnSimulator` 模拟器完全一致（10 字节 FINS 头、无标识/长度字段、DM 字区 `0x82` / 位区 `0x02`、响应解析兼容 HSL Server 偏移），因此可直接对接该模拟器与欧姆龙直连 PLC。
- **多数据项可配置扩展**：在配置页「＋新增数据项」即可加 SN2、批次号、数量等，监控/查询表格列自动跟随，无需改代码、不改数据库表结构。
- **触发机制**：轮询全局触发寄存器，检测上升沿后一次性读取全部数据项并打包为一条记录，可选读取后回写复位触发位。
- **防卡顿数量上限**：监控列表、日志框保留行数可在配置页设置（默认列表 1000 条 / 日志 500 行），超出自动移除最旧（仅影响 UI 显示，已入库数据不受影响）。
- 配置存 `config.json`，历史存 SQLite `records.db`，均位于程序运行目录，启动自动加载。

## 运行方式
- **VS2026**：打开 `PlcSnReceiver.csproj`（.NET 10 SDK），F5 运行。
- **直接运行**：`bin\Debug\net10.0-windows\PlcSnReceiver.exe`（已编译好）。

## 首次使用（对接真实 PLC）
1. 打开「参数配置」页，填 PLC IP、FINS 端口(9600)、本机监听端口、本地/PLC 节点号。
2. 设全局触发寄存器（如 D100），PLC 把该位写 1 时触发读取。
3. 在「采集数据项」里按需增删条目：字段名称 = 表格列名，寄存器地址支持 `D110`/`W20`/`H5` 等，字符串选编码。
4. 点「测试 FINS 连接」验证通信 →「保存配置」→ 回「状态监控」页「启动监听」。

## 项目结构
```
PlcSnReceiver/
├─ PlcSnReceiver.csproj        （.NET 10 WPF，依赖 Newtonsoft.Json + Microsoft.Data.Sqlite）
├─ App.xaml / MainWindow.xaml  （TabControl，三页）
├─ Models/
│   ├─ Enums.cs                数据类型/编码/内存区
│   ├─ AppConfig.cs            配置模型（FINS UDP / 触发 / 数据项集合）
│   ├─ PlcRecord.cs            接收记录（字段值用字典，支持动态扩展）
│   └─ RegisterAddress.cs      寄存器地址解析（D110 等 → 区码+字地址）
├─ Services/
│   ├─ FinsUdpClient.cs        自实现 FINS UDP 客户端（最小协议，与桌面 sn-collector 项目/模拟器一致，兼容 HSL Server）
│   ├─ FinsUdpService.cs       轮询/触发/读取编排，事件上报
│   ├─ ConfigService.cs        config.json 读写
│   ├─ SqliteDbService.cs      历史记录读写
│   └─ CsvExporter.cs          CSV 导出（UTF-8 BOM，Excel 兼容中文）
└─ UserControls/
    ├─ MonitorPage.xaml(.cs)   状态监控页（动态列）
    ├─ QueryPage.xaml(.cs)     历史查询页（动态列）
    └─ ConfigPage.xaml(.cs)    参数配置页（动态数据项增删）
```

## 说明
- 编译状态：`dotnet build` 0 警告 0 错误，冒烟运行正常，`config.json` 自动生成。
- 默认样例配置为演示用，`PLC IP`、节点号、寄存器地址需按你的实际 PLC 程序修改。
