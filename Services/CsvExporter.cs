using System.Collections.Generic;
using System.IO;
using System.Text;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Services;

/// <summary>CSV 导出工具（UTF-8 带 BOM，Excel 可直接打开中文）</summary>
public static class CsvExporter
{
    /// <summary>导出记录列表到 CSV 文件。列顺序 = 时间 + 全部数据项字段 + 状态。</summary>
    public static void Export(IEnumerable<PlcRecord> records, IReadOnlyList<DataItemConfig> dataItems, string filePath)
    {
        var sb = new StringBuilder();
        sb.Append("序号,接收时间");
        foreach (var di in dataItems)
        {
            sb.Append(',');
            sb.Append(Escape(di.FieldName));
        }
        sb.Append(",状态");
        sb.Append('\n');

        long index = 1;
        foreach (var rec in records)
        {
            sb.Append(index++);
            sb.Append(',');
            sb.Append(Escape(rec.ReceiveTime.ToString("yyyy-MM-dd HH:mm:ss")));
            foreach (var di in dataItems)
            {
                sb.Append(',');
                sb.Append(Escape(rec.FieldValues.TryGetValue(di.FieldName, out var v) ? v : ""));
            }
            sb.Append(',');
            sb.Append(Escape(rec.Status));
            sb.Append('\n');
        }

        // UTF-8 带 BOM，Excel 正确识别中文
        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
