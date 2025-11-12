// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Authentication Service using ADO.NET

using ST10439147_CLDV6212_POE.Models;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Services
{
    /// <summary>
    /// Service for handling user authentication operations using ADO.NET
    /// Manages user registration, login, password hashing, and user retrieval
    /// </summary>
    public class AuthenticationService
    {
        private readonly DatabaseHelper _dbHelper;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _functionBaseUrl;
        private readonly string _functionKey;

        public AuthenticationService(
            DatabaseHelper dbHelper,
            ILogger<AuthenticationService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _dbHelper = dbHelper ?? throw new ArgumentNullException(nameof(dbHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClientFactory.CreateClient();
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"];
            _functionKey = configuration["AzureFunctions:FunctionKey"];
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
        /// Creates customer in Azure Table Storage via Azure Functions and user in SQL Database
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

                // Check if email already exists in SQL Database
                if (await EmailExistsAsync(model.Email))
                {
                    _logger.LogWarning("Email already exists: {Email}", model.Email);
                    return (false, "Email already exists. Please use a different email address.", null);
                }

                // Step 1: Create customer record in Azure Table Storage via Azure Function
                var customer = new Customer
                {
                    PartitionKey = "Customer",
                    RowKey = Guid.NewGuid().ToString(),
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber ?? string.Empty
                };

                var (customerSuccess, customerId, customerMessage) = await CreateCustomerViaAzureFunctionAsync(customer);

                if (!customerSuccess)
                {
                    _logger.LogError("Failed to create customer in Table Storage: {Message}", customerMessage);
                    return (false, "Failed to create customer profile. Please try again.", null);
                }

                _logger.LogInformation("Customer created in Table Storage: {CustomerId}", customerId);

                // Step 2: Create user record in SQL Database with link to customer
                try
                {
                    string insertQuery = @"
                        INSERT INTO Users (Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate)
                        VALUES (@Email, @PasswordHash, @Role, @CustomerId, @IsActive, @CreatedDate);
                        SELECT CAST(SCOPE_IDENTITY() as int);";

                    var parameters = new[]
                    {
                        new SqlParameter("@Email", model.Email),
                        new SqlParameter("@PasswordHash", HashPassword(model.Password)),
                        new SqlParameter("@Role", "Customer"),
                        new SqlParameter("@CustomerId", customerId),
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
                        CustomerId = customerId,
                        IsActive = true,
                        CreatedDate = DateTime.UtcNow
                    };

                    _logger.LogInformation("User registered successfully: {Email}, CustomerId: {CustomerId}",
                        user.Email, user.CustomerId);

                    return (true, "Registration successful", user);
                }
                catch (Exception sqlEx)
                {
                    // If SQL insert fails, attempt to clean up the customer record
                    _logger.LogError(sqlEx, "SQL Database error during registration, attempting cleanup");

                    try
                    {
                        await DeleteCustomerViaAzureFunctionAsync("Customer", customerId);
                        _logger.LogInformation("Cleaned up customer record after SQL failure");
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "Failed to cleanup customer record");
                    }

                    return (false, "An error occurred during registration. Please try again.", null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for email: {Email}", model.Email);
                return (false, "An error occurred during registration. Please try again.", null);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Create customer via Azure Function
        /// </summary>
        private async Task<(bool success, string customerId, string message)> CreateCustomerViaAzureFunctionAsync(Customer customer)
        {
            try
            {
                _logger.LogInformation("Creating customer via Azure Function: {Email}", customer.Email);

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/customers");
                request.Headers.Add("x-functions-key", _functionKey);

                var jsonContent = JsonSerializer.Serialize(customer);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var createdCustomer = JsonSerializer.Deserialize<Customer>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogInformation("Customer created successfully via Azure Function: {CustomerId}", createdCustomer?.RowKey);
                    return (true, createdCustomer?.RowKey ?? customer.RowKey, "Customer created successfully");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error creating customer via Azure Function: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                return (false, string.Empty, $"Failed to create customer: {errorContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception creating customer via Azure Function");
                return (false, string.Empty, $"Error creating customer: {ex.Message}");
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Delete customer via Azure Function (cleanup on registration failure)
        /// </summary>
        private async Task DeleteCustomerViaAzureFunctionAsync(string partitionKey, string rowKey)
        {
            try
            {
                _logger.LogInformation("Deleting customer via Azure Function: {PartitionKey}/{RowKey}",
                    partitionKey, rowKey);

                var request = new HttpRequestMessage(HttpMethod.Delete,
                    $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting customer via Azure Function");
                throw;
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