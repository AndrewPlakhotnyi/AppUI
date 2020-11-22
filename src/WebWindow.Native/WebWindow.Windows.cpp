#include "WebWindow.h"

#include <atomic>
#include <comdef.h>
#include <map>
#include <mutex>
#include <stdexcept>
#include <Shlwapi.h>

#include "WebView2.h"
#include "WebView2EnvironmentOptions.h"
using namespace Microsoft::WRL;

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
HINSTANCE _hInstance;
#define WM_USER_INVOKE (WM_USER + 0x0002)
std::map<HWND, WebWindow> WebWindows = {};
DWORD _mainThreadId;
wil::com_ptr<ICoreWebView2Environment> _environment = nullptr;

void RegisterWebWindowClass(HINSTANCE hInstance)
{
    _hInstance = hInstance;
	WNDCLASSW wc = { };
	wc.lpfnWndProc = WindowProc;
	wc.hInstance = _hInstance;
	wc.lpszClassName = L"WebWindow";
    //wc.style = CS_DROPSHADOW;
	RegisterClass(&wc);

	SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE);
}

HWND CreateWebWindow(WindowStarupOptions options) {
    _mainThreadId = GetCurrentThreadId();
	WebWindow webWindow = {};
	webWindow.MovedCallback = options.MovedCallback;
	webWindow.SizeChangedCallback = options.SizeChangedCallback;
	webWindow.ClosingCallback = options.ClosingCallback;
	webWindow.ClosedCallback = options.ClosedCallback;
	webWindow.DpiChangedCallback = options.DpiChangedCallback;
	webWindow.NavigationStartingCallback = options.NavigationStartingCallback;
	webWindow.style = options.style;
    HWND hWnd = CreateWindowEx(
		0,                              // Optional window styles.
		L"WebWindow",                     // Window class
		options.title,							// Window text
		(options.position.isMaximized ? WS_MAXIMIZE : 0),            // Window style

		// Size and position
		options.position.x,
        options.position.y,
        options.position.width,
        options.position.height,

		NULL,       // Parent window
		NULL,       // Menu
		_hInstance, // Instance handle
		NULL // Additional application data
	);
	if (hWnd == NULL)
		return NULL;

	webWindow.hWnd = hWnd;
	WebWindows.emplace(hWnd, webWindow);
	return hWnd;
}

