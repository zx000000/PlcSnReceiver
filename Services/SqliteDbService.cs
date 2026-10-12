using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using PlcSnReceiver.Models;

namespace PlcSnReceiver.Services;

/// <summary>
/// SQLite 历史记录服务（每通道一个独立库文件）。字段值以 JSON 存一列，查询时反序列化，
/// 因此新增/删除数据项不需要改表结构。
/// 性能优化：WAL 模式、时间索引、按保留天数清理超期数据并 VACUUM 压缩。
/// </summary>
public class SqliteDbService
{
    private readonly string _dbPath;

    /// <param name="dbFile">数据库文件名（如 records.db / Channel2.db），存放到程序运行目录。</param>
    public SqliteDbService(string dbFile)
    {
        _dbPath = Path.Combine(AppContext.BaseDirectory, string.IsNullOrWhiteSpace(dbFile) ? "records.db" : dbFile);
    }

    public string DbPath => _dbPath;

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // WAL 模式 + NORMAL 同步：写入更快且读写并发友好；WAL 模式会持久化到库
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public void InitTable()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS PlcRecords (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ReceiveTime  TEXT NOT NULL,
    Status       TEXT,
    FieldValues  TEXT
);
CREATE INDEX IF NOT EXISTS IX_PlcRecords_Time ON PlcRecords(ReceiveTime);
";
        cmd.ExecuteNonQuery();
    }

    public void InsertRecord(PlcRecord record)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO PlcRecords(ReceiveTime, Status, FieldValues) VALUES($t,$s,$f)";
        cmd.Parameters.AddWithValue("$t", record.ReceiveTime.ToString("o"));
        cmd.Parameters.AddWithValue("$s", record.Status);
        cmd.Parameters.AddWithValue("$f", JsonConvert.SerializeObject(record.FieldValues));
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除超过保留天数的旧数据，并执行 VACUUM 压缩释放空间。返回删除条数。</summary>
    public int CleanupExpired(int retentionDays)
    {
        if (retentionDays <= 0) return 0;
        var cutoff = DateTime.Now.AddDays(-retentionDays);

        int deleted;
        using (var conn = Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM PlcRecords WHERE ReceiveTime < $c";
            cmd.Parameters.AddWithValue("$c", cutoff.ToString("o"));
            deleted = cmd.ExecuteNonQuery();
        }

        if (deleted > 0)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM";
            cmd.ExecuteNonQuery();
        }
        return deleted;
    }

    /// <summary>
    /// 按时间范围查询历史记录；可选按某个字段名模糊检索关键词。
    /// </summary>
    public List<PlcRecord> QueryByTime(DateTime startTime, DateTime endTime,
                                       string? searchField = null, string? keyword = null)
    {
        var list = new List<PlcRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id,ReceiveTime,Status,FieldValues FROM PlcRecords WHERE ReceiveTime BETWEEN $t1 AND $t2 ORDER BY ReceiveTime DESC";
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
