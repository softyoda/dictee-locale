using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using Forms=System.Windows.Forms;
namespace DicteeLocale;
public partial class MainWindow : Window
{
    readonly Settings _settings=Settings.Load();
    readonly LocalEngine _engine=new();
    readonly SemaphoreSlim _inference=new(1,1);
    readonly object _audioLock=new();
    readonly MemoryStream _chunk=new();
    readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(750)};
    Window? _settingsWindow;
    Forms.NotifyIcon? _tray;
    System.Drawing.Icon? _trayIcon;
    WaveInEvent? _capture;
    TextTarget? _target;
    IntPtr _handle;
    bool _hotkeyRegistered;
    volatile bool _dismissedOnEnter, _retargeting;
    CancellationTokenSource _session=new();
    bool _ready,_recording,_stopping,_speech,_quitting,_healthBusy,_pendingInjection;
    DateTime _lastSpeech;
    DateTime _lastPoll=DateTime.MinValue;
    string _committed="",_raw="",_clean="",_display="";
    int _generation, _clickVersion, _popupVersion;
    bool _canRestoreRaw;
    readonly bool _interactionTest;
    public MainWindow(bool interactionTest=false)
    {
        _interactionTest=interactionTest;
        InitializeComponent();
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","dictee.ico")));
        ShowActivated=false;
        Left=(SystemParameters.WorkArea.Width-Width)/2;
        Top=SystemParameters.WorkArea.Bottom-Height-24;
        RefreshLanguage();
        SourceInitialized+=InitNative;
        _timer.Tick+=Tick;
        Closing+=OnClosing;
    }
    void InitNative(object? sender,EventArgs e)
    {
        _handle=new WindowInteropHelper(this).Handle;
        Native.ToolbarHandle=_handle;
        Native.SetWindowLongPtr(_handle,-20,new IntPtr(Native.GetWindowLongPtr(_handle,-20).ToInt64()|0x08000000|0x80));
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);

        Native.EnterPressed=()=> {
            if(!_recording && !_stopping)return;
            // Invalidate the destination before Enter can submit/clear the editor.
            _dismissedOnEnter=true;
            _session.Cancel();
            _target?.Detach("Dictee terminee par Entree.");
            Interlocked.Increment(ref _generation);
            Dispatcher.BeginInvoke(new Action(StopOnEnter),DispatcherPriority.Send);
        };
        Native.ParsecShortcutPressed=()=>Dispatcher.BeginInvoke(new Action(async()=>await Toggle()),DispatcherPriority.Input);
        Native.UserEdited=()=> {
            if(_target is {Owned:true} && (_recording||_stopping||_canRestoreRaw))
                _target.Detach("Modification manuelle : insertion suspendue.");
        };
        try {
            Native.TargetChanging=()=> {
                _target?.Detach("Changement de destination.");
                if(!_recording)return;
                _retargeting=true;
                Interlocked.Increment(ref _generation);
                Dispatcher.BeginInvoke(new Action(()=> {
                    _clickVersion++;
                    _session.Cancel();_session=new CancellationTokenSource();
                    lock(_audioLock) {_chunk.SetLength(0);_speech=false;}
                    _committed=_raw=_clean=_display="";
                    _pendingInjection=false;_canRestoreRaw=false;
                }),DispatcherPriority.Input);
            };
            Native.TargetClicked=()=>Dispatcher.BeginInvoke(new Action(()=> {
                if(_recording)_=RetargetAfterClick();
            }),DispatcherPriority.Input);
            Native.StartHooks();
            RegisterShortcut();
            SetupTray();
            if(!_interactionTest)_engine.Start();
            else _ready=true;
            _timer.Start();
            if(Array.Exists(Environment.GetCommandLineArgs(),a=>a=="--smoke-test")) _=SmokeTest();
        } catch(Exception ex) { SetStatus(ex.Message,true); }
    }
    void RegisterShortcut()
    {
        Native.UnregisterHotKey(_handle,Native.HOTKEY_ID);
        Native.HotkeyKey=_settings.hotkeyKey;
        _hotkeyRegistered=Native.RegisterHotKey(_handle,Native.HOTKEY_ID,(uint)_settings.hotkeyModifiers|Native.MOD_NOREPEAT,(uint)_settings.hotkeyKey);
        if(!_hotkeyRegistered)
            SetStatus("Raccourci indisponible. Réessaie depuis l’icône.",true);
    }
    IntPtr WndProc(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if(message==Native.WM_MOUSEACTIVATE) {handled=true;return new IntPtr(3);}
        if(message==Native.WM_HOTKEY && w.ToInt32()==Native.HOTKEY_ID) {
            handled=true;
            Dispatcher.BeginInvoke(new Action(async()=>await Toggle()));
        }
        return IntPtr.Zero;
    }
    void SetupTray()
    {
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add(Ui.Text("Afficher la barre"),null,(_,_)=>Dispatcher.Invoke(()=>ShowBriefly()));
        menu.Items.Add(Ui.Text("Restaurer la derniere dictee brute"),null,(_,_)=>Dispatcher.Invoke(async()=> { if(_canRestoreRaw)await RawClick(); }));
        menu.Items.Add(Ui.Text("Démarrer / arrêter"),null,(_,_)=>Dispatcher.Invoke(async()=>await Toggle()));
        menu.Items.Add(Ui.Text("Réglages"),null,(_,_)=>Dispatcher.Invoke(()=>OpenSettings()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var startup=new Forms.ToolStripMenuItem(Ui.Text("Lancer au d\u00e9marrage de Windows")) {Checked=StartupRegistration.Enabled};
        startup.Click+=(_,_)=> {
            try {StartupRegistration.SetEnabled(!StartupRegistration.Enabled);startup.Checked=StartupRegistration.Enabled;}
            catch(Exception ex) {MessageBox.Show(Ui.Text("Impossible de modifier le d\u00e9marrage : ")+ex.Message);}
        };
        menu.Items.Add(startup);
        var shortcut=new Forms.ToolStripMenuItem();
        shortcut.Click+=(_,_)=>Dispatcher.Invoke(()=> {
            RegisterShortcut();
            _tray?.ShowBalloonTip(4000,Ui.Text("Dict\u00e9e locale"),_hotkeyRegistered?
                Ui.Text("Raccourci actif : ")+Ui.HotkeyLabel:Ui.Text("Le raccourci est d\u00e9j\u00e0 utilis\u00e9 par une autre application."),
                _hotkeyRegistered?Forms.ToolTipIcon.Info:Forms.ToolTipIcon.Warning);
        });
        menu.Items.Add(shortcut);
        menu.Opening+=(_,_)=> {
            startup.Checked=StartupRegistration.Enabled;
            shortcut.Text=Ui.Text("Raccourci : ")+Ui.HotkeyLabel+(_hotkeyRegistered?Ui.Text(" (actif)"):Ui.Text(" (r\u00e9essayer)"));
        };
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Ui.Text("Quitter compl\u00e8tement"),null,(_,_)=>Dispatcher.Invoke(()=>Quit()));
        _trayIcon=new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory,"Assets","dictee.ico"),32,32);
        _tray=new Forms.NotifyIcon {Icon=_trayIcon,Visible=true,
            Text=Ui.Text("Dictée locale · ")+Ui.HotkeyLabel,ContextMenuStrip=menu};
        if(!_hotkeyRegistered)_tray.ShowBalloonTip(5000,Ui.Text("Dictée locale"),Ui.Text("Raccourci indisponible : clique sur l’icône pour réessayer."),Forms.ToolTipIcon.Warning);
        _tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(()=>OpenSettings());
    }
    void SetStatus(string text,bool error=false)
    {
        StatusText.Text=Ui.Text(text);StatusText.ToolTip=StatusText.Text;
        StatusText.Foreground=new SolidColorBrush(error?Color.FromRgb(251,146,146):Colors.White);
    }
    static string Join(string a,string b) => string.IsNullOrWhiteSpace(a)?b.Trim():
        string.IsNullOrWhiteSpace(b)?a.Trim():a.TrimEnd()+" "+b.TrimStart();
    async void Tick(object? sender,EventArgs e)
    {
        if(!_ready && !_healthBusy && (DateTime.UtcNow-_lastPoll).TotalSeconds>1) {
            _lastPoll=DateTime.UtcNow; _healthBusy=true;
            try {
                if(_engine.Process is {HasExited:true})throw new InvalidOperationException(_engine.LastError??"Le moteur s'est arrêté.");
                var health=await _engine.Health();
                SetStatus(health.GetProperty("stage").GetString()??"Chargement…",
                          health.TryGetProperty("error",out var err)&&err.ValueKind!=System.Text.Json.JsonValueKind.Null);
                _ready=health.GetProperty("ready").GetBoolean();
            } catch(Exception ex) {
                if(_engine.Process is {HasExited:true})SetStatus(ex.Message,true);
            } finally {_healthBusy=false;}
        }
        if(_pendingInjection && Native.ModifiersReleased() && _target is {Owned:true,Updating:false}) {
            var target=_target;int generation=_generation;
            try {string pending=_display; await target.UpdateAsync(pending); if(generation==_generation)_pendingInjection=_display!=pending;} catch(Exception ex) {target.Detach(ex.Message);if(generation==_generation)SetStatus(ex.Message,true);}
        }
        if(!_recording || _retargeting || _target is not {Owned:true}) return;
        if(!await _inference.WaitAsync(0))return;
        try {
            byte[] audio;
            bool final;
            lock(_audioLock) {
                if(!_speech || _chunk.Length<24000)return;
                final=(DateTime.UtcNow-_lastSpeech).TotalMilliseconds>850 || _chunk.Length>=640000;
                if(!final && _target is {IsParsec:true})return; // No remote preview, so decode only finished phrases.
                audio=_chunk.ToArray();
                if(final) {_chunk.SetLength(0);_speech=false;}
            }
            int generation=_generation;
            string text=await _engine.Transcribe(audio,_committed,_session.Token);
            if(generation!=_generation)return;
            if(_target is {IsParsec:true}) {
                // A remote video surface cannot acknowledge or safely revise provisional words.
                if(!final) {SetStatus("Parsec : ecoute, phrases finalisees");return;}
                if(!_interactionTest && _settings.cleanupEnabled && !string.IsNullOrWhiteSpace(text)) {
                    SetStatus("Parsec : nettoyage avant insertion");
                    var cleaned=await _engine.Cleanup(text,_session.Token);
                    if(generation!=_generation)return;
                    text=cleaned.text;
                }
            }
            if(final)_committed=Join(_committed,text);
            _raw=final?_committed:Join(_committed,text);
            await ShowTranscript(_raw);
            if(generation!=_generation)return;
            if(_recording)SetStatus(_target is {Owned:true}?(_target.IsParsec?"Parsec : ecoute, phrases finalisees":"Écoute dans le champ actif"):(_target?.Reason??"Insertion suspendue"),_target is not {Owned:true});
        } catch(OperationCanceledException) {}
        catch(Exception ex) {SetStatus(ex.Message,true);}
        finally {_inference.Release();}
    }
    async Task ShowTranscript(string text)
    {
        _display=text;

        var target=_target;
        int generation=_generation;
        if(target is {Owned:true}) {
            try {
                if(!Native.ModifiersReleased())_pendingInjection=true;
                else if(!await target.UpdateAsync(text)) {
                    if(generation==_generation && target.Owned)_pendingInjection=true;
                    else if(generation==_generation)SetStatus(target.Reason??"Insertion suspendue",true);
                }
            } catch(Exception ex) {target.Detach(ex.Message);if(generation==_generation)SetStatus(ex.Message,true);}
        }
    }
    async Task Toggle()
    {
        if(_stopping)return;
        if(_recording) {await StopRecording();return;}
        if(!_ready) {ShowBriefly();SetStatus("Les modèles se chargent. Patiente un instant.");return;}
        try {
            _target=await TextTarget.CaptureAsync(_settings.parsecEnabled,_settings.parsecKeyboardLayout);
            _retargeting=false;
            _session.Cancel();_session=new CancellationTokenSource();
            _dismissedOnEnter=false;
            _generation++;
            _committed=_raw=_clean=_display="";
            _pendingInjection=false;
            lock(_audioLock) {_chunk.SetLength(0);_speech=false;}
            _canRestoreRaw=false;
            if(!_interactionTest) {
            _capture=new WaveInEvent {DeviceNumber=_settings.microphoneIndex,WaveFormat=new WaveFormat(16000,16,1),
                                     BufferMilliseconds=100,NumberOfBuffers=3};
            _capture.DataAvailable+=AudioAvailable;
            _capture.RecordingStopped+=(_,args)=> {
                if(args.Exception is not null)Dispatcher.BeginInvoke(new Action(()=> {
                    _recording=false;SetStatus("Micro : "+args.Exception.Message,true);
                    MicButton.Background=new SolidColorBrush(Color.FromRgb(99,102,241));
                }));
            };
            }
            _recording=true;
            _capture?.StartRecording();
            MicButton.Background=new SolidColorBrush(Color.FromRgb(225,71,109));
            MicButton.Content="■";

            SetStatus(_target.IsParsec?"Parsec : ecoute, phrases finalisees":"Écoute dans le champ actif");
            _popupVersion++;Show();_=Dispatcher.BeginInvoke(new Action(PositionNearTarget),DispatcherPriority.Background);
        } catch(Exception ex) {
            _recording=false;
            _capture?.Dispose();_capture=null;
            SetStatus(ex.Message,true);ShowBriefly();
        }
    }
    void AudioAvailable(object? sender,WaveInEventArgs e)
    {
        if(!_recording || !Native.MouseReleased() || (!_retargeting && _target is not {Owned:true}))return;
        double energy=0;
        for(int i=0;i+1<e.BytesRecorded;i+=2) {double sample=BitConverter.ToInt16(e.Buffer,i)/32768.0;energy+=sample*sample;}
        double rms=Math.Sqrt(energy/Math.Max(1,e.BytesRecorded/2));
        lock(_audioLock) {
            _chunk.Write(e.Buffer,0,e.BytesRecorded);
            if(rms>0.008) {_speech=true;_lastSpeech=DateTime.UtcNow;}
            // Retain only half a second of silence before speech.
            if(!_speech && _chunk.Length>16000) {
                var data=_chunk.ToArray();_chunk.SetLength(0);_chunk.Write(data,data.Length-16000,16000);
            }
        }
    }
    void StopOnEnter()
    {
        _recording=false;_pendingInjection=false;_canRestoreRaw=false;
        _popupVersion++;_clickVersion++;
        lock(_audioLock) {_chunk.SetLength(0);_speech=false;}
        var capture=_capture;_capture=null;
        Hide();
        try {capture?.StopRecording();}catch {}finally {capture?.Dispose();}
        MicButton.Content="\u25cf";
        MicButton.Background=new SolidColorBrush(Color.FromRgb(99,102,241));
        Hide();
    }
    async Task StopRecording()
    {
        int generation=_generation;
        _recording=false;_stopping=true;
        MicButton.Content="…";
        SetStatus("Derniers mots…");
        _capture?.StopRecording();_capture?.Dispose();_capture=null;
        await _inference.WaitAsync();
        try {
            byte[] audio;bool speech;
            lock(_audioLock) {audio=_chunk.ToArray();speech=_speech;_chunk.SetLength(0);_speech=false;}
            if(speech && audio.Length>=16000) {
                var text=await _engine.Transcribe(audio,_committed,_session.Token);
                if(generation!=_generation || _dismissedOnEnter)return;
                if(_target is {IsParsec:true} && !_interactionTest && _settings.cleanupEnabled && !string.IsNullOrWhiteSpace(text)) {
                    SetStatus("Parsec : nettoyage avant insertion");
                    var cleaned=await _engine.Cleanup(text,_session.Token);
                    if(generation!=_generation || _dismissedOnEnter)return;
                    text=cleaned.text;
                }
                _committed=Join(_committed,text);
            }
            if(generation!=_generation || _dismissedOnEnter)return;
            _raw=_committed;
            await ShowTranscript(_raw);
            if(generation!=_generation || _dismissedOnEnter)return;
            if(!_interactionTest && _settings.cleanupEnabled && !string.IsNullOrWhiteSpace(_raw) && _target is not {IsParsec:true}) {
                SetStatus("Nettoyage intelligent · local");
                var result=await _engine.Cleanup(_raw,_session.Token);
                if(generation!=_generation || _dismissedOnEnter)return;
                _clean=result.text;
                await ShowTranscript(_clean);
                if(result.applied && _clean!=_raw)_canRestoreRaw=true;
                SetStatus(_target is {Owned:true}?"Terminé":"Insertion suspendue");
            } else {
                _clean=_raw;
                SetStatus(string.IsNullOrWhiteSpace(_raw)?"Aucune parole détectée":_target is {IsParsec:true}?"Parsec : phrases envoyees":"Terminé · dictée brute");
            }

        } catch(OperationCanceledException) {}
        catch(Exception ex) {
            _display=_raw;

            SetStatus("Brut conservé · "+ex.Message,true);
        } finally {
            _inference.Release();_stopping=false;
            if(!_dismissedOnEnter)ShowBriefly(1000);
            MicButton.Content="●";
            MicButton.Background=new SolidColorBrush(Color.FromRgb(99,102,241));
        }
    }
    async void ToggleClick(object sender,RoutedEventArgs e)=>await Toggle();
    async Task RawClick()
    {
        if(_recording||_stopping)return;
        await ShowTranscript(_raw);
        _canRestoreRaw=false;
        SetStatus(_target is {Owned:true}?"Dictee brute restauree":"Insertion suspendue");
        ShowBriefly();
    }
    async void HideClick(object sender,RoutedEventArgs e)
    {
        if(_recording)await StopRecording();
        Hide();
    }
    public void InitializeHidden() => new WindowInteropHelper(this).EnsureHandle();

    void PositionNearTarget()
    {
        var anchor=_target?.PopupAnchor();
        if(anchor is not null) {
            PopupPlacement.Place(_handle,anchor.Value.field,anchor.Value.caretX,Width,Height);
        } else {
            var mouse=Forms.Cursor.Position;
            PopupPlacement.Place(_handle,new Rect(mouse.X,mouse.Y,1,1),mouse.X,Width,Height);
        }
    }
    void ShowBriefly(int milliseconds=2500)
    {
        int version=++_popupVersion;
        PositionNearTarget();Show();
        _=HideAfterDelay(version,milliseconds);
    }
    async Task HideAfterDelay(int version,int milliseconds)
    {
        await Task.Delay(milliseconds);
        if(version==_popupVersion && !_recording && !_stopping)Hide();
    }
    async Task RetargetAfterClick()
    {
        if(!_recording)return;
        int version=++_clickVersion;
        await Task.Delay(200); // After button release; the second click supersedes the first.
        if(version!=_clickVersion || !_recording || _stopping || !Native.MouseReleased())return;
        try {
            var target=await TextTarget.CaptureAsync(_settings.parsecEnabled,_settings.parsecKeyboardLayout);
            if(version!=_clickVersion || !_recording || _stopping)return;
            _target=target;
            _retargeting=false;
            PositionNearTarget();
            SetStatus(target.IsParsec?"Parsec : ecoute, phrases finalisees":"Ecoute dans le champ actif");
        } catch(Exception ex) {
            if(version==_clickVersion) {_retargeting=false;SetStatus(ex.Message,true);}
        }
    }
    void DragBar(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is Button)return;
        try {DragMove();} catch {}
    }
    void SettingsClick(object sender,RoutedEventArgs e)=>OpenSettings();
    void RefreshLanguage()
    {
        Ui.Language=_settings.interfaceLanguage=="fr"?"fr":"en";
        Native.ParsecModeEnabled=_settings.parsecEnabled;
        Title=Ui.Text("Dict\u00e9e locale");
        ShortcutText.Text=Ui.HotkeyLabel+" \u00b7 100 % local";
        MicButton.ToolTip=Ui.Text("D\u00e9marrer / arr\u00eater");
        SettingsButton.ToolTip=Ui.Text("R\u00e9glages");
        HideButton.ToolTip=Ui.Text("Arr\u00eater et masquer");
        StatusText.Text=Ui.Text(StatusText.Text);
    }
    void OpenSettings()
    {
        if(_settingsWindow is not null) {_settingsWindow.Activate();return;}
        if(_recording || _stopping) {
            // Opening settings must always work: keep the displayed text and stop listening.
            _dismissedOnEnter=true;_session.Cancel();Interlocked.Increment(ref _generation);
            _target?.Detach("R\u00e9glages ouverts.");StopOnEnter();
        }
        _target?.Detach("R\u00e9glages ouverts.");
        var panel=new StackPanel {Margin=new Thickness(22)};
        panel.Children.Add(new TextBlock {Text=Ui.Text("Raccourci global"),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        panel.Children.Add(new TextBlock {Text=Ui.HotkeyLabel,Margin=new Thickness(0,0,0,14)});
        panel.Children.Add(new TextBlock {Text=Ui.Text("Langue de l'interface"),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var language=new ComboBox {Name="InterfaceLanguage",Margin=new Thickness(0,0,0,14)};
        language.Items.Add("English");language.Items.Add("Fran\u00e7ais");
        language.SelectedIndex=Ui.Language=="fr"?1:0;panel.Children.Add(language);
        panel.Children.Add(new TextBlock {Text="Microphone",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var mic=new ComboBox {Name="MicrophoneChoice",Margin=new Thickness(0,0,0,14)};
        mic.Items.Add(Ui.Text("Microphone par d\u00e9faut de Windows"));
        for(int i=0;i<WaveIn.DeviceCount;i++)mic.Items.Add(WaveIn.GetCapabilities(i).ProductName);
        mic.SelectedIndex=Math.Clamp(_settings.microphoneIndex+1,0,mic.Items.Count-1);panel.Children.Add(mic);
        var clean=new CheckBox {Content=Ui.Text("Nettoyer h\u00e9sitations et corrections orales \u00e0 l'arr\u00eat"),IsChecked=_settings.cleanupEnabled,Margin=new Thickness(0,0,0,14)};
        panel.Children.Add(clean);
        var parsec=new CheckBox {Name="ParsecMode",Content=Ui.Text("Activer le mode Parsec (experimental)"),IsChecked=_settings.parsecEnabled,Margin=new Thickness(0,0,0,8)};
        panel.Children.Add(parsec);
        panel.Children.Add(new TextBlock {Text=Ui.Text("Parsec : phrases terminees uniquement. Clique dans le champ distant. Les mots envoyes ne peuvent pas etre corriges automatiquement."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),Foreground=Brushes.DimGray});
        panel.Children.Add(new TextBlock {Text=Ui.Text("Clavier du PC distant"),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var remoteLayout=new ComboBox {Name="ParsecKeyboardLayout",Margin=new Thickness(0,0,0,14)};
        remoteLayout.Items.Add(Ui.Text("Identique au clavier local"));
        remoteLayout.Items.Add("Fran\u00e7ais (AZERTY)");remoteLayout.Items.Add("English (US)");
        remoteLayout.SelectedIndex=_settings.parsecKeyboardLayout=="fr"?1:_settings.parsecKeyboardLayout=="en"?2:0;
        panel.Children.Add(remoteLayout);
        var save=new Button {Name="SaveSettings",Content=Ui.Text("Enregistrer"),Padding=new Thickness(14,8,14,8),Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(99,102,241))};
        panel.Children.Add(save);
        var dialog=new Window {Title=Ui.Text("R\u00e9glages \u00b7 Dict\u00e9e locale"),Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Width=480,MaxHeight=SystemParameters.WorkArea.Height-50,SizeToContent=SizeToContent.Height,
            Icon=Icon,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowActivated=true,Topmost=true};
        _settingsWindow=dialog;
        dialog.Closed+=(_,_)=>_settingsWindow=null;
        dialog.Loaded+=(_,_)=> {dialog.Activate();language.Focus();};
        save.Click+=(_,_)=> {
            try {
                RegisterShortcut();
                if(!_hotkeyRegistered) {MessageBox.Show(dialog,Ui.Text("Ce raccourci est d\u00e9j\u00e0 utilis\u00e9. Choisis-en un autre."));return;}
                _settings.microphoneIndex=mic.SelectedIndex-1;_settings.cleanupEnabled=clean.IsChecked==true;
                _settings.interfaceLanguage=language.SelectedIndex==1?"fr":"en";
                _settings.parsecEnabled=parsec.IsChecked==true;
                _settings.parsecKeyboardLayout=remoteLayout.SelectedIndex==1?"fr":remoteLayout.SelectedIndex==2?"en":"local";
                _settings.Save();RefreshLanguage();
                _tray?.Dispose();_trayIcon?.Dispose();SetupTray();
                dialog.Close();
            } catch(Exception ex) {MessageBox.Show(dialog,ex.Message);}
        };
        dialog.Show();
    }
    void OnClosing(object? sender,CancelEventArgs e) {if(!_quitting){e.Cancel=true;Hide();}}


    public async Task InteractionTest()
    {
        var results=new List<object>();
        var first=new TextBox {Text="Avant | apres",Margin=new Thickness(15),FontSize=18};
        var second=new TextBox {Text="Autre : ",Margin=new Thickness(15),FontSize=18};
        var panel=new StackPanel();panel.Children.Add(first);panel.Children.Add(second);
        var editor=new Window {Title="Dictation interaction test",Content=panel,Width=500,Height=220,Topmost=true,Left=100,Top=100};
        var originalMouse=Forms.Cursor.Position;
        string originalLanguage=_settings.interfaceLanguage;
        void Check(bool passed,string test) {results.Add(new {test,passed});if(!passed)throw new InvalidOperationException(test);}
        try {
            Check(!IsVisible,"hidden-on-startup");
            bool initialStartup=StartupRegistration.Enabled;
            try {
                var startupItem=_tray!.ContextMenuStrip!.Items.OfType<Forms.ToolStripMenuItem>().Single(i=>i.Text==Ui.Text("Lancer au d\u00e9marrage de Windows"));
                startupItem.PerformClick();
                Check(StartupRegistration.Enabled!=initialStartup && startupItem.Checked==StartupRegistration.Enabled,"tray-startup-option-toggles-registration");
                startupItem.PerformClick();
                Check(StartupRegistration.Enabled==initialStartup,"tray-startup-option-restores-original-state");
                Check(StartupRegistration.Command=="\""+Path.Combine(Paths.Root,"app","DicteeLocale.exe")+"\"","startup-command-quotes-executable");
            } finally {StartupRegistration.SetEnabled(initialStartup);}
            Check(_trayIcon is not null && Icon is not null && _hotkeyRegistered,"custom-icon-and-shortcut-available");
            editor.Show();Native.ActivateTestWindow(new WindowInteropHelper(editor).Handle);
            editor.Activate();first.Focus();first.CaretIndex=6;await Task.Delay(200);
            // Click the actual non-activating microphone button, without opening audio in test mode.

            Show();await Task.Delay(100);
            var mic=MicButton.PointToScreen(new Point(MicButton.ActualWidth/2,MicButton.ActualHeight/2));
            Native.ClickAt((int)mic.X,(int)mic.Y);await Task.Delay(250);
            Check(_recording && _target is {Owned:true},"mic-button-keeps-editor-target");

            Check(first.IsKeyboardFocused,"popup-does-not-take-keyboard-focus");
            var firstAnchor=_target!.PopupAnchor()!.Value;
            var firstPopup=PopupPlacement.Bounds(_handle);
            Check(firstPopup.Bottom<=firstAnchor.field.Top,"popup-above-active-text-field");
            var upperEdge=PopupPlacement.Calculate(new Rect(1250,0,40,20),1280,new Rect(0,0,1280,720),390,80,10);
            Check(upperEdge.Top==30 && upperEdge.Right<=1280,"top-and-right-edge-use-below-and-clamp");
            var otherMonitor=PopupPlacement.Calculate(new Rect(-1000,200,400,100),-980,new Rect(-1280,0,1280,720),390,80,10);
            Check(otherMonitor.Left>=-1280 && otherMonitor.Right<=0 && otherMonitor.Bottom<200,"negative-monitor-coordinates");
            _committed=_raw="bonjour";await ShowTranscript(_raw);await Task.Delay(150);
            Check(first.Text=="Avant bonjour| apres","live-transcript-in-active-editor");
            var point=second.PointToScreen(new Point(second.ActualWidth-10,second.ActualHeight/2));
            Native.ClickAt((int)point.X,(int)point.Y);await Task.Delay(400);

            Check(_target is {Owned:true} && _display=="","click-retargets-without-replaying-old-text");
            var secondAnchor=_target!.PopupAnchor()!.Value;
            var secondPopup=PopupPlacement.Bounds(_handle);
            Check(secondPopup.Bottom<=secondAnchor.field.Top && secondPopup.Top!=firstPopup.Top,"popup-follows-new-text-field");
            _committed=_raw="nouveau champ";await ShowTranscript(_raw);await Task.Delay(150);
            Check(second.Text=="Autre : nouveau champ" && first.Text=="Avant bonjour| apres","dictation-follows-click-to-other-editor");
            for(int i=0;i<6;i++) {
                var box=i%2==0?first:second;
                var end=box.PointToScreen(new Point(box.ActualWidth-10,box.ActualHeight/2));
                var previousToken=_session.Token;
                var pendingRequest=Task.Delay(TimeSpan.FromMinutes(2),previousToken);
                Native.ClickAt((int)end.X,(int)end.Y);await Task.Delay(350);
                Check(previousToken.IsCancellationRequested && pendingRequest.IsCanceled && _target is {Owned:true} && !_retargeting,"return-to-field-"+i);
                string before=box.Text;
                _committed=_raw=" suite";await ShowTranscript(_raw);
                if(box.Text!=before+" suite")results.Add(new {test="return-insertion-diagnostic",before,after=box.Text,caret=box.CaretIndex,owned=_target?.Owned,reason=_target?.Reason,modifiersReleased=Native.ModifiersReleased()});
                Check(box.Text==before+" suite","continued-dictation-after-return-"+i);
            }
            first.IsReadOnly=true;
            var readOnlyPoint=first.PointToScreen(new Point(10,first.ActualHeight/2));
            Native.ClickAt((int)readOnlyPoint.X,(int)readOnlyPoint.Y);await Task.Delay(350);
            Check(!_retargeting && _target is {Owned:false},"unsupported-field-suspends-without-freezing");
            first.IsReadOnly=false;
            Native.ClickAt((int)readOnlyPoint.X,(int)readOnlyPoint.Y);await Task.Delay(350);
            Check(!_retargeting && _target is {Owned:true},"click-recovers-from-unavailable-field-without-restart");
            second.Text="Avant mauvais mot apres";await Task.Delay(100);
            var word=second.GetRectFromCharacterIndex(8);
            var wordPoint=second.PointToScreen(new Point(word.X+1,word.Y+word.Height/2));
            Native.ClickAt((int)wordPoint.X,(int)wordPoint.Y);await Task.Delay(70);
            Native.ClickAt((int)wordPoint.X,(int)wordPoint.Y);await Task.Delay(350);
            results.Add(new {test="double-click-diagnostic",selected=second.SelectedText,owned=_target?.Owned,reason=_target?.Reason,retargeting=_retargeting});
            Check(second.SelectedText.Trim()=="mauvais" && _target is {Owned:true},"double-click-selects-word-and-retargets");
            _committed=_raw="correct";await ShowTranscript(_raw);
            Check(second.Text=="Avant correct mot apres","double-click-selection-replaced-by-dictation");
            var selectionStart=second.GetRectFromCharacterIndex(6);
            var selectionEnd=second.GetRectFromCharacterIndex(17);
            var start=second.PointToScreen(new Point(selectionStart.X,selectionStart.Y+selectionStart.Height/2));
            var finish=second.PointToScreen(new Point(selectionEnd.X,selectionEnd.Y+selectionEnd.Height/2));
            await Task.Delay(650); // A drag must not be interpreted as the third click of a double-click.
            Native.HoldMouseInTest((int)start.X,(int)start.Y);await Task.Delay(500);
            Check(_retargeting && _target is {Owned:false},"held-mouse-keeps-old-target-detached");
            Native.MoveMouseInTest((int)finish.X,(int)finish.Y);await Task.Delay(150);
            Native.ReleaseMouseInTest((int)finish.X,(int)finish.Y);await Task.Delay(350);
            results.Add(new {test="drag-diagnostic",selected=second.SelectedText,owned=_target?.Owned,reason=_target?.Reason,retargeting=_retargeting});
            Check(second.SelectedText=="correct mot" && _target is {Owned:true},"drag-selection-captured-after-release");
            _committed=_raw="bons mots";await ShowTranscript(_raw);
            Check(second.Text=="Avant bons mots apres","multiple-selected-words-replaced-by-dictation");
            _committed=_raw="bons mots ensuite";await ShowTranscript(_raw);
            Check(second.Text=="Avant bons mots ensuite apres","same-dictation-continues-after-selection");
            await StopRecording();await Task.Delay(1200);

            Check(!IsVisible && !_recording,"stop-hides-popup-and-stops-recording");
            editor.Activate();second.Focus();second.CaretIndex=second.Text.Length;await Task.Delay(100);
            await Toggle();_committed=_raw=" texte a envoyer";await ShowTranscript(_raw);
            int submissions=0;
            System.Windows.Input.KeyEventHandler submit=(_,args)=> {
                if(args.Key==Key.Enter) {submissions++;second.Clear();args.Handled=true;}
            };
            second.PreviewKeyDown+=submit;
            Native.PressEnterInTest();await Task.Delay(250);
            second.PreviewKeyDown-=submit;
            Check(submissions==1,"enter-is-forwarded-once-to-editor");
            Check(!_recording && !IsVisible && _target is {Owned:false},"enter-stops-recording-and-hides-without-cleanup");
            await ShowTranscript("late transcription must not appear");await Task.Delay(150);

            Check(second.Text=="" && !IsVisible,"no-late-insertion-or-popup-after-submit");
            second.Focus();await Toggle();_committed=_raw="second message";await ShowTranscript(_raw);
            await _inference.WaitAsync();
            var finishing=StopRecording(); // Hold a pending inference while Enter submits.
            second.PreviewKeyDown+=submit;
            Native.PressEnterInTest();await Task.Delay(150);
            second.PreviewKeyDown-=submit;
            _inference.Release();await finishing;await Task.Delay(1100);
            Check(second.Text=="" && !IsVisible && !_recording && !_stopping,"enter-during-finishing-never-reopens-popup");
            second.Focus();await Toggle();_committed=_raw="settings preserve this text";await ShowTranscript(_raw);
            string beforeSettings=second.Text;
            var gear=SettingsButton.PointToScreen(new Point(SettingsButton.ActualWidth/2,SettingsButton.ActualHeight/2));
            Native.ClickAt((int)gear.X,(int)gear.Y);await Task.Delay(300);
            Check(!_recording && _settingsWindow is {IsVisible:true} && second.Text==beforeSettings,"gear-opens-settings-during-dictation-with-text-preserved");
            var settingsPanel=(StackPanel)((ScrollViewer)_settingsWindow!.Content).Content;
            var language=settingsPanel.Children.OfType<ComboBox>().Single(c=>c.Name=="InterfaceLanguage");
            language.SelectedIndex=0;
            settingsPanel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Ui.Language=="en" && Settings.Load().interfaceLanguage=="en" && MicButton.ToolTip as string=="Start / stop","english-interface-applies-and-persists");
            Check(_tray!.ContextMenuStrip!.Items.OfType<Forms.ToolStripMenuItem>().Any(i=>i.Text=="Quit completely"),"english-tray-translated");
            OpenSettings();
            settingsPanel=(StackPanel)((ScrollViewer)_settingsWindow!.Content).Content;
            Check(settingsPanel.Children.OfType<ComboBox>().Single(c=>c.Name=="MicrophoneChoice").Items[0] as string=="Windows default microphone","english-settings-translated");
            settingsPanel.Children.OfType<ComboBox>().Single(c=>c.Name=="InterfaceLanguage").SelectedIndex=1;
            settingsPanel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Ui.Language=="fr" && MicButton.ToolTip as string=="D\u00e9marrer / arr\u00eater","french-interface-restored-without-restart");
            Native.ActivateTestWindow(new WindowInteropHelper(editor).Handle);second.Focus();second.Text="Avant mauvais apres";second.Select(6,7);await Task.Delay(150);
            var remote=TextTarget.CaptureRemoteForTest();
            _target=remote;_recording=true;_committed=_raw=_display="";
            lock(_audioLock) {_chunk.SetLength(0);_chunk.Write(new byte[24000]);_speech=true;_lastSpeech=DateTime.UtcNow;}
            Tick(null,EventArgs.Empty);await Task.Delay(100);
            Check(second.Text=="Avant mauvais apres" && _display=="" && remote.Owned,"parsec-does-not-decode-or-insert-unfinished-preview");
            _recording=false;lock(_audioLock){_chunk.SetLength(0);_speech=false;}
            Check(await remote.UpdateAsync("correct") && second.Text=="Avant correct apres","parsec-keyboard-replaces-selection-without-backspace");
            Check(await remote.UpdateAsync("correct and run the tests") && second.Text=="Avant correct and run the tests apres","parsec-append-only-continuation");
            Check(await remote.UpdateAsync("correct and run the tests? Yes!") && second.Text=="Avant correct and run the tests? Yes! apres","parsec-uppercase-and-punctuation-keyboard-input");
            var frenchPlan=Native.BuildParsecStrokes("éèàç ?",Native.ParsecKeyboardLayout("fr"));
            Check(frenchPlan.Count==6,"parsec-french-azerty-characters-supported");
            string preserved=second.Text;
            Check(!await remote.UpdateAsync("rewrite existing text") && second.Text==preserved,"parsec-never-rewrites-sent-text");
            remote=TextTarget.CaptureRemoteForTest();
            bool unsupported=false;
            try {await remote.UpdateAsync("prefix \ud83d\ude42");}catch(InvalidOperationException){unsupported=true;}
            Check(unsupported && second.Text==preserved,"parsec-preflights-unsupported-characters-before-insertion");
            remote=TextTarget.CaptureRemoteForTest();
            string slow=new string('x',80);
            var interrupted=remote.UpdateAsync(slow);await Task.Delay(100);remote.Detach("test interrupt");
            await interrupted;int interruptedLength=second.Text.Length;await Task.Delay(200);
            Check(second.Text.Length==interruptedLength && interruptedLength<preserved.Length+slow.Length,"parsec-stops-injection-immediately-when-detached");
            Native.ParsecModeEnabled=true;Native.SetParsecWindowForTest(new WindowInteropHelper(editor).Handle);
            int shortcutEvents=0;
            var actualShortcut=Native.ParsecShortcutPressed;
            Native.ParsecShortcutPressed=()=>shortcutEvents++;
            second.Text="";second.Focus();Native.PressShortcutInTest();await Task.Delay(150);
            Native.ParsecShortcutPressed=actualShortcut;Native.SetParsecWindowForTest(IntPtr.Zero);
            Check(shortcutEvents==1 && second.Text=="","parsec-shortcut-handled-once-without-forwarding-D");


        } catch(Exception ex) {results.Add(new {test="exception",passed=false,error=ex.ToString()});}
        finally {
            Native.SetParsecWindowForTest(IntPtr.Zero);
            _settings.interfaceLanguage=originalLanguage;_settings.Save();
            Forms.Cursor.Position=originalMouse;
            File.WriteAllText(Path.Combine(Paths.Root,"test-results","interaction.json"),System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            editor.Close();Quit();
        }
    }
    public async Task ParsecProbe()
    {
        var results=new List<object>();
        try {
            var handle=Native.FindParsecWindow();
            results.Add(new {test="actual-parsec-window-detected",passed=handle!=IntPtr.Zero});
            if(handle!=IntPtr.Zero) {
                var target=await Task.Run(()=>TextTarget.CaptureParsecMetadataForProbe(handle,_settings.parsecKeyboardLayout)).WaitAsync(TimeSpan.FromSeconds(2));
                results.Add(new {test="opaque-parsec-window-accepted-without-injection",passed=target.IsParsec && !target.Owned});
                var strokes=Native.BuildParsecStrokes("Je vais push la branche and then run the tests.",Native.ParsecKeyboardLayout(_settings.parsecKeyboardLayout));
                results.Add(new {test="remote-typing-plan-without-injection",passed=strokes.Count>0});
            }
        } catch(Exception ex) {results.Add(new {test="exception",passed=false,error=ex.ToString()});}
        File.WriteAllText(Path.Combine(Paths.Root,"test-results","parsec-probe.json"),System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
        Quit();
    }
    async Task SmokeTest()
    {
        var results=new List<object>();
        try {
            for(int i=0;i<120 && !_ready;i++)await Task.Delay(500);
            var health=await _engine.Health();
            results.Add(new {test="dedicated-tray-and-executable-icon",passed=_trayIcon is not null && Icon is not null});
            results.Add(new {test="startup-option-and-quit-in-menu",passed=_tray?.ContextMenuStrip?.Items.OfType<Forms.ToolStripMenuItem>().Any(i=>i.Text==Ui.Text("Lancer au d\u00e9marrage de Windows"))==true && _tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().Any(i=>i.Text==Ui.Text("Quitter compl\u00e8tement"))});
            results.Add(new {test="hidden-at-idle",passed=!IsVisible});
            results.Add(new {test="engine-ready",passed=health.GetProperty("ready").GetBoolean(),health});
            results.Add(new {test="global-hotkey",passed=_hotkeyRegistered,shortcut=_settings.hotkeyLabel});
            results.Add(new {test="toolbar-no-activation",passed=(Native.GetWindowLongPtr(_handle,-20).ToInt64()&0x08000000)!=0});
            results.Add(new {test="microphone-devices",passed=WaveIn.DeviceCount>0,count=WaveIn.DeviceCount,
                names=Enumerable.Range(0,WaveIn.DeviceCount).Select(i=>WaveIn.GetCapabilities(i).ProductName).ToArray()});
            results.Add(new {test="http-authentication",passed=await _engine.RejectsAnonymous()});
            using var wave=new WaveFileReader(Path.Combine(Paths.Root,"tests","fixtures","franglais.wav"));
            var pcm=new byte[(int)wave.Length];int total=0,read;
            while(total<pcm.Length && (read=wave.Read(pcm,total,pcm.Length-total))>0)total+=read;
            string raw=await _engine.Transcribe(pcm,"");
            var clean=await _engine.Cleanup("Euh "+raw);
            results.Add(new {test="end-to-end-franglais",passed=clean.text.Contains("branche")&&clean.text.Contains("run the tests"),raw,clean=clean.text});
            Show();await Task.Delay(100);
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var picture=File.Create(Path.Combine(Paths.Root,"test-results","bar.png"));encoder.Save(picture);
            ShowBriefly(100);await Task.Delay(200);
            results.Add(new {test="popup-auto-hide",passed=!IsVisible});
        } catch(Exception ex) {results.Add(new {test="exception",passed=false,error=ex.ToString()});}
        File.WriteAllText(Path.Combine(Paths.Root,"test-results","application.json"),System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
    }
    void Quit()
    {
        _quitting=true;_timer.Stop();_recording=false;
        _capture?.Dispose();Native.UnregisterHotKey(_handle,Native.HOTKEY_ID);Native.StopHooks();
        _session.Cancel();_engine.Dispose();_tray?.Dispose();_trayIcon?.Dispose();
        Application.Current.Shutdown();
    }
}
