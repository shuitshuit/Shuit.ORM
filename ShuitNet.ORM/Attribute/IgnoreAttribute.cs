using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShuitNet.ORM.Attribute
{
    /// <summary>
    /// Exclusion.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class IgnoreAttribute : System.Attribute
    {
    }
}
