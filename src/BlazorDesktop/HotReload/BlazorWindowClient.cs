using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebWindowCSharp;

namespace BlazorDesktop.HotReload {

public class 
BlazorWindowClient: IDisposable {
    internal Lock<WebViewEvents> WebViewEvents { get; }
    public DesktopJsRuntime JsRuntime { get; }
    internal DesktopRenderer Renderer {get;}

    public BlazorHostWebViewCommunicationChannel CommunicationChannel => (BlazorHostWebViewCommunicationChannel)WebViewEvents.Value.CommunicationChannel;

    internal BlazorWindowClient(WebViewEvents ipcEvents, DesktopJsRuntime jsRuntime, DesktopRenderer renderer) {
        WebViewEvents = new Lock<WebViewEvents>(ipcEvents);
        JsRuntime = jsRuntime;
        Renderer = renderer;
    }

    public void Dispose() => Renderer.RemoveFromJsInteropDispatcher();
}

public static class 
BlazorWindowClientHelper {
    public static BlazorWindowClient 
    CreateBlazorWindowClient<TRootComponent>(ILogger logger, string assetsDirectory, IEnumerable<string> scriptsToInject, Action<IServiceCollection> configureServices){
        logger.LogInformation("Creating blazor window client");
        var clock = Stopwatch.StartNew();
        var communicationChannel = new BlazorHostWebViewCommunicationChannel(
            pipeNameOut: "BlazorWindowHost",
            pipeNameIn: "BlazorWindowClient",
            logger: logger
        );

        var ipcEvents = new WebViewEvents(logger, communicationChannel);
        var jsRuntime = new DesktopJsRuntime(ipcEvents, communicationChannel);

        var services = new ServiceCollection();
        configureServices(services);
        services.AddSingleton<IJSRuntime>(jsRuntime);

        var renderer = new DesktopRenderer(
            ipcEvents: ipcEvents,
            ipcChannel: communicationChannel,
            logger: logger, 
            renderedId: 0, 
            jsRuntime: jsRuntime,
            serviceProvider: services.BuildServiceProvider(),
            loggerFactory: new LoggerFactory(new [] {new SingleLoggerFactory(logger)})).RegisterInJSInteropEventDispatcher();

        var blazorWindowClient = new BlazorWindowClient(ipcEvents, jsRuntime, renderer);

        Task.Run(async () => {
            await ipcEvents.PerformHandshakeAsync();
            blazorWindowClient.AttachJSInterop();
            await renderer.AddComponentAsync(
                componentType: typeof(TRootComponent),
                domElementSelector: typeof(TRootComponent).Name,
                blazorWindow: new BlazorWindow(new WebWindow(hWnd: 0), 
                    ipcEvents: ipcEvents,
                    jsRuntime: jsRuntime,
                    renderer: renderer,
                    position: default,
                    logger: logger
                ),
                componentParameters: null);
            logger.LogInformation($"Blazor window has been created for {clock.ElapsedMilliseconds}ms");
            //BlazorDispatcher.Instance.Invoke(() => blazorWindow.OnLoaded());
        });

        blazorWindowClient.NavigateToString(BlazorWindowHelper.GetInitialHtml(rootComponentType:  typeof(TRootComponent), scriptsToInject));

        Task.Run(communicationChannel.StartListening);

        return blazorWindowClient;
    }

    public static void 
    Reload(this BlazorWindowClient client) =>
        ((BlazorHostWebViewCommunicationChannel) client.WebViewEvents.Value.CommunicationChannel).SendReload();

    public static void
    NavigateToString(this BlazorWindowClient client, string html) => client.CommunicationChannel.SendNavigateToString(html);

    public static void 
    AttachJSInterop(this BlazorWindowClient client) => 
        client.WebViewEvents.AttachJSInterop(client.JsRuntime, CancellationToken.None);
}

}
