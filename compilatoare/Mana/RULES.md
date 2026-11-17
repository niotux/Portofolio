# Coding Rules

## Use the tool you have

If the language or the types already give you what you need, use it. Do not re-implement behavior that exists. If a type has an operator, a comparison, or a method for the job, that is the path. Writing your own version is a lie about what the code does.

## One source of truth

Every constant, symbol, or keyword exists in exactly one place. Everything else references it. Never scatter magic values across the codebase. If it needs to change, it changes in one place.

## No allocations for reading

You are reading data. You are not copying it. A reference, a span, an index — use what you have. Allocating a new string or buffer just to inspect something is a performance bug and a clarity bug.

## Trust the contract

A type's public surface is its truth. `Equals`, `TryParse`, `operator==` — they exist for a reason. Do not work around them. If they don't do what you need, the type is wrong, not your code.

## Control is explicit

When something is structural — a separator, a marker, a terminator — treat it as control. It does not carry data. It does not produce output. It changes state and yields. Confusing structure with data is how pipelines leak state.

## Eliminate, don't guess

Classify by elimination. Try the most specific thing first, fall back to the next. Do not scan or pattern-match to predict what something is. The parser tells you, not the heuristics.

## Errors are noise, not stop signs

A diagnostic is a side effect, not a control flow. Log it, discard the bad input, continue. Never let an error branch produce output that wasn't there before.

## Simple is correct

If you need special cases, exceptions, or fall-through logic, you have already missed something. The data model is wrong, not the code. Restructure until the happy path is a straight line.

## The type is the rule

If you find yourself writing `if`, `switch`, or `match` on raw values, ask what type should own that decision. Branching on literals is a symptom of missing abstractions.

## Read the source

Before writing code, read the code you are touching. Know the types, know the operators, know the contracts. The answers are already there. Making them up is how you invent bugs.