HRESULT CreateController(HWND hWnd, WebWindow* webWindow, std::atomic_flag* notReady, HRESULT* createWebViewResult, ICoreWebView2Environment* environment) {
    return _environment->CreateCoreWebView2Controller(hWnd, Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
        [=](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT {
            *createWebViewResult = result;
            if (result != S_OK) {
                notReady->clear();
                return S_OK;
            }

            ICoreWebView2* webView = nullptr;
            if (controller != nullptr) {
                result = controller->get_CoreWebView2(&webView);
                if (result != S_OK)
                    return result;
            }

            //Disable web-specific settings
            ICoreWebView2Settings* settings;
		    webView->get_Settings(&settings);
            settings->put_AreDefaultContextMenusEnabled(FALSE);
            settings->put_IsStatusBarEnabled(FALSE);
            settings->put_AreDefaultScriptDialogsEnabled(FALSE);

            //Adding communication
			EventRegistrationToken webMessageToken;
			webView->AddScriptToExecuteOnDocumentCreated(L"window.external = { sendMessage: function(message) { window.chrome.webview.postMessage(message); }, receiveMessage: function(callback) { console.log(\"receive message hook installed\"); window.chrome.webview.addEventListener(\'message\', function(e) { callback(e.data); }); } };", nullptr);
            webView->add_WebMessageReceived(Callback<ICoreWebView2WebMessageReceivedEventHandler>(
				[=](ICoreWebView2* webview, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
					wil::unique_cotaskmem_string message;
                    //Communication by raw strings (not json and js objects)
					if (args->get_WebMessageAsJson(&message) == 0)
						webWindow->webMessageReceivedCallback(message.get());
					return S_OK;
				}).Get(), &webMessageToken);

            EventRegistrationToken webResourceRequestedToken;
			webView->AddWebResourceRequestedFilter(L"*", COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);
			webView->add_WebResourceRequested(Callback<ICoreWebView2WebResourceRequestedEventHandler>(
				[=](ICoreWebView2* sender, ICoreWebView2WebResourceRequestedEventArgs* args)
				{
					ICoreWebView2WebResourceRequest* request;
					args->get_Request(&request);

					wil::unique_cotaskmem_string uri;
					request->get_Uri(&uri);
					std::wstring uriString = uri.get();
					size_t colonPosition = uriString.find(L':', 0);
					if (colonPosition > 0)
					{
						std::wstring scheme = uriString.substr(0, colonPosition);
						if (webWindow->schemes.find(scheme) == webWindow->schemes.end())
							return S_OK;
						WebResourceRequestedCallback handler = webWindow->schemes[scheme];
						if (handler != NULL)
						{
							int numBytes;
							AutoString contentType;
							wil::unique_cotaskmem dotNetResponse(handler(uriString.c_str(), &numBytes, &contentType));

							if (dotNetResponse != nullptr && contentType != nullptr)
							{
								std::wstring contentTypeWS = contentType;

								IStream* dataStream = SHCreateMemStream((BYTE*)dotNetResponse.get(), numBytes);
								wil::com_ptr<ICoreWebView2WebResourceResponse> response;
								environment->CreateWebResourceResponse(
									dataStream, 200, L"OK", (L"Content-Type: " + contentTypeWS).c_str(),
									&response);
								args->put_Response(response.get());

                                dataStream->Release();
							}
						}
					}

					return S_OK;
				}
			).Get(), &webResourceRequestedToken);

			EventRegistrationToken navigationStartingToken;
			webView->add_NavigationStarting(Callback<ICoreWebView2NavigationStartingEventHandler>(
				[=](ICoreWebView2* sender, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {

				wil::unique_cotaskmem_string uri;
				args->get_Uri(&uri);
				auto result = uri.get();
				webWindow->NavigationStartingCallback(result);
				return S_OK;
			}).Get(), &navigationStartingToken);

            webWindow->controller = controller;
            webWindow->webView = webView;
			
			RefitContent(webWindow);

            notReady->clear();
            return S_OK;
    }).Get());
}

HRESULT AttachWebView(WebWindow* webWindow, WebMessageReceivedCallback webMessageReceivedCallbak) {
    std::atomic_flag notReady = ATOMIC_FLAG_INIT;
	notReady.test_and_set();
    HRESULT createWebViewResult = S_OK;
	HWND hWnd = webWindow -> hWnd;
    if (!IsWindow(hWnd))
        throw std::invalid_argument("Given window handle is not a window.");
	webWindow->webMessageReceivedCallback = webMessageReceivedCallbak;
    //Create environment only once per application
    if (_environment == nullptr) {
		auto options = Microsoft::WRL::Make<CoreWebView2EnvironmentOptions>();
		//todo: enable to auto open devtools from c# startup options
		options->put_AdditionalBrowserArguments(L"--auto-open-devtools-for-tabs");
	    HRESULT createEnvironmentResult = CreateCoreWebView2EnvironmentWithOptions(nullptr, nullptr, 
			options.Get(),
            Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
                [&](HRESULT result, ICoreWebView2Environment* environment) -> HRESULT {
                    // Create a CoreWebView2Controller and get the associated CoreWebView2 whose parent is the main window hWnd
                    _environment = environment;
                    CreateController(hWnd, webWindow, &notReady, &createWebViewResult, environment);
                    return S_OK;
            }).Get());
        
        if (createEnvironmentResult != S_OK)
            return createEnvironmentResult;
    }
    else
        CreateController(hWnd, webWindow, &notReady, &createWebViewResult, _environment.get());
    // Block until it's ready. This simplifies things for the caller, so they
	// don't need to regard this process as async.
	MSG msg = { };
    while(notReady.test_and_set() && GetMessage(&msg, NULL, 0,0)) {
        TranslateMessage(&msg);
		DispatchMessage(&msg);
    }
    return createWebViewResult ;
}


 WebWindow* GetWebWindow(HWND hWnd) {
    if (hWnd == NULL)
        throw std::invalid_argument("Application window handle can't be zero");

    const auto window = WebWindows.find(hWnd);
    if (window == WebWindows.end())
        throw std::invalid_argument("Application window with the given hWnd not found");

    return &window->second;
}

