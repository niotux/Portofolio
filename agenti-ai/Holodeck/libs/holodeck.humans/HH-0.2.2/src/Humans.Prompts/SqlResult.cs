using System.Text.Json;

namespace Humans;

public sealed record SqlResult(List<string> Columns, List<List<JsonElement>> Rows, int Affected);
