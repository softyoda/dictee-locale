using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
namespace DicteeLocale;
public static class Ui
{
    static readonly Dictionary<string,string> English=Load();
    public static string Language {get;set;}="en";
    public static string HotkeyLabel => Language=="fr"?"Ctrl+Maj+Alt+D":"Ctrl+Shift+Alt+D";
    static Dictionary<string,string> Load()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DicteeLocale.Strings.en.json")!;
        using var reader=new System.IO.StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string,string>>(reader.ReadToEnd())!;
    }
    public static string Text(string text)
    {
        if(Language=="en") {
            if(English.TryGetValue(text,out var exact))return exact;
            foreach(var pair in English.OrderByDescending(p=>p.Key.Length))
                if(pair.Key.EndsWith(" ",StringComparison.Ordinal) && text.StartsWith(pair.Key,StringComparison.Ordinal))
                    return pair.Value+text.Substring(pair.Key.Length);
        } else {
            foreach(var pair in English.OrderByDescending(p=>p.Value.Length)) {
                if(text==pair.Value)return pair.Key;
                if(pair.Value.EndsWith(" ",StringComparison.Ordinal) && text.StartsWith(pair.Value,StringComparison.Ordinal))
                    return pair.Key+text.Substring(pair.Value.Length);
            }
        }
        return text;
    }
}
