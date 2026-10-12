Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
root = fso.GetParentFolderName(WScript.ScriptFullName)
shell.Run Chr(34) & root & "\app\DicteeLocale.exe" & Chr(34), 0, False
