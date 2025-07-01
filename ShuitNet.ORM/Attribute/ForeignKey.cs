using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShuitNet.ORM.Attribute
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ForeignKeyAttribute : System.Attribute
    {
        public Type? Type { get; set; }

        public ForeignKeyAttribute(Type type)
        {
            Type = type;
        }

        public ForeignKeyAttribute()
        {
            Type = null;
        }
    }
}
