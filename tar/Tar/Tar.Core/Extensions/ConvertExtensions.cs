using System;
using System.Text;
using Tar.Core.Serialization;

namespace Tar.Core.Extensions
{
    public static class ConvertExtensions
    {
        public static Int32 ToInt32(this Object source)
        {
            return Convert.ToInt32(source);
        }

        public static Int64 ToInt64(this Object source)
        {
            return Convert.ToInt64(source);
        }

        public static Int16 ToInt16(this Object source)
        {
            return Convert.ToInt16(source);
        }

        public static Decimal ToDecimal(this object source)
        {
            return Convert.ToDecimal(source);
        }

        public static Double ToDouble(this object source)
        {
            return Convert.ToDouble(source);
        }

        public static T To<T>(object source)
        {
            return (T) source;
        }

        public static string ToJson(this object source)
        {
            return JsonSerializer.Instance.Serialize(source);
        }

        public static void ToJson(this object source, StringBuilder output)
        {
            output.Append(ToJson(source));
        }

        public static T FromJson<T>(this string source)
        {
            return JsonSerializer.Instance.Deserialize<T>(source);
        }

        public static string ToBase64String(this byte[] source)
        {
            return Convert.ToBase64String(source);
        }

        public static byte[] ToByteArray(this string source)
        {
            return Encoding.UTF8.GetBytes(source);
        }

        public static string ToBase64String(this string source)
        {
            return Convert.ToBase64String(ToByteArray(source));
        }

        public static byte[] FromBase64String(this string source)
        {
            return Convert.FromBase64String(source);
        }

        public static string ToUtf8(this byte[] source)
        {
            return Encoding.UTF8.GetString(source);
        }

        public static string ToAscii(this byte[] source)
        {
            return Encoding.ASCII.GetString(source);
        }

        public static string ToAscii(this string source)
        {
            return Encoding.ASCII.GetString(ToByteArray(source));
        }
        public static string ToUtf8(this string source)
        {
            return Encoding.UTF8.GetString(ToByteArray(source));
        }
    }
}
