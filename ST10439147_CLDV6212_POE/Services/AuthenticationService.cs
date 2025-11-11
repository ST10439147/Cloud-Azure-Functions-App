// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Authentication Service using ADO.NET

using ST10439147_CLDV6212_POE.Models;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace ST10439147_CLDV6212_POE.Services
{
    /// <summary>
    /// Service for handling user authentication operations using ADO.NET
    /// Manages user registration, login, password hashing, and user retrieval
    /// </summary>
    public class AuthenticationService
    {
        private readonly DatabaseHelper _dbHelper;
        private readonly TableService _tableService;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            DatabaseHelper dbHelper,
            TableService tableService,
            ILogger<AuthenticationService> logger)
        {
            _dbHelper = dbHelper ?? throw new ArgumentNullException(nameof(dbHelper));
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Login user with email and password
        /// </summary>
        public async Task<(bool success, string message, User? user)> LoginAsync(LoginViewModel model)
        {
            try
            {
                if (model == null)
                {
                    return (false, "Invalid login request", null);
                }

                _logger.LogInformation("Login attempt for email: {Email}", model.Email);

                // Query to find user by email
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    WHERE Email = @Email";

                var parameters = new[]
                {
                    new SqlParameter("@Email", model.Email)
                };

                var result = await _dbHelper.ExecuteQueryAsync(query, parameters);

                if (result.Rows.Count == 0)
                {
                    _logger.LogWarning("User not found: {Email}", model.Email);
                    return (false, "Invalid email or password", null);
                }

                // Map DataRow to User object
                var row = result.Rows[0];
                var user = new User
                {
                    UserId = Convert.ToInt32(row["UserId"]),
                    Email = row["Email"].ToString() ?? string.Empty,
                    PasswordHash = row["PasswordHash"].ToString() ?? string.Empty,
                    Role = row["Role"].ToString() ?? string.Empty,
                    CustomerId = row["CustomerId"] != DBNull.Value ? row["CustomerId"].ToString() : null,
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    LastLoginDate = row["LastLoginDate"] != DBNull.Value ? Convert.ToDateTime(row["LastLoginDate"]) : null
                };

                // Check if user is active
                if (!user.IsActive)
                {
                    _logger.LogWarning("Inactive user attempted login: {Email}", model.Email);
                    return (false, "Your account has been deactivated. Please contact support.", null);
                }

                // Verify password
                if (!VerifyPassword(model.Password, user.PasswordHash))
                {
                    _logger.LogWarning("Invalid password for user: {Email}", model.Email);
                    return (false, "Invalid email or password", null);
                }

                // Update last login date
                await UpdateLastLoginAsync(user.UserId);

                _logger.LogInformation("User logged in successfully: {Email}", user.Email);
                return (true, "Login successful", user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for email: {Email}", model.Email);
                return (false, "An error occurred during login. Please try again.", null);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Register a new customer with login credentials
        /// </summary>
        public async Task<(bool success, string message, User? user)> RegisterCustomerAsync(RegisterViewModel model)
        {
            try
            {
                if (model == null)
                {
                    return (false, "Invalid registration request", null);
                }

                _logger.LogInformation("Registration attempt for email: {Email}", model.Email);

                // Check if email already exists
                if (await EmailExistsAsync(model.Email))
                {
                    _logger.LogWarning("Email already exists: {Email}", model.Email);
                    return (false, "Email already exists. Please use a different email address.", null);
                }

                // Create customer record in Azure Table Storage
                var customer = new Customer
                {
                    PartitionKey = "Customer",
                    RowKey = Guid.NewGuid().ToString(),
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber ?? string.Empty
                };

                await _tableService.InsertCustomerAsync(customer);
                _logger.LogInformation("Customer created in Table Storage: {CustomerId}", customer.RowKey);

                // Create user record in SQL Database
                string insertQuery = @"
                    INSERT INTO Users (Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate)
                    VALUES (@Email, @PasswordHash, @Role, @CustomerId, @IsActive, @CreatedDate);
                    SELECT CAST(SCOPE_IDENTITY() as int);";

                var parameters = new[]
                {
                    new SqlParameter("@Email", model.Email),
                    new SqlParameter("@PasswordHash", HashPassword(model.Password)),
                    new SqlParameter("@Role", "Customer"),
                    new SqlParameter("@CustomerId", customer.RowKey),
                    new SqlParameter("@IsActive", true),
                    new SqlParameter("@CreatedDate", DateTime.UtcNow)
                };

                var userId = await _dbHelper.ExecuteScalarAsync(insertQuery, parameters);

                var user = new User
                {
                    UserId = Convert.ToInt32(userId),
                    Email = model.Email,
                    PasswordHash = HashPassword(model.Password),
                    Role = "Customer",
                    CustomerId = customer.RowKey,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                _logger.LogInformation("User registered successfully: {Email}, CustomerId: {CustomerId}",
                    user.Email, user.CustomerId);

                return (true, "Registration successful", user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for email: {Email}", model.Email);
                return (false, "An error occurred during registration. Please try again.", null);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Get user by ID
        /// </summary>
        public async Task<User?> GetUserByIdAsync(int userId)
        {
            try
            {
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    WHERE UserId = @UserId";

                var parameters = new[]
                {
                    new SqlParameter("@UserId", userId)
                };

                var result = await _dbHelper.ExecuteQueryAsync(query, parameters);

                if (result.Rows.Count == 0)
                {
                    return null;
                }

                var row = result.Rows[0];
                return new User
                {
                    UserId = Convert.ToInt32(row["UserId"]),
                    Email = row["Email"].ToString() ?? string.Empty,
                    PasswordHash = row["PasswordHash"].ToString() ?? string.Empty,
                    Role = row["Role"].ToString() ?? string.Empty,
                    CustomerId = row["CustomerId"] != DBNull.Value ? row["CustomerId"].ToString() : null,
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    LastLoginDate = row["LastLoginDate"] != DBNull.Value ? Convert.ToDateTime(row["LastLoginDate"]) : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user by ID: {UserId}", userId);
                return null;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Get user by email
        /// </summary>
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            try
            {
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    WHERE Email = @Email";

                var parameters = new[]
                {
                    new SqlParameter("@Email", email)
                };

                var result = await _dbHelper.ExecuteQueryAsync(query, parameters);

                if (result.Rows.Count == 0)
                {
                    return null;
                }

                var row = result.Rows[0];
                return new User
                {
                    UserId = Convert.ToInt32(row["UserId"]),
                    Email = row["Email"].ToString() ?? string.Empty,
                    PasswordHash = row["PasswordHash"].ToString() ?? string.Empty,
                    Role = row["Role"].ToString() ?? string.Empty,
                    CustomerId = row["CustomerId"] != DBNull.Value ? row["CustomerId"].ToString() : null,
                    IsActive = Convert.ToBoolean(row["IsActive"]),
                    CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                    LastLoginDate = row["LastLoginDate"] != DBNull.Value ? Convert.ToDateTime(row["LastLoginDate"]) : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user by email: {Email}", email);
                return null;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Check if email exists
        /// </summary>
        public async Task<bool> EmailExistsAsync(string email)
        {
            try
            {
                string query = "SELECT COUNT(*) FROM Users WHERE Email = @Email";
                var parameters = new[]
                {
                    new SqlParameter("@Email", email)
                };

                var result = await _dbHelper.ExecuteScalarAsync(query, parameters);
                return Convert.ToInt32(result) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email existence: {Email}", email);
                return false;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Hash password using SHA256
        /// </summary>
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Verify password against stored hash
        /// </summary>
        private bool VerifyPassword(string password, string storedHash)
        {
            var passwordHash = HashPassword(password);
            return passwordHash == storedHash;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Update user last login date
        /// </summary>
        public async Task UpdateLastLoginAsync(int userId)
        {
            try
            {
                string query = "UPDATE Users SET LastLoginDate = @LastLoginDate WHERE UserId = @UserId";
                var parameters = new[]
                {
                    new SqlParameter("@LastLoginDate", DateTime.UtcNow),
                    new SqlParameter("@UserId", userId)
                };

                await _dbHelper.ExecuteNonQueryAsync(query, parameters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last login for user: {UserId}", userId);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Deactivate user account
        /// </summary>
        public async Task<bool> DeactivateUserAsync(int userId)
        {
            try
            {
                string query = "UPDATE Users SET IsActive = 0 WHERE UserId = @UserId";
                var parameters = new[]
                {
                    new SqlParameter("@UserId", userId)
                };

                int rowsAffected = await _dbHelper.ExecuteNonQueryAsync(query, parameters);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("User deactivated: {UserId}", userId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating user: {UserId}", userId);
                return false;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Activate user account
        /// </summary>
        public async Task<bool> ActivateUserAsync(int userId)
        {
            try
            {
                string query = "UPDATE Users SET IsActive = 1 WHERE UserId = @UserId";
                var parameters = new[]
                {
                    new SqlParameter("@UserId", userId)
                };

                int rowsAffected = await _dbHelper.ExecuteNonQueryAsync(query, parameters);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("User activated: {UserId}", userId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating user: {UserId}", userId);
                return false;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Get all users (Admin only)
        /// </summary>
        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    ORDER BY CreatedDate DESC";

                var result = await _dbHelper.ExecuteQueryAsync(query);
                var users = new List<User>();

                foreach (System.Data.DataRow row in result.Rows)
                {
                    users.Add(new User
                    {
                        UserId = Convert.ToInt32(row["UserId"]),
                        Email = row["Email"].ToString() ?? string.Empty,
                        PasswordHash = row["PasswordHash"].ToString() ?? string.Empty,
                        Role = row["Role"].ToString() ?? string.Empty,
                        CustomerId = row["CustomerId"] != DBNull.Value ? row["CustomerId"].ToString() : null,
                        IsActive = Convert.ToBoolean(row["IsActive"]),
                        CreatedDate = Convert.ToDateTime(row["CreatedDate"]),
                        LastLoginDate = row["LastLoginDate"] != DBNull.Value ? Convert.ToDateTime(row["LastLoginDate"]) : null
                    });
                }

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all users");
                return new List<User>();
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Delete user account
        /// </summary>
        public async Task<bool> DeleteUserAsync(int userId)
        {
            try
            {
                string query = "DELETE FROM Users WHERE UserId = @UserId";
                var parameters = new[]
                {
                    new SqlParameter("@UserId", userId)
                };

                int rowsAffected = await _dbHelper.ExecuteNonQueryAsync(query, parameters);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("User deleted: {UserId}", userId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user: {UserId}", userId);
                return false;
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//