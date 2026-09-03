# ACPollerForAPS

Multi-ERP integration product: converts invoices exported from APS (ReadSoft)
into the format expected by each destination ERP, and delivers them where the
ERP imports them.

A single input format (APS XML), multiple outputs depending on the Buyer:
CSV for Dynamics AX, XML for Optima, etc. Everything is configuration-driven,
no recompilation.

---

## Architecture (3 projects)

| Project | Role | Target |
|---|---|---|
| **ACPollerForAPS.Core** | Shared DLL: config model, mapping engine, validation, encryption, path extractor, plugin contract | net48 |
| **ACPollerForAPS.Service** | The Windows service: detection, routing, merge, transport, archiving. Exe: `ACPollerForAPS.Service.exe` | net4.8 |
| **ACPollerForAPS.UI** | The configuration interface (WPF/MahApps). Exe: `ACPollerForAPS.UI.exe` | net48 |

The Core DLL is referenced by both the service AND the UI: a single definition of
the model, no divergence between what the UI writes and what the service reads.

---

## The flow (service)

At a scheduled interval, the service:
1. scans the input folder (APS XML files),
2. reads each file's Buyer and routes it to the right output channel,
3. merges the files of the same channel,
4. converts to the channel's format (via the provider: default mapping, or a
   plugin DLL),
5. delivers the result via the channel transport (FS / FTPS / S3),
6. archives the source files — ONLY if delivery succeeded (otherwise retry on the
   next run, no data loss).

---

## Configuration: settings.json

SINGLE file shared between the UI and the service, structure:

```json
{ "Pipeline": { ... } }
```

- The **service** loads it automatically at startup, next to its exe.
- The **UI** loads it automatically at launch if it sits next to its exe, and
  edits it (Open / Save). Atomic write with a .bak backup.

> The service reads settings.json once, at startup. After a change through the UI,
> RESTART the service so it picks up the new configuration.

### Global parameters

| Field | Role |
|---|---|
| InputFolder | folder watched for input |
| ArchiveFolder / ErrorFolder | archiving (dated) / files in error |
| ArchiveEnabled | archive (else delete) sources after delivery |
| FileFilter | file filter (e.g. *.xml) |
| StableCheckMs | delay to check a file is complete |
| RecordPath | XPath to one invoice (e.g. /InvoiceData) |
| BuyerPath | XPath (relative to record) to the routing value |
| Schedule | run interval (value + Minutes/Hours) |
| Channels | list of output channels |

### An output channel

| Field | Role |
|---|---|
| Name / Enabled | label / active |
| Buyers | Buyer values routed to this channel |
| OutputFormat | Csv or Xml |
| Provider | "mapping" (default) or a plugin DLL name |
| OutputFolder / OutputFileName | destination (FS) and name ({date},{time},{guid},{batch}) |
| BatchSize | max invoices per output file (0 = single merged file) |
| RecordPath / LinesPath | invoice / accounting lines location |
| SampleXml | sample input XML of the channel (preview + path auto-completion) |
| CsvFormat / XmlFormat | output formatting |
| Transport | destination (FS / FTPS / S3) |
| Fields | column/element mapping |

### A mapping field (Fields)

| Property | Role |
|---|---|
| Name | CSV column / XML element name |
| Type | text / amount / date |
| Source | fixed (Value) / header / xpath (Path, invoice level) / line (Path, line level) |
| Value | constant value (Source=fixed) |
| Path | XPath (Source=header/xpath/line) |
| DecDigits / AbsoluteValue | decimals / absolute value (amounts) |
| InFormat | input date format |
| Values | value translation (e.g. Credit→C) |
| OnlyWhenPath / OnlyWhenEquals | conditional write (see below) |

---

## Conditional mapping (Vendor/Ledger lines)

Some formats (Dynamics AX) produce, per invoice, one "Vendor" line (vendor credit)
and N "Ledger" lines (debits), with different columns depending on the line type.

