# Patchbay — Backlog

A modern replacement for RDCMan (Sysinternals). WPF on .NET 10, hosting the
Microsoft RDP ActiveX control (`mstscax.dll`) via `WindowsFormsHost`.

**Effort key** — `S` ≤ half a day · `M` ≈ 1 day · `L` 2–3 days · `XL` a week or more.
**Flags** — `[risk]` likely to overrun, `[sec]` security-sensitive, `[spike]` timeboxed investigation, `[cut]` part of the days-not-weeks first cut.

Milestones M0–M5 and M7 are v1. M6 is deliberately held to v1.1 — see the note there.

---

## M0 — Foundations

- [x] `M0-01` Init repo: `.gitignore` (VS/Rider/.NET), `.editorconfig`, LICENCE — **S** `[cut]`
- [x] `M0-02` Solution layout: `Patchbay.Core` / `Patchbay.Rdp` / `Patchbay.App` / `Patchbay.Tests` — **S** `[cut]`
- [x] `M0-03` Target `net10.0` / `net10.0-windows`, WPF + WinForms, x64 + ARM64 — **S** `[cut]`
- [x] `M0-04` Nullable enabled, warnings-as-errors, analysers — **S**
- [ ] `M0-05` DI container + Generic Host, application lifetime — **M**
- [ ] `M0-06` CommunityToolkit.Mvvm, ViewModel conventions — **S** `[cut]`
- [ ] `M0-07` Serilog rolling file to `%LOCALAPPDATA%\Patchbay\logs`, runtime level switch — **M**
- [ ] `M0-08` Global exception handler → log + recoverable crash dialog — **M**
- [ ] `M0-09` App data path service (`%APPDATA%\Patchbay`), portable-mode flag — **S**
- [ ] `M0-10` Design tokens: colour / type / spacing resource dictionaries, light + dark — **L**
- [ ] `M0-11` Theme service: Light / Dark / Follow system (`WM_SETTINGCHANGE`) — **M**
- [ ] `M0-12` Shell window with custom title bar, caption buttons, snap-layouts support — **L** — must use `WindowChrome`. Setting `AllowsTransparency` makes the window layered, and a layered window does not render a hosted RDP session at all: no error, no blank rectangle, nothing. `AirspaceRules` fails the placement check if this is ever turned on
- [ ] `M0-13` Three-pane layout, `GridSplitter`s, persisted pane widths — **M**
- [ ] `M0-14` Icon set (Fluent System Icons) + icon control — **S**
- [ ] `M0-15` Toast / notification service — **M**

## M1 — Model and storage

- [x] `M1-01` Domain model: `NodeBase`, `GroupNode`, `ServerNode`, parent references — **M** `[cut]`
- [x] `M1-02` Inheritance pattern: `null` = inherit, value = override — **M** `[cut]`
- [x] `M1-03` Setting groups: Connection, Credentials, Gateway, Display, LocalResources, Experience, Security, Advanced — **L** — eight sections in `SettingCatalogue`, and the ordering is the point rather than the count. They run from what a connection cannot do without to what almost nobody changes: Connection, Credentials, Gateway, Display, Local resources, Experience, Security, Advanced. Somebody adding a machine fills in the first section and stops; somebody chasing a problem knows the awkward ones are at the bottom. Grouping is a property of the catalogue and not of the editor, so the sections are asserted in `Core` tests rather than eyeballed in a window — a section that exists but holds nothing would render as an empty heading, and a setting that belongs to no section would render nowhere at all, which is the failure worth a test because it looks like the setting was never added
- [x] `M1-04` Inheritance resolver: walk ancestors, return value and source node — **M** `[cut]`
- [x] `M1-05` Root document model with schema version field — **S** `[cut]`
- [x] `M1-06` `System.Text.Json` serialisation, polymorphic node converter — **M** `[cut]`
- [x] `M1-07` Atomic save: temp file + `File.Replace`, rotate 5 backups — **M**
- [x] `M1-08` Load path with schema migration hook — **M**
- [ ] `M1-09` `FileSystemWatcher` for external edits, reload prompt — **M**
- [ ] `M1-10` Dirty tracking + debounced save-on-change — **M**
- [ ] `M1-11` Undo/redo command stack covering every tree mutation — **L**
- [x] `M1-12` Import RDCMan `.rdg`: `XmlResolver = null`, `DtdProcessing.Prohibit` — **M** `[sec]` — hardening lives in `SafeXml`, with the attacks it stands for named in `RdgImporterSecurityTests`
- [x] `M1-13` Map `.rdg` schema v3 (groups, servers, credentials, inheritance) — **L** — `inherit="FromParent"` maps straight onto null-means-inherit; passwords are counted and reported, never decrypted
- [ ] `M1-14` Import individual `.rdp` files — **M**
- [ ] `M1-15` Import mRemoteNG `confCons.xml` — **M**
- [ ] `M1-16` Export selection to `.rdp` — **S**
- [ ] `M1-17` Export document to `.rdg` for interop — **M**
- [x] `M1-18` Unit tests: inheritance resolution matrix — **M**
- [x] `M1-19` Unit tests: serialisation round-trip and migration — **M**
- [ ] `M1-20` Malformed-input tests for every importer — **M** `[sec]` — done for `.rdg`; still owed by `M1-14` and `M1-15`

## M2 — Tree and CRUD

