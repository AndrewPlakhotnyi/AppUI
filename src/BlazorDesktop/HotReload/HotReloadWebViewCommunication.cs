using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace BlazorDesktop.HotReload {

public class
BlazorHostWebViewCommunicationChannel : IWebViewCommunicationChannel {
    public string PipeNameOut {get;}
    public string PipeNameIn {get;}
    public ILogger Logger { get; }

    public event Action<string>? WebViewMessageJsonReceived;
    public event Action<IBlazorHotReloadMessage>? MessageReceived;
    public BlazorHostWebViewCommunicationChannel(string pipeNameOut, string pipeNameIn, ILogger logger) {
        (PipeNameIn, PipeNameOut) = (pipeNameIn, pipeNameOut);
        Logger = logger;
    }

    public void 
    StartListening(){
        using var pipeServer = new NamedPipeServerStream(PipeNameIn, PipeDirection.In);
        Logger.LogInformation($"Start listening on {PipeNameIn}");
        while(true){
            pipeServer.WaitForConnection();
            using var reader = new StreamReader(pipeServer, leaveOpen: true);
            var message = reader.ReadToEnd().ParseBlazorHotReloadMessage();
            Logger.LogInformation($"Received {message.GetType().Name}: {message.ToJson()}");
            switch(message) {
                case WebViewMessageJson webViewMessageJson:
                    try {
                        WebViewMessageJsonReceived?.Invoke(webViewMessageJson.Json);
                    }
                    catch(Exception exception){
                        Logger.LogError(exception, $"Failed to invoke {nameof(WebViewMessageJsonReceived)}");
                    }
                    break;
            }

            try {
                MessageReceived?.Invoke(message);
            }
            catch(Exception exception){
                Logger.LogError(exception, $"Failed to invoke {nameof(MessageReceived)}");
            }

            pipeServer.Disconnect();
        }
    }

    public void 
    SendReload() => SendMessage(new ReloadMessage());

    public void 
    SendNavigateToString(string content) => SendMessage(new NavigateToStringMessage(content));

    public void 
    SendWebViewMessageJson(string json){
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Can't post an empty json message", nameof(json));
        SendMessage(new WebViewMessageJson(json: json));
    }

    public void 
    SendMessage(IBlazorHotReloadMessage message){
        Logger.LogInformation($"Sending {message.GetType().Name}: {message.ToJson()}");
        using var pipe = new NamedPipeClientStream(".", PipeNameOut, PipeDirection.Out);
        pipe.Connect();
        var buffer = message.ToJsonBox().ToUtf8Bytes();
        pipe.Write(buffer, 0, buffer.Length);
    }
}

public class 
BlazorHotReloadMessagBox {
    public string MessageType {get;}
    public string MessageJson {get;}
    public BlazorHotReloadMessagBox(string messageType, string messageJson) {
        MessageType = messageType;
        MessageJson = messageJson;
    }
}

public class IBlazorHotReloadMessage {}

public class 
WebViewMessageJson: IBlazorHotReloadMessage {
    public string Json {get;}
    public WebViewMessageJson(string json) => Json = json;
}

public class 
ReloadMessage : IBlazorHotReloadMessage { 
}

public class 
NavigateToStringMessage : IBlazorHotReloadMessage {
    public string Content {get;}
    public NavigateToStringMessage(string content) => Content = content;
}

public static class 
Ipc {
    public static void 
    RunPipeServer(string pipeName, Action<IBlazorHotReloadMessage> ipcMessageHandler){
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.In);
        while(true){
            server.WaitForConnection();
            using var reader = new StreamReader(server);
            var message = reader.ReadToEnd().ParseJson<IBlazorHotReloadMessage>();
            ipcMessageHandler(message);
        }
    }
    
    public static string 
    ToJsonBox(this IBlazorHotReloadMessage message) => 
        new BlazorHotReloadMessagBox(message.GetType().Name, (message as object).ToJson()).ToJson();

    public static IBlazorHotReloadMessage 
    ParseBlazorHotReloadMessage(this string json) {
        var box = json.ParseJson<BlazorHotReloadMessagBox>();
        return box.MessageType switch {
            nameof(WebViewMessageJson) => box.MessageJson.ParseJson<WebViewMessageJson>(),
            nameof(ReloadMessage) => box.MessageJson.ParseJson<ReloadMessage>(),
            nameof(NavigateToStringMessage) => box.MessageJson.ParseJson<NavigateToStringMessage>(),
            _ => throw new InvalidOperationException($"Unsupported message type {box.MessageType}")
        };
    }
        
}

}
