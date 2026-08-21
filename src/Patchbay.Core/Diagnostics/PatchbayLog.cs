using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Patchbay.Core.Diagnostics;

/// <summary>
/// Makes Patchbay's loggers, with redaction fitted rather than offered
/// (M0-07, M3-08).
///
/// <para>
/// There is no way to get a logger from here without
/// <see cref="SecretRedactingPolicy"/> and
/// <see cref="SecretRedactingEnricher"/> on it. A <c>RedactSecrets()</c>
/// extension that callers remembered to call would be the same code and a
/// worse control: the failure would be a missing line in a file nobody looks
/// at twice, and the symptom would be a log that reads perfectly well and has
/// a password in it.
/// </para>
///
/// <para>
/// Sinks are the caller's business, which is how Core ends up owning a
/// logging policy without owning a log file. <c>Patchbay.App</c> knows where
/// <c>%LOCALAPPDATA%</c> is and Core does not need to.
/// </para>
/// </summary>
public static class PatchbayLog
{
    /// <summary>
    /// The level everything is filtered at, changeable while running.
    ///
    /// <para>
    /// A switch rather than a fixed minimum, because the log is wanted at its
    /// most detailed exactly when restarting would lose the thing being
    /// chased. A settings page can turn this up (M7-01), and until there is
    /// one <c>PATCHBAY_LOG_LEVEL</c> sets the starting point.
    /// </para>
    /// </summary>
    public static LoggingLevelSwitch Level { get; } = new(LogEventLevel.Information);

    /// <summary>
    /// Builds a logger with the redaction in place and whatever sinks the
    /// caller adds.
    /// </summary>
    /// <param name="sinks">
    /// Given the configuration after the policy is fitted, so a sink cannot be
    /// added ahead of the redaction.
    /// </param>
    public static Logger Create(Action<LoggerConfiguration> sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(Level)
            .Destructure.With<SecretRedactingPolicy>()

            // Order matters. FromLogContext puts properties on the event —
            // the correlation id in M4-16 arrives this way — and the redactor
            // has to run after anything that can add one, or it scrubs an
            // event that is not finished yet.
            .Enrich.FromLogContext()
            .Enrich.With<SecretRedactingEnricher>();

        sinks(configuration);

        return configuration.CreateLogger();
    }

    /// <summary>
    /// Reads a starting level from <c>PATCHBAY_LOG_LEVEL</c> and applies it,
    /// leaving <see cref="Level"/> alone if the variable is missing or is not
    /// a level name. Returns what the level ended up as.
    ///
    /// <para>
    /// Unparseable rather than invalid: a typo means the release default, not
    /// a refusal to start. Nothing here is worth failing a launch over.
    /// </para>
    /// </summary>
    public static LogEventLevel ApplyEnvironmentLevel(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out LogEventLevel parsed))
        {
            Level.MinimumLevel = parsed;
        }

        return Level.MinimumLevel;
    }
}
