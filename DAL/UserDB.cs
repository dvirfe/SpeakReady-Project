using System.Data.Common;
using Model;
using MySql.Data.MySqlClient;

namespace DAL;

// מחלקה זו מחזיקה מחרוזת חיבור בלבד. לכל פעולה חיבור משלה, ולכן Singleton בטוח כאן.
public class UserDB(string connectionString)
{
    private const string SelectUser = """
        SELECT UserId, Username, FullName, Email, PasswordHash, Role,
               TeacherRequestStatus, IsActive, CreatedAt FROM Users
        """;

    private async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            using var utc = new MySqlCommand("SET time_zone = '+00:00';", connection);
            await utc.ExecuteNonQueryAsync();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static User ReadUser(DbDataReader row) => new(
        row.GetInt32(row.GetOrdinal("UserId")),
        row.GetString(row.GetOrdinal("Username")),
        row.GetString(row.GetOrdinal("FullName")),
        row.GetString(row.GetOrdinal("Email")),
        row.GetString(row.GetOrdinal("PasswordHash")),
        Enum.Parse<UserRole>(row.GetString(row.GetOrdinal("Role"))),
        Enum.Parse<TeacherRequestState>(row.GetString(row.GetOrdinal("TeacherRequestStatus"))),
        row.GetBoolean(row.GetOrdinal("IsActive")),
        DateTime.SpecifyKind(row.GetDateTime(row.GetOrdinal("CreatedAt")), DateTimeKind.Utc));

    public async Task<int> AddUserAsync(User user)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand("""
            INSERT INTO Users (Username, FullName, Email, PasswordHash)
            VALUES (@username, @name, @email, @hash);
            """, connection);
        command.Parameters.AddWithValue("@username", user.Username);
        command.Parameters.AddWithValue("@name", user.FullName);
        command.Parameters.AddWithValue("@email", user.Email);
        command.Parameters.AddWithValue("@hash", user.PasswordHash);
        try { await command.ExecuteNonQueryAsync(); return checked((int)command.LastInsertedId); }
        catch (MySqlException ex) when (ex.Number == 1062) { throw new DuplicateUserException(ex); }
    }

    public Task<bool> UsernameExistsAsync(string username) =>
        ExistsAsync("SELECT COUNT(*) FROM Users WHERE Username = @value;", username);
    public Task<bool> EmailExistsAsync(string email) =>
        ExistsAsync("SELECT COUNT(*) FROM Users WHERE Email = @value;", email);

    private async Task<bool> ExistsAsync(string sql, string value)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@value", value);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(SelectUser + " WHERE UserId = @id;", connection);
        command.Parameters.AddWithValue("@id", id);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadUser(reader) : null;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(SelectUser + " ORDER BY UserId;", connection);
        using var reader = await command.ExecuteReaderAsync();
        var users = new List<User>();
        while (await reader.ReadAsync()) users.Add(ReadUser(reader));
        return users;
    }

    public async Task<User?> CheckLoginAsync(string username, string password)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(SelectUser + " WHERE (Username = @username OR Email = @username) AND IsActive = TRUE;", connection);
        command.Parameters.AddWithValue("@username", username);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var user = ReadUser(reader);
        // השוואה רגישה לאותיות באמצעות Hash, ולא השוואת טקסט ב-MySQL.
        return PasswordHasher.Verify(password, user.PasswordHash) ? user : null;
    }

    public async Task<List<RoleCount>> CountUsersByRoleAsync()
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand("SELECT Role, COUNT(*) AS Total FROM Users GROUP BY Role;", connection);
        using var reader = await command.ExecuteReaderAsync();
        var counts = new List<RoleCount>();
        while (await reader.ReadAsync())
            counts.Add(new RoleCount(reader.GetString(0), Convert.ToInt32(reader.GetValue(1))));
        return counts;
    }

    public Task<bool> UpdateUserRoleAsync(int id, UserRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        return UpdateAsync("""
            UPDATE Users SET Role = @value,
            TeacherRequestStatus = CASE WHEN @value = 'Teacher' THEN 'Approved' ELSE 'None' END
            WHERE UserId = @id;
            """, id, role.ToString());
    }

    public Task<bool> UpdatePasswordAsync(int id, string password) =>
        UpdateAsync("UPDATE Users SET PasswordHash = @value WHERE UserId = @id;", id, PasswordHasher.Hash(password));

    public Task<bool> UpdateEmailAsync(int id, string email) =>
        UpdateAsync("UPDATE Users SET Email = @value WHERE UserId = @id;", id, email);

    public Task<bool> UpdateActiveAsync(int id, bool isActive) =>
        UpdateAsync("UPDATE Users SET IsActive = @value WHERE UserId = @id;", id, isActive);

    private async Task<bool> UpdateAsync(string sql, int id, object value)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@value", value);
        try { return await command.ExecuteNonQueryAsync() > 0; }
        catch (MySqlException ex) when (ex.Number == 1062) { throw new DuplicateUserException(ex); }
    }
}

