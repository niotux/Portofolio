namespace Humans;

// One statement with positional parameters ($1, $2, ...). A float[] travels as a number array: cast it with $n::vector.
public sealed record SqlStatement(string Sql, List<object?> Parameters);
