using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq; // Required for AllKeys and header copying

public class ZeroRatedProxyLogic
{
    private HttpListener _httpListener;
    private string _zeroRatedDomain; // Should be just the hostname e.g. "example.com"
    private string _zeroRatedScheme = "http"; // Default to http
    private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }); // Configure HttpClient as needed
    public event Action<string> LogMessage;
    private CancellationTokenSource _proxyCts;

    public ZeroRatedProxyLogic(string zeroRatedDomain)
    {
        // Ensure domain is just hostname, not scheme or path
        if (Uri.TryCreate(zeroRatedDomain, UriKind.Absolute, out Uri uriResult))
        {
            _zeroRatedDomain = uriResult.Host;
            _zeroRatedScheme = uriResult.Scheme;
        }
        else if (Uri.TryCreate($"http://{zeroRatedDomain}", UriKind.Absolute, out uriResult)) // try with http if no scheme
        {
             _zeroRatedDomain = uriResult.Host;
             _zeroRatedScheme = uriResult.Scheme; // will be http
        }
        else
        {
            // Fallback or throw error if domain is invalid
            _zeroRatedDomain = zeroRatedDomain;
            LogMessage?.Invoke($"Warning: Could not parse scheme from zeroRatedDomain '{zeroRatedDomain}'. Defaulting to http and using provided string as host.");
        }
    }

    public async Task StartProxyAsync(string listeningAddress = "http://0.0.0.0:8080/")
    {
        if (_httpListener != null && _httpListener.IsListening)
        {
            LogMessage?.Invoke("Proxy is already running.");
            return;
        }

        _proxyCts = new CancellationTokenSource();
        _httpListener = new HttpListener();

        // Ensure listeningAddress ends with a slash for HttpListener
        if (!listeningAddress.EndsWith("/"))
        {
            listeningAddress += "/";
        }
        _httpListener.Prefixes.Add(listeningAddress);

        try
        {
            _httpListener.Start();
            LogMessage?.Invoke($"Zero-rated proxy started on {listeningAddress}. Forwarding to {_zeroRatedScheme}://{_zeroRatedDomain}");
        }
        catch (HttpListenerException ex)
        {
            LogMessage?.Invoke($"Failed to start proxy: {ex.Message}. Common issues: Port already in use, or insufficient permissions (run as admin or use 'netsh http add urlacl url={listeningAddress} user=EVERYONE').");
            _httpListener = null; // Ensure it's null if start failed
            return;
        }

        try
        {
            while (_httpListener.IsListening && !_proxyCts.Token.IsCancellationRequested)
            {
                HttpListenerContext context = await _httpListener.GetContextAsync();
                // Don't wait for HandleRequestAsync to complete before accepting next request
                _ = Task.Run(() => HandleRequestAsync(context, _proxyCts.Token), _proxyCts.Token);
            }
        }
        catch (HttpListenerException ex) when (_proxyCts.IsCancellationRequested || _httpListener == null || !_httpListener.IsListening)
        {
            LogMessage?.Invoke($"HttpListener stopped or cancellation requested (Message: {ex.Message}).");
        }
        catch (ObjectDisposedException)
        {
             LogMessage?.Invoke("HttpListener has been disposed. Proxy stopping.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Proxy error in listening loop: {ex.Message}");
        }
        finally
        {
            if (_httpListener?.IsListening ?? false)
            {
                _httpListener.Stop();
            }
            _httpListener = null;
            LogMessage?.Invoke("Proxy has shut down.");
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken token)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // Construct target URL carefully
        string pathAndQuery = request.Url.PathAndQuery;
        if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery == "/") {
             // Optionally, define a default path if the original path is empty or just "/"
             // pathAndQuery = "/some/default/path";
        }

        string targetUrl = $"{_zeroRatedScheme}://{_zeroRatedDomain}{pathAndQuery}";
        LogMessage?.Invoke($"Proxying {request.HttpMethod} {request.Url.AbsolutePath} to {targetUrl}");

        try
        {
            using (HttpRequestMessage forwardRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), targetUrl))
            {
                // Copy content if present (e.g., for POST requests)
                if (request.HasEntityBody)
                {
                    var streamContent = new StreamContent(request.InputStream);
                    forwardRequest.Content = streamContent;
                    // Copy content headers
                    foreach (string key in request.Headers.AllKeys)
                    {
                        if (key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                        {
                            streamContent.Headers.TryAddWithoutValidation(key, request.Headers[key]);
                        }
                    }
                }

                // Copy request headers (excluding content headers, Host, and connection-specific ones)
                foreach (string key in request.Headers.AllKeys)
                {
                    if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                        key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) // For WebSockets, etc.
                        )
                    {
                        continue;
                    }
                    forwardRequest.Headers.TryAddWithoutValidation(key, request.Headers.GetValues(key));
                }
                // Set Host header for the target
                forwardRequest.Headers.Host = _zeroRatedDomain;


                using (HttpResponseMessage targetResponse = await _httpClient.SendAsync(forwardRequest, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.StatusCode = (int)targetResponse.StatusCode;
                    response.StatusDescription = targetResponse.ReasonPhrase;

                    // Copy response headers
                    foreach (var header in targetResponse.Headers)
                    {
                        if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                        response.Headers.Set(header.Key, string.Join(", ", header.Value));
                    }
                    if (targetResponse.Content != null)
                    {
                        foreach (var header in targetResponse.Content.Headers)
                        {
                            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                            response.Headers.Set(header.Key, string.Join(", ", header.Value));
                        }
                        // Stream the content
                        await targetResponse.Content.CopyToAsync(response.OutputStream);
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            LogMessage?.Invoke($"Request to {targetUrl} was canceled.");
            if (!response.HeadersSent)
            {
                response.StatusCode = 503; // Service Unavailable or specific code for cancellation
                response.StatusDescription = "Request Canceled";
            }
        }
        catch (HttpRequestException ex)
        {
            LogMessage?.Invoke($"Error forwarding request to {targetUrl}: {ex.Message}");
            if (!response.HeadersSent)
            {
                response.StatusCode = (int)HttpStatusCode.BadGateway;
                response.StatusDescription = "Bad Gateway";
                byte[] errorBody = Encoding.UTF8.GetBytes($"Error proxying request: {ex.Message}");
                response.ContentLength64 = errorBody.Length;
                response.OutputStream.Write(errorBody, 0, errorBody.Length);
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Unexpected error handling request for {targetUrl}: {ex.Message}");
            if (!response.HeadersSent)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.StatusDescription = "Internal Server Error";
                byte[] errorBody = Encoding.UTF8.GetBytes($"Internal proxy error: {ex.Message}");
                response.ContentLength64 = errorBody.Length;
                response.OutputStream.Write(errorBody, 0, errorBody.Length);
            }
        }
        finally
        {
            try
            {
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error closing response stream: {ex.Message}");
            }
        }
    }

    public void StopProxy()
    {
        LogMessage?.Invoke("Stopping proxy...");
        _proxyCts?.Cancel(); // Signal tasks to stop

        if (_httpListener != null)
        {
            // HttpListener.Stop() is synchronous and can block.
            // HttpListener.Close() or Abort() might be needed for forceful shutdown.
            // Running Stop in a task to prevent blocking the caller if it's on UI thread.
            Task.Run(() => {
                try
                {
                    if (_httpListener.IsListening)
                    {
                        _httpListener.Stop(); // This should make GetContextAsync throw.
                    }
                     _httpListener.Close(); // Releases resources
                }
                catch(Exception ex)
                {
                    LogMessage?.Invoke($"Exception while stopping HttpListener: {ex.Message}");
                }
                finally
                {
                    _httpListener = null;
                }
            });
        }
        _proxyCts?.Dispose();
        _proxyCts = null;
        LogMessage?.Invoke("Zero-rated proxy stop requested. Listener should terminate soon.");
    }
}
