using Npgsql;
using NpgsqlTypes;
using ShuitNet.ORM.Attribute;
using ShuitNet.ORM.PostgreSQL.LinqToSql;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


namespace ShuitNet.ORM.PostgreSQL
{
    public class PostgreSqlConnect : IDisposable
    {
        public static NamingCase NamingCase { get; set; } = NamingCase.CamelCase;
        public string Host { get; set; }
        public int Port { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string ConnectionString { get; set; }
        private readonly NpgsqlConnection _con;
        private NpgsqlTransaction? _currentTransaction;

        public PostgreSqlConnect(string host, int port, string database, string username,
            string password)
        {
            Host = host;
            Port = port;
            Database = database;
            Username = username;
            Password = password;
            ConnectionString = ToString();
            _con = new NpgsqlConnection(ConnectionString);
        }

        /// <summary>
        /// Connect to the database using a connection string.
        /// </summary>
        /// <param name="connectionString">Connection string format: Host=xxx;Port=xxx;Database=xxx;Username=xxx;Password=xxx</param>
        /// <exception cref="ArgumentException"></exception>
        public PostgreSqlConnect(string connectionString)
        {
            NpgsqlConnectionStringBuilder builder = new(connectionString);
            try
            {
                Host = builder.Host!;
                Port = builder.Port;
                Database = builder.Database!;
                Username = builder.Username!;
                Password = builder.Password!;
                ConnectionString = connectionString;
                _con = new NpgsqlConnection(ConnectionString);
            }
            catch (NullReferenceException)
            {
                throw new ArgumentException("Invalid connection string");
            }
        }

        public void Dispose()
        {
            _con.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// connection string format: Host=xxx;Port=xxx;Database=xxx;Username=xxx;Password=xxx
        /// </summary>
        /// <returns></returns>
        public sealed override string ToString() =>
            $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";

        public static implicit operator string(PostgreSqlConnect connection) => connection.ToString();

        public async Task OpenAsync() => await _con.OpenAsync();

        public void Open() => _con.Open();

        public async Task CloseAsync() => await _con.CloseAsync();

        public void Close() => _con.Close();

        public async ValueTask<NpgsqlTransaction> BeginTransactionAsync()
        {
            _currentTransaction = await _con.BeginTransactionAsync();
            return _currentTransaction;
        }

        public async Task CommitAsync()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("No active transaction.");
            await _currentTransaction.CommitAsync();
            _currentTransaction = null;
        }

        public async Task RollbackAsync()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("No active transaction.");
            await _currentTransaction.RollbackAsync();
            _currentTransaction = null;
        }

        public async Task ExecuteInTransactionAsync(Func<Task> action)
        {
            await using var tx = await _con.BeginTransactionAsync();
            _currentTransaction = tx;
            try
            {
                await action();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
            finally
            {
                _currentTransaction = null;
            }
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action)
        {
            await using var tx = await _con.BeginTransactionAsync();
            _currentTransaction = tx;
            try
            {
                var result = await action();
                await tx.CommitAsync();
                return result;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
            finally
            {
                _currentTransaction = null;
            }
        }

        /// <summary>
        /// Retrieve data using the primary key value or an anonymous function.
        /// </summary>
        /// <typeparam name="T">Data class</typeparam>
        /// <param name="key">Primary key or anonymous function</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<T> GetAsync<T>(object key)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(key));

            if (key is string s)
            {
                if (string.IsNullOrEmpty(s))
                    throw new ArgumentException("Key is null or empty.");
            }

            // 匿名型の場合は、GetByAnonymousAsyncを呼び出す ex: new { Id = 1, Name = "test" }
            if (key.GetType().Name.Contains("<>f__AnonymousType"))
                return await GetByAnonymousAsync<T>(key);

            var tableName = GetTableName<T>();
            var keyColumnName = GetKeyColumnName<T>();
            var sql = $"SELECT * FROM {tableName} WHERE \"{keyColumnName}\" = @key";

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                AddParameterWithProperType(command, "key", key);
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("Sequence contains no elements.");

                var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    string fieldName = reader.GetName(i);
                    // カラム名からプロパティ名を取得
                    var property = typeof(T).GetProperty(GetPropertyName<T>(fieldName))
                        ?? throw new InvalidOperationException($"No property matching {fieldName} was found.");
                    var value = reader.GetValue(i);
                    SetValue(property, ref instance, value);
                }
                return instance;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("GetAsync", sql, new { key }, ex);
            }
        }

        /// <summary>
        /// Retrieve data using the primary key value or an anonymous function.
        /// </summary>
        /// <typeparam name="T">Represents the type of objects being retrieved from the database.</typeparam>
        /// <param name="key">Primary key or anonymous function</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public T Get<T>(object key) => GetAsync<T>(key).Result;