- [x] `M2-01` `TreeView` + `HierarchicalDataTemplate`, virtualisation on — **M** `[cut]`
- [x] `M2-02` Node template: name, address, tag badges, type glyph — **M** `[cut]` — status dot needs a session, so it lands with `M4-05`
- [ ] `M2-03` Expand/collapse with persisted expansion state — **S** — works in session and survives a search; not yet written to disk
- [x] `M2-04` Selection bound to inspector — **S** `[cut]`
- [ ] `M2-05` Multi-select (Ctrl / Shift) — **L**
- [x] `M2-06` New connection: inspector "new" mode + validation — **M** `[cut]`
- [x] `M2-07` Edit connection: inspector "edit" mode, unsaved-changes guard — **M** `[cut]`
- [x] `M2-08` Delete with inline confirm; cascade warning for groups — **M** `[cut]`
- [x] `M2-09` Duplicate connection or group (deep copy, name suffix) — **S**
- [x] `M2-10` New / rename / delete group, arbitrary nesting depth — **M**
- [ ] `M2-11` Drag and drop: reorder within group, move between groups — **L**
- [ ] `M2-12` Cut / copy / paste nodes — **M**
- [x] `M2-13` Search box: debounce, match name + address + tag, auto-expand hits — **M** `[cut]`
- [ ] `M2-14` Highlight matched substring in results — **S**
- [ ] `M2-15` Sort modes: manual, name, address, status — **S**
- [x] `M2-16` Tree context menu covering all node actions — **M**
- [x] `M2-17` Keyboard: arrows, Enter connects, F2 renames, Del, Ctrl+F — **M** — Enter was the last of the five and had nothing to open until `M5-01`. It connects the selected machine, as does a double-click; on a group both do nothing and the double-click falls through to expanding the row, which is what a double-click on a folder is for
- [x] `M2-18` Inspector read view with inheritance chips — **M** `[cut]`
- [x] `M2-19` Per-field inherit/override toggle — **M** `[cut]`
- [x] `M2-20` Validation: hostname/IP format, port range, name uniqueness — **M**
- [x] `M2-21` Empty states: no connections, no search results — **S**
- [ ] `M2-22` Bulk edit across a multi-selection — **L**

## M3 — Credentials

- [x] `M3-01` Credential model + named, reusable `CredentialProfile` — **M** — the store was already there: M3-02 protects a secret into a string and reads it back, so what was left was deciding where the string lives and how a profile is referred to.

  Profiles sit beside the tree on `ConnectionDocument`, not in it. A profile is not a place to connect to, and putting it in the tree would make it inherit settings and turn up in search results. Nodes point at one through `CredentialProfileId`, which was already on `ConnectionSettings` and already inheritable, so a group can name the account its servers use and one edit changes fifty machines. No schema bump: a document written before this existed reads back with an empty list, which is what it meant.

  `CredentialVault` owns both directions, so the protector has exactly one caller. Spreading protect and unprotect around the application is how a plaintext password ends up assigned straight to `ProtectedPassword` by a screen that meant well, and nothing about the document looks wrong until somebody opens it in a text editor. Reading never throws — a deleted profile and a password from another Windows account are both ordinary outcomes of opening a file that has moved. Writing does throw, following M3-02: a failed protect must not fall back to plaintext, and a failed save leaves any previous password alone rather than half-writing over it.

  Four outcomes, and the two that went wrong still carry a usable sign-in. `Resolved` covers a profile with a password and a profile without one, because the difference is `HasPassword` and a profile with nothing saved is configured correctly and simply needs asking (M3-05). `NoProfile` is prompt and current-user connections, where the account name is left to the mapper, which already falls back to the node (M4-10). `ProfileMissing` covers both a deleted profile and one that was never named, because the same thing has to happen next either way. `PasswordUnreadable` is the one with a rule attached: the stored value must be left exactly as it is, since a blob this account cannot open is very likely one another account can, and overwriting it loses somebody else's password to save a round trip.

  Wired into the connect path, so this is not a model nothing uses. The shell resolves before opening a tab and hands the result to `SessionRequest.Credentials`, and a missing profile or an unreadable password says so in the status bar rather than arriving as a logon prompt nobody expected. The connection still goes ahead in both cases: the session comes up showing its own logon screen, which is what M3-06 will put a panel over. Real DPAPI is passed in from `App.xaml.cs`; the view model defaults to the protector that refuses, so a shell can be built without reaching the platform.

  28 tests. `CredentialProfileId` stays `SettingKind.Hidden` in the catalogue — it is a `Guid`, and a text box for one is worse than nothing. Turning it into a picker is `M3-10`, which is also where creating and deleting profiles lives; deleting is why `NodesUsingCredential` exists, and why it reports only nodes that name the profile themselves rather than every server that inherits it
- [x] `M3-02` DPAPI `CurrentUser` protect/unprotect for stored secrets — **M** `[sec]` `[cut]` — the primitive, before anything has a secret to put through it. `ISecretProtector` and everything around the platform call live in `Core`; the two lines that are actually Windows live in the shell, because `ArchitectureTests` says where the implementation goes.

  A protected secret is written as `pb1:dpapi:BASE64`, and the marker earns its keep three times. A field holding a password and a field holding a blob are both strings, so without one the only way to tell them apart is to try decrypting, which destroys a password that happens to be valid base64. A document will hold blobs from more than one store at once, since Credential Manager (M3-04) and a master password (M3-07) arrive one secret at a time. And a file written by a later Patchbay has to be refusable politely rather than reported as corrupt.

  Reading fails in five distinguishable ways because they are five different sentences. `WrongScheme`, `TooNew` and `Unreadable` all mean leave the stored value alone, and collapsing them is how somebody who signed in as a different Windows account gets told their connection file is damaged. Writing is the one direction that throws: `UnavailableSecretProtector` refuses rather than falling back to plaintext, because the fallback is invisible — nothing on screen changes and the only difference is a cleartext password in a file that gets backed up and attached to support tickets.

  Two things about DPAPI itself. The scope is chosen when protecting and travels inside the blob, so passing `LocalMachine` to `Unprotect` opens a `CurrentUser` blob happily — that argument is not a check and nothing relies on it. And the entropy is a constant in the binary, so it is not a secret; it buys only that another program running as the same user cannot open a Patchbay blob by handing it to DPAPI and seeing what falls out. Nothing here defends against code running as the signed-in user, because that is DPAPI's contract. M3-07's job.

  Binding a blob to its node id was considered and rejected: duplicate (M2-09) and import both move a secret between ids, and it would silently break the copy. 46 tests in `Core`, plus 33 checks against real DPAPI covering tamper, truncation, foreign entropy, a 4 KB secret and every not-ours case. Cross-account and cross-machine unreadability cannot be tested from one account and are on the `M4-17` matrix
