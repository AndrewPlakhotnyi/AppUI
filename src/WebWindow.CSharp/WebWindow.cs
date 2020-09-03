using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;

namespace WebWindowCSharp {

public class 
WebWindow: IDisposable {
    public int HWnd {get; private set;}
    public event EventHandler<string> WebMessageReceived;
    public event EventHandler<WindowMovedEventArgs> Moved;
    public event EventHandler<WindowSizeChangedEventArgs> SizeChanged;
    public event EventHandler<WindowClosingEventArgs> Closing;
    public event EventHandler<WindowDpiChangedEventArgs> DpiChanged;
    public event EventHandler Closed;
    public static Thread CreatedThread {get; private set;}
    private bool IsClosed {get;set;}
    public WebWindow(int hWnd) {
        HWnd = hWnd;
        CreatedThread = Thread.CurrentThread;
        Closed += (s,e) => IsClosed = true;
    }

    private List<IntPtr> _hGlobalsToFree = new List<IntPtr>();
    private List<GCHandle> _gcHandlesToFree = new List<GCHandle>();

    static WebWindow() =>  Native.WebWindow_RegisterClass(Marshal.GetHINSTANCE(typeof(WebWindow).Module));
    
    public static WebWindow
    CreateWebWindow(WebWindowStartupOptions options) {
        
        WebWindow webWindow = new WebWindow(0);
        
        Native.WindowMovedCallback movedCallback = (newX, newY) => webWindow.Moved?.Invoke(webWindow, new WindowMovedEventArgs(newX, newY));
        Native.WindowSizeChangedCallback sizeChangedCallback = (newWidth, newHeight, isMaximized) => webWindow.SizeChanged?.Invoke(webWindow, new WindowSizeChangedEventArgs(newWidth, newHeight, isMaximized));
        Native.WindowDpiChangedCallback dpiChangedCallback = (newDpi) => webWindow.DpiChanged?.Invoke(webWindow, new WindowDpiChangedEventArgs(newDpi));
        Native.WindowClosingCallback closingCallback = () => {
            var args = new WindowClosingEventArgs();
            webWindow.Closing?.Invoke(webWindow, args);
            return args.CancelClosure ? 0 : 1;
        };
        Native.InvokeCallback closedCallback = () => webWindow.Closed?.Invoke(webWindow, EventArgs.Empty);

        var hWnd = Native.WebWindow_CreateWebWindow(
            options: new Native.WindowStartupOptions(){ 
                Position = options.Position,
                Title = options.Title,
                Style = (Native.WebWindowStyles)options.WindowStyle,
                MovedCallback = movedCallback,
                SizeChangedCallback = sizeChangedCallback,
                ClosedCallback = closedCallback,
                ClosingCallback = closingCallback,
                DpiChangedCallback = dpiChangedCallback,
            });
        if (hWnd == 0)
            throw new Win32Exception();

        webWindow.HWnd = hWnd;

        options.Schemes.ForEach(x => webWindow.AddCustomScheme(x.scheme, x.resolveWebResource));

        //We must show window before attach a webview
        Native.WebWindow_Show(hWnd);
        Native.WebMessageReceivedCallback webMesasgeReceivedCallback =  message => {
            webWindow.WebMessageReceived?.Invoke(webWindow, message);
            return default;
        };
        webWindow._gcHandlesToFree.Add(GCHandle.Alloc(webMesasgeReceivedCallback));
        webWindow._gcHandlesToFree.Add(GCHandle.Alloc(movedCallback));
        webWindow._gcHandlesToFree.Add(GCHandle.Alloc(sizeChangedCallback));
        webWindow._gcHandlesToFree.Add(GCHandle.Alloc(closingCallback));
        webWindow._gcHandlesToFree.Add(GCHandle.Alloc(closedCallback));
        webWindow._gcHandlesToFree.Add(GCHandle.Alloc(dpiChangedCallback));
        var attachResult = Native.WebWindow_AttachWebView(hWnd, webMesasgeReceivedCallback);
        if (attachResult != 0)
            throw new InvalidOperationException("Failed to attach the webview", Marshal.GetExceptionForHR(attachResult));
        return webWindow;
    }

    public void
    NavigateToString(string html) => Native.WebWindow_NavigateToString(HWnd, html);

    public void 
    PostMessageAsJson(string json) {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Can't post an empty json message", nameof(json));
        if (IsClosed)
            return;
        var result = Native.WebWindow_PostMessageAsJson(HWnd, json);
        if (result != 0)
            throw new InvalidOperationException("Failed to post message as json", Marshal.GetExceptionForHR(result));
    } 

    public static void 
    Invoke(Action action){
        //Do not dispatch if already in the UI thread
        if (Thread.CurrentThread == CreatedThread)
            action();
        else 
            Native.WebWindow_Invoke(action.Invoke);
    }

    public static void 
    WaitForExit() => Native.WebWindow_WaitForExit();

    private void 
    AddCustomScheme(string scheme, ResolveWebResourceDelegate resolveWebResource){
        // Because of WKWebView limitations, this can only be called during the constructor
        // before the first call to Show. To enforce this, it's private and is only called
        // in response to the constructor options.

        Native.OnWebResourceRequestedCallback callback = (string url, out int numBytes, out string contentType) => {
            var responseStream = resolveWebResource(url, out contentType);
            if (responseStream == null) {
                // Webview should pass through request to normal handlers (e.g., network)
                // or handle as 404 otherwise
                numBytes = 0;
                return default;
            }

            // Read the stream into memory and serve the bytes
            // In the future, it would be possible to pass the stream through into C++
            using (responseStream) {
                using var ms = new MemoryStream();
                responseStream.CopyTo(ms);

                numBytes = (int)ms.Position;
                var buffer = Marshal.AllocHGlobal(numBytes);
                Marshal.Copy(ms.GetBuffer(), 0, buffer, numBytes);
                _hGlobalsToFree.Add(buffer);
                return buffer;
            }
        };

        _gcHandlesToFree.Add(GCHandle.Alloc(callback));
        Native.WebWindow_AddCustomScheme(HWnd, scheme, callback);
    }

    public void
    Minimize() => Native.WebWindow_Minimize(HWnd);

    public void 
    Maximize() => Native.WebWindow_Maximize(HWnd);

    public void 
    Restore() => Native.WebWindow_Restore(HWnd);

    public void 
    Close() => Native.WebWindow_Close(HWnd);

    public void 
    Reload() => Native.WebWindow_Reload(HWnd);

    public void 
    Move(int x, int y) => Native.WebWindow_Move(HWnd, x, y);

    public void
    DragMove() => Native.WebWindow_DragMove(HWnd);

    public int
    GetScreenDpi() => Native.WebWindow_GetScreenDpi(HWnd);

    public void 
    Dispose() {
        _gcHandlesToFree.ForEach(x => x.Free());
        _hGlobalsToFree.ForEach(Marshal.FreeHGlobal);
    }
}

public class 
WindowMovedEventArgs {
    public int NewX {get;}
    public int NewY {get;}
    public WindowMovedEventArgs(int newX, int newY) {
        NewX = newX;
        NewY = newY;
    }
}

public class 
WindowSizeChangedEventArgs {
    public int NewWidth {get;}
    public int NewHeight {get;}
    public bool IsMaximized {get;}
    public WindowSizeChangedEventArgs(int newWidth, int newHeight, bool isMaximized) {
        NewWidth = newWidth;
        NewHeight = newHeight;
        IsMaximized = isMaximized;
    }
}

public class 
WindowClosingEventArgs {
    public bool CancelClosure {get;set;}
}

public class 
WindowDpiChangedEventArgs {
    public int NewDpi {get;}
    public WindowDpiChangedEventArgs(int newDpi) {
        NewDpi = newDpi;
    }
}

public struct 
WindowPosition {
    public int X {get;}
    public int Y {get;}
    public int Width {get;}
    public int Height {get;}
    public bool IsMaximized {get;}
    public WindowPosition(int x, int y, int width, int height, bool isMaximized) : this() {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsMaximized = isMaximized;
    }
}

public enum 
WindowStyle {
    Default = 0,
    Transparent = 1,
    Toolbox = 2
}

public class 
WebWindowStartupOptions {
    public string Title {get;}
    public WindowPosition Position {get;}
    public WindowStyle WindowStyle {get;}
    public bool IsMaximized {get;}
    public List<(string scheme, ResolveWebResourceDelegate resolveWebResource)> Schemes {get;}
    public WebWindowStartupOptions(string title, WindowPosition position, WindowStyle windowStyle = WindowStyle.Default, bool isMaximized = false, List<(string scheme, ResolveWebResourceDelegate resolveWebResource)> schemes = null) {
        Title = title;
        Position = position;
        WindowStyle = windowStyle;
        IsMaximized = isMaximized;
        Schemes = schemes;
    }

    public WebWindow
    CreateWebWindow() => WebWindow.CreateWebWindow(this);
}

public delegate Stream ResolveWebResourceDelegate(string url, out string contentType);

internal class Native {

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] 
    public delegate void InvokeCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] 
    public delegate void WindowMovedCallback(int newX, int newY);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] 
    public delegate void WindowSizeChangedCallback(int newWidth, int newHeight, bool isMaximized);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] 
    public delegate int WindowClosingCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] 
    public delegate void WindowDpiChangedCallback(int newDpi);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Auto)] 
    public delegate IntPtr OnWebResourceRequestedCallback([MarshalAs(UnmanagedType.LPWStr)] string url, out int numBytes, out string contentType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Auto)] 
    public delegate IntPtr WebMessageReceivedCallback([MarshalAs(UnmanagedType.LPWStr)]string message);

    const string DllName = "WebWindow.Native";
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] 
    public static extern void WebWindow_RegisterClass(IntPtr hInstance);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] 
    public static extern int WebWindow_CreateWebWindow(WindowStartupOptions options);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] 
    public static extern int WebWindow_PostMessageAsJson(int hWnd, [MarshalAs(UnmanagedType.LPWStr)] string json);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] 
    public static extern int WebWindow_AttachWebView(int hWnd, WebMessageReceivedCallback webMessageReceivedCallback);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int WebWindow_WaitForExit();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int WebWindow_NavigateToString(int hWnd, [MarshalAs(UnmanagedType.LPWStr)] string html);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int WebWindow_Invoke(InvokeCallback callback);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] 
    public static extern void WebWindow_AddCustomScheme(int hWnd, [MarshalAs(UnmanagedType.LPWStr)] string scheme, OnWebResourceRequestedCallback requestHandler);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_Show(int hWnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_Minimize(int hWnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_Maximize(int hWnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_Restore(int hWnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_Close(int hWnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_Reload(int hWnd);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_Move(int hWnd, int x, int y);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void WebWindow_DragMove(int hWnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] 
    public static extern int WebWindow_GetScreenDpi(int hWnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct 
    WindowStartupOptions {
        public WindowPosition Position;
        
        [MarshalAs(UnmanagedType.LPWStr)] 
        public string Title;

        public WebWindowStyles Style;

        public WindowMovedCallback MovedCallback;
        public WindowSizeChangedCallback SizeChangedCallback;
        public WindowClosingCallback ClosingCallback;
        public WindowDpiChangedCallback DpiChangedCallback;
        public InvokeCallback ClosedCallback;
    }

    internal enum 
    WebWindowStyles {
        Default = 0,
        Transparent = 1,
        Toolbox = 2
    }
}

}