WebWindow* TryGetWebWindow(HWND hWnd){
    if (hWnd == NULL)
        throw std::invalid_argument("Application window handle can't be zero");
	const auto window = WebWindows.find(hWnd);
    if (window == WebWindows.end())
		return NULL;

    return &window->second;
}

HRESULT NavigateToUrl(AutoString url, const WebWindow* window) {
    return window->webView->Navigate(url);
}

HRESULT NavigateToString(AutoString html, const WebWindow* window){
	return window->webView->NavigateToString(html);
}

HRESULT PostMessageAsJson(AutoString json, const WebWindow* window){
	return window->webView->PostWebMessageAsJson(json);
}

struct InvokeWaitInfo
{
	std::condition_variable completionNotifier;
	bool isCompleted;
};
std::mutex invokeLockMutex;
void WaitForExit() {
	//In case no window has been open but the loop did started
	if (_mainThreadId == 0)
		_mainThreadId = GetCurrentThreadId();
	MSG msg = { };
	while (GetMessage(&msg, NULL, 0, 0)) {
        if (msg.message == WM_USER_INVOKE) {
            ACTION callback = (ACTION)msg.wParam;
		    callback();
		    InvokeWaitInfo* waitInfo = (InvokeWaitInfo*)msg.lParam;
		    {
			    std::lock_guard<std::mutex> guard(invokeLockMutex);
			    waitInfo->isCompleted = true;
		    }
		    waitInfo->completionNotifier.notify_one();
		    continue;
        }
		TranslateMessage(&msg);
		DispatchMessage(&msg);
	}
}

void Invoke( ACTION callback) {
    InvokeWaitInfo waitInfo = {};
	PostThreadMessage(_mainThreadId, WM_USER_INVOKE, (WPARAM)callback, (LPARAM)&waitInfo);

	// Block until the callback is actually executed and completed
	// TODO: Add return values, exception handling, etc.
	std::unique_lock<std::mutex> uLock(invokeLockMutex);
	waitInfo.completionNotifier.wait(uLock, [&] { return waitInfo.isCompleted; });
}

void AddCustomScheme(AutoString scheme, WebResourceRequestedCallback requestHandler, WebWindow* window) {
    auto result = window->schemes.try_emplace(scheme, requestHandler);
    if (!result.second)
        throw std::invalid_argument("Handler for the given scheme has been already added");
}

void Close(const WebWindow* webWindow){
    //Check the case when the window has been already closed
    if (!IsWindow(webWindow->hWnd))
        return;
	PostMessage(webWindow->hWnd, WM_CLOSE, 0,0);
}

void Reload(const WebWindow* webWindow){
	webWindow->webView->Reload();
}

