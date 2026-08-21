# Patchbay

A modern replacement for Remote Desktop Connection Manager (RDCMan).

RDCMan was retired by Microsoft in 2020, briefly revived by Sysinternals, and
has been static since. What remains is either dated (mRemoteNG) or heavy
(Devolutions RDM). Patchbay aims at the middle: grouped connections with real
setting inheritance, tabbed sessions, and nothing else in the way.

> **Status: early, but it connects.** Sessions are real — the RDP control is
> hosted and tabbed, driven by the full settings surface, and it reconnects
> itself when a link drops. It is not yet something to run day to day: there is
> no logging, no installer and no release.
> 56 of the 122 items planned for v1 are done — see
> [`docs/BACKLOG.md`](docs/BACKLOG.md), where every box is ticked with a note on
> what was actually verified.

## Working today

- The model: groups, servers, and settings that inherit down the tree with
  per-host overrides, resolved back to the node each value came from
- Storage: atomic saves with rotating backups, schema-versioned, migration hook
- Import from RDCMan `.rdg`, including its own inheritance flags
- The tree: templates, selection, editing, search
- Live tabbed RDP sessions over `mstscax.dll` — gateway, redirection, display,
  performance and security settings all applied and verified against the real
  control — with auto-reconnect and readable disconnect reasons
- Secrets encrypted at rest with DPAPI

Roughly: the model, the tree and the RDP engine are largely in place; the
credential UI, the application shell services and everything release-shaped are
not.

## Still to come for v1

- Credential profiles, and prompting when a session needs a sign-in it has not
  been given
- Undo and redo across every edit to the tree
- Import from mRemoteNG and plain `.rdp`; export back out to both
- Light and dark themes that follow the system
- Logging, a crash handler, an installer and a signed release

Held to v1.1: the thumbnail grid. Rendering many live sessions at once is a
performance problem worth solving properly rather than early.

## How it works

WPF on .NET 10. Sessions are the Microsoft RDP ActiveX control (`mstscax.dll`,
`IMsRdpClient9`+) hosted through `WindowsFormsHost` — the same engine `mstsc.exe`
uses, so protocol support, RD Gateway, NLA and redirection come for free.

That hosting choice drives one visible design decision: an ActiveX control paints
over WPF content, so nothing floats above a live session. Editors and prompts are
docked panels rather than modal dialogs.

| Project | Purpose |
|---|---|
| `Patchbay.Core` | Domain model, inheritance resolution, storage, importers. No UI, no COM. |
| `Patchbay.Rdp` | ActiveX interop and the session host. Windows-only. |
| `Patchbay.App` | WPF shell, views and view models. |
| `Patchbay.Tests` | Unit tests, mostly against `Core`. |

`Patchbay.Core` has no dependency on `Patchbay.Rdp`. The session host sits behind
`IRemoteSessionHost`, so the entire UI can be built and tested against a fake.

## Building

Requires the .NET 10 SDK. Runs on Windows 10 1809 or later.

```
dotnet build
dotnet test
```

## Security

Connection documents hold hostnames, addresses and encrypted credential blobs.
They are gitignored by default — do not commit one.

Importers parse untrusted XML. RDCMan was pulled in 2020 over an XXE in exactly
that code path, so every importer prohibits DTD processing and runs with
`XmlResolver` set to null. If you are changing an importer, read the threat model
first.

Report anything sensitive privately rather than opening an issue.

## Licence

GNU General Public License v3.0 or later — see [`LICENSE`](LICENSE).
Copyright © 2026 TiredEyesHam.

Use it, change it, share it. What you may not do is close it: anything
distributed that is built from Patchbay has to come with its source under the
same terms. That is the whole reason this licence and not a permissive one.

Other people's work that ships alongside it is acknowledged in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
