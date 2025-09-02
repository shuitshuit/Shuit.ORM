# ShuitNet.ORM

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
  - [基本的なCRUD操作](#基本的なcrud操作)
  - [カスタムクエリ](#カスタムクエリ)
- [属性](#属性)
  - [KeyAttribute](#keyattribute)
  - [NameAttribute](#nameattribute)
  - [IgnoreAttribute](#ignoreattribute)
  - [SerialAttribute](#serialattribute)
  - [ForeignKeyAttribute](#foreignkeyattribute)
  - [MaskAttribute](#maskattribute)
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
- **命名規則の自動変換**: CamelCase, SnakeCase, KebabCase, PascalCaseに対応
- **外部キー対応**: ForeignKey属性によるリレーション処理
- **マスキング機能**: データの自動マスキング処理
- **複数データベース対応**: PostgreSQLとMySQLをサポート

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

1.2.0