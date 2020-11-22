using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlazorDesktop {

internal class 
DesktopRenderer: Renderer {

    public WebViewEvents IpcEvents {get;}
    public IWebViewCommunicationChannel IpcChannel { get; }
    public ILogger Logger {get;}
    public JSRuntime JsRuntime {get;}
    public int RendererId {get;}

    /// <summary>
    /// Notifies when a rendering exception occured.
    /// </summary>
    public event EventHandler<Exception> UnhandledException;

    public DesktopRenderer(WebViewEvents ipcEvents, IWebViewCommunicationChannel ipcChannel, ILogger logger, int renderedId, JSRuntime jsRuntime, IServiceProvider serviceProvider, ILoggerFactory loggerFactory) : base(serviceProvider, loggerFactory)  {
        IpcEvents = ipcEvents;
        IpcChannel = ipcChannel;
        Logger = logger;
        RendererId = renderedId;
        JsRuntime = jsRuntime;
    }

    public async Task<ComponentBase> 
    AddComponentAsync(Type componentType, string domElementSelector, BlazorWindow blazorWindow, IDictionary<string, object>? componentParameters) {
        try {
            var component = InstantiateComponent(componentType);
            var componentId = AssignRootComponentId(component);
            if (component is BlazorWindowContent blazorWindowContent)
                blazorWindowContent.Window = blazorWindow;
            
            var attachComponentTask = JsRuntime.InvokeAsync<object>(
                "Blazor._internal.attachRootComponentToElement",
                domElementSelector,
                componentId,
                RendererId);

    
            if (componentParameters?.Count > 0)
                await component.SetParametersAsync(ParameterView.FromDictionary(componentParameters));

            CaptureAsyncExceptions(attachComponentTask);
            await RenderRootComponentAsync(componentId);
            if (component is not ComponentBase componentBase)
                throw new InvalidOperationException($"Expecting a component to be ComponentBase but was {component.GetType()}");
            return componentBase;
        }
        catch(Exception exception)
        {
            blazorWindow.Logger.LogError(exception, $"Failed to {nameof(AddComponentAsync)} of type {componentType}");
            throw;
        }
    }
    
    protected override Task 
    UpdateDisplayAsync(in RenderBatch renderBatch) { 
        using var memoryStream = new MemoryStream();
        using var writer = new RenderBatchWriter(memoryStream, leaveOpen: false);
        writer.Write(renderBatch);
        IpcChannel.SendEvent("JS.RenderBatch", RendererId, memoryStream.ToArray().ToBase64());

        // TODO: Consider finding a way to get back a completion message from the Desktop side
        // in case there was an error. We don't really need to wait for anything to happen, since
        // this is not prerendering and we don't care how quickly the UI is updated, but it would
        // be desirable to flow back errors.
        return Task.CompletedTask;
    }

    protected override void
    HandleException(Exception exception) =>
        Logger.LogError(exception, "Exception occured in the renderer");

    public override Dispatcher 
    Dispatcher { get; } = NullDispatcher.Instance;

    private async void 
    CaptureAsyncExceptions(ValueTask<object> task) {
        try {
            await task;
        }
        catch (Exception ex) {
            UnhandledException?.Invoke(this, ex);
        }
    }
}

internal class 
NullDispatcher: Dispatcher {
    public static NullDispatcher Instance = new NullDispatcher();
    public override bool 
    CheckAccess() => true;

    public override Task 
    InvokeAsync(Action workItem) {
        workItem.Invoke();
        return Task.CompletedTask;
    }

    public override Task 
    InvokeAsync(Func<Task> workItem) => workItem();

    public override Task<TResult> 
    InvokeAsync<TResult>(Func<TResult> workItem) => Task.FromResult(workItem());

    public override Task<TResult> 
    InvokeAsync<TResult>(Func<Task<TResult>> workItem) => workItem();
}

}
