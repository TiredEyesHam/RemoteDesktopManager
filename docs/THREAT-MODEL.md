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
| A hostile file offered for import | yes | `.rdg` and `confCons.xml` files circulate as "here are the servers", and a `.rdp` arrives by email |
| A hostile or compromised RDP server | partly | It sees what the session sends it, which is the point |
| Code running as the signed-in user | **no** | Out of reach, see above |
| A user with local administrator rights | with a master password | Without one they can read another user's DPAPI store, and Credential Manager is no different |

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

There are three schemes. `dpapi` is Windows data protection scoped to
`CurrentUser` with a fixed application entropy, and it is what a document uses
until somebody says otherwise. `wincred` is Windows Credential Manager, where
the document holds a name and Windows holds the password (`M3-04`). `master` is
a key derived from a password the person chooses, and it takes precedence over
either while it is on (`M3-07`).

Which one writes the next password is `credentialStore` in the document, and it
is not a claim about what the document already contains. Reads dispatch on what
each blob says it is, so a document is routinely mixed and stays that way — a
blob this Windows account cannot read is left exactly where it is rather than
being moved or overwritten. A document naming a store this build does not have
refuses to save rather than falling back to one it does.

### `dpapi`

The consequences are worth being blunt about:

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

The honest summary is that a `dpapi` blob is protected against the file moving
and against nothing else.

### `wincred`

Windows Credential Manager, as a generic credential per saved password, target
name `Patchbay/{document}/{entry}`.

The cryptography is the same. A Credential Manager entry is protected by the
same user-scoped Windows data protection a `dpapi` blob is, so nothing in the
list above stops being true: a local administrator still reads it, code running
as the signed-in user still reads it, and it still does not travel. Choosing
this over `dpapi` on grounds of strength would be choosing at random.

What changes is where the ciphertext is:

- **The document carries no password material.** With `dpapi` a file put on a
  share, attached to a ticket or committed by accident carries an encrypted
  password with it — useless to whoever picks it up, and theirs to keep and
  attack offline for as long as they like. With `wincred` it carries a 16-byte
  identifier and nothing else. The same goes for every backup beside it.
- **The document stops being sufficient.** Restore it on a fresh machine and
  the connections are all there and none of the passwords are. A `dpapi`
  document at least still holds them for the account that wrote it.
  `SecretUnprotectStatus.Missing` exists to say which of the two happened —
  absent is not the same as shut, and the person needs telling which.
- **Two things now have to agree.** The store can hold entries the document has
  forgotten, and the document can name entries the store no longer has. Every
  place that stops referring to a saved password releases it — replacing one,
  clearing one, deleting a profile, moving to another store — and what escapes
  that (a crash between the write and the save, a document restored from a
  backup) is swept on request.

Two decisions inside it are worth stating. Persistence is
`CRED_PERSIST_LOCAL_MACHINE` and not `CRED_PERSIST_ENTERPRISE`: the roaming
option would push saved passwords into domain profile storage, which is a
different security claim and not one to make on somebody's behalf. And the
stored value is 16 bytes rather than the target name, so that a hand-edited
document cannot name an arbitrary credential in the person's store and have
Patchbay read it back and hand it to a server.

The sweep is scoped to one document, and that is a safety property rather than
tidiness. Patchbay opens one file at a time but a person may have several, all
filing entries in the same Windows store. A sweep that deleted every Patchbay
entry the open document did not mention would delete the other document's
passwords, silently, while tidying up. Entries therefore carry the document
they belong to, which errs towards leaving an orphan behind rather than towards
destroying a password.

### `master`

A master password is the answer to the last two, and it is optional because
its costs are real ones.

The scheme has **two keys**. The master password derives a key-encryption key
with PBKDF2-HMAC-SHA256 at 600,000 iterations over a 16-byte random salt; that
key wraps a separate 32-byte random document key with AES-256-GCM; and the
document key is what each saved password is encrypted with, again AES-256-GCM,
under a fresh 96-bit nonce every time. The wrapped key, the salt, the
iteration count and the name of the derivation function live in the document as
`masterKey`. None of them is secret and all of them are meant to be readable.

The indirection is not ceremony. It means one derivation per unlock rather than
one per password; it means changing the master password rewraps thirty-two
bytes rather than re-encrypting every secret, so a crash cannot leave a
document with half its passwords under each; and it means a wrong password is
caught once, by the GCM tag on the wrapped key, rather than by trying to
decrypt a password and seeing what comes out. The wrapped key is its own
verifier — a separate check value would be one more thing to get wrong and one
more thing to test a guess against.

