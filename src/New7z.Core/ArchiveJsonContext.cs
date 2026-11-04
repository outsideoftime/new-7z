using System.Text.Json.Serialization;
namespace New7z.Core;
[JsonSerializable(typeof(List<ArchiveEntry>))]
internal partial class ArchiveJsonContext:JsonSerializerContext;
