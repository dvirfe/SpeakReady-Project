using Model;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace DAL;

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
        command.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(user.PasswordHash));
        try
        {
            await command.ExecuteNonQueryAsync();
            int id = checked((int)command.LastInsertedId);
            Console.WriteLine($"[Register] New user added: id {id}, role Student");
            return id;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            Console.WriteLine("[Users] Write rejected: duplicate username or email");
            throw new DuplicateUserException(ex);
        }
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
        bool exists = Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
        Console.WriteLine($"[Users] Availability check completed: exists={exists}");
        return exists;
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(SelectUser + " WHERE UserId = @id;", connection);
        command.Parameters.AddWithValue("@id", id);
        using var reader = await command.ExecuteReaderAsync();
        User? user = await reader.ReadAsync() ? ReadUser(reader) : null;
        Console.WriteLine(user is null ? $"[Users] No user with id {id}" : $"[Users] Read user by id {id}");
        return user;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(SelectUser + " ORDER BY UserId;", connection);
        using var reader = await command.ExecuteReaderAsync();
        var users = new List<User>();
        while (await reader.ReadAsync()) users.Add(ReadUser(reader));
        Console.WriteLine($"[Users] Read {users.Count} users");
        return users;
    }

    public async Task<User?> CheckLoginAsync(string username, string password)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(SelectUser + " WHERE (Username = @username OR Email = @username) AND IsActive = TRUE;", connection);
        command.Parameters.AddWithValue("@username", username);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            // השאילתה מסננת גם חשבונות לא פעילים, לכן אין להסיק שהשם אינו קיים.
            Console.WriteLine("[Login] Failed, user not found or inactive");
            return null;
        }
        var user = ReadUser(reader);
        bool isPasswordCorrect = BCrypt.Net.BCrypt.Verify(password,user.PasswordHash);
        if (isPasswordCorrect)
        {
            Console.WriteLine($"[Login] Success: user {user.Id} ({user.RoleName})");
            return user;
        }
        else
        {
            Console.WriteLine($"[Login] Failed, wrong password for user {user.Id}");
            return null;
        }
    }

    public async Task<List<RoleCount>> CountUsersByRoleAsync()
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand("SELECT Role, COUNT(*) AS Total FROM Users GROUP BY Role;", connection);
        using var reader = await command.ExecuteReaderAsync();
        var counts = new List<RoleCount>();
        while (await reader.ReadAsync())
            counts.Add(new RoleCount(reader.GetString(0), Convert.ToInt32(reader.GetValue(1))));
        Console.WriteLine($"[Users] Counted users across {counts.Count} roles");
        return counts;
    }

    public Task<bool> UpdateUserRoleAsync(int id, UserRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        return UpdateAsync("""
            UPDATE Users SET Role = @value,
            TeacherRequestStatus = CASE WHEN @value = 'Teacher' THEN 'Approved' ELSE 'None' END
            WHERE UserId = @id;
            """, id, role.ToString(), "Role update");
    }

    public Task<bool> UpdatePasswordAsync(int id, string password)
    {
        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

        return UpdateAsync(
            "UPDATE Users SET PasswordHash = @value WHERE UserId = @id;",
            id,
            hashedPassword, "Password update");
    }
    public Task<bool> UpdateEmailAsync(int id, string email) =>
        UpdateAsync("UPDATE Users SET Email = @value WHERE UserId = @id;", id, email, "Email update");

    public Task<bool> UpdateActiveAsync(int id, bool isActive) =>
        UpdateAsync("UPDATE Users SET IsActive = @value WHERE UserId = @id;", id, isActive, "Active status update");

    private async Task<bool> UpdateAsync(string sql, int id, object value, string operation)
    {
        using var connection = await OpenAsync();
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@value", value);
        try
        {
            bool updated = await command.ExecuteNonQueryAsync() > 0;
            Console.WriteLine(updated
                ? $"[Users] {operation} succeeded for user {id}"
                : $"[Users] {operation}: no affected row for user {id}");
            return updated;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            Console.WriteLine("[Users] Write rejected: duplicate username or email");
            throw new DuplicateUserException(ex);
        }
    }
}

