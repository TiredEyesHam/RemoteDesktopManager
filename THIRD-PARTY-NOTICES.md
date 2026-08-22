# Third-party notices

Patchbay is licensed under the GNU General Public License v3.0 or later; see
[`LICENSE`](LICENSE). The components below are other people's work, under their
own terms, and this file is where those terms are acknowledged.

## Shipped with Patchbay

| Component | Licence | Used for |
|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4.0 | MIT | Observable objects and commands in the WPF shell |
| [.NET](https://github.com/dotnet/runtime) 10 | MIT | The runtime, WPF and Windows Forms |
| [Serilog](https://github.com/serilog/serilog) 4.3.0 | Apache-2.0 | Logging, and the destructuring policy secrets are redacted by |
| [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file) 7.0.0 | Apache-2.0 | The rolling log file |

MIT and Apache-2.0 are both permissive, and the GPL permits redistributing
either: a permissive licence can be combined into a copyleft work, though not
the other way round. Apache-2.0 is compatible with GPLv3 specifically and not
with GPLv2, which is one of the reasons this repository is v3 or later.

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

Every importer was written against the format, not against anybody's source. No
code from RDCMan, from `mstsc.exe`, from mRemoteNG or from any other connection
manager is present here, and the same must hold for the ones still to come.

- **RDCMan `.rdg`** — RDCMan was never open source, so there was nothing to
  copy even if copying had been the intention.
- **Remote Desktop `.rdp`** — Microsoft documents the settings, and the reader
  was written from that.
- **mRemoteNG `confCons.xml`** — mRemoteNG is GPL-2.0 and Patchbay is
  GPL-3.0-or-later, which are incompatible in that direction: lifting even a
  helper out of their tree would bind this repository to terms it has not
  chosen, and the remedy would be relicensing rather than deleting a file.
  Reading a file format is a different act, and it is the one that was
  performed.
