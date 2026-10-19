using System;
namespace DicteeLocale;

// Pure document reconciliation. No UI Automation, windows, keyboard input or I/O.
public static class EditorText
{
    public static string Normalize(string text)=>text.Replace("\r\n","\n").Replace('\r','\n').Replace('\u00a0',' ').Replace('\u202f',' ');
    public static bool SameDocument(string? first,string? second,bool richText)
    {
        if(first is null || second is null)return first==second;
        first=Normalize(first);second=Normalize(second);
        return richText?first.TrimEnd('\n')==second.TrimEnd('\n'):first==second;
    }
    public static string? AfterEdit(string? before,int? caret,int selectionLength,string oldTail,string suffix)
    {
        if(before is null || caret is not int offset || offset<0 || offset>before.Length || selectionLength<0)return null;
        if(selectionLength>0) {
            if(oldTail.Length>0 || selectionLength>before.Length-offset)return null;
            return before.Remove(offset,selectionLength).Insert(offset,suffix);
        }
        if(offset<oldTail.Length || !before.AsSpan(offset-oldTail.Length,oldTail.Length).SequenceEqual(oldTail.AsSpan()))return null;
        return before.Remove(offset-oldTail.Length,oldTail.Length).Insert(offset-oldTail.Length,suffix);
    }
}
