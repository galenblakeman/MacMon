using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var directory = Path.Combine(root, "src", "MacMon");
var keys = new SortedSet<string>(StringComparer.Ordinal);
void Literals(ExpressionSyntax expression)
{
    foreach (var literal in expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
        if (literal.IsKind(SyntaxKind.StringLiteralExpression) && literal.Token.ValueText.Length > 0 &&
            !literal.Ancestors().OfType<PatternSyntax>().Any()) keys.Add(literal.Token.ValueText);
}
foreach (var file in Directory.GetFiles(directory, "*.cs"))
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
    foreach (var call in tree.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (!call.Expression.ToString().StartsWith("UiText.", StringComparison.Ordinal) || call.ArgumentList.Arguments.Count == 0) continue;
        var expression = call.ArgumentList.Arguments[0].Expression;
        if (call.Expression.ToString() != "UiText.Interpolate") { Literals(expression); continue; }
        if (expression is not InterpolatedStringExpressionSyntax interpolated) continue;
        var format = new StringBuilder(); var index = 0;
        foreach (var part in interpolated.Contents)
        {
            if (part is InterpolatedStringTextSyntax text) format.Append(text.TextToken.ValueText);
            else if (part is InterpolationSyntax hole) format.Append('{').Append(index++)
                .Append(hole.AlignmentClause?.ToString()).Append(hole.FormatClause?.ToString()).Append('}');
        }
        keys.Add(format.ToString());
    }
    foreach (var creation in tree.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        if (creation.Type.ToString() == "ProcessDialog" && creation.ArgumentList is { Arguments.Count: > 0 } list)
            Literals(list.Arguments[0].Expression);
}
foreach (var file in Directory.GetFiles(directory, "*.axaml"))
    foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{local:Loc (?:&quot;(.*?)&quot;|([^}]+))\}"))
        keys.Add(WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value));

Dictionary<string, string> Read(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var property in document.RootElement.EnumerateObject())
        if (!result.TryAdd(property.Name, property.Value.GetString() ?? "")) throw new InvalidDataException($"Duplicate localization key: {property.Name}");
    return result;
}
if (args.Contains("--keys"))
{
    Console.WriteLine(JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    return 0;
}
var english = Read(Path.Combine(directory, "Localization", "en.json"));
var errors = new List<string>();
foreach (var key in keys) if (!english.ContainsKey(key)) errors.Add($"Missing English key: {key}");
foreach (var pair in english) if (pair.Key != pair.Value) errors.Add($"English catalog must preserve its key: {pair.Key}");
string[] Placeholders(string value) => Regex.Matches(value, @"(?<!\{)\{[0-9]+(?:,[^}:]+)?(?::[^}]+)?\}(?!\})")
    .Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();
foreach (var path in Directory.GetFiles(Path.Combine(directory, "Localization"), "*.json").Where(path => Path.GetFileName(path) != "en.json"))
{
    var translated = Read(path);
    foreach (var key in english.Keys)
    {
        if (!translated.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) { errors.Add($"{Path.GetFileName(path)} missing: {key}"); continue; }
        try { _ = System.Text.CompositeFormat.Parse(value); }
        catch (FormatException) { errors.Add($"{Path.GetFileName(path)} invalid format: {key}"); }
        if (!Placeholders(key).SequenceEqual(Placeholders(value))) errors.Add($"{Path.GetFileName(path)} changed placeholders: {key}");
    }
    foreach (var key in translated.Keys) if (!english.ContainsKey(key)) errors.Add($"English catalog missing translated key: {key}");
}
foreach (var error in errors) Console.Error.WriteLine(error);
Console.WriteLine($"Localization audit: {keys.Count} source keys, {english.Count} catalog keys, {errors.Count} errors.");
return errors.Count > 0 ? 1 : 0;
