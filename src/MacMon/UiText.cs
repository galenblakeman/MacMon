using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace MacMon;

public sealed record LanguageChoice(string Id, string Label) { public override string ToString() => Label; }

public static class UiText
{
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    // Avalonia observes binding sources weakly. Retain one source per resource key so
    // labels created before a collection still refresh after a language change.
    private static readonly Dictionary<string, LocalizedValue> Values = [];
    private static readonly Dictionary<string, string> Spanish = LoadSpanish();
    public static IReadOnlyList<LanguageChoice> Languages { get; } = [new("System", "System language"), new("en", "English"), new("es", "Español")];
    public static string Language { get; private set; } = "System";
    private static bool IsSpanish => Language == "es" || Language == "System" && SystemCulture.TwoLetterISOLanguageName == "es";
    public static string NormalizeLanguage(string? language) => language is "en" or "es" ? language : "System";
    public static string Get(string english) => IsSpanish && Spanish.TryGetValue(english, out var value) ? value : english;
    public static string Format(string english, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(english), args);
    public static void Apply(string language)
    {
        Language = NormalizeLanguage(language);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(IsSpanish ? "es" : "en");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        foreach (var value in Values.Values.ToArray()) value.Refresh();
    }
    public static Binding Bind(string english)
    {
        if (!Values.TryGetValue(english, out var value)) Values.Add(english, value = new(english));
        return new(nameof(LocalizedValue.Value)) { Source = value };
    }
    public static Avalonia.Controls.TextBlock Label(string english) => new() { [!Avalonia.Controls.TextBlock.TextProperty] = Bind(english) };
    private static Dictionary<string, string> LoadSpanish()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("MacMon.Localization.es.json");
        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
    public sealed class LocalizedValue(string english) : INotifyPropertyChanged
    {
        public string Value => Get(english);
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Value)));
    }
}

public sealed class LocExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Bind(text);
}
