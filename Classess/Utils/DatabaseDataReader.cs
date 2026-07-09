using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Microsoft.Data.SqlClient;

namespace Upsanctionscreener.Classess.Utils
{
    public class DatabaseReadResult
    {
        public bool Successful { get; set; }
        public DataTable? Data { get; set; }
        public string Message { get; set; } = "";
    }

    public static class DatabaseDataReader
    {
        private static string Quote(string identifier) => $"\"{identifier}\"";

        public static async Task<DatabaseReadResult> ReadDatabaseRecords(
              string query,
              DatabaseSettings settings,
              string foldername,
              string filename
              )
        {
            string batch_process = AppConfigFetcher.GetValue("BatchFetch")!;
            bool process_in_batches = batch_process.Trim().ToLower() == "true";
            Logger.LogToFile(foldername, filename, $"BatchFetch = {process_in_batches.ToString()}");
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return Fail("Query is required.");

                if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                    return Fail("Connection string is required.");

                return settings.DatabaseType.Trim().ToLower() switch
                {
                    "postgresql" or "postgres" => process_in_batches ? await ReadFromPostgresInBatches(query, settings, foldername, filename) : await ReadFromPostgresWithoutBatches(query, settings, foldername, filename),
                    "oracle" => process_in_batches ? await ReadFromOracleInBatches(query, settings, foldername, filename) : await ReadFromOracleWithoutBatches(query, settings, foldername, filename),
                    "mssql" or "sqlserver" or "microsoft sql server" => process_in_batches ? await ReadFromSqlServerInBatches(query, settings, foldername, filename) : await ReadFromSqlServerWithoutBatches(query, settings, foldername, filename),
                    _ => Fail($"Unsupported database type: '{settings.DatabaseType}'. Supported types are PostgreSQL, Oracle, and MSSQL.")
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return Fail(ex.Message);
            }
        }





        private static async Task<DatabaseReadResult> ReadFromOracleInBatches(
    string query,
    DatabaseSettings settings,
    string foldername,
    string filename,
    int batchSize = 10000,
    int maxRetries = 5,
    int commandTimeoutSeconds = 60
   
            
            )
        {
            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);
            var fullTable = new DataTable();
            bool schemaInitialized = false;
            int offset = 0;

            while (true)
            {
                string batchQuery = $"""
            SELECT * FROM ({query})
            OFFSET {offset} ROWS FETCH NEXT {batchSize} ROWS ONLY
            """;
            
                Logger.LogToFile(foldername, filename, $"Executing batch query with OFFSET {offset}... \n\n {batchQuery}");

                DataTable? batchTable = await FetchOracleBatchWithRetry(
                    batchQuery, connectionString, commandTimeoutSeconds, maxRetries);

                if (batchTable == null)
                    return Fail($"Batch at offset {offset} failed after {maxRetries} retries.");

                // No more rows — we're done
                if (batchTable.Rows.Count == 0)
                    break;

                // Initialize schema from first batch
                if (!schemaInitialized)
                {
                    foreach (DataColumn col in batchTable.Columns)
                        fullTable.Columns.Add(col.ColumnName, col.DataType);

                    schemaInitialized = true;
                }

                // Merge batch into full table
                foreach (DataRow row in batchTable.Rows)
                    fullTable.ImportRow(row);

                // If we got fewer rows than batchSize, this was the last batch
                if (batchTable.Rows.Count < batchSize)
                    break;

                offset += batchSize;
            }

