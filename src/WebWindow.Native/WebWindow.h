#ifndef APPUI_H
#define APPUI_H
#include <map>
#include <Windows.h>
#include <wrl.h>
#include <wil/com.h>
#include "WebView2.h"
#include <string>

#ifdef _WIN32
typedef const wchar_t* AutoString;
#endif
typedef void (*WebMessageReceivedCallback)(AutoString message);
typedef void* (*WebResourceRequestedCallback)(AutoString url, int* outNumBytes, AutoString* outContentType);
typedef void (*WindowMovedCallback)(int newX, int newY);
typedef void (*WindowSizeChangedCallback)(int newWidth, int newHeight, bool isMaximized);
typedef int (*WindowClosingCallback)();

typedef void (*ACTION)();

enum WebWindowStyle: int {
    Default = 0,
    Transparent = 1,
    Toolbox = 2
};

struct WebWindow {
    HWND hWnd;
    WebWindowStyle style;
    WebMessageReceivedCallback webMessageReceivedCallback;
    WindowMovedCallback MovedCallback;
    WindowSizeChangedCallback SizeChangedCallback;
    WindowClosingCallback ClosingCallback;
    ACTION ClosedCallback;
    wil::com_ptr<ICoreWebView2Controller> controller;
    wil::com_ptr<ICoreWebView2> webView;
    std::map<std::wstring, WebResourceRequestedCallback> schemes = {};
};

struct WindowPosition {
    int x,y, width, height;
    bool isMaximized;
};

struct WindowStarupOptions {
    WindowPosition position;
    AutoString title;
    WebWindowStyle style;
    WindowMovedCallback MovedCallback;
    WindowSizeChangedCallback SizeChangedCallback;
    WindowClosingCallback ClosingCallback;
    ACTION ClosedCallback;
};

WebWindow* GetWebWindow(HWND hWnd);
void RegisterWebWindowClass(HINSTANCE hInstance);
HWND CreateWebWindow(WindowStarupOptions starupOptions);

HRESULT AttachWebView(WebWindow* window, WebMessageReceivedCallback webMessageReceivedCallbak);
HRESULT NavigateToUrl(AutoString url, const WebWindow* window);
HRESULT NavigateToString(AutoString html, const WebWindow* window);
HRESULT PostMessageAsJson(AutoString json, const WebWindow* window);
void AddCustomScheme(AutoString scheme, WebResourceRequestedCallback requestHandler, WebWindow* window);
void Invoke(ACTION action);
void WaitForExit();
void Show(const WebWindow* window);
void Minimize(const WebWindow* window);
void Maximize(const WebWindow* window);
void Restore(const WebWindow* window);
void Close(const WebWindow* window);
void RefitContent(const WebWindow* window);
void Move(const WebWindow* window, int x, int y);
void Reload(const WebWindow* window);
int GetScreenDpi(const WebWindow* window);
#endif // !APPUI_H