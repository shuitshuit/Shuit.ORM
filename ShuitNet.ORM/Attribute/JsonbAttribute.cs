using System;

namespace ShuitNet.ORM.Attribute
{
    /// <summary>
    /// Indicates that the property should be serialized/deserialized as JSONB type in PostgreSQL.
    /// The property type will be automatically converted to/from JSON.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class JsonbAttribute : System.Attribute
    {
    }
}
