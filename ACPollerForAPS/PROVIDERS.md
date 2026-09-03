# ERP Providers (plugin mechanism)

**Hybrid** architecture for the XML → ERP-format transformation:

- **Default: mapping by configuration.** Most ERPs (convertible to CSV or XML by
  a simple field mapping) need NO DLL. They go through the built-in `mapping`
  provider (MappingExporter), driven by the channel's JSON. Adding such an ERP =
  adding a channel in the config.

- **For specific cases: DLL plugin.** If an ERP needs a transformation that a
  field mapping cannot describe (aggregations, unusual output structure, business
  logic), you provide a dedicated DLL dropped into the **`providers/`** folder at
  the root of the module. The service discovers it at startup. The rest of the
  pipeline (Buyer routing, merge, transport, archiving) is unchanged.

---

## How the service discovers providers

At startup, `ProviderLoader.LoadAll`:
1. registers the default `mapping` provider;
2. scans `<exe folder>/providers/*.dll`;
3. for each DLL, instantiates the types implementing `IErpExporter` and registers
   them under the name(s) they declare.

Robustness: a DLL that is unreadable, incompatible, or whose type fails to load is
**logged and ignored** — it never crashes the service.

---

## How a channel picks its provider

In the channel config (settings.json), the `Provider` field:
- `"mapping"` (default, or empty) → transformation by configuration;
- `"<name declared by a DLL>"` → that DLL is used.

If the referenced name is not found, the service falls back to `mapping` and logs
it.

In the UI, this is set in **Channel settings > Provider** (editable list:
"mapping" suggested, free text for a plugin name).

---

## The contract: IErpExporter

Defined in the shared **ACPollerForAPS.Core** DLL:

```csharp
namespace ACPollerForAPS.Core
{
    // Result of a transformation: the content of the file to deliver.
    public class ExportResult
    {
        public byte[] Content { get; set; }          // file ready to deliver
        public List<string> Warnings { get; }        // non-blocking warnings
    }

    public interface IErpExporter
    {
        // Name(s) this provider registers under (referenced by
        // OutputChannel.Provider). Case-insensitive.
        IEnumerable<string> ProviderNames { get; }

        // Transforms the input XMLs (already routed to this channel) into a
        // single output file, according to the channel configuration.
        ExportResult Export(IEnumerable<string> inputXmls, OutputChannel channel);
    }
}
```

---

## Creating a new provider (specific ERP)

1. **New class library project** (.NET Framework 4.8) referencing
   `ACPollerForAPS.Core`.

2. **Implement `IErpExporter`**:

```csharp
using System.Collections.Generic;
using System.Text;
using ACPollerForAPS.Core;

public class OptimaExporter : IErpExporter
{
    public IEnumerable<string> ProviderNames => new[] { "optima" };

    public ExportResult Export(IEnumerable<string> inputXmls, OutputChannel channel)
    {
        var result = new ExportResult();

        // ... your specific transformation from inputXmls ...
        // (you have access to channel: RecordPath, LinesPath, Fields, Format, etc.)

        // result.Warnings.Add("a non-blocking warning");  // optional
        result.Content = Encoding.UTF8.GetBytes(/* the produced file */);
        return result;
    }
}
```

3. **Compile** the DLL.

4. **Drop** the DLL into the `providers/` folder next to the service exe
   (`ACPollerForAPS.Service.exe`). Create the `providers/` folder if needed.

5. In the config, set `"Provider": "optima"` on the relevant channel
   (or via the UI: Channel settings > Provider).

No recompilation of the service. The contract (`IErpExporter`, `ExportResult`,
`OutputChannel`) lives in the shared Core DLL — the provider sees exactly the same
model as the service and the UI.

---

## Watch out: version compatibility

A third-party provider is compiled against a version of `ACPollerForAPS.Core`.
The Core DLL shipped next to the service MUST be compatible with the one used to
compile the provider. If the Core version changes the contract, recompile the
providers.

---

## What stays common to all providers

The provider only produces the file content. Everything else is handled by the
pipeline, identical regardless of the provider:
- input file detection and reading,
- Buyer routing, batching (Invoices per file),
- output file naming (tokens {date}, {time}, {guid}, {batch}),
- delivery via the channel transport (FS / FTPS / S3) with retry,
- archiving of sources only if delivery succeeded.
