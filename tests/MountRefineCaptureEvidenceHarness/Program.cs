using System;
using System.Net;
using System.Net.Sockets;
using WPELibrary.Lib;
using WPELibrary.Lib.MountSpeed;

namespace MountRefineCaptureEvidenceHarness
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Socket_Cache.SocketQueue.ResetSocketQueue();
                Socket_Cache.SocketList.lstRecPacket.Clear();
                Socket_Cache.SocketList.BeginCaptureSession();

                byte[] packetBytes = ParseHex(
                    "4D 5A 00 00 00 00 00 00 00 1D A0 50 00 06 03 00 00 00 00 13 " +
                    "32 30 39 32 31 30 30 34 34 32 39 33 33 38 33 33 37 33 32");
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                TcpClient client = new TcpClient();
                var acceptTask = listener.AcceptTcpClientAsync();
                client.Connect(IPAddress.Loopback, port);
                TcpClient server = acceptTask.GetAwaiter().GetResult();
                int capturedSocket = client.Client.Handle.ToInt32();
                try
                {
                    Socket_Cache.SocketPacket.SpeedMode = true;
                    Socket_Cache.SocketQueue.SocketPacket_ToQueue(
                        client.Client.Handle.ToInt32(),
                        (byte[])packetBytes.Clone(),
                        (byte[])packetBytes.Clone(),
                        Socket_Cache.SocketPacket.PacketType.WS2_Send,
                        new Socket_Cache.SocketPacket.SockAddr(),
                        Socket_Cache.Filter.FilterAction.None,
                        DateTime.Now.AddMilliseconds(1));
                }
                finally
                {
                    Socket_Cache.SocketPacket.SpeedMode = false;
                    client.Close();
                    server.Close();
                    listener.Stop();
                }

                if (Socket_Cache.SocketList.CaptureMountRefineA050Evidence().Count != 1)
                {
                    throw new InvalidOperationException(
                        "SpeedMode A050 evidence was not retained before the UI queue.");
                }
                if (Socket_Cache.SocketQueue.qSocket_PacketInfo.Count != 0 ||
                    Socket_Cache.SocketList.lstRecPacket.Count != 0)
                {
                    throw new InvalidOperationException(
                        "SpeedMode unexpectedly populated the bounded UI queue.");
                }

                // Simulates the visible packet table's 5000-row auto-clear.
                Socket_Cache.SocketList.lstRecPacket.Clear();
                MountRefineA050PacketTemplate template;
                MountRefineA050RouteTemplate route;
                string error;
                bool bound = MountRefineA050CaptureBinding.TryCreateFromCurrentCapture(
                    "capture-evidence-regression",
                    out template,
                    out route,
                    out error);
                if (!bound || template == null || route == null)
                {
                    throw new InvalidOperationException(
                        "A050 binding did not survive visible-list auto-clear: " + error);
                }
                if (route.PacketSocket != capturedSocket ||
                    route.PacketType != Socket_Cache.SocketPacket.PacketType.WS2_Send)
                {
                    throw new InvalidOperationException(
                        "A050 binding did not preserve the manually selected Socket route.");
                }

                Socket_Cache.SocketList.StopCaptureSessionPreservingRoutes();
                if (Socket_Cache.SocketList.CaptureSessionActive)
                {
                    throw new InvalidOperationException(
                        "Stopping capture did not mark the recording session inactive.");
                }
                if (Socket_Cache.SocketList.CaptureMountRefineA050Evidence().Count != 1)
                {
                    throw new InvalidOperationException(
                        "Stopping capture unexpectedly cleared the current A050 route evidence.");
                }
                bound = MountRefineA050CaptureBinding.TryCreateFromCurrentCapture(
                    "capture-evidence-after-stop",
                    out template,
                    out route,
                    out error);
                if (!bound || route == null || route.PacketSocket != capturedSocket)
                {
                    throw new InvalidOperationException(
                        "A050 binding did not survive stopping capture: " + error);
                }

                Socket_Cache.SocketList.BeginCaptureSession();
                if (Socket_Cache.SocketList.CaptureMountRefineA050Evidence().Count != 0)
                {
                    throw new InvalidOperationException(
                        "A new injection session reused previous A050 evidence.");
                }

                bound = MountRefineA050CaptureBinding.TryCreateFromCurrentCapture(
                    "capture-evidence-regression-new-session",
                    out template,
                    out route,
                    out error);
                if (bound || !string.Equals(
                        error,
                        "mount_refine_a050_outbound_capture_missing",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "A new empty session did not fail closed.");
                }

                byte[] wrongProtocolPacket = new byte[39];
                wrongProtocolPacket[0] = 0x4D;
                wrongProtocolPacket[1] = 0x5A;
                wrongProtocolPacket[10] = 0xA0;
                wrongProtocolPacket[11] = 0x51;
                Socket_Cache.SocketList.lstRecPacket.Add(new Socket_PacketInfo
                {
                    PacketTime = DateTime.Now,
                    PacketSocket = 123,
                    PacketType = Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    PacketFrom = "127.0.0.1:1000",
                    PacketTo = "127.0.0.1:2000",
                    RawBuffer = (byte[])wrongProtocolPacket.Clone(),
                    PacketBuffer = wrongProtocolPacket,
                    PacketLen = wrongProtocolPacket.Length
                });
                string diagnostics;
                bound = MountRefineA050CaptureBinding.TryCreateFromCurrentCapture(
                    "capture-evidence-diagnostic-regression",
                    out template,
                    out route,
                    out error,
                    out diagnostics);
                if (bound || diagnostics.IndexOf(
                        "reject=a050_protocol_not_matched=1",
                        StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "A050 rejection diagnostics did not identify the wrong protocol: " +
                        diagnostics);
                }

                Console.WriteLine(
                    "PASS: current-session A050 evidence survives UI auto-clear and resets on a new session.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL_TYPE: " + exception.GetType().FullName);
                Console.Error.WriteLine("FAIL_MESSAGE: " + exception.Message);
                Console.Error.WriteLine("FAIL_STACK: " + exception.StackTrace);
                return 1;
            }
        }

        private static byte[] ParseHex(string value)
        {
            string[] parts = value.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            byte[] result = new byte[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                result[index] = Convert.ToByte(parts[index], 16);
            }
            return result;
        }
    }
}
