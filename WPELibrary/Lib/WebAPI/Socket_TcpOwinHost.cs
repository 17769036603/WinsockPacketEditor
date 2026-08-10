using Microsoft.Owin.Builder;
using Owin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WPELibrary.Lib.WebAPI
{
    using AppFunc = Func<IDictionary<string, object>, Task>;

    /// <summary>
    /// HTTPS OWIN fallback that binds a normal TCP socket instead of HTTP.sys.
    /// It is used only when the configured HTTP.sys listener cannot start, so
    /// an injected non-elevated target does not depend on a machine URL ACL.
    /// </summary>
    internal sealed class Socket_TcpOwinHost : IDisposable
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxRequestBodyBytes = 4 * 1024 * 1024;
        private const int MaxResponseBodyBytes = 16 * 1024 * 1024;
        private const int MaxConcurrentClients = 64;

        private readonly TcpListener listener;
        private readonly X509Certificate2 certificate;
        private readonly AppFunc application;
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private readonly SemaphoreSlim clientSlots =
            new SemaphoreSlim(MaxConcurrentClients, MaxConcurrentClients);
        private readonly ConcurrentDictionary<TcpClient, byte> activeClients =
            new ConcurrentDictionary<TcpClient, byte>();
        private readonly Task acceptTask;
        private int disposed;

        private Socket_TcpOwinHost(Uri remoteUri, X509Certificate2 serverCertificate)
        {
            this.certificate = serverCertificate;
            this.application = BuildApplication();
            this.listener = new TcpListener(ResolveAddress(remoteUri.Host), remoteUri.Port);
            this.listener.Start();
            this.acceptTask = Task.Run(() => this.AcceptLoopAsync());
        }

        public static IDisposable Start(Uri remoteUri)
        {
            if (remoteUri == null)
            {
                throw new ArgumentNullException(nameof(remoteUri));
            }
            if (!string.Equals(remoteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("TCP OWIN 后备仅支持 HTTPS。");
            }

            X509Certificate2 serverCertificate = FindServerCertificate(remoteUri);
            try
            {
                return new Socket_TcpOwinHost(remoteUri, serverCertificate);
            }
            catch
            {
                serverCertificate.Dispose();
                throw;
            }
        }

        private static AppFunc BuildApplication()
        {
            var builder = new AppBuilder();
            builder.Properties["host.AppName"] = typeof(Socket_Web).Assembly.GetName().Name;
            new Socket_Web().Configuration(builder);
            return builder.Build<AppFunc>();
        }

        private async Task AcceptLoopAsync()
        {
            while (!this.stopping.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await this.listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = this.HandleClientWithSlotAsync(client);
                }
                catch (ObjectDisposedException)
                {
                    client?.Dispose();
                    break;
                }
                catch (SocketException)
                {
                    client?.Dispose();
                    if (this.stopping.IsCancellationRequested)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    client?.Dispose();
                    Socket_Operation.DoLog(nameof(Socket_TcpOwinHost), ex.Message);
                }
            }
        }

        private async Task HandleClientWithSlotAsync(TcpClient client)
        {
            this.activeClients.TryAdd(client, 0);
            bool slotAcquired = false;
            try
            {
                if (!this.clientSlots.Wait(0))
                {
                    return;
                }
                slotAcquired = true;

                await this.HandleClientAsync(client).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(nameof(Socket_TcpOwinHost), ex.Message);
            }
            finally
            {
                this.activeClients.TryRemove(client, out byte ignored);
                if (slotAcquired)
                {
                    this.clientSlots.Release();
                }
                client.Dispose();
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream network = client.GetStream())
            using (var ssl = new SslStream(network, false))
            using (CancellationTokenSource requestTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(this.stopping.Token))
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await AuthenticateClientAsync(ssl, requestTimeout.Token).ConfigureAwait(false);

                    await this.ProcessRequestAsync(ssl, client, requestTimeout.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The mobile client can close an idle/obsolete request at any time.
                }
                catch (OperationCanceledException)
                {
                    // Bound slow-header/body connections so they cannot retain a
                    // worker indefinitely.
                }
                catch (AuthenticationException ex)
                {
                    Socket_Operation.DoLog(nameof(Socket_TcpOwinHost), ex.Message);
                }
                catch (Exception ex)
                {
                    Socket_Operation.DoLog(nameof(Socket_TcpOwinHost), ex.Message);
                }
            }
        }

        private async Task AuthenticateClientAsync(SslStream ssl, CancellationToken token)
        {
            Task authentication = ssl.AuthenticateAsServerAsync(
                this.certificate,
                false,
                SslProtocols.Tls12,
                false);
            Task cancellation = Task.Delay(Timeout.Infinite, token);
            Task completed = await Task.WhenAny(authentication, cancellation).ConfigureAwait(false);
            if (completed != authentication)
            {
                // The stream is disposed by the caller immediately after the
                // timeout. Observe a late TLS fault so it is not reported as an
                // unobserved task exception.
                Task observeAuthentication = authentication.ContinueWith(
                    task => { var ignored = task.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new OperationCanceledException(token);
            }

            await authentication.ConfigureAwait(false);
        }

        private async Task ProcessRequestAsync(
            SslStream stream,
            TcpClient client,
            CancellationToken requestToken)
        {
            RequestHead request = await ReadRequestHeadAsync(stream, requestToken).ConfigureAwait(false);
            if (request == null)
            {
                return;
            }

            if (request.ContentLength > MaxRequestBodyBytes)
            {
                await WriteSimpleResponseAsync(
                    stream,
                    413,
                    "Request body is too large.",
                    requestToken).ConfigureAwait(false);
                return;
            }
            if (request.HasTransferEncoding)
            {
                await WriteSimpleResponseAsync(
                    stream,
                    501,
                    "Transfer-Encoding request bodies are not supported.",
                    requestToken).ConfigureAwait(false);
                return;
            }

            if (request.ExpectsContinue)
            {
                byte[] continueBytes = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                await stream.WriteAsync(continueBytes, 0, continueBytes.Length, requestToken).ConfigureAwait(false);
            }

            byte[] bodyBytes = await ReadRequestBodyAsync(
                stream,
                request.InitialBody,
                request.ContentLength,
                requestToken).ConfigureAwait(false);

            string path;
            string query;
            SplitRequestTarget(request.Target, out path, out query);

            using (var requestBody = new MemoryStream(bodyBytes, false))
            using (var responseBody = new LimitedMemoryStream(MaxResponseBodyBytes))
            {
                var responseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                var callbacks = new List<Tuple<Action<object>, object>>();
                IPEndPoint remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                IPEndPoint localEndPoint = client.Client.LocalEndPoint as IPEndPoint;

                var environment = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["owin.Version"] = "1.0",
                    ["owin.RequestBody"] = requestBody,
                    ["owin.RequestHeaders"] = request.Headers,
                    ["owin.RequestMethod"] = request.Method,
                    ["owin.RequestPath"] = Uri.UnescapeDataString(path),
                    ["owin.RequestPathBase"] = string.Empty,
                    ["owin.RequestProtocol"] = request.Protocol,
                    ["owin.RequestQueryString"] = query,
                    ["owin.RequestScheme"] = Uri.UriSchemeHttps,
                    ["owin.ResponseBody"] = responseBody,
                    ["owin.ResponseHeaders"] = responseHeaders,
                    ["owin.ResponseStatusCode"] = 200,
                    ["owin.CallCancelled"] = requestToken,
                    ["server.RemoteIpAddress"] = remoteEndPoint?.Address.ToString(),
                    ["server.RemotePort"] = remoteEndPoint?.Port.ToString(CultureInfo.InvariantCulture),
                    ["server.LocalIpAddress"] = localEndPoint?.Address.ToString(),
                    ["server.LocalPort"] = localEndPoint?.Port.ToString(CultureInfo.InvariantCulture),
                    ["server.IsLocal"] = remoteEndPoint != null && IPAddress.IsLoopback(remoteEndPoint.Address),
                    ["server.OnSendingHeaders"] = new Action<Action<object>, object>(
                        (callback, state) => callbacks.Add(Tuple.Create(callback, state)))
                };

                try
                {
                    await this.application(environment).ConfigureAwait(false);
                }
                catch (ResponseBodyTooLargeException)
                {
                    await WriteSimpleResponseAsync(
                        stream,
                        413,
                        "Response body is too large.",
                        requestToken).ConfigureAwait(false);
                    return;
                }
                for (int index = callbacks.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        callbacks[index].Item1(callbacks[index].Item2);
                    }
                    catch (ResponseBodyTooLargeException)
                    {
                        await WriteSimpleResponseAsync(
                            stream,
                            413,
                            "Response body is too large.",
                            requestToken).ConfigureAwait(false);
                        return;
                    }
                }

                int statusCode = environment.TryGetValue("owin.ResponseStatusCode", out object statusValue)
                    ? Convert.ToInt32(statusValue, CultureInfo.InvariantCulture)
                    : 200;
                string reasonPhrase = environment.TryGetValue("owin.ResponseReasonPhrase", out object reasonValue)
                    ? Convert.ToString(reasonValue, CultureInfo.InvariantCulture)
                    : GetReasonPhrase(statusCode);

                await WriteResponseAsync(
                    stream,
                    request.Method,
                    statusCode,
                    reasonPhrase,
                    responseHeaders,
                    responseBody.ToArray(),
                    requestToken).ConfigureAwait(false);
            }
        }

        private static async Task<RequestHead> ReadRequestHeadAsync(Stream stream, CancellationToken token)
        {
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[4096];
                int headerEnd = -1;
                while (buffer.Length < MaxHeaderBytes && headerEnd < 0)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        return null;
                    }

                    long previousLength = buffer.Length;
                    buffer.Write(chunk, 0, read);
                    byte[] current = buffer.GetBuffer();
                    int scanStart = Math.Max(0, (int)previousLength - 3);
                    headerEnd = FindHeaderEnd(current, scanStart, (int)buffer.Length);
                }

                if (headerEnd < 0 || headerEnd + 4 > MaxHeaderBytes)
                {
                    throw new InvalidDataException("HTTP request headers are too large.");
                }

                byte[] allBytes = buffer.ToArray();
                string headerText = Encoding.GetEncoding("iso-8859-1").GetString(allBytes, 0, headerEnd);
                string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] requestLine = lines[0].Split(new[] { ' ' }, 3);
                if (requestLine.Length != 3)
                {
                    throw new InvalidDataException("Invalid HTTP request line.");
                }

                var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1))
                {
                    int separator = line.IndexOf(':');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string name = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (headers.TryGetValue(name, out string[] existing))
                    {
                        headers[name] = existing.Concat(new[] { value }).ToArray();
                    }
                    else
                    {
                        headers[name] = new[] { value };
                    }
                }

                int contentLength = 0;
                if (headers.TryGetValue("Content-Length", out string[] contentLengthValues))
                {
                    if (contentLengthValues.Length != 1 ||
                        !int.TryParse(contentLengthValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out contentLength) ||
                        contentLength < 0)
                    {
                        throw new InvalidDataException("Invalid Content-Length header.");
                    }
                }

                int bodyOffset = headerEnd + 4;
                byte[] initialBody = new byte[Math.Max(0, allBytes.Length - bodyOffset)];
                if (initialBody.Length > 0)
                {
                    Buffer.BlockCopy(allBytes, bodyOffset, initialBody, 0, initialBody.Length);
                }

                return new RequestHead
                {
                    Method = requestLine[0],
                    Target = requestLine[1],
                    Protocol = requestLine[2],
                    Headers = headers,
                    ContentLength = contentLength,
                    InitialBody = initialBody,
                    HasTransferEncoding = headers.ContainsKey("Transfer-Encoding"),
                    ExpectsContinue = HeaderContains(headers, "Expect", "100-continue")
                };
            }
        }

        private static int FindHeaderEnd(byte[] bytes, int start, int length)
        {
            for (int index = start; index <= length - 4; index++)
            {
                if (bytes[index] == '\r' && bytes[index + 1] == '\n' &&
                    bytes[index + 2] == '\r' && bytes[index + 3] == '\n')
                {
                    return index;
                }
            }
            return -1;
        }

        private static bool HeaderContains(
            IDictionary<string, string[]> headers,
            string headerName,
            string value)
        {
            return headers.TryGetValue(headerName, out string[] values) &&
                values.Any(item => item.Split(',').Any(part =>
                    string.Equals(part.Trim(), value, StringComparison.OrdinalIgnoreCase)));
        }

        private static async Task<byte[]> ReadRequestBodyAsync(
            Stream stream,
            byte[] initialBody,
            int contentLength,
            CancellationToken token)
        {
            if (contentLength == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] body = new byte[contentLength];
            int copied = Math.Min(contentLength, initialBody.Length);
            if (copied > 0)
            {
                Buffer.BlockCopy(initialBody, 0, body, 0, copied);
            }

            while (copied < contentLength)
            {
                int read = await stream.ReadAsync(body, copied, contentLength - copied, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("The HTTP request body ended early.");
                }
                copied += read;
            }
            return body;
        }

        private static void SplitRequestTarget(string target, out string path, out string query)
        {
            string pathAndQuery = target;
            if (Uri.TryCreate(target, UriKind.Absolute, out Uri absolute))
            {
                pathAndQuery = absolute.PathAndQuery;
            }

            int queryIndex = pathAndQuery.IndexOf('?');
            if (queryIndex < 0)
            {
                path = string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;
                query = string.Empty;
                return;
            }

            path = queryIndex == 0 ? "/" : pathAndQuery.Substring(0, queryIndex);
            query = pathAndQuery.Substring(queryIndex + 1);
        }

        private static async Task WriteResponseAsync(
            Stream stream,
            string requestMethod,
            int statusCode,
            string reasonPhrase,
            IDictionary<string, string[]> responseHeaders,
            byte[] responseBody,
            CancellationToken token)
        {
            byte[] body = string.Equals(requestMethod, "HEAD", StringComparison.OrdinalIgnoreCase) ||
                          statusCode == 204 || statusCode == 304
                ? Array.Empty<byte>()
                : responseBody ?? Array.Empty<byte>();

            responseHeaders.Remove("Transfer-Encoding");
            responseHeaders["Content-Length"] = new[] { body.Length.ToString(CultureInfo.InvariantCulture) };
            responseHeaders["Connection"] = new[] { "close" };

            var headerBuilder = new StringBuilder();
            headerBuilder.Append("HTTP/1.1 ")
                .Append(statusCode.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(SanitizeHeaderValue(reasonPhrase))
                .Append("\r\n");
            foreach (KeyValuePair<string, string[]> header in responseHeaders)
            {
                foreach (string value in header.Value ?? Array.Empty<string>())
                {
                    headerBuilder.Append(header.Key)
                        .Append(": ")
                        .Append(SanitizeHeaderValue(value))
                        .Append("\r\n");
                }
            }
            headerBuilder.Append("\r\n");

            if (headerBuilder.Length > MaxHeaderBytes)
            {
                throw new InvalidDataException("HTTP response headers are too large.");
            }

            byte[] headerBytes = Encoding.GetEncoding("iso-8859-1").GetBytes(headerBuilder.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, 0, body.Length, token).ConfigureAwait(false);
            }
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        private static Task WriteSimpleResponseAsync(
            Stream stream,
            int statusCode,
            string message,
            CancellationToken token)
        {
            byte[] body = Encoding.UTF8.GetBytes(message ?? string.Empty);
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = new[] { "text/plain; charset=utf-8" }
            };
            return WriteResponseAsync(
                stream,
                "GET",
                statusCode,
                GetReasonPhrase(statusCode),
                headers,
                body,
                token);
        }

        private static string SanitizeHeaderValue(string value)
        {
            return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        private static string GetReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 201: return "Created";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 404: return "Not Found";
                case 409: return "Conflict";
                case 413: return "Payload Too Large";
                case 501: return "Not Implemented";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "Response";
            }
        }

        private static IPAddress ResolveAddress(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress parsed))
            {
                return parsed;
            }

            IPAddress address = Dns.GetHostAddresses(host)
                .FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork);
            if (address == null)
            {
                throw new InvalidOperationException("远程管理地址没有可用的 IPv4 地址。");
            }
            return address;
        }

        private static X509Certificate2 FindServerCertificate(Uri remoteUri)
        {
            string thumbprint = ReadSslBindingThumbprint(remoteUri);
            if (!string.IsNullOrEmpty(thumbprint))
            {
                X509Certificate2 bound = FindCertificateByThumbprint(StoreLocation.CurrentUser, thumbprint) ??
                    FindCertificateByThumbprint(StoreLocation.LocalMachine, thumbprint);
                if (bound != null)
                {
                    return bound;
                }
            }

            X509Certificate2 fallback = FindFallbackCertificate(StoreLocation.CurrentUser) ??
                FindFallbackCertificate(StoreLocation.LocalMachine);
            if (fallback == null)
            {
                throw new InvalidOperationException("找不到具有私钥的 WPE Mobile HTTPS 证书。");
            }
            return fallback;
        }

        private static string ReadSslBindingThumbprint(Uri remoteUri)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                    Arguments = string.Format(
                        CultureInfo.InvariantCulture,
                        "http show sslcert ipport={0}:{1}",
                        remoteUri.Host,
                        remoteUri.Port),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    Match match = Regex.Match(output, @"\b[0-9a-fA-F]{40}\b");
                    return match.Success ? match.Value : string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static X509Certificate2 FindCertificateByThumbprint(
            StoreLocation location,
            string thumbprint)
        {
            using (var store = new X509Store(StoreName.My, location))
            {
                try
                {
                    store.Open(OpenFlags.ReadOnly);
                    X509Certificate2 certificate = store.Certificates
                        .Find(X509FindType.FindByThumbprint, thumbprint, false)
                        .OfType<X509Certificate2>()
                        .FirstOrDefault(item => item.HasPrivateKey && IsCurrentlyValid(item));
                    return certificate == null ? null : new X509Certificate2(certificate);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static X509Certificate2 FindFallbackCertificate(StoreLocation location)
        {
            using (var store = new X509Store(StoreName.My, location))
            {
                try
                {
                    store.Open(OpenFlags.ReadOnly);
                    X509Certificate2 certificate = store.Certificates
                        .OfType<X509Certificate2>()
                        .Where(item => item.HasPrivateKey && IsCurrentlyValid(item))
                        .Where(item => item.Subject.IndexOf(
                            "WPE Mobile Local",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(item => item.NotAfter)
                        .FirstOrDefault();
                    return certificate == null ? null : new X509Certificate2(certificate);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static bool IsCurrentlyValid(X509Certificate2 certificate)
        {
            DateTime now = DateTime.Now;
            return certificate.NotBefore <= now && certificate.NotAfter > now;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            this.stopping.Cancel();
            this.listener.Stop();
            foreach (TcpClient client in this.activeClients.Keys)
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                }
            }
            try
            {
                this.acceptTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (this.activeClients.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }

            this.certificate.Dispose();
            this.stopping.Dispose();
        }

        private sealed class ResponseBodyTooLargeException : Exception
        {
            public ResponseBodyTooLargeException()
                : base("HTTP response body is too large.")
            {
            }
        }

        private sealed class LimitedMemoryStream : MemoryStream
        {
            private readonly int maximumLength;

            public LimitedMemoryStream(int maximumLength)
            {
                this.maximumLength = maximumLength;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                EnsureWriteCapacity(count);
                base.Write(buffer, offset, count);
            }

            public override Task WriteAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                EnsureWriteCapacity(count);
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override void WriteByte(byte value)
            {
                EnsureWriteCapacity(1);
                base.WriteByte(value);
            }

            public override void SetLength(long value)
            {
                if (value < 0 || value > this.maximumLength)
                {
                    throw new ResponseBodyTooLargeException();
                }
                base.SetLength(value);
            }

            private void EnsureWriteCapacity(int count)
            {
                if (count < 0 || Position > this.maximumLength - count)
                {
                    throw new ResponseBodyTooLargeException();
                }
            }
        }

        private sealed class RequestHead
        {
            public string Method { get; set; }
            public string Target { get; set; }
            public string Protocol { get; set; }
            public Dictionary<string, string[]> Headers { get; set; }
            public int ContentLength { get; set; }
            public byte[] InitialBody { get; set; }
            public bool HasTransferEncoding { get; set; }
            public bool ExpectsContinue { get; set; }
        }
    }
}
