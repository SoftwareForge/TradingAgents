using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TradingManager.Services;

public sealed class RunPersistenceService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public RunPersistenceService(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "tradingmanager.db");
        _connectionString = $"Data Source={_dbPath}";
        EnsureSchema();
    }

    public async Task UpsertAsync(RunSnapshot snapshot, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var tx = connection.BeginTransaction();

        var runCmd = connection.CreateCommand();
        runCmd.Transaction = tx;
        runCmd.CommandText =
            """
            INSERT INTO runs (
                job_id, ticker, analysis_date, provider, model_id, small_model_id, large_model_id, research_depth, language,
                context_length, max_concurrent,
                agents_completed, agents_total, llm_calls, tool_calls, prompt_tokens, completion_tokens,
                reports_completed, reports_total, elapsed_seconds,
                status, progress, queue_info, created_at, started_at, finished_at, last_error,
                cancel_requested, summary, raw_status_json, raw_result_json, updated_at
            ) VALUES (
                $job_id, $ticker, $analysis_date, $provider, $model_id, $small_model_id, $large_model_id, $research_depth, $language,
                $context_length, $max_concurrent,
                $agents_completed, $agents_total, $llm_calls, $tool_calls, $prompt_tokens, $completion_tokens,
                $reports_completed, $reports_total, $elapsed_seconds,
                $status, $progress, $queue_info, $created_at, $started_at, $finished_at, $last_error,
                $cancel_requested, $summary, $raw_status_json, $raw_result_json, $updated_at
            )
            ON CONFLICT(job_id) DO UPDATE SET
                ticker=excluded.ticker,
                analysis_date=excluded.analysis_date,
                provider=excluded.provider,
                model_id=excluded.model_id,
                small_model_id=excluded.small_model_id,
                large_model_id=excluded.large_model_id,
                research_depth=excluded.research_depth,
                language=excluded.language,
                context_length=excluded.context_length,
                max_concurrent=excluded.max_concurrent,
                agents_completed=excluded.agents_completed,
                agents_total=excluded.agents_total,
                llm_calls=excluded.llm_calls,
                tool_calls=excluded.tool_calls,
                prompt_tokens=excluded.prompt_tokens,
                completion_tokens=excluded.completion_tokens,
                reports_completed=excluded.reports_completed,
                reports_total=excluded.reports_total,
                elapsed_seconds=excluded.elapsed_seconds,
                status=excluded.status,
                progress=excluded.progress,
                queue_info=excluded.queue_info,
                created_at=COALESCE(runs.created_at, excluded.created_at),
                started_at=COALESCE(runs.started_at, excluded.started_at),
                finished_at=excluded.finished_at,
                last_error=excluded.last_error,
                cancel_requested=excluded.cancel_requested,
                summary=excluded.summary,
                raw_status_json=excluded.raw_status_json,
                raw_result_json=excluded.raw_result_json,
                updated_at=excluded.updated_at;
            """;
        runCmd.Parameters.AddWithValue("$job_id", snapshot.JobId);
        runCmd.Parameters.AddWithValue("$ticker", (object?)snapshot.Ticker ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$analysis_date", (object?)snapshot.AnalysisDate ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$provider", (object?)snapshot.Provider ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$model_id", (object?)snapshot.ModelId ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$small_model_id", (object?)snapshot.SmallModelId ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$large_model_id", (object?)snapshot.LargeModelId ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$research_depth", (object?)snapshot.ResearchDepth ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$language", (object?)snapshot.Language ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$context_length", (object?)snapshot.ContextLength ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$max_concurrent", (object?)snapshot.MaxConcurrent ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$agents_completed", (object?)snapshot.AgentsCompleted ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$agents_total", (object?)snapshot.AgentsTotal ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$llm_calls", (object?)snapshot.LlmCalls ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$tool_calls", (object?)snapshot.ToolCalls ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$prompt_tokens", (object?)snapshot.PromptTokens ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$completion_tokens", (object?)snapshot.CompletionTokens ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$reports_completed", (object?)snapshot.ReportsCompleted ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$reports_total", (object?)snapshot.ReportsTotal ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$elapsed_seconds", (object?)snapshot.ElapsedSeconds ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$status", (object?)snapshot.Status ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$progress", snapshot.Progress);
        runCmd.Parameters.AddWithValue("$queue_info", (object?)snapshot.QueueInfo ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$created_at", (object?)snapshot.CreatedAt ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$started_at", (object?)snapshot.StartedAt ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$finished_at", (object?)snapshot.FinishedAt ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$last_error", (object?)snapshot.LastError ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$cancel_requested", snapshot.CancelRequested ? 1 : 0);
        runCmd.Parameters.AddWithValue("$summary", (object?)snapshot.Summary ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$raw_status_json", (object?)snapshot.RawStatusJson ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$raw_result_json", (object?)snapshot.RawResultJson ?? DBNull.Value);
        runCmd.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("O"));
        await runCmd.ExecuteNonQueryAsync(ct);

        var delMetrics = connection.CreateCommand();
        delMetrics.Transaction = tx;
        delMetrics.CommandText = "DELETE FROM run_metrics WHERE job_id = $job_id;";
        delMetrics.Parameters.AddWithValue("$job_id", snapshot.JobId);
        await delMetrics.ExecuteNonQueryAsync(ct);

        if (snapshot.Metrics.Count > 0)
        {
            foreach (var metric in snapshot.Metrics)
            {
                var metricCmd = connection.CreateCommand();
                metricCmd.Transaction = tx;
                metricCmd.CommandText =
                    """
                    INSERT INTO run_metrics (job_id, key, value, updated_at, confidence)
                    VALUES ($job_id, $key, $value, $updated_at, $confidence);
                    """;
                metricCmd.Parameters.AddWithValue("$job_id", snapshot.JobId);
                metricCmd.Parameters.AddWithValue("$key", metric.Key);
                metricCmd.Parameters.AddWithValue("$value", metric.Value);
                metricCmd.Parameters.AddWithValue("$updated_at", (object?)metric.UpdatedAt ?? DBNull.Value);
                metricCmd.Parameters.AddWithValue("$confidence", metric.Confidence);
                await metricCmd.ExecuteNonQueryAsync(ct);
            }
        }

        var delSignals = connection.CreateCommand();
        delSignals.Transaction = tx;
        delSignals.CommandText = "DELETE FROM run_signals WHERE job_id = $job_id;";
        delSignals.Parameters.AddWithValue("$job_id", snapshot.JobId);
        await delSignals.ExecuteNonQueryAsync(ct);

        if (snapshot.Signals.Count > 0)
        {
            foreach (var signal in snapshot.Signals)
            {
                var signalCmd = connection.CreateCommand();
                signalCmd.Transaction = tx;
                signalCmd.CommandText =
                    """
                    INSERT INTO run_signals (job_id, signal, source, context, updated_at)
                    VALUES ($job_id, $signal, $source, $context, $updated_at);
                    """;
                signalCmd.Parameters.AddWithValue("$job_id", snapshot.JobId);
                signalCmd.Parameters.AddWithValue("$signal", signal.Signal);
                signalCmd.Parameters.AddWithValue("$source", signal.Source);
                signalCmd.Parameters.AddWithValue("$context", signal.Context);
                signalCmd.Parameters.AddWithValue("$updated_at", signal.UpdatedAt);
                await signalCmd.ExecuteNonQueryAsync(ct);
            }
        }

        var telemetryCmd = connection.CreateCommand();
        telemetryCmd.Transaction = tx;
        telemetryCmd.CommandText =
            """
            INSERT INTO run_telemetry (
                job_id, ts, agents_completed, agents_total, llm_calls, tool_calls, prompt_tokens,
                completion_tokens, reports_completed, reports_total, elapsed_seconds
            ) VALUES (
                $job_id, $ts, $agents_completed, $agents_total, $llm_calls, $tool_calls, $prompt_tokens,
                $completion_tokens, $reports_completed, $reports_total, $elapsed_seconds
            );
            """;
        telemetryCmd.Parameters.AddWithValue("$job_id", snapshot.JobId);
        telemetryCmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        telemetryCmd.Parameters.AddWithValue("$agents_completed", (object?)snapshot.AgentsCompleted ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$agents_total", (object?)snapshot.AgentsTotal ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$llm_calls", (object?)snapshot.LlmCalls ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$tool_calls", (object?)snapshot.ToolCalls ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$prompt_tokens", (object?)snapshot.PromptTokens ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$completion_tokens", (object?)snapshot.CompletionTokens ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$reports_completed", (object?)snapshot.ReportsCompleted ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$reports_total", (object?)snapshot.ReportsTotal ?? DBNull.Value);
        telemetryCmd.Parameters.AddWithValue("$elapsed_seconds", (object?)snapshot.ElapsedSeconds ?? DBNull.Value);
        await telemetryCmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<RunSnapshotSummary>> ListAsync(int limit = 100, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT job_id, ticker, analysis_date, status, progress, provider, model_id, small_model_id, large_model_id, context_length, max_concurrent, updated_at
            FROM runs
            ORDER BY datetime(updated_at) DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

        var items = new List<RunSnapshotSummary>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new RunSnapshotSummary(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }
        return items;
    }

    public async Task<RunSnapshot?> GetAsync(string jobId, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var runCmd = connection.CreateCommand();
        runCmd.CommandText =
            """
            SELECT
                job_id, ticker, analysis_date, provider, model_id, research_depth, language, context_length, max_concurrent,
                agents_completed, agents_total, llm_calls, tool_calls, prompt_tokens, completion_tokens, reports_completed, reports_total, elapsed_seconds,
                small_model_id, large_model_id,
                status, progress,
                queue_info, created_at, started_at, finished_at, last_error, cancel_requested, summary,
                raw_status_json, raw_result_json
            FROM runs WHERE job_id = $job_id;
            """;
        runCmd.Parameters.AddWithValue("$job_id", jobId);
        using var runReader = await runCmd.ExecuteReaderAsync(ct);
        if (!await runReader.ReadAsync(ct))
        {
            return null;
        }

        var snapshot = new RunSnapshot
        {
            JobId = runReader.GetString(0),
            Ticker = runReader.IsDBNull(1) ? null : runReader.GetString(1),
            AnalysisDate = runReader.IsDBNull(2) ? null : runReader.GetString(2),
            Provider = runReader.IsDBNull(3) ? null : runReader.GetString(3),
            ModelId = runReader.IsDBNull(4) ? null : runReader.GetString(4),
            ResearchDepth = runReader.IsDBNull(5) ? null : runReader.GetString(5),
            Language = runReader.IsDBNull(6) ? null : runReader.GetString(6),
            ContextLength = runReader.IsDBNull(7) ? null : runReader.GetInt32(7),
            MaxConcurrent = runReader.IsDBNull(8) ? null : runReader.GetInt32(8),
            AgentsCompleted = runReader.IsDBNull(9) ? null : runReader.GetInt32(9),
            AgentsTotal = runReader.IsDBNull(10) ? null : runReader.GetInt32(10),
            LlmCalls = runReader.IsDBNull(11) ? null : runReader.GetInt32(11),
            ToolCalls = runReader.IsDBNull(12) ? null : runReader.GetInt32(12),
            PromptTokens = runReader.IsDBNull(13) ? null : runReader.GetInt32(13),
            CompletionTokens = runReader.IsDBNull(14) ? null : runReader.GetInt32(14),
            ReportsCompleted = runReader.IsDBNull(15) ? null : runReader.GetInt32(15),
            ReportsTotal = runReader.IsDBNull(16) ? null : runReader.GetInt32(16),
            ElapsedSeconds = runReader.IsDBNull(17) ? null : runReader.GetInt32(17),
            SmallModelId = runReader.IsDBNull(18) ? null : runReader.GetString(18),
            LargeModelId = runReader.IsDBNull(19) ? null : runReader.GetString(19),
            Status = runReader.IsDBNull(20) ? null : runReader.GetString(20),
            Progress = runReader.IsDBNull(21) ? 0 : runReader.GetInt32(21),
            QueueInfo = runReader.IsDBNull(22) ? null : runReader.GetString(22),
            CreatedAt = runReader.IsDBNull(23) ? null : runReader.GetString(23),
            StartedAt = runReader.IsDBNull(24) ? null : runReader.GetString(24),
            FinishedAt = runReader.IsDBNull(25) ? null : runReader.GetString(25),
            LastError = runReader.IsDBNull(26) ? null : runReader.GetString(26),
            CancelRequested = !runReader.IsDBNull(27) && runReader.GetInt32(27) == 1,
            Summary = runReader.IsDBNull(28) ? null : runReader.GetString(28),
            RawStatusJson = runReader.IsDBNull(29) ? null : runReader.GetString(29),
            RawResultJson = runReader.IsDBNull(30) ? null : runReader.GetString(30),
        };
        await runReader.CloseAsync();

        var metricCmd = connection.CreateCommand();
        metricCmd.CommandText = "SELECT key, value, updated_at, confidence FROM run_metrics WHERE job_id = $job_id;";
        metricCmd.Parameters.AddWithValue("$job_id", jobId);
        using var metricReader = await metricCmd.ExecuteReaderAsync(ct);
        while (await metricReader.ReadAsync(ct))
        {
            snapshot.Metrics.Add(new PersistedMetric(
                metricReader.GetString(0),
                metricReader.IsDBNull(1) ? string.Empty : metricReader.GetString(1),
                metricReader.IsDBNull(2) ? null : metricReader.GetString(2),
                metricReader.IsDBNull(3) ? 0 : metricReader.GetInt32(3)));
        }
        await metricReader.CloseAsync();

        var signalCmd = connection.CreateCommand();
        signalCmd.CommandText = "SELECT signal, source, context, updated_at FROM run_signals WHERE job_id = $job_id ORDER BY id DESC;";
        signalCmd.Parameters.AddWithValue("$job_id", jobId);
        using var signalReader = await signalCmd.ExecuteReaderAsync(ct);
        while (await signalReader.ReadAsync(ct))
        {
            snapshot.Signals.Add(new PersistedSignal(
                signalReader.IsDBNull(0) ? string.Empty : signalReader.GetString(0),
                signalReader.IsDBNull(1) ? string.Empty : signalReader.GetString(1),
                signalReader.IsDBNull(2) ? string.Empty : signalReader.GetString(2),
                signalReader.IsDBNull(3) ? DateTime.UtcNow.ToString("O") : signalReader.GetString(3)));
        }

        return snapshot;
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS runs (
                job_id TEXT PRIMARY KEY,
                ticker TEXT NULL,
                analysis_date TEXT NULL,
                provider TEXT NULL,
                model_id TEXT NULL,
                small_model_id TEXT NULL,
                large_model_id TEXT NULL,
                research_depth TEXT NULL,
                language TEXT NULL,
                context_length INTEGER NULL,
                max_concurrent INTEGER NULL,
                agents_completed INTEGER NULL,
                agents_total INTEGER NULL,
                llm_calls INTEGER NULL,
                tool_calls INTEGER NULL,
                prompt_tokens INTEGER NULL,
                completion_tokens INTEGER NULL,
                reports_completed INTEGER NULL,
                reports_total INTEGER NULL,
                elapsed_seconds INTEGER NULL,
                status TEXT NULL,
                progress INTEGER NOT NULL DEFAULT 0,
                queue_info TEXT NULL,
                created_at TEXT NULL,
                started_at TEXT NULL,
                finished_at TEXT NULL,
                last_error TEXT NULL,
                cancel_requested INTEGER NOT NULL DEFAULT 0,
                summary TEXT NULL,
                raw_status_json TEXT NULL,
                raw_result_json TEXT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS run_metrics (
                job_id TEXT NOT NULL,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                updated_at TEXT NULL,
                confidence INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (job_id, key)
            );

            CREATE TABLE IF NOT EXISTS run_signals (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                signal TEXT NOT NULL,
                source TEXT NOT NULL,
                context TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS run_telemetry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                ts TEXT NOT NULL,
                agents_completed INTEGER NULL,
                agents_total INTEGER NULL,
                llm_calls INTEGER NULL,
                tool_calls INTEGER NULL,
                prompt_tokens INTEGER NULL,
                completion_tokens INTEGER NULL,
                reports_completed INTEGER NULL,
                reports_total INTEGER NULL,
                elapsed_seconds INTEGER NULL
            );
            """;
        cmd.ExecuteNonQuery();

        EnsureColumn(connection, "runs", "context_length", "INTEGER NULL");
        EnsureColumn(connection, "runs", "max_concurrent", "INTEGER NULL");
        EnsureColumn(connection, "runs", "small_model_id", "TEXT NULL");
        EnsureColumn(connection, "runs", "large_model_id", "TEXT NULL");
        EnsureColumn(connection, "runs", "agents_completed", "INTEGER NULL");
        EnsureColumn(connection, "runs", "agents_total", "INTEGER NULL");
        EnsureColumn(connection, "runs", "llm_calls", "INTEGER NULL");
        EnsureColumn(connection, "runs", "tool_calls", "INTEGER NULL");
        EnsureColumn(connection, "runs", "prompt_tokens", "INTEGER NULL");
        EnsureColumn(connection, "runs", "completion_tokens", "INTEGER NULL");
        EnsureColumn(connection, "runs", "reports_completed", "INTEGER NULL");
        EnsureColumn(connection, "runs", "reports_total", "INTEGER NULL");
        EnsureColumn(connection, "runs", "elapsed_seconds", "INTEGER NULL");
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string ddlType)
    {
        var existsCmd = connection.CreateCommand();
        existsCmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = existsCmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {ddlType};";
        alter.ExecuteNonQuery();
    }
}

public sealed class RunSnapshot
{
    public string JobId { get; set; } = string.Empty;
    public string? Ticker { get; set; }
    public string? AnalysisDate { get; set; }
    public string? Provider { get; set; }
    public string? ModelId { get; set; }
    public string? SmallModelId { get; set; }
    public string? LargeModelId { get; set; }
    public string? ResearchDepth { get; set; }
    public string? Language { get; set; }
    public int? ContextLength { get; set; }
    public int? MaxConcurrent { get; set; }
    public int? AgentsCompleted { get; set; }
    public int? AgentsTotal { get; set; }
    public int? LlmCalls { get; set; }
    public int? ToolCalls { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? ReportsCompleted { get; set; }
    public int? ReportsTotal { get; set; }
    public int? ElapsedSeconds { get; set; }
    public string? Status { get; set; }
    public int Progress { get; set; }
    public string? QueueInfo { get; set; }
    public string? CreatedAt { get; set; }
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public string? LastError { get; set; }
    public bool CancelRequested { get; set; }
    public string? Summary { get; set; }
    public string? RawStatusJson { get; set; }
    public string? RawResultJson { get; set; }
    public List<PersistedMetric> Metrics { get; set; } = [];
    public List<PersistedSignal> Signals { get; set; } = [];
}

public sealed record PersistedMetric(string Key, string Value, string? UpdatedAt, int Confidence);
public sealed record PersistedSignal(string Signal, string Source, string Context, string UpdatedAt);
public sealed record RunSnapshotSummary(
    string JobId,
    string? Ticker,
    string? AnalysisDate,
    string? Status,
    int Progress,
    string? Provider,
    string? ModelId,
    string? SmallModelId,
    string? LargeModelId,
    int? ContextLength,
    int? MaxConcurrent,
    string? UpdatedAt);
