using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using Upsanctionscreener.Classess.Utils;

public class Merchant
{
    public int MerchantId { get; set; }
    public string MerchantName { get; set; }
    public string MerchantAddress { get; set; }
    public string MerchantEmail { get; set; }
    public string MerchantPhone { get; set; }
}
public enum DatabaseType
{
    Postgres,
    Oracle
}
public static class MerchantGenerator
{
    public static List<Merchant> GenerateMerchants(int count = 1000)
    {
        var merchants = new List<Merchant>();
        var random = new Random();

        string[] firstNames =
        {
        "Adebayo", "Chinedu", "Ibrahim", "Oluwaseun", "Uche", "Emeka", "Abubakar",
        "Tunde", "Kelechi", "Yakubu", "Femi", "Chukwuemeka", "Suleiman", "Bola"
    };

        string[] lastNames =
        {
        "Okafor", "Balogun", "Mohammed", "Eze", "Akinyemi", "Ibrahim", "Nwankwo",
        "Ojo", "Danladi", "Obi", "Usman", "Ademola", "Okeke", "Bello"
    };

        string[] businessTypes =
        {
        "Enterprises", "Stores", "Trading Co", "Supermarket", "Ventures",
        "Global Ventures", "Retailers", "Distribution Ltd", "General Merchants"
    };

        string[] cities =
        {
        "Lagos", "Abuja", "Port Harcourt", "Ibadan", "Enugu", "Kano", "Kaduna"
    };

        string[] streets =
        {
        "Allen Avenue", "Broad Street", "Awolowo Road", "Herbert Macaulay Way",
        "Ikorodu Road", "Lagos Island Road", "Ahmadu Bello Way"
    };

        for (int i = 1; i <= count; i++)
        {
            var first = firstNames[random.Next(firstNames.Length)];
            var last = lastNames[random.Next(lastNames.Length)];
            var business = businessTypes[random.Next(businessTypes.Length)];

            string ownerName = $"{first} {last}";
            string businessName = $"{ownerName} {business}";

            string address = $"{random.Next(1, 300)} {streets[random.Next(streets.Length)]}, {cities[random.Next(cities.Length)]}";
            string email = $"{first.ToLower()}.{last.ToLower()}{i}@mail.com";
            string phone = $"+234{random.Next(700000000, 809999999)}";

            merchants.Add(new Merchant
            {
                MerchantId = i,
                MerchantName = businessName,
                MerchantAddress = address,
                MerchantEmail = email,
                MerchantPhone = phone
            });
        }

        return merchants;
    }
    public static async Task InsertMerchantsAsync(
         DatabaseType dbType,
         string connectionString,
         List<Merchant> merchants
     )
        
    {

        connectionString = Cryptor.Decrypt(connectionString, true);


        if (merchants == null || merchants.Count == 0)
            return;

        switch (dbType)
        {
            case DatabaseType.Postgres:
                await InsertMerchantsPostgresAsync(connectionString, merchants);
                break;

            case DatabaseType.Oracle:
                await InsertMerchantsOracleAsync(connectionString, merchants);
                break;

            default:
                throw new Exception("Unsupported database type");
        }
    }

    private static async Task InsertMerchantsPostgresAsync(string connectionString, List<Merchant> merchants)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            string sql = @"
                INSERT INTO merchants (merchant_name, merchant_address, merchant_email, merchant_phone)
                VALUES (@name, @address, @email, @phone);
            ";

            foreach (var merchant in merchants)
            {
                using var cmd = new NpgsqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@name", merchant.MerchantName);
                cmd.Parameters.AddWithValue("@address", merchant.MerchantAddress);
                cmd.Parameters.AddWithValue("@email", merchant.MerchantEmail);
                cmd.Parameters.AddWithValue("@phone", merchant.MerchantPhone);

                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task InsertMerchantsOracleAsync(string connectionString, List<Merchant> merchants)
    {
        using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync();

        using var transaction = conn.BeginTransaction();

        try
        {
            string sql = @"
                INSERT INTO merchants (merchant_name, merchant_address, merchant_email, merchant_phone)
                VALUES (:name, :address, :email, :phone)
            ";

            foreach (var merchant in merchants)
            {
                using var cmd = new OracleCommand(sql, conn);
                cmd.Transaction = transaction;

                cmd.Parameters.Add(new OracleParameter("name", merchant.MerchantName));
                cmd.Parameters.Add(new OracleParameter("address", merchant.MerchantAddress));
                cmd.Parameters.Add(new OracleParameter("email", merchant.MerchantEmail));
                cmd.Parameters.Add(new OracleParameter("phone", merchant.MerchantPhone));

                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

}