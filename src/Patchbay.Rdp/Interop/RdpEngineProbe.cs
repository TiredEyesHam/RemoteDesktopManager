using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Patchbay.Rdp.Interop;

/// <summary>
/// Finds a usable RDP control and reports what it can do (M4-02).
///
/// The method is to create each candidate coclass, newest first, and ask the
/// object itself what interfaces it implements. That sounds like more work than
/// reading the registry until you watch a real machine do it: on the one this
/// was written against, the two newest registered coclasses returned
/// CLASS_E_CLASSNOTAVAILABLE when created, while every generation from there
/// down produced an identical control answering to IMsRdpClient10. A registry
/// read would have chosen the broken one, and a name-based guess would have
/// reported the wrong generation for all of them.
///
/// Nothing here needs a window. Embedding the control in one is M4-03, and
/// keeping the two apart means detection can run at startup and be logged
/// before any session exists.
/// </summary>
public static class RdpEngineProbe
{
    /// <summary>
    /// The oldest control Patchbay will accept.
    ///
    /// The supported floor is Windows 10 1809 (SupportedOSPlatformVersion in
    /// Directory.Build.props), which ships IMsRdpClient10. Anything answering
    /// below <see cref="RdpClientLevel.Client9"/> on a machine that can run
    /// Patchbay at all is not an honest old install; it is a damaged or
    /// hijacked registration, and connecting through it would be a worse
    /// outcome than refusing to.
    /// </summary>
    public const RdpClientLevel MinimumLevel = RdpClientLevel.Client9;

    /// <summary>
    /// Candidates in the order they are tried. The MsTscAx family only, for
    /// the credential reason set out in <see cref="RdpClsids"/>.
    /// </summary>
    private static readonly (string ProgId, string ClassId)[] Candidates =
    [
        ("MsTscAx.MsTscAx.13", RdpClsids.MsTscAx13),
        ("MsTscAx.MsTscAx.12", RdpClsids.MsTscAx12),
        ("MsTscAx.MsTscAx.11", RdpClsids.MsTscAx11),
        ("MsTscAx.MsTscAx.10", RdpClsids.MsTscAx10),
        ("MsTscAx.MsTscAx.9", RdpClsids.MsTscAx9),
        ("MsTscAx.MsTscAx.8", RdpClsids.MsTscAx8),
    ];

    private static readonly Lock Gate = new();
    private static RdpProbeResult? _cached;

    /// <summary>
    /// Detects the best available control. The answer is cached: probing
    /// creates and destroys up to six COM objects, and the answer cannot
    /// change while the process is running.
    /// </summary>
    /// <param name="refresh">Probe again even if an answer is already held.</param>
    public static RdpProbeResult Detect(bool refresh = false)
    {
        lock (Gate)
        {
            if (refresh || _cached is null)
            {
                _cached = ProbeOnStaThread();
            }

            return _cached;
        }
    }

