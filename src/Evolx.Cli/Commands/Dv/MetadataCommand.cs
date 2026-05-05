using System.ComponentModel;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class MetadataCommand : DvCommandBase<MetadataCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--out <PATH>")]
        [Description("File to write the CSDL XML to. Required.")]
        public string Out { get; set; } = "";

        [CommandOption("--filter <PREFIX>")]
        [Description("Drop EntityType/ComplexType/Action/Function/EntityContainer entries whose name doesn't start with this prefix.")]
        public string? Filter { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Out))
        {
            AnsiConsole.MarkupLine("[red]--out <path> is required.[/]");
            return 2;
        }

        var bytes = await dv.GetCsdlMetadataAsync(ct);

        if (string.IsNullOrWhiteSpace(s.Filter))
        {
            // No filter: write the bytes exactly as the server returned them — no re-encoding.
            await File.WriteAllBytesAsync(s.Out, bytes, ct);
            AnsiConsole.MarkupLine($"[green]Wrote[/] {bytes.Length:N0} bytes to {Markup.Escape(s.Out)}");
            return 0;
        }

        // Filter mode: parse, prune, write back as UTF-8 XML via XDocument/XmlWriter.
        // Never edit XML as text — XDocument handles namespaces, escaping, CDATA, and BOMs correctly.
        XDocument doc;
        using (var ms = new MemoryStream(bytes))
        {
            doc = XDocument.Load(ms, LoadOptions.PreserveWhitespace);
        }

        var prefix = s.Filter;
        CsdlFilter.Prune(doc, prefix);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            Async = true,
        };
        await using (var fs = File.Create(s.Out))
        await using (var xw = XmlWriter.Create(fs, settings))
        {
            doc.Save(xw);
        }

        var info = new FileInfo(s.Out);
        AnsiConsole.MarkupLine($"[green]Wrote[/] {info.Length:N0} bytes (filter: {Markup.Escape(prefix)}) to {Markup.Escape(s.Out)}");
        return 0;
    }

}
