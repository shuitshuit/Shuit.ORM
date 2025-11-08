using System;

namespace ShuitNet.ORM.Attribute
{
    /// <summary>
    /// Indicates that the property should be serialized/deserialized as JSON type.
    /// - PostgreSQL: Maps to JSONB type
    /// - MySQL: Maps to JSON type
    /// The property type will be automatically converted to/from JSON.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class JsonAttribute : System.Attribute
    {
    }
}
