using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Threading.Tasks;
namespace DicteeLocale;

public static class Native
{
    public const int WM_HOTKEY = 0x0312, WM_MOUSEACTIVATE = 0x21;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int HOTKEY_ID = 4815;
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    public static void PressEnterInTest()=>Submit(new List<INPUT> {Key(13),Key(13,2)});
    public static void ClickAt(int x,int y)
    {
        SetCursorPos(x,y);
        Submit(new List<INPUT> {
            new INPUT {Type=0,Data=new INPUTUNION {Mouse=new MOUSEINPUT {Flags=2}}},
            new INPUT {Type=0,Data=new INPUTUNION {Mouse=new MOUSEINPUT {Flags=4}}}});
    }
    public static void HoldMouseInTest(int x,int y)
    {
        SetCursorPos(x,y);
        Submit(new List<INPUT> {new INPUT {Type=0,Data=new INPUTUNION {Mouse=new MOUSEINPUT {Flags=2}}}});
    }
    public static void MoveMouseInTest(int x,int y)=>SetCursorPos(x,y);
    public static void ReleaseMouseInTest(int x,int y)
    {
        SetCursorPos(x,y);
        Submit(new List<INPUT> {new INPUT {Type=0,Data=new INPUTUNION {Mouse=new MOUSEINPUT {Flags=4}}}});
    }
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadKeyboardLayout(string name,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern short VkKeyScanEx(char character,IntPtr layout);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint code,uint mapType,IntPtr layout);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int max);
    static readonly UIntPtr DictationMarker=new(0xD1C7EE);
    public static bool ParsecModeEnabled;
    public static Action? ParsecShortcutPressed;
    static bool _parsecShortcutHeld;
    static IntPtr _parsecTestWindow;
    public static void SetParsecWindowForTest(IntPtr hwnd)
    {
        if(hwnd!=IntPtr.Zero) {
            GetWindowThreadProcessId(hwnd,out uint process);
            if(process!=(uint)Environment.ProcessId)throw new InvalidOperationException("Test window must belong to this process.");
        }
        _parsecTestWindow=hwnd;
    }
    public static bool IsParsecWindow(IntPtr hwnd)
    {
        if(hwnd==IntPtr.Zero)return false;
        if(hwnd==_parsecTestWindow)return true;
        var name=new System.Text.StringBuilder(64);GetClassName(hwnd,name,name.Capacity);
        if(name.ToString()!="MTY_Window")return false;
        GetWindowThreadProcessId(hwnd,out uint process);
        try {using var app=System.Diagnostics.Process.GetProcessById((int)process);return app.ProcessName.Equals("parsecd",StringComparison.OrdinalIgnoreCase);}
        catch {return false;}
    }
    delegate bool EnumWindowProc(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr parameter);
    [DllImport("user32.dll")] static extern short GetKeyState(int key);
    public static IntPtr FindParsecWindow()
    {
        IntPtr found=IntPtr.Zero;
        EnumWindows((hwnd,_)=> {if(IsParsecWindow(hwnd)){found=hwnd;return false;}return true;},IntPtr.Zero);
        return found;
    }
    public static IntPtr ParsecKeyboardLayout(string choice)
    {
        if(choice=="fr" || choice=="en") {
            var layout=LoadKeyboardLayout(choice=="fr"?"0000040c":"00000409",0x80);
            if(layout==IntPtr.Zero)throw new InvalidOperationException("Disposition de clavier indisponible.");
            return layout;
        }
        return GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(),out _));
    }
    public readonly record struct ParsecStroke(ushort Key,int Modifiers);
    public static List<ParsecStroke> BuildParsecStrokes(string text,IntPtr layout)
    {
        var strokes=new List<ParsecStroke>();
        foreach(char character in text) {
            if(char.IsControl(character) || char.IsSurrogate(character))throw new InvalidOperationException("Parsec : caractere non pris en charge par le clavier distant.");
            short mapping=VkKeyScanEx(character,layout);
            if(mapping==-1 || ((mapping>>8)&255)>7)throw new InvalidOperationException("Parsec : caractere non pris en charge par le clavier distant.");
            ushort key=(ushort)(mapping&255);
            int modifiers=(mapping>>8)&7;
            if(key>=65 && key<=90 && (GetKeyState(20)&1)!=0)modifiers^=1;
            strokes.Add(new ParsecStroke(key,modifiers));
        }
        return strokes;
    }
    static INPUT ScanKey(ushort key,bool up,IntPtr layout)
    {
        uint scan=MapVirtualKeyEx(key,4,layout);
        if(scan==0)throw new InvalidOperationException("Parsec : touche non prise en charge.");
        return new INPUT {Type=1,Data=new INPUTUNION {Keyboard=new KEYBDINPUT {
            Scan=(ushort)(scan&255),Flags=8u|(up?2u:0u)|((scan&0xff00)!=0?1u:0u),Extra=DictationMarker}}};
    }
    public static async Task SendParsecTextAsync(string text,IntPtr layout,Func<bool> owns,Action<char> inserted)
    {
        var strokes=BuildParsecStrokes(text,layout); // Preflight the whole phrase before any input.
        foreach(var stroke in strokes) { // Include physical scan-code validation in the all-or-nothing preflight.
            _=ScanKey(stroke.Key,false,layout);
            if((stroke.Modifiers&1)!=0)_=ScanKey(16,false,layout);
            if((stroke.Modifiers&2)!=0)_=ScanKey(17,false,layout);
            if((stroke.Modifiers&4)!=0)_=ScanKey(18,false,layout);
        }
        for(int i=0;i<strokes.Count;i++) {
            if(!owns() || !ModifiersReleased())throw new OperationCanceledException();
            var stroke=strokes[i];
            var modifiers=new List<ushort>();
            if((stroke.Modifiers&2)!=0)modifiers.Add(17);
            if((stroke.Modifiers&4)!=0)modifiers.Add(18);
            if((stroke.Modifiers&1)!=0)modifiers.Add(16);
            try {
                var down=new List<INPUT>();
                foreach(var modifier in modifiers)down.Add(ScanKey(modifier,false,layout));
                down.Add(ScanKey(stroke.Key,false,layout));Submit(down);
                await Task.Delay(8);
            } finally {
                var up=new List<INPUT> {ScanKey(stroke.Key,true,layout)};
                for(int m=modifiers.Count-1;m>=0;m--)up.Add(ScanKey(modifiers[m],true,layout));
                Submit(up);
            }
            inserted(text[i]);
            await Task.Delay(4);
        }
    }
    public static void PressShortcutInTest()=>Submit(new List<INPUT> {
        Key(17),Key(16),Key(18),Key((ushort)HotkeyKey),Key((ushort)HotkeyKey),Key((ushort)HotkeyKey,2),Key(18,2),Key(16,2),Key(17,2)});
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    static HookProc? _keyboardProc, _mouseProc;
    static IntPtr _keyboardHook, _mouseHook;
    static uint _hookThreadId;
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread,uint message,IntPtr w,IntPtr l);
    public static Action? UserEdited;
    public static Action? EnterPressed;
    public static Action? TargetClicked;
    public static Action? TargetChanging;
    public static int HotkeyKey = 68;
    public static IntPtr ToolbarHandle;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct KBD { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSE { public POINT Point; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION
    { [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT
    { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT
    { public int Dx, Dy; public uint MouseData, Flags, Time; public UIntPtr Extra; }

    public static void StartHooks()
    {
        _keyboardProc = (code, w, l) => {
            if(code>=0) {
                var data=Marshal.PtrToStructure<KBD>(l);
                bool down=w.ToInt64() is 0x100 or 0x104;
                bool up=w.ToInt64() is 0x101 or 0x105;
                if(data.Key==HotkeyKey && data.Extra!=DictationMarker) {
                    if(up && _parsecShortcutHeld) {_parsecShortcutHeld=false;return new IntPtr(1);}
                    if(down && (_parsecShortcutHeld || (ParsecModeEnabled && IsParsecWindow(GetForegroundWindow()) &&
                        (GetAsyncKeyState(16)&0x8000)!=0 && (GetAsyncKeyState(17)&0x8000)!=0 && (GetAsyncKeyState(18)&0x8000)!=0))) {
                        if(!_parsecShortcutHeld) {_parsecShortcutHeld=true;ParsecShortcutPressed?.Invoke();}
                        return new IntPtr(1); // Handle locally; do not forward this D to the remote host.
                    }
                }
            }
            if (code >= 0 && (w.ToInt64() == 0x100 || w.ToInt64() == 0x104)) {
                var data = Marshal.PtrToStructure<KBD>(l);
                if(data.Key==13 && (GetAsyncKeyState(16)&0x8000)==0)EnterPressed?.Invoke();
                else if ((data.Flags & 0x10) == 0 && (data.Key != HotkeyKey ||
                    (GetAsyncKeyState(16)&0x8000)==0 || (GetAsyncKeyState(17)&0x8000)==0 || (GetAsyncKeyState(18)&0x8000)==0) &&
                    data.Key is not (16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165))
                { UserEdited?.Invoke(); }
            }
            return CallNextHookEx(_keyboardHook, code, w, l);
        };
        _mouseProc = (code, w, l) => {
            if (code >= 0 && w.ToInt64() is 0x201 or 0x204 or 0x207 or 0x202 or 0x205 or 0x208) {
                var data = Marshal.PtrToStructure<MOUSE>(l);
                if (GetAncestor(WindowFromPoint(data.Point), 2) != ToolbarHandle)
                {
                    if(w.ToInt64() is 0x201 or 0x204 or 0x207)TargetChanging?.Invoke();
                    else TargetClicked?.Invoke();
                }
            }
            return CallNextHookEx(_mouseHook, code, w, l);
        };
        using var initialized=new System.Threading.ManualResetEventSlim();
        Exception? failure=null;
        var thread=new System.Threading.Thread(()=> {
            try {
                _hookThreadId=GetCurrentThreadId();
                _keyboardHook=SetWindowsHookEx(13,_keyboardProc,GetModuleHandle(null),0);
                _mouseHook=SetWindowsHookEx(14,_mouseProc,GetModuleHandle(null),0);
                if(_keyboardHook==IntPtr.Zero || _mouseHook==IntPtr.Zero)
                    throw new InvalidOperationException("Input hooks could not be installed.");
            } catch(Exception ex) {failure=ex;}
            finally {initialized.Set();}
            try {if(failure is null)System.Windows.Forms.Application.Run();}
            finally {
                if(_keyboardHook!=IntPtr.Zero)UnhookWindowsHookEx(_keyboardHook);
                if(_mouseHook!=IntPtr.Zero)UnhookWindowsHookEx(_mouseHook);
            }
        }) {IsBackground=true,Name="Dictation input hooks"};
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();initialized.Wait();
        if(failure is not null)throw failure;
    }
    public static void StopHooks()
    {
        if(_hookThreadId!=0)PostThreadMessage(_hookThreadId,0x12,IntPtr.Zero,IntPtr.Zero);
    }

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
    public static bool ActivateTestWindow(IntPtr hwnd)
    {
        uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out _),current=GetCurrentThreadId();
        bool attached=foreground!=current && AttachThreadInput(current,foreground,true);
        try {return SetForegroundWindow(hwnd);}
        finally {if(attached)AttachThreadInput(current,foreground,false);}
    }
    public static bool ModifiersReleased() =>
        (GetAsyncKeyState(16) & 0x8000) == 0 && (GetAsyncKeyState(17) & 0x8000) == 0 &&
        (GetAsyncKeyState(18) & 0x8000) == 0 && (GetAsyncKeyState(91) & 0x8000) == 0 && MouseReleased();
    public static bool MouseReleased() => (GetAsyncKeyState(1)&0x8000)==0 &&
        (GetAsyncKeyState(2)&0x8000)==0 && (GetAsyncKeyState(4)&0x8000)==0;

    static INPUT Key(ushort vk, uint flags = 0) =>
        new INPUT {Type=1, Data=new INPUTUNION {Keyboard=new KEYBDINPUT {Vk=vk, Flags=flags}}};
    static void Submit(List<INPUT> input)
    {
        if (input.Count == 0) return;
        if (SendInput((uint)input.Count, input.ToArray(), Marshal.SizeOf<INPUT>()) != input.Count)
            throw new InvalidOperationException("Insertion bloquée par Windows (application administrateur ou champ protégé).");
    }
    public static void ReplaceSuffix(int deleteCount, string suffix)
    {
        var inputs = new List<INPUT>();
        // Backspace is used only inside the owned dictation span, with focus/caret checks.
        for(int i=0;i<deleteCount;i++) {inputs.Add(Key(8)); inputs.Add(Key(8,2));}
        foreach(char c in suffix) {
            if (c is '\r' or '\n') throw new InvalidOperationException("Les retours à la ligne ne sont pas injectés.");
            inputs.Add(new INPUT {Type=1,Data=new INPUTUNION {Keyboard=new KEYBDINPUT {Scan=c,Flags=4}}});
            inputs.Add(new INPUT {Type=1,Data=new INPUTUNION {Keyboard=new KEYBDINPUT {Scan=c,Flags=6}}});
        }
        Submit(inputs);
    }
}

public sealed class TextTarget
{
    public bool IsParsec {get;}
    readonly IntPtr _parsecLayout;
    readonly bool _useTextPattern,_richText;
    readonly AutomationElement _element;
    readonly IntPtr _window;
    readonly int[] _runtime;
    string? _expectedValue;
    TextPatternRange? _expectedCaret;
    volatile bool _owned=true;
    public bool Owned => _owned;
    int _updateFlag;
    int _selectionLength;
    readonly string _selectionLeading="",_selectionTrailing="";
    readonly System.Windows.Rect _field;
    public bool Updating => System.Threading.Volatile.Read(ref _updateFlag)!=0;
    public string Inserted { get; private set; } = "";
    public string? Reason { get; private set; }

    private TextTarget(AutomationElement element,bool allowParsec=false,string layout="local",bool remoteTest=false,IntPtr? probeWindow=null)
    {
        _element=element; _runtime=element.GetRuntimeId(); _window=probeWindow??Native.GetForegroundWindow();
        bool parsec=Native.IsParsecWindow(_window);
        if(parsec && !allowParsec)throw new InvalidOperationException("Parsec : active le mode Parsec dans les reglages.");
        if((parsec && allowParsec) || remoteTest) {
            if(remoteTest && element.Current.ProcessId!=Environment.ProcessId)throw new InvalidOperationException("Remote tests require an owned test editor.");
            IsParsec=true;_parsecLayout=Native.ParsecKeyboardLayout(layout);
            var point=System.Windows.Forms.Cursor.Position;
            _field=new System.Windows.Rect(point.X,point.Y,1,1);
            return;
        }
        if(element.Current.IsPassword) throw new InvalidOperationException("La dictée n'est pas activée dans un mot de passe.");
        if(!element.Current.IsKeyboardFocusable) throw new InvalidOperationException("Place le curseur dans un champ de texte.");
        if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var vp) && ((ValuePattern)vp).Current.IsReadOnly)
            throw new InvalidOperationException("Ce champ est en lecture seule.");
        if(!element.TryGetCurrentPattern(ValuePattern.Pattern,out _) && !element.TryGetCurrentPattern(TextPattern.Pattern,out _))
            throw new InvalidOperationException("Place le curseur dans un champ de texte accessible.");
        bool hasText=element.TryGetCurrentPattern(TextPattern.Pattern,out _);
        // Keep rich-editor reads and caret offsets in the same provider coordinate system.
        // Ordinary native/WPF textboxes retain their existing ValuePattern behavior.
        _richText=hasText && (element.Current.FrameworkId=="Chrome" ||
            element.Current.ClassName.StartsWith("ql-editor",StringComparison.Ordinal));
        _useTextPattern=_richText || (hasText && !element.TryGetCurrentPattern(ValuePattern.Pattern,out _));
        CaptureState();
        _field=element.Current.BoundingRectangle;
        _selectionLength=ReadSelectionLength();
        var selected=ReadCaret()?.GetText(500000)??"";
        if(!string.IsNullOrWhiteSpace(selected)) {
            _selectionLeading=new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.TakeWhile(selected,c=>c==' ')));
            _selectionTrailing=new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Reverse(System.Linq.Enumerable.TakeWhile(System.Linq.Enumerable.Reverse(selected),c=>c==' '))));
        }
        if(_selectionLength>0 && (ReadCaretOffset() is null || _expectedValue is null))
            throw new InvalidOperationException("La selection de ce champ n'est pas accessible.");
    }
    public static TextTarget Capture(bool allowParsec=false,string layout="local") => new TextTarget(AutomationElement.FocusedElement ??
        throw new InvalidOperationException("Aucun champ actif."),allowParsec,layout);
    public static TextTarget CaptureRemoteForTest() => new TextTarget(AutomationElement.FocusedElement!,true,"local",true);
    public static TextTarget CaptureParsecMetadataForProbe(IntPtr hwnd,string layout)
    {
        if(!Native.IsParsecWindow(hwnd))throw new InvalidOperationException("Expected a Parsec window.");
        var target=new TextTarget(AutomationElement.FromHandle(hwnd),true,layout,false,hwnd);
        target.Detach("Diagnostic only; never inject.");
        return target;
    }
    public static async Task<TextTarget> CaptureAsync(bool allowParsec=false,string layout="local")
    {
        try {return await Task.Run(()=>Capture(allowParsec,layout)).WaitAsync(TimeSpan.FromSeconds(2));}
        catch(TimeoutException) {throw new InvalidOperationException("Le champ ne repond pas. Clique dans un autre champ pour reprendre.");}
    }
    int ReadSelectionLength()
    {
        var selection=ReadCaret();
        return selection is null?0:Normalize(selection.GetText(500000)).Length;
    }

    public (System.Windows.Rect field,double caretX)? PopupAnchor()
    {
        try {
            var field=_field;
            if(field.IsEmpty || field.Width<=0 || field.Height<=0)return null;
            double x=field.Left+field.Width/2;
            var caret=PopupPlacement.CaretPosition();
            if(caret is not null && caret.Value.X>=field.Left && caret.Value.X<=field.Right)
                x=caret.Value.X;
            return (field,x);
        } catch {return null;}
    }
    public void Detach(string reason) { _owned=false; Reason=reason; }
    string? ReadValue()
    {
        try {
            if(_useTextPattern) {
                if(_element.TryGetCurrentPattern(TextPattern.Pattern,out var tp))return Normalize(((TextPattern)tp).DocumentRange.GetText(500000));
                return null; // Do not switch coordinate systems if a provider disappears.
            }
            if(_element.TryGetCurrentPattern(ValuePattern.Pattern,out var vp)) return Normalize(((ValuePattern)vp).Current.Value);
        } catch {}
        return null;
    }
    TextPatternRange? ReadCaret()
    {
        try {
            if(_element.TryGetCurrentPattern(TextPattern.Pattern,out var tp)) {
                var ranges=((TextPattern)tp).GetSelection();
                if(ranges.Length==1) return ranges[0].Clone();
            }
        } catch {}
        return null;
    }

    static string Normalize(string text)=>EditorText.Normalize(text);
    bool SameDocument(string? first,string? second)=>EditorText.SameDocument(first,second,_richText);
    int? ReadCaretOffset()
    {
        try {
            if(_element.TryGetCurrentPattern(TextPattern.Pattern,out var tp)) {
                var pattern=(TextPattern)tp;
                var selection=pattern.GetSelection();
                if(selection.Length!=1)return null;
                var prefix=pattern.DocumentRange.Clone();
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,selection[0],TextPatternRangeEndpoint.Start);
                return Normalize(prefix.GetText(500000)).Length;
            }
        } catch {}
        return null;
    }
    bool FocusMatches()
    {
        try {
            var focus=AutomationElement.FocusedElement;
            return Native.GetForegroundWindow()==_window && focus is not null &&
                System.Linq.Enumerable.SequenceEqual(_runtime,focus.GetRuntimeId());
        } catch {return false;}
    }
    void CaptureState() { _expectedValue=ReadValue(); _expectedCaret=ReadCaret(); }
    public bool Check()
    {
        if(!Owned) return false;
        try {
            var focus=AutomationElement.FocusedElement;
            if(Native.GetForegroundWindow()!=_window || focus==null ||
                !System.Linq.Enumerable.SequenceEqual(_runtime,focus.GetRuntimeId())) {
                Detach("Le champ actif a changé."); return false;
            }
            if(IsParsec)return true;
            if(_expectedValue is not null && !SameDocument(ReadValue(),_expectedValue)) {
                Detach("Le texte a été modifié : insertion suspendue."); return false;
            }
            var caret=ReadCaret();
            if(_expectedCaret is not null && caret is not null &&
               (caret.CompareEndpoints(TextPatternRangeEndpoint.Start,_expectedCaret,TextPatternRangeEndpoint.Start)!=0 ||
                caret.CompareEndpoints(TextPatternRangeEndpoint.End,_expectedCaret,TextPatternRangeEndpoint.End)!=0)) {
                Detach("Le curseur a été déplacé."); return false;
            }
            return true;
        } catch { Detach("Le champ n'est plus accessible."); return false; }
    }
    public async Task<bool> UpdateAsync(string next)
    {
        if(System.Threading.Interlocked.CompareExchange(ref _updateFlag,1,0)!=0)return false;
        try {return await Task.Run(()=>UpdateCoreAsync(next)).WaitAsync(TimeSpan.FromSeconds(IsParsec?Math.Min(60,4+next.Length*.025):4));}
        catch(TimeoutException) {Detach("Le champ ne repond pas : clique pour reprendre.");return false;}
        finally {System.Threading.Interlocked.Exchange(ref _updateFlag,0);}
    }
    async Task<bool> UpdateCoreAsync(string next)
    {
        next=Normalize(next).Replace("\r"," ").Replace("\n"," ");
        if(!Check()) return false;
        if(!Native.ModifiersReleased()) return false;
        if(IsParsec) {
            if(!next.StartsWith(Inserted,StringComparison.Ordinal)) {
                Detach("Parsec : le texte deja envoye ne peut pas etre revise.");return false;
            }
            string remoteSuffix=next.Substring(Inserted.Length);
            try {
                await Native.SendParsecTextAsync(remoteSuffix,_parsecLayout,()=>Owned && Native.GetForegroundWindow()==_window,c=>Inserted+=c);
                return Owned;
            } catch(OperationCanceledException) {
                Detach("Parsec : insertion interrompue, clique pour reprendre.");return false;
            }
        }
        if(next.Length==0 && Inserted.Length==0)return true;
        if(_selectionLeading.Length>0 && !next.StartsWith(_selectionLeading,StringComparison.Ordinal))next=_selectionLeading+next;
        if(_selectionTrailing.Length>0 && !next.EndsWith(_selectionTrailing,StringComparison.Ordinal))next+=_selectionTrailing;
        if(next==Inserted)return true;
        int prefix=0;
        while(prefix<Inserted.Length && prefix<next.Length && Inserted[prefix]==next[prefix]) prefix++;
        // Do not split an astral/combining character when deleting a tail.
        var oldTail=Inserted.Substring(prefix);
        foreach(char c in oldTail) {
            if(char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) {
                Detach("Révision Unicode suspendue."); return false;
            }
        }

        string suffix=next.Substring(prefix);
        string? before=ReadValue();
        int? caretOffset=ReadCaretOffset();
        string? expected=EditorText.AfterEdit(before,caretOffset,_selectionLength,oldTail,suffix);
        if(oldTail.Length>0 && before is not null && caretOffset is not null && expected is null) {
            Detach("Le texte au curseur ne correspond plus a la dictee.");return false;
        }
        // Recheck ownership after potentially slow provider calls, immediately before injection.
        if(!FocusMatches() || !Owned || !Native.ModifiersReleased())return false;
        Native.ReplaceSuffix(oldTail.Length,suffix);
        // SendInput queues events. An Electron editor may take hundreds of ms to
        // publish the resulting value; never capture the old value as our new baseline.
        var deadline=DateTime.UtcNow.AddMilliseconds(1800);
        while(DateTime.UtcNow<deadline) {
            await Task.Delay(30);
            if(!Owned)return false;
            if(!FocusMatches()) {Detach("Le champ actif a change.");return false;}
            var value=ReadValue();
            bool confirmed=expected is not null?SameDocument(value,expected):
                before is not null && value is not null && value!=before &&
                value.Length==before.Length-oldTail.Length-_selectionLength+suffix.Length && value.Contains(next,StringComparison.Ordinal);
            if(!confirmed)continue;
            _selectionLength=0;
            Inserted=next;
            _expectedValue=value;
            _expectedCaret=ReadCaret();
            return true;
        }
        Detach("Le champ n'a pas confirme l'insertion : dictee suspendue.");
        return false;
    }
}
