//using System;
//using System.Data;
//using System.Threading.Tasks;
//using Npgsql;
//using Oracle.ManagedDataAccess.Client;

//namespace Upsanctionscreener.Classess.Utils
//{
//    public class DatabaseReadResult
//    {
//        public bool Successful { get; set; }
//        public DataTable? Data { get; set; }
//        public string Message { get; set; } = "";
//    }

//    public static class DatabaseDataReader
//    {
//        private static string Quote(string identifier) => $"\"{identifier}\"";

//        public static async Task<DatabaseReadResult> ReadDatabaseRecords(
//            string query,
//            DatabaseSettings settings,
//            string foldername,
//            string filename
            
//            )

//        {

//            string batch_process = AppConfigFetcher.GetValue("BatchFetch")!;
//            bool process_in_batches = batch_process.Trim().ToLower() == "true";
//            Logger.LogToFile(foldername, filename, $"BatchFetch = {process_in_batches.ToString()}");
//            try
//            {
//                if (string.IsNullOrWhiteSpace(query))
//                    return Fail("Query is required.");

//                if (string.IsNullOrWhiteSpace(settings.ConnectionString))
//                    return Fail("Connection string is required.");

//                return settings.DatabaseType.Trim().ToLower() switch
//                {
//                    "postgresql" or "postgres" => process_in_batches? await ReadFromPostgresInBatches(query, settings, foldername, filename):await ReadFromPostgresWithoutBatches(query, settings, foldername, filename),
//                    "oracle" => process_in_batches? await ReadFromOracleInBatches(query, settings, foldername, filename): await ReadFromOracleWithoutBatches(query, settings, foldername, filename),
//                    _ => Fail($"Unsupported database type: '{settings.DatabaseType}'. Supported types are PostgreSQL and Oracle.")
//                };
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine(ex.Message);
//                return Fail(ex.Message);
//            }
//        }

//        // =========================
//        // POSTGRES
//        // =========================
//        //private static async Task<DatabaseReadResult> ReadFromPostgres(
//        //    string query,
//        //    DatabaseSettings settings,
//        //    string foldername,
//        //    string filename

//        //    )
//        //{
//        //    string conncection_string = settings.ConnectionString;
//        //    conncection_string = Cryptor.Decrypt(conncection_string, true);
//        //    Logger.LogToFile(foldername, filename, $"Executing quer{query}");
//        //    await using var connection = new NpgsqlConnection(conncection_string);
//        //    await connection.OpenAsync();

//        //    await using var command = new NpgsqlCommand(query, connection);

//        //    await using var dbReader = await command.ExecuteReaderAsync();

//        //    var table = new DataTable();
//        //    table.Load(dbReader);

//        //    return Ok(table);
//        //}

       

//        private static async Task<DatabaseReadResult> ReadFromOracleInBatches(
//    string query,
//    DatabaseSettings settings,
//    string foldername,
//    string filename,
//    int batchSize = 10000,
//    int maxRetries = 5,
//    int commandTimeoutSeconds = 60
   
            
//            )
//        {
//            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);
//            var fullTable = new DataTable();
//            bool schemaInitialized = false;
//            int offset = 0;

//            while (true)
//            {
//                string batchQuery = $"""
//            SELECT * FROM ({query})
//            OFFSET {offset} ROWS FETCH NEXT {batchSize} ROWS ONLY
//            """;
            
//                Logger.LogToFile(foldername, filename, $"Executing batch query with OFFSET {offset}... \n\n {batchQuery}");

//                DataTable? batchTable = await FetchOracleBatchWithRetry(
//                    batchQuery, connectionString, commandTimeoutSeconds, maxRetries);

//                if (batchTable == null)
//                    return Fail($"Batch at offset {offset} failed after {maxRetries} retries.");

//                // No more rows — we're done
//                if (batchTable.Rows.Count == 0)
//                    break;

