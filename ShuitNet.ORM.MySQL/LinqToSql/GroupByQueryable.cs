using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace ShuitNet.ORM.MySQL.LinqToSql
{
    public class GroupByQueryable<TSource, TKey> : IQueryable<IGrouping<TKey, TSource>>
    {
        private readonly MySQLConnect _connection;
        private readonly IQueryable<TSource> _source;
        private readonly Expression<Func<TSource, TKey>> _keySelector;
        private readonly MySqlQueryProvider _provider;

        public GroupByQueryable(
            MySQLConnect connection,
            IQueryable<TSource> source,
            Expression<Func<TSource, TKey>> keySelector)
        {
            _connection = connection;
            _source = source;
            _keySelector = keySelector;
            _provider = new MySqlQueryProvider(connection);
        }

        public Type ElementType => typeof(IGrouping<TKey, TSource>);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => _provider;

        public IEnumerator<IGrouping<TKey, TSource>> GetEnumerator()
        {
            var results = ExecuteGroupBy().Result;
            return results.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public async Task<List<IGrouping<TKey, TSource>>> ToListAsync()
        {
            return await ExecuteGroupBy();
        }

        private async Task<List<IGrouping<TKey, TSource>>> ExecuteGroupBy()
        {
            var sql = BuildGroupBySql();
            var results = await _connection.QueryAsync<TSource>(sql.Query, sql.Parameters);
            
            // メモリ内でグループ化
            var grouped = results.GroupBy(GetKeyValue).ToList();
            return grouped.Cast<IGrouping<TKey, TSource>>().ToList();
        }

        private Func<TSource, TKey> GetKeyValue
        {
            get
            {
                return _keySelector.Compile();
            }
        }

        private SqlResult BuildGroupBySql()
        {
            var tableName = MySQLConnect.GetTableName<TSource>();
            var groupByColumn = GetGroupByColumn();
            
            var query = $"SELECT * FROM {tableName} ORDER BY `{groupByColumn}`";
            
            return new SqlResult 
            { 
                Query = query, 
                Parameters = new Dictionary<string, object>() 
            };
        }

        private string GetGroupByColumn()
        {
            if (_keySelector.Body is MemberExpression memberExpression)
            {
                return GetColumnName(memberExpression.Member);
            }
            throw new ArgumentException("GroupBy key selector must be a member access");
        }

        private static string GetColumnName(MemberInfo member)
        {
            var nameAttribute = member.GetCustomAttribute<ShuitNet.ORM.Attribute.NameAttribute>();
            if (nameAttribute != null)
                return nameAttribute.Name;

            return MySQLConnect.NamingCase switch
            {
                NamingCase.CamelCase => ConvertToCamelCase(member.Name),
                NamingCase.SnakeCase => ConvertToSnakeCase(member.Name),
                NamingCase.KebabCase => ConvertToKebabCase(member.Name),
                NamingCase.PascalCase => member.Name,
                _ => throw new ArgumentException("Invalid naming case."),
            };
        }

        private static string ConvertToCamelCase(string pascalCaseStr)
        {
            return pascalCaseStr[..1].ToLower() + pascalCaseStr[1..];
        }

        private static string ConvertToSnakeCase(string pascalCaseStr)
        {
            var head = pascalCaseStr[..1].ToLower();
            var body = pascalCaseStr[1..];
            const string alphabet = "ABCDEFGHIZKLMNOPQRSTUVWXYZ";
            foreach (var c in alphabet.Where(c => pascalCaseStr.Contains(c)))
            {
                body = body.Replace(c.ToString(), $"_{char.ToLower(c)}");
            }
            return $"{head}{body}";
        }

        private static string ConvertToKebabCase(string pascalCaseStr)
        {
            var head = pascalCaseStr[..1].ToLower();
            var body = pascalCaseStr[1..];
            const string alphabet = "ABCDEFGHIZKLMNOPQRSTUVWXYZ";
            foreach (var c in alphabet.Where(c => pascalCaseStr.Contains(c)))
            {
                body = body.Replace(c.ToString(), $"-{char.ToLower(c)}");
            }
            return $"{head}{body}";
        }
    }

    public class AggregateQueryable<TSource, TKey, TResult> : IQueryable<TResult>
    {
        private readonly MySQLConnect _connection;
        private readonly GroupByQueryable<TSource, TKey> _groupBy;
        private readonly Expression<Func<IGrouping<TKey, TSource>, TResult>> _resultSelector;
        private readonly MySqlQueryProvider _provider;

        public AggregateQueryable(
            MySQLConnect connection,
            GroupByQueryable<TSource, TKey> groupBy,
            Expression<Func<IGrouping<TKey, TSource>, TResult>> resultSelector)
        {
            _connection = connection;
            _groupBy = groupBy;
            _resultSelector = resultSelector;
            _provider = new MySqlQueryProvider(connection);
        }

        public Type ElementType => typeof(TResult);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => _provider;

        public IEnumerator<TResult> GetEnumerator()
        {
            var results = ExecuteAggregate().Result;
            return results.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public async Task<List<TResult>> ToListAsync()
        {
            return await ExecuteAggregate();
        }

        private async Task<List<TResult>> ExecuteAggregate()
        {
            var sql = BuildAggregateSql();
            var results = await _connection.QueryAsync<TResult>(sql.Query, sql.Parameters);
            return results.ToList();
        }

        private SqlResult BuildAggregateSql()
        {
            var tableName = MySQLConnect.GetTableName<TSource>();
            var groupByColumn = GetGroupByColumn();
            var selectClause = BuildAggregateSelectClause();
            
            var query = $"SELECT {selectClause} FROM {tableName} GROUP BY `{groupByColumn}`";
            
            return new SqlResult 
            { 
                Query = query, 
                Parameters = new Dictionary<string, object>() 
            };
        }

        private string GetGroupByColumn()
        {
            // GroupByQueryableから取得
            var keySelector = GetKeySelector();
            if (keySelector.Body is MemberExpression memberExpression)
            {
                return GetColumnName(memberExpression.Member);
            }
            throw new ArgumentException("GroupBy key selector must be a member access");
        }

        private Expression<Func<TSource, TKey>> GetKeySelector()
        {
            // リフレクションを使ってGroupByQueryableのkeySelectorを取得
            var field = typeof(GroupByQueryable<TSource, TKey>).GetField("_keySelector", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (Expression<Func<TSource, TKey>>)field!.GetValue(_groupBy)!;
        }

        private string BuildAggregateSelectClause()
        {
            if (_resultSelector.Body is NewExpression newExpression)
            {
                var columns = new List<string>();
                var keySelector = GetKeySelector();
                var keyColumn = GetGroupByColumn();
                
                for (int i = 0; i < newExpression.Arguments.Count; i++)
                {
                    var argument = newExpression.Arguments[i];
                    var memberName = newExpression.Members?[i]?.Name ?? $"Column{i}";

                    if (argument is MemberExpression memberExpr && memberExpr.Expression is ParameterExpression)
                    {
                        // Key プロパティの場合
                        columns.Add($"`{keyColumn}` AS `{memberName}`");
                    }
                    else if (argument is MethodCallExpression methodCall)
                    {
                        var aggregateFunction = GetAggregateFunction(methodCall);
                        columns.Add($"{aggregateFunction} AS `{memberName}`");
                    }
                }
                
                return string.Join(", ", columns);
            }

            return "*";
        }

        private string GetAggregateFunction(MethodCallExpression methodCall)
        {
            var method = methodCall.Method;
            
            if (method.Name == "Count")
            {
                return "COUNT(*)";
            }
            else if (method.Name == "Sum" || method.Name == "Average" || method.Name == "Min" || method.Name == "Max")
            {
                if (methodCall.Arguments.Count > 1 && methodCall.Arguments[1] is LambdaExpression lambda)
                {
                    if (lambda.Body is MemberExpression memberExpr)
                    {
                        var columnName = GetColumnName(memberExpr.Member);
                        var sqlFunction = method.Name.ToUpper();
                        if (method.Name == "Average") sqlFunction = "AVG";
                        
                        return $"{sqlFunction}(`{columnName}`)";
                    }
                }
                else
                {
                    // 引数なしの場合（例：Sum()）
                    var sqlFunction = method.Name.ToUpper();
                    if (method.Name == "Average") sqlFunction = "AVG";
                    return $"{sqlFunction}(*)";
                }
            }
            
            throw new NotSupportedException($"Aggregate function {method.Name} is not supported");
        }

        private static string GetColumnName(MemberInfo member)
        {
            var nameAttribute = member.GetCustomAttribute<ShuitNet.ORM.Attribute.NameAttribute>();
            if (nameAttribute != null)
                return nameAttribute.Name;

            return MySQLConnect.NamingCase switch
            {
                NamingCase.CamelCase => ConvertToCamelCase(member.Name),
                NamingCase.SnakeCase => ConvertToSnakeCase(member.Name),
                NamingCase.KebabCase => ConvertToKebabCase(member.Name),
                NamingCase.PascalCase => member.Name,
                _ => throw new ArgumentException("Invalid naming case."),
            };
        }

        private static string ConvertToCamelCase(string pascalCaseStr)
        {
            return pascalCaseStr[..1].ToLower() + pascalCaseStr[1..];
        }

        private static string ConvertToSnakeCase(string pascalCaseStr)
        {
            var head = pascalCaseStr[..1].ToLower();
            var body = pascalCaseStr[1..];
            const string alphabet = "ABCDEFGHIZKLMNOPQRSTUVWXYZ";
            foreach (var c in alphabet.Where(c => pascalCaseStr.Contains(c)))
            {
                body = body.Replace(c.ToString(), $"_{char.ToLower(c)}");
            }
            return $"{head}{body}";
        }

        private static string ConvertToKebabCase(string pascalCaseStr)
        {
            var head = pascalCaseStr[..1].ToLower();
            var body = pascalCaseStr[1..];
            const string alphabet = "ABCDEFGHIZKLMNOPQRSTUVWXYZ";
            foreach (var c in alphabet.Where(c => pascalCaseStr.Contains(c)))
            {
                body = body.Replace(c.ToString(), $"-{char.ToLower(c)}");
            }
            return $"{head}{body}";
        }
    }
}