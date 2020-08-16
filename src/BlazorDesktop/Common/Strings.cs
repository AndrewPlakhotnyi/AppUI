using System;
using System.Buffers.Text;
using System.Text;

namespace BlazorDesktop {
internal static class Strings {

    public static string 
    ToBase64(this byte[] bytes) => Convert.ToBase64String(bytes);

    public static byte[] 
    ToUtf8Bytes(this string @string) => Encoding.UTF8.GetBytes(@string);

    public static bool
    IsNullOrWhiteSpace(this string @string) => string.IsNullOrWhiteSpace(@string);
}
}