The `OnlyWhenPath` / `OnlyWhenEquals` field handles this: a field is written only
if the node at OnlyWhenPath (relative to the line) equals OnlyWhenEquals. Otherwise
the cell is empty. Examples:
- Credit column: OnlyWhenPath=PostingType, OnlyWhenEquals=Credit
- Debit column: OnlyWhenPath=PostingType, OnlyWhenEquals=Debit
- vendor control account: OnlyWhen PostingType=Credit

Editable in the mapping grid of the UI (columns "Only when (path)" and "equals").

---

## Output transports

Each channel delivers its file via a transport:

- **FS**: file system (OutputFolder).
- **FTPS**: via FluentFTP, explicit (FTPES) or implicit (port 990) mode, optional
  certificate validation.
- **S3**: via the AWS SDK, standard AWS endpoint OR a custom one (MinIO /
  S3-compatible, with path-style access).

Configurable retry (count + delay). If it fails after all attempts, the sources are
not archived (retry on the next run).

### Credential security

FTPS passwords and S3 secret keys are encrypted via **DPAPI machine scope**
(never stored in clear in the JSON).

> Consequence: encrypt on the TARGET MACHINE (the one running the service).
> A secret encrypted on another machine cannot be decrypted there. Use the UI
> installed on the service machine to enter the passwords.

The UI offers "Test connection" buttons for FTPS and S3.

---

## ERP Providers (plugins)

Hybrid architecture:
- **Default ("mapping")**: most ERPs are covered by the configurable mapping, with
  no DLL.
- **DLL plugin**: for an ERP with specific logic, a DLL implementing `IErpExporter`
  dropped into a **`providers/`** folder at the module root is discovered at
  service startup. The channel points to it via its `Provider` field.

See PROVIDERS.md to build a provider.

---

## Prerequisites

- .NET Framework 4.8 (runtime) on the target machine.
- NuGet-restored packages: Newtonsoft.Json, NLog, FluentFTP, AWSSDK.S3,
  MahApps.Metro (UI).

---

## Installing the service

From an **administrator** command prompt, in the folder containing
`ACPollerForAPS.Service.exe`:

```
install.bat
```

install.bat:
```
set INSTALLUTIL=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe
set EXE=%~dp0ACPollerForAPS.Service.exe
"%INSTALLUTIL%" "%EXE%"
sc start ACPollerForAPS
```

Uninstall: `uninstall.bat` (sc stop + InstallUtil /u).

> The service name (`ACPollerForAPS`) in the .bat files must match the ServiceName
> defined in ProjectInstaller.cs AND in the service constructor. Otherwise
> `sc start` won't find the service.

---

## Controlling the service

```
sc start ACPollerForAPS
sc pause ACPollerForAPS
sc continue ACPollerForAPS
sc stop ACPollerForAPS
```

Console mode (debug): `ACPollerForAPS.Service.exe --console`.

---

## Logs

NLog writes to `logs/` next to the service exe, with daily + size rotation and
automatic purge. Startup traces the watched folder, loaded providers, and each run
(routed files, deliveries, archiving) with a per-run summary. Set the level to
Debug in NLog.config for detailed diagnostics (no recompilation).

---

## Frequent errors

- **FileNotFoundException at install** → the .bat points to the wrong exe name, or
  is launched outside the exe folder.
- **`[SC] OpenService 1060`** → install failed earlier; the service doesn't exist.
- **"the service did not respond in time"** → ServiceName mismatch between code and
  .bat.
- **Empty Path field in the UI after load** → known and fixed (the suggestion
  dropdown must not overwrite Path on load).
- **FTPS/S3 secret cannot be decrypted** → encrypted on a machine other than the
  service's (DPAPI machine scope).

---

## Business items to finalize

- Real APS input XMLs (awaited from Marko) to lock the final mapping.
- Dynamics CSV columns still unmapped (AP codes, MCC dimensions, intermediate
  columns) — column↔field mapping to confirm.
- Optima output format (depends on Poland).