- [ ] `M3-03` In-memory secret handling: pinned buffers, zero on dispose — **M** `[sec]` — M3-02 zeroes every byte array it makes but hands the secret back as a `string`, which it cannot zero. Shortening that string's life is this task; abolishing it is not possible, because the RDP control takes its password as a BSTR and cleartext exists in managed memory at connect time whatever the store does
- [ ] `M3-04` Windows Credential Manager as an alternative store — **L**
- [ ] `M3-05` "Prompt each time" flow with per-session cache — **M**
- [ ] `M3-06` Credential prompt as a docked panel (airspace-safe, not a modal) — **M**
- [ ] `M3-07` Optional document master password (AES-GCM + PBKDF2/Argon2id) — **L** `[sec]`
- [ ] `M3-08` Log scrubbing: secrets never serialised, Serilog redaction policy — **S** `[sec]`
- [ ] `M3-09` Copy username/password to clipboard with 30 s auto-clear — **S**
- [ ] `M3-10` Credential profile manager UI — **M**
- [ ] `M3-11` Threat model doc: at-rest, in-memory, import parsing, clipboard — **M** `[sec]`
- [ ] `M3-12` Security review of all importers before import ships — **M** `[sec]` — `.rdg` reviewed and gated: XXE, out-of-band parameter entity, external DTD, entity expansion and nesting all covered by failing-if-broken tests. Re-run for each new importer.

## M4 — RDP engine

- [x] `M4-01` `IRemoteSessionHost` abstraction + fake impl so UI builds without RDP — **M** `[cut]` — lives in `Core`, so the tests reach it without a Windows target; nothing visual crosses the seam, and the fake makes refusal, cancellation and a dropped session into properties you set
- [x] `M4-02` COM interop for `MsTscAx`; detect `IMsRdpClient` 9/10/11 at runtime — **L** `[risk]` `[cut]` — there is no `IMsRdpClient11`; 11 was a coclass number, and the scriptable chain stops at 10. Every GUID is read from the type library in `mstscax.dll`, and the control is chosen by creating it rather than by reading the registry, because the newest coclass registered on a current Windows 11 cannot be created at all. The interfaces are dispinterfaces with no members, so there is no vtable to get wrong; everything else goes by name. Probing moves itself to an STA thread — the non-scriptable interfaces have no proxy, so an MTA probe reports no credential support on a control that has it
- [x] `M4-03` `WindowsFormsHost` embedding + airspace-safe layout rules — **L** `[risk]` `[cut]` — the rule is swap, never stack: `SessionSurface` collapses the session to show anything else, because in-window WPF content drawn over a hosted HWND is simply not visible. Popups, tooltips and context menus are the exception and are fine — they get their own top-level window. `AirspaceRules` checks placement on load rather than trusting it, since all three failures (ignored opacity and transforms, escaped clipping, and a layered window rendering nothing at all) are silent and none of them looks like a layout mistake
- [x] `M4-04` Settings mapper: model → `AdvancedSettings` (full property surface) — **L** — a plan, not a sequence of calls. Every write goes out late-bound, so none of it is checked by a compiler; a list can be, so `RdpSettingsMapper` builds one in `Core` — no COM, no Windows target, no control — and `RdpSettingsApplier` walks it. Everything worth getting wrong is in the list: which settings object a property lives on (the gateway is on `TransportSettings` and nowhere else), which number a mode is, and whether a redirection somebody switched off is actually sent. `ConnectToAdministerServer` falls back to `ConnectToServerConsole`, which is what `Alternatives` is for.

  The report turns on material versus not. A resolution that did not apply announces itself the moment the session draws; a clipboard redirection that did not get turned off announces itself never. So turning a redirection off is material and turning it on is not, silencing audio is and un-silencing is not, and every gateway write is, because a gateway that did not apply either fails the connection or quietly goes direct. `IsClean` and `IsSafe` are therefore different questions, and the notice stays silent about failures that do not matter. Unsupported and rejected are kept apart: the control being old and the control objecting are different conversations.

  Verified against the real control (`AdvancedSettings9` / `SecuredSettings3` / `TransportSettings4`): all 17 writes applied and read back as written, an unknown name reported `Unsupported`, a bad value reported `Rejected`, and a missing primary name applied through its alternative.

  Correction, from `M4-14`: the casing traps recorded here were overstated. Only `ColorDepth` is a real trap. `GatewayHostname` and `overallConnectionTimeout` differ from the model by case alone and dispatch lookup ignores case, so neither would ever have missed. A difference of letters is dangerous; a difference of capitals is cosmetic.

  Correction, from `M4-10`: passwords are no longer absent from the plan. The reason they were still holds — the write comes from the `SessionRequest`, never from `ConnectionSettings`, so saving a connection file cannot write a secret into it — and the entry carries `IsSecret`, which every printer of a write now respects
