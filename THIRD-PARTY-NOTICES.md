# Third-party notices

Patchbay is licensed under the GNU General Public License v3.0 or later; see
[`LICENSE`](LICENSE). The components below are other people's work, under their
own terms, and this file is where those terms are acknowledged.

## Shipped with Patchbay

| Component | Licence | Used for |
|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4.0 | MIT | Observable objects and commands in the WPF shell |
| [.NET](https://github.com/dotnet/runtime) 10 | MIT | The runtime, WPF and Windows Forms |

Both are MIT, which the GPL permits redistributing: a permissive licence can be
combined into a copyleft work, though not the other way round.

## Used but not redistributed

`mstscax.dll` — the Microsoft Remote Desktop ActiveX control — is a Windows
component and stays on the machine it came with. Patchbay creates it through COM
and ships no part of it. Every interface id, DISPID and property name in
`Patchbay.Rdp` was read from the type library on a machine that already had it,
which is a fact about the control rather than a copy of it.

The GPL's own exception for system libraries covers this: a program may rely on
the major components of the operating system it runs on without those components
having to be distributed under the GPL.

## Build and test only

None of these reach a released binary.

| Component | Licence |
|---|---|
| [xunit](https://github.com/xunit/xunit) 2.9.3 | Apache-2.0 |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) 3.1.4 | Apache-2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) 17.14.1 | MIT |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) 6.0.4 | MIT |

## File formats

The RDCMan `.rdg` importer was written against the format, not against anybody's
source. RDCMan itself was never open source, and no code from it — or from any
other connection manager — is present here. The same must hold for the importers
still to come: mRemoteNG is GPL-2.0, and reading its file format is fine while
copying its code would bind this repository to terms it has not chosen.
