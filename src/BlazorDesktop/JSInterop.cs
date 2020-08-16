using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace BlazorDesktop {

public class 
DesktopJsRuntime: JSRuntime {
    
    public WebViewEvents WebViewEvents {get;}
    internal IWebViewCommunicationChannel CommunicationChannel {get;}
    public DesktopJsRuntime(WebViewEvents ipcEvents, IWebViewCommunicationChannel ipcChannel) {
        CommunicationChannel = ipcChannel;
        WebViewEvents = ipcEvents;
    }

    private static readonly Type 
    VoidTaskResultType = typeof(Task).Assembly.GetType("System.Threading.Tasks.VoidTaskResult", true);

    protected override void 
    BeginInvokeJS(long taskId, string identifier, string? argsJson) => 
        CommunicationChannel.SendEvent("JS.BeginInvokeJS", taskId, identifier, argsJson);

    protected override void 
    EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult) {
        if (invocationResult.Result != null && invocationResult.Result.GetType() != VoidTaskResultType)
            CommunicationChannel.SendEvent("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success, invocationResult.Result);
        else 
            CommunicationChannel.SendEvent("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success);
    }
}

public static class
JSInteropEventDispatcher {

    private static Dictionary<int, DesktopRenderer> Renderers = new Dictionary<int, DesktopRenderer>();
    private static object _lockObject = new object();

    internal static DesktopRenderer 
    RegisterInJSInteropEventDispatcher(this DesktopRenderer renderer){
        lock(_lockObject)
            Renderers.Add(renderer.RendererId, renderer);
        return renderer;
    }

    internal static void 
    RemoveFromJsInteropDispatcher(this DesktopRenderer renderer){
        lock(_lockObject)
            Renderers.Remove(renderer.RendererId);
    }


    //This methods must be in a public class.
    [JSInvokable(nameof(DispatchEvent))]
    public static async Task 
    DispatchEvent(WebEventDescriptor eventDescriptor, string eventArgsJson) {
        var webEvent = WebEventData.Parse(eventDescriptor, eventArgsJson);
        //RendererId is always the hWnd of the window
        if (Renderers.TryGetValue(webEvent.BrowserRendererId, out var renderer))
            await renderer.DispatchEventAsync(
                webEvent.EventHandlerId,
                webEvent.EventFieldInfo,
                webEvent.EventArgs);
    } 
}

public static class 
JSInteropHelper {

    private static DesktopSynchronizationContext SyncContext = new DesktopSynchronizationContext(CancellationToken.None);

    internal static void
    AttachJSInterop(this Lock<WebViewEvents> webViewEvents, JSRuntime jsRuntime, CancellationToken applicationLifetimeToken) {
        SynchronizationContext.SetSynchronizationContext(SyncContext);
        lock(webViewEvents) {
            webViewEvents.Value =  webViewEvents.Value.On(
                eventName:"BeginInvokeDotNetFromJS", 
                callback: argsObject => {
                    BlazorDispatcher.Instance.Invoke(() => {
                            var args = (object[])(argsObject ?? throw new ArgumentException("Args is not expected to be null", nameof(argsObject)));
                            DotNetDispatcher.BeginInvokeDotNet(
                                jsRuntime:jsRuntime,
                                invocationInfo:new DotNetInvocationInfo(
                                    assemblyName: ((JsonElement)args[1]).GetString() ?? throw new ArgumentException("AssemblyName is null in the received args"),
                                    methodIdentifier: ((JsonElement)args[2]).GetString()?? throw new ArgumentException("AssemblyName is null in the received args"),
                                    dotNetObjectId: ((JsonElement)args[3]).GetInt64() ,
                                    callId: ((JsonElement)args[0]).GetString()),
                                argsJson:((JsonElement)args[4]).GetString());
                        });
                }
            );

           webViewEvents.Value =  webViewEvents.Value.On(
                eventName:"EndInvokeJSFromDotNet", 
                callback: argsObject => {
                    BlazorDispatcher.Instance.Invoke(() => {
                        var args = (object[])(argsObject ?? throw new ArgumentException("Args is not expected to be null", nameof(argsObject)));
                        DotNetDispatcher.EndInvokeJS(
                            jsRuntime:jsRuntime,
                            arguments: ((JsonElement)args[2]).GetString());

                    });
                }
            );
        }
    }

    

    [JSInvokable(nameof(NotifyLocationChanged))]
    public static void 
    NotifyLocationChanged(string uri, bool isInterceptedLink) {
        throw new NotImplementedException();
    }
}

}