- [x] `M4-05` Connect / disconnect lifecycle state machine — **L** `[cut]` — the transition table lives in `SessionStateMachine` in `Core` and is written out in full in its tests, so a move that ought to be refused cannot quietly stop being refused. Two ways in: `MoveTo` throws, for moves Patchbay decides, and `TryMoveTo` returns false, for anything the control announces — a disconnect reported twice is the world repeating itself, not a bug. A drop by the far end is a disconnect and never a failure; offering a retry to someone who meant to log off is the mistake it exists to prevent. The `IRemoteSession` that drives a real control needs `M4-06` first
- [x] `M4-06` Event wiring: Connecting, Connected, LoginComplete, Disconnected, FatalError, LogonError — **M** — the six arrive as a `SessionSignal` and are read by `SessionSignalRouter`, which lives in `Core`, so the half where the mistakes are is testable without a Windows target or a server. Three things the event names hide. A disconnect is not news of a failure, or of a success: the same one arrives for a log off, a refused password, a pulled cable and a closed tab, and only the reason code and what the session was doing at the time tell them apart — the three the documentation calls "not an error code" are the whole of the ordinary set. A logon error ends nothing; the control keeps the connection and puts a logon screen up, which is exactly what makes `M4-10` possible and what tearing the tab down here would prevent. And the failure arrives *before* the disconnect that carries it, so the router remembers — otherwise someone whose password was refused is told "Disconnected". Also: negative logon codes are not all informational, which is the tempting rule; `-1` is access denied and `STATUS_LOGON_FAILURE` is `-1073741715`, and reading the sign alone swallows both. Sunk by DISPID read from the type library, because `OnLogonError` is 22 and sits between members numbered 21 and 29 — implicit ordering would have wired the disconnect notice to whatever was declared fourth. Verified against the real control: refused connect → 1800 → Failed, bad DNS → 260 → Failed, cancel mid-connect → 1 → Disconnected. `OnConnected`, `OnLoginComplete`, `OnLogonError` and `OnFatalError` need a server that answers and go on the `M4-17` matrix
- [x] `M4-07` `ExtendedDisconnectReason` → human-readable message table — **M** — mostly not a table, which is the finding. The codes are composed rather than enumerated, so 260, 516, 772, 1028, 1288 and 1540 all describe the same cannot-find-that-computer family, and a hand-written switch is either enormous or mistaken. `GetErrorDescription(disconnectReason, ExtendedDisconnectReason)` is Microsoft's own text for its own codes, already translated into whatever language Windows is running in — this machine answered in en-GB, "authorised" and "licence" included. Patchbay can do neither, so it asks.

  The exception is the ordinary ending, where the control is flatly wrong. Asked about reason 1, a disconnect this computer requested, it answers "An internal error has occurred"; the same for reason 2, somebody signing out. Those are the two commonest ways a session ends, so they never reach the control and `SessionReasons` answers them itself — checked against `SessionSignalRouter.IsOrdinaryDisconnect` by a test walking four thousand codes, because two places deciding the same thing is how they drift.

  Both halves of a reason are read together, always. Reason 3 alone is "your session has ended, possibly for one of the following reasons" and three guesses; reason 3 with extended reason 5 is "you have been disconnected because another connection was made to the remote computer". Passing one and not the other turns the answer back into the question. The strings were laid out for a message box and carry embedded newlines and doubled spaces, so they are collapsed on the way out.

  Logon errors are not disconnects and never go to `GetErrorDescription`, which would answer confidently about a different failure. Only the two codes pinned down get words (`-1073741715` a refused password, `-1` a refused account); the rest report their number, as do the fatal-error codes, because a plausible sentence about an unchecked code is worse than a number somebody can search for. `M4-10` earns more of the logon set. Verified against the real control: 13 checks covering the codes it knows better, the four it does not, the tidying, and the pair
- [x] `M4-08` Auto-reconnect: exponential backoff, attempt cap, cancel, visible countdown — **L** — the control already does half of this, better than Patchbay can. `EnableAutoReconnect` is on by default from `IMsRdpClientAdvancedSettings2` up, and a momentary transport loss makes the control rejoin the session it already has with the cookie the server issued: desktop, open windows and half-typed command intact, where a fresh connect gets a new session. So there are two layers covering different failures. The control's reaches a wireless blip; Patchbay's reaches a reboot, a gateway restart, or a laptop shut for an hour.

  Starting a sequence is narrow, continuing one is wide. Only a working session breaking starts one, because an attempt that never got anywhere has somebody watching it with a button underneath, and silently retrying a name that has never resolved only delays the truth. Once running, an outright failure continues it: a rebooting machine refuses connections for a minute before accepting one. Anything anybody decided stops it — a disconnect that was asked for, an attempt called off, a sign-out, and a session that ended with no stated reason.

  A refused sign-in is checked before the counting, so it stops a sequence already in flight. The case is the first reconnect after a drop reaching a machine whose password has since changed; without the check the remaining nine attempts lock the account out with nobody at the keyboard. It is also the one fact a transition cannot carry, which is why `IRemoteSession` grew `LastLogonError`. The counter resets on success, or a connection that drops once a fortnight and recovers every time is called exhausted after ten fortnights.

  Jitter is not decoration: a gateway restart takes every session through it down at the same instant, and without a spread they come back in lockstep at the machine that has just come up. Supplied as a sample rather than drawn inside, so the arithmetic stays a function of its inputs. Ten attempts at 5, 10, 20, 40 then 60 seconds is a little over seven minutes, which covers a reboot and stops short of hammering a decommissioned server.

  Decision, arithmetic and countdown live in `Core`; the shell owns only the clock, because a visible countdown is redrawn on the thread that draws. Each tab measures its own elapsed time — a shared stopwatch charged a countdown joining a clock already running for the part of the interval that passed before it existed, costing up to a second of its first wait, and the end-to-end harness caught it.

  `OnAutoReconnecting2` (34) and `OnAutoReconnected` (33) are sunk; `OnAutoReconnecting` (17) is not, because its third parameter is an `[out]` the control reads back to decide whether to keep trying, and answering it wrongly stops the reconnect silently. The newer event carries more anyway: the attempt cap, and whether this computer is the one that went offline. Neither ends anything, so both announce without a transition.

  Verified against the real control (`EnableAutoReconnect` written both ways, accepted both ways) and end to end against the fake: a break starting a countdown, the countdown connecting, a recovery clearing the count, a machine that never answers exhausting exactly the permitted attempts, cancelling, a refused sign-in making no attempt, a log-off making none, the setting off making none, and a tab closed mid-countdown taking its clock with it. The control's own reconnect firing needs a server that can be made to drop a live session and is on the `M4-17` matrix
