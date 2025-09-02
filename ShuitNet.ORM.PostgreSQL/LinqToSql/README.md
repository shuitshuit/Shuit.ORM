# ShuitNet.ORM PostgreSQL LINQ to SQL

このライブラリは、ShuitNet.ORMライブラリにLINQ to SQL機能を追加します。LINQ式を使用してPostgreSQLクエリを記述し、自動的にSQLに変換して実行することができます。

## 機能

### 基本機能
- **Where**: 条件による絞り込み
- **Select**: フィールドの選択と射影
- **OrderBy/OrderByDescending/ThenBy/ThenByDescending**: ソート
- **Take/Skip**: ページング
- **Count/Any**: 集約関数
- **First/FirstOrDefault**: 最初の要素取得

### 高度な機能
- **Join/LeftJoin/RightJoin/FullJoin**: テーブル結合
- **GroupBy**: グループ化
- **Sum/Average/Min/Max**: 集約関数
- **文字列検索**: Contains/StartsWith/EndsWith

## 使用方法

### 基本的なクエリ

```csharp
using ShuitNet.ORM.PostgreSQL.LinqToSql;

// 接続の作成
var connection = new PostgreSqlConnect("Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass");
await connection.OpenAsync();

// 基本的なSelect
var users = await connection.AsQueryable<User>()
    .ToListAsync();

// Where条件
var youngUsers = await connection.AsQueryable<User>()
    .Where(u => u.Age < 30)
    .ToListAsync();

// 複数の条件
var specificUsers = await connection.AsQueryable<User>()
    .Where(u => u.Age > 25)
    .Where(u => u.Email.Contains("@company.com"))
    .ToListAsync();
```

### ソートとページング

```csharp
// ソート
var sortedUsers = await connection.AsQueryable<User>()
    .OrderBy(u => u.Name)
    .ThenByDescending(u => u.Age)
    .ToListAsync();

// ページング
var pagedUsers = await connection.AsQueryable<User>()
    .OrderBy(u => u.Id)
    .Skip(10)
    .Take(5)
    .ToListAsync();
```

### 射影（Select）

```csharp
// 匿名型への射影
var userSummary = await connection.AsQueryable<User>()
    .Select(u => new { u.Name, u.Email })
    .ToListAsync();
```

### 集約関数

```csharp
// カウント
var count = await connection.AsQueryable<User>()
    .Where(u => u.Age > 18)
    .CountAsync();

// 存在チェック
var hasYoungUsers = await connection.AsQueryable<User>()
    .AnyAsync(u => u.Age < 25);
```

### テーブル結合

```csharp
// Inner Join
var userDepartments = await connection.AsQueryable<User>()
    .Join(connection.AsQueryable<Department>(),
          user => user.DepartmentId,
          dept => dept.Id,
          (user, dept) => new { 
              UserName = user.Name, 
              DepartmentName = dept.Name 
          })
    .ToListAsync();

// Left Join
var usersWithDepts = await connection.AsQueryable<User>()
    .LeftJoin(connection.AsQueryable<Department>(),
             user => user.DepartmentId,
             dept => dept.Id,
             (user, dept) => new {
                 UserName = user.Name,
                 DepartmentName = dept?.Name ?? "No Department"
             })
    .ToListAsync();
```

### グループ化と集約

```csharp
// GroupByと集約関数
var stats = await connection.AsQueryable<User>()
    .GroupBy(u => u.DepartmentId)
    .Select(g => new {
        DepartmentId = g.Key,
        UserCount = g.Count(),
        AverageAge = g.Average(u => u.Age),
        MinAge = g.Min(u => u.Age),
        MaxAge = g.Max(u => u.Age)
    })
    .ToListAsync();
```

### 文字列検索

```csharp
// 文字列検索
var searchResults = await connection.AsQueryable<User>()
    .Where(u => u.Name.StartsWith("John"))
    .Where(u => u.Email.EndsWith(".com"))
    .Where(u => u.Name.Contains("Smith"))
    .ToListAsync();
```

## データクラスの定義

```csharp
using ShuitNet.ORM.Attribute;

public class User
{
    [Key]
    public int Id { get; set; }
    
    public string Name { get; set; } = "";
    
    [Name("email_address")] // カスタムカラム名
    public string Email { get; set; } = "";
    
    public int Age { get; set; }
    
    public int DepartmentId { get; set; }
}

public class Department
{
    [Key]
    public int Id { get; set; }
    
    public string Name { get; set; } = "";
    
    public string Location { get; set; } = "";
}
```

## 対応する演算子

### 比較演算子
- `==` → `=`
- `!=` → `!=`
- `>` → `>`
- `>=` → `>=`
- `<` → `<`
- `<=` → `<=`

### 論理演算子
- `&&` → `AND`
- `||` → `OR`

### 文字列メソッド
- `Contains(string)` → `LIKE '%value%'`
- `StartsWith(string)` → `LIKE 'value%'`
- `EndsWith(string)` → `LIKE '%value'`

## 注意事項

1. **非同期操作**: すべてのクエリ実行は非同期メソッド（`ToListAsync()`, `FirstAsync()`など）を使用してください。

2. **パラメータ化クエリ**: すべての値は自動的にパラメータ化され、SQLインジェクション攻撃から保護されます。

3. **ネーミング規則**: `PostgreSqlConnect.NamingCase`を設定することで、プロパティ名からカラム名への変換ルールを変更できます。

4. **カスタム属性のサポート**: `[Name]`, `[Key]`, `[Ignore]`などの既存の属性がサポートされます。

## 生成されるSQLの確認

開発時には、生成されるSQLを確認することができます：

```csharp
var provider = new PostgreSqlQueryProvider(connection);
var queryable = new PostgreSqlQueryable<User>(connection);
var expression = queryable.Where(u => u.Age > 25).Expression;
var sql = provider.BuildSql<User>(expression);

Console.WriteLine("Generated SQL: " + sql.Query);
foreach (var param in sql.Parameters)
{
    Console.WriteLine($"Parameter {param.Key}: {param.Value}");
}
```

これにより、LINQクエリが正確にSQLに変換されているかを確認できます。