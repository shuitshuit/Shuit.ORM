using Npgsql;
using ShuitNet.ORM.Attribute;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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

        public ValueTask<NpgsqlTransaction> BeginTransaction() => _con.BeginTransactionAsync();

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
            await using NpgsqlCommand command = new(sql, _con);
            command.Parameters.AddWithValue("key", key);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Sequence contains no elements.");

            var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
            for (var i = 0; i < reader.FieldCount; i++)
            {
                // カラム名からプロパティ名を取得
                var property = typeof(T).GetProperty(GetPropertyName<T>(reader.GetName(i)))
                    ?? throw new InvalidOperationException("Property not found.");
                var value = reader.GetValue(i);
                SetValue(property, ref instance, value);
            }
            return instance;
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
            await using NpgsqlCommand command = new(sql, _con);
            foreach (var property in key.GetType().GetProperties())
            {
                command.Parameters.AddWithValue(property.Name, property.GetValue(key)!);
            }

            await using var reader = await command.ExecuteReaderAsync();
            T[] values = [];
            while (reader.Read())
            {
                var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    // カラム名からプロパティ名を取得
                    var property = typeof(T).GetProperty(GetPropertyName<T>(reader.GetName(i)))
                        ?? throw new InvalidOperationException("Property not found.");
                    var value = reader.GetValue(i);
                    SetValue(property, ref instance, value);
                }
                values = values.Append(instance).ToArray();
            }
            return values;
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
            await using NpgsqlCommand command = new(sql, _con);
            foreach (var property in parameter.GetType().GetProperties())
            {
                command.Parameters.AddWithValue(property.Name, property.GetValue(parameter)!);
            }

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Sequence contains no elements.");
            {
                var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    // カラム名からプロパティ名を取得
                    var property = typeof(T).GetProperty(GetPropertyName<T>(reader.GetName(i)))
                        ?? throw new InvalidOperationException("Property not found.");
                    var value = reader.GetValue(i);
                    SetValue(property, ref instance, value);
                }
                return instance;
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
            await using NpgsqlCommand command = new(sql, _con);
            await using var reader = await command.ExecuteReaderAsync();
            List<T> list = [];
            while (await reader.ReadAsync())
            {
                var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    // カラム名からプロパティ名を取得
                    var property = type.GetProperty(GetPropertyName<T>(reader.GetName(i)))
                        ?? throw new InvalidOperationException("Property not found.");
                    var value = reader.GetValue(i);
                    SetValue(property, ref instance, value);
                }
                if (func(instance))
                    list.Add(instance);
            }
            return list.Any() ? list : throw new InvalidOperationException("Sequence contains no elements.");
        }

        public IEnumerable<T> GetAll<T>(Func<T, bool> func) => GetAllAsync(func).Result;
        public async Task<IEnumerable<T>> GetAllAsync<T>()
        {
            var type = typeof(T);
            var tableName = GetTableName<T>();
            var sql = $"SELECT * FROM {tableName}";
            await using NpgsqlCommand command = new(sql, _con);
            await using var reader = await command.ExecuteReaderAsync();
            List<T> list = [];
            while (await reader.ReadAsync())
            {
                var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    // カラム名からプロパティ名を取得
                    var property = type.GetProperty(GetPropertyName<T>(reader.GetName(i)))
                        ?? throw new InvalidOperationException("Property not found.");
                    var value = reader.GetValue(i);
                    SetValue(property, ref instance, value);
                }
                list.Add(instance);
            }
            return list;
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
            await using NpgsqlCommand command = new(sql, _con);
            command.Parameters.AddWithValue("key", key);
            return await command.ExecuteNonQueryAsync();
        }

        public int Delete<T>(object key) => DeleteAsync<T>(key).Result;

        public async Task<int> DeleteAsync<T>(T instance)
        {
            ArgumentNullException.ThrowIfNull(instance, nameof(instance));

            var tableName = GetTableName<T>();
            var keyColumnName = GetKeyColumnName<T>();
            var sql = $"DELETE FROM {tableName} WHERE \"{keyColumnName}\" = @key";
            await using NpgsqlCommand command = new(sql, _con);
            var key = typeof(T).GetProperty(GetPropertyName<T>(keyColumnName))?.GetValue(instance)
                ?? throw new ArgumentException("Key is null.");
            command.Parameters.AddWithValue("key", key);
            return await command.ExecuteNonQueryAsync();
        }

        public int Delete<T>(T instance) => DeleteAsync(instance).Result;

        public async Task<int> UpdateAsync<T>(T instance)
        {
            ArgumentNullException.ThrowIfNull(instance, nameof(instance));

            var tableName = GetTableName<T>();
            var keyColumnName = GetKeyColumnName<T>();
            var sql = $"UPDATE {tableName} SET ";
            sql = typeof(T).GetProperties()
                .Where(property => property.GetCustomAttribute<IgnoreAttribute>() == null && property.GetCustomAttribute<SerialAttribute>() == null)
                .Aggregate(sql, (current, property) => current + $"\"{GetColumnName<T>(property)}\" = @{property.Name},");
            sql = sql[..^1] + $" WHERE \"{keyColumnName}\" = @{keyColumnName}";

            await using NpgsqlCommand command = new(sql, _con);
            foreach (var property in typeof(T).GetProperties())
            {
                if (property.GetCustomAttribute<IgnoreAttribute>() != null ||
                    property.GetCustomAttribute<SerialAttribute>() != null)
                {
                    continue;
                }
                var foreignKey = property.GetCustomAttribute<ForeignKeyAttribute>();
                if (foreignKey is { Type: not null } && foreignKey.Type != property.PropertyType)
                {
                    var propertyValue = property.GetValue(instance);
                    if (propertyValue == null) continue;
                    // 外部キーの場合は値を変換する
                    command.Parameters.AddWithValue(property.Name, GetForeignKeyData(propertyValue, foreignKey.Type));
                }
                else
                {
                    command.Parameters.AddWithValue(property.Name, property.GetValue(instance)!);
                }
            }
            command.Parameters.AddWithValue(keyColumnName, typeof(T).GetProperty(GetPropertyName<T>(keyColumnName))?.GetValue(instance)!);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<int> ExecuteAsync(string sql, object? parameter = null)
        {
            try
            {
                await using NpgsqlCommand command = new(sql, _con);
                if (parameter == null)
                    return await command.ExecuteNonQueryAsync();
                foreach (var property in parameter.GetType().GetProperties())
                {
                    command.Parameters.AddWithValue(property.Name, property.GetValue(parameter)!);
                }
                return await command.ExecuteNonQueryAsync();
            }
            catch (NullReferenceException)
            {
                throw new ArgumentException("Invalid parameter.");
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
            await using NpgsqlCommand command = new(sql, _con);
            foreach (var property in typeof(T).GetProperties())
            {
                if (property.GetCustomAttribute<IgnoreAttribute>() != null ||
                    property.GetCustomAttribute<SerialAttribute>() != null)
                    continue;
                var foreignKey = property.GetCustomAttribute<ForeignKeyAttribute>();
                if (foreignKey is { Type: not null } && foreignKey.Type != property.PropertyType)
                {
                    var propertyValue = property.GetValue(instance);
                    if (propertyValue == null) continue;
                    // 外部キーの場合は値を変換する
                    command.Parameters.AddWithValue(property.Name, GetForeignKeyData(propertyValue, foreignKey.Type));
                }
                else
                {
                    command.Parameters.AddWithValue(property.Name, property.GetValue(instance)!);
                }
            }
            return await command.ExecuteNonQueryAsync();
        }

        public int Insert<T>(T instance) => InsertAsync(instance).Result;

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameter = null)
        {
            await using NpgsqlCommand command = new(sql, _con);
            if (parameter != null)
            {
                foreach (var property in parameter.GetType().GetProperties())
                {
                    command.Parameters.AddWithValue(property.Name, property.GetValue(parameter)!);
                }
            }

            await using var reader = await command.ExecuteReaderAsync();
            List<T> list = [];
            while (await reader.ReadAsync())
            {
                var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    // カラム名からプロパティ名を取得
                    var property = typeof(T).GetProperty(GetPropertyName<T>(reader.GetName(i)))
                        ?? throw new InvalidOperationException("Property not found.");
                    var value = reader.GetValue(i);
                    SetValue(property, ref instance, value);
                }
                list.Add(instance);
            }
            return list;
        }

        public IEnumerable<T> Query<T>(string sql, object? parameter = null) => QueryAsync<T>(sql, parameter).Result;

        public async Task<T> QueryFirstAsync<T>(string sql, object? parameter = null)
        {
            await using NpgsqlCommand command = new(sql, _con);
            if (parameter != null)
            {
                foreach (var property in parameter.GetType().GetProperties())
                {
                    command.Parameters.AddWithValue(property.Name, property.GetValue(parameter)!);
                }
            }

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Sequence contains no elements.");
            {
                var instance = Activator.CreateInstance<T>(); // デフォルトコンストラクタを呼び出す
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    // カラム名からプロパティ名を取得
                    var property = typeof(T).GetProperty(GetPropertyName<T>(reader.GetName(i)))
                        ?? throw new InvalidOperationException("Property not found.");
                    var value = reader.GetValue(i);
                    SetValue(property, ref instance, value);
                }
                return instance;
            }
        }

        public T QueryFirst<T>(string sql, object? parameter = null) => QueryFirstAsync<T>(sql, parameter).Result;

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
            throw new ArgumentException("Property not found.");
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
                    property.SetValue(instance, null);
                else
                    property.SetValue(instance, value);
            }
        }
    }
}