//                // Initialize schema from first batch
//                if (!schemaInitialized)
//                {
//                    foreach (DataColumn col in batchTable.Columns)
//                        fullTable.Columns.Add(col.ColumnName, col.DataType);

//                    schemaInitialized = true;
//                }

//                // Merge batch into full table
//                foreach (DataRow row in batchTable.Rows)
//                    fullTable.ImportRow(row);

//                // If we got fewer rows than batchSize, this was the last batch
//                if (batchTable.Rows.Count < batchSize)
//                    break;

//                offset += batchSize;
//            }

//            return Ok(fullTable);
//        }

//        private static async Task<DataTable?> FetchOracleBatchWithRetryOldWorking(string batchQuery,string connectionString,int commandTimeoutSeconds, int maxRetries)
//        {
//            int attempt = 0;

//            while (attempt < maxRetries)
//            {
//                attempt++;
//                try
//                {
//                    await using var connection = new OracleConnection(connectionString);
//                    await connection.OpenAsync();

//                    await using var command = new OracleCommand(batchQuery, connection);
//                    command.CommandTimeout = commandTimeoutSeconds;

//                    await using var dbReader = await command.ExecuteReaderAsync();

//                    var table = new DataTable();

//                    for (int i = 0; i < dbReader.FieldCount; i++)
//                        table.Columns.Add(dbReader.GetName(i), dbReader.GetFieldType(i));

//                    while (await dbReader.ReadAsync())
//                    {
//                        var row = table.NewRow();
//                        for (int i = 0; i < dbReader.FieldCount; i++)
//                            row[i] = dbReader.IsDBNull(i) ? DBNull.Value : dbReader.GetValue(i);

//                        table.Rows.Add(row);
//                    }

//                    return table;
//                }
//                catch (OracleException ex) when (IsTransient(ex))
//                {
//                    if (attempt >= maxRetries)
//                        return null;

//                    int delay = (int)Math.Pow(2, attempt) * 500; // 1s, 2s, 4s
//                    await Task.Delay(delay);
//                }
//                catch (OracleException ex)
//                {
//                    // Non-transient — no point retrying
//                    throw new InvalidOperationException($"Non-transient Oracle error: {ex.Message}", ex);
//                }
//            }

//            return null;
//        }

//        private static async Task<DataTable?> FetchOracleBatchWithRetry(string batchQuery, string connectionString, int commandTimeoutSeconds, int maxRetries)
//        {
//            int attempt = 0;

//            while (attempt < maxRetries)
//            {
//                attempt++;
//                try
//                {
//                    await using var connection = new OracleConnection(connectionString);
//                    await connection.OpenAsync();

//                    await using var command = new OracleCommand(batchQuery, connection);
//                    command.CommandTimeout = commandTimeoutSeconds;

//                    await using var dbReader = await command.ExecuteReaderAsync();

//                    var table = new DataTable();

//                    for (int i = 0; i < dbReader.FieldCount; i++)
//                        table.Columns.Add(dbReader.GetName(i), dbReader.GetFieldType(i));

//                    while (await dbReader.ReadAsync())
//                    {
//                        var row = table.NewRow();
//                        for (int i = 0; i < dbReader.FieldCount; i++)
//                            row[i] = dbReader.IsDBNull(i) ? DBNull.Value : dbReader.GetValue(i);

//                        table.Rows.Add(row);
//                    }

//                    return table;
//                }
//                catch (OracleException ex) when (IsTransient(ex))
//                {
//                    if (attempt >= maxRetries)
//                        return null;

//                    int delay = (int)Math.Pow(2, attempt) * 500; // 1s, 2s, 4s
//                    await Task.Delay(delay);
//                }
//                catch (OracleException ex)
//                {
//                    // Non-transient — no point retrying
//                    throw new InvalidOperationException($"Non-transient Oracle error: {ex.Message}", ex);
//                }
//            }

//            return null;
//        }












//        private static async Task<DatabaseReadResult> ReadFromOracleWithoutBatches(
//      string query,
//      DatabaseSettings settings,
//      string foldername,
//      string filename,
//      int maxRetries = 5,
//      int commandTimeoutSeconds = 60)
//        {
//            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);

