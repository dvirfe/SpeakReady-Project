using Model;
using MySql.Data.MySqlClient;

namespace DAL;

public static class DatabaseInitializer
{
    public const string DatabaseName = "SpeakReadyDB";

    public static string GetDatabaseConnectionString(string serverConnectionString)
    {
        var builder = new MySqlConnectionStringBuilder(serverConnectionString) { Database = DatabaseName };
        return builder.ConnectionString;
    }

    public static async Task InitializeAsync(string serverConnectionString, string? demoPassword)
    {
        var builder = new MySqlConnectionStringBuilder(serverConnectionString) { Database = "" };
        using (var server = new MySqlConnection(builder.ConnectionString))
        {
            await server.OpenAsync();
            using var create = new MySqlCommand(
                "CREATE DATABASE IF NOT EXISTS SpeakReadyDB CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", server);
            await create.ExecuteNonQueryAsync();
        }

        using var connection = new MySqlConnection(GetDatabaseConnectionString(serverConnectionString));
        await connection.OpenAsync();
        using (var utc = new MySqlCommand("SET time_zone = '+00:00';", connection))
            await utc.ExecuteNonQueryAsync();
        string filePath = Path.Combine(AppContext.BaseDirectory, "Schema.sql");

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Schema.sql file was not found.");
        }

        string schema = await File.ReadAllTextAsync(filePath);
        foreach (var statement in schema.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(statement)) continue;
            using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }

        if (!string.IsNullOrEmpty(demoPassword))
        {
            if (demoPassword.Length < 8)
                throw new InvalidOperationException("Seed:DemoPassword must have at least 8 characters.");
            await SeedUsersAsync(connection, demoPassword);
        }
    }

    private static async Task SeedUsersAsync(MySqlConnection connection, string demoPassword)
    {
        using var count = new MySqlCommand("SELECT COUNT(*) FROM Users;", connection);
        if (Convert.ToInt64(await count.ExecuteScalarAsync()) != 0) return;

        using var transaction = connection.BeginTransaction();
        for (int index = 0; index < 10; index++)
        {
            UserRole role;
            string username;
            string fullName;
            if (index == 0)
            {
                role = UserRole.Admin;
                username = "admin";
                fullName = "מנהל הדגמה";
            }
            else if (index < 3)
            {
                role = UserRole.Teacher;
                username = $"teacher{index}";
                fullName = $"מורה לדוגמה {index}";
            }
            else
            {
                role = UserRole.Student;
                username = $"student{index - 2}";
                fullName = $"תלמיד לדוגמה {index - 2}";
            }
            using var command = new MySqlCommand("""
                INSERT INTO Users (Username, FullName, Email, PasswordHash, Role, TeacherRequestStatus)
                VALUES (@username, @name, @email, @hash, @role, @request);
                """, connection, transaction);
            command.Parameters.AddWithValue("@username", username);
            command.Parameters.AddWithValue("@name", fullName);
            command.Parameters.AddWithValue("@email", $"{username}@speakready.example");
            command.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(demoPassword));
            command.Parameters.AddWithValue("@role", role.ToString());
            command.Parameters.AddWithValue("@request", role == UserRole.Teacher ? "Approved" : "None");
            await command.ExecuteNonQueryAsync();
        }
        transaction.Commit();
    }
}

