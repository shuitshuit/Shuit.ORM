using ShuitNet.ORM.Attribute;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace ShuitNet.ORM
{
    public class TableClass
    {
        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static T FromJson<T>(string json)
        {
            Type type = typeof(T);
            var obj = Activator.CreateInstance(type);
            var props = type.GetProperties();
            var dic = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? throw new Exception("json deserialize error");
            foreach (var prop in props)
            {
                var name = prop.GetCustomAttribute<NameAttribute>()?.Name;
                var nullable = prop.GetCustomAttribute<AllowNullAttribute>();
                if (name != null && dic.TryGetValue(name, out object? value)) // NameAttributeがある場合はそちらを優先
                    prop.SetValue(obj, value);
                else if (dic.TryGetValue(prop.Name, out value)) // NameAttributeがない場合はプロパティ名をキーにする
                {
                    prop.SetValue(obj, value);
                }
                else if (nullable != null) // null許容の場合は nullを設定
                {
                    prop.SetValue(obj, null);
                }
                else // null許容でない場合は例外を投げる
                {
                    throw new ArgumentException($"property {prop.Name} not found");
                }
            }
            if (obj != null)
                return (T)obj;
            else
                throw new InvalidOperationException($"{type.Name} does not possess a constructor with no arguments.");
        }

        [Obsolete]
        public string ToJson()
        {
            Type type = GetType();
            var props = type.GetProperties();
            var dic = new Dictionary<dynamic, dynamic?>();
            foreach (var prop in props)
            {
                var name = prop.GetCustomAttribute<NameAttribute>()?.Name;
                var value = prop.GetValue(this)?.ToString();
                var key = name ?? prop.Name;
                var propertyType = prop.PropertyType;
                foreach (var property in propertyType.GetProperties())
                {
                    if (property.GetCustomAttribute<KeyAttribute>() != null)
                    {
                        dic.Add(key, property.GetValue(prop.GetValue(this)));
                        break;
                    }
                }
                dic.TryAdd(key, value);
            }
            return JsonSerializer.Serialize(dic, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
    }
}
