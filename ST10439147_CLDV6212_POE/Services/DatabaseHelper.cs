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
    /// ADO.NET helper service for database operations
    /// Provides methods for executing queries and stored procedures
    /// </summary>
    public class DatabaseHelper
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseHelper> _logger;

        public DatabaseHelper(IConfiguration configuration, ILogger<DatabaseHelper> logger)
        {
            // Use GetConnectionString() which automatically looks in ConnectionStrings section
            _connectionString = configuration.GetConnectionString("AzureSQL")
                ?? throw new InvalidOperationException("Database connection string not found");
            _logger = logger;
        }

        /// <summary>
        /// Executes a SQL query and returns a DataTable
        /// </summary>
        public async Task<DataTable> ExecuteQueryAsync(string query, params SqlParameter[] parameters)
        {
            var dataTable = new DataTable();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                await connection.OpenAsync();

                using var adapter = new SqlDataAdapter(command);
                adapter.Fill(dataTable);

                _logger.LogInformation("Query executed successfully: {Query}", query);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error executing query: {Query}", query);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing query: {Query}", query);
                throw;
            }

            return dataTable;
        }

        /// <summary>
        /// Executes a non-query SQL command (INSERT, UPDATE, DELETE)
        /// </summary>
        public async Task<int> ExecuteNonQueryAsync(string query, params SqlParameter[] parameters)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                await connection.OpenAsync();
                int rowsAffected = await command.ExecuteNonQueryAsync();

                _logger.LogInformation("NonQuery executed successfully: {Query}, Rows affected: {Rows}",
                    query, rowsAffected);

                return rowsAffected;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error executing non-query: {Query}", query);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing non-query: {Query}", query);
                throw;
            }
        }

        /// <summary>
        /// Executes a scalar query and returns a single value
        /// </summary>
        public async Task<object?> ExecuteScalarAsync(string query, params SqlParameter[] parameters)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(query, connection);

                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                await connection.OpenAsync();
                var result = await command.ExecuteScalarAsync();

                _logger.LogInformation("Scalar query executed successfully: {Query}", query);

                return result;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error executing scalar query: {Query}", query);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing scalar query: {Query}", query);
                throw;
            }
        }

        /// <summary>
        /// Executes a stored procedure and returns a DataTable
        /// </summary>
        public async Task<DataTable> ExecuteStoredProcedureAsync(string procedureName, params SqlParameter[] parameters)
        {
            var dataTable = new DataTable();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(procedureName, connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }

                await connection.OpenAsync();

                using var adapter = new SqlDataAdapter(command);
                adapter.Fill(dataTable);

                _logger.LogInformation("Stored procedure executed successfully: {Procedure}", procedureName);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error executing stored procedure: {Procedure}", procedureName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing stored procedure: {Procedure}", procedureName);
                throw;
            }

            return dataTable;
        }

        /// <summary>
        /// Tests the database connection
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("Database connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                return false;
            }
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//