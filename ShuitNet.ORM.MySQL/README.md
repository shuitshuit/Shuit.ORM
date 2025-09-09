# ShuitNet.ORM.MySQL

[![NuGet version](https://badge.fury.io/nu/ShuitNet.ORM.MySQL.svg)](https://badge.fury.io/nu/ShuitNet.ORM.MySQL)

MySQL用のShuitNet.ORMライブラリです。

## 概要

ShuitNet.ORM.MySQLは、MySQLデータベースに対応したObject-Relational Mapping (ORM) ライブラリです。シンプルなAPIでMySQLデータベースの操作を簡単に行えます。

## 特徴

- **シンプルなAPI**: 直感的で使いやすいメソッド
- **非同期対応**: async/awaitパターンをサポート
- **属性ベース設定**: データクラスに属性を付与してマッピング設定
- **LINQ to SQL**: LINQ式を使用したタイプセーフなクエリ
- **命名規則の自動変換**: CamelCase, SnakeCase, KebabCase, PascalCaseに対応
- **外部キー対応**: ForeignKey属性によるリレーション処理
- **トランザクション対応**: 安全なデータ操作

## インストール

```bash
dotnet add package ShuitNet.ORM.MySQL
```

## 使用方法

### 基本的な設定

```csharp
using ShuitNet.ORM.MySQL;

var connection = new MySQLConnect("Server=localhost;Database=testdb;Uid=user;Pwd=password;");
await connection.OpenAsync();
```

### データクラスの定義

```csharp
using ShuitNet.ORM.Attribute;

public class User
{
    [Key]
    public int Id { get; set; }
    
    public string Name { get; set; }
    
    public string Email { get; set; }
    
    [Ignore]
    public string TemporaryData { get; set; }
    
    [Serial]
    public DateTime CreatedAt { get; set; }
}
```

### 基本的なCRUD操作

#### データの挿入
```csharp
var user = new User { Name = "John Doe", Email = "john@example.com" };
await connection.InsertAsync(user);
```

#### データの取得
```csharp
// プライマリキーで取得
var user = await connection.GetAsync<User>(1);

// 条件指定で取得
var user = await connection.GetAsync<User>(new { Name = "John Doe" });

// 複数レコードの取得
var users = await connection.GetMultipleAsync<User>(new { Department = "IT" });

// 全件取得
var users = await connection.GetAllAsync<User>();
```

#### データの更新
```csharp
user.Email = "newemail@example.com";
await connection.UpdateAsync(user);
```

#### データの削除
```csharp
await connection.DeleteAsync<User>(1);
```

### カスタムクエリ

```csharp
// クエリの実行
var users = await connection.QueryAsync<User>("SELECT * FROM users WHERE age > @age", new { age = 18 });

// 単一レコードの取得
var user = await connection.QueryFirstAsync<User>("SELECT * FROM users WHERE email = @email", new { email = "john@example.com" });

// 非クエリの実行
await connection.ExecuteAsync("UPDATE users SET last_login = NOW() WHERE id = @id", new { id = 1 });
```

### LINQ to SQL

LINQ式を使用してタイプセーフなクエリを記述できます：

```csharp
using ShuitNet.ORM.MySQL.LinqToSql;

// 基本的なクエリ
var users = await connection.AsQueryable<User>()
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
var stats = await connection.AsQueryable<User>()
    .GroupBy(u => u.DepartmentId)
    .Select(g => new {
        DepartmentId = g.Key,
        UserCount = g.Count(),
        AverageAge = g.Average(u => u.Age)
    })
    .ToListAsync();

// 文字列検索
var searchResults = await connection.AsQueryable<User>()
    .Where(u => u.Name.Contains("John"))
    .Where(u => u.Email.EndsWith(".com"))
    .ToListAsync();
```

### 命名規則

デフォルトでは`CamelCase`が使用されますが、以下のように変更できます：

```csharp
MySQLConnect.NamingCase = NamingCase.SnakeCase;
```

### トランザクション

```csharp
var transaction = await connection.BeginTransaction();
try
{
    await connection.InsertAsync(user1);
    await connection.InsertAsync(user2);
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

## 必要なパッケージ

このパッケージは以下のパッケージに依存しています：

- ShuitNet.ORM (コアライブラリ)
- MySqlConnector (MySQLデータベース接続)

## ライセンス

このプロジェクトはMITライセンスの下で公開されています。詳細については、[LICENSE.txt](LICENSE.txt)ファイルを参照してください。

## 作者

shuit (shuit.net)