using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace ShuitNet.ORM.MySQL.LinqToSql
{
    public class JoinQueryable<TOuter, TInner, TKey, TResult> : IQueryable<TResult>
    {
        private readonly MySQLConnect _connection;
        private readonly IQueryable<TOuter> _outer;
        private readonly IQueryable<TInner> _inner;
        private readonly Expression<Func<TOuter, TKey>> _outerKeySelector;
        private readonly Expression<Func<TInner, TKey>> _innerKeySelector;
        private readonly Expression<Func<TOuter, TInner, TResult>> _resultSelector;
        private readonly JoinType _joinType;

        public JoinQueryable(
            MySQLConnect connection,
            IQueryable<TOuter> outer,
            IQueryable<TInner> inner,
            Expression<Func<TOuter, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<TOuter, TInner, TResult>> resultSelector,
            JoinType joinType = JoinType.Inner)
        {
            _connection = connection;
            _outer = outer;
            _inner = inner;
            _outerKeySelector = outerKeySelector;
            _innerKeySelector = innerKeySelector;
            _resultSelector = resultSelector;
            _joinType = joinType;
        }

        public Type ElementType => typeof(TResult);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => new MySqlQueryProvider(_connection);

        public IEnumerator<TResult> GetEnumerator()
        {
            var results = ExecuteJoin().Result;
            return results.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public async Task<List<TResult>> ToListAsync()
        {
            return await ExecuteJoin();
        }

        private async Task<List<TResult>> ExecuteJoin()
        {
            var sql = BuildJoinSql();
            var results = await _connection.QueryAsync<TResult>(sql.Query, sql.Parameters);
            return results.ToList();
        }

        private SqlResult BuildJoinSql()
        {
            var outerTable = MySQLConnect.GetTableName<TOuter>();
            var innerTable = MySQLConnect.GetTableName<TInner>();
            
            var outerAlias = "o";
            var innerAlias = "i";

            var selectClause = BuildJoinSelectClause(outerAlias, innerAlias);
            var joinTypeString = _joinType switch
            {
                JoinType.Inner => "INNER JOIN",
                JoinType.Left => "LEFT JOIN", 
                JoinType.Right => "RIGHT JOIN",
                JoinType.Full => "LEFT JOIN UNION RIGHT JOIN", // MySQL doesn't support FULL OUTER JOIN directly
                _ => "INNER JOIN"
            };

            var outerKeyColumn = GetColumnName(_outerKeySelector);
            var innerKeyColumn = GetColumnName(_innerKeySelector);

            string query;
            
            // Handle MySQL's lack of FULL OUTER JOIN
            if (_joinType == JoinType.Full)
            {
                query = $@"
                    SELECT {selectClause}
                    FROM {outerTable} {outerAlias}
                    LEFT JOIN {innerTable} {innerAlias} 
                    ON {outerAlias}.`{outerKeyColumn}` = {innerAlias}.`{innerKeyColumn}`
                    UNION
                    SELECT {selectClause}
                    FROM {outerTable} {outerAlias}
                    RIGHT JOIN {innerTable} {innerAlias} 
                    ON {outerAlias}.`{outerKeyColumn}` = {innerAlias}.`{innerKeyColumn}`";
            }
            else
            {
                query = $@"
                    SELECT {selectClause}
                    FROM {outerTable} {outerAlias}
                    {joinTypeString} {innerTable} {innerAlias} 
                    ON {outerAlias}.`{outerKeyColumn}` = {innerAlias}.`{innerKeyColumn}`";
            }

            return new SqlResult 
            { 
                Query = query.Trim(), 
                Parameters = new Dictionary<string, object>() 
            };
        }

        private string BuildJoinSelectClause(string outerAlias, string innerAlias)
        {
            if (_resultSelector.Body is NewExpression newExpression)
            {
                var columns = new List<string>();
                for (int i = 0; i < newExpression.Arguments.Count; i++)
                {
                    var argument = newExpression.Arguments[i];
                    var memberName = newExpression.Members?[i]?.Name ?? $"Column{i}";

                    if (argument is MemberExpression memberExpression)
                    {
                        var columnName = GetColumnName(memberExpression);
                        var tableAlias = IsOuterProperty(memberExpression) ? outerAlias : innerAlias;
                        columns.Add($"{tableAlias}.`{columnName}` AS `{memberName}`");
                    }
                }
                return string.Join(", ", columns);
            }

            // 単純な選択の場合は両方のテーブルの全カラムを返す
            var outerProperties = typeof(TOuter).GetProperties()
                .Where(p => p.GetCustomAttribute<ShuitNet.ORM.Attribute.IgnoreAttribute>() == null)
                .Select(p => $"{outerAlias}.`{GetColumnName(p)}` AS `{p.Name}`");
                
            var innerProperties = typeof(TInner).GetProperties()
                .Where(p => p.GetCustomAttribute<ShuitNet.ORM.Attribute.IgnoreAttribute>() == null)
                .Select(p => $"{innerAlias}.`{GetColumnName(p)}` AS `Inner_{p.Name}`");

            return string.Join(", ", outerProperties.Concat(innerProperties));
        }

        private bool IsOuterProperty(MemberExpression memberExpression)
        {
            // パラメータの型を確認して、OuterかInnerかを判定
            if (memberExpression.Expression is ParameterExpression paramExpr)
            {
                return paramExpr.Type == typeof(TOuter);
            }
            return false;
        }

        private string GetColumnName(Expression<Func<TOuter, TKey>> expression)
        {
            if (expression.Body is MemberExpression memberExpression)
            {
                return GetColumnName(memberExpression.Member);
            }
            throw new ArgumentException("Expression must be a member access");
        }

        private string GetColumnName(Expression<Func<TInner, TKey>> expression)
        {
            if (expression.Body is MemberExpression memberExpression)
            {
                return GetColumnName(memberExpression.Member);
            }
            throw new ArgumentException("Expression must be a member access");
        }

        private string GetColumnName(MemberExpression memberExpression)
        {
            return GetColumnName(memberExpression.Member);
        }

        private string GetColumnName(PropertyInfo property)
        {
            return GetColumnName((MemberInfo)property);
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

    public enum JoinType
    {
        Inner,
        Left, 
        Right,
        Full
    }
}