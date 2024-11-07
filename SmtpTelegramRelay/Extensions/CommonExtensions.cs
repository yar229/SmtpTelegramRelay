using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmtpTelegramRelay.Extensions
{
    internal static class CommonExtensions
    {
        public static IEnumerable<T> Enumerate<T>(this T obj)
            => Enumerable.Repeat(obj, 1);
    }
}
