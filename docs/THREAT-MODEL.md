# Patchbay — threat model

What Patchbay protects, what it does not, and why. Written for whoever is
about to change an importer, add a store, or put something on the clipboard.

Read this before changing `Patchbay.Core/Import`. Sequencing rule 2 in the
backlog exists because RDCMan was pulled from download in 2020 over a flaw in
exactly that code path.

## What this is

A connection manager holds a list of machines somebody can reach and, if they
ask it to, the passwords that open them. That makes the document a map of an
estate and a set of keys to it, in one file, on a laptop.

Two properties are worth stating plainly because everything below follows from
them:

- **A saved password is recoverable by design.** It has to be, or the session
  could not use it. Nothing here is a one-way hash; the question is only who
  can reverse it and where.
- **Patchbay cannot defend against code running as the signed-in user.** Data
  protection serves that user, the RDP control takes a plaintext BSTR, and the
  clipboard is readable by anything on the desktop. An attacker at that level
  has already won, and pretending otherwise leads to security theatre.

## Who is being defended against

| | In scope | Why |
|---|---|---|
| Someone who obtains the document file | yes | Backups, sync folders, a stolen laptop, a support ticket attachment |
| Another user on the same machine | yes | Shared workstations, service accounts |
| A hostile file offered for import | yes | `.rdg` files circulate as "here are the servers" |
| A hostile or compromised RDP server | partly | It sees what the session sends it, which is the point |
| Code running as the signed-in user | **no** | Out of reach, see above |
| A user with local administrator rights | **no** | Can read another user's DPAPI store |

## At rest

The document is JSON at a path the person chooses, default
`connections.json`. It holds host names, addresses, ports, gateway names,
account names, domains, and protected password blobs. It is **not** encrypted
as a whole; individual secrets are.

`connections.json` is in `.gitignore`. A connection document is a map of an
estate — do not commit one, and do not attach one to an issue.

A saved password is written as `pb1:<scheme>:<base64>`. The marker earns its
keep three ways: it separates a blob from a password that happens to look like
base64, it names which store can open it so a document can hold blobs from
more than one, and it lets a file written by a later version be refused
politely rather than reported as corrupt.

The only scheme today is `dpapi`, which is Windows data protection scoped to
`CurrentUser` with a fixed application entropy. The consequences are worth
being blunt about:

- A blob does not travel. Another machine, or another Windows account on the
  same machine, cannot read it. That is deliberate, and
  `SecretUnprotectStatus.Unreadable` exists to explain it rather than report
  the file as damaged.
- The entropy is a constant compiled into the binary and is **not a secret**.
  It buys one thing: another program running as the same user cannot open a
  Patchbay blob by handing it to DPAPI and seeing what falls out. It has to be
  written against Patchbay specifically.
- A local administrator can read another account's DPAPI store. User scope is
  not a boundary against them.

A document master password (`M3-07`) is the answer to the last two, and is not
built. Until it is, the honest summary is that saved passwords are protected
against the file moving and against nothing else.

Writing is atomic — a temporary file, then a replace — with five previous
versions kept. **Backups inherit the same exposure as the document**, which is
worth remembering before pointing the file at a synced folder.

The log is the other file Patchbay leaves on disk, in
`%LOCALAPPDATA%\Patchbay\logs`. Local rather than roaming, so it does not
follow a profile share, and seven days of it are kept. Retention is a security
setting rather than a disk one: once `M4-16` lands these files say which
machines were connected to, as which account, and when, which is the same map
of an estate the rest of this document is about. What they never hold is a
password — see below.

Protection failing is never a reason to store plaintext. `UnavailableSecretProtector`
throws rather than falling back, because the fallback is invisible: nothing on
screen changes, and the only difference is a cleartext password in a file that
gets backed up. Where data protection does not work, saving is not offered at
all rather than offered and then refused.

## In memory

**A .NET `string` cannot be erased.** It is immutable, it may be interned, and
a compacting collection can copy it elsewhere and leave the old bytes behind
with nothing pointing at them. So a password that arrives as a string at nine
in the morning is still legible in a memory dump at five, and no code in the
process can change that.

