using System;
using System.IO;
using Microsoft.Win32;
namespace DicteeLocale;
public static class StartupRegistration
{
    const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name="DicteeLocale";
    public static string Command => "\""+Path.Combine(Paths.Root,"app","DicteeLocale.exe")+"\"";
    public static bool Enabled {
        get {using var key=Registry.CurrentUser.OpenSubKey(KeyPath);return key?.GetValue(Name) as string==Command;}
    }
    public static void SetEnabled(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath);
        if(enabled)key.SetValue(Name,Command,RegistryValueKind.String);
        else key.DeleteValue(Name,false);
    }
}