PBKDF2 rather than Argon2id, which is the better function and is not in the
framework. Argon2id would mean a third-party cryptographic implementation in
the path that protects every password Patchbay saves. The derivation function
is **named in the record**, so that is a decision to revisit rather than one to
live with: a document written today says `pbkdf2-sha256`, and a build that
grows an Argon2id option will still open it.

What it buys, and what it costs:

- A local administrator cannot read it, and neither can code running as the
  signed-in account, because the key is not held by the machine.
- **The document travels.** Anyone who knows the password can read it on any
  machine. That is the point and it is also the liability: a `dpapi` document
  that leaks is useless to whoever has it, and a `master` document that leaks
  is one password away.
- **There is no recovery.** A forgotten master password is a document of
  unreadable passwords. The connections themselves still work; they just ask.
- The iteration count is what stands between a leaked document and a
  dictionary. 600,000 is OWASP's figure for this function and costs about
  90 ms per unlock on the machine this was written on.

A document with a master password is **schema version 2 or later**, and the bump
matters more than any field added so far. An unrecognised property is dropped on
deserialisation and gone on the next save; for a setting that loses a setting,
and for `masterKey` it loses the only copy of the key wrapping every password
in the file. So a build that has never heard of it refuses to open the document
rather than quietly discarding it.

**Schema version 3** is the same guard for `id` (`M3-04`). A document that keeps
its passwords in Credential Manager files them under its own identity, so a
build that dropped the id would write the file back without one, the next load
would mint a fresh one, and every password the document keeps in Windows would
be filed under an id nothing refers to — present, unreachable, and invisible to
the sweep meant to clear it up. `credentialStore` did not need the bump and does
not have one of its own: dropping it loses a preference, and every blob already
written still names the scheme that wrote it.

A locked document is a working document. The tree, the editor, the import and
every connection that does not use a saved password all behave normally; the
saved passwords report `Locked`, which says what to do about it, and nothing
overwrites them. A locked document also **refuses to save a new password**
rather than falling back to `dpapi` — a silent downgrade of the protection
somebody deliberately turned on would look exactly like success.

### Copies of the document

Writing is atomic — a temporary file, then a replace — with five previous
versions kept. **Backups inherit the protection the document had when they were
written, not the protection it has now.**

That is not a general caution; it was measured. Turning on a master password
re-protects the document and leaves every previous version beside it holding
`pb1:dpapi:` blobs that still open under Windows data protection alone. The
security panel says so, counts them, and offers to delete them — offers rather
than does, because a backup is what recovers a document from a bad save and the
moment just after changing how it is protected is a poor one to have none.

Moving the passwords out to Credential Manager has exactly the same shape and
the same warning. The document afterwards carries no password material and the
copies beside it still do, which matters more here than for a master password:
the reason to choose `wincred` in the first place is usually that the file is
going somewhere, and it is the backups people forget to look at.

The same applies to any copy Patchbay did not make. Pointing the document at a
synced folder means the sync service holds every version it ever saw.

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

There are three formats and two different problems. A `.rdg` and an mRemoteNG
`confCons.xml` are inventories somebody built and kept, and the danger is in
the parser. A `.rdp` is a message that arrives — emailed by a supplier,
downloaded from a portal, left on a share — and the danger is in the file
doing exactly what the format allows.

### XML (`.rdg` and `confCons.xml`)

Every XML reader of a file someone else produced goes through `SafeXml`:
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
imports none. The same holds for `password 51` in a `.rdp`, and there it is
structural: `RdpFile` does not keep a binary value at all, so no blob is in
memory for a node name, a warning or a log line to pick up.

### `.rdp`

The format can say a great deal more than "here is a machine to connect to".
It can hand the far end every drive on this computer, the smart card in its
reader and the microphone; it can name a program to run instead of a desktop;
and it can ask the client not to check who it is connecting to. In October
2024 a campaign used signed `.rdp` attachments to do the first of those at
scale.

So the rule is one sentence: **an imported file may switch a redirection off,
but it may not switch on one that Patchbay leaves off.** What counts as off is
read from `ConnectionSettings.Defaults` rather than a list beside the rule, so
it keeps meaning the same thing if a default ever moves. Nothing is dropped in
silence — everything refused is named in the warnings, and turning any of it
on afterwards is a checkbox in the inspector, which makes it a decision by the
person rather than by the file.

