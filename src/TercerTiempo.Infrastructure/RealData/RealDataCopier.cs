using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.Teams;

namespace TercerTiempo.Infrastructure.RealData;

internal sealed record RealDataTableChanges(string Table, int Inserted, int Updated, int Deleted)
{
    public int Total => Inserted + Updated + Deleted;
}

// Copia la base de datos reales a la base de este entorno, con los mismos Id, en una sola transacción y sobre una foto
// fija de la base real (aislamiento SNAPSHOT). Lo de cada entorno no se toca:
//   - el escudo que sube un club (Teams.CrestFromClub), las medidas del campo que apunta y las fichas que gestiona
//     (RosterEntries.ManagedByClub), con su foto y si están ocultas;
//   - lo que ya no está en la base real solo se borra si nada del entorno lo usa (un partido con acta interna, un árbitro
//     con notas…), y las ligas, temporadas y equipos nunca.
// SeedAsync hace lo contrario una sola vez: llena una base real vacía con los datos reales de la base de este entorno.
internal sealed class RealDataCopier(
    RealDataConnections connections,
    IServiceScopeFactory scopes,
    FileSystemCrestStorage crestFiles,
    ILogger<RealDataCopier> logger)
{
    private static readonly HashSet<string> NeverRemoved = ["SeasonYears", "Competitions", "Seasons", "Teams"];

    private static readonly Dictionary<string, TableRule> Rules = new()
    {
        ["Teams"] = new(LocalColumns: ["CrestFromClub", "Profile_PitchLengthM", "Profile_PitchWidthM"],
            Expressions: new() { ["CrestKey"] = "CASE WHEN t.[CrestFromClub] = 1 THEN t.[CrestKey] ELSE s.[CrestKey] END" }),
        ["RosterEntries"] = new(LocalColumns: ["Hidden", "PhotoKey", "ManagedByClub"], LocalRows: "t.[ManagedByClub] = 1"),
    };

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["SeasonYears"] = "temporadas",
        ["Competitions"] = "ligas",
        ["Seasons"] = "temporadas de liga",
        ["Teams"] = "equipos",
        ["RosterEntries"] = "fichas de plantilla",
        ["Matches"] = "partidos",
        ["Standings"] = "filas de clasificación",
        ["SeasonZones"] = "plazas de liga",
        ["Referees"] = "árbitros",
        ["MatchOfficials"] = "árbitros de partido",
        ["OfficialSheets"] = "actas",
        ["OfficialSheetLines"] = "líneas de acta",
        ["OfficialSheetEvents"] = "sucesos de acta",
    };

    private static readonly string[] IngestJobs =
        [JobNames.FootballData, JobNames.OfficialSheets, JobNames.WeeklySheets, JobNames.MatchOfficials, JobNames.LeagueZones];

    public async Task<JobOutcome> CopyAsync(CancellationToken cancellationToken)
    {
        var tables = TableNames(RealDataModel.Copied);

        await using var source = new SqlConnection(connections.RealData);
        await source.OpenAsync(cancellationToken);
        await using var snapshot = (SqlTransaction)await source.BeginTransactionAsync(IsolationLevel.Snapshot, cancellationToken);

        await using var target = new SqlConnection(connections.Environment);
        await target.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(cancellationToken);

        var plans = new List<TablePlan>();
        foreach (var table in tables)
        {
            var targetColumns = await ColumnsAsync(target, transaction, table, cancellationToken);
            var sourceColumns = (await ColumnsAsync(source, snapshot, table, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var plan = new TablePlan(
                table,
                $"#rd_{table}",
                targetColumns.Where(sourceColumns.Contains).ToList(),
                await PrimaryKeyAsync(target, transaction, table, cancellationToken),
                await UniqueIndexesAsync(target, transaction, table, cancellationToken),
                await LocalReferencesAsync(target, transaction, table, tables, cancellationToken),
                Rules.GetValueOrDefault(table) ?? TableRule.None);
            plans.Add(plan);

            await ExecuteAsync(target, transaction, $"SELECT TOP 0 {List(plan.Columns)} INTO {plan.Temp} FROM dbo.[{table}];", cancellationToken);
            await using var reader = await ReadAsync(source, snapshot, $"SELECT {List(plan.Columns)} FROM dbo.[{table}];", cancellationToken);
            using var bulk = new SqlBulkCopy(target, SqlBulkCopyOptions.KeepNulls, transaction) { DestinationTableName = plan.Temp, BulkCopyTimeout = 300 };
            foreach (var column in plan.Columns) bulk.ColumnMappings.Add(column, column);
            await bulk.WriteToServerAsync(reader, cancellationToken);
        }

        // Primero se borra de hijas a madres (un acta sustituida deja libre su partido) y después se actualiza e inserta
        // de madres a hijas.
        var deleted = new Dictionary<string, int>();
        foreach (var plan in Enumerable.Reverse(plans))
            deleted[plan.Table] = NeverRemoved.Contains(plan.Table) ? 0 : await CountAsync(target, transaction, DeleteSql(plan), cancellationToken);

        var changes = new List<RealDataTableChanges>();
        foreach (var plan in plans)
        {
            var updated = await CountAsync(target, transaction, UpdateSql(plan), cancellationToken);
            var inserted = await CountAsync(target, transaction, InsertSql(plan), cancellationToken);
            changes.Add(new RealDataTableChanges(plan.Table, inserted, updated, deleted[plan.Table]));
            await ExecuteAsync(target, transaction, $"DROP TABLE {plan.Temp};", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        var crests = await CopyCrestFilesAsync(cancellationToken);
        var changed = changes.Where(c => c.Total > 0).ToList();
        if (changed.Count == 0)
            return new JobOutcome(crests > 0 ? $"{crests} escudos copiados" : "Sin cambios", [], Quiet: crests == 0);

        using (var scope = scopes.CreateScope())
            scope.ServiceProvider.GetRequiredService<TercerTiempo.Application.Search.SearchIndexHolder>().Invalidate();
        logger.LogInformation("Datos reales copiados: {Changes}", string.Join(", ", changed.Select(c => $"{c.Table} +{c.Inserted} ~{c.Updated} -{c.Deleted}")));

        var parts = changed.Select(c => $"{c.Total} {Labels.GetValueOrDefault(c.Table, c.Table)}").ToList();
        if (crests > 0) parts.Add($"{crests} escudos");
        return new JobOutcome($"Nuevo o cambiado: {Join(parts)}", []);
    }

    // Una sola vez, al crear la base real: si está vacía, se llena con los datos reales de la base de este entorno (sin lo
    // de los clubes) y con el estado de sus descargas, para seguir donde iban (límites de peticiones, cola de actas…).
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        await using var target = new SqlConnection(connections.RealData);
        await target.OpenAsync(cancellationToken);
        if (await ScalarAsync<int>(target, null, "SELECT COUNT(*) FROM dbo.Competitions;", cancellationToken) > 0) return false;

        await using var source = new SqlConnection(connections.Environment);
        await source.OpenAsync(cancellationToken);
        if (await ScalarAsync<int>(source, null, "SELECT COUNT(*) FROM sys.tables WHERE name = 'Competitions';", cancellationToken) == 0) return false;

        var shared = TableNames(RealDataModel.SharedOnly.Where(t => t != typeof(TeamCrestImage) && t != typeof(RealDataIngester)));
        var jobRuns = TableNames([typeof(JobRun)]).Single();
        var tables = TableNames(RealDataModel.Copied).Concat(shared).Append(jobRuns).ToList();

        await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(cancellationToken);
        // Las filas de partida de las migraciones (los ajustes de la pasada de las ligas) dejan paso a las del entorno.
        foreach (var table in Enumerable.Reverse(tables))
            await ExecuteAsync(target, transaction, $"DELETE FROM dbo.[{table}];", cancellationToken);

        foreach (var table in tables)
        {
            if (await ScalarAsync<int>(source, null, $"SELECT COUNT(*) FROM sys.tables WHERE name = '{table}';", cancellationToken) == 0) continue;

            var sourceColumns = (await ColumnsAsync(source, null, table, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var columns = (await ColumnsAsync(target, transaction, table, cancellationToken)).Where(sourceColumns.Contains).ToList();
            var select = string.Join(", ", columns.Select(c => SeedExpression(table, c) is { } e ? $"{e} AS [{c}]" : $"[{c}]"));
            var where = table switch
            {
                "RosterEntries" => " WHERE [ExternalId] IS NOT NULL",
                _ when table == jobRuns => $" WHERE [Job] IN ({string.Join(", ", IngestJobs.Select(j => $"'{j}'"))})",
                _ => "",
            };

            var options = SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.CheckConstraints;
            if (await HasIdentityAsync(target, transaction, table, cancellationToken)) options |= SqlBulkCopyOptions.KeepIdentity;

            await using var reader = await ReadAsync(source, null, $"SELECT {select} FROM dbo.[{table}]{where};", cancellationToken);
            using var bulk = new SqlBulkCopy(target, options, transaction) { DestinationTableName = $"dbo.[{table}]", BulkCopyTimeout = 600 };
            foreach (var column in columns) bulk.ColumnMappings.Add(column, column);
            await bulk.WriteToServerAsync(reader, cancellationToken);
            logger.LogInformation("Base de datos reales: {Table} copiada desde este entorno", table);
        }

        var crests = 0;
        foreach (var (teamId, key) in await TeamCrestsAsync(target, transaction, cancellationToken))
        {
            if (await crestFiles.ReadAllBytesAsync(key, cancellationToken) is not { } file) continue;
            await using var insert = new SqlCommand(
                "INSERT INTO dbo.TeamCrestImages ([Key], TeamId, ContentType, Content, SavedAt) VALUES (@key, @team, @type, @content, SYSDATETIMEOFFSET());",
                target, transaction);
            insert.Parameters.AddWithValue("@key", key);
            insert.Parameters.AddWithValue("@team", teamId);
            insert.Parameters.AddWithValue("@type", file.ContentType);
            insert.Parameters.Add("@content", SqlDbType.VarBinary, -1).Value = file.Content;
            await insert.ExecuteNonQueryAsync(cancellationToken);
            crests++;
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Base de datos reales creada desde este entorno ({Crests} escudos)", crests);
        return true;
    }

    private static string? SeedExpression(string table, string column) => (table, column) switch
    {
        ("RosterEntries", "Hidden" or "ManagedByClub") => "CAST(0 AS bit)",
        ("RosterEntries", "PhotoKey") => "CAST(NULL AS nvarchar(100))",
        ("Teams", "CrestKey") => "CASE WHEN [CrestFromClub] = 1 THEN NULL ELSE [CrestKey] END",
        ("Teams", "CrestFromClub") => "CAST(0 AS bit)",
        ("Teams", "Profile_PitchLengthM" or "Profile_PitchWidthM") => "CAST(NULL AS decimal(5,1))",
        _ => null,
    };

    // Los escudos que la base real tiene y el entorno aún no tiene en sus ficheros.
    private async Task<int> CopyCrestFilesAsync(CancellationToken cancellationToken)
    {
        await using var target = new SqlConnection(connections.Environment);
        await target.OpenAsync(cancellationToken);
        var missing = new List<string>();
        await using (var reader = await ReadAsync(target, null,
            "SELECT DISTINCT CrestKey FROM dbo.Teams WHERE CrestFromClub = 0 AND CrestKey IS NOT NULL;", cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = reader.GetString(0);
                if (!crestFiles.Exists(key)) missing.Add(key);
            }
        }
        if (missing.Count == 0) return 0;

        await using var source = new SqlConnection(connections.RealData);
        await source.OpenAsync(cancellationToken);
        var copied = 0;
        foreach (var key in missing)
        {
            await using var command = new SqlCommand("SELECT Content FROM dbo.TeamCrestImages WHERE [Key] = @key;", source);
            command.Parameters.AddWithValue("@key", key);
            if (await command.ExecuteScalarAsync(cancellationToken) is not byte[] content) continue;
            await crestFiles.WriteAsync(key, content, cancellationToken);
            copied++;
        }
        return copied;
    }

    private List<string> TableNames(IEnumerable<Type> types)
    {
        using var scope = scopes.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<RealDataDbContext>().Model;
        return types.Select(t => model.FindEntityType(t)?.GetTableName()
            ?? throw new InvalidOperationException($"{t.Name} no está en el modelo de la base de datos reales.")).ToList();
    }

    private static string DeleteSql(TablePlan plan)
    {
        var sql = $"DELETE t FROM dbo.[{plan.Table}] AS t WHERE NOT EXISTS (SELECT 1 FROM {plan.Temp} AS s WHERE {On(plan.Key)})";
        if (plan.Rule.LocalRows is { } local) sql += $" AND NOT ({local})";
        foreach (var reference in plan.LocalReferences)
            sql += $" AND NOT EXISTS (SELECT 1 FROM dbo.[{reference.Table}] AS r WHERE {string.Join(" AND ", reference.Columns.Select(c => $"r.[{c.Child}] = t.[{c.Parent}]"))})";
        return sql + ";";
    }

    private static string? UpdateSql(TablePlan plan)
    {
        var columns = plan.Columns.Except(plan.Key, StringComparer.OrdinalIgnoreCase)
            .Except(plan.Rule.LocalColumns, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (columns.Count == 0) return null;

        string Source(string column) => plan.Rule.Expressions.GetValueOrDefault(column) ?? $"s.[{column}]";
        var sql = $"UPDATE t SET {string.Join(", ", columns.Select(c => $"t.[{c}] = {Source(c)}"))} " +
            $"FROM dbo.[{plan.Table}] AS t JOIN {plan.Temp} AS s ON {On(plan.Key)} " +
            $"WHERE EXISTS (SELECT {string.Join(", ", columns.Select(Source))} EXCEPT SELECT {string.Join(", ", columns.Select(c => $"t.[{c}]"))})";
        if (plan.Rule.LocalRows is { } local) sql += $" AND NOT ({local})";
        return sql + ";";
    }

    // Una fila nueva que chocaría con otra del entorno en un índice único (p. ej. una ficha del club con la misma
    // referencia) se deja fuera: la copia no se para por ella.
    private static string InsertSql(TablePlan plan)
    {
        var sql = $"INSERT INTO dbo.[{plan.Table}] ({List(plan.Columns)}) SELECT {string.Join(", ", plan.Columns.Select(c => $"s.[{c}]"))} " +
            $"FROM {plan.Temp} AS s WHERE NOT EXISTS (SELECT 1 FROM dbo.[{plan.Table}] AS t WHERE {On(plan.Key)})";
        foreach (var unique in plan.Uniques)
            sql += $" AND NOT EXISTS (SELECT 1 FROM dbo.[{plan.Table}] AS u WHERE {string.Join(" AND ", unique.Select(c => $"u.[{c}] = s.[{c}]"))})";
        return sql + ";";
    }

    private static string On(IReadOnlyList<string> key) => string.Join(" AND ", key.Select(k => $"s.[{k}] = t.[{k}]"));

    private static string List(IEnumerable<string> columns) => string.Join(", ", columns.Select(c => $"[{c}]"));

    private static string Join(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} y {parts[^1]}";

    private static async Task<List<string>> ColumnsAsync(SqlConnection connection, SqlTransaction? transaction, string table, CancellationToken cancellationToken) =>
        await ListAsync(connection, transaction,
            "SELECT c.name FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id " +
            "WHERE c.object_id = OBJECT_ID(@table) AND c.is_computed = 0 AND ty.name NOT IN ('timestamp', 'rowversion') ORDER BY c.column_id;",
            table, r => r.GetString(0), cancellationToken);

    private static async Task<List<string>> PrimaryKeyAsync(SqlConnection connection, SqlTransaction transaction, string table, CancellationToken cancellationToken) =>
        await ListAsync(connection, transaction,
            "SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
            "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE i.object_id = OBJECT_ID(@table) AND i.is_primary_key = 1 ORDER BY ic.key_ordinal;",
            table, r => r.GetString(0), cancellationToken);

    private static async Task<List<List<string>>> UniqueIndexesAsync(SqlConnection connection, SqlTransaction transaction, string table, CancellationToken cancellationToken)
    {
        var rows = await ListAsync(connection, transaction,
            "SELECT i.index_id, c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
            "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE i.object_id = OBJECT_ID(@table) AND i.is_unique = 1 AND i.is_primary_key = 0 AND ic.is_included_column = 0 ORDER BY i.index_id, ic.key_ordinal;",
            table, r => (Index: r.GetInt32(0), Column: r.GetString(1)), cancellationToken);
        return rows.GroupBy(r => r.Index).Select(g => g.Select(r => r.Column).ToList()).ToList();
    }

    // Claves ajenas que apuntan a la tabla desde tablas del entorno (las que no se copian).
    private static async Task<List<LocalReference>> LocalReferencesAsync(
        SqlConnection connection, SqlTransaction transaction, string table, IReadOnlyCollection<string> copied, CancellationToken cancellationToken)
    {
        var rows = await ListAsync(connection, transaction,
            "SELECT fk.object_id, OBJECT_NAME(fk.parent_object_id), pc.name, rc.name FROM sys.foreign_keys fk " +
            "JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
            "JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id " +
            "JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id " +
            "WHERE fk.referenced_object_id = OBJECT_ID(@table);",
            table, r => (Id: r.GetInt32(0), Child: r.GetString(1), ChildColumn: r.GetString(2), ParentColumn: r.GetString(3)), cancellationToken);
        return rows
            .Where(r => !copied.Contains(r.Child, StringComparer.OrdinalIgnoreCase))
            .GroupBy(r => (r.Id, r.Child))
            .Select(g => new LocalReference(g.Key.Child, g.Select(r => (r.ChildColumn, r.ParentColumn)).ToList()))
            .ToList();
    }

    private static async Task<bool> HasIdentityAsync(SqlConnection connection, SqlTransaction transaction, string table, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID(@table);", connection, transaction);
        command.Parameters.AddWithValue("@table", $"dbo.{table}");
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! > 0;
    }

    private static async Task<List<(Guid TeamId, string Key)>> TeamCrestsAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var crests = new List<(Guid, string)>();
        await using var reader = await ReadAsync(connection, transaction, "SELECT Id, CrestKey FROM dbo.Teams WHERE CrestKey IS NOT NULL;", cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) crests.Add((reader.GetGuid(0), reader.GetString(1)));
        return crests;
    }

    private static async Task<List<T>> ListAsync<T>(
        SqlConnection connection, SqlTransaction? transaction, string sql, string table, Func<SqlDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@table", $"dbo.{table}");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<T>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(map(reader));
        return items;
    }

    private static async Task<SqlDataReader> ReadAsync(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 300 };
        return await command.ExecuteReaderAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountAsync(SqlConnection connection, SqlTransaction transaction, string? sql, CancellationToken cancellationToken)
    {
        if (sql is null) return 0;
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 300 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    // LocalColumns: columnas del entorno que la copia no toca; LocalRows: filas del entorno que no toca; Expressions: el
    // valor que se copia cuando depende de lo que hay en el entorno (t) además de la base real (s).
    private sealed class TableRule(string[] LocalColumns, string? LocalRows = null, Dictionary<string, string>? Expressions = null)
    {
        public static readonly TableRule None = new([]);

        public string[] LocalColumns { get; } = LocalColumns;
        public string? LocalRows { get; } = LocalRows;
        public Dictionary<string, string> Expressions { get; } = Expressions ?? [];
    }

    private sealed record LocalReference(string Table, IReadOnlyList<(string Child, string Parent)> Columns);

    private sealed record TablePlan(
        string Table,
        string Temp,
        IReadOnlyList<string> Columns,
        IReadOnlyList<string> Key,
        IReadOnlyList<List<string>> Uniques,
        IReadOnlyList<LocalReference> LocalReferences,
        TableRule Rule);
}
