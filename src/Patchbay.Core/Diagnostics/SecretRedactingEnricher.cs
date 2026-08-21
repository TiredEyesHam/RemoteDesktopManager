using Serilog.Core;
using Serilog.Events;

namespace Patchbay.Core.Diagnostics;

/// <summary>
/// The last thing between a log event and its sinks (M3-08).
///
/// <para>
/// <see cref="SecretRedactingPolicy"/> handles objects. This handles names.
/// <c>logger.Information("Signing in as {UserName} with {Password}", user,
/// password)</c> hands Serilog a bare string, and there is no object for a
/// destructuring policy to have an opinion about — only a hole in a message
/// template called <c>Password</c>. That is enough to act on, and it is the
/// shape the mistake usually takes.
/// </para>
///
/// <para>
/// It walks into structures, sequences and dictionaries as well as the top
/// level, so a secret reached through a <c>Dictionary&lt;string, string&gt;</c>
/// with a key called <c>Password</c> is masked too. Nothing is rebuilt unless
/// something in it changed.
/// </para>
///
/// <para>
/// What none of this can catch is a secret pasted into the message itself.
/// <c>logger.Information("password " + password)</c> makes the password part
/// of the template text, and by the time an enricher sees it there is nothing
/// left to tell it apart from the rest of the sentence. Only holes can be
/// redacted, because only holes are still values. That one stays a review
/// question until an analyser that understands Serilog templates is worth
/// adding to the build.
/// </para>
/// </summary>
public sealed class SecretRedactingEnricher : ILogEventEnricher
{
    private static readonly ScalarValue Masked = new(SecretNames.Mask);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Collected first, applied after: AddOrUpdateProperty writes to the
        // dictionary being enumerated.
        List<LogEventProperty>? replacements = null;

        foreach (KeyValuePair<string, LogEventPropertyValue> property in logEvent.Properties)
        {
            if (Redact(property.Key, property.Value) is { } redacted)
            {
                (replacements ??= []).Add(new LogEventProperty(property.Key, redacted));
            }
        }

        if (replacements is null)
        {
            return;
        }

        foreach (LogEventProperty replacement in replacements)
        {
            logEvent.AddOrUpdateProperty(replacement);
        }
    }

    /// <summary>
    /// The replacement for a named value, or null if it does not need one.
    /// </summary>
    private static LogEventPropertyValue? Redact(string name, LogEventPropertyValue value)
    {
        // Unwrapped, so that HasPassword = true survives as the useful fact it
        // is rather than being masked for its name.
        object? held = value is ScalarValue scalar ? scalar.Value : value;

        return SecretNames.Redacts(name, held) ? Masked : RedactWithin(value);
    }

    /// <summary>
    /// The same again one level down, or null if nothing below changed.
    /// </summary>
    private static LogEventPropertyValue? RedactWithin(LogEventPropertyValue value)
    {
        switch (value)
        {
            case StructureValue structure:
            {
                List<LogEventProperty>? rebuilt = null;

                for (int i = 0; i < structure.Properties.Count; i++)
                {
                    LogEventProperty member = structure.Properties[i];

                    if (Redact(member.Name, member.Value) is { } redacted)
                    {
                        rebuilt ??= [.. structure.Properties];
                        rebuilt[i] = new LogEventProperty(member.Name, redacted);
                    }
                }

                return rebuilt is null ? null : new StructureValue(rebuilt, structure.TypeTag);
            }

            case DictionaryValue dictionary:
            {
                Dictionary<ScalarValue, LogEventPropertyValue>? rebuilt = null;

                foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> entry in dictionary.Elements)
                {
                    string key = entry.Key.Value?.ToString() ?? string.Empty;

                    if (Redact(key, entry.Value) is { } redacted)
                    {
                        rebuilt ??= new Dictionary<ScalarValue, LogEventPropertyValue>(dictionary.Elements);
                        rebuilt[entry.Key] = redacted;
                    }
                }

                return rebuilt is null ? null : new DictionaryValue(rebuilt);
            }

            case SequenceValue sequence:
            {
                // Elements have no names of their own, so only what is inside
                // them can match.
                List<LogEventPropertyValue>? rebuilt = null;

                for (int i = 0; i < sequence.Elements.Count; i++)
                {
                    if (RedactWithin(sequence.Elements[i]) is { } redacted)
                    {
                        rebuilt ??= [.. sequence.Elements];
                        rebuilt[i] = redacted;
                    }
                }

                return rebuilt is null ? null : new SequenceValue(rebuilt);
            }

            default:
                return null;
        }
    }
}
