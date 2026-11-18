using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlareLabels;
internal static class WindowEffects
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    public static void Attach(Window window)=>window.SourceInitialized+=(_,_)=>
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
        int round=2;DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,33,ref round,sizeof(int));
    };
}
