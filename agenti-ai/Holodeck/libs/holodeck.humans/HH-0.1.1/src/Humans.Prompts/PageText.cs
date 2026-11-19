namespace Humans;

// Library prose for the page a person's own passes continue. {name}, {self}, {personality} and {other} come from the
// instance settings and the role. Recognise opens what I remember about someone I have talked to before;
// ThoughtAfterRecall opens a thought that follows something remembered.
public sealed record PageText(string Header, string Self, string Personality, string Other, string Summary, string Recognise, string Remember, string Thought, string ThoughtAfterRecall, string Close);