Three more things a file does not get to decide:

- **The identity check.** `authentication level:i:0` is "connect anyway, and
  say nothing". It is not imported, and the connection keeps the setting it
  inherits. A session to a server that could not prove who it is looks pixel
  for pixel like a session to one that could.
- **The address.** It goes through `NodeValidator.IsValidHost`, the same check
  as one somebody typed, so a file cannot put something into the tree that a
  person could not have put there.
- **How a name reads.** Display names and any text quoted back into a warning
  are stripped of control characters and Unicode formatting characters. A
  right-to-left override in a file name reads in the tree as an entirely
  different machine from the one it connects to.

A start program is quoted rather than counted: Patchbay opens desktops, so
`alternate shell` and the RemoteApp settings are not imported whatever they
say, and a file that arrived from somewhere else and names a program to run is
the part worth reading before connecting.

Parsing is bounded at `RdpFile.MaxCharacters` (1 MiB), checked while the file
is being read rather than after, so a file made of one very long line is
refused rather than allocated. The encoding comes from the byte order mark:
`mstsc.exe` writes UTF-16, and reading one of its files as UTF-8 produces a
file that appears to hold no settings at all.

`RdpImporterSecurityTests` is the gate for all of it, the same way
`RdgImporterSecurityTests` is for the XML.

### mRemoteNG (`confCons.xml`)

An inventory, not an invitation, so the rule above is not applied to it. A
`.rdp` is one connection circulating as an attachment; a `confCons.xml` is
somebody's whole estate, imported because it was asked for. Silently dropping
the drive redirection they configured on forty connections would be losing
their work rather than defending them. What it gets instead is a count: a file
whose connections hand this computer's drives, smart card reader, ports or
microphone to the far end says so in a sentence, as do connections set to
connect without checking the server's identity and connections with network
level authentication switched off.

**That is a residual risk and it is named rather than closed.** A hostile
inventory can turn a redirection on, for this format and for `.rdg` alike.
What makes it a different bet from a `.rdp` is that importing somebody's whole
estate is already an act of trusting them wholesale, where opening a single
attached connection is not.

**Passwords are counted and never decrypted, and here that is a decision
rather than a limitation.** RDCMan's blobs and a `.rdp`'s `password 51` are
DPAPI-protected to whoever saved them, so they cannot be read at all.
mRemoteNG encrypts under a key derived from a password that defaults to a
value published in its own documentation, so these could be. Reading somebody's
credential store because the key is guessable is a thing to do deliberately,
with the person watching, and not in the middle of an import.

Two smaller things. A connection is only imported if it resolves to RDP, and
the protocol is followed up the tree rather than assumed, because a folder that
says SSH with connections that inherit is an ordinary file. And full file
encryption is detected before the parse rather than after: what mRemoteNG
writes then is not XML at all, and "this file is not valid XML" sends somebody
looking for a corrupt file when what they have is a working one they need a
password for.

`MremoteNgImporterSecurityTests` re-runs the whole XML battery through this
reader rather than assuming the shared parser covers it. The settings live on
one object, and a change to it would break both readers while only one of them
had tests.

`M3-12` re-runs this review for every new importer. All three have had it.
`M1-12` does not ship until it passes, and it now does.

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
| A forgotten master password cannot be recovered | by design; no escrow, no hint |
| Argon2id is not offered, only PBKDF2 | named in the record, so addable |
| A blob can be moved between profiles in the file by hand | not bound to the profile id |
| Copies made outside Patchbay keep the protection they were made with | nothing can reach them |
| A password handed to the RDP control must be a `string` | not fixable at this layer |
| Secret buffers are not locked out of the swap file | `VirtualLock`, not built |
| A document's Credential Manager entries are stranded if its `id` is lost | restore an older backup and they become unreachable |
| A signed `.rdp` is imported without its signature being checked | nothing claims a publisher either |
| An imported inventory can switch a redirection on | counted and reported, not refused; `.rdp` is the exception |
| A secret concatenated into a message template is not redactable | review, not run time |
| No signed release, so no way to verify what you ran | `M7` |

None of these is a reason not to use Patchbay on a machine you already trust.
All of them are reasons not to treat the document as safe to hand around.
