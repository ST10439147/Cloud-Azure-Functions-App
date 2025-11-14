// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - ADO.NET Database Helper

using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ST10439147_CLDV6212_POE.Services
{
    /// <summary>
    /// Helper service providing low-level database access using ADO.NET.
    /// Encapsulates common database operations with proper connection management,
    /// parameterized queries for SQL injection prevention, and comprehensive error logging.
    /// Supports queries, non-queries, scalar operations, and stored procedures.
    /// </summary>
    public class DatabaseHelper
    {
        // Private fields for configuration and logging
        private readonly string _connectionString;
        private readonly ILogger<DatabaseHelper> _logger;

        /// <summary>
        /// Initializes a new instance of the DatabaseHelper with database configuration.
        /// </summary>
        /// <param name="configuration">Configuration provider for accessing connection strings</param>
        /// <param name="logger">Logger for recording database operations and errors</param>
        /// <exception cref="InvalidOperationException">Thrown when database connection string is not configured</exception>
        public DatabaseHelper(IConfiguration configuration, ILogger<DatabaseHelper> logger)
        {
            // Retrieve connection string from ConnectionStrings section of configuration
            // GetConnectionString() automatically looks in the ConnectionStrings section
            _connectionString = configuration.GetConnectionString("AzureSQL")
                ?? throw new InvalidOperationException("Database connection string not found");
            _logger = logger;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Executes a SQL SELECT query and returns the results as a DataTable.
        /// Uses parameterized queries to prevent SQL injection attacks.
        /// Properly manages database connections with automatic disposal.
        /// </summary>
        /// <param name="query">SQL SELECT query to execute</param>
        /// <param name="parameters">Optional SQL parameters for the query (prevents SQL injection)</param>
        /// <returns>DataTable containing query results</returns>
        /// <exception cref="SqlException">Thrown when a SQL-specific error occurs</exception>
        /// <exception cref="Exception">Thrown for other unexpected errors</exception>
        public async Task<DataTable> ExecuteQueryAsync(string query, params SqlParameter[] parameters)
        {
            var dataTable = new DataTable();

            try
            {
                // Create connection - using statement ensures proper disposal
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                // Add parameters if provided (for parameterized queries)
                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                // Open connection asynchronously to avoid blocking
                await connection.OpenAsync();

                // Use SqlDataAdapter to fill DataTable with query results
                using var adapter = new SqlDataAdapter(command);
                adapter.Fill(dataTable);

                _logger.LogInformation("Query executed successfully: {Query}", query);
            }
            catch (SqlException ex)
            {
                // Log SQL-specific errors (connection issues, syntax errors, constraint violations)
                _logger.LogError(ex, "SQL error executing query: {Query}", query);
                throw;
            }
            catch (Exception ex)
            {
                // Log general errors
                _logger.LogError(ex, "Error executing query: {Query}", query);
                throw;
            }

            return dataTable;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Executes a non-query SQL command (INSERT, UPDATE, DELETE) that modifies data.
        /// Returns the number of rows affected by the operation.
        /// Uses parameterized queries to prevent SQL injection attacks.
        /// </summary>
        /// <param name="query">SQL command to execute (INSERT, UPDATE, or DELETE)</param>
        /// <param name="parameters">Optional SQL parameters for the command (prevents SQL injection)</param>
        /// <returns>Number of rows affected by the command</returns>
        /// <exception cref="SqlException">Thrown when a SQL-specific error occurs</exception>
        /// <exception cref="Exception">Thrown for other unexpected errors</exception>
        public async Task<int> ExecuteNonQueryAsync(string query, params SqlParameter[] parameters)
        {
            try
            {
                // Create connection - using statement ensures proper disposal
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                // Add parameters if provided (for parameterized queries)
                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                // Open connection asynchronously
                await connection.OpenAsync();

                // Execute command and get number of affected rows
                int rowsAffected = await command.ExecuteNonQueryAsync();

                _logger.LogInformation("NonQuery executed successfully: {Query}, Rows affected: {Rows}",
                    query, rowsAffected);

                return rowsAffected;
            }
            catch (SqlException ex)
            {
                // Log SQL-specific errors (foreign key violations, unique constraints, etc.)
                _logger.LogError(ex, "SQL error executing non-query: {Query}", query);
                throw;
            }
            catch (Exception ex)
            {
                // Log general errors
                _logger.LogError(ex, "Error executing non-query: {Query}", query);
                throw;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Executes a SQL query that returns a single value (first column of first row).
        /// Commonly used for COUNT, SUM, MAX, MIN, or retrieving generated IDs (SCOPE_IDENTITY).
        /// Uses parameterized queries to prevent SQL injection attacks.
        /// </summary>
        /// <param name="query">SQL query to execute that returns a single value</param>
        /// <param name="parameters">Optional SQL parameters for the query (prevents SQL injection)</param>
        /// <returns>Single value returned by the query, or null if no results</returns>
        /// <exception cref="SqlException">Thrown when a SQL-specific error occurs</exception>
        /// <exception cref="Exception">Thrown for other unexpected errors</exception>
        public async Task<object?> ExecuteScalarAsync(string query, params SqlParameter[] parameters)
        {
            try
            {
                // Create connection - using statement ensures proper disposal
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                // Add parameters if provided (for parameterized queries)
                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                // Open connection asynchronously
                await connection.OpenAsync();

                // Execute and return single value (first column of first row)
                var result = await command.ExecuteScalarAsync();

                _logger.LogInformation("Scalar query executed successfully: {Query}", query);

                return result;
            }
            catch (SqlException ex)
            {
                // Log SQL-specific errors
                _logger.LogError(ex, "SQL error executing scalar query: {Query}", query);
                throw;
            }
            catch (Exception ex)
            {
                // Log general errors
                _logger.LogError(ex, "Error executing scalar query: {Query}", query);
                throw;
            }
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Executes a stored procedure and returns the results as a DataTable.
        /// Stored procedures provide better performance, security, and maintainability for complex operations.
        /// Uses parameterized execution to safely pass input parameters.
        /// </summary>
        /// <param name="procedureName">Name of the stored procedure to execute</param>
        /// <param name="parameters">Optional SQL parameters to pass to the stored procedure</param>
        /// <returns>DataTable containing the stored procedure results</returns>
        /// <exception cref="SqlException">Thrown when a SQL-specific error occurs</exception>
        /// <exception cref="Exception">Thrown for other unexpected errors</exception>
        public async Task<DataTable> ExecuteStoredProcedureAsync(string procedureName, params SqlParameter[] parameters)
        {
            var dataTable = new DataTable();

            try
            {
                // Create connection - using statement ensures proper disposal
                using var connection = new SqlConnection(_connectionString);

                // Create command configured for stored procedure execution
                using var command = new SqlCommand(procedureName, connection)
                {
                    CommandType = CommandType.StoredProcedure  // Specify command type as stored procedure
                };

                // Add parameters if provided
                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                // Open connection asynchronously
                await connection.OpenAsync();

                // Use SqlDataAdapter to fill DataTable with stored procedure results
                using var adapter = new SqlDataAdapter(command);
                adapter.Fill(dataTable);

                _logger.LogInformation("Stored procedure executed successfully: {Procedure}", procedureName);
            }
            catch (SqlException ex)
            {
                // Log SQL-specific errors (procedure not found, parameter mismatches, etc.)
                _logger.LogError(ex, "SQL error executing stored procedure: {Procedure}", procedureName);
                throw;
            }
            catch (Exception ex)
            {
                // Log general errors
                _logger.LogError(ex, "Error executing stored procedure: {Procedure}", procedureName);
                throw;
            }

            return dataTable;
        }

        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        /// <summary>
        /// Tests the database connection by attempting to open it.
        /// Useful for health checks, startup validation, and troubleshooting connectivity issues.
        /// Does not throw exceptions - returns false on failure for graceful handling.
        /// </summary>
        /// <returns>True if connection successful, false otherwise</returns>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Attempt to open database connection
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Connection successful - it will be automatically closed by using statement
                _logger.LogInformation("Database connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                // Connection failed - log error and return false (don't throw)
                _logger.LogError(ex, "Database connection test failed");
                return false;
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//