`Secret` (`M3-03`) is what Patchbay holds instead. The bytes live in a buffer
from the pinned object heap, which the collector never moves — pinning is not
about interop here, it is that a buffer which has been moved cannot be erased,
because erasing writes over where it is and not over where it has been.
Verified rather than assumed: across a forced compacting gen-2 collection an
ordinary array moved and a pinned one did not.

The bytes are UTF-8, which is what `M3-02` already protects and stores.
Changing that would make every password saved by an earlier version
unreadable.

Where the plaintext is erased:

- A password read out of the store never becomes a string on the way. It goes
  from the protector's decrypted bytes straight into a `Secret`.
- A session erases the sign-in it was given when it ends. That is the
  longest-lived copy in the application, because a tab can be open all day.
- A session handed a different sign-in after one was refused erases the
  refused one immediately.
- The credential manager erases what was typed as soon as it is saved.

**Erasing destroys the plaintext and not the identity.** A `Secret` keeps a
fingerprint — HMAC-SHA-256 under a key generated fresh each time the process
starts — so it can still answer whether something equals it after the password
itself is gone. That is what lets a re-prompt go on refusing to resubmit the
password the far end just rejected without keeping that password anywhere.
The per-process key means such a fingerprint cannot be looked up in a table of
hashed common passwords, or matched against one from another run.

What is not fixed, and cannot be here:

- The RDP control takes its password as a BSTR, so a `string` has to exist at
  the moment of connecting. `RevealAsString` is the one call that makes one,
  at the dispatch site, and the string it returns cannot be taken back.
- `PasswordBox.Password` hands out a fresh string on every read, and WPF owns
  those. Binding it would be worse still, keeping the plaintext in the binding
  engine for as long as the panel is up, so code-behind pushes it across
  instead — but the strings WPF made are not reachable.
- Nothing locks the pages out of the swap file. That needs `VirtualLock`,
  which is Windows-only and unsafe, and is not built.

The manager can set and forget a password and cannot read one back. That is
deliberate: a screen that displays stored passwords is a screen that will be
asked to, by whoever is standing behind the person using it.

## Printing, logging and diagnostics

A settings plan is a diagnostic object. It gets inspected, printed in
harnesses, shown when the control refuses something, and — once `M4-16` lands
— written to a log file that people attach to tickets. A password travelling
through it as an ordinary value would appear in all four, and not through
anybody's mistake: through adding one more row to a table where every other
row is safe to print.

So `RdpSettingWrite.IsSecret` sits on the entry and `ToString` redacts to
`SecretNames.Mask` — fixed width, so the length does not leak either. Every
type that could carry a secret overrides `ToString`: `SessionCredentials`,
`CredentialProfile`, `CredentialPrompt`, `SecretUnprotectResult`.

**A record's generated `ToString` prints every property.** That is the most
likely way a password reaches a log file, and it arrives through a line of
code nobody wrote. Any new type holding a secret must override it —
`ArchitectureTests.Anything_holding_a_secret_overrides_ToString` fails if one
does not, so this is a gate rather than a request.

The check behind that gate has to ask whether somebody *wrote* the
`ToString`, not whether one exists. A record has one synthesised onto the type
itself, so "the type declares a `ToString`" is true of every record in the
codebase including the one printing `Password = hunter2`. The synthesised
member carries `CompilerGeneratedAttribute` and a written one does not, and
that is the only thing separating them. `SecretRedactingPolicy.PrintsItself`
is where the question is asked, and both the gate and the redaction policy
call it, so they cannot come to different answers.

### Logging (`M0-07`, `M3-08`)

A value reaches a sink three ways, and each needs its own answer:

| Route | What stops a secret |
|---|---|
| `{Thing}` — rendered with `ToString` | the type's own override, held to by the gate above |
| `{@Thing}` — destructured by reflection, never calls `ToString` | `SecretRedactingPolicy` |
| `{Password}` — a bare value in a named hole | `SecretRedactingEnricher` |

The policy hands Patchbay's own types back to their `ToString` where they have
one, and otherwise destructures them with secret-named members masked. The
enricher works on names alone and walks into structures, sequences and
dictionaries, so a password under a `Password` key in a parsed file is masked
too. Both read one list, `SecretNames.Telltale`, which the architecture gate
also reads.

`PatchbayLog.Create` is the only way to build a Patchbay logger, and it fits
both before the caller can add a sink. A `RedactSecrets()` extension that
callers were expected to remember would be the same code and a worse control:
the failure would be a missing line, and the symptom would be a log that reads
perfectly well and has a password in it.

