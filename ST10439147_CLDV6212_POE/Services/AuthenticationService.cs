// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Authentication Service

using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using ST10439147_CLDV6212_POE.Models;

namespace ST10439147_CLDV6212_POE.Services
{
    /// <summary>
    /// Service for handling user authentication operations without ASP.NET Identity
    /// </summary>
    public class AuthenticationService
    {
        private readonly string _connectionString;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly TableService _tableService;

        public AuthenticationService(
            IConfiguration configuration,
            ILogger<AuthenticationService> logger,
            TableService tableService)
        {
            _connectionString = configuration["AzureSQL:ConnectionString"]
                ?? throw new ArgumentNullException("Azure SQL connection string not configured");
            _logger = logger;
            _tableService = tableService;
        }

        /// <summary>
        /// Hashes a password using SHA256 (consider using BCrypt for production)
        /// </summary>
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        /// <summary>
        /// Registers a new user (customer) in the system
        /// Creates both SQL user record and Azure Table Storage customer record
        /// </summary>
        public async Task<(bool Success, string Message, User? User)> RegisterCustomerAsync(RegisterViewModel model)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if username or email already exists
                    var checkQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username OR Email = @Email";
                    using (var checkCmd = new SqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@Username", model.Username);
                        checkCmd.Parameters.AddWithValue("@Email", model.Email);

                        var existingCount = (int)await checkCmd.ExecuteScalarAsync();
                        if (existingCount > 0)
                        {
                            return (false, "Username or email already exists", null);
                        }
                    }

                    // Create customer in Azure Table Storage first
                    var customer = new Customer
                    {
                        PartitionKey = "Customer",
                        RowKey = Guid.NewGuid().ToString(),
                        FirstName = model.FirstName,
                        LastName = model.LastName,
                        Email = model.Email,
                        PhoneNumber = model.PhoneNumber
                    };

                    await _tableService.InsertCustomerAsync(customer);

                    // Insert user into SQL database
                    var insertQuery = @"
                        INSERT INTO Users (Username, PasswordHash, Email, Role, CustomerId, IsActive, CreatedDate)
                        OUTPUT INSERTED.UserId
                        VALUES (@Username, @PasswordHash, @Email, @Role, @CustomerId, 1, GETDATE())";

                    using (var insertCmd = new SqlCommand(insertQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@Username", model.Username);
                        insertCmd.Parameters.AddWithValue("@PasswordHash", HashPassword(model.Password));
                        insertCmd.Parameters.AddWithValue("@Email", model.Email);
                        insertCmd.Parameters.AddWithValue("@Role", "Customer");
                        insertCmd.Parameters.AddWithValue("@CustomerId", customer.RowKey);

                        var userId = (int)await insertCmd.ExecuteScalarAsync();

                        var user = new User
                        {
                            UserId = userId,
                            Username = model.Username,
                            Email = model.Email,
                            Role = "Customer",
                            CustomerId = customer.RowKey,
                            IsActive = true,
                            CreatedDate = DateTime.UtcNow
                        };

                        _logger.LogInformation("User registered successfully: {Username}", model.Username);
                        return (true, "Registration successful", user);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering user: {Username}", model.Username);
                return (false, "An error occurred during registration", null);
            }
        }

        /// <summary>
        /// Authenticates a user with username and password
        /// </summary>
        public async Task<(bool Success, string Message, User? User)> LoginAsync(LoginViewModel model)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                        SELECT UserId, Username, Email, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                        FROM Users
                        WHERE Username = @Username AND PasswordHash = @PasswordHash";

                    using (var cmd = new SqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@Username", model.Username);
                        cmd.Parameters.AddWithValue("@PasswordHash", HashPassword(model.Password));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var isActive = reader.GetBoolean(reader.GetOrdinal("IsActive"));

                                if (!isActive)
                                {
                                    return (false, "Account is disabled", null);
                                }

                                var user = new User
                                {
                                    UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                                    Username = reader.GetString(reader.GetOrdinal("Username")),
                                    Email = reader.GetString(reader.GetOrdinal("Email")),
                                    Role = reader.GetString(reader.GetOrdinal("Role")),
                                    CustomerId = reader.IsDBNull(reader.GetOrdinal("CustomerId"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("CustomerId")),
                                    IsActive = isActive,
                                    CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                                    LastLoginDate = reader.IsDBNull(reader.GetOrdinal("LastLoginDate"))
                                        ? null
                                        : reader.GetDateTime(reader.GetOrdinal("LastLoginDate"))
                                };

                                // Close reader before executing update
                                reader.Close();

                                // Update last login date
                                var updateQuery = "UPDATE Users SET LastLoginDate = GETDATE() WHERE UserId = @UserId";
                                using (var updateCmd = new SqlCommand(updateQuery, connection))
                                {
                                    updateCmd.Parameters.AddWithValue("@UserId", user.UserId);
                                    await updateCmd.ExecuteNonQueryAsync();
                                }

                                _logger.LogInformation("User logged in successfully: {Username}", model.Username);
                                return (true, "Login successful", user);
                            }
                            else
                            {
                                _logger.LogWarning("Failed login attempt for username: {Username}", model.Username);
                                return (false, "Invalid username or password", null);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for username: {Username}", model.Username);
                return (false, "An error occurred during login", null);
            }
        }

        /// <summary>
        /// Gets user details by user ID
        /// </summary>
        public async Task<User?> GetUserByIdAsync(int userId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                        SELECT UserId, Username, Email, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                        FROM Users
                        WHERE UserId = @UserId";

                    using (var cmd = new SqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return new User
                                {
                                    UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                                    Username = reader.GetString(reader.GetOrdinal("Username")),
                                    Email = reader.GetString(reader.GetOrdinal("Email")),
                                    Role = reader.GetString(reader.GetOrdinal("Role")),
                                    CustomerId = reader.IsDBNull(reader.GetOrdinal("CustomerId"))
                                        ? null
                                        : reader.GetString(reader.GetOrdinal("CustomerId")),
                                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                                    CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                                    LastLoginDate = reader.IsDBNull(reader.GetOrdinal("LastLoginDate"))
                                        ? null
                                        : reader.GetDateTime(reader.GetOrdinal("LastLoginDate"))
                                };
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user by ID: {UserId}", userId);
                return null;
            }
        }

        /// <summary>
        /// Creates an admin user (use this for initial setup)
        /// </summary>
        public async Task<(bool Success, string Message)> CreateAdminAsync(string username, string email, string password)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if admin already exists
                    var checkQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username OR Email = @Email";
                    using (var checkCmd = new SqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@Username", username);
                        checkCmd.Parameters.AddWithValue("@Email", email);

                        var existingCount = (int)await checkCmd.ExecuteScalarAsync();
                        if (existingCount > 0)
                        {
                            return (false, "Username or email already exists");
                        }
                    }

                    // Insert admin user
                    var insertQuery = @"
                        INSERT INTO Users (Username, PasswordHash, Email, Role, CustomerId, IsActive, CreatedDate)
                        VALUES (@Username, @PasswordHash, @Email, 'Admin', NULL, 1, GETDATE())";

                    using (var insertCmd = new SqlCommand(insertQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@Username", username);
                        insertCmd.Parameters.AddWithValue("@PasswordHash", HashPassword(password));
                        insertCmd.Parameters.AddWithValue("@Email", email);

                        await insertCmd.ExecuteNonQueryAsync();

                        _logger.LogInformation("Admin user created successfully: {Username}", username);
                        return (true, "Admin created successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating admin user: {Username}", username);
                return (false, "An error occurred during admin creation");
            }
        }
    }
}