        /// <summary>
        /// Retrieves multiple records from a database based on the provided anonymous type key.
        /// </summary>
        /// <typeparam name="T">Represents the type of the records to be retrieved from the database.</typeparam>
        /// <param name="key">An anonymous type containing the criteria for selecting records from the database.</param>
        /// <returns>An enumerable collection of records matching the specified criteria.</returns>
        /// <exception cref="ArgumentException">Thrown when the provided key is not of an anonymous type.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a property corresponding to a database column is not found in the specified type.</exception>
        public async Task<IEnumerable<T>> GetMultipleAsync<T>(object key)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(key));
            // 匿名型の場合は、GetByAnonymousAsyncを呼び出す ex: new { Id = 1, Name = "test" }
            if (!key.GetType().Name.Contains("<>f__AnonymousType"))
                throw new ArgumentException("Key is not anonymous type.");

            var tableName = GetTableName<T>();
            var sql = $"SELECT * FROM {tableName} WHERE ";
            sql = key.GetType().GetProperties()
                .Aggregate(sql, (current, property) =>
                    current + $"\"{GetColumnName<T>(property)}\" = @{property.Name} AND ");
            sql = sql[..^5];

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                foreach (var property in key.GetType().GetProperties())
                {
                    var value = property.GetValue(key);
                    AddParameterWithProperType(command, property.Name, value);
                }

                await using var reader = await command.ExecuteReaderAsync();
                T[] values = [];
                while (reader.Read())
                {
                    var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        string fieldName = reader.GetName(i);
                        // カラム名からプロパティ名を取得
                        var property = typeof(T).GetProperty(GetPropertyName<T>(fieldName))
                            ?? throw new InvalidOperationException($"No property matching {fieldName} was found.");
                        var value = reader.GetValue(i);
                        SetValue(property, ref instance, value);
                    }
                    values = values.Append(instance).ToArray();
                }
                return values;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("GetMultipleAsync", sql, key, ex);
            }
        }

        private async Task<T> GetByAnonymousAsync<T>(object parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter, nameof(parameter));

            var tableName = GetTableName<T>();
            var sql = $"SELECT * FROM {tableName} WHERE ";
            sql = parameter.GetType().GetProperties()
                .Aggregate(sql, (current, property) =>
                current + $"\"{GetColumnName<T>(property)}\" = @{property.Name} AND ");
            sql = sql[..^5];

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                foreach (var property in parameter.GetType().GetProperties())
                {
                    var value = property.GetValue(parameter);
                    AddParameterWithProperType(command, property.Name, value);
                }

                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new InvalidOperationException("Sequence contains no elements.");
                {
                    var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        string fieldName = reader.GetName(i);
                        // カラム名からプロパティ名を取得
                        var property = typeof(T).GetProperty(GetPropertyName<T>(fieldName))
                            ?? throw new InvalidOperationException($"No property matching {fieldName} was found.");
                        var value = reader.GetValue(i);
                        SetValue(property, ref instance, value);
                    }
                    return instance;
                }
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("GetByAnonymousAsync", sql, parameter, ex);
            }
        }

        /// <summary>
        /// Retrieves all records of a specified type from a database asynchronously, filtering them based on a provided
        /// condition.
        /// </summary>
        /// <typeparam name="T">Represents the type of objects being retrieved from the database.</typeparam>
        /// <param name="func">A function that determines whether a retrieved object should be included in the result set.</param>
        /// <returns>An enumerable collection of objects that meet the specified condition.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a property cannot be found for mapping or when no elements match the condition.</exception>
        public async Task<IEnumerable<T>> GetAllAsync<T>(Func<T, bool> func)
        {
            var type = typeof(T);
            var tableName = GetTableName<T>();
            var sql = $"SELECT * FROM {tableName}";

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                await using var reader = await command.ExecuteReaderAsync();
                List<T> list = [];
                while (await reader.ReadAsync())
                {
                    var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var fieldName = reader.GetName(i);
                        // カラム名からプロパティ名を取得
                        var property = type.GetProperty(GetPropertyName<T>(fieldName))
                            ?? throw new InvalidOperationException($"No property matching {fieldName} was found.");
                        var value = reader.GetValue(i);
                        SetValue(property, ref instance, value);
                    }
                    if (func(instance))
                        list.Add(instance);
                }
                return list.Any() ? list : throw new InvalidOperationException("Sequence contains no elements.");
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("GetAllAsync", sql, null, ex);
            }
        }

        public IEnumerable<T> GetAll<T>(Func<T, bool> func) => GetAllAsync(func).Result;

        public async Task<IEnumerable<T>> GetAllAsync<T>()
        {
            var type = typeof(T);
            var tableName = GetTableName<T>();
            var sql = $"SELECT * FROM {tableName}";

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                await using var reader = await command.ExecuteReaderAsync();
                List<T> list = [];
                while (await reader.ReadAsync())
                {
                    var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var fieldName = reader.GetName(i);
                        // カラム名からプロパティ名を取得
                        var property = type.GetProperty(GetPropertyName<T>(fieldName))
                            ?? throw new InvalidOperationException($"No property matching {fieldName} was found.");
                        var value = reader.GetValue(i);
                        SetValue(property, ref instance, value);
                    }
                    list.Add(instance);
                }
                return list;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("GetAllAsync", sql, null, ex);
            }
        }

        public IEnumerable<T> GetAll<T>() => GetAllAsync<T>().Result;

        public async Task<int> DeleteAsync<T>(object key)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(key));

            if (key is string s)
            {
                if (string.IsNullOrEmpty(s))
                    throw new ArgumentException("Key is null or empty.");
            }

            var tableName = GetTableName<T>();
            var keyColumnName = GetKeyColumnName<T>();
            var sql = $"DELETE FROM {tableName} WHERE \"{keyColumnName}\" = @key";

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                AddParameterWithProperType(command, "key", key);
                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("DeleteAsync", sql, new { key }, ex);
            }
        }

        public int Delete<T>(object key) => DeleteAsync<T>(key).Result;

        public async Task<int> DeleteAsync<T>(T instance)
        {
            ArgumentNullException.ThrowIfNull(instance, nameof(instance));

            var tableName = GetTableName<T>();
            var keyColumnName = GetKeyColumnName<T>();
            var sql = $"DELETE FROM {tableName} WHERE \"{keyColumnName}\" = @key";

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                var key = typeof(T).GetProperty(GetPropertyName<T>(keyColumnName))?.GetValue(instance)
                    ?? throw new ArgumentException("Key is null.");
                AddParameterWithProperType(command, "key", key);
                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException<T>("DeleteAsync", sql, instance, ex);
            }
        }

        public int Delete<T>(T instance) => DeleteAsync(instance).Result;

        public async Task<int> UpdateAsync<T>(T instance)
        {
            ArgumentNullException.ThrowIfNull(instance, nameof(instance));

            var tableName = GetTableName<T>();
            var keyColumnName = GetKeyColumnName<T>();
            var sql = $"UPDATE {tableName} SET ";
            sql = typeof(T).GetProperties()
                .Where(property => property.GetCustomAttribute<IgnoreAttribute>() == null &&
                    property.GetCustomAttribute<SerialAttribute>() == null)
                .Aggregate(sql, (current, property) =>
                    current + $"\"{GetColumnName<T>(property)}\" = @{property.Name},");
            sql = sql[..^1] + $" WHERE \"{keyColumnName}\" = @{keyColumnName}";

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                foreach (var property in typeof(T).GetProperties())
                {
                    if (property.GetCustomAttribute<IgnoreAttribute>() != null ||
                        property.GetCustomAttribute<SerialAttribute>() != null)
                    {
                        continue;
                    }

                    var propertyValue = property.GetValue(instance);
                    var foreignKey = property.GetCustomAttribute<ForeignKeyAttribute>();
                    if (foreignKey is { Type: not null } && foreignKey.Type != property.PropertyType)
                    {
                        if (propertyValue == null) continue;
                        // 外部キーの場合は値を変換する
                        command.Parameters.AddWithValue(property.Name, GetForeignKeyData(propertyValue, foreignKey.Type));
                    }
                    else
                    {
                        // nullの場合はDBNull.Valueを使用する
                        if (propertyValue == null)
                        {
                            command.Parameters.AddWithValue(property.Name, DBNull.Value);
                        }
                        // JSONB型の場合はJSONシリアライズ
                        else if (property.GetCustomAttribute<JsonbAttribute>() != null)
                        {
                            var json = JsonSerializer.Serialize(propertyValue);
                            var param = new NpgsqlParameter(property.Name, NpgsqlDbType.Jsonb)
                            {
                                Value = json
                            };
                            command.Parameters.Add(param);
                        }
                        // JSON型の場合はJSONシリアライズ
                        else if (property.GetCustomAttribute<JsonAttribute>() != null)
                        {
                            var json = JsonSerializer.Serialize(propertyValue);
                            var param = new NpgsqlParameter(property.Name, NpgsqlDbType.Json)
                            {
                                Value = json
                            };
                            command.Parameters.Add(param);
                        }
                        // Enumの場合は文字列名に変換してUnknown型として設定（PostgreSQL ENUM型対応）
                        else if (property.PropertyType.IsEnum)
                        {
                            var param = new NpgsqlParameter(property.Name, NpgsqlDbType.Unknown)
                            {
                                Value = propertyValue.ToString()!
                            };
                            command.Parameters.Add(param);
                        }
                        // List<T>の場合は配列に変換
                        else if (propertyValue is IEnumerable enumerable && property.PropertyType.IsGenericType)
                        {
                            var elementType = property.PropertyType.GetGenericArguments()[0];
                            if (elementType == typeof(string))
                            {
                                command.Parameters.AddWithValue(property.Name, ((IEnumerable<string>)propertyValue).ToArray());
                            }
                            else
                            {
                                command.Parameters.AddWithValue(property.Name, propertyValue);
                            }
                        }
                        else
                        {
                            AddParameterWithProperType(command, property.Name, propertyValue);
                        }
                    }
                }
                var keyValue = typeof(T).GetProperty(GetPropertyName<T>(keyColumnName))?.GetValue(instance)!;
                AddParameterWithProperType(command, keyColumnName, keyValue);
                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException<T>("UpdateAsync", sql, instance, ex);
            }
        }

        public async Task<int> ExecuteAsync(string sql, object? parameter = null)
        {
            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                if (parameter == null)
                    return await command.ExecuteNonQueryAsync();

                AddParameters(command, parameter);
                return await command.ExecuteNonQueryAsync();
            }
            catch (NullReferenceException)
            {
                throw new ArgumentException("Invalid parameter.");
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("ExecuteAsync", sql, parameter, ex);
            }
        }

        public int Execute(string sql, object? parameter = null) => ExecuteAsync(sql, parameter).Result;

        public async Task<int> InsertAsync<T>(T instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var tableName = GetTableName<T>();
            var sql = $"INSERT INTO {tableName} (";
            var values = "VALUES (";
            foreach (var property in typeof(T).GetProperties())
            {
                // IgnoreAttribute SerialAttributeが付与されている場合は無視する
                if (property.GetCustomAttribute<IgnoreAttribute>() != null ||
                    property.GetCustomAttribute<SerialAttribute>() != null)
                {
                    continue;
                }
                if (property.GetValue(instance) == null) // nullの場合は無視する
                    continue;

                sql += $"\"{GetColumnName<T>(property)}\",";
                values += $"@{property.Name},";
            }
            sql = sql[..^1] + ") ";
            values = values[..^1] + ")";
            sql += values;

            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                foreach (var property in typeof(T).GetProperties())
                {
                    if (property.GetCustomAttribute<IgnoreAttribute>() != null ||
                        property.GetCustomAttribute<SerialAttribute>() != null)
                        continue;

                    var propertyValue = property.GetValue(instance);
                    if (propertyValue == null) continue; // nullの場合は無視する

                    var foreignKey = property.GetCustomAttribute<ForeignKeyAttribute>();
                    if (foreignKey is { Type: not null } && foreignKey.Type != property.PropertyType)
                    {
                        // 外部キーの場合は値を変換する
                        command.Parameters.AddWithValue(property.Name, GetForeignKeyData(propertyValue, foreignKey.Type));
                    }
                    else
                    {
                        // JSONB型の場合はJSONシリアライズ
                        if (property.GetCustomAttribute<JsonbAttribute>() != null)
                        {
                            var json = JsonSerializer.Serialize(propertyValue);
                            var param = new NpgsqlParameter(property.Name, NpgsqlDbType.Jsonb)
                            {
                                Value = json
                            };
                            command.Parameters.Add(param);
                        }
                        // JSON型の場合はJSONシリアライズ
                        else if (property.GetCustomAttribute<JsonAttribute>() != null)
                        {
                            var json = JsonSerializer.Serialize(propertyValue);
                            var param = new NpgsqlParameter(property.Name, NpgsqlDbType.Json)
                            {
                                Value = json
                            };
                            command.Parameters.Add(param);
                        }
                        // Enumの場合は文字列名に変換してUnknown型として設定（PostgreSQL ENUM型対応）
                        else if (property.PropertyType.IsEnum)
                        {
                            var param = new NpgsqlParameter(property.Name, NpgsqlDbType.Unknown)
                            {
                                Value = propertyValue.ToString()!
                            };
                            command.Parameters.Add(param);
                        }
                        // List<T>の場合は配列に変換
                        else if (propertyValue is IEnumerable enumerable && property.PropertyType.IsGenericType)
                        {
                            var elementType = property.PropertyType.GetGenericArguments()[0];
                            if (elementType == typeof(string))
                            {
                                command.Parameters.AddWithValue(property.Name, ((IEnumerable<string>)propertyValue).ToArray());
                            }
                            else
                            {
                                command.Parameters.AddWithValue(property.Name, propertyValue);
                            }
                        }
                        else
                        {
                            AddParameterWithProperType(command, property.Name, propertyValue);
                        }
                    }
                }
                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException<T>("InsertAsync", sql, instance, ex);
            }
        }

        public int Insert<T>(T instance) => InsertAsync(instance).Result;

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameter = null)
        {
            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                if (parameter != null)
                {
                    AddParameters(command, parameter);
                }

                await using var reader = await command.ExecuteReaderAsync();
                List<T> list = [];
                while (await reader.ReadAsync())
                {
                    var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        string fieldName = reader.GetName(i);
                        // カラム名からプロパティ名を取得
                        var property = typeof(T).GetProperty(GetPropertyName<T>(fieldName))
                            ?? throw new InvalidOperationException($"No property matching {fieldName} was found.");
                        var value = reader.GetValue(i);
                        SetValue(property, ref instance, value);
                    }
                    list.Add(instance);
                }
                return list;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("QueryAsync", sql, parameter, ex);
            }
        }

        public IEnumerable<T> Query<T>(string sql, object? parameter = null) => QueryAsync<T>(sql, parameter).Result;

        public async Task<T> QueryFirstAsync<T>(string sql, object? parameter = null)
        {
            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                if (parameter != null)
                {
                    AddParameters(command, parameter);
                }

                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new InvalidOperationException("Sequence contains no elements.");
                {
                    var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        string fieldName = reader.GetName(i);
                        // カラム名からプロパティ名を取得
                        var property = typeof(T).GetProperty(GetPropertyName<T>(fieldName))
                            ?? throw new InvalidOperationException($"No property matching {fieldName} was found.");
                        var value = reader.GetValue(i);
                        SetValue(property, ref instance, value);
                    }
                    return instance;
                }
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("QueryFirstAsync", sql, parameter, ex);
            }
        }

        public T QueryFirst<T>(string sql, object? parameter = null) => QueryFirstAsync<T>(sql, parameter).Result;

        /// <summary>
        /// Execute a SQL query and return a single scalar value
        /// </summary>
        /// <typeparam name="T">The type of the scalar value</typeparam>
        /// <param name="sql">SQL query</param>
        /// <param name="parameter">Parameters</param>
        /// <returns>Scalar value</returns>
        public async Task<T> ExecuteScalarAsync<T>(string sql, object? parameter = null)
        {
            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                command.Transaction = _currentTransaction;
                if (parameter != null)
                {
                    AddParameters(command, parameter);
                }

                var result = await command.ExecuteScalarAsync();
                if (result == null || result is DBNull)
                    return default(T)!;

                return (T)Convert.ChangeType(result, typeof(T));
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not ArgumentNullException)
            {
                throw DatabaseErrorHelper.CreateException("ExecuteScalarAsync", sql, parameter, ex);
            }
        }

        public T ExecuteScalar<T>(string sql, object? parameter = null) => ExecuteScalarAsync<T>(sql, parameter).Result;

        /// <summary>
        /// Create a LINQ queryable interface for the specified type
        /// </summary>
        /// <typeparam name="T">Data class</typeparam>
        /// <returns>IQueryable interface for LINQ operations</returns>
        public PostgreSqlQueryable<T> AsQueryable<T>()
        {
            return new PostgreSqlQueryable<T>(this);
        }

        /// <summary>
        /// Refer to <see cref="NameAttribute"/>. If not assigned, the class name is converted to a snake case.
        /// </summary>
        /// <typeparam name="T">Data class</typeparam>
        /// <returns></returns>
        public static string GetTableName<T>()
        {
            const string alphabet = "ABCDEFGHIZKLMNOPQRSTUVWXYZ";
            var type = typeof(T);
            var name = type.GetCustomAttribute<NameAttribute>()?.Name;
            int index;
            if (string.IsNullOrEmpty(name))
                name = type.Name;
            else
            {
                index = name.LastIndexOf('.');
                return index != -1 ? $"\"{name[..index]}\".\"{name[(index + 1)..]}\"" : $"\"{name}\"";
            }
            index = name.LastIndexOf('.');
            if (!name.Contains('.') || index == 0) return $"\"{name}\"";
            var tableName = name[(index + 1)..];
            name = $"\"{name[..index]}\"";
            // 頭文字を小文字にする
            var head = tableName[..1].ToLower();
            var body = tableName[1..];
            // 大文字はアンダースコアと小文字にする ex: userId -> user_id
            body = alphabet.Where(c => tableName.Contains(c))
                .Aggregate(body, (current, c) => current.Replace(c.ToString(), $"_{char.ToLower(c)}"));
            tableName = $"{head}{body}";
            return $"{name}.\"{tableName}\"";
        }

        /// <summary>
        /// Get the column name of the property to which the <see cref="KeyAttribute"/> is assigned.
        /// </summary>
        /// <typeparam name="T">Data class</typeparam>
        /// <returns>column name</returns>
        /// <exception cref="ArgumentException">If <see cref="KeyAttribute"/> does not exist in T</exception>
        private static string GetKeyColumnName<T>()
        {
            var type = typeof(T);
            foreach (var property in type.GetProperties())
            {
                if (property.GetCustomAttribute<KeyAttribute>() != null)
                    return GetColumnName<T>(property);
            }
            throw new ArgumentException("KeyAttribute not found.", type.Name);
        }

        /// <summary>
        /// Gets the column name of the property.
        /// </summary>
        /// <typeparam name="T">Data class</typeparam>
        /// <returns>column name</returns>
        private static string GetColumnName<T>(PropertyInfo info)
        {
            var name = info.GetCustomAttribute<NameAttribute>()?.Name;
            return name switch
            {
                null => NamingCase switch
                {
                    NamingCase.CamelCase => ConvertToCamelCase(info.Name),
                    NamingCase.SnakeCase => ConvertToSnakeCase(info.Name),
                    NamingCase.KebabCase => ConvertToKebabCase(info.Name),
                    NamingCase.PascalCase => info.Name,
                    _ => throw new ArgumentException("Invalid naming case."),
                },
                _ => name
            };
        }

        /// <summary>
        /// Refer to <see cref="NameAttribute"/>. If not given, returns a match except for the first letter of the property name.
        /// </summary>
        /// <typeparam name="T">Data class</typeparam>
        /// <param name="columnName"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static string GetPropertyName<T>(string columnName)
        {
            var type = typeof(T);
            foreach (var property in type.GetProperties())
            {
                var name = property.GetCustomAttribute<NameAttribute>()?.Name;
                if (name == null)
                {
                    if (property.Name == columnName)
                        return property.Name;
                    else
                    {
                        switch (NamingCase)
                        {
                            case NamingCase.CamelCase:
                                if (ConvertToCamelCase(property.Name) == columnName)
                                    return property.Name;
                                break;
                            case NamingCase.SnakeCase:
                                if (ConvertToSnakeCase(property.Name) == columnName)
                                    return property.Name;
                                break;
                            case NamingCase.KebabCase:
                                if (ConvertToKebabCase(property.Name) == columnName)
                                    return property.Name;
                                break;
                            case NamingCase.PascalCase:
                                if (property.Name == columnName)
                                    return property.Name;
                                break;
                            default:
                                throw new ArgumentException("Invalid naming case.");
                        }
                    }
                }
                else
                {
                    if (name == columnName)
                        return property.Name;
                }
            }
            throw new ArgumentException($"No property matching {columnName} was found.");
        }

        private static object SetForeignKeyData(PropertyInfo info, object value)
        {
            var type = info.PropertyType;
            #region if
            if (type.IsEnum)
                return value is string ? Enum.Parse(type, value.ToString()!) : Enum.ToObject(type, value);
            else if (type == typeof(string))
            {
                return value.ToString()!;
            }
            else if (type == typeof(int))
            {
                return Convert.ToInt32(value);
            }
            else if (type == typeof(long))
            {
                return Convert.ToInt64(value);
            }
            else if (type == typeof(short))
            {
                return Convert.ToInt16(value);
            }
            else if (type == typeof(byte))
            {
                return Convert.ToByte(value);
            }
            else if (type == typeof(bool))
            {
                return Convert.ToBoolean(value);
            }
            else if (type == typeof(DateTime))
            {
                return Convert.ToDateTime(value);
            }
            else if (type == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(value.ToString()!);
            }
            else if (type == typeof(decimal))
            {
                return Convert.ToDecimal(value);
            }
            else if (type == typeof(double))
            {
                return Convert.ToDouble(value);
            }
            else if (type == typeof(float))
            {
                return Convert.ToSingle(value);
            }
            else if (type == typeof(Guid))
            {
                return Guid.Parse(value.ToString()!);
            }
            else if (type == typeof(char))
            {
                return Convert.ToChar(value);
            }
            else if (type == typeof(byte[]))
            {
                return (byte[])value;
            }
            else if (type == typeof(char[]))
            {
                return (char[])value;
            }
            else if (type == typeof(TimeSpan))
            {
                return TimeSpan.Parse(value.ToString()!);
            }
            else if (type == typeof(uint))
            {
                return Convert.ToUInt32(value);
            }
            else if (type == typeof(ulong))
            {
                return Convert.ToUInt64(value);
            }
            else if (type == typeof(ushort))
            {
                return Convert.ToUInt16(value);
            }
            else if (type == typeof(sbyte))
            {
                return Convert.ToSByte(value);
            }
            else if (type == typeof(object))
            {
                return value;
            }
            else if (type == typeof(IEnumerable))
            {
                return value;
            }
            #endregion
            var instance = Activator.CreateInstance(type);
            foreach (var property in type.GetProperties())
            {
                if (property.GetCustomAttribute<KeyAttribute>() != null)
                    property.SetValue(instance, value);
            }
            return instance!;
        }

        private static object GetForeignKeyData(object instance, Type convertTo)
        {
            var type = instance.GetType();
            if (type.IsEnum)
            {
                if (convertTo == typeof(string))
                    return instance?.ToString()!;
                else if (convertTo == typeof(int))
                    return (int)instance;
                else if (convertTo == typeof(long))
                    return (long)instance;
                else if (convertTo == typeof(short))
                    return (short)instance;
                else if (convertTo == typeof(byte))
                    return (byte)instance;
                else if (convertTo == typeof(sbyte))
                    return (sbyte)instance;
                else if (convertTo == typeof(uint))
                    return (uint)instance;
                else if (convertTo == typeof(ulong))
                    return (ulong)instance;
                else if (convertTo == typeof(ushort))
                    return (ushort)instance;
            }
            foreach (var property in type.GetProperties())
            {
                var value = property.GetValue(instance);
                if (property.GetCustomAttribute<KeyAttribute>() == null) continue;
                if (convertTo == typeof(string))
                    return value?.ToString()!;
                else if (convertTo == typeof(int))
                {
                    return Convert.ToInt32(value);
                }
                else if (convertTo == typeof(long))
                {
                    return Convert.ToInt64(value);
                }
                else if (convertTo == typeof(short))
                {
                    return Convert.ToInt16(value);
                }
                else if (convertTo == typeof(byte))
                {
                    return Convert.ToByte(value);
                }
                else if (convertTo == typeof(bool))
                {
                    return Convert.ToBoolean(value);
                }
                else if (convertTo == typeof(DateTime))
                {
                    return Convert.ToDateTime(value);
                }
                else if (convertTo == typeof(DateTimeOffset))
                {
                    return DateTimeOffset.Parse(property.GetValue(instance)!.ToString()!).ToString()!;
                }
                else if (convertTo == typeof(decimal))
                {
                    return Convert.ToDecimal(value);
                }
                else if (convertTo == typeof(double))
                {
                    return Convert.ToDouble(value);
                }
                else if (convertTo == typeof(float))
                {
                    return Convert.ToSingle(value);
                }
                else if (convertTo == typeof(Guid))
                {
                    return Guid.Parse(value?.ToString()!);
                }
                else if (convertTo == typeof(char))
                {
                    return Convert.ToChar(value);
                }
                else if (convertTo == typeof(byte[]))
                {
                    return Convert.ToBase64String((byte[])value!)!;
                }
                else if (convertTo == typeof(char[]))
                {
                    return new string((char[])value!)!;
                }
                else if (convertTo == typeof(TimeSpan))
                {
                    return TimeSpan.Parse(value?.ToString()!).ToString()!;
                }
                else if (convertTo == typeof(uint))
                {
                    return Convert.ToUInt32(value);
                }
                else if (convertTo == typeof(ulong))
                {
                    return Convert.ToUInt64(value);
                }
                else if (convertTo == typeof(ushort))
                {
                    return Convert.ToUInt16(value);
                }
                else
                {
                    return value!.ToString()!;
                }
            }
            throw new ArgumentException("KeyAttribute not found.");
        }

        /// <summary>
        /// Add a single parameter to NpgsqlCommand with proper type handling
        /// </summary>
        /// <param name="command">NpgsqlCommand instance</param>
        /// <param name="parameterName">Parameter name</param>
        /// <param name="value">Parameter value</param>
        private static void AddParameterWithProperType(NpgsqlCommand command, string parameterName, object? value)
        {
            if (value == null || value is DBNull)
            {
                command.Parameters.AddWithValue(parameterName, DBNull.Value);
                return;
            }

            // Guid型は明示的にUuid型を指定
            if (value is Guid guidValue)
            {
                command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Uuid) { Value = guidValue });
            }
            else if (value.GetType() == typeof(Guid?))
            {
                var nullableGuid = (Guid?)value;
                if (nullableGuid.HasValue)
                {
                    command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Uuid) { Value = nullableGuid.Value });
                }
                else
                {
                    command.Parameters.AddWithValue(parameterName, DBNull.Value);
                }
            }
            // DateTime型は明示的にTimestamp型を指定
            else if (value is DateTime dateTimeValue)
            {
                command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Timestamp) { Value = dateTimeValue });
            }
            else if (value.GetType() == typeof(DateTime?))
            {
                var nullableDateTime = (DateTime?)value;
                if (nullableDateTime.HasValue)
                {
                    command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Timestamp) { Value = nullableDateTime.Value });
                }
                else
                {
                    command.Parameters.AddWithValue(parameterName, DBNull.Value);
                }
            }
            // DateTimeOffset型は明示的にTimestampTz型を指定
            else if (value is DateTimeOffset dateTimeOffsetValue)
            {
                command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.TimestampTz) { Value = dateTimeOffsetValue });
            }
            else if (value.GetType() == typeof(DateTimeOffset?))
            {
                var nullableDateTimeOffset = (DateTimeOffset?)value;
                if (nullableDateTimeOffset.HasValue)
                {
                    command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.TimestampTz) { Value = nullableDateTimeOffset.Value });
                }
                else
                {
                    command.Parameters.AddWithValue(parameterName, DBNull.Value);
                }
            }
            // byte[]型は明示的にBytea型を指定
            else if (value is byte[] byteArrayValue)
            {
                command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Bytea) { Value = byteArrayValue });
            }
            // TimeSpan型は明示的にInterval型を指定
            else if (value is TimeSpan timeSpanValue)
            {
                command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Interval) { Value = timeSpanValue });
            }
            else if (value.GetType() == typeof(TimeSpan?))
            {
                var nullableTimeSpan = (TimeSpan?)value;
                if (nullableTimeSpan.HasValue)
                {
                    command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Interval) { Value = nullableTimeSpan.Value });
                }
                else
                {
                    command.Parameters.AddWithValue(parameterName, DBNull.Value);
                }
            }
            // bool型は明示的にBoolean型を指定
            else if (value is bool boolValue)
            {
                command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Boolean) { Value = boolValue });
            }
            else if (value.GetType() == typeof(bool?))
            {
                var nullableBool = (bool?)value;
                if (nullableBool.HasValue)
                {
                    command.Parameters.Add(new NpgsqlParameter(parameterName, NpgsqlDbType.Boolean) { Value = nullableBool.Value });
                }
                else
                {
                    command.Parameters.AddWithValue(parameterName, DBNull.Value);
                }
            }
            else
            {
                command.Parameters.AddWithValue(parameterName, value);
            }
        }

        /// <summary>
        /// Add parameters to NpgsqlCommand with proper type handling
        /// </summary>
        private static void AddParameters(NpgsqlCommand command, object parameter)
        {
            // Dictionary<string, object>の場合
            if (parameter is IDictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    AddParameterWithProperType(command, kvp.Key, kvp.Value);
                }
            }
            else
            {
                // 通常のオブジェクトの場合
                foreach (var property in parameter.GetType().GetProperties())
                {
                    var value = property.GetValue(parameter);
                    AddParameterWithProperType(command, property.Name, value);
                }
            }
        }

        private static string ConvertToCamelCase(string pascalCaseStr)
        {
            pascalCaseStr = pascalCaseStr[..1].ToLower() + pascalCaseStr[1..];
            return pascalCaseStr;
        }

        private static string ConvertToSnakeCase(string pascalCaseStr)
        {
            var head = pascalCaseStr[..1].ToLower();
            var body = pascalCaseStr[1..];
            const string alphabet = "ABCDEFGHIZKLMNOPQRSTUVWXYZ";
            foreach (var c in alphabet)
            {
                if (pascalCaseStr.Contains(c))
                    body = body.Replace(c.ToString(), $"_{char.ToLower(c)}");
            }
            return $"{head}{body}";
        }

        private static string ConvertToKebabCase(string pascalCaseStr)
        {
            string head = pascalCaseStr[..1].ToLower();
            string body = pascalCaseStr[1..];
            const string alphabet = "ABCDEFGHIZKLMNOPQRSTUVWXYZ";
            foreach (char c in alphabet)
            {
                if (pascalCaseStr.Contains(c))
                    body = body.Replace(c.ToString(), $"-{char.ToLower(c)}");
            }
            return $"{head}{body}";
        }

        private static void SetValue<T>(PropertyInfo property, ref T instance, object value)
        {
            if (property.GetCustomAttribute<IgnoreAttribute>() != null)
                return;
            else if (property.GetCustomAttribute<ForeignKeyAttribute>() != null)
            {
                var foreignKey = property.GetCustomAttribute<ForeignKeyAttribute>()!;
                if (foreignKey.Type != null && foreignKey.Type != property.PropertyType)
                    property.SetValue(instance, SetForeignKeyData(property, value));
                else
                {
                    property.SetValue(instance, value);
                }
            }
            else if (property.GetCustomAttribute<MaskAttribute>() != null)
            {
                var mask = property.GetCustomAttribute<MaskAttribute>()!;
                if (mask.Func != null)
                    property.SetValue(instance, mask.Func(value.ToString()!));
                else
                {
                    StringBuilder stringBuilder = new();
                    for (int i = 0; i < value.ToString()!.Length; i++)
                    {
                        stringBuilder.Append(mask.Replacement);
                    }
                    property.SetValue(instance, stringBuilder.ToString());
                }
            }
            else
            {
                if (value is DBNull)
                {
                    property.SetValue(instance, null);
                }
                // JSONB型の場合はJSONデシリアライズ
                else if (property.GetCustomAttribute<JsonbAttribute>() != null)
                {
                    var jsonString = value.ToString()!;
                    var deserializedValue = JsonSerializer.Deserialize(jsonString, property.PropertyType);
                    property.SetValue(instance, deserializedValue);
                }
                // JSON型の場合はJSONデシリアライズ
                else if (property.GetCustomAttribute<JsonAttribute>() != null)
                {
                    var jsonString = value.ToString()!;
                    var deserializedValue = JsonSerializer.Deserialize(jsonString, property.PropertyType);
                    property.SetValue(instance, deserializedValue);
                }
                // Enumの場合は文字列からEnumに変換（PostgreSQL ENUM型対応）
                else if (property.PropertyType.IsEnum)
                {
                    if (value is string stringValue)
                    {
                        property.SetValue(instance, Enum.Parse(property.PropertyType, stringValue, ignoreCase: true));
                    }
                    else if (value is int intValue)
                    {
                        property.SetValue(instance, Enum.ToObject(property.PropertyType, intValue));
                    }
                    else
                    {
                        property.SetValue(instance, Enum.Parse(property.PropertyType, value.ToString()!, ignoreCase: true));
                    }
                }
                // List<string>の場合は配列からListに変換
                else if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elementType = property.PropertyType.GetGenericArguments()[0];
                    if (elementType == typeof(string) && value is string[] stringArray)
                    {
                        property.SetValue(instance, new List<string>(stringArray));
                    }
                    else if (value is Array array)
                    {
                        var listType = typeof(List<>).MakeGenericType(elementType);
                        var list = Activator.CreateInstance(listType);
                        var addMethod = listType.GetMethod("Add");
                        foreach (var item in array)
                        {
                            addMethod?.Invoke(list, new[] { item });
                        }
                        property.SetValue(instance, list);
                    }
                    else
                    {
                        property.SetValue(instance, value);
                    }
                }
                // Guid型とstring型の相互変換
                else if (property.PropertyType == typeof(string) && value is Guid guidValue)
                {
                    property.SetValue(instance, guidValue.ToString());
                }
                else if (property.PropertyType == typeof(Guid) && value is string stringGuid)
                {
                    property.SetValue(instance, Guid.Parse(stringGuid));
                }
                // Guid?型とstring型の相互変換
                else if (property.PropertyType == typeof(string) && value is Guid?)
                {
                    property.SetValue(instance, value?.ToString());
                }
                else if (property.PropertyType == typeof(Guid?) && value is string stringNullableGuid)
                {
                    property.SetValue(instance, Guid.Parse(stringNullableGuid));
                }
                else
                {
                    property.SetValue(instance, value);
                }
            }
        }
    }
}
