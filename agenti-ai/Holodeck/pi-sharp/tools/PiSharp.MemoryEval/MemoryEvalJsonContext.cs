// Source-generated JSON metadata for the memory eval harness types (no reflection, Native AOT safe).
// Follows the repo pattern in src/PiSharp.Core/Json/CoreJsonContext.cs.
using System.Text.Json.Serialization;

namespace PiSharp.MemoryEval;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EvalSuite))]
[JsonSerializable(typeof(EvalCase))]
[JsonSerializable(typeof(JudgeProfile))]
[JsonSerializable(typeof(RunReport))]
[JsonSerializable(typeof(EvalCaseResult))]
[JsonSerializable(typeof(Baseline))]
[JsonSerializable(typeof(BaselineDiff))]
[JsonSerializable(typeof(BaselineDiff.CaseDelta))]
internal partial class MemoryEvalJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
