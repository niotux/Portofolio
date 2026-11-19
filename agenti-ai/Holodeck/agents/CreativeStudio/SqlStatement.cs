namespace CreativeStudio;

public sealed record SqlStatement(string Sql, List<object?> Parameters);
