using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;
using System.Windows.Controls;
namespace DicteeLocale;

// A dedicated editor that models Chromium/Quill accessibility formatting.
// The actual textbox stays ordinary; only its UIA text surface has NBSPs and
// paragraph sentinels, and ValuePattern can deliberately expose another view.
public sealed class QuillLikeTestBox : TextBox
{
    public bool DifferentValueView {get;set;}
    public string AccessibleText(string text,bool document) => text.Replace(" ","\u00a0")+
        (document?(Text.Length==0?"\n":"\n\n"):"");
    protected override AutomationPeer OnCreateAutomationPeer()=>new QuillPeer(this);
    sealed class QuillPeer : TextBoxAutomationPeer,IValueProvider
    {
        readonly QuillLikeTestBox _owner;
        public QuillPeer(QuillLikeTestBox owner):base(owner) {_owner=owner;}
        protected override string GetClassNameCore()=>"ql-editor";
        public override object GetPattern(PatternInterface pattern)
        {
            if(pattern==PatternInterface.Value)return this;
            if(pattern==PatternInterface.Text)return new QuillProvider((ITextProvider)base.GetPattern(pattern),_owner);
            return base.GetPattern(pattern);
        }
        bool IValueProvider.IsReadOnly=>_owner.IsReadOnly;
        string IValueProvider.Value=>(_owner.DifferentValueView?"Value-only wrapper: ":"")+_owner.AccessibleText(_owner.Text,true);
        void IValueProvider.SetValue(string value)=>_owner.Text=value;
    }
    sealed class QuillProvider : ITextProvider
    {
        readonly ITextProvider _inner;
        readonly QuillLikeTestBox _owner;
        public QuillProvider(ITextProvider inner,QuillLikeTestBox owner) {_inner=inner;_owner=owner;}
        public ITextRangeProvider DocumentRange=>new QuillRange(_inner.DocumentRange,_owner,true);
        public SupportedTextSelection SupportedTextSelection=>_inner.SupportedTextSelection;
        ITextRangeProvider[] Wrap(ITextRangeProvider[] ranges)=>Array.ConvertAll(ranges,r=>(ITextRangeProvider)new QuillRange(r,_owner,false));
        public ITextRangeProvider[] GetSelection()=>Wrap(_inner.GetSelection());
        public ITextRangeProvider[] GetVisibleRanges()=>Wrap(_inner.GetVisibleRanges());
        public ITextRangeProvider RangeFromChild(IRawElementProviderSimple child)=>new QuillRange(_inner.RangeFromChild(child),_owner,false);
        public ITextRangeProvider RangeFromPoint(Point point)=>new QuillRange(_inner.RangeFromPoint(point),_owner,false);
    }
    sealed class QuillRange : ITextRangeProvider
    {
        readonly ITextRangeProvider _inner;
        readonly QuillLikeTestBox _owner;
        bool _document;
        public QuillRange(ITextRangeProvider inner,QuillLikeTestBox owner,bool document) {_inner=inner;_owner=owner;_document=document;}
        static ITextRangeProvider Inner(ITextRangeProvider range)=>range is QuillRange q?q._inner:range;
        public ITextRangeProvider Clone()=>new QuillRange(_inner.Clone(),_owner,_document);
        public bool Compare(ITextRangeProvider range)=>_inner.Compare(Inner(range));
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint,ITextRangeProvider target,TextPatternRangeEndpoint targetEndpoint)=>_inner.CompareEndpoints(endpoint,Inner(target),targetEndpoint);
        public void ExpandToEnclosingUnit(TextUnit unit) {_document=false;_inner.ExpandToEnclosingUnit(unit);}
        public ITextRangeProvider? FindAttribute(int attribute,object value,bool backward)
        {var result=_inner.FindAttribute(attribute,value,backward);return result is null?null:new QuillRange(result,_owner,false);}
        public ITextRangeProvider? FindText(string text,bool backward,bool ignoreCase)
        {var result=_inner.FindText(text,backward,ignoreCase);return result is null?null:new QuillRange(result,_owner,false);}
        public object GetAttributeValue(int attribute)=>_inner.GetAttributeValue(attribute);
        public double[] GetBoundingRectangles()=>_inner.GetBoundingRectangles();
        public IRawElementProviderSimple[] GetChildren()=>_inner.GetChildren();
        public IRawElementProviderSimple GetEnclosingElement()=>_inner.GetEnclosingElement();
        public string GetText(int maximum)
        {
            string text=_owner.AccessibleText(_inner.GetText(-1),_document);
            return maximum>=0 && text.Length>maximum?text.Substring(0,maximum):text;
        }
        public int Move(TextUnit unit,int count) {_document=false;return _inner.Move(unit,count);}
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint,ITextRangeProvider target,TextPatternRangeEndpoint targetEndpoint)
        {_document=false;_inner.MoveEndpointByRange(endpoint,Inner(target),targetEndpoint);}
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint,TextUnit unit,int count)
        {_document=false;return _inner.MoveEndpointByUnit(endpoint,unit,count);}
        public void Select()=>_inner.Select();
        public void AddToSelection()=>_inner.AddToSelection();
        public void RemoveFromSelection()=>_inner.RemoveFromSelection();
        public void ScrollIntoView(bool alignToTop)=>_inner.ScrollIntoView(alignToTop);
    }
}