//            Logger.LogToFile(foldername, filename, $"Executing query: \n\n {query}");

//            DataTable? table = await FetchOracleBatchWithRetry(
//                query, connectionString, commandTimeoutSeconds, maxRetries);

//            if (table == null)
//                return Fail($"Query failed after {maxRetries} retries.");

//            return Ok(table);
//        }




//        private static async Task<DatabaseReadResult> ReadFromPostgresInBatches( string query, DatabaseSettings settings,string foldername, string filename, int batchSize = 10000,int maxRetries = 5, int commandTimeoutSeconds = 60)
//        {
//            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);
//            var fullTable = new DataTable();
//            bool schemaInitialized = false;
//            int offset = 0;

//            while (true)
//            {
//                string batchQuery = $"""
//        SELECT * FROM ({query}) AS batch_subquery
//        LIMIT {batchSize} OFFSET {offset}
//        """;

//                Logger.LogToFile(foldername, filename, $"Executing batch query with OFFSET {offset}... \n\n {batchQuery}");

//                DataTable? batchTable = await FetchPostgresBatchWithRetry(
//                    batchQuery, connectionString, commandTimeoutSeconds, maxRetries);

//                if (batchTable == null)
//                    return Fail($"Batch at offset {offset} failed after {maxRetries} retries.");

//                // No more rows — we're done
//                if (batchTable.Rows.Count == 0)
//                    break;

//                // Initialize schema from first batch
//                if (!schemaInitialized)
//                {
//                    foreach (DataColumn col in batchTable.Columns)
//                        fullTable.Columns.Add(col.ColumnName, col.DataType);

//                    schemaInitialized = true;
//                }

//                // Merge batch into full table
//                foreach (DataRow row in batchTable.Rows)
//                    fullTable.ImportRow(row);

//                // If we got fewer rows than batchSize, this was the last batch
//                if (batchTable.Rows.Count < batchSize)
//                    break;

//                offset += batchSize;
//            }

//            return Ok(fullTable);
//        }


//        private static async Task<DataTable?> FetchPostgresBatchWithRetry(
//    string batchQuery,
//    string connectionString,
//    int commandTimeoutSeconds,
//    int maxRetries)
//        {
//            int attempt = 0;

//            while (attempt < maxRetries)
//            {
//                attempt++;
//                try
//                {
//                    await using var connection = new NpgsqlConnection(connectionString);
//                    await connection.OpenAsync();

//                    await using var command = new NpgsqlCommand(batchQuery, connection);
//                    command.CommandTimeout = commandTimeoutSeconds;

//                    await using var dbReader = await command.ExecuteReaderAsync();

//                    var table = new DataTable();
//                    table.Load(dbReader);

//                    return table;
//                }
//                catch (NpgsqlException ex) when (IsTransient(ex))
//                {
//                    if (attempt >= maxRetries)
//                        return null;

//                    int delay = (int)Math.Pow(2, attempt) * 500; // 1s, 2s, 4s
//                    await Task.Delay(delay);
//                }
//                catch (NpgsqlException ex)
//                {
//                    // Non-transient — no point retrying
//                    throw new InvalidOperationException($"Non-transient Postgres error: {ex.Message}", ex);
//                }
//            }

//            return null;
//        }

//        private static async Task<DatabaseReadResult> ReadFromPostgresWithoutBatches(
//            string query,
//            DatabaseSettings settings,
//            string foldername,
//            string filename,
//            int maxRetries = 5,
//            int commandTimeoutSeconds = 60)
//        {
//            string connectionString = Cryptor.Decrypt(settings.ConnectionString, true);

//            Logger.LogToFile(foldername, filename, $"Executing query: \n\n {query}");

//            DataTable? table = await FetchPostgresBatchWithRetry(
//                query, connectionString, commandTimeoutSeconds, maxRetries);

