# ShuitNet.ORM (Core)

[![NuGet version](https://badge.fury.io/nu/ShuitNet.ORM.svg)](https://badge.fury.io/nu/ShuitNet.ORM)

軽量でシンプルなORMライブラリのコアパッケージです。

## 概要

ShuitNet.ORMは、.NET環境でデータベースを簡単に操作するためのObject-Relational Mapping (ORM) ライブラリです。このパッケージにはコア機能と属性定義が含まれています。

## 特徴

- **属性ベース設定**: データクラスに属性を付与してマッピング設定
- **命名規則の自動変換**: CamelCase, SnakeCase, KebabCase, PascalCaseに対応
- **外部キー対応**: ForeignKey属性によるリレーション処理
- **マスキング機能**: データの自動マスキング処理

## インストール

```bash
dotnet add package ShuitNet.ORM
```

## 使用方法

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

## 属性

### KeyAttribute
プライマリキーを指定します。

### NameAttribute
テーブル名やカラム名をカスタマイズします。

### IgnoreAttribute
データベース操作から除外するプロパティを指定します。

### SerialAttribute
自動増分カラムを指定します。

### ForeignKeyAttribute
外部キーの関係を定義します。

### MaskAttribute
データの自動マスキングを行います。

## データベース固有の実装

実際のデータベース操作には、以下の実装パッケージが必要です：

- **PostgreSQL**: [ShuitNet.ORM.PostgreSQL](https://www.nuget.org/packages/ShuitNet.ORM.PostgreSQL/)
- **MySQL**: [ShuitNet.ORM.MySQL](https://www.nuget.org/packages/ShuitNet.ORM.MySQL/)

## ライセンス

このプロジェクトはMITライセンスの下で公開されています。詳細については、[LICENSE.txt](LICENSE.txt)ファイルを参照してください。

## 作者

shuit (shuit.net)