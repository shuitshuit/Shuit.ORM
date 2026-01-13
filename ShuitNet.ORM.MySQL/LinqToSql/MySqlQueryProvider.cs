using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace ShuitNet.ORM.MySQL.LinqToSql
{
    public class MySqlQueryProvider : IQueryProvider
    {
        private readonly MySQLConnect _connection;

        public MySqlQueryProvider(MySQLConnect connection)
        {
            _connection = connection;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            var elementType = expression.Type.GetGenericArguments()[0];
            var queryableType = typeof(MySqlQueryable<>).MakeGenericType(elementType);
            return (IQueryable)Activator.CreateInstance(queryableType, _connection, expression)!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new MySqlQueryable<TElement>(_connection, expression);
        }

        public object Execute(Expression expression)
        {
            return Execute<object>(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            // Count()メソッドの場合は特別な処理
            if (expression is MethodCallExpression methodCall && methodCall.Method.Name == "Count")
            {
                var elementType = methodCall.Method.GetGenericArguments().Length > 0
                    ? methodCall.Method.GetGenericArguments()[0]
                    : methodCall.Arguments[0].Type.GetGenericArguments()[0];

                var tableName = (string)typeof(MySQLConnect)
                    .GetMethod("GetTableName")!
                    .MakeGenericMethod(elementType)
                    .Invoke(null, null)!;

                var whereClause = (SqlResult)GetType()
                    .GetMethod("BuildWhereClause")!
                    .MakeGenericMethod(elementType)
                    .Invoke(this, new object[] { methodCall.Arguments[0] })!;

                var countQuery = $"SELECT COUNT(*) FROM {tableName}";
                if (!string.IsNullOrEmpty(whereClause.WhereClause))
                {
                    countQuery += $" WHERE {whereClause.WhereClause}";
                }

                var count = _connection.ExecuteScalar<long>(countQuery, whereClause.Parameters);
                return (TResult)(object)(int)count;
            }

            var sql = BuildSql<TResult>(expression);
            var results2 = _connection.Query<TResult>(sql.Query, sql.Parameters);
            return results2.First();
        }

        public SqlResult BuildSql<T>(Expression expression)
        {
            // Expressionツリーから元のエンティティ型を取得
            var sourceType = ExtractSourceElementType(expression);
            var tableName = (string)typeof(MySQLConnect)
                .GetMethod("GetTableName")!
                .MakeGenericMethod(sourceType)
                .Invoke(null, null)!;

            var selectClause = BuildSelectClause<T>(expression);
            var query = $"SELECT {selectClause} FROM {tableName}";
            var parameters = new Dictionary<string, object>();

            var whereClause = BuildWhereClause<T>(expression);
            if (!string.IsNullOrEmpty(whereClause.WhereClause))
            {
                query += $" WHERE {whereClause.WhereClause}";
                parameters = whereClause.Parameters;
            }

            var orderByClause = BuildOrderByClause<T>(expression);
            if (!string.IsNullOrEmpty(orderByClause))
            {
                query += $" ORDER BY {orderByClause}";
            }

            var limitClause = BuildLimitClause<T>(expression);
            if (!string.IsNullOrEmpty(limitClause))
            {
                query += $" {limitClause}";
            }

            return new SqlResult { Query = query, Parameters = parameters };
        }

        private Type ExtractSourceElementType(Expression expression)
        {
            if (expression is MethodCallExpression methodCall)
            {
                // 再帰的に最初のソースまで遡る
                return ExtractSourceElementType(methodCall.Arguments[0]);
            }
            else if (expression is ConstantExpression constantExpression)
            {
                // Queryableのソース型を取得
                var queryableType = constantExpression.Type;
                if (queryableType.IsGenericType)
                {
                    return queryableType.GetGenericArguments()[0];
                }
            }

            // フォールバック: 型パラメータを返す
            return typeof(object);
        }

        private string BuildSelectClause<T>(Expression expression)
        {
            var selectExpression = ExtractSelectExpression(expression);
            if (selectExpression != null)
            {
                return BuildSelectColumns(selectExpression);
            }
            return "*";
        }

        private LambdaExpression? ExtractSelectExpression(Expression expression)
        {
            if (expression is MethodCallExpression methodCall)
            {
                if (methodCall.Method.Name == "Select")
                {
                    return (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                }
                
                // 再帰的に内側の式をチェック
                return ExtractSelectExpression(methodCall.Arguments[0]);
            }
            
            return null;
        }

        private string BuildSelectColumns(LambdaExpression selectExpression)
        {
            if (selectExpression.Body is NewExpression newExpression)
            {
                var columns = new List<string>();
                for (int i = 0; i < newExpression.Arguments.Count; i++)
                {
                    if (newExpression.Arguments[i] is MemberExpression memberExpression)
                    {
                        var columnName = GetColumnName(memberExpression.Member);
                        var alias = newExpression.Members?[i]?.Name ?? columnName;
                        columns.Add($"`{columnName}` AS `{alias}`");
                    }
                }
                return string.Join(", ", columns);
            }
            else if (selectExpression.Body is MemberExpression memberExpr)
            {
                var columnName = GetColumnName(memberExpr.Member);
                return $"`{columnName}`";
            }
            
            return "*";
        }

        private string BuildLimitClause<T>(Expression expression)
        {
            if (expression is MethodCallExpression methodCall)
            {
                if (methodCall.Method.Name == "Take")
                {
                    if (methodCall.Arguments[1] is ConstantExpression countExpression)
                    {
                        return $"LIMIT {countExpression.Value}";
                    }
                }
                else if (methodCall.Method.Name == "Skip")
                {
                    if (methodCall.Arguments[1] is ConstantExpression offsetExpression)
                    {
                        var innerLimit = BuildLimitClause<T>(methodCall.Arguments[0]);
                        return $"LIMIT {offsetExpression.Value}, {ExtractLimitValue(innerLimit)}";
                    }
                }
                
                // 再帰的に内側の式をチェック
                return BuildLimitClause<T>(methodCall.Arguments[0]);
            }
            
            return "";
        }

        private string ExtractLimitValue(string limitClause)
        {
            if (string.IsNullOrEmpty(limitClause))
                return "18446744073709551615"; // MySQL's max value for LIMIT
            
            var parts = limitClause.Split(' ');
            if (parts.Length >= 2 && parts[0] == "LIMIT")
            {
                var values = parts[1].Split(',');
                return values.Length > 1 ? values[1].Trim() : parts[1];
            }
            
            return "18446744073709551615";
        }

        public SqlResult BuildWhereClause<T>(Expression expression)
        {
            var visitor = new ExpressionVisitor();
            var whereClause = "";
            var parameters = new Dictionary<string, object>();

            var whereExpressions = ExtractWhereExpressions(expression);
            
            foreach (var whereExpr in whereExpressions)
            {
                var tempVisitor = new ExpressionVisitor();
                tempVisitor.Visit(whereExpr);
                
                if (!string.IsNullOrEmpty(whereClause))
                    whereClause += " AND ";
                    
                whereClause += tempVisitor.Sql;
                foreach (var param in tempVisitor.Parameters)
                {
                    parameters[param.Key] = param.Value;
                }
            }

            return new SqlResult { WhereClause = whereClause, Parameters = parameters };
        }

        private List<LambdaExpression> ExtractWhereExpressions(Expression expression)
        {
            var whereExpressions = new List<LambdaExpression>();
            
            if (expression is MethodCallExpression methodCall)
            {
                // 再帰的に内側の式をチェック
                whereExpressions.AddRange(ExtractWhereExpressions(methodCall.Arguments[0]));
                
                if (methodCall.Method.Name == "Where")
                {
                    var predicate = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                    whereExpressions.Add(predicate);
                }
            }
            
            return whereExpressions;
        }

        public string BuildOrderByClause<T>(Expression expression)
        {
            if (expression is MethodCallExpression methodCall)
            {
                if (methodCall.Method.Name == "OrderBy" || methodCall.Method.Name == "ThenBy")
                {
                    var keySelector = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                    if (keySelector.Body is MemberExpression memberExpression)
                    {
                        var columnName = GetColumnName(memberExpression.Member);
                        return $"`{columnName}` ASC";
                    }
                }
                else if (methodCall.Method.Name == "OrderByDescending" || methodCall.Method.Name == "ThenByDescending")
                {
                    var keySelector = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                    if (keySelector.Body is MemberExpression memberExpression)
                    {
                        var columnName = GetColumnName(memberExpression.Member);
                        return $"`{columnName}` DESC";
                    }
                }

                // 再帰的に前の式をチェック
                var innerOrderBy = BuildOrderByClause<T>(methodCall.Arguments[0]);
                if (!string.IsNullOrEmpty(innerOrderBy))
                    return innerOrderBy;
            }

            return "";
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

    public class SqlResult
    {
        public string Query { get; set; } = "";
        public string WhereClause { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}