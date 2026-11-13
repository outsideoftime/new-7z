using System.Text.Json.Serialization;
namespace New7z.App;
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(ShellRequest))]
[JsonSerializable(typeof(SharedTask))]
[JsonSerializable(typeof(Dictionary<string,string>))]
internal partial class AppJsonContext:JsonSerializerContext;
