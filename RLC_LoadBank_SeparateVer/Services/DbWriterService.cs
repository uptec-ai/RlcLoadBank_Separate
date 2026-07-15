using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Npgsql;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>Write category — decides whether the dashboard toggle can drop it.</summary>
    public enum DbLogCategory
    {
        /// <summary>Always written while DB is enabled (alarms, app sessions).</summary>
        Critical,
        /// <summary>Written only when FullLogging (dashboard DB toggle) is ON.</summary>
        Normal,
    }

    /// <summary>One queued INSERT/UPDATE with named parameters.</summary>
    public sealed class DbWork
    {
        public DbLogCategory Category { get; init; }
        public string Sql { get; init; }
        public (string Name, object Value)[] Args { get; init; }
    }

    /// <summary>
    /// Shared background DB writer (ServiceHub singleton) — the ONLY component
    /// that opens PostgreSQL connections for logging. Producers enqueue from
    /// any thread; a single consumer batches inserts off the UI thread.
    ///
    /// - Master gate: RLC_DB_CONN environment variable (unset → fully disabled,
    ///   HMI unaffected).
    /// - Per-write gate: DbLogCategory.Normal is dropped at enqueue time while
    ///   FullLogging is OFF (dashboard toggle); Critical is always accepted.
    /// - EnsureSchema runs the embedded db/schema.sql (idempotent) before the
    ///   first batch; while the DB is unreachable it retries every 60 s and
    ///   the queue keeps absorbing writes (bounded — overflow drops oldest-
    ///   style via TryWrite failure, counted and logged).
    /// - Batch: coalesce ~300 ms, up to 200 rows per transaction, 3 attempts,
    ///   then drop the batch and log — best-effort by design (DB down must
    ///   never affect HMI operation).
    /// </summary>
    public sealed class DbWriterService
    {
        private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

        private const int    QueueCapacity   = 10_000;
        private const int    MaxBatch        = 200;
        private const int    CoalesceMs      = 300;
        private const int    WriteAttempts   = 3;
        private const int    SchemaRetrySec  = 60;
        private const string FullLoggingKey  = "Db.FullLogging";

        private readonly string _cs;
        private readonly Channel<DbWork> _queue;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        private bool _schemaReady;
        private long _dropped;

        /// <summary>False when RLC_DB_CONN is not set — every Enqueue becomes a no-op.</summary>
        public bool Enabled { get; }

        /// <summary>
        /// Dashboard DB toggle. OFF → only Critical writes (alarms/sessions) are
        /// stored; ON → everything. Restored from app.config at startup; call
        /// <see cref="SaveFullLogging"/> to persist a change.
        /// </summary>
        public bool FullLogging { get; set; }

        public DbWriterService(string connectionString)
        {
            _cs     = connectionString;
            Enabled = !string.IsNullOrWhiteSpace(connectionString);
            FullLogging = LoadFullLogging();

            if (!Enabled)
            {
                Log.Info("DbWriter disabled: RLC_DB_CONN is not set.");
                return;
            }

            _queue = Channel.CreateBounded<DbWork>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode     = BoundedChannelFullMode.DropWrite,   // 폭주 시 신규 유실(카운트)
            });
            _loop = Task.Run(RunAsync);
        }

        // ── Producer API ──────────────────────────────────────────────────────

        public void Enqueue(DbLogCategory category, string sql, params (string Name, object Value)[] args)
        {
            if (!Enabled) return;
            if (category == DbLogCategory.Normal && !FullLogging) return;

            if (!_queue.Writer.TryWrite(new DbWork { Category = category, Sql = sql, Args = args }))
            {
                long n = Interlocked.Increment(ref _dropped);
                if (n % 1000 == 1) Log.Warn("DbWriter queue full — dropped {0} writes so far.", n);
            }
        }

        // ── Consumer loop (background thread) ─────────────────────────────────

        private async Task RunAsync()
        {
            var ct = _cts.Token;
            var batch = new List<DbWork>(MaxBatch);
            try
            {
                while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    if (!await EnsureSchemaOnceAsync(ct).ConfigureAwait(false)) continue;

                    // 첫 항목 대기 후 잠깐 모아서(coalesce) 배치로 쓴다
                    await Task.Delay(CoalesceMs, ct).ConfigureAwait(false);
                    batch.Clear();
                    while (batch.Count < MaxBatch && _queue.Reader.TryRead(out var w)) batch.Add(w);
                    if (batch.Count == 0) continue;

                    WriteBatchBestEffort(batch);
                }
                // Writer.Complete() 후 잔여분 드레인 (Shutdown 경로)
                batch.Clear();
                while (_queue.Reader.TryRead(out var w)) batch.Add(w);
                if (batch.Count > 0 && _schemaReady) WriteBatchBestEffort(batch);
            }
            catch (OperationCanceledException) { /* shutdown timeout */ }
            catch (Exception ex) { Log.Error(ex, "DbWriter loop terminated unexpectedly."); }
        }

        private void WriteBatchBestEffort(List<DbWork> batch)
        {
            for (int attempt = 1; attempt <= WriteAttempts; attempt++)
            {
                try
                {
                    using var conn = new NpgsqlConnection(_cs);
                    conn.Open();
                    using var tx = conn.BeginTransaction();
                    foreach (var w in batch)
                    {
                        using var cmd = new NpgsqlCommand(w.Sql, conn, tx);
                        if (w.Args != null)
                            foreach (var (name, value) in w.Args)
                                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == WriteAttempts)
                        Log.Warn(ex, "DbWriter batch dropped after {0} attempts ({1} rows).", WriteAttempts, batch.Count);
                    else
                        Thread.Sleep(1000 * attempt);
                }
            }
        }

        // ── Schema bootstrap ──────────────────────────────────────────────────

        private async Task<bool> EnsureSchemaOnceAsync(CancellationToken ct)
        {
            if (_schemaReady) return true;
            try
            {
                using var conn = new NpgsqlConnection(_cs);
                conn.Open();
                using var cmd = new NpgsqlCommand(ReadEmbeddedSchema(), conn);
                cmd.ExecuteNonQuery();
                _schemaReady = true;
                Log.Info("DbWriter: schema ensured on {0}.", conn.Database);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "DbWriter: EnsureSchema failed — retrying in {0}s.", SchemaRetrySec);
                try { await Task.Delay(TimeSpan.FromSeconds(SchemaRetrySec), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                return false;
            }
        }

        private static string ReadEmbeddedSchema()
        {
            var asm  = typeof(DbWriterService).Assembly;
            var name = asm.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith("schema.sql", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("Embedded db/schema.sql resource not found.");
            using var s = asm.GetManifestResourceStream(name);
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        // ── Shutdown (App.OnExit) ─────────────────────────────────────────────

        /// <summary>Stops intake and drains the remaining queue within the timeout.</summary>
        public void Shutdown(TimeSpan timeout)
        {
            if (!Enabled) return;
            try
            {
                _queue.Writer.TryComplete();
                if (!_loop.Wait(timeout)) _cts.Cancel();
            }
            catch (Exception ex) { Log.Warn(ex, "DbWriter shutdown incomplete."); }
        }

        // ── FullLogging persistence (app.config, DeviceConfigService pattern) ─

        private static bool LoadFullLogging()
        {
            var v = ConfigurationManager.AppSettings[FullLoggingKey];
            return !bool.TryParse(v, out bool b) || b;   // 기본값 ON
        }

        public void SaveFullLogging(bool value)
        {
            FullLogging = value;
            try
            {
                var cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var s   = cfg.AppSettings.Settings;
                if (s[FullLoggingKey] == null) s.Add(FullLoggingKey, value.ToString());
                else s[FullLoggingKey].Value = value.ToString();
                cfg.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception ex) { Log.Warn(ex, "Failed to persist {0}.", FullLoggingKey); }
        }
    }
}
