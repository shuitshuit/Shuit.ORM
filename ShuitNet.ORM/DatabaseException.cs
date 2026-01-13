using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ShuitNet.ORM.Attribute;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShuitNet.ORM
{
    /// <summary>
    /// Exception thrown when a database operation fails
    /// </summary>
    public class DatabaseException : Exception
    {
        public string? Sql { get; }
        public string? ParametersInfo { get; }

        public DatabaseException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }

        public DatabaseException(string message, string? sql, string? parametersInfo, Exception? innerException = null)
            : base(message, innerException)
        {
            Sql = sql;
            ParametersInfo = parametersInfo;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(base.ToString());

            if (!string.IsNullOrEmpty(Sql))
            {
                sb.AppendLine($"SQL: {Sql}");
            }

            if (!string.IsNullOrEmpty(ParametersInfo))
            {
                sb.AppendLine($"Parameters: {ParametersInfo}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Helper class for database error handling
    /// </summary>
    public static class DatabaseErrorHelper
    {
        private static IHostEnvironment? _hostEnvironment;
        private static ILogger? _logger;

        /// <summary>
        /// Configure the host environment for error handling
        /// </summary>
        public static void Configure(IHostEnvironment? hostEnvironment, ILogger? logger = null)
        {
            _hostEnvironment = hostEnvironment;
            _logger = logger;
        }

        /// <summary>
        /// Format parameter information with Mask attribute support
        /// </summary>
        public static string FormatParameters(object? parameter)
        {
            if (parameter == null)
                return "null";

            var sb = new StringBuilder();
            sb.Append("{ ");

            if (parameter is System.Collections.Generic.IDictionary<string, object> dict)
            {
                var items = dict.Select(kvp => $"{kvp.Key} = {FormatValue(kvp.Value)}");
                sb.Append(string.Join(", ", items));
            }
            else
            {
                var properties = parameter.GetType().GetProperties();
                var items = new List<string>();

                foreach (var property in properties)
                {
                    var value = property.GetValue(parameter);
                    var formattedValue = ShouldMaskProperty(property)
                        ? "***MASKED***"
                        : FormatValue(value);
                    items.Add($"{property.Name} = {formattedValue}");
                }

                sb.Append(string.Join(", ", items));
            }

            sb.Append(" }");
            return sb.ToString();
        }

        /// <summary>
        /// Format parameter information for a generic instance with Mask attribute support
        /// </summary>
        public static string FormatInstanceParameters<T>(T instance)
        {
            if (instance == null)
                return "null";

            var sb = new StringBuilder();
            sb.Append("{ ");

            var properties = typeof(T).GetProperties();
            var items = new List<string>();

            foreach (var property in properties)
            {
                // Skip properties with IgnoreAttribute or SerialAttribute
                if (property.GetCustomAttribute<IgnoreAttribute>() != null ||
                    property.GetCustomAttribute<SerialAttribute>() != null)
                    continue;

                var value = property.GetValue(instance);
                var formattedValue = ShouldMaskProperty(property)
                    ? "***MASKED***"
                    : FormatValue(value);
                items.Add($"{property.Name} = {formattedValue}");
            }

            sb.Append(string.Join(", ", items));
            sb.Append(" }");
            return sb.ToString();
        }

        private static bool ShouldMaskProperty(PropertyInfo property)
        {
            return property.GetCustomAttribute<MaskAttribute>() != null;
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return "null";

            if (value is string str)
                return $"\"{str}\"";

            if (value is DateTime dt)
                return $"\"{dt:yyyy-MM-dd HH:mm:ss}\"";

            if (value is DateTimeOffset dto)
                return $"\"{dto:yyyy-MM-dd HH:mm:ss zzz}\"";

            if (value is Guid guid)
                return $"\"{guid}\"";

            if (value is byte[] bytes)
                return $"byte[{bytes.Length}]";

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                var items = enumerable.Cast<object>().Take(5).Select(FormatValue);
                var preview = string.Join(", ", items);
                return $"[{preview}...]";
            }

            return value.ToString() ?? "null";
        }

        /// <summary>
        /// Check if detailed error information should be logged based on environment
        /// </summary>
        public static bool ShouldLogDetailedErrors()
        {
            try
            {
                // Check IHostEnvironment first
                if (_hostEnvironment != null)
                {
                    return _hostEnvironment.IsDevelopment();
                }

                // Fallback to environment variable check
                var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

                if (environmentName?.Equals("Development", StringComparison.OrdinalIgnoreCase) == true)
                    return true;

                // Check for DEBUG flag
                #if DEBUG
                return true;
                #else
                return false;
                #endif
            }
            catch
            {
                // If we can't determine environment, default to safe mode (no detailed errors)
                return false;
            }
        }

        /// <summary>
        /// Create a DatabaseException with appropriate detail level
        /// </summary>
        public static DatabaseException CreateException(string operation, string? sql, object? parameters, Exception innerException)
        {
            var shouldLogDetails = ShouldLogDetailedErrors();

            // Log error if logger is available
            if (_logger != null)
            {
                if (shouldLogDetails)
                {
                    var parametersInfo = parameters != null ? FormatParameters(parameters) : null;
                    _logger.LogError(innerException,
                        "Database operation '{Operation}' failed. SQL: {Sql}, Parameters: {Parameters}",
                        operation, sql, parametersInfo);
                }
                else
                {
                    _logger.LogError(innerException,
                        "Database operation '{Operation}' failed",
                        operation);
                }
            }

            if (shouldLogDetails)
            {
                var parametersInfo = parameters != null ? FormatParameters(parameters) : null;
                return new DatabaseException(
                    $"Database operation '{operation}' failed. See Sql and ParametersInfo for details.",
                    sql,
                    parametersInfo,
                    innerException
                );
            }
            else
            {
                // In production, don't expose SQL or parameters
                return new DatabaseException(
                    $"Database operation '{operation}' failed: {innerException.Message}",
                    innerException
                );
            }
        }

        /// <summary>
        /// Create a DatabaseException for typed instance operations
        /// </summary>
        public static DatabaseException CreateException<T>(string operation, string? sql, T? instance, Exception innerException)
        {
            var shouldLogDetails = ShouldLogDetailedErrors();

            // Log error if logger is available
            if (_logger != null)
            {
                if (shouldLogDetails)
                {
                    var parametersInfo = instance != null ? FormatInstanceParameters(instance) : null;
                    _logger.LogError(innerException,
                        "Database operation '{Operation}' failed. SQL: {Sql}, Parameters: {Parameters}",
                        operation, sql, parametersInfo);
                }
                else
                {
                    _logger.LogError(innerException,
                        "Database operation '{Operation}' failed",
                        operation);
                }
            }

            if (shouldLogDetails)
            {
                var parametersInfo = instance != null ? FormatInstanceParameters(instance) : null;
                return new DatabaseException(
                    $"Database operation '{operation}' failed. See Sql and ParametersInfo for details.",
                    sql,
                    parametersInfo,
                    innerException
                );
            }
            else
            {
                // In production, don't expose SQL or parameters
                return new DatabaseException(
                    $"Database operation '{operation}' failed: {innerException.Message}",
                    innerException
                );
            }
        }
    }
}