- [x] `M4-09` Certificate warning UI: subject, thumbprint, expiry, trust-once — **M** `[sec]` — none of those four things can be built, which is the finding. The control never hands the container a certificate. Nothing in the type library of `mstscax.dll` (10.0.26100.8875) exposes one; the only member with the word in its name, `PublisherCertificateChain`, signs RemoteApp publishers and says nothing about the machine at the other end. The trust-once button is not Patchbay's to build either — it is in the control's own warning, with its own "don't ask me again" box, and the answer kept where `mstsc.exe` keeps it.

  Three things are left and all three are done. Whether the warning appears at all is `AuthenticationLevel`, which landed with `M4-04` (1 strict, 2 lenient). Saying a session is waiting on a person is new: `OnAuthenticationWarningDisplayed` (18) and `OnAuthenticationWarningDismissed` (19), both parameterless, read by the router without moving any state. Nothing has failed and nothing has ended; the attempt is paused on a dialog, and the dialog is inside the session's own window, which is where somebody looking at a tab reading "Connecting…" will not think to look. The dismissal does not say which way it was answered, so the router takes its sentence down and lets what follows speak.

  Reporting what was agreed fills `SessionVitals.Security`, which `M4-18` left `Unknown`. `AuthenticationType` is read-only on `IMsRdpClientAdvancedSettings6` with four documented values, and two of the mappings are why `RdpAuthenticationType` is a type rather than a cast. Kerberos means NLA, because Kerberos only reaches RDP through CredSSP and CredSSP is what NLA is. A certificate alone is reported as no more than TLS, even though NLA over NTLM outside a domain is indistinguishable from here — the field is read as an assurance, so it under-reports on purpose. Zero is read as "the engine did not say" rather than as legacy RDP security, because zero is also what the property reads before anything has connected; guessing wrong would put a red badge on every session, and an alarm that is always wrong is one people stop seeing.

  `OnReceivedTSPublicKey` (16) carries server identity and is deliberately not taken. It has an `[out]` the control reads back to decide whether to continue, so answering it wrongly refuses every server or waves every one through, silently, and it cannot be tested without a server. A public key is not a certificate either, so the dialog it would permit could say no more than the control's own. Undeclared, the control checks certificates the way `mstsc.exe` does.

  Verified against the real control: `AuthenticationType` present on `AdvancedSettings9`, a write to it refused, reading 0 before a connection, a session that never connected reporting no layer, and the sink still delivering `Connecting → Failed` in the control's own words after growing by two members. 36 checks, none failing. What a live session reports needs a server and is on the `M4-17` matrix
- [ ] `M4-10` Credential re-prompt on logon failure without tearing down the tab — **M** — `M4-06` holds the door open: a logon error moves no state, so the session is still connected and still showing its own logon screen when this lands. The engine half is built and verified; what is left is the panel, and that is `M3-06`.

  `SessionCredentials` is the sign-in for one attempt, deliberately outside both the node and the document, assembled at connect time from a profile or a prompt and thrown away with the attempt. It is a record, so two attempts with the same sign-in compare equal — the question a re-prompt has to answer before offering to try again, since reconnecting with what was just refused is not a retry and enough of those locks the account. The password is a `string` rather than a `SecureString` because the control takes a BSTR, so cleartext exists at connect time whatever it was held in beforehand. Shortening its life is `M3-03`.

  The hazard was not the COM call, it was the printing. A plan is a diagnostic object: inspected, printed in a harness, shown when a write is refused, and once `M4-16` lands written to a log file somebody attaches to a ticket. A password travelling as an ordinary `RdpSettingWrite` would appear in all four, through adding one more row to a table where every other row is safe to print. So `IsSecret` sits on the write and `ToString` redacts to a fixed-width mask — fixed width because a variable one hands over the length. The harness was doing exactly this, formatting `entry.Write.Value` by hand and reaching past the redaction, and is fixed.

  No vtable was needed. Every account of RDP credential passing points at `IMsTscNonScriptable`, which is `IUnknown`-derived and would mean transcribing one by hand, and it does carry `ClearTextPassword` at DISPID 1. The type library says it is avoidable: the same property is on every generation of `IMsRdpClientAdvancedSettings` from the first, at DISPID 186, put only, reachable late-bound. Written to the live control it applies. Not material — a password that did not arrive produces a logon prompt, which is visible and fixable by the person looking at it, and nothing is left less protected than was asked for.

  The attempt now outranks the document for who is signing in. That matters in one situation: somebody refused, asked again, and typing a different account, where sending the stored name back would retry what was just turned down. The domain travels with the user name and not on its own, because an empty domain is how a local account is expressed and falling back to the document's realm fails in the way that most looks like a bad password.

  `LogonFailure` answers whether asking again is any use, which is a different question from what went wrong and the only one that changes behaviour. Ten NTSTATUS codes, read rather than guessed; `M4-07`'s rule against inventing sentences for unchecked codes still holds, and nothing here produces wording. A wrong password and a refused account are worth asking about; locked, disabled, expired, out-of-hours and barred-from-this-machine are not. Expired and must-change count as unusable rather than wrong, which is the judgement call: what is on file may be correct and what is needed is a change at the far end. Anything unpinned stays `Unknown` and still permits asking, since a person typing once more is not the lockout risk an automatic retry loop is.

  `SessionSignalRouter.IsAwaitingCredentials` is set the moment the logon error arrives, while the session is still up behind its own logon screen, not after the disconnect — waiting for the ending is what "without tearing down the tab" rules out, because by then what is on offer is a fresh connection wearing a retry button. No transition either way. Cleared by signing in, by the next attempt and by the end of the session; the held failure is separate and survives, so somebody who ignores the prompt and loses the connection is still told the password was refused.

  Verified against the real control: `ClearTextPassword` applied through the scriptable advanced settings, the attempt's account reaching `UserName` instead of the document's, and the secret absent from both the report and the log file the run writes. 41 checks, none failing. What remains is the docked panel (`M3-06`, which needs `M3-01`) and the tab-side reconnect that hands new credentials to a new request
