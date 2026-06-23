using System;
using System.Collections.Generic;
using Npgsql;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// PostgreSQL-backed history (Npgsql). Used when ServiceHub.UseDatabase = true.
    /// Creates the table on first use; all DB calls are guarded so a missing
    /// server degrades gracefully instead of crashing the UI.
    /// </summary>
    public class PostgresHistoryRepository : IHistoryRepository
    {
        private readonly string _cs;

        public PostgresHistoryRepository(string connectionString)
        {
            _cs = connectionString;
            try { EnsureTable(); } catch { /* server unavailable */ }
        }

        private void EnsureTable()
        {
            using var conn = new NpgsqlConnection(_cs);
            conn.Open();
            using var cmd = new NpgsqlCommand(
                @"CREATE TABLE IF NOT EXISTS operation_history (
                    id BIGSERIAL PRIMARY KEY,
                    ts TIMESTAMP NOT NULL,
                    panel TEXT, event TEXT, result TEXT);", conn);
            cmd.ExecuteNonQuery();
        }

        public void Add(HistoryEntry e)
        {
            try
            {
                using var conn = new NpgsqlConnection(_cs);
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "INSERT INTO operation_history (ts, panel, event, result) VALUES (@t,@p,@e,@r)", conn);
                cmd.Parameters.AddWithValue("t", e.Time);
                cmd.Parameters.AddWithValue("p", (object)e.Panel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("e", (object)e.Event ?? DBNull.Value);
                cmd.Parameters.AddWithValue("r", (object)e.Result ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch { /* best-effort */ }
        }

        public IReadOnlyList<HistoryEntry> Query(int max = 500)
        {
            var list = new List<HistoryEntry>();
            try
            {
                using var conn = new NpgsqlConnection(_cs);
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "SELECT ts, panel, event, result FROM operation_history ORDER BY ts DESC LIMIT @m", conn);
                cmd.Parameters.AddWithValue("m", max);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new HistoryEntry
                    {
                        Time = r.GetDateTime(0),
                        Panel = r.IsDBNull(1) ? null : r.GetString(1),
                        Event = r.IsDBNull(2) ? null : r.GetString(2),
                        Result = r.IsDBNull(3) ? null : r.GetString(3),
                    });
            }
            catch { /* server unavailable */ }
            return list;
        }
    }
}