            return Ok(fullTable);
        }
        private static async Task<DataTable?> FetchOracleBatchWithRetry(string batchQuery, string connectionString, int commandTimeoutSeconds, int maxRetries)
        {
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    await using var connection = new OracleConnection(connectionString);
                    await connection.OpenAsync();

                    await using var command = new OracleCommand(batchQuery, connection);
                    command.CommandTimeout = commandTimeoutSeconds;

                    await using var dbReader = await command.ExecuteReaderAsync();

                    var table = new DataTable();

                    for (int i = 0; i < dbReader.FieldCount; i++)
                        table.Columns.Add(dbReader.GetName(i), dbReader.GetFieldType(i));

                    while (await dbReader.ReadAsync())
                    {
                        var row = table.NewRow();
                        for (int i = 0; i < dbReader.FieldCount; i++)
                            row[i] = dbReader.IsDBNull(i) ? DBNull.Value : dbReader.GetValue(i);

                        table.Rows.Add(row);
                    }

                    return table;
                }
                catch (OracleException ex) when (IsTransient(ex))
                {
                    if (attempt >= maxRetries)
                        return null;

                    int delay = (int)Math.Pow(2, attempt) * 500; // 1s, 2s, 4s
                    await Task.Delay(delay);
                }
                catch (OracleException ex)
                {
                    // Non-transient — no point retrying
                    throw new InvalidOperationException($"Non-transient Oracle error: {ex.Message}", ex);
                }
            }

            return null;
        }
        private static async Task<DatabaseReadResult> ReadFromOracleWithoutBatches(
      string query,
      DatabaseSettings settings,
      string foldername,
      string filename,
      int maxRetries = 5,
      int commandTimeoutSeconds = 60)
        {
            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);

            Logger.LogToFile(foldername, filename, $"Executing query: \n\n {query}");

            DataTable? table = await FetchOracleBatchWithRetry(
                query, connectionString, commandTimeoutSeconds, maxRetries);

            if (table == null)
                return Fail($"Query failed after {maxRetries} retries.");

            return Ok(table);
        }




        private static async Task<DatabaseReadResult> ReadFromPostgresInBatches( string query, DatabaseSettings settings,string foldername, string filename, int batchSize = 10000,int maxRetries = 5, int commandTimeoutSeconds = 60)
        {
            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);
            var fullTable = new DataTable();
            bool schemaInitialized = false;
            int offset = 0;

            while (true)
            {
                string batchQuery = $"""
        SELECT * FROM ({query}) AS batch_subquery
        LIMIT {batchSize} OFFSET {offset}
        """;

                Logger.LogToFile(foldername, filename, $"Executing batch query with OFFSET {offset}... \n\n {batchQuery}");

                DataTable? batchTable = await FetchPostgresBatchWithRetry(
                    batchQuery, connectionString, commandTimeoutSeconds, maxRetries);

                if (batchTable == null)
                    return Fail($"Batch at offset {offset} failed after {maxRetries} retries.");

                // No more rows — we're done
                if (batchTable.Rows.Count == 0)
                    break;

                // Initialize schema from first batch
                if (!schemaInitialized)
                {
                    foreach (DataColumn col in batchTable.Columns)
                        fullTable.Columns.Add(col.ColumnName, col.DataType);

                    schemaInitialized = true;
                }

                // Merge batch into full table
                foreach (DataRow row in batchTable.Rows)
                    fullTable.ImportRow(row);

                // If we got fewer rows than batchSize, this was the last batch
                if (batchTable.Rows.Count < batchSize)
                    break;

                offset += batchSize;
            }

            return Ok(fullTable);
        }
        private static async Task<DataTable?> FetchPostgresBatchWithRetry(
    string batchQuery,
    string connectionString,
    int commandTimeoutSeconds,
    int maxRetries)
        {
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();

                    await using var command = new NpgsqlCommand(batchQuery, connection);
                    command.CommandTimeout = commandTimeoutSeconds;

                    await using var dbReader = await command.ExecuteReaderAsync();

                    var table = new DataTable();
                    table.Load(dbReader);

                    return table;
                }
                catch (NpgsqlException ex) when (IsTransient(ex))
                {
                    if (attempt >= maxRetries)
                        return null;

                    int delay = (int)Math.Pow(2, attempt) * 500; // 1s, 2s, 4s
                    await Task.Delay(delay);
                }
                catch (NpgsqlException ex)
                {
                    // Non-transient — no point retrying
                    throw new InvalidOperationException($"Non-transient Postgres error: {ex.Message}", ex);
                }
            }

            return null;
        }
        private static async Task<DatabaseReadResult> ReadFromPostgresWithoutBatches(
            string query,
            DatabaseSettings settings,
            string foldername,
            string filename,
            int maxRetries = 5,
            int commandTimeoutSeconds = 60)
        {
            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);

            Logger.LogToFile(foldername, filename, $"Executing query: \n\n {query}");

            DataTable? table = await FetchPostgresBatchWithRetry(
                query, connectionString, commandTimeoutSeconds, maxRetries);

            if (table == null)
                return Fail($"Query failed after {maxRetries} retries.");

            return Ok(table);
        }


        private static async Task<DatabaseReadResult> ReadFromSqlServerInBatches(
    string query,
    DatabaseSettings settings,
    string foldername,
    string filename,
    int batchSize = 10000,
    int maxRetries = 5,
    int commandTimeoutSeconds = 60)
        {
            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);
            var fullTable = new DataTable();
            bool schemaInitialized = false;
            int offset = 0;

            while (true)
            {
                // NOTE: query is expected to already end with an ORDER BY clause
                // (BuildSelectQuery guarantees this). SQL Server requires ORDER BY
                // to use OFFSET/FETCH, and rejects a bare ORDER BY inside a derived
                // table — so we append pagination directly instead of wrapping the
                // query in "SELECT * FROM (query) AS x" like the Postgres/Oracle paths do.
                string batchQuery = $"""
        {query}
        OFFSET {offset} ROWS FETCH NEXT {batchSize} ROWS ONLY
        """;

                Logger.LogToFile(foldername, filename, $"Executing batch query with OFFSET {offset}... \n\n {batchQuery}");

                DataTable? batchTable = await FetchSqlServerBatchWithRetry(
                    batchQuery, connectionString, commandTimeoutSeconds, maxRetries);

                if (batchTable == null)
                    return Fail($"Batch at offset {offset} failed after {maxRetries} retries.");

                if (batchTable.Rows.Count == 0)
                    break;

                if (!schemaInitialized)
                {
                    foreach (DataColumn col in batchTable.Columns)
                        fullTable.Columns.Add(col.ColumnName, col.DataType);

                    schemaInitialized = true;
                }

                foreach (DataRow row in batchTable.Rows)
                    fullTable.ImportRow(row);

                if (batchTable.Rows.Count < batchSize)
                    break;

                offset += batchSize;
            }

            return Ok(fullTable);
        }

        private static async Task<DataTable?> FetchSqlServerBatchWithRetry(
    string batchQuery,
    string connectionString,
    int commandTimeoutSeconds,
    int maxRetries)
        {
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    await using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

                    await using var command = new SqlCommand(batchQuery, connection);
                    command.CommandTimeout = commandTimeoutSeconds;

                    await using var dbReader = await command.ExecuteReaderAsync();

                    var table = new DataTable();
                    table.Load(dbReader);

                    return table;
                }
                catch (SqlException ex) when (IsTransient(ex))
                {
                    if (attempt >= maxRetries)
                        return null;

                    int delay = (int)Math.Pow(2, attempt) * 500; // 1s, 2s, 4s
                    await Task.Delay(delay);
                }
                catch (SqlException ex)
                {
                    // Non-transient — no point retrying
                    throw new InvalidOperationException($"Non-transient SQL Server error: {ex.Message}", ex);
                }
            }

            return null;
        }

        private static async Task<DatabaseReadResult> ReadFromSqlServerWithoutBatches(
    string query,
    DatabaseSettings settings,
    string foldername,
    string filename,
    int maxRetries = 5,
    int commandTimeoutSeconds = 60)
        {
            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);

            Logger.LogToFile(foldername, filename, $"Executing query: \n\n {query}");

            DataTable? table = await FetchSqlServerBatchWithRetry(
                query, connectionString, commandTimeoutSeconds, maxRetries);

            if (table == null)
                return Fail($"Query failed after {maxRetries} retries.");

            return Ok(table);
        }













        private static bool IsTransient(NpgsqlException ex) => ex.SqlState switch
        {
            "08000" => true, // connection_exception
            "08003" => true, // connection_does_not_exist
            "08006" => true, // connection_failure
            "08001" => true, // sqlclient_unable_to_establish_sqlconnection
            "08004" => true, // sqlserver_rejected_establishment_of_sqlconnection
            "40001" => true, // serialization_failure
            "40P01" => true, // deadlock_detected
            "57P03" => true, // cannot_connect_now (server starting up)
            "53300" => true, // too_many_connections
            _ => false
        };
        private static bool IsTransient(OracleException ex) => ex.Number switch
        {
            50000 => true,
            12203 => true,
            12541 => true,
            12170 => true,
            3113 => true,
            3135 => true,
            12570 => true,   // ← ADD THIS: TNS packet reader failure
            _ => false
        };
        private static bool IsTransient(SqlException ex) => ex.Number switch
        {
            -2 => true,     // Timeout expired
            -1 => true,     // Connection broken
            2 => true,      // Timeout
            53 => true,     // Network path not found
            233 => true,    // No process on the other end of the pipe (connection reset)
            10053 => true,  // Transport-level error (connection aborted)
            10054 => true,  // Transport-level error (connection reset)
            10060 => true,  // Network/connection timeout
            40197 => true,  // Azure SQL: service busy
            40501 => true,  // Azure SQL: service busy
            40613 => true,  // Azure SQL: database unavailable
            49918 => true,  // Azure SQL: not enough resources
            49919 => true,  // Azure SQL: too many operations in progress
            49920 => true,  // Azure SQL: too many requests
            _ => false
        };







        // =========================
        // HELPERS
        // =========================
        private static DatabaseReadResult Ok(DataTable data) =>
            new() { Successful = true, Data = data, Message = "Records retrieved successfully." };

        private static DatabaseReadResult Fail(string error) =>
            new() { Successful = false, Message = error };




        public static class DatabaseQueryBuilder
        {
          

            private static string Quote(string identifier) => $"\"{identifier}\"";


            //public static string BuildSelectQuery(DatabaseSettings dbsettings,  AutomationSettings automation_settings, string lasttrackedtime)
            //{


            //    DataSettings settings = dbsettings.DataSettings;
            //    string database_type = dbsettings.DatabaseType;
            //    if (string.IsNullOrWhiteSpace(settings.TableName))
            //        throw new ArgumentException("TableName is required.");

            //    if (string.IsNullOrWhiteSpace(settings.IdColumn))
            //        throw new ArgumentException("IdColumn is required.");

            //    var columns = new List<string> { Quote(settings.IdColumn) };

            //    if (settings.OtherFields != null && settings.OtherFields.Count > 0)
            //    {
            //        foreach (var field in settings.OtherFields)
            //        {
            //            if (!string.IsNullOrWhiteSpace(field.ColumnName) && !string.IsNullOrWhiteSpace(field.MatchAs))
            //                columns.Add(Quote(field.ColumnName));
            //        }
            //    }

            //    if (automation_settings.TrackTime)
            //    {

            //        columns.Add(Quote(automation_settings.TimeColumn));
            //        string columnListtracked = string.Join($",{Environment.NewLine}  ", columns);

            //        string time_track_duration = automation_settings.Frequency == "minutely" ? "minutes" : "hours";

            //        if (string.IsNullOrEmpty(lasttrackedtime))
            //        {
            //            if (database_type == "MSSQL")
            //            {
            //                // SQL Server has no NOW() / INTERVAL syntax — use DATEADD instead.
            //                // DATEADD's datepart is an unquoted keyword, so map the unit to
            //                // its singular form ("minute" / "hour"), not "minutes" / "hours".
            //                string datePart = automation_settings.Frequency == "minutely" ? "minute" : "hour";

            //                            return $"""
            //                SELECT
            //                  {columnListtracked}
            //                FROM {settings.TableName}
            //                WHERE {Quote(automation_settings.TimeColumn)} >= DATEADD({datePart}, -{automation_settings.IntervalMinutes}, GETDATE())
            //                ORDER BY {Quote(settings.IdColumn)}
            //                """;
            //            }


            //            //else return this   
            //            return $"""
            //            SELECT
            //              {columnListtracked}
            //            FROM {settings.TableName}
            //            WHERE {Quote(automation_settings.TimeColumn)} >= NOW() - INTERVAL '{automation_settings.IntervalMinutes} {time_track_duration}'
            //            ORDER BY {Quote(settings.IdColumn)}
            //            """;
            //        }
            //        else
            //        {

            //            if (database_type == "MSSQL")//return microsoft sql server equvalent of the command bellow

            //                //else return this     
            //                return $"""
            //            SELECT
            //              {columnListtracked}
            //            FROM {settings.TableName}
            //            WHERE {Quote(automation_settings.TimeColumn)} >= '{lasttrackedtime.Trim()}'
            //            ORDER BY {Quote(settings.IdColumn)}
            //            """;

            //        }



            //    }

            //    // No semicolon — safe for subquery wrapping
            //    // ORDER BY IdColumn — guarantees stable pagination across batches
            //    if (automation_settings.UseCustomQuery)
            //    {
            //        return automation_settings.CustomQuery;
            //    }


            //    string columnList = string.Join($",{Environment.NewLine}  ", columns);

            //    if (database_type == "MSSQL")//return microsoft sql server equvalent of the command bellow
            //     //else return this     
            //     return $"SELECT{Environment.NewLine}  {columnList}{Environment.NewLine}FROM {settings.TableName}{Environment.NewLine}ORDER BY {Quote(settings.IdColumn)}";

            //}


            public static string BuildSelectQuery(DatabaseSettings dbsettings, AutomationSettings automation_settings, string lasttrackedtime)
            {
                DataSettings settings = dbsettings.DataSettings;
                string database_type = dbsettings.DatabaseType;
                if (string.IsNullOrWhiteSpace(settings.TableName))
                    throw new ArgumentException("TableName is required.");

                if (string.IsNullOrWhiteSpace(settings.IdColumn))
                    throw new ArgumentException("IdColumn is required.");

                var columns = new List<string> { Quote(settings.IdColumn) };

                if (settings.OtherFields != null && settings.OtherFields.Count > 0)
                {
                    foreach (var field in settings.OtherFields)
                    {
                        if (!string.IsNullOrWhiteSpace(field.ColumnName) && !string.IsNullOrWhiteSpace(field.MatchAs))
                            columns.Add(Quote(field.ColumnName));
                    }
                }

                if (automation_settings.TrackTime)
                {
                    columns.Add(Quote(automation_settings.TimeColumn));
                    string columnListtracked = string.Join($",{Environment.NewLine}  ", columns);

                    if (string.IsNullOrEmpty(lasttrackedtime))
                    {
                        if (database_type == "MSSQL")
                        {
                            // SQL Server has no NOW() / INTERVAL syntax — use DATEADD instead.
                            // DATEADD's datepart is an unquoted keyword, so map the unit to
                            // its singular form ("minute" / "hour"), not "minutes" / "hours".
                            string datePart = automation_settings.Frequency == "minutely" ? "minute" : "hour";

                            return $"""
                SELECT
                  {columnListtracked}
                FROM {settings.TableName}
                WHERE {Quote(automation_settings.TimeColumn)} >= DATEADD({datePart}, -{automation_settings.IntervalMinutes}, GETDATE())
                ORDER BY {Quote(settings.IdColumn)}
                """;
                        }

                        string time_track_duration = automation_settings.Frequency == "minutely" ? "minutes" : "hours";

                        return $"""
            SELECT
              {columnListtracked}
            FROM {settings.TableName}
            WHERE {Quote(automation_settings.TimeColumn)} >= NOW() - INTERVAL '{automation_settings.IntervalMinutes} {time_track_duration}'
            ORDER BY {Quote(settings.IdColumn)}
            """;
                    }
                    else
                    {
                        if (database_type == "MSSQL")
                        {
                            // Comparing against a literal timestamp string is ANSI SQL —
                            // identical on SQL Server, so no rewrite needed here.
                            return $"""
                SELECT
                  {columnListtracked}
                FROM {settings.TableName}
                WHERE {Quote(automation_settings.TimeColumn)} >= '{lasttrackedtime.Trim()}'
                ORDER BY {Quote(settings.IdColumn)}
                """;
                        }

                        return $"""
            SELECT
              {columnListtracked}
            FROM {settings.TableName}
            WHERE {Quote(automation_settings.TimeColumn)} >= '{lasttrackedtime.Trim()}'
            ORDER BY {Quote(settings.IdColumn)}
            """;
                    }
                }

                // No semicolon — safe for subquery wrapping
                // ORDER BY IdColumn — guarantees stable pagination across batches
                if (automation_settings.UseCustomQuery)
                {
                    return automation_settings.CustomQuery;
                }

                string columnList = string.Join($",{Environment.NewLine}  ", columns);

                if (database_type == "MSSQL")
                {
                    // Plain SELECT ... ORDER BY is identical on SQL Server — no rewrite needed.
                    return $"SELECT{Environment.NewLine}  {columnList}{Environment.NewLine}FROM {settings.TableName}{Environment.NewLine}ORDER BY {Quote(settings.IdColumn)}";
                }

                return $"SELECT{Environment.NewLine}  {columnList}{Environment.NewLine}FROM {settings.TableName}{Environment.NewLine}ORDER BY {Quote(settings.IdColumn)}";
            }













            //space
        }





    }
}