- [x] `M4-11` RD Gateway settings + gateway credential source — **L** — the gateway lives on `TransportSettings` and nowhere else, and every write in it is material: a gateway that did not apply either fails the connection or quietly goes direct to a machine somebody meant to reach *through* one, which is the worse of the two because it looks like success. `GatewayCredsSource` is not contiguous — password 0, smart card 1, and any is 4, not 2 — so it is written out as a switch rather than cast off the enum, the same treatment `ProxyMode` gets. `GatewayCredSharing` is declared `UI4` despite reading like a flag, so it goes out as a number. And naming a gateway account while `GatewayUseSameCredentials` is on would write two contradictory instructions and let the control choose, so the user name and domain are skipped in that case, which is the default one. Verified against the real control: all six gateway writes applied, including `GatewayUsername` and `GatewayDomain` on `TransportSettings4`
- [x] `M4-12` Console / admin session flag — **S** — `ConnectToAdministerServer`, with `ConnectToServerConsole` behind it in `Alternatives`, which is exactly the case that mechanism was built for: the property was renamed between control generations and the older name is what an older control answers to. Applied and verified on the real control
- [x] `M4-13` Redirection: clipboard, drives, printers, audio, smart card, ports, USB — **L** — nine redirections, and the materiality rule from `M4-04` decides every one of them: turning a redirection off is material and turning it on is not, because a drive that failed to be shared is noticed within a minute and a drive that failed to be *un*-shared is noticed never. Two surprises from the type library rather than from memory: `AudioCaptureRedirectionMode` is declared `BOOL` despite the word *mode* in its name, and `RedirectPOSDevices` shouts its middle three letters. The second turned out not to matter — see `M4-14` — but it is the type library's spelling and the plan matches it. Microphone capture and audio quality sit here too; playback stays on `SecuredSettings` where `M4-04` put it. All nine verified applied against the real control
- [x] `M4-14` Experience and performance flags, bitmap caching, compression — **M** — `PerformanceFlags` is a bit field, and six of its eight flags turn something off while two turn something on, so reading it the obvious way gets six of eight exactly backwards and produces a desktop that is the photographic negative of what was asked for. That inversion is `RdpPerformanceFlags` in `Core` with one test per flag, rather than a line of bit-twiddling buried in the mapper. `ConnectionQuality.Detect` writes `BandwidthDetection` on and nothing else; any other answer turns detection off and states `NetworkConnectionType`, because asking the control to both measure and be told is asking it to ignore one of them. Nothing in this group is material — every one of these is visible the instant the session draws. Verified on the real control: `PerformanceFlags` read back as 399, the exact mask expected. The casing traps recorded here and in `M4-04` were overstated and the notes have been corrected: dispatch lookup resolves names case-insensitively — measured, not assumed, since `redirectdrives`, `RedirectPosDevices` and `GatewayUserName` all resolve — so the capitals are the IDL's and not a requirement. What does bite is a difference of *letters*: `BitmapPersistence` sits beside `BitmapPeristence`, which is missing its second *s*, and both are still present on a control from this year, while an undeclared name is rejected outright with `DISP_E_UNKNOWNNAME`
- [x] `M4-15` Keep-alive and idle timeout handling — **S** — `keepAliveInterval` is on the advanced settings, reads 0 out of the box, and takes milliseconds, which the `.rdp` file setting of the same name does not — that one is in minutes, so the unit is the entire content of the setting and getting it wrong is wrong by a factor of sixty thousand, in one direction a flood and in the other a silence. Verified reading back as 45000 from a request of 45 seconds. The idle half arrives as `OnIdleTimeoutNotification`, DISPID 13, no parameters — and the control raises it and then does nothing at all. A host that only listens leaves a session somebody asked to be closed sitting open; a host that listens and *reports* draws a disconnect message over a live desktop. So the router announces and `RdpRemoteSession` closes the session itself, through the ordinary `DisconnectAsync` rather than by moving the state, which matters because that routes the ending through `Disconnecting` and `M4-08` leaves endings somebody asked for alone. An idle timeout that reconnected itself would be a timeout in name only. The call is posted back through the pane rather than made in place, because it arrives inside the control's own call frame and asking the control to tear itself down while it is still on the stack is asking for the crash
- [ ] `M4-16` Per-session logging with a correlation id — **S**
- [ ] `M4-17` Manual test matrix: Win10 / Win11 / Server targets, gateway on-off, NLA on-off — **M** — one row on it changes code rather than confidence: connect with NLA on, with NLA off, and with legacy RDP security, and write down what `AuthenticationType` reports each time. If a live legacy session really does report 0, `RdpAuthenticationType.ToSecurity` maps it to `RdpLegacy` and the status bar gets its red case (`M4-09`)
- [x] `M4-18` Real `IRemoteSessionHost` / `IRemoteSession` over the control — **L** `[risk]` — owed since `M4-01` and never given an id of its own. Mostly assembly: settings from `M4-04`, announcements read by `M4-06`, transitions from `M4-05`, wording from `M4-07`, all of it already tested in `Core`. Three things are new.

  A session cannot connect until something has given it a window. The control has no COM object until its handle exists and no handle until it is in a window on screen, and the shell adds a tab and connects it in one go while WPF realises the tab a moment later. So `ConnectAsync` waits for the handle, and waiting is what makes it work: awaiting yields the dispatcher, which is what lets the realisation happen. It gives up after ten seconds and says the session never appeared on screen.

  An event-driven control is made awaitable by a `TaskCompletionSource` the state machine completes. Connected returns, Failed throws, Disconnected cancels — an attempt somebody called off is not a failure, and reporting one would offer a retry to a person who changed their mind. Cancellation goes out through the control's own `Disconnect` rather than being resolved locally, so the ending arrives as an ordinary disconnect. Settings are applied before `Connect` and never after, because the control reads most of them once.

  `IHostedSessionView` is the seam for the window. `Core` may not name a WinForms control, so the session declares it in `Patchbay.Rdp` and the shell is the only place the two meet; a session that does not implement it is the fake, not a broken one. `SessionSurface` binds to the session and finds its own window rather than something outside handing one over at a moment nobody can name. Vitals report the resolution and leave the rest unknown — filling either from the request is the one thing `M5-17` says the status bar must never be told.

  Verified end to end against the real control: the real host selected, a tab opened, its window attached and handled, a connect to an unresolvable name ending in Failed with the control's own sentence rather than a number, all 12 settings applied with nothing material refused, the sizing toggle reaching the pane, and the tab closing without taking anything with it. A session that actually comes up needs a server and is on the `M4-17` matrix

## M5 — Session UX

