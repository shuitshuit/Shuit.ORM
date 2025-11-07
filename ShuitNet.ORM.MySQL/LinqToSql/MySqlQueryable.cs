using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ShuitNet.ORM.MySQL.LinqToSql
{
    public class MySqlQueryable<T> : IOrderedQueryable<T>
    {
        private readonly MySQLConnect _connection;
        private readonly Expression _expression;
        private readonly MySqlQueryProvider _provider;

        public MySqlQueryable(MySQLConnect connection)
        {
            _connection = connection;
            _provider = new MySqlQueryProvider(connection);
            _expression = Expression.Constant(this);
        }

        public MySqlQueryable(MySQLConnect connection, Expression expression)
        {
            _connection = connection;
            _provider = new MySqlQueryProvider(connection);
            _expression = expression;
        }

        public MySQLConnect Connection => _connection;
        public Type ElementType => typeof(T);
        public Expression Expression => _expression;
        public IQueryProvider Provider => _provider;

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)_provider.Execute(_expression)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public async Task<List<T>> ToListAsync()
        {
            var sql = _provider.BuildSql<T>(_expression);
            var results = await _connection.QueryAsync<T>(sql.Query, sql.Parameters);
            return results.ToList();
        }

        public async Task<T> FirstAsync()
        {
            var sql = _provider.BuildSql<T>(_expression);
            sql.Query += " LIMIT 1";
            return await _connection.QueryFirstAsync<T>(sql.Query, sql.Parameters);
        }

        public async Task<T?> FirstOrDefaultAsync()
        {
            var sql = _provider.BuildSql<T>(_expression);
            sql.Query += " LIMIT 1";
            var results = await _connection.QueryAsync<T>(sql.Query, sql.Parameters);
            return results.FirstOrDefault();
        }

        public async Task<int> CountAsync()
        {
            var tableName = MySQLConnect.GetTableName<T>();
            var whereClause = _provider.BuildWhereClause<T>(_expression);

            var countQuery = $"SELECT COUNT(*) FROM {tableName}";
            if (!string.IsNullOrEmpty(whereClause.WhereClause))
            {
                countQuery += $" WHERE {whereClause.WhereClause}";
            }

            var count = await _connection.ExecuteScalarAsync<long>(countQuery, whereClause.Parameters);
            return (int)count;
        }

        public async Task<bool> AnyAsync()
        {
            var count = await CountAsync();
            return count > 0;
        }
    }
}