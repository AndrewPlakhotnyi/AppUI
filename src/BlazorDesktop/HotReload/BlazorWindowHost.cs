using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebWindowCSharp;

namespace BlazorDesktop.HotReload {


public static class 
BlazorWindowHost {
    public static void 
    RunBlazorWindowHost(this WindowStartupOptions options, ILogger logger, string assetsDirectory){
        logger.LogInformation("Creating blazor window host...");
        var clock = Stopwatch.StartNew();

        var schemes = BlazorWindowHelper.CreateSchemes(assetsDirectory);
        var webWindow = new WebWindowStartupOptions(
            title: options.Title,
            position: options.Position,
            windowStyle: options.WindowStyle,
            schemes: schemes
        ).CreateWebWindow();

        var communicationChannel = new BlazorHostWebViewCommunicationChannel(
            pipeNameOut: "BlazorWindowClient",
            pipeNameIn: "BlazorWindowHost",
            logger:logger
        );

        communicationChannel.WebViewMessageJsonReceived += json => WebWindow.Invoke(() => webWindow.PostMessageAsJson(json));
        webWindow.WebMessageReceived += (_, json) => communicationChannel.SendWebViewMessageJson(json);

        communicationChannel.MessageReceived += message => {
            switch(message){
                case ReloadMessage _: 
                    WebWindow.Invoke(() => webWindow.Reload());
                    break;

                case NavigateToStringMessage navigateToStringMessage:
                    WebWindow.Invoke(() => webWindow.NavigateToString(navigateToStringMessage.Content));
                    break;
            }
        };

        Task.Run(() => communicationChannel.StartListening());

        WebWindow.WaitForExit();
    }
}

}
