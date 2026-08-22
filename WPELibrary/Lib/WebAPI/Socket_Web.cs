using Owin;
using System.Web.Http;
using Microsoft.Owin.StaticFiles;
using Microsoft.Owin.FileSystems;
using System.Net;
using System.Security.Principal;
using System.Text;
using System;
using System.Reflection;
using Microsoft.Owin;
using System.IO;

namespace WPELibrary.Lib.WebAPI
{
    public class Socket_Web
    {
        private const long MaxOwinRequestBodyBytes = 4L * 1024L * 1024L;
        private const long MaxOwinResponseBodyBytes = 16L * 1024L * 1024L;

        private static bool IsLocalNetworkAddress(string value)
        {
            if (!IPAddress.TryParse(value, out IPAddress address))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                return bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254);
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                byte[] bytes = address.GetAddressBytes();
                return address.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
            }

            return false;
        }

        private static bool TryGetBasicCredentials(
            string authorization,
            out string username,
            out string password)
        {
            username = string.Empty;
            password = string.Empty;

            if (string.IsNullOrWhiteSpace(authorization) ||
                !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                string encoded = authorization.Substring("Basic ".Length).Trim();
                byte[] credentialBytes = Convert.FromBase64String(encoded);
                string decoded;
                try
                {
                    decoded = new UTF8Encoding(false, true).GetString(credentialBytes);
                }
                catch (DecoderFallbackException)
                {
                    // RFC 7617 permits the historical ISO-8859-1 form. Keep it
                    // as a fallback for existing desktop/browser clients.
                    decoded = Encoding.GetEncoding("iso-8859-1").GetString(credentialBytes);
                }
                int separator = decoded.IndexOf(':');
                if (separator < 0)
                {
                    return false;
                }

                username = decoded.Substring(0, separator);
                password = decoded.Substring(separator + 1);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public void Configuration(IAppBuilder app)
        {
            try
            {
                app.Use(async (context, next) =>
                {
                    string contentLengthText = context.Request.Headers["Content-Length"];
                    long contentLength;
                    if (long.TryParse(contentLengthText, out contentLength) &&
                        contentLength > MaxOwinRequestBodyBytes)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                        await context.Response.WriteAsync("Request body is too large.");
                        return;
                    }

                    Stream originalResponseBody = context.Response.Body;
                    using (MemoryStream responseBody = new MemoryStream())
                    {
                        context.Response.Body = responseBody;
                        try
                        {
                            await next.Invoke();
                            if (responseBody.Length > MaxOwinResponseBodyBytes)
                            {
                                responseBody.SetLength(0);
                                context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                                await context.Response.WriteAsync("Response body is too large.");
                            }

                            responseBody.Position = 0;
                            await responseBody.CopyToAsync(originalResponseBody);
                        }
                        finally
                        {
                            context.Response.Body = originalResponseBody;
                        }
                    }
                });

                #region//HTTP Basic Authentication

                app.Use(async (context, next) =>
                {
                    var authHeader = context.Request.Headers["Authorization"];
                    string requestPath = context.Request.Path == null
                        ? string.Empty
                        : context.Request.Path.Value ?? string.Empty;
                    bool isMobileSync = requestPath.Equals(
                            "/MobileSync",
                            StringComparison.OrdinalIgnoreCase) ||
                        requestPath.StartsWith(
                            "/MobileSync/",
                            StringComparison.OrdinalIgnoreCase);

                    // MobileSync is limited to the local network and uses a
                    // dedicated low-privilege proxy account. All non-MobileSync
                    // routes remain behind administrator authentication.
                    if (isMobileSync)
                    {
                        if (!IsLocalNetworkAddress(context.Request.RemoteIpAddress))
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                            await context.Response.WriteAsync("MobileSync is available only from the local network.");
                            return;
                        }

                        if (!TryGetBasicCredentials(authHeader, out string mobileUsername, out string mobilePassword) ||
                            !Socket_Cache.ProxyAccount.IsValidMobile(mobileUsername, mobilePassword))
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                            context.Response.Headers.Add(
                                "WWW-Authenticate",
                                new[] { "Basic realm=\"WPE MobileSync\"" });
                            return;
                        }

                        await next.Invoke();
                        return;
                    }

                    if (TryGetBasicCredentials(authHeader, out string username, out string password))
                    {
                        if (Socket_Cache.ProxyAccount.IsValidAdmin(username, password))
                        {
                            var principal = new GenericPrincipal(new GenericIdentity(username), null);
                            context.Request.User = principal;

                            await next.Invoke();

                            return;
                        }
                    }

                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    context.Response.Headers.Add("WWW-Authenticate", new[] { "Basic realm=\"WPE x64\"" });
                });

                #endregion

                #region//设置 Web API 路由

                var config = new HttpConfiguration();
                config.MapHttpAttributeRoutes();               

                app.UseWebApi(config);

                #endregion

                #region//静态文件

                var staticFileOptions = new StaticFileOptions
                {
                    FileSystem = new PhysicalFileSystem(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web")),
                    ServeUnknownFileTypes = true
                };

                app.UseStaticFiles(staticFileOptions);

                #endregion

                #region//默认文档

                var defaultFileOptions = new DefaultFilesOptions
                {
                    DefaultFileNames = new[] { "index.html" }
                };

                app.UseDefaultFiles(defaultFileOptions);

                #endregion

                #region//处理默认路径

                app.Use(async (context, next) =>
                {
                    if (context.Request.Path == new PathString("/"))
                    {
                        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "index.html");

                        if (File.Exists(filePath))
                        {
                            context.Response.ContentType = "text/html";
                            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            {
                                await fileStream.CopyToAsync(context.Response.Body);
                            }
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        }
                    }
                    else
                    {
                        await next.Invoke();
                    }
                });

                #endregion

                #region//处理 ProxyAccount 路径

                app.Use(async (context, next) =>
                {
                    if (context.Request.Path == new PathString("/ProxyAccount"))
                    {
                        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "ProxyAccount.html");

                        if (File.Exists(filePath))
                        {
                            context.Response.ContentType = "text/html";
                            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            {
                                await fileStream.CopyToAsync(context.Response.Body);
                            }
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        }
                    }
                    else
                    {
                        await next.Invoke();
                    }
                });

                #endregion                

                #region//处理 SystemLog 路径

                app.Use(async (context, next) =>
                {
                    if (context.Request.Path == new PathString("/SystemLog"))
                    {
                        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "SystemLog.html");

                        if (File.Exists(filePath))
                        {
                            context.Response.ContentType = "text/html";
                            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            {
                                await fileStream.CopyToAsync(context.Response.Body);
                            }
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        }
                    }
                    else
                    {
                        await next.Invoke();
                    }
                });

                #endregion                                                

                #region//处理 Account 路径

                app.Use(async (context, next) =>
                {
                    if (context.Request.Path == new PathString("/account"))
                    {
                        if (context.Request.Method == "GET")
                        {
                            string sReturn = WebAPI.CCProxy_Controller.QueryUserAll();

                            if (!string.IsNullOrEmpty(sReturn))
                            {
                                context.Response.ContentType = "text/html; charset=utf-8";
                                await context.Response.WriteAsync(sReturn);
                            }
                        }
                        else if (context.Request.Method == "POST")
                        {
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";

                            var body = await context.Request.ReadFormAsync();

                            bool IsAdd = false;
                            if (body["add"] != null)
                            {
                                IsAdd = Socket_Operation.StringToBool(body["add"].ToString());
                            }

                            bool IsDel = false;
                            if (body["delete"] != null)
                            {
                                IsDel = Socket_Operation.StringToBool(body["delete"].ToString());
                            }

                            bool IsEdit = false;
                            if (body["edit"] != null)
                            {
                                IsEdit = Socket_Operation.StringToBool(body["edit"].ToString());
                            }

                            Proxy_AccountInfo pai = new Proxy_AccountInfo();

                            if (body["enable"] != null)
                            {
                                pai.IsEnable = Socket_Operation.StringToBool(body["enable"].ToString());
                            }

                            if (body["username"] != null)
                            {
                                pai.UserName = body["username"].ToString();
                            }

                            if (body["password"] != null)
                            {
                                pai.PassWord = body["password"].ToString();
                            }

                            if (body["autodisable"] != null)
                            {
                                pai.IsExpiry = Socket_Operation.StringToBool(body["autodisable"].ToString());
                            }

                            if (body["disabledate"] != null && body["disabletime"] != null)
                            {
                                pai.ExpiryTime = Socket_Operation.StringToDateTime(body["disabledate"].ToString(), body["disabletime"].ToString());
                            }

                            if (IsAdd)
                            {
                                if (WebAPI.CCProxy_Controller.AddUser(pai))
                                {
                                    await context.Response.WriteAsync("1");
                                }
                            }

                            if (IsDel)
                            {
                                if (body["userid"] != null)
                                {
                                    string UserName = body["userid"].ToString();
                                    if (WebAPI.CCProxy_Controller.DelUser(UserName))
                                    {
                                        await context.Response.WriteAsync("1");
                                    }
                                }
                            }

                            if (IsEdit)
                            {
                                if (WebAPI.CCProxy_Controller.UserUpdate(pai))
                                {
                                    await context.Response.WriteAsync("1");
                                }
                            }
                        }                        
                    }
                    else
                    {                        
                        await next();
                    }
                });

                #endregion                
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog(MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }
    }
}
