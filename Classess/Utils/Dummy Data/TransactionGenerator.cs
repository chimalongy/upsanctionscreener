using Npgsql;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Upsanctionscreener.Classess.Utils;

// Note: reuses the DatabaseType enum already declared alongside MerchantGenerator.
// If this ends up in a project that doesn't have it yet, uncomment:
// public enum DatabaseType { Postgres, Oracle }

public class Transaction
{
    public string Account { get; set; }
    public DateTime Initiated { get; set; }
    public string Status { get; set; }
    public string Details { get; set; }
}

public static class TransactionGenerator
{
    private static readonly Random Rng = new Random();

    // (bankCode, bankName) pairs seen in the real feed, plus common Nigerian banks.
    private static readonly (string Code, string Name)[] Banks =
    {
        ("044", "ACCESS BANK"),
        ("058", "GTBANK"),
        ("057", "ZENITH BANK"),
        ("011", "FIRST BANK"),
        ("033", "UBA"),
        ("035", "WEMA BANK"),
        ("232", "STERLING BANK"),
        ("070", "FIDELITY BANK"),
        ("214", "FCMB"),
        ("100004", "OPAY"),
        ("609", "PALMPAY"),
        ("50515", "MONIEPOINT"),
    };

    private static readonly string[] Names =
    {
        "SYLVESTER PETER AMEH",
        "TITILOPE MONSURAT BELLO",
        "MUHAMMAD IBRAHIM AMINU",
        "DAVID IYINOLUWA OYINLOYE",
        "CHIOMA ADAEZE OKAFOR",
        "EMEKA JOHNSON NWOSU",
        "FATIMA ABDULLAHI YUSUF",
        "BLESSING OGHENEKARO EFE",
        "IBRAHIM MUSA DANJUMA",
        "GRACE CHIDINMA EZE",
    };

    private static readonly string[] Narrations =
    {
        "transfer",
        "airtime 100 mtn",
        "data purchase",
        "bills payment",
        "test",
        "",
    };

    public static List<Transaction> GenerateTransactions(int count = 100)
    {
        var transactions = new List<Transaction>();

        for (int i = 0; i < count; i++)
        {
            transactions.Add(BuildTransaction());
        }

        return transactions;
    }

    private static Transaction BuildTransaction()
    {
        var details = Rng.Next(2) == 0
            ? BuildSenderDetails()
            : BuildBeneficiaryDetails();

        return new Transaction
        {
            Account = GenerateAccountNumber(),
            Initiated = DateTime.Now,
            Status = "Approved", // status is always Approved
            Details = details    // details is always populated JSON
        };
    }

    private static string GenerateAccountNumber()
    {
        // 10-digit account number, first digit non-zero.
        var first = Rng.Next(1, 10);
        var rest = Rng.NextInt64(0, 1_000_000_000); // up to 9 more digits
        return $"{first}{rest:D9}";
    }

    private static string BuildSenderDetails()
    {
        var bank = Banks[Rng.Next(Banks.Length)];
        var name = Names[Rng.Next(Names.Length)];
        var narration = Narrations[Rng.Next(Narrations.Length)];

        var payload = new
        {
            senderName = name,
            senderBank = bank.Code,
            narration,
            senderBankName = bank.Name,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildBeneficiaryDetails()
    {
        var bank = Banks[Rng.Next(Banks.Length)];
        var name = Names[Rng.Next(Names.Length)];
        var narration = Narrations[Rng.Next(Narrations.Length)];

        var payload = new
        {
            beneficiaryBank = bank.Code,
            beneficiaryBankName = bank.Name,
            beneficiaryName = name,
            narration,
        };

        return JsonSerializer.Serialize(payload);
    }

    public static async Task InsertTransactionsAsync(
        DatabaseType dbType,
        string connectionString,
        List<Transaction> transactions,
        string schemaTable = "public.transactions2")
    {
        connectionString = Cryptor.Decrypt(connectionString, true);

        if (transactions == null || transactions.Count == 0)
            return;

        switch (dbType)
        {
            case DatabaseType.Postgres:
                await InsertTransactionsPostgresAsync(connectionString, transactions, schemaTable);
                break;

            case DatabaseType.Oracle:
                // This generator is Postgres-focused for now — extend here the same way
                // InsertMerchantsOracleAsync does (MERGE ... WHEN NOT MATCHED) if/when needed.
                throw new NotSupportedException("TransactionGenerator: Oracle insert path is not implemented yet.");

            default:
                throw new Exception("Unsupported database type");
        }
    }

    private static async Task InsertTransactionsPostgresAsync(
        string connectionString,
        List<Transaction> transactions,
        string schemaTable)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        using var transactionScope = await conn.BeginTransactionAsync();

        try
        {
            // One INSERT per transaction — executed sequentially, not batched —
            // so rows are added one at a time while still landing quickly enough
            // that several fall within the same second.
            string sql = $@"
                INSERT INTO {schemaTable} (account, initiated, status, details)
                VALUES (@account, @initiated, @status, @details);
            ";

            foreach (var txn in transactions)
            {
                using var cmd = new NpgsqlCommand(sql, conn, transactionScope);
                cmd.Parameters.AddWithValue("@account", txn.Account);
                cmd.Parameters.AddWithValue("@initiated", txn.Initiated);
                cmd.Parameters.AddWithValue("@status", txn.Status);
                cmd.Parameters.AddWithValue("@details", txn.Details);

                await cmd.ExecuteNonQueryAsync();
            }

            await transactionScope.CommitAsync();
        }
        catch
        {
            await transactionScope.RollbackAsync();
            throw;
        }
    }
}