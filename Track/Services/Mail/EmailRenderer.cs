using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Dynamic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Fluid;
using Fluid.ViewEngine;
using Microsoft.Extensions.FileProviders;
using PreMailer.Net;
using Track.Enums;

namespace Track.Services.Mail;

public sealed record RenderedEmail(string Subject, string Html);

/// <summary>Renders the Liquid templates in Track/Templates/Email: front matter holds the subject, the body names its layout.</summary>
public sealed class EmailRenderer
{
    private const string ResourceFolder = "Track.Templates.Email";
    private const string LayoutKey = "$$layout";
    private const string BodyKey = "$$body";

    private static readonly FluidViewParser Parser = new();

    // Encodes every output, so templates must not add `| escape` (it would encode twice).
    private static readonly HtmlEncoder BodyEncoder = HtmlEncoder.Create(UnicodeRanges.All);

    private readonly TemplateOptions _options = new()
    {
        FileProvider = new EmbeddedFileProvider(typeof(EmailRenderer).Assembly, ResourceFolder),
    };
    private readonly FrozenDictionary<EmailKind, (IFluidTemplate Subject, IFluidTemplate Body)> _templates = LoadTemplates();
    private readonly ConcurrentDictionary<string, IFluidTemplate> _layouts = new();

    public async Task<RenderedEmail> RenderAsync(EmailKind kind, object model)
    {
        var (subjectTemplate, bodyTemplate) = _templates[kind];
        var values = ToLiquidModel(model);

        var subject = (await subjectTemplate.RenderAsync(new TemplateContext(values, _options), NullEncoder.Default)).Trim();
        ((IDictionary<string, object?>)values)["subject"] = subject;

        var context = new TemplateContext(values, _options);
        var html = await bodyTemplate.RenderAsync(context, BodyEncoder);
        if (context.AmbientValues.TryGetValue(LayoutKey, out var layout) && layout is string { Length: > 0 } layoutPath)
        {
            context.AmbientValues[BodyKey] = html;
            html = await Layout(layoutPath).RenderAsync(context, BodyEncoder);
        }

        return new RenderedEmail(subject, PreMailer.Net.PreMailer.MoveCssInline(html, removeStyleElements: true, stripIdAndClassAttributes: true).Html);
    }

    private IFluidTemplate Layout(string path) => _layouts.GetOrAdd(path, p =>
    {
        var file = _options.FileProvider.GetFileInfo(p);
        if (!file.Exists)
            throw new InvalidOperationException($"Email layout '{p}' not found.");
        using var reader = new StreamReader(file.CreateReadStream());
        return Parse(reader.ReadToEnd(), p);
    });

    private static FrozenDictionary<EmailKind, (IFluidTemplate, IFluidTemplate)> LoadTemplates()
    {
        var assembly = typeof(EmailRenderer).Assembly;
        var templates = new Dictionary<EmailKind, (IFluidTemplate, IFluidTemplate)>();
        foreach (var kind in Enum.GetValues<EmailKind>())
        {
            var name = $"{ResourceFolder}.{kind}.liquid";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Email template '{name}' is not embedded.");
            using var reader = new StreamReader(stream);
            var (subject, body) = SplitFrontMatter(reader.ReadToEnd(), name);
            templates[kind] = (Parse(subject, name), Parse(body, name));
        }

        return templates.ToFrozenDictionary();
    }

    internal static (string Subject, string Body) SplitFrontMatter(string raw, string name)
    {
        if (!raw.StartsWith("---", StringComparison.Ordinal))
            throw new InvalidOperationException($"Email template '{name}' has no front matter.");
        var end = raw.IndexOf("---", 3, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException($"Email template '{name}' has unterminated front matter.");

        var subject = raw[3..end]
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("subject:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["subject:".Length..].Trim().Trim('"'))
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Email template '{name}' has no subject.");

        return (subject, raw[(end + 3)..].TrimStart('\r', '\n'));
    }

    private static IFluidTemplate Parse(string source, string name) =>
        Parser.TryParse(source, out var template, out var error)
            ? template
            : throw new InvalidOperationException($"Email template '{name}' doesn't parse: {error}");

    // Round-trips through JSON so templates see camelCase names whatever type the caller passed.
    private static ExpandoObject ToLiquidModel(object model) =>
        ToExpando(JsonNode.Parse(JsonSerializer.Serialize(model, JsonSerializerOptions.Web))!.AsObject());

    private static ExpandoObject ToExpando(JsonObject json)
    {
        var expando = new ExpandoObject();
        var values = (IDictionary<string, object?>)expando;
        foreach (var (key, value) in json)
            values[key] = ToValue(value);
        return expando;
    }

    private static object? ToValue(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.Object => ToExpando(node.AsObject()),
        JsonValueKind.Array => node.AsArray().Select(ToValue).ToList(),
        JsonValueKind.String => node.GetValue<string>(),
        JsonValueKind.Number => node.GetValue<double>(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
