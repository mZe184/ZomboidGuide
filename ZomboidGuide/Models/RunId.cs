using System;

namespace ZomboidGuide.Models;

public readonly record struct RunId
{
    public RunId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? "run-unknown"
            : value.Trim();
    }

    public string Value { get; }

    public bool IsUnknown => Value.Equals("run-unknown", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Value;
}
