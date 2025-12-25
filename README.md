# ShuitNet.ORM

**Core:** [![NuGet version](https://badge.fury.io/nu/ShuitNet.ORM.svg)](https://badge.fury.io/nu/ShuitNet.ORM)  
**PostgreSQL:** [![NuGet version](https://badge.fury.io/nu/ShuitNet.ORM.PostgreSQL.svg)](https://badge.fury.io/nu/ShuitNet.ORM.PostgreSQL)  
**MySQL:** [![NuGet version](https://badge.fury.io/nu/ShuitNet.ORM.MySQL.svg)](https://badge.fury.io/nu/ShuitNet.ORM.MySQL)

シンプルで軽量なORMライブラリです。PostgreSQLとMySQLに対応しています。

## 目次

- [概要](#概要)
- [特徴](#特徴)
- [インストール](#インストール)
  - [PostgreSQL版](#postgresql版)
  - [MySQL版](#mysql版)
- [使用方法](#使用方法)
  - [基本的な設定](#基本的な設定)
  - [データクラスの定義](#データクラスの定義)
    - [サポートされている型](#サポートされている型)
    - [Guid型とstring型の相互変換](#guid型とstring型の相互変換)
  - [基本的なCRUD操作](#基本的なcrud操作)
  - [カスタムクエリ](#カスタムクエリ)
  - [LINQ to SQL](#linq-to-sql)
- [属性](#属性)
  - [KeyAttribute](#keyattribute)
  - [NameAttribute](#nameattribute)
  - [IgnoreAttribute](#ignoreattribute)
  - [SerialAttribute](#serialattribute)
  - [ForeignKeyAttribute](#foreignkeyattribute)
  - [MaskAttribute](#maskattribute)
  - [JsonAttribute](#jsonattribute)
  - [JsonbAttribute](#jsonbattribute)
- [命名規則](#命名規則)
- [トランザクション](#トランザクション)
- [ライセンス](#ライセンス)
- [作者](#作者)
- [バージョン](#バージョン)

## 概要

ShuitNet.ORMは、.NET環境でPostgreSQLとMySQLデータベースを簡単に操作するためのObject-Relational Mapping (ORM) ライブラリです。属性ベースの設定により、データクラスとデータベーステーブル間のマッピングを行います。

## 特徴

- **シンプルなAPI**: 直感的で使いやすいメソッド
- **属性ベース設定**: データクラスに属性を付与してマッピング設定
- **非同期対応**: async/awaitパターンをサポート
- **LINQ to SQL**: LINQ式を使用したタイプセーフなクエリ（PostgreSQL・MySQL対応）
- **命名規則の自動変換**: CamelCase, SnakeCase, KebabCase, PascalCaseに対応
- **外部キー対応**: ForeignKey属性によるリレーション処理
- **マスキング機能**: データの自動マスキング処理
- **複数データベース対応**: PostgreSQLとMySQLをサポート
- **豊富な型サポート**: Guid, DateTime, DateTimeOffset, byte[], TimeSpan, bool, decimal, JSON/JSONBなどの型を明示的にサポート
- **型の自動変換**: Guid ⟷ string の相互変換、複雑な型のJSON自動変換をサポート

## インストール

NuGetパッケージマネージャーまたはPackage Manager Consoleを使用してインストールできます。

### PostgreSQL版
```bash
dotnet add package ShuitNet.ORM.PostgreSQL
```

### MySQL版
```bash
dotnet add package ShuitNet.ORM.MySQL
```

## 使用方法

### 基本的な設定

#### PostgreSQL
```csharp
using ShuitNet.ORM.PostgreSQL;

var connection = new PostgreSqlConnect("Host=localhost;Port=5432;Database=testdb;Username=user;Password=password");
await connection.OpenAsync();
```

#### MySQL
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

    // JSON/JSONB型の使用例
    [Json]
    public List<string> Roles { get; set; }

    [Json]
    public Dictionary<string, string> Preferences { get; set; }
}
```

#### サポートされている型

ShuitNet.ORMは以下の型を明示的にサポートしています：

**PostgreSQL:**
- `Guid`, `Guid?` → UUID型
- `DateTime`, `DateTime?` → TIMESTAMP型
- `DateTimeOffset`, `DateTimeOffset?` → TIMESTAMPTZ型
- `byte[]` → BYTEA型
- `TimeSpan`, `TimeSpan?` → INTERVAL型
- `bool`, `bool?` → BOOLEAN型
- 複雑な型（`[Json]`属性）→ JSONB型
- 複雑な型（`[Jsonb]`属性）→ JSONB型
- `List<T>`、カスタムクラス等 → JSON/JSONB型（属性指定時）
- その他の基本型（int, long, string, decimal等）

**MySQL:**
- `Guid`, `Guid?` → GUID型（CHAR(36) または BINARY(16)）
- `DateTime`, `DateTime?` → DATETIME型
- `DateTimeOffset`, `DateTimeOffset?` → DATETIME型（UTC変換）
- `byte[]` → BLOB型
- `TimeSpan`, `TimeSpan?` → TIME型
- `bool`, `bool?` → BIT型
- `decimal`, `decimal?` → DECIMAL型
- 複雑な型（`[Json]`属性）→ JSON型
- `List<T>`、カスタムクラス等 → JSON型（属性指定時）
- その他の基本型（int, long, string等）

#### Guid型とstring型の相互変換

PostgreSQLのUUID型カラムは、C#では`Guid`型または`string`型のどちらでも使用できます：

```csharp
public class Product
{
    [Key]
    public Guid ProductId { get; set; }  // PostgreSQL: uuid型
    public string Name { get; set; }
}

// または

public class Product
{
    [Key]
    public string ProductId { get; set; }  // PostgreSQL: uuid型（自動変換）
    public string Name { get; set; }
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

// 匿名型での条件指定
var user = await connection.GetAsync<User>(new { Name = "John Doe" });

// 複数レコードの取得（匿名型での条件指定）
var users = await connection.GetMultipleAsync<User>(new { Department = "IT", IsActive = true });

// 全件取得
var users = await connection.GetAllAsync<User>();

// 条件付き取得
var activeUsers = await connection.GetAllAsync<User>(u => u.IsActive);
```

#### データの更新
```csharp
user.Email = "newemail@example.com";
await connection.UpdateAsync(user);
```

#### データの削除
```csharp
// プライマリキーで削除
await connection.DeleteAsync<User>(1);

// インスタンスで削除
await connection.DeleteAsync(user);
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
using ShuitNet.ORM.PostgreSQL.LinqToSql; // PostgreSQLの場合
// または
using ShuitNet.ORM.MySQL.LinqToSql; // MySQLの場合

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

## 属性

### KeyAttribute
プライマリキーを指定します。

```csharp
[Key]
public int Id { get; set; }
```

### NameAttribute
テーブル名やカラム名をカスタマイズします。スキーマ表記（`スキーマ名.テーブル名`）にも対応しています。

```csharp
[Name("user_table")]
public class User { }

[Name("public.user_table")]
public class User { }

[Name("user_name")]
public string Name { get; set; }
```

### IgnoreAttribute
データベース操作から除外するプロパティを指定します。

```csharp
[Ignore]
public string TemporaryData { get; set; }
```

### SerialAttribute
自動増分カラム（SERIAL/AUTO_INCREMENT）を指定します。

```csharp
[Serial]
public int Id { get; set; }
```

### ForeignKeyAttribute
外部キーの関係を定義します。

```csharp
[ForeignKey(typeof(int))]
public User User { get; set; }
```

### MaskAttribute
データの自動マスキングを行います。

```csharp
[Mask('*')]
public string Password { get; set; }
```

### JsonAttribute
プロパティをJSON型としてシリアライズ/デシリアライズします。複雑なオブジェクトやリストをデータベースに保存する際に使用します。

- **PostgreSQL**: JSONB型にマッピング
- **MySQL**: JSON型にマッピング

```csharp
[Json]
public List<string> Tags { get; set; }

[Json]
public Dictionary<string, object> Metadata { get; set; }

[Json]
public UserSettings Settings { get; set; }
```

### JsonbAttribute
PostgreSQL専用の属性で、プロパティをJSONB型としてシリアライズ/デシリアライズします。

```csharp
[Jsonb]
public List<Address> Addresses { get; set; }

[Jsonb]
public CustomObject Data { get; set; }
```

## 命名規則

デフォルトでは`CamelCase`が使用されますが、以下のように変更できます：

```csharp
// PostgreSQL
PostgreSqlConnect.NamingCase = NamingCase.SnakeCase;

// MySQL
MySQLConnect.NamingCase = NamingCase.SnakeCase;
```

利用可能な命名規則：
- `CamelCase`: firstName
- `SnakeCase`: first_name
- `KebabCase`: first-name
- `PascalCase`: FirstName

## トランザクション

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

## ライセンス

このプロジェクトはMITライセンスの下で公開されています。詳細については、[LICENSE.txt](LICENSE.txt)ファイルを参照してください。

## 作者

shuit (shuit.net)

## バージョン

1.3.4