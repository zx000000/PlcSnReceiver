using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Services;

/// <summary>
/// SQLite 历史记录服务。字段值以 JSON 存在一列，查询时再反序列化，
/// 因此新增/删除数据项不需要改表结构。
/// </summary>
public class SqliteDbService
{
    private readonly string _dbPath;

    public SqliteDbService()
    {
        _dbPath = Path.Combine(AppContext.BaseDirectory, "records.db");
    }

    public string DbPath => _dbPath;

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    public void InitTable()
    {
        using var conn = Open();
        var sql = @"
CREATE TABLE IF NOT EXISTS PlcRecords (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ReceiveTime  TEXT NOT NULL,
    Status       TEXT,
    FieldValues  TEXT
);
CREATE INDEX IF NOT EXISTS IX_PlcRecords_Time ON PlcRecords(ReceiveTime);
";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    public void InsertRecord(PlcRecord record)
    {
        using var conn = Open();
        const string sql = @"INSERT INTO PlcRecords(ReceiveTime, Status, FieldValues) VALUES($t,$s,$f)";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("$t", record.ReceiveTime.ToString("o"));
        cmd.Parameters.AddWithValue("$s", record.Status);
        cmd.Parameters.AddWithValue("$f", JsonConvert.SerializeObject(record.FieldValues));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 按时间范围查询历史记录；可选按某个字段名模糊检索关键词。
    /// </summary>
    public List<PlcRecord> QueryByTime(DateTime startTime, DateTime endTime,
                                       string? searchField = null, string? keyword = null)
    {
        var list = new List<PlcRecord>();
        using var conn = Open();
        const string sql = @"SELECT Id,ReceiveTime,Status,FieldValues FROM PlcRecords WHERE ReceiveTime BETWEEN $t1 AND $t2 ORDER BY ReceiveTime DESC";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("$t1", startTime.ToString("o"));
        cmd.Parameters.AddWithValue("$t2", endTime.ToString("o"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var rec = new PlcRecord
            {
                Id = (long)reader["Id"],
                ReceiveTime = DateTime.Parse((string)reader["ReceiveTime"]),
                Status = (string)reader["Status"],
                FieldValues = JsonConvert.DeserializeObject<Dictionary<string, string>>((string)reader["FieldValues"]) ?? new()
            };

            if (!string.IsNullOrEmpty(searchField) && !string.IsNullOrEmpty(keyword))
            {
                if (!rec.FieldValues.TryGetValue(searchField, out var val) ||
                    !val.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            list.Add(rec);
        }
        return list;
    }
}
