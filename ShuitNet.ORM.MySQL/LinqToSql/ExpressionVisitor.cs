using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using ShuitNet.ORM.Attribute;

namespace ShuitNet.ORM.MySQL.LinqToSql
{
    public class ExpressionVisitor
    {
        private readonly StringBuilder _sql;
        private readonly Dictionary<string, object> _parameters;
        private int _parameterIndex;

        public ExpressionVisitor()
        {
            _sql = new StringBuilder();
            _parameters = new Dictionary<string, object>();
            _parameterIndex = 0;
        }

        public string Sql => _sql.ToString();
        public Dictionary<string, object> Parameters => _parameters;

        public void Visit(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Lambda:
                    VisitLambda((LambdaExpression)expression);
                    break;
                case ExpressionType.MemberAccess:
                    VisitMemberAccess((MemberExpression)expression);
                    break;
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                    VisitBinary((BinaryExpression)expression);
                    break;
                case ExpressionType.AndAlso:
                case ExpressionType.OrElse:
                    VisitLogical((BinaryExpression)expression);
                    break;
                case ExpressionType.Constant:
                    VisitConstant((ConstantExpression)expression);
                    break;
                case ExpressionType.Parameter:
                    VisitParameter((ParameterExpression)expression);
                    break;
                case ExpressionType.Call:
                    VisitMethodCall((MethodCallExpression)expression);
                    break;
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    VisitConvert((UnaryExpression)expression);
                    break;
                default:
                    throw new NotSupportedException($"Expression type {expression.NodeType} is not supported");
            }
        }

        private void VisitLambda(LambdaExpression lambda)
        {
            Visit(lambda.Body);
        }

        private void VisitMemberAccess(MemberExpression memberExpression)
        {
            // メンバーアクセスの対象がラムダパラメータの場合、カラムとして扱う
            if (memberExpression.Expression is ParameterExpression)
            {
                var columnName = GetColumnName(memberExpression.Member);
                _sql.Append($"`{columnName}`");
            }
            else
            {
                // それ以外の場合は外部変数として評価し、値を取得
                var value = GetMemberValue(memberExpression);
                var paramName = $"@p{_parameterIndex++}";
                _parameters.Add(paramName, value!);
                _sql.Append(paramName);
            }
        }

        private static object? GetMemberValue(MemberExpression memberExpression)
        {
            // メンバーアクセスの式を評価して値を取得
            var objectMember = Expression.Convert(memberExpression, typeof(object));
            var getterLambda = Expression.Lambda<Func<object>>(objectMember);
            var getter = getterLambda.Compile();
            return getter();
        }

        private static object? GetExpressionValue(Expression expression)
        {
            // 定数式の場合は直接値を返す
            if (expression is ConstantExpression constExpr)
                return constExpr.Value;

            // メンバーアクセスでラムダパラメータでない場合は評価
            if (expression is MemberExpression memberExpr && memberExpr.Expression is not ParameterExpression)
                return GetMemberValue(memberExpr);

            // その他の式は評価する
            var objectMember = Expression.Convert(expression, typeof(object));
            var getterLambda = Expression.Lambda<Func<object>>(objectMember);
            var getter = getterLambda.Compile();
            return getter();
        }

        private void VisitBinary(BinaryExpression binary)
        {
            Visit(binary.Left);
            _sql.Append(" ");
            _sql.Append(GetSqlOperator(binary.NodeType));
            _sql.Append(" ");
            Visit(binary.Right);
        }

        private void VisitLogical(BinaryExpression logical)
        {
            _sql.Append("(");
            Visit(logical.Left);
            _sql.Append(logical.NodeType == ExpressionType.AndAlso ? " AND " : " OR ");
            Visit(logical.Right);
            _sql.Append(")");
        }

        private void VisitConstant(ConstantExpression constant)
        {
            var paramName = $"@p{_parameterIndex++}";
            _parameters.Add(paramName, constant.Value!);
            _sql.Append(paramName);
        }

        private void VisitParameter(ParameterExpression parameter)
        {
            // パラメータ名は通常使用されない（メンバーアクセス時に処理される）
        }

        private void VisitConvert(UnaryExpression unary)
        {
            // 型変換は無視して内部の式を処理
            Visit(unary.Operand);
        }

        private void VisitMethodCall(MethodCallExpression methodCall)
        {
            if (methodCall.Method.Name == "Contains" && methodCall.Method.DeclaringType == typeof(string))
            {
                Visit(methodCall.Object!);
                _sql.Append(" LIKE ");

                // Contains の引数を取得して評価
                var argument = methodCall.Arguments[0];
                var value = GetExpressionValue(argument);
                var paramName = $"@p{_parameterIndex++}";
                _parameters.Add(paramName, $"%{value}%");
                _sql.Append(paramName);
            }
            else if (methodCall.Method.Name == "StartsWith" && methodCall.Method.DeclaringType == typeof(string))
            {
                Visit(methodCall.Object!);
                _sql.Append(" LIKE ");

                var argument = methodCall.Arguments[0];
                var value = GetExpressionValue(argument);
                var paramName = $"@p{_parameterIndex++}";
                _parameters.Add(paramName, $"{value}%");
                _sql.Append(paramName);
            }
            else if (methodCall.Method.Name == "EndsWith" && methodCall.Method.DeclaringType == typeof(string))
            {
                Visit(methodCall.Object!);
                _sql.Append(" LIKE ");

                var argument = methodCall.Arguments[0];
                var value = GetExpressionValue(argument);
                var paramName = $"@p{_parameterIndex++}";
                _parameters.Add(paramName, $"%{value}");
                _sql.Append(paramName);
            }
            else
            {
                throw new NotSupportedException($"Method {methodCall.Method.Name} is not supported");
            }
        }

        private static string GetSqlOperator(ExpressionType expressionType)
        {
            return expressionType switch
            {
                ExpressionType.Equal => "=",
                ExpressionType.NotEqual => "!=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThan => "<",
                ExpressionType.LessThanOrEqual => "<=",
                _ => throw new NotSupportedException($"Operator {expressionType} is not supported")
            };
        }

        private static string GetColumnName(MemberInfo member)
        {
            var nameAttribute = member.GetCustomAttribute<NameAttribute>();
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