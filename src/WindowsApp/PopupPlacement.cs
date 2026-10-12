using System;
using System.Runtime.InteropServices;
using System.Windows;
using Forms=System.Windows.Forms;
namespace DicteeLocale;

public static class PopupPlacement
{
    [StructLayout(LayoutKind.Sequential)] struct POINT {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct RECT {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point,uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);

    [StructLayout(LayoutKind.Sequential)] struct GUIINFO
    {
        public uint Size,Flags;
        public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;
        public RECT CaretRect;
    }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GUIINFO info);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref POINT point);
    public static Point? CaretPosition()
    {
        var info=new GUIINFO {Size=(uint)Marshal.SizeOf<GUIINFO>()};
        if(!GetGUIThreadInfo(0,ref info) || info.Caret==IntPtr.Zero)return null;
        var point=new POINT {X=info.CaretRect.Left,Y=info.CaretRect.Top};
        return ClientToScreen(info.Caret,ref point)?new Point(point.X,point.Y):null;
    }
    // All coordinates here are physical screen pixels, including negative monitor origins.
    public static Rect Calculate(Rect field,double caretX,Rect work,double width,double height,double gap)
    {
        double x=Math.Clamp(caretX-width/2,work.Left,Math.Max(work.Left,work.Right-width));
        double y=field.Top-height-gap;
        if(y<work.Top)y=field.Bottom+gap;
        y=Math.Clamp(y,work.Top,Math.Max(work.Top,work.Bottom-height));
        return new Rect(x,y,width,height);
    }
    public static Rect Bounds(IntPtr hwnd)
    {
        if(!GetWindowRect(hwnd,out var r))return Rect.Empty;
        return new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
    }
    public static void Place(IntPtr hwnd,Rect field,double caretX,double widthDip,double heightDip)
    {
        var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)caretX,(int)(field.Top+field.Height/2)));
        var area=screen.WorkingArea;
        var work=new Rect(area.X,area.Y,area.Width,area.Height);
        double scale=1;
        try {
            var monitor=MonitorFromPoint(new POINT {X=(int)caretX,Y=(int)(field.Top+field.Height/2)},2);
            if(GetDpiForMonitor(monitor,0,out var dpi,out _)==0)scale=dpi/96.0;
        } catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {}
        var position=Calculate(field,caretX,work,Math.Ceiling(widthDip*scale),Math.Ceiling(heightDip*scale),10*scale);
        // Preserve focus and z order; WPF handles the destination monitor's DPI change.
        SetWindowPos(hwnd,IntPtr.Zero,(int)Math.Round(position.X),(int)Math.Round(position.Y),0,0,0x1|0x4|0x10);
    }
}