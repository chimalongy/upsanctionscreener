using System;
using System.Data;
using System.Threading.Tasks;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

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
        public static async Task<DatabaseReadResult> ReadDatabaseRecords(
            string query,
            DatabaseSettings settings)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return Fail("Query is required.");

                if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                    return Fail("Connection string is required.");

                return settings.DatabaseType.Trim().ToLower() switch
                {
                    "postgresql" or "postgres" => await ReadFromPostgres(query, settings),
                    "oracle" => await ReadFromOracle(query, settings),
                    _ => Fail($"Unsupported database type: '{settings.DatabaseType}'. Supported types are PostgreSQL and Oracle.")
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // =========================
        // POSTGRES
        // =========================
        private static async Task<DatabaseReadResult> ReadFromPostgres(
            string query,
            DatabaseSettings settings)
        {
            string conncection_string = settings.ConnectionString;
            conncection_string = Cryptor.Decrypt(conncection_string, true);

            await using var connection = new NpgsqlConnection(conncection_string);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(query, connection);

            await using var dbReader = await command.ExecuteReaderAsync();

            var table = new DataTable();
            table.Load(dbReader);

            return Ok(table);
        }

        // =========================
        // ORACLE
        // =========================
        private static async Task<DatabaseReadResult> ReadFromOracle(
            string query,
            DatabaseSettings settings)
        {
            string conncection_string = settings.ConnectionString;
            conncection_string = Cryptor.Decrypt(conncection_string,true);

            await using var connection = new OracleConnection(conncection_string);
            await connection.OpenAsync();

            await using var command = new OracleCommand(query, connection);

            await using var dbReader = await command.ExecuteReaderAsync();

            var table = new DataTable();
            table.Load(dbReader);

            return Ok(table);
        }

      

        // =========================
        // HELPERS
        // =========================
        private static DatabaseReadResult Ok(DataTable data) =>
            new() { Successful = true, Data = data, Message = "Records retrieved successfully." };

        private static DatabaseReadResult Fail(string error) =>
            new() { Successful = false, Message = error };




        public static class DatabaseQueryBuilder
        {
            public static string BuildSelectQuery(DataSettings settings)
            {
                if (string.IsNullOrWhiteSpace(settings.TableName))
                    throw new ArgumentException("TableName is required.");

                if (string.IsNullOrWhiteSpace(settings.IdColumn))
                    throw new ArgumentException("IdColumn is required.");

                // ID column always aliased as "ID"
                var columns = new List<string> { $"{Quote(settings.IdColumn)} AS ID" };

                // Other fields aliased using their match_as value
                if (settings.OtherFields != null && settings.OtherFields.Count > 0)
                {
                    foreach (var field in settings.OtherFields)
                    {
                        if (!string.IsNullOrWhiteSpace(field.ColumnName) && !string.IsNullOrWhiteSpace(field.MatchAs))
                            columns.Add($"{Quote(field.ColumnName)} AS {field.MatchAs}");
                    }
                }

                string columnList = string.Join($",{Environment.NewLine}  ", columns);

                return $"SELECT{Environment.NewLine}  {columnList}{Environment.NewLine}FROM {Quote(settings.TableName)};";
            }

            private static string Quote(string identifier) => $"\"{identifier}\"";
        }



    }
}