void Show(const WebWindow* webWindow){
	ShowWindow(webWindow->hWnd, SW_SHOWDEFAULT);
	if (webWindow->style == WebWindowStyle::Transparent) {
	auto hwnd = webWindow->hWnd;
		LONG lStyle = GetWindowLong(hwnd, GWL_STYLE);
		lStyle |= WS_THICKFRAME;
		lStyle = lStyle & ~WS_CAPTION;
		SetWindowLong(hwnd, GWL_STYLE, lStyle);
		SetWindowPos(hwnd, NULL, 0,0,0,0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
	}
}

void Minimize(const WebWindow* window) {
	ShowWindow(window->hWnd, SW_MINIMIZE);
}

void Maximize(const WebWindow* window){
	ShowWindow(window->hWnd, SW_MAXIMIZE);
}

void Restore(const WebWindow* window){
	ShowWindow(window->hWnd, SW_RESTORE);
}

void RefitContent(const WebWindow* webWindow){
	if (webWindow->controller == NULL)
		return;
    RECT bounds;
    GetClientRect(webWindow->hWnd, &bounds);
    webWindow->controller->put_Bounds(bounds);
}

void Move(const WebWindow* window, int x, int y) {
	RECT currentArea = {};
	GetWindowRect(window->hWnd, &currentArea);
	MoveWindow(window->hWnd, x, y, currentArea.right - currentArea.left, currentArea.bottom - currentArea.top, false);
}

void DragMove(const WebWindow* window) {
	ReleaseCapture();
	SendMessage(window->hWnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
}

int GetScreenDpi(const WebWindow* window){
	return GetDpiForWindow(window->hWnd);
}

LRESULT CALLBACK WindowProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
	switch(uMsg){
	case WM_SIZE: {
			WebWindow* webWindow = TryGetWebWindow(hWnd);
			if (webWindow == NULL)
				break;
			RefitContent(webWindow);
			if (webWindow->SizeChangedCallback != NULL)
				webWindow->SizeChangedCallback(LOWORD(lParam), HIWORD(lParam), wParam == SIZE_MAXIMIZED);

		}
		break;
	case WM_MOVE: {
			WebWindow* webWindow = TryGetWebWindow(hWnd);
			if (webWindow == NULL || webWindow->MovedCallback == NULL)
				break;
			RECT windowRect = {};
			GetWindowRect(hWnd, &windowRect);
			webWindow->MovedCallback(windowRect.left, windowRect.top);
		}
		break;
	case WM_NCCALCSIZE: {
		LRESULT result = DefWindowProc(hWnd, uMsg, wParam, lParam);
		auto sz = (NCCALCSIZE_PARAMS*)lParam;
        sz->rgrc[0].top -= 7;
        return result;
	}

	case WM_CLOSE: {
			WebWindow* webWindow = TryGetWebWindow(hWnd);
			if (webWindow == NULL)
				break;

			//Do not close if the user canceled and ClosingCallback returned 0
			if (webWindow->ClosingCallback == NULL || webWindow->ClosingCallback() == 1)
				DestroyWindow(webWindow->hWnd);

			return 0;
	}
	case WM_DESTROY: {
		WebWindow* webWindow = TryGetWebWindow(hWnd);
		if (webWindow != NULL && webWindow->ClosedCallback != NULL)
			webWindow->ClosedCallback();

        if (webWindow != NULL){
            webWindow->controller->Close();
            WebWindows.erase(webWindow->hWnd);
        }
        if (WebWindows.size() == 0)
		    PostQuitMessage(0);
		break;
	}
	case WM_DPICHANGED: {
		WebWindow* webWindow = TryGetWebWindow(hWnd);
		if (webWindow == NULL)
			return 0;
		LPRECT lprNewRect = (LPRECT)lParam;
		SetWindowPos(hWnd, 0, lprNewRect->left, lprNewRect->top, lprNewRect->right - lprNewRect->left, lprNewRect->bottom - lprNewRect->top, SWP_NOZORDER | SWP_NOACTIVATE);
		if (webWindow->DpiChangedCallback != NULL) {
			int newDpi = LOWORD(wParam);
			webWindow->DpiChangedCallback(newDpi);
		}
		return 0;
	}
	}

	return DefWindowProc(hWnd, uMsg, wParam, lParam);
}