**A secret concatenated into the message itself cannot be redacted.**
`logger.Information("password " + password)` makes the password part of the
template text, and only holes are still values by the time an enricher sees
the event. That one is a review question, and the fix if it ever bites is a
Serilog-aware analyser in the build rather than anything at run time.

## Import parsing

Every reader of a file someone else produced goes through `SafeXml`:
`DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`,
and a document size bound. Depth is bounded separately by the walker at
`SafeXml.MaxDepth` (64), because deep nesting is legal XML and a few thousand
levels turns a recursive parse into a stack overflow the process cannot
catch.

CVE-2020-0765 was an XXE in this exact file format. A malicious `.rdg` could
read files off the machine that opened it and post them elsewhere. The tests
in `RdgImporterSecurityTests` fail if any of the settings above is removed;
they are the gate, not the documentation.

**Imported passwords are counted and never decrypted.** RDCMan's blobs are
DPAPI-protected to whoever saved them, so reading one is usually impossible
and always somebody else's decision. The importer reports how many it saw and
imports none.

`M3-12` re-runs this review for every new importer. `M1-12` does not ship
until it passes.

## Clipboard

The clipboard is readable by every process on the desktop, so a password on it
is a password published. Patchbay will put one there — it is the only way past
a logon screen that credential injection did not reach — and takes it off
again after thirty seconds.

**The countdown is the smaller half of this.** Since Windows 10 1809 the
clipboard is not one slot. What is copied goes into clipboard history, which
survives being cleared and stays readable from Win+V for the rest of the
session, and it is uploaded to the cloud clipboard and pushed to the person's
other machines if they have that turned on. A timer against the current slot
is no defence against either. Both are opted out of by putting extra formats
on the data object — `CanIncludeInClipboardHistory`,
`CanUploadToCloudClipboard` and `ExcludeClipboardContentFromMonitorProcessing`
— which is a property of the copy and not something the countdown can do.
Verified against the real clipboard: all three formats are present on what
comes back out of it.

Clearing happens only if the clipboard has not changed since, which is checked
with the Windows clipboard sequence number rather than by reading the contents
back. Reading them back would mean holding the password to compare against,
and it would be wrong anyway, since two copies of the same text are
indistinguishable and only one of them is Patchbay's. A clear that fails
because another process is holding the clipboard open is retried rather than
given up on, and if it keeps failing it says so.

A user name gets none of this. It is not a secret, it is in the connection
document in the clear, and keeping it out of clipboard history would cost
somebody a feature they use in exchange for nothing.

Copying is offered from a session and not from the credential manager.
Patchbay has already sent that password to that server, so putting it on the
clipboard to paste into the same server's own logon screen reveals nothing it
has not already done. A manager that hands back saved passwords is a different
claim, and `M3-10` decided against it.

Clipboard redirection into a session is a different question and is a
per-connection setting. Turning it **off** is material and turning it on is
not: a redirection that failed to be disabled is invisible, and somebody
carries on believing the opposite of what is true.

## The session itself

Server authentication is a setting, and the control owns the warning dialog.
Patchbay chooses whether the warning appears at all (`AuthenticationLevel`)
and reports what was actually agreed (`AuthenticationType`), but it never sees
a certificate — nothing in the control exposes one — so it cannot draw a
better dialog than the control's own.

`SessionVitals.Security` under-reports on purpose. A certificate alone is
called TLS even though NLA over NTLM is indistinguishable from the client
side, and an authentication type of zero is called unknown rather than legacy
RDP security, because zero is also what an unconnected control returns. A
badge that is always wrong is one people stop reading.

## Known gaps

| Gap | Item |
|---|---|
| No document master password; DPAPI is the only scheme | `M3-07` |
| A password handed to the RDP control must be a `string` | not fixable at this layer |
| Secret buffers are not locked out of the swap file | `VirtualLock`, not built |
| No Credential Manager store | `M3-04` |
| A secret concatenated into a message template is not redactable | review, not run time |
| No signed release, so no way to verify what you ran | `M7` |

None of these is a reason not to use Patchbay on a machine you already trust.
All of them are reasons not to treat the document as safe to hand around.
