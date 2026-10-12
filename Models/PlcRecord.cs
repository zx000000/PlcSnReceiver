using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace PlcSnReceiver.Models;

/// <summary>
/// 一条接收记录。字段值用字典存储，字段名与配置的数据项一一对应，
/// 这样新增/删除数据项时无需修改数据库表结构。
/// 实现 INotifyPropertyChanged：列表重排序号（Id）时 DataGrid 能自动刷新显示。
/// </summary>
public class PlcRecord : INotifyPropertyChanged
{
    private long _id;
    private string _status = "正常";

    public long Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public DateTime ReceiveTime { get; set; }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    /// <summary>Key=FieldName(如SN1)，Value=读取到的值</summary>
    public Dictionary<string, string> FieldValues { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
