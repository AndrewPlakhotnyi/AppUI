using System.Text.Json;

namespace BlazorDesktop {
internal static class Json {

    public static string 
    ToJson(this object @object) => JsonSerializer.Serialize(@object);

    public static T 
    ParseJson<T>(this string json) => JsonSerializer.Deserialize<T>(json);
}
}
