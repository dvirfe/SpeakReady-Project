using System.Security.Cryptography;
using System.Text;
using Model;
using MySql.Data.MySqlClient;

namespace DAL;

public sealed class SessionDB(string connectionString, UserDB users)
{
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<LoginResult> CreateAsync(User user)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTime.UtcNow.AddHours(8);
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand("INSERT INTO AuthSessions (TokenHash, UserId, ExpiresAt) VALUES (@hash,@user,@expires)", connection);
        command.Parameters.AddWithValue("@hash", Hash(token));
        command.Parameters.AddWithValue("@user", user.Id);
        command.Parameters.AddWithValue("@expires", expires);
        await command.ExecuteNonQueryAsync();
        return new(user, token, expires);
    }

    public async Task<User?> GetUserAsync(string token)
    {
        if (token.Length != 64) return null;
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand("SELECT UserId FROM AuthSessions WHERE TokenHash=@hash AND ExpiresAt>UTC_TIMESTAMP()", connection);
        command.Parameters.AddWithValue("@hash", Hash(token));
        var id = await command.ExecuteScalarAsync();
        if (id is null) return null;
        var user = await users.GetUserByIdAsync(Convert.ToInt32(id));
        return user is { IsActive: true } ? user : null;
    }

    public async Task RevokeAsync(string token)
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand("DELETE FROM AuthSessions WHERE TokenHash=@hash OR ExpiresAt<=UTC_TIMESTAMP()", connection);
        command.Parameters.AddWithValue("@hash", Hash(token));
        await command.ExecuteNonQueryAsync();
    }
}
