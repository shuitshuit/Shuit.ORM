using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace ShuitNet.ORM.MySQL.LinqToSql
{
    public static class QueryableExtensions
    {
        // Join operations
        public static JoinQueryable<TOuter, TInner, TKey, TResult> Join<TOuter, TInner, TKey, TResult>(
            this IQueryable<TOuter> outer,
            IQueryable<TInner> inner,
            Expression<Func<TOuter, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<TOuter, TInner, TResult>> resultSelector)
        {
            if (outer is MySqlQueryable<TOuter> outerQueryable)
            {
                return new JoinQueryable<TOuter, TInner, TKey, TResult>(
                    outerQueryable.Connection,
                    outer,
                    inner,
                    outerKeySelector,
                    innerKeySelector,
                    resultSelector,
                    JoinType.Inner);
            }
            throw new NotSupportedException("Join is only supported for MySqlQueryable");
        }

        public static JoinQueryable<TOuter, TInner, TKey, TResult> LeftJoin<TOuter, TInner, TKey, TResult>(
            this IQueryable<TOuter> outer,
            IQueryable<TInner> inner,
            Expression<Func<TOuter, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<TOuter, TInner, TResult>> resultSelector)
        {
            if (outer is MySqlQueryable<TOuter> outerQueryable)
            {
                return new JoinQueryable<TOuter, TInner, TKey, TResult>(
                    outerQueryable.Connection,
                    outer,
                    inner,
                    outerKeySelector,
                    innerKeySelector,
                    resultSelector,
                    JoinType.Left);
            }
            throw new NotSupportedException("LeftJoin is only supported for MySqlQueryable");
        }

        public static JoinQueryable<TOuter, TInner, TKey, TResult> RightJoin<TOuter, TInner, TKey, TResult>(
            this IQueryable<TOuter> outer,
            IQueryable<TInner> inner,
            Expression<Func<TOuter, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<TOuter, TInner, TResult>> resultSelector)
        {
            if (outer is MySqlQueryable<TOuter> outerQueryable)
            {
                return new JoinQueryable<TOuter, TInner, TKey, TResult>(
                    outerQueryable.Connection,
                    outer,
                    inner,
                    outerKeySelector,
                    innerKeySelector,
                    resultSelector,
                    JoinType.Right);
            }
            throw new NotSupportedException("RightJoin is only supported for MySqlQueryable");
        }

        public static JoinQueryable<TOuter, TInner, TKey, TResult> FullJoin<TOuter, TInner, TKey, TResult>(
            this IQueryable<TOuter> outer,
            IQueryable<TInner> inner,
            Expression<Func<TOuter, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<TOuter, TInner, TResult>> resultSelector)
        {
            if (outer is MySqlQueryable<TOuter> outerQueryable)
            {
                return new JoinQueryable<TOuter, TInner, TKey, TResult>(
                    outerQueryable.Connection,
                    outer,
                    inner,
                    outerKeySelector,
                    innerKeySelector,
                    resultSelector,
                    JoinType.Full);
            }
            throw new NotSupportedException("FullJoin is only supported for MySqlQueryable");
        }

        // GroupBy operations
        public static GroupByQueryable<TSource, TKey> GroupBy<TSource, TKey>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, TKey>> keySelector)
        {
            if (source is MySqlQueryable<TSource> mysqlQueryable)
            {
                return new GroupByQueryable<TSource, TKey>(mysqlQueryable.Connection, source, keySelector);
            }
            throw new NotSupportedException("GroupBy is only supported for MySqlQueryable");
        }

        public static AggregateQueryable<TSource, TKey, TResult> Select<TSource, TKey, TResult>(
            this GroupByQueryable<TSource, TKey> source,
            Expression<Func<IGrouping<TKey, TSource>, TResult>> resultSelector)
        {
            // GroupByQueryableからConnectionを取得する方法を実装
            var connectionField = typeof(GroupByQueryable<TSource, TKey>)
                .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance);
            var connection = (MySQLConnect)connectionField!.GetValue(source)!;
            
            return new AggregateQueryable<TSource, TKey, TResult>(connection, source, resultSelector);
        }

        public static IQueryable<T> AsQueryable<T>(this MySQLConnect connection)
        {
            return new MySqlQueryable<T>(connection);
        }

        public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> queryable)
        {
            if (queryable is MySqlQueryable<T> mysqlQueryable)
            {
                return await mysqlQueryable.ToListAsync();
            }
            throw new NotSupportedException("ToListAsync is only supported for MySqlQueryable");
        }

        public static async Task<T> FirstAsync<T>(this IQueryable<T> queryable)
        {
            if (queryable is MySqlQueryable<T> mysqlQueryable)
            {
                return await mysqlQueryable.FirstAsync();
            }
            throw new NotSupportedException("FirstAsync is only supported for MySqlQueryable");
        }

        public static async Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> queryable)
        {
            if (queryable is MySqlQueryable<T> mysqlQueryable)
            {
                return await mysqlQueryable.FirstOrDefaultAsync();
            }
            throw new NotSupportedException("FirstOrDefaultAsync is only supported for MySqlQueryable");
        }

        public static async Task<int> CountAsync<T>(this IQueryable<T> queryable)
        {
            if (queryable is MySqlQueryable<T> mysqlQueryable)
            {
                return await mysqlQueryable.CountAsync();
            }
            throw new NotSupportedException("CountAsync is only supported for MySqlQueryable");
        }

        public static async Task<bool> AnyAsync<T>(this IQueryable<T> queryable)
        {
            if (queryable is MySqlQueryable<T> mysqlQueryable)
            {
                return await mysqlQueryable.AnyAsync();
            }
            throw new NotSupportedException("AnyAsync is only supported for MySqlQueryable");
        }

        public static async Task<bool> AnyAsync<T>(this IQueryable<T> queryable, Expression<Func<T, bool>> predicate)
        {
            if (queryable is MySqlQueryable<T> mysqlQueryable)
            {
                return await mysqlQueryable.Where(predicate).AnyAsync();
            }
            throw new NotSupportedException("AnyAsync is only supported for MySqlQueryable");
        }
    }
}