    /// <summary>
    /// Runs the probe in an apartment where the answers are true.
    ///
    /// This is not tidiness. The scriptable interfaces carry a type library, so
    /// COM can marshal them anywhere and a query from any thread answers
    /// correctly. The non-scriptable ones have no proxy or stub registered, so
    /// asking for them across an apartment boundary returns E_NOINTERFACE —
    /// not "this control cannot do that", but "not from here". Probing from a
    /// pool thread therefore reports <see cref="RdpNonScriptableLevel.None"/>
    /// on a control that fully supports credential passing, and Patchbay would
    /// go on to tell someone their saved passwords will be ignored when they
    /// will not. Measured, not theorised: the same control reported None from
    /// an MTA thread and V8 from an STA one.
    ///
    /// Detection stays callable from anywhere, which is the point — startup
    /// logging should not have to care.
    /// </summary>
    private static RdpProbeResult ProbeOnStaThread()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return Probe();
        }

        RdpProbeResult? result = null;
        Exception? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                result = Probe();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "Patchbay RDP probe",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new RdpEngineException("Probing for an RDP control failed.", failure);
        }

        return result ?? new RdpProbeResult { Engine = null, Attempts = [] };
    }

    /// <summary>
    /// The detected control, or a failure carrying the whole probe report.
    /// Call this where there is no sensible fallback; call <see cref="Detect"/>
    /// where there is one.
    /// </summary>
    /// <exception cref="RdpEngineException">No usable control is installed.</exception>
    public static RdpEngineInfo Require()
    {
        RdpProbeResult result = Detect();

        return result.Engine ?? throw new RdpEngineException(
            "Patchbay could not find a usable Remote Desktop control on this computer. "
            + "Connections are not possible until that is put right."
            + Environment.NewLine
            + result.Describe());
    }

    /// <summary>
    /// Creates a live control, ready to be embedded (M4-03) and configured
    /// (M4-04). The caller owns it and must dispose it.
    /// </summary>
    /// <exception cref="RdpEngineException">
    /// No usable control is installed, or the calling thread is not STA.
    /// </exception>
    public static RdpClientInstance CreateClient()
    {
        RdpEngineInfo engine = Require();

        // The control is registered ThreadingModel=Apartment. COM will happily
        // create one from a pool thread by spinning up a host apartment and
        // handing back a proxy, and it will even appear to work, until the
        // window it eventually owns is pumped by a thread that is not the one
        // that created it and input stops arriving for reasons no stack trace
        // will explain. Refusing here costs a line; diagnosing that later costs
        // an evening.
        ApartmentState apartment = Thread.CurrentThread.GetApartmentState();

        if (apartment != ApartmentState.STA)
        {
            string wrongApartment = string.Create(
                CultureInfo.InvariantCulture,
                $"The RDP control must be created on an STA thread, but this one is {apartment}.");

            throw new RdpEngineException(wrongApartment + " Create sessions on the UI thread.");
        }

        object instance = Create(engine.ClassId)
            ?? throw new RdpEngineException(string.Create(
                CultureInfo.InvariantCulture,
                $"COM returned nothing for {engine.ProgId}."));

        return new RdpClientInstance(instance, engine);
    }

    private static RdpProbeResult Probe()
    {
        List<RdpProbeAttempt> attempts = new(Candidates.Length);
        RdpEngineInfo? engine = null;

        foreach ((string progId, string classId) in Candidates)
        {
            Guid clsid = new(classId);
            object? instance;

            try
            {
                instance = Create(clsid);
            }
            catch (COMException ex)
            {
                attempts.Add(Failed(progId, clsid, DescribeHResult(ex.HResult)));
                continue;
            }
            catch (Exception ex) when (ex is TypeLoadException or InvalidComObjectException or NotSupportedException)
            {
                attempts.Add(Failed(progId, clsid, ex.Message));
                continue;
            }

            if (instance is null)
            {
                attempts.Add(Failed(progId, clsid, "COM returned nothing"));
                continue;
            }

            try
            {
                RdpClientLevel level = LevelOf(instance);

                if (level < MinimumLevel)
                {
                    attempts.Add(Failed(progId, clsid, string.Create(
                        CultureInfo.InvariantCulture,
                        $"only reaches IMsRdpClient level {(int)level}, below the minimum of {(int)MinimumLevel}")));
                    continue;
                }

                // Answering QueryInterface is not the same as working. A
                // registration can point at a DLL that loads but will not talk,
                // and it is better to learn that here than on someone's first
                // connection attempt. Reading a property every generation has
                // proves IDispatch is live.
                if (!RdpDispatch.Has(instance, "Server"))
                {
                    attempts.Add(Failed(progId, clsid, "created, but does not answer to IDispatch"));
                    continue;
                }

                (string? modulePath, string? moduleVersion) = ReadModule(clsid);

                engine = new RdpEngineInfo
                {
                    ProgId = progId,
                    ClassId = clsid,
                    Level = level,
                    NonScriptableLevel = NonScriptableLevelOf(instance),
                    ModulePath = modulePath,
                    ModuleVersion = moduleVersion,
                };

                attempts.Add(new RdpProbeAttempt { ProgId = progId, ClassId = clsid, Level = level });
                break;
            }
            finally
            {
                Release(instance);
            }
        }

        return new RdpProbeResult { Engine = engine, Attempts = attempts };
    }

    private static object? Create(Guid classId)
    {
        Type? type = Type.GetTypeFromCLSID(classId, throwOnError: false);
        return type is null ? null : Activator.CreateInstance(type);
    }

    private static RdpProbeAttempt Failed(string progId, Guid classId, string reason)
        => new() { ProgId = progId, ClassId = classId, Failure = reason };

    private static RdpClientLevel LevelOf(object instance) => instance switch
    {
        IMsRdpClient10 => RdpClientLevel.Client10,
        IMsRdpClient9 => RdpClientLevel.Client9,
        IMsRdpClient8 => RdpClientLevel.Client8,
        IMsRdpClient => RdpClientLevel.Client,
        IMsTscAx => RdpClientLevel.Base,
        _ => RdpClientLevel.None,
    };

    private static RdpNonScriptableLevel NonScriptableLevelOf(object instance) => instance switch
    {
        IMsRdpClientNonScriptable8 => RdpNonScriptableLevel.V8,
        IMsRdpClientNonScriptable7 => RdpNonScriptableLevel.V7,
        IMsRdpClientNonScriptable6 => RdpNonScriptableLevel.V6,
        IMsRdpClientNonScriptable5 => RdpNonScriptableLevel.V5,
        IMsTscNonScriptable => RdpNonScriptableLevel.Base,
        _ => RdpNonScriptableLevel.None,
    };

    /// <summary>
    /// Names the HRESULTs a probe actually meets. These three are the
    /// difference between "RDP is not installed", "it is installed but not
    /// usable" and "COM was never initialised on this thread", and the bare
    /// number tells nobody that.
    /// </summary>
    private static string DescribeHResult(int hresult) => hresult switch
    {
        unchecked((int)0x80040154) => "not registered (REGDB_E_CLASSNOTREG)",
        unchecked((int)0x80040111) => "registered but not creatable (CLASS_E_CLASSNOTAVAILABLE)",
        unchecked((int)0x800401F0) => "COM was not initialised on this thread (CO_E_NOTINITIALIZED)",
        _ => string.Create(CultureInfo.InvariantCulture, $"HRESULT 0x{hresult:X8}"),
    };

    /// <summary>
    /// Which DLL the registration points at, for the log. Best effort: an
    /// unreadable key is no reason to reject a control that has already proved
    /// it works.
    /// </summary>
    private static (string? Path, string? Version) ReadModule(Guid classId)
    {
        try
        {
            string key = string.Create(CultureInfo.InvariantCulture, $@"CLSID\{{{classId}}}\InprocServer32");
            using RegistryKey? registry = Registry.ClassesRoot.OpenSubKey(key);

            if (registry?.GetValue(null) is not string path || !File.Exists(path))
            {
                return (null, null);
            }

            return (path, FileVersionInfo.GetVersionInfo(path).FileVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (null, null);
        }
    }

    private static void Release(object? instance)
    {
        if (instance is null || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(instance);
        }
        catch (ArgumentException)
        {
            // Already released. Nothing to do, and nothing worth saying.
        }
    }
}
