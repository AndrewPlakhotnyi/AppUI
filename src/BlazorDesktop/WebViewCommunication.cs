using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebWindowCSharp;

namespace BlazorDesktop {

public interface 
IWebViewCommunicationChannel {
    void SendWebViewMessageJson(string messageJson);
    event Action<string>? WebViewMessageJsonReceived;
}

public class
WebWindowCommunicationChannel : IWebViewCommunicationChannel {
    public WebWindow WebWindow { get; }
    public event Action<string>? WebViewMessageJsonReceived;
    public WebWindowCommunicationChannel(WebWindow webWindow) {
        WebWindow = webWindow;
        webWindow.WebMessageReceived += (s,e) => WebViewMessageJsonReceived?.Invoke(e);
    }

    public void 
    SendWebViewMessageJson(string message) => WebWindow.SendMessage(message);
}


public static class 
WebWindowCommunicationChannelHelper {
    public static void 
    SendEvent(this IWebViewCommunicationChannel channel, string eventName, params object[]? args) => 
        channel.SendWebViewMessageJson(new WebViewEvent(eventName, args).ToJson());

    public static void 
    SendMessage(this WebWindow webWindow, string message) =>
        WebWindow.Invoke(() => webWindow.PostMessageAsJson(message));
}

public class 
WebViewEvents {
    public Dictionary<string, List<Action<object>>> Registrations {get;} = new Dictionary<string, List<Action<object>>>();
    public ILogger Logger {get;}
    public IWebViewCommunicationChannel CommunicationChannel {get;}
    public WebViewEvents(ILogger logger, IWebViewCommunicationChannel communicationChannel) {
        Logger = logger;
        CommunicationChannel = communicationChannel;
        communicationChannel.WebViewMessageJsonReceived += HandleMessageReceived;
    }

    private object _lockObject = new object();

    public WebViewEvents
    On(string eventName, Action<object> callback) {
        lock(_lockObject)
            Registrations.AddOrAddToList(eventName, callback);
        return this;
    }
            
    public void
    Off(string eventName, Action<object> callback) {
        lock(_lockObject){
            if (!Registrations.TryGetValue(eventName, out var callbacks))
                throw new InvalidOperationException($"No subscribers on the event {eventName}");

            if (!callbacks.Contains(callback))
                throw new InvalidOperationException($"The given callback is not a subscriber to the event {eventName}");

            callbacks.Remove(callback);
        }
    }

    public void 
    Once(string eventName, Action<object> callback) {
        void CallbackOnce(object arg) {
            Off(eventName, CallbackOnce);
            callback(arg);
        }

        On(eventName, CallbackOnce);
    }

    internal async Task
    PerformHandshakeAsync() {
        var tcs = new TaskCompletionSource<object>();
        Once("BlazorDesktopJSInitialized", args => tcs.SetResult(null));
        await tcs.Task;
    }

    public void
    HandleMessageReceived(string message) {
        // Move off the browser UI thread
        Task.Run(() => {
            var @event = message.ParseJson<WebViewEvent>();

            List<Action<object>> callbacksCopy;
            lock(_lockObject){
                if (!Registrations.TryGetValue(@event.EventName, out var callbacks))
                    return;
                callbacksCopy = callbacks.ToList();
                
            }
            foreach(var callback in callbacksCopy)
                callback(@event.Args);
        });
    }
 }

public readonly struct 
WebViewEvent {
    [JsonConstructor]
    public WebViewEvent(string eventName, object[]? args) {
        EventName = eventName;
        Args = args;
    }
    public string EventName {get;}
    public object[]? Args {get;}
}

}
