using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebWindowCSharp;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Dynamic;

namespace BlazorDesktop {

public abstract class 
BlazorWindowContent: ComponentBase {    
    public BlazorWindow Window {get;set;}
    
    public async Task OnParametersChangedAsync(IEnumerable<(string name, object value)> parameters){
        await SetParametersAsync(ParameterView.FromDictionary(parameters.ToDictionary(x => x.name, x => x.value)));
        //Can we avoid calling StateHasChanged explicitly?
        StateHasChanged();
    }
}

public class
BlazorWindow {
    internal WebWindow WebWindow { get; }
    internal Lock<WebViewEvents> WebViewEvents { get; }
    internal DesktopRenderer Renderer {get;}
    public DesktopJsRuntime JsRuntime { get; }
    public event EventHandler<WindowMovedEventArgs> Moved;
    public event EventHandler<WindowSizeChangedEventArgs> SizeChanged;
    public event EventHandler<WindowClosingEventArgs> Closing;
    public event EventHandler<WindowDpiChangedEventArgs> DpiChanged;
    public event EventHandler<WindowNavigationStartingEventArgs> NavigationStarting;
    public event EventHandler Closed;
    public event EventHandler Loaded;
    public ILogger Logger {get;}

    /// <summary>
    /// RootComponent of the window's content. It is used when we need to update the root model state of the window.
    /// </summary>
    public BlazorWindowContent RootComponent {get;set;}
    internal BlazorWindow(WebWindow webWindow, WebViewEvents ipcEvents, DesktopJsRuntime jsRuntime, DesktopRenderer renderer, WindowPosition position, ILogger logger) {
        WebWindow = webWindow;
        WebViewEvents = new Lock<WebViewEvents>(ipcEvents);
        Logger = logger;
        JsRuntime = jsRuntime;
        Renderer = renderer;
        Position = position;
        webWindow.Moved += (s, e) => {
            Position = new WindowPosition(
                x: e.NewX,
                y: e.NewY,
                width: Position.Width,
                height: Position.Height,
                isMaximized: Position.IsMaximized
            );
            Moved?.Invoke(this, e);
        };

        webWindow.SizeChanged += (s,e) => {
            Position = new WindowPosition(
                x: Position.X,
                y: Position.Y,
                width: e.NewWidth,
                height: e.NewHeight,
                isMaximized: e.IsMaximized
            );
            SizeChanged?.Invoke(this, e);
        };

        webWindow.Closed += (s,e) => OnClosed();
        webWindow.Closing += (s,e) => Closing?.Invoke(s,e);
        webWindow.DpiChanged += (s,e) => { 
            if (ScreenDpi != e.NewDpi) {
                ScreenDpi = e.NewDpi; 
                DpiChanged?.Invoke(s, e); 
            }
        };

        webWindow.NavigationStarting += (s,e) => NavigationStarting?.Invoke(s, new WindowNavigationStartingEventArgs(e.NewUri));
    }


    public void 
    OnLoaded() => Loaded?.Invoke(this, EventArgs.Empty);

    public void 
    OnClosed(){
        Closed?.Invoke(this, EventArgs.Empty);
        this.WebWindow.Dispose();
        this.Renderer.Dispose();
    }

    public WindowPosition Position {get; private set;}
        
    private int? _screenDpi;
    public int ScreenDpi {
        get =>_screenDpi ??= WebWindow.GetScreenDpi();
        private set => _screenDpi = value;
    }

    public double DevicePixelRatio => ScreenDpi / 96d;
}

public class SingleLoggerFactory : ILoggerProvider {
    public ILogger Logger {get;}
    public SingleLoggerFactory(ILogger logger) => Logger = logger;

    public ILogger CreateLogger(string categoryName) => Logger;

