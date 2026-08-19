using Patchbay.Core.Sessions;
using Patchbay.Rdp.Interop;

namespace Patchbay.Rdp.Hosting;

/// <summary>
/// Walks a plan from <see cref="RdpSettingsMapper"/> and writes it to a control
/// (M4-04). The only part of the settings mapper that touches COM, and it is
/// deliberately the small part.
///
/// <para>
/// <b>Every write is attempted.</b> Stopping at the first refusal would leave a
/// control half configured and give no account of what was missed — and the
/// commonest reason for a refusal is an older control that never had the
/// property, which is not a reason to abandon a connection that would work.
/// So the plan runs to the end and the result is a report, not an exception.
/// </para>
///
/// <para>
/// <b>Not supported and rejected are kept apart.</b> A control with no
/// <c>RedirectClipboard</c> is old; a control that has one and refuses the
/// value is being told something wrong. They read identically in a log that
/// collapses them, and only one of them is Patchbay's fault.
/// </para>
/// </summary>
public static class RdpSettingsApplier
{
    /// <summary>
    /// Applies <paramref name="plan"/> to <paramref name="client"/>.
    /// </summary>
    /// <remarks>
    /// Must run on the thread the control lives on, before it connects. Most of
    /// these properties are read once when the connection is made and ignored
    /// afterwards, so applying them to a live session succeeds and does nothing
    /// — which is the worst of both answers and why the caller connects after.
    /// </remarks>
    public static RdpSettingsReport Apply(RdpClientInstance client, IReadOnlyList<RdpSettingWrite> plan)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Count == 0)
        {
            return RdpSettingsReport.Empty;
        }

        RdpSettingsObjects objects = new(client);
        List<RdpSettingReport> entries = new(plan.Count);

        foreach (RdpSettingWrite write in plan)
        {
            entries.Add(ApplyOne(objects, write));
        }

        return new RdpSettingsReport { Entries = entries };
    }

    private static RdpSettingReport ApplyOne(RdpSettingsObjects objects, RdpSettingWrite write)
    {
        if (objects.Resolve(write.Target) is not { } target)
        {
            return new RdpSettingReport
            {
                Write = write,
                Outcome = RdpSettingOutcome.Unsupported,
                Message = $"This RDP control has no {write.Target} object.",
            };
        }

        foreach (string name in write.Candidates)
        {
            // Asked before written, so that a control which never had the
            // property is told apart from one that has it and objects to the
            // value. Both throw the same exception from a bare write.
            if (!RdpDispatch.Has(target, name))
            {
                continue;
            }

            try
            {
                RdpDispatch.Set(target, name, write.Value);

                return new RdpSettingReport
                {
                    Write = write,
                    Outcome = RdpSettingOutcome.Applied,
                    UsedName = name,
                };
            }
            catch (RdpEngineException ex)
            {
                return new RdpSettingReport
                {
                    Write = write,
                    Outcome = RdpSettingOutcome.Rejected,
                    Message = ex.Message,
                    UsedName = name,
                };
            }
        }

        return new RdpSettingReport
        {
            Write = write,
            Outcome = RdpSettingOutcome.Unsupported,
            Message = $"This RDP control has no '{write.Name}'.",
        };
    }
}
