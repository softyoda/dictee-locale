using System;
using System.IO;
using System.Threading;
using System.Windows;
namespace DicteeLocale;
public partial class App : Application
{
    private Mutex? _mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Ui.Language=Settings.Load().interfaceLanguage;
        if(Array.Exists(e.Args,a=>a is "--self-test" or "--interaction-test" or "--smoke-test"))
            Directory.CreateDirectory(Path.Combine(Paths.Root,"test-results"));
        _mutex = new Mutex(true, @"Local\DicteeLocale-tts_local", out bool created);
        if (!created) { MessageBox.Show(Ui.Text("La dictée locale est déjà lancée (icône dans la zone de notification).")); Shutdown(); return; }
        if (Array.Exists(e.Args, a => a == "--self-test")) { _ = SelfTest.Run(); return; }
        bool interactionTest=Array.Exists(e.Args,a=>a=="--interaction-test");
        var window = new MainWindow(interactionTest);
        MainWindow = window;
        window.InitializeHidden();
        if(interactionTest)_=window.InteractionTest();
    }
    protected override void OnExit(ExitEventArgs e) { _mutex?.Dispose(); base.OnExit(e); }
}
public static class Paths
{
    public static readonly string Root = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
}
