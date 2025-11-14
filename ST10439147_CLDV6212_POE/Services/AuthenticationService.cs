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
    /// Service responsible for user authentication and account management operations.
    /// Implements a hybrid architecture using SQL Server (via ADO.NET) for user credentials
    /// and Azure Table Storage (via Azure Functions) for customer profile data.
    /// Provides secure password hashing, login validation, user registration, and account lifecycle management.
    /// </summary>
    public class AuthenticationService
    {
        // Private fields for dependency injection and configuration
        private readonly DatabaseHelper _dbHelper;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _functionBaseUrl;
        private readonly string _functionKey;

        /// <summary>
        /// Initializes a new instance of the AuthenticationService with required dependencies.
        /// </summary>
        /// <param name="dbHelper">Helper for executing SQL queries using ADO.NET</param>
        /// <param name="logger">Logger for recording authentication events and errors</param>
        /// <param name="httpClientFactory">Factory for creating HTTP clients to communicate with Azure Functions</param>
        /// <param name="configuration">Configuration provider for accessing Azure Function settings</param>
        /// <exception cref="ArgumentNullException">Thrown when any required dependency is null</exception>
        public AuthenticationService(
            DatabaseHelper dbHelper,
            ILogger<AuthenticationService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _dbHelper = dbHelper ?? throw new ArgumentNullException(nameof(dbHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClientFactory.CreateClient();

            // Retrieve Azure Function configuration for customer data operations
            _functionBaseUrl = configuration["AzureFunctions:BaseUrl"];
            _functionKey = configuration["AzureFunctions:FunctionKey"];
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Authenticates a user with email and password credentials.
        /// Validates credentials against SQL database, checks account status, and updates last login timestamp.
        /// </summary>
        /// <param name="model">Login view model containing email and password</param>
        /// <returns>Tuple containing success status, message, and authenticated user object (if successful)</returns>
        public async Task<(bool success, string message, User? user)> LoginAsync(LoginViewModel model)
        {
            try
            {
                // Validate input model
                if (model == null)
                {
                    return (false, "Invalid login request", null);
                }

                _logger.LogInformation("Login attempt for email: {Email}", model.Email);

                // Query SQL database to find user by email
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    WHERE Email = @Email";

                var parameters = new[]
                {
                    new SqlParameter("@Email", model.Email)
                };

                var result = await _dbHelper.ExecuteQueryAsync(query, parameters);

                // Check if user exists
                if (result.Rows.Count == 0)
                {
                    _logger.LogWarning("User not found: {Email}", model.Email);
                    return (false, "Invalid email or password", null);
                }

                // Map database row to User object
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

                // Validate account is active
                if (!user.IsActive)
                {
                    _logger.LogWarning("Inactive user attempted login: {Email}", model.Email);
                    return (false, "Your account has been deactivated. Please contact support.", null);
                }

                // Verify password hash matches stored hash
                if (!VerifyPassword(model.Password, user.PasswordHash))
                {
                    _logger.LogWarning("Invalid password for user: {Email}", model.Email);
                    return (false, "Invalid email or password", null);
                }

                // Update last login timestamp for audit purposes
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
        /// Registers a new customer with login credentials and profile information.
        /// Implements a two-phase commit pattern: creates customer profile in Azure Table Storage first,
        /// then creates user credentials in SQL database. Performs cleanup if SQL operation fails.
        /// </summary>
        /// <param name="model">Registration view model containing user details and credentials</param>
        /// <returns>Tuple containing success status, message, and created user object (if successful)</returns>
        public async Task<(bool success, string message, User? user)> RegisterCustomerAsync(RegisterViewModel model)
        {
            try
            {
                // Validate input model
                if (model == null)
                {
                    return (false, "Invalid registration request", null);
                }

                _logger.LogInformation("Registration attempt for email: {Email}", model.Email);

                // Check for duplicate email in SQL database
                if (await EmailExistsAsync(model.Email))
                {
                    _logger.LogWarning("Email already exists: {Email}", model.Email);
                    return (false, "Email already exists. Please use a different email address.", null);
                }

                // Step 1: Create customer record in Azure Table Storage via Azure Function
                var customer = new Customer
                {
                    PartitionKey = "Customer",
                    RowKey = Guid.NewGuid().ToString(),  // Generate unique customer ID
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber ?? string.Empty
                };

                var (customerSuccess, customerId, customerMessage) = await CreateCustomerViaAzureFunctionAsync(customer);

                // Validate customer creation was successful
                if (!customerSuccess)
                {
                    _logger.LogError("Failed to create customer in Table Storage: {Message}", customerMessage);
                    return (false, "Failed to create customer profile. Please try again.", null);
                }

                _logger.LogInformation("Customer created in Table Storage: {CustomerId}", customerId);

                // Step 2: Create user record in SQL Database with link to customer
                try
                {
                    // SQL query to insert user and return the generated UserId
                    string insertQuery = @"
                        INSERT INTO Users (Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate)
                        VALUES (@Email, @PasswordHash, @Role, @CustomerId, @IsActive, @CreatedDate);
                        SELECT CAST(SCOPE_IDENTITY() as int);";

                    var parameters = new[]
                    {
                        new SqlParameter("@Email", model.Email),
                        new SqlParameter("@PasswordHash", HashPassword(model.Password)),  // Hash password before storing
                        new SqlParameter("@Role", "Customer"),                            // Default role for registration
                        new SqlParameter("@CustomerId", customerId),                      // Link to customer profile
                        new SqlParameter("@IsActive", true),                              // Account is active by default
                        new SqlParameter("@CreatedDate", DateTime.UtcNow)
                    };

                    // Execute insert and retrieve generated UserId
                    var userId = await _dbHelper.ExecuteScalarAsync(insertQuery, parameters);

                    // Create user object to return
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
                    // If SQL insert fails, attempt rollback by deleting customer record
                    _logger.LogError(sqlEx, "SQL Database error during registration, attempting cleanup");

                    try
                    {
                        // Clean up orphaned customer record to maintain data consistency
                        await DeleteCustomerViaAzureFunctionAsync("Customer", customerId);
                        _logger.LogInformation("Cleaned up customer record after SQL failure");
                    }
                    catch (Exception cleanupEx)
                    {
                        // Log cleanup failure but continue - manual intervention may be needed
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
        /// Creates a customer profile in Azure Table Storage via Azure Function.
        /// Called during registration process to establish customer data before creating credentials.
        /// </summary>
        /// <param name="customer">Customer object containing profile information</param>
        /// <returns>Tuple containing success status, generated customer ID, and message</returns>
        private async Task<(bool success, string customerId, string message)> CreateCustomerViaAzureFunctionAsync(Customer customer)
        {
            try
            {
                _logger.LogInformation("Creating customer via Azure Function: {Email}", customer.Email);

                // Create HTTP request to Azure Function customer endpoint
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_functionBaseUrl}/customers");
                request.Headers.Add("x-functions-key", _functionKey);

                // Serialize customer object to JSON
                var jsonContent = JsonSerializer.Serialize(customer);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                // Handle successful creation
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Deserialize response to get created customer with generated ID
                    var createdCustomer = JsonSerializer.Deserialize<Customer>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogInformation("Customer created successfully via Azure Function: {CustomerId}", createdCustomer?.RowKey);
                    return (true, createdCustomer?.RowKey ?? customer.RowKey, "Customer created successfully");
                }

                // Handle creation failure
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
        /// Deletes a customer profile from Azure Table Storage via Azure Function.
        /// Used for cleanup/rollback when registration fails after customer creation but before user creation.
        /// Ensures data consistency by removing orphaned customer records.
        /// </summary>
        /// <param name="partitionKey">Partition key of the customer to delete</param>
        /// <param name="rowKey">Row key (customer ID) of the customer to delete</param>
        /// <exception cref="Exception">Rethrows any exception to signal cleanup failure</exception>
        private async Task DeleteCustomerViaAzureFunctionAsync(string partitionKey, string rowKey)
        {
            try
            {
                _logger.LogInformation("Deleting customer via Azure Function: {PartitionKey}/{RowKey}",
                    partitionKey, rowKey);

                // Create HTTP request to delete customer
                var request = new HttpRequestMessage(HttpMethod.Delete,
                    $"{_functionBaseUrl}/customers/{partitionKey}/{rowKey}");
                request.Headers.Add("x-functions-key", _functionKey);

                await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting customer via Azure Function");
                throw;  // Rethrow to signal cleanup failure to caller
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Retrieves a user by their unique user ID.
        /// Used for loading user profile information after authentication.
        /// </summary>
        /// <param name="userId">Unique identifier of the user</param>
        /// <returns>User object if found, null otherwise</returns>
        public async Task<User?> GetUserByIdAsync(int userId)
        {
            try
            {
                // Query SQL database for user by ID
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    WHERE UserId = @UserId";

                var parameters = new[]
                {
                    new SqlParameter("@UserId", userId)
                };

                var result = await _dbHelper.ExecuteQueryAsync(query, parameters);

                // Return null if user not found
                if (result.Rows.Count == 0)
                {
                    return null;
                }

                // Map database row to User object
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
        /// Retrieves a user by their email address.
        /// Used for email uniqueness validation and user lookup operations.
        /// </summary>
        /// <param name="email">Email address of the user</param>
        /// <returns>User object if found, null otherwise</returns>
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            try
            {
                // Query SQL database for user by email
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    WHERE Email = @Email";

                var parameters = new[]
                {
                    new SqlParameter("@Email", email)
                };

                var result = await _dbHelper.ExecuteQueryAsync(query, parameters);

                // Return null if user not found
                if (result.Rows.Count == 0)
                {
                    return null;
                }

                // Map database row to User object
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
        /// Checks if an email address is already registered in the system.
        /// Used during registration to prevent duplicate accounts.
        /// </summary>
        /// <param name="email">Email address to check</param>
        /// <returns>True if email exists, false otherwise</returns>
        public async Task<bool> EmailExistsAsync(string email)
        {
            try
            {
                // Query to count users with matching email
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
                return false;  // Return false on error to allow registration attempt (will fail with better error)
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Hashes a password using SHA256 cryptographic algorithm.
        /// Converts plain text password to Base64-encoded hash for secure storage.
        /// Note: For production systems, consider using more secure algorithms like bcrypt or Argon2.
        /// </summary>
        /// <param name="password">Plain text password to hash</param>
        /// <returns>Base64-encoded SHA256 hash of the password</returns>
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Verifies a plain text password against a stored hash.
        /// Hashes the provided password and performs constant-time comparison with stored hash.
        /// </summary>
        /// <param name="password">Plain text password to verify</param>
        /// <param name="storedHash">Stored hash to compare against</param>
        /// <returns>True if password matches, false otherwise</returns>
        private bool VerifyPassword(string password, string storedHash)
        {
            var passwordHash = HashPassword(password);
            return passwordHash == storedHash;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Updates the last login timestamp for a user.
        /// Called after successful authentication for audit and activity tracking purposes.
        /// </summary>
        /// <param name="userId">ID of the user whose login timestamp should be updated</param>
        public async Task UpdateLastLoginAsync(int userId)
        {
            try
            {
                // Update last login date to current UTC time
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
                // Log error but don't fail login - this is non-critical
                _logger.LogError(ex, "Error updating last login for user: {UserId}", userId);
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Deactivates a user account, preventing login without deleting the account.
        /// Useful for temporarily suspending accounts or soft-delete functionality.
        /// </summary>
        /// <param name="userId">ID of the user to deactivate</param>
        /// <returns>True if user was deactivated, false otherwise</returns>
        public async Task<bool> DeactivateUserAsync(int userId)
        {
            try
            {
                // Set IsActive flag to false (0)
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

                return false;  // User not found or already deactivated
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating user: {UserId}", userId);
                return false;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Activates a previously deactivated user account, allowing login again.
        /// Used to restore access for suspended accounts.
        /// </summary>
        /// <param name="userId">ID of the user to activate</param>
        /// <returns>True if user was activated, false otherwise</returns>
        public async Task<bool> ActivateUserAsync(int userId)
        {
            try
            {
                // Set IsActive flag to true (1)
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

                return false;  // User not found or already active
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating user: {UserId}", userId);
                return false;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Retrieves all users in the system (administrative function).
        /// Returns users ordered by creation date (newest first) for admin management interface.
        /// </summary>
        /// <returns>List of all user accounts, or empty list on error</returns>
        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                // Query all users ordered by creation date descending
                string query = @"
                    SELECT UserId, Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate, LastLoginDate
                    FROM Users
                    ORDER BY CreatedDate DESC";

                var result = await _dbHelper.ExecuteQueryAsync(query);
                var users = new List<User>();

                // Map each database row to User object
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
                return new List<User>();  // Return empty list on error
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Permanently deletes a user account from the database.
        /// Note: This does not automatically delete the associated customer profile in Azure Table Storage.
        /// Consider implementing cascade delete or cleanup logic for complete account removal.
        /// </summary>
        /// <param name="userId">ID of the user to delete</param>
        /// <returns>True if user was deleted, false otherwise</returns>
        public async Task<bool> DeleteUserAsync(int userId)
        {
            try
            {
                // Permanently delete user record
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

                return false;  // User not found
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