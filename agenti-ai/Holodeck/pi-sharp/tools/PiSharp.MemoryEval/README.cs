// Eval harness scaffold for the pi-sharp memory pipeline (adr-pi-sharp-memory-pipeline).
// Shape follows ai-blue's eval run (docs/evaluation.md): versioned JSON suites, a named
// judge profile, a pass threshold, and a committed baseline that is only updated by an
// explicit operator-blessed step. Two modes, as in ai-blue:
//
//   recorded : run the suite against committed fixtures, no live model, no network.
//   live     : run the suite against a real judge model (opt-in, needs a profile).
//
// This is SCAFFOLD ONLY (pi-sharp-wi-memory-phase0, out-of-scope rules): the pipeline itself
// (MCP server, extraction, negative-rule enforcement) is not implemented here. The harness
// is the acceptance precondition the ADR names.
