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
            var columnName = GetColumnName(memberExpression.Member);
            _sql.Append($"`{columnName}`");
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

        private void VisitMethodCall(MethodCallExpression methodCall)
        {
            if (methodCall.Method.Name == "Contains" && methodCall.Method.DeclaringType == typeof(string))
            {
                Visit(methodCall.Object!);
                _sql.Append(" LIKE ");
                
                // Contains の引数を取得
                var argument = methodCall.Arguments[0];
                if (argument is ConstantExpression constExpr)
                {
                    var paramName = $"@p{_parameterIndex++}";
                    _parameters.Add(paramName, $"%{constExpr.Value}%");
                    _sql.Append(paramName);
                }
            }
            else if (methodCall.Method.Name == "StartsWith" && methodCall.Method.DeclaringType == typeof(string))
            {
                Visit(methodCall.Object!);
                _sql.Append(" LIKE ");
                
                var argument = methodCall.Arguments[0];
                if (argument is ConstantExpression constExpr)
                {
                    var paramName = $"@p{_parameterIndex++}";
                    _parameters.Add(paramName, $"{constExpr.Value}%");
                    _sql.Append(paramName);
                }
            }
            else if (methodCall.Method.Name == "EndsWith" && methodCall.Method.DeclaringType == typeof(string))
            {
                Visit(methodCall.Object!);
                _sql.Append(" LIKE ");
                
                var argument = methodCall.Arguments[0];
                if (argument is ConstantExpression constExpr)
                {
                    var paramName = $"@p{_parameterIndex++}";
                    _parameters.Add(paramName, $"%{constExpr.Value}");
                    _sql.Append(paramName);
                }
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