
using System.Text;

namespace SmtpTelegramRelay.Extensions
{
    internal static class CommonExtensions
    {
        public static StringBuilder AppendNotEmpty(this StringBuilder sb, string? data, Func<string, string> func)
        {
            if (!string.IsNullOrEmpty(data))
                sb.Append(func(data));
            return sb;
        }

        public static StringBuilder AppendIf(this StringBuilder sb, bool doAppend, string? data, Func<string, string> func)
        {
            if (doAppend)
                sb = sb.AppendNotEmpty(data, func);
            return sb;
        }

        public static T? IsNotNullOrEmpty<T>(this string? data, Func<string, T?> func) where T : class
            => string.IsNullOrEmpty(data)
                ? null 
                : func(data);

        public static TOut? TryCatch<TIn, TOut>(this TIn data, Func<TIn, TOut?> doTry, Action<TIn> doCatch)
            where TOut : class
        {
            try
            {
                return doTry(data);
            }
            catch (Exception)
            {
                doCatch(data);
            }

            return null;
        }

        public static IEnumerable<T> Enumerate<T>(this T obj)
            => Enumerable.Repeat(obj, 1);
    }
}