    public void Dispose() {  }
}

public class
WindowStartupOptions {
    public string Title {get;}
    public WindowPosition Position {get;}
    public WindowStyle WindowStyle {get;}
    public ImmutableList<string> Scripts {get;}
    public WindowStartupOptions(string title, WindowPosition position, WindowStyle windowStyle = WindowStyle.Default, ImmutableList<string>? scripts = null) {
        Title = title;
        Position = position;
        WindowStyle = windowStyle;
        Scripts = scripts ?? ImmutableList<string>.Empty;
    }
}

public static class
BlazorWindowHelper {

    public static Dictionary<int, BlazorWindow> 
    WindowsByHWnd {get;} = new Dictionary<int, BlazorWindow>();

    private static Action<ServiceCollection> _configureServices;

    public static void
    Run(Action startup, Action<ServiceCollection> configureServices) {
        BlazorDispatcher.Instance = new BlazorDispatcher();   
        _configureServices = configureServices;
        startup();
        BlazorDispatcher.Instance.Run();
    }

    public static BlazorWindow
    CreateBlazorWindow<TRootComponent>(this WindowStartupOptions options, ILogger logger,  IDictionary<string, object> componentParameters, string assetsDirectory = "assets")
    where TRootComponent: IComponent => 
        options.CreateBlazorWindow(typeof(TRootComponent), logger, componentParameters, assetsDirectory);

    public static BlazorWindow
    CreateBlazorWindow(this WindowStartupOptions options,
        Type rootComponentType, 
        ILogger logger, 
        IDictionary<string, object> rootComponentParameters, 
        string assetsDirectory = "assets")
    {
        BlazorDispatcher.Instance.VerifyAccess();
        logger.LogInformation("Creating blazor window...");
        var clock = Stopwatch.StartNew();
        var schemes = CreateSchemes(assetsDirectory);
        JSInteropEventDispatcher.Logger = logger;
        var webWindow = new WebWindowStartupOptions(
            title: options.Title,
            position: options.Position,
            windowStyle: options.WindowStyle,
            schemes: schemes
        ).CreateWebWindow();

        var communicationChannel = new WebWindowCommunicationChannel(webWindow);

        var webViewEvents = new WebViewEvents(
            communicationChannel: communicationChannel,
            logger: logger);

        var jsRuntime = new DesktopJsRuntime(webViewEvents, communicationChannel);

        var services = new ServiceCollection();
        _configureServices(services);
        services.AddSingleton<IJSRuntime>(jsRuntime);

        var desktopRenderer = new DesktopRenderer(
            ipcEvents: webViewEvents,
            ipcChannel: communicationChannel,
            logger: logger, 
            renderedId: webWindow.HWnd, 
            jsRuntime: jsRuntime,
            serviceProvider: services.BuildServiceProvider(),
            loggerFactory: new LoggerFactory(new [] {new SingleLoggerFactory(logger)})).RegisterInJSInteropEventDispatcher();

        var blazorWindow = new BlazorWindow(webWindow, webViewEvents, jsRuntime, desktopRenderer, options.Position, logger);
        WindowsByHWnd.Add(webWindow.HWnd, blazorWindow);

        Task.Run(async () => {
            await webViewEvents.PerformHandshakeAsync();
            blazorWindow.Closed += (_,__) => blazorWindow.Renderer.RemoveFromJsInteropDispatcher();
            blazorWindow.AttachJSInterop(CancellationToken.None);
            var rootComponent = await desktopRenderer.AddComponentAsync(
                componentType: rootComponentType,
                domElementSelector: rootComponentType.Name,
                blazorWindow: blazorWindow,
                componentParameters: rootComponentParameters
                );
            if (rootComponent is not BlazorWindowContent blazorWindowContent)
                throw new InvalidOperationException($"Blazor window root component must be  a {nameof(BlazorWindowContent)} but was {rootComponent.GetType()}");
            blazorWindow.RootComponent = blazorWindowContent;
            logger.LogInformation($"Blazor window has been created for {clock.ElapsedMilliseconds}ms");
            BlazorDispatcher.Instance.Invoke(() => blazorWindow.OnLoaded());
        });
        
        webWindow.NavigateToString(GetInitialHtml(rootComponentType, scriptsToInject: options.Scripts));
        return blazorWindow;
    }

    public static void
    AttachJSInterop(this BlazorWindow window, CancellationToken cancellationToken) => 
        window.WebViewEvents.AttachJSInterop(window.JsRuntime, cancellationToken);

    public static List<(string scheme, ResolveWebResourceDelegate resolveWebResource)>
    CreateSchemes(string assetsDirectory){
        var schemes = new List<(string scheme, ResolveWebResourceDelegate resolveWebResource)>();
        schemes.Add((scheme: "framework", resolveWebResource: (string uri, out string contentType) => {
            contentType = GetContentType(uri);
            if (uri == "framework://blazor.desktop.js")
                return typeof(BlazorWindowHelper).Assembly.GetManifestResourceStream("BlazorDesktop.blazor.desktop.js");
            throw new InvalidOperationException($"Unknown framework file: {uri}");
        }));

        // On Windows, we can't use a custom scheme to host the initial HTML,
        // because webview2 won't let you do top-level navigation to such a URL.
        // On Linux/Mac, we must use a custom scheme, because their webviews
        // don't have a way to intercept http:// scheme requests.
        var blazorAppScheme = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "http" : "app";
        schemes.Add((scheme: blazorAppScheme, resolveWebResource: (string uri, out string contentType) => {
            // TODO: Only intercept for the hostname 'app' and passthrough for others
            // TODO: Prevent directory traversal?
            var appFile = Path.Combine(assetsDirectory, new Uri(uri).AbsolutePath.Substring(1));
            contentType = GetContentType(appFile);
            return File.Exists(appFile) ? File.OpenRead(appFile) : null;
        }));

        return schemes;

        string
        GetContentType(string url) => Path.GetExtension(url) switch {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".wasm" => "application/wasm",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        }; 
    }



    public static string 
    GetInitialHtml(Type rootComponentType, IEnumerable<string> scriptsToInject){
        var scripts = new StringBuilder();
        foreach(var script in scriptsToInject)
            scripts.AppendLine(@$"<script src=""http://assets/{script}""></script>");

        return @$"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width"" />
    <base href=""/"" />
    <link href=""http://assets/css/main.css"" rel=""stylesheet"" />
</head>
<body>
    <{rootComponentType.Name}>Loading...</{rootComponentType.Name}>
    <script src=""framework://blazor.desktop.js""></script>
    {scripts}
</body>
</html>";
    }

    public static bool
    TryGetBlazorWindow(this int hWnd, [NotNullWhen(true)] out BlazorWindow? result) => WindowsByHWnd.TryGetValue(hWnd, out result);

    public static void
    WaitForExit() => WebWindow.WaitForExit();

    public static void
    Close(this BlazorWindow window) => window.WebWindow.Close();

    public static void
    Minimize(this BlazorWindow window) => window.WebWindow.Minimize();

    public static void 
    Maximize(this BlazorWindow window) => window.WebWindow.Maximize();

    public static void 
    RestoreDown(this BlazorWindow window) => window.WebWindow.Restore();

    public static void 
    Move(this BlazorWindow window, int x, int y) => window.WebWindow.Move(x, y);

    public static void    
    DragMove(this BlazorWindow window) => window.WebWindow.DragMove(); 

    public static void 
    Shift(this BlazorWindow window, int deltaX, int deltaY) => window.WebWindow.Move(window.Position.X + deltaX, window.Position.Y + deltaY);

    public static void
    NavigateTo(this BlazorWindow window, string url) => window.WebWindow.NavigateToUrl(url);
}
}
