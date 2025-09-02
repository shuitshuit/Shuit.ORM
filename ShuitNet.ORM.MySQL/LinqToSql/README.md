# MySQL LinqToSql

MySQL LinqToSqlは、ShuitNet.ORMのMySQLデータベース用LinqToSql実装です。PostgreSQL版の実装をベースにしており、MySQLの構文と特性に合わせて最適化されています。

## 機能

- **基本的なLINQクエリ**: Where, Select, OrderBy, Skip, Take
- **非同期操作**: ToListAsync, FirstAsync, FirstOrDefaultAsync, CountAsync, AnyAsync
- **JOIN操作**: Inner Join, Left Join, Right Join, Full Join (UNIONで実装)
- **GROUP BY操作**: GroupBy と集約関数 (Count, Sum, Average, Min, Max)
- **文字列操作**: Contains, StartsWith, EndsWith (LIKE句に変換)

## 使用方法

```csharp
using ShuitNet.ORM.MySQL;
using ShuitNet.ORM.MySQL.LinqToSql;

// データベース接続
var connection = new MySQLConnect("Server=localhost;Database=test;Uid=user;Pwd=password");

// クエリ可能オブジェクトの作成
var queryable = connection.AsQueryable<User>();

// 基本的なクエリ
var users = await queryable
    .Where(u => u.Age > 18)
    .OrderBy(u => u.Name)
    .ToListAsync();

// JOIN操作
var userOrders = await connection.AsQueryable<User>()
    .Join(connection.AsQueryable<Order>(),
          u => u.Id,
          o => o.UserId,
          (u, o) => new { UserName = u.Name, OrderDate = o.Date })
    .ToListAsync();

// GROUP BY操作
var orderStats = await connection.AsQueryable<Order>()
    .GroupBy(o => o.UserId)
    .Select(g => new { 
        UserId = g.Key, 
        OrderCount = g.Count(), 
        TotalAmount = g.Sum(o => o.Amount) 
    })
    .ToListAsync();
```

## PostgreSQLとの違い

1. **識別子の引用符**: PostgreSQLの `"column"` に対してMySQLは `\`column\`` を使用
2. **LIMIT構文**: 
   - PostgreSQL: `LIMIT count OFFSET offset`
   - MySQL: `LIMIT offset, count`
3. **FULL OUTER JOIN**: MySQLではサポートされていないため、LEFT JOIN UNION RIGHT JOINで実装

## ファイル構成

- `MySqlQueryProvider.cs`: クエリプロバイダーの実装
- `MySqlQueryable.cs`: クエリ可能オブジェクトの実装
- `ExpressionVisitor.cs`: 式の解析とSQL変換
- `QueryableExtensions.cs`: LINQクエリの拡張メソッド
- `JoinQueryable.cs`: JOIN操作の実装
- `GroupByQueryable.cs`: GROUP BY操作の実装

## 制限事項

- 複雑な式や関数はサポートされていません
- サブクエリはサポートされていません
- トランザクション内でのクエリ実行は、MySQLConnect側で制御する必要があります