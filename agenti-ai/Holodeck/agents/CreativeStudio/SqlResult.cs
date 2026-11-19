using System.Text.Json;

namespace CreativeStudio;

public sealed record SqlResult(List<string> Columns, List<List<JsonElement>> Rows, int Affected);
