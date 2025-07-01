using System;


namespace ShuitNet.ORM.Attribute
{
    /// <summary>
    /// Denotes a property used as a primary key.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class KeyAttribute : System.Attribute
    {
    }
}
