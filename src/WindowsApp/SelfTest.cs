using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace DicteeLocale;
public static class SelfTest
{
    public static async Task Run()
    {
        var results=new List<object>();
        var box=new TextBox {Text="Avant | après",Margin=new Thickness(20),FontSize=20};
        var other=new TextBox {Text="Autre champ",Margin=new Thickness(20)};
        var rich=new QuillLikeTestBox {Margin=new Thickness(20),FontSize=18,AcceptsReturn=true};
        var panel=new StackPanel(); panel.Children.Add(box);panel.Children.Add(other);panel.Children.Add(rich);
        var window=new Window {Title="Test d'insertion · Dictée locale",Content=panel,Width=560,Height=320,Topmost=true};
        try {
            window.Show();Native.ActivateTestWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);window.Activate();System.Windows.Input.Keyboard.Focus(box);box.CaretIndex=6;

            await Task.Delay(250);
            for(int attempt=0;attempt<20;attempt++) {
                var focus=System.Windows.Automation.AutomationElement.FocusedElement;
                if(focus.Current.ProcessId==Environment.ProcessId && focus.Current.ClassName=="TextBox")break;
                Native.ActivateTestWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                window.Activate();System.Windows.Input.Keyboard.Focus(box);
                await Task.Delay(100);
            }
            results.Add(new {test="focus-diagnostic",window=Native.GetForegroundWindow().ToString(),focus=System.Windows.Automation.AutomationElement.FocusedElement.Current.ClassName,hasFocus=box.IsKeyboardFocused,expectedProcess=Environment.ProcessId,actualProcess=System.Windows.Automation.AutomationElement.FocusedElement.Current.ProcessId,expectedWindow=new System.Windows.Interop.WindowInteropHelper(window).Handle.ToString(),visible=window.IsVisible});
            Assert(System.Windows.Automation.AutomationElement.FocusedElement.Current.ProcessId==Environment.ProcessId,"Test window owns keyboard focus",results);
            var target=TextTarget.Capture();
            bool firstApplied=await target.UpdateAsync("bonjour");
            if(!firstApplied || box.Text!="Avant bonjour| apr\u00e8s")results.Add(new {test="first-insertion-diagnostic",firstApplied,text=box.Text,reason=target.Reason});
            Assert(box.Text=="Avant bonjour| après","Insertion au curseur sans modifier le texte voisin",results);
            await target.UpdateAsync("salut");
            Assert(box.Text=="Avant salut| après","Révision limitée au texte dicté",results);

            string pending="";bool scheduled=false;
            System.Windows.Input.TextCompositionEventHandler delayInput=async (_,args)=> {
                args.Handled=true;pending+=args.Text;
                if(scheduled)return;
                scheduled=true;await Task.Delay(250);
                int position=box.CaretIndex;
                box.Text=box.Text.Insert(position,pending);box.CaretIndex=position+pending.Length;
                pending="";scheduled=false;
            };
            box.PreviewTextInput+=delayInput;
            await target.UpdateAsync("salut suite");
            await Task.Delay(400);
            box.PreviewTextInput-=delayInput;
            Assert(box.Text=="Avant salut suite| apr\u00e8s" && target.Check(),"Delayed editor acknowledgement does not suspend dictation",results);
            await target.UpdateAsync("salut suite de la phrase");
            Assert(box.Text=="Avant salut suite de la phrase| apr\u00e8s","The rest of the same dictation is inserted",results);
            box.Text="Avant salut MODIFI\u00c9 | apr\u00e8s";box.CaretIndex=box.Text.Length;
            string edited=box.Text;
            bool applied=await target.UpdateAsync("écrasement interdit");
            Assert(!applied && box.Text==edited,"Une modification manuelle bloque le remplacement",results);
            box.Text="Avant | après";box.Focus();box.CaretIndex=6;
            await Task.Delay(100);
            var moved=TextTarget.Capture();
            await moved.UpdateAsync("ici");
            other.Focus();await Task.Delay(100);
            applied=await moved.UpdateAsync("ailleurs");
            Assert(!applied && other.Text=="Autre champ","Un changement de champ bloque l'insertion",results);
            box.Focus();box.Text="Avant mauvais mot apres";box.Select(6,11);await Task.Delay(100);
            var selected=await TextTarget.CaptureAsync();
            Assert(await selected.UpdateAsync("bon mot") && box.Text=="Avant bon mot apres","Selected words are replaced and neighbours preserved",results);
            Assert(await selected.UpdateAsync("autre mot") && box.Text=="Avant autre mot apres","Revision after selection only changes dictated text",results);
            Assert(await selected.UpdateAsync("autre mot ensuite") && box.Text=="Avant autre mot ensuite apres","Dictation continues after replacing selected text",results);
            box.Text="Avant mauvais apres";box.Select(6,7);await Task.Delay(100);
            selected=await TextTarget.CaptureAsync();
            Assert(await selected.UpdateAsync("") && box.Text=="Avant mauvais apres","Empty recognition leaves selection unchanged",results);
            Assert(await selected.UpdateAsync("correct") && box.Text=="Avant correct apres","Single selected word can be replaced",results);
            rich.Focus();rich.CaretIndex=0;await Task.Delay(100);
            var quill=await TextTarget.CaptureAsync();
            bool confirmed=await quill.UpdateAsync("Tu pourrais");
            Assert(confirmed && rich.Text=="Tu pourrais" && await Task.Run(quill.Check),"Quill first words acknowledge NBSP and changing paragraph sentinel",results);
            Assert(await quill.UpdateAsync("Tu pourrais continuer la phrase") && rich.Text=="Tu pourrais continuer la phrase","Quill same dictation continues beyond first words",results);
            Assert(await quill.UpdateAsync("Pourrais-tu continuer la phrase ?") && rich.Text=="Pourrais-tu continuer la phrase ?","Quill owned revision preserves guarded deletion",results);
            rich.Text="Introduction\nAvant mauvais apres";rich.Select(19,7);rich.DifferentValueView=true;await Task.Delay(100);
            quill=await TextTarget.CaptureAsync();
            Assert(await quill.UpdateAsync("correct") && rich.Text=="Introduction\nAvant correct apres","Quill multiline selection uses the same text provider as caret offsets",results);
            Assert(await quill.UpdateAsync("correct encore") && rich.Text=="Introduction\nAvant correct encore apres","Quill different ValuePattern view does not break continuation",results);
            rich.Text="Introduction\nAvant EDIT MANUEL apres";
            string richEdited=rich.Text;
            Assert(!await quill.UpdateAsync("interdit") && rich.Text==richEdited,"Quill meaningful manual changes remain protected",results);
            bool newlineRejected=false;
            try {Native.ReplaceSuffix(0,"ne pas envoyer\n");}catch(InvalidOperationException){newlineRejected=true;}
            Assert(newlineRejected,"Aucune touche Entrée injectée",results);
        } catch(Exception ex) {results.Add(new {test="exception",passed=false,error=ex.ToString()});}
        finally {
            Directory.CreateDirectory(Path.Combine(Paths.Root,"test-results"));
            File.WriteAllText(Path.Combine(Paths.Root,"test-results","insertion.json"),
                JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
            window.Close();Application.Current.Shutdown();
        }
    }
    static void Assert(bool ok,string label,List<object> results)
    {
        results.Add(new {test=label,passed=ok});
        if(!ok)throw new InvalidOperationException(label);
    }
}
