using System;
using System.IO;
using System.Text.Json;
namespace DicteeLocale;
public sealed class Settings
{
    public string interfaceLanguage {get;set;}=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="fr"?"fr":"en";
    public int hotkeyModifiers {get;set;}=7;
    public int hotkeyKey {get;set;}=68;
    public string hotkeyLabel {get;set;}="Ctrl+Maj+Alt+D";
    public bool cleanupEnabled {get;set;}=true;
    public int microphoneIndex {get;set;}=-1;
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Paths.Root,"settings.json"))) ?? new(); }
        catch { return new(); }
    }
    public void Save() => File.WriteAllText(Path.Combine(Paths.Root,"settings.json"),
        JsonSerializer.Serialize(this,new JsonSerializerOptions {WriteIndented=true}));
}
