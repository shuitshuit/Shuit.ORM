using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShuitNet.ORM.Attribute
{
    [AttributeUsage(AttributeTargets.Property)]
    public class MaskAttribute : System.Attribute
    {
        public string Replacement { get; set; } = "*";
        public Func<string, string>? Func { get; set; }

        public MaskAttribute()
        {
        }

        public MaskAttribute(string replacement) => Replacement = replacement;
        public MaskAttribute(Func<string, string> func) => Func = func;
        public MaskAttribute(string replacement, Func<string, string> func)
        {
            Replacement = replacement;
            Func = func;
        }
    }
}
