using System.Runtime.InteropServices;

namespace Patchbay.Rdp.Interop;

// These declarations exist so Patchbay can ask a control what it is. Casting an
// RCW to a [ComImport] interface issues a QueryInterface for its IID and yields
// null when the control does not implement it, which is the cheapest reliable
// capability check there is.
//
// They are deliberately empty, and that is the whole design. Hand-written COM
// interop normally means transcribing the vtable in exact order — several
// hundred methods across the IMsTscAx → IMsRdpClient10 chain, where a single
// misplaced or mistyped entry does not fail to compile, does not fail to QI,
// and instead corrupts the stack at the first call. That is the risk the
// backlog flagged on this task.
//
// Declaring them as dispinterfaces removes it. Calls through an interface
// marked InterfaceIsIDispatch go out by *name* via IDispatch::GetIDsOfNames,
// so vtable position never enters into it: members can be added in any order,
// and a name the control does not recognise fails loudly and harmlessly at the
// call site instead of silently jumping into the wrong slot. Members get added
// as the settings mapper (M4-04) needs them; until then QI is all that is
// asked of these types, and RdpDispatch covers the rest by name.

/// <summary>The original Terminal Services control. Every generation implements it.</summary>
[ComImport]
[Guid(RdpIids.IMsTscAx)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IMsTscAx
{
}

/// <summary>The first RDP-branded generation.</summary>
[ComImport]
[Guid(RdpIids.IMsRdpClient)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IMsRdpClient
{
}

/// <summary>Windows 8 / Server 2012 generation.</summary>
[ComImport]
[Guid(RdpIids.IMsRdpClient8)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IMsRdpClient8
{
}

/// <summary>Windows 8.1 generation. Patchbay's floor — see <see cref="RdpEngineProbe.MinimumLevel"/>.</summary>
[ComImport]
[Guid(RdpIids.IMsRdpClient9)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IMsRdpClient9
{
}

/// <summary>Windows 10 generation, and the last of the scriptable chain.</summary>
[ComImport]
[Guid(RdpIids.IMsRdpClient10)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IMsRdpClient10
{
}

// The non-scriptable tier is a separate chain, and a genuine vtable interface
// rather than a dispinterface — which is precisely why the scriptable control
// cannot be handed a password and this one can. Nothing here is called yet
// (M3-02 and M4-10 own that); the probe only records how far it reaches.

/// <summary>The original non-scriptable interface, carrying <c>ClearTextPassword</c>.</summary>
[ComImport]
[Guid(RdpIids.IMsTscNonScriptable)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMsTscNonScriptable
{
}

[ComImport]
[Guid(RdpIids.IMsRdpClientNonScriptable5)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMsRdpClientNonScriptable5
{
}

[ComImport]
[Guid(RdpIids.IMsRdpClientNonScriptable6)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMsRdpClientNonScriptable6
{
}

[ComImport]
[Guid(RdpIids.IMsRdpClientNonScriptable7)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMsRdpClientNonScriptable7
{
}

[ComImport]
[Guid(RdpIids.IMsRdpClientNonScriptable8)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMsRdpClientNonScriptable8
{
}
