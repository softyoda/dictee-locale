using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace DicteeLocale;

public static class EditorTextSelfTest
{
    // This mode never creates a window, initializes hooks, loads models or calls SendInput.
    public static void Run()
    {
        var results=new List<object>();int failures=0;
        void Check(bool passed,string name) {results.Add(new {test=name,passed});if(!passed)failures++;}
        string first="Tu pourrais";
        string expected=EditorText.AfterEdit("\n",0,0,"",first)!;
        string actual="Tu\u00a0pourrais\r\n\r\n";
        Check(expected!=actual,"original-exact-comparison-reproduces-rich-editor-mismatch");
        Check(EditorText.SameDocument(expected,actual,true),"rich-first-words-confirm-with-nbsp-and-paragraph-sentinel");
        string before=EditorText.Normalize(actual);
        expected=EditorText.AfterEdit(before,first.Length,0,""," continuer la phrase")!;
        actual="Tu\u202fpourrais\u00a0continuer\u00a0la\u00a0phrase\n";
        Check(EditorText.SameDocument(expected,actual,true),"rich-next-words-continue-in-same-dictation");
        before=EditorText.Normalize(actual);
        string owned="Tu pourrais continuer la phrase";
        expected=EditorText.AfterEdit(before,owned.Length,0,owned,"Pourrais-tu continuer ?")!;
        Check(EditorText.SameDocument(expected,"Pourrais-tu\u00a0continuer ?\n\n",true),"owned-rich-revision-is-confirmed");
        before="Introduction\nAvant mauvais apres\n";
        expected=EditorText.AfterEdit(before,"Introduction\nAvant ".Length,7,"","correct")!;
        Check(expected=="Introduction\nAvant correct apres\n","multiline-selection-preserves-surrounding-text");
        Check(!EditorText.SameDocument(expected,"Introduction\nAvant MODIFIE apres\n",true),"meaningful-manual-edit-is-not-normalized-away");
        Check(!EditorText.SameDocument("a\nb\n","ab\n",true),"internal-line-breaks-remain-significant");
        Check(!EditorText.SameDocument("text ","text",true),"real-trailing-spaces-remain-significant");
        Check(!EditorText.SameDocument("text\n","text",false),"plain-editor-line-breaks-are-not-trimmed");
        Check(EditorText.AfterEdit("Avant autre apres",11,0,"bonjour","salut")==null,"foreign-tail-cannot-be-deleted");
        Check(EditorText.AfterEdit("abc",-1,0,"","x")==null && EditorText.AfterEdit("abc",4,0,"","x")==null,"out-of-range-caret-is-refused");
        Check(EditorText.AfterEdit("abc",1,4,"","x")==null,"out-of-range-selection-is-refused");
        Check(EditorText.AfterEdit(null,0,0,"","x")==null && EditorText.AfterEdit("abc",null,0,"","x")==null,"missing-provider-data-does-not-create-a-false-acknowledgement");
        Check(!EditorText.SameDocument("Bonjour ici\n","ici Bonjour\n",true),"same-words-at-wrong-position-are-not-acknowledged");
        Check(EditorText.SameDocument("a\r\nb\r","a\nb\n",true),"windows-and-provider-newlines-normalize-consistently");
        Directory.CreateDirectory(Path.Combine(Paths.Root,"test-results"));
        File.WriteAllText(Path.Combine(Paths.Root,"test-results","text-reconciliation.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
        Environment.ExitCode=failures==0?0:1;
    }
}