- [x] `M5-01` Tab strip: open, activate, close, middle-click close — **M** `[cut]` — the two rules that can be wrong live in `SessionWorkspace` in `Core`, where they are tested: closing the tab in front promotes the one that slid into its place, or the last one if it was at the end, because leaving nothing selected shows an empty pane beside a strip full of live sessions; and opening a machine that is already open brings its tab forward rather than starting a second session, since two sessions to one server usually means the first was forgotten and Windows Server frequently ends it for you. Nodes match by id and not by host name, so two entries pointing at one machine keep their own tabs — they differ in credentials or gateway, which is why both exist. There is a permanent Connections tab, so closing the last session never leaves an empty window, and a tab outlives its session so a drop still has somewhere to say why and a button to try again. Deliberately not a `TabControl`: it keeps one content presenter and rebuilds it on every switch, which for a hosted session means destroying the window the RDP control lives in each time someone glances at another tab. Every session stays in the visual tree and only the active one is visible. Sessions come from `IRemoteSessionHost`, so this runs against the fake today and the real engine by changing one argument; the fake says so in amber on every session it draws
- [ ] `M5-02` Tab reorder by drag — **M**
- [ ] `M5-03` Tab overflow: scroll plus dropdown list — **M** — the strip is in a horizontal `ScrollViewer` with no bar and no wheel binding, so a long strip clips today. Both halves of this task are still owed
- [ ] `M5-04` Detach tab to its own window and re-dock — **XL** `[risk]`
- [ ] `M5-05` Full-screen toggle with a persistent escape affordance — **L** `[risk]`
- [ ] `M5-06` Focus capture and release: Ctrl+Alt+Enter, click-in / click-out semantics — **L** `[risk]`
- [ ] `M5-07` Keyboard passthrough config (`ApplyWindowsKey`, focus-based) — **L** `[risk]`
- [ ] `M5-08` Ctrl+Alt+End → Ctrl+Alt+Del, plus a send-keys menu — **M**
- [x] `M5-09` Smart sizing toggle — the v1 default — **M** `[cut]` — a session keeps the resolution it negotiated however the window is resized afterwards, so the picture and the space for it are rarely the same size, and there are exactly two things to do about that: scale it, or scroll it. `SessionScaling` lives in `Core` and works out both, so the arithmetic has tests rather than a resize handler. The part worth knowing is the letterbox — the control's own `SmartSizing` scales the desktop to fill whatever window it is given, whatever shape that window is, so handing it the whole pane gives back a stretched desktop, and nothing about a stretched desktop announces itself as wrong. It gets the largest rectangle of the session's own shape instead, centred, and then the scaling cannot distort. It enlarges as well as shrinks, because a 1024×768 session drawn at its own size in a maximised tab reads as a bug rather than as a choice. The scrolling happens on the WinForms side of the airspace boundary, in `RdpSessionPane`, because a hosted window inside a `ScrollViewer` neither scrolls nor clips and `AirspaceRules` says so by name; scrollbars are taken away and the fit worked out again inside the same layout pass, since WinForms otherwise leaves a bar sitting under a picture that stopped needing one. The toggle is a button beside the strip — not over the session (M4-03), and not a keyboard shortcut either, because a live session takes the keyboard until M5-06 and M5-07 say otherwise; mstsc puts the same toggle in its system menu for the same reason. It belongs to the tab and is never written back to the document: it is a way of looking at a session, not a change to the connection. What it is not is a resolution change — text at sixty per cent is blurred text, not smaller text, which is the whole of M5-10's case. Verified against the real control on both settings, including that `SmartSizing` reaches the COM object and comes back changed; how it looks on a session that has a picture goes on the `M4-17` matrix
- [ ] `M5-10` Dynamic resolution via `UpdateSessionDisplaySettings` + resize debounce — **XL** `[risk]` — this is the task M5-09 is standing in for. Scaling is not a resolution change: no amount of it gives the far end more room to put things
- [ ] `M5-11` Per-monitor DPI v2: manifest, hosted-HWND scaling, monitor-change handling — **XL** `[risk]`
- [ ] `M5-12` Multi-monitor spanning (`UseMultimon`) — **L**
- [ ] `M5-13` Paste text into session (type-through) — **S**
- [ ] `M5-14` Screenshot session to file or clipboard — **S**
- [ ] `M5-15` Connect all / disconnect all within a group — **S**
- [ ] `M5-16` Session context menu: reconnect, screenshot, properties, full screen — **S**
- [x] `M5-17` Status bar: host, resolution, security layer, gateway, latency — **M** `[cut]` — one rule runs through all five: the engine when it has spoken, the configuration when it has not, and muted whenever it is the second. A resolution is muted until the far end agrees to one, a gateway until a session has really gone through it, and both stop being muted the moment they become facts. Showing the configured value as though it were negotiated would hide the most useful thing a status bar can say, which is that what you asked for is not what you got.

  All five fields are always present, dashes and all. A bar whose fields come and go is harder to read than one with gaps, because the eye learns where a value lives.

  Security earns a colour and is the exception to the rule: before connecting it is a dash rather than the configured level, because it is the one place a muted value could be read as an assurance. TLS without NLA is amber, since the logon happens inside the session rather than before it; legacy RDP security is red, being encrypted to whoever answered. The resolution carries a percentage only when it is not a hundred. "Direct" is a fact rather than an absence, and a gateway set to `WhenDirectFails` says in its tooltip that it does not know whether it was used.

  `SessionVitals` is what the engine reports, with an event of its own because latency moves while state does not, and it is cleared by every transition away from Connected — a bar still reporting 1920 × 1080 about a session that ended two minutes ago is not stale, it is wrong. The fake reports TLS rather than the best of the three on purpose: a fake that always claims what everyone wants hides the field whose job is to notice when something weaker was agreed. Verified against the real window rather than the XAML, 43 cases on the line and 11 on the fake
- [ ] `M5-18` Latency probe + sparkline — **M** — the field and its thresholds are already there; this is the measuring. `SessionVitals.Latency` is where the answer goes and `VitalsChanged` is how it gets out, so nothing above needs changing. `OnNetworkStatusChanged` on the control carries a round trip and is the obvious source — DISPID 32, `(unsigned long qualityLevel, long bandwidth, long rtt)`, read from the type library while wiring `M4-08`; an ICMP probe measures a different thing from what the session experiences and should not be mistaken for it

## M6 — Thumbnail grid (v1.1)

> Held back deliberately. Twenty live scaled ActiveX controls is a performance
> problem, and it is not worth solving in week one. `M6-01` decides the approach
> before anything else here is committed to.

- [ ] `M6-01` Spike: live scaled control vs periodic bitmap capture — benchmark at 5 / 10 / 25 sessions — **L** `[spike]` `[risk]`
- [ ] `M6-02` Grid layout with virtualisation — **M**
- [ ] `M6-03` Capture pipeline + throttle, pause when the view is hidden — **L**
- [ ] `M6-04` Thumbnail size slider and density control — **S**
- [ ] `M6-05` Click through to session, hover preview — **S**
- [ ] `M6-06` Group-scoped grid and all-sessions grid — **S**
- [ ] `M6-07` Offline and error thumbnail states — **S**
- [ ] `M6-08` Performance budget + regression check in CI — **M**

