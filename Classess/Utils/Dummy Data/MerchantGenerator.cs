using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using Upsanctionscreener.Classess.Utils;

public class Merchant
{
    public int MerchantId { get; set; }
    public string MerchantName { get; set; }
    public string MerchantContactName { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }
    public string Phone { get; set; }
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
            "Tunde", "Kelechi", "Yakubu", "Femi", "Chukwuemeka", "Suleiman", "Bola",
            "Ngozi", "Amina", "Fatima", "Chisom", "Blessing", "Yetunde"
        };

        string[] lastNames =
        {
            "Okafor", "Balogun", "Mohammed", "Eze", "Akinyemi", "Ibrahim", "Nwankwo",
            "Ojo", "Danladi", "Obi", "Usman", "Ademola", "Okeke", "Bello",
            "Adesanya", "Chukwu", "Garba", "Lawal", "Musa", "Adeyemi"
        };

        string[] businessPrefixes =
        {
            "Sunrise", "Golden", "Royal", "Premier", "Eagle", "Pinnacle", "Sterling",
            "Heritage", "Allied", "Landmark", "Summit", "United", "Apex", "Emerald",
            "Crown", "Titan", "Nova", "Crest", "Horizon", "Zenith"
        };

        string[] businessSuffixes =
        {
            "Enterprises Ltd", "Trading Company", "Supermarket", "Ventures Ltd",
            "Global Supplies", "Retail Stores", "Distribution Ltd", "General Merchants",
            "Commodities Ltd", "Logistics", "Holdings", "Solutions Ltd",
            "Resources Ltd", "Industries Ltd", "Services Ltd"
        };

        string[] cities =
        {
            "Lagos", "Abuja", "Port Harcourt", "Ibadan", "Enugu", "Kano", "Kaduna"
        };

        string[] streets =
        {
            "Allen Avenue", "Broad Street", "Awolowo Road", "Herbert Macaulay Way",
            "Ikorodu Road", "Lagos Island Road", "Ahmadu Bello Way",
            "Adeola Odeku Street", "Victoria Arobieke Street", "Ozumba Mbadiwe Avenue"
        };

        for (int i = 1; i <= count; i++)
        {
            // Business name: prefix + suffix (no person name)
            var prefix = businessPrefixes[random.Next(businessPrefixes.Length)];
            var suffix = businessSuffixes[random.Next(businessSuffixes.Length)];
            string merchantName = $"{prefix} {suffix}";

            // Contact name: just a person's name (no business words)
            var firstName = firstNames[random.Next(firstNames.Length)];
            var lastName = lastNames[random.Next(lastNames.Length)];
            string contactName = $"{firstName} {lastName}";

            string address = $"{random.Next(1, 300)} {streets[random.Next(streets.Length)]}, {cities[random.Next(cities.Length)]}";
            string email = $"{firstName.ToLower()}.{lastName.ToLower()}{i}@mail.com";
            string phone = $"+234{random.Next(700000000, 809999999)}";

            merchants.Add(new Merchant
            {
                MerchantId = i,
                MerchantName = merchantName,
                MerchantContactName = contactName,
                Email = email,
                Address = address,
                Phone = phone
            });
        }

        return merchants;
    }

    public static async Task InsertMerchantsAsync(
        DatabaseType dbType,
        string connectionString,
        List<Merchant> merchants)
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
                INSERT INTO merchants (merchant_name, merchant_contact_name, email, address, phone)
                VALUES (@merchantName, @merchantContactName, @email, @address, @phone);
            ";

            foreach (var merchant in merchants)
            {
                using var cmd = new NpgsqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@merchantName", merchant.MerchantName);
                cmd.Parameters.AddWithValue("@merchantContactName", merchant.MerchantContactName);
                cmd.Parameters.AddWithValue("@email", merchant.Email);
                cmd.Parameters.AddWithValue("@address", merchant.Address);
                cmd.Parameters.AddWithValue("@phone", merchant.Phone);

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
                INSERT INTO merchants (merchant_name, merchant_contact_name, email, address, phone)
                VALUES (:merchantName, :merchantContactName, :email, :address, :phone)
            ";

            foreach (var merchant in merchants)
            {
                using var cmd = new OracleCommand(sql, conn);
                cmd.Transaction = transaction;

                cmd.Parameters.Add(new OracleParameter("merchantName", merchant.MerchantName));
                cmd.Parameters.Add(new OracleParameter("merchantContactName", merchant.MerchantContactName));
                cmd.Parameters.Add(new OracleParameter("email", merchant.Email));
                cmd.Parameters.Add(new OracleParameter("address", merchant.Address));
                cmd.Parameters.Add(new OracleParameter("phone", merchant.Phone));

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