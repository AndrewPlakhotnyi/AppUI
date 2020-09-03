#include "WebWindow.h"

#ifdef _WIN32
# define EXPORTED __declspec(dllexport)
#else
# define EXPORTED
#endif

extern "C" {

    EXPORTED void WebWindow_RegisterClass(HINSTANCE hInstance) {
        RegisterWebWindowClass(hInstance);
    }

    EXPORTED HWND WebWindow_CreateWebWindow(WindowStarupOptions options) {
        return CreateWebWindow(options);
    }

    EXPORTED HRESULT WebWindow_AttachWebView(HWND hWnd, WebMessageReceivedCallback webMessageReceivedCallbak) {
        return AttachWebView(GetWebWindow(hWnd), webMessageReceivedCallbak);
    }

    EXPORTED HRESULT WebWindow_NavigateToUrl(HWND hWnd, AutoString url) {
        return NavigateToUrl(url, GetWebWindow(hWnd));
    }

    EXPORTED HRESULT WebWindow_NavigateToString(HWND hWnd, AutoString html){
        return NavigateToString(html, GetWebWindow(hWnd));
    }

    EXPORTED HRESULT WebWindow_PostMessageAsJson(HWND hWnd, AutoString json){
        return PostMessageAsJson(json, GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_WaitForExit() {
        WaitForExit();
    }

    EXPORTED void WebWindow_Invoke(ACTION callback) {
        Invoke(callback);
    }

    EXPORTED void WebWindow_AddCustomScheme(HWND hWnd, AutoString scheme,  WebResourceRequestedCallback requestHandler) {
        AddCustomScheme(scheme, requestHandler, GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_Show(HWND hWnd){
        Show(GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_Minimize(HWND hWnd){
        Minimize(GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_Maximize(HWND hWnd){
        Maximize(GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_Restore(HWND hWnd){
        Restore(GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_Close(HWND hWnd){
        Close(GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_Reload(HWND hWnd){
        Reload(GetWebWindow(hWnd));
    }

    EXPORTED void WebWindow_Move(HWND hWnd, int x, int y){
        Move(GetWebWindow(hWnd), x, y);
    }

    EXPORTED void WebWindow_DragMove(HWND hWnd) {
        DragMove(GetWebWindow(hWnd));
    }

    EXPORTED int WebWindow_GetScreenDpi(HWND hWnd){
        return GetScreenDpi(GetWebWindow(hWnd));
    }
}