//            if (table == null)
//                return Fail($"Query failed after {maxRetries} retries.");

//            return Ok(table);
//        }

//        private static bool IsTransient(NpgsqlException ex) => ex.SqlState switch
//        {
//            "08000" => true, // connection_exception
//            "08003" => true, // connection_does_not_exist
//            "08006" => true, // connection_failure
//            "08001" => true, // sqlclient_unable_to_establish_sqlconnection
//            "08004" => true, // sqlserver_rejected_establishment_of_sqlconnection
//            "40001" => true, // serialization_failure
//            "40P01" => true, // deadlock_detected
//            "57P03" => true, // cannot_connect_now (server starting up)
//            "53300" => true, // too_many_connections
//            _ => false
//        };






//        private static bool IsTransient(OracleException ex) => ex.Number switch
//        {
//            50000 => true,
//            12203 => true,
//            12541 => true,
//            12170 => true,
//            3113 => true,
//            3135 => true,
//            12570 => true,   // ← ADD THIS: TNS packet reader failure
//            _ => false
//        };









//        // =========================
//        // HELPERS
//        // =========================
//        private static DatabaseReadResult Ok(DataTable data) =>
//            new() { Successful = true, Data = data, Message = "Records retrieved successfully." };

//        private static DatabaseReadResult Fail(string error) =>
//            new() { Successful = false, Message = error };




//        public static class DatabaseQueryBuilder
//        {
          

//            private static string Quote(string identifier) => $"\"{identifier}\"";


//            public static string BuildSelectQuery(DataSettings settings, AutomationSettings automation_settings, string lasttrackedtime)
//            {
//                if (string.IsNullOrWhiteSpace(settings.TableName))
//                    throw new ArgumentException("TableName is required.");

//                if (string.IsNullOrWhiteSpace(settings.IdColumn))
//                    throw new ArgumentException("IdColumn is required.");

//                var columns = new List<string> { Quote(settings.IdColumn) };

//                if (settings.OtherFields != null && settings.OtherFields.Count > 0)
//                {
//                    foreach (var field in settings.OtherFields)
//                    {
//                        if (!string.IsNullOrWhiteSpace(field.ColumnName) && !string.IsNullOrWhiteSpace(field.MatchAs))
//                            columns.Add(Quote(field.ColumnName));
//                    }
//                }

//                if (automation_settings.TrackTime)
//                {

//                    columns.Add(Quote(automation_settings.TimeColumn));
//                    string columnListtracked = string.Join($",{Environment.NewLine}  ", columns);

//                    string time_track_duration = automation_settings.Frequency == "minutely" ? "minutes" : "hours";

//                    if (string.IsNullOrEmpty(lasttrackedtime))
//                    {
//                        return $"""
//                        SELECT
//                          {columnListtracked}
//                        FROM {settings.TableName}
//                        WHERE {Quote(automation_settings.TimeColumn)} >= NOW() - INTERVAL '{automation_settings.IntervalMinutes} {time_track_duration}'
//                        ORDER BY {Quote(settings.IdColumn)}
//                        """;
//                    }
//                    else
//                    {
//                        return $"""
//                        SELECT
//                          {columnListtracked}
//                        FROM {settings.TableName}
//                        WHERE {Quote(automation_settings.TimeColumn)} >= '{lasttrackedtime.Trim()}'
//                        ORDER BY {Quote(settings.IdColumn)}
//                        """;

//                    }



//                }

                

                

//                // No semicolon — safe for subquery wrapping
//                // ORDER BY IdColumn — guarantees stable pagination across batches
//                 if (automation_settings.UseCustomQuery)
//                {
//                    return automation_settings.CustomQuery;
//                }
//                string columnList = string.Join($",{Environment.NewLine}  ", columns);
//                return $"SELECT{Environment.NewLine}  {columnList}{Environment.NewLine}FROM {settings.TableName}{Environment.NewLine}ORDER BY {Quote(settings.IdColumn)}";

//            }
//        }


//    }
//}