## M7 — Release

- [ ] `M7-01` Settings dialog: general, appearance, shortcuts, security, advanced — **L**
- [ ] `M7-02` Keyboard shortcut map with user rebinding — **L**
- [ ] `M7-03` Window and layout state persistence — **M**
- [ ] `M7-04` First run: welcome, offer import when `.rdg` or mRemoteNG config is detected — **M**
- [ ] `M7-05` About box, version, third-party licences — **S**
- [ ] `M7-06` Accessibility pass: `AutomationProperties`, keyboard-only path, focus visuals, contrast — **L**
- [ ] `M7-07` High-contrast theme support — **M**
- [ ] `M7-08` Localisation scaffolding (`.resx`), en-GB baseline — **M**
- [ ] `M7-09` README, screenshots, feature list — **M**
- [ ] `M7-10` Installer: MSIX preferred, per-user, no admin required — **L**
- [ ] `M7-11` Code signing certificate + signed binaries — **M**
- [ ] `M7-12` Auto-update channel (Velopack) — **L**
- [ ] `M7-13` CI: GitHub Actions build, test, package, release on tag — **M**
- [ ] `M7-14` Telemetry decision — default to none, and document that — **S**
- [ ] `M7-15` Opt-in crash reporting — **M**
- [x] `M7-16` Licence choice + third-party attribution file — **S** — GPL-3.0-or-later, copyright TiredEyesHam. The obvious answer is the wrong one here: MIT is the most-used licence on GitHub and names selling in its own text, so a permissive licence is precisely what somebody would need in order to close this and charge for it. No open-source licence forbids selling — free redistribution is part of the definition, and GPL permits charging like the rest. What copyleft stops is the closed fork: anything distributed that is built from Patchbay arrives with its source under the same terms.

  A source-available non-commercial licence (PolyForm) was considered and rejected. It stops commercial use outright at the price of no longer being open source, and of most corporate desktops being unable to touch it — for a tool aimed at corporate desktop fleets that costs more than it protects. Holding the whole copyright keeps two things open: this can be relicensed until the first outside commit lands, and a commercial licence can be sold alongside the public one.

  `LICENSE` is the FSF text verbatim, which is what GitHub's detection reads. `THIRD-PARTY-NOTICES.md` is the other half: `CommunityToolkit.Mvvm` and .NET are MIT and are redistributed, which the GPL permits in that direction and not the reverse; the four test packages reach no released binary; `mstscax.dll` is neither, being a Windows component reached over COM and never shipped, covered by the GPL's exception for major components of the operating system. Every interface id and property name in `Patchbay.Rdp` was read from its type library, which is a fact about the control rather than a copy of it.

  The trap this heads off is the mRemoteNG importer (`M1-16`): mRemoteNG is GPL-2.0, reading its file format is fine, and copying its source would bind this repository to terms it did not choose, because GPL-2.0 and GPL-3.0 are not compatible in that direction. Not done deliberately: per-file licence headers, which would put a notice at the top of 140 files. The interactive notice the GPL suggests belongs with an About box, which does not exist yet
- [ ] `M7-17` v1 release checklist and smoke-test script — **M**

## M8 — Post-v1

- [ ] `M8-01` SSH / terminal tabs (ConPTY + SSH.NET, or the Terminal control) — **XL**
- [ ] `M8-02` VNC support — **XL**
- [ ] `M8-03` Hyper-V VMConnect / console sessions — **L**
- [ ] `M8-04` Azure Bastion and AVD integration — **XL**
- [ ] `M8-05` KeePass / Bitwarden / 1Password credential providers — **L**
- [ ] `M8-06` Azure Key Vault and CyberArk enterprise vaults — **XL**
- [ ] `M8-07` Shared team connection file with a per-user credential overlay — **XL**
- [ ] `M8-08` SSH tunnel / jump host chaining for RDP — **L**
- [ ] `M8-09` Port forwarding manager — **M**
- [ ] `M8-10` Host health polling (ICMP / TCP 3389) driving real status dots — **M**
- [ ] `M8-11` Active Directory / OU discovery and sync — **L**
- [ ] `M8-12` Tags and saved smart views — **L**
- [ ] `M8-13` Command palette (Ctrl+Shift+P) — **M**
- [ ] `M8-14` Session recording to video — **XL**
- [ ] `M8-15` CLI `patchbay connect WEB-PRD-01` + `patchbay://` protocol handler — **M**
- [ ] `M8-16` Scripting / plugin API — **XL**
- [ ] `M8-17` Wake-on-LAN, remote restart, service actions — **M**
- [ ] `M8-18` Per-host notes, attachments, runbook links — **M**
- [ ] `M8-19` Cross-platform via FreeRDP behind an Avalonia shell — **XL**
- [ ] `M8-20` Connection templates and CSV provisioning — **M**

---

## The first cut

Tasks marked `[cut]` are the walking skeleton — a usable app with full CRUD and
one real session in a tab. Roughly four evenings. The session half of that
promise landed with `M4-18`, which had no id of its own until it was done.

| Day | Tasks |
|-----|-------|
| 1 | `M0-01`→`M0-03`, `M0-06`, `M1-01`, `M1-02`, `M1-04`→`M1-06` |
| 2 | `M2-01`, `M2-02`, `M2-04`, `M2-06`→`M2-08`, `M2-13`, `M2-18`, `M2-19` |
| 3 | `M4-01`→`M4-03`, `M4-05`, `M5-01`, `M5-09` |
| 4 | `M3-02`, `M1-12`, `M5-17` |

Day 3 is the one that can swallow the whole schedule on its own. Connecting is
easy; resizing sanely is not. Ship smart sizing and leave `M5-10` alone.

## Sequencing rules

1. `M4-01` before any other M4 task — the fake host is what lets M2 be built and tested without touching COM.
2. `M1-12` does not ship until `M3-12` passes. RDCMan was pulled in 2020 over an XXE in exactly this parser.
3. `M6-01` gates the rest of M6. If neither approach hits the budget, M6 gets cut, not extended.
4. `M5-10` and `M5-11` are the two most likely tasks to overrun. Neither blocks v1.
5. `M0-10` before `M0-12` — the custom title bar needs tokens to exist first.
