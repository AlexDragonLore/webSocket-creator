using AustinHarris.JsonRpc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace webSocket_creator
{
    internal class RPCServer : IDisposable
    {
        private static CancellationTokenSource m_cancellation;
        private static HttpListener m_listener;

        private static string url = "";//$"http://{_host}:{_port}/";

        public static Dictionary<Guid, Tuple<HttpListenerWebSocketContext, Guid?>> Clients = new Dictionary<Guid, Tuple<HttpListenerWebSocketContext, Guid?>>();
        public static Dictionary<string, DateTime> TempCodes = new Dictionary<string, DateTime>();
        public static Dictionary<Guid, Guid> CreatedOrders = new Dictionary<Guid, Guid>();

        private static List<IPAddress> iPAddress = new List<IPAddress>();
        private static IPAddress choosenIp = null;
        private static int _port = 8001;
        private static string _host = "127.0.0.1";

        public RPCServer()
        {
            try
            {
                iPAddress.Clear();
                IPAddress pAddress = GetNetworkInterface();
                choosenIp = pAddress;

                m_listener = new HttpListener();
                m_listener.Prefixes.Add($"http://127.0.0.1:8001/");

                m_listener.Start();

                m_cancellation = new CancellationTokenSource();
                Task.Run(() => AcceptWebSocketClientsAsync(m_listener, m_cancellation.Token));

                Task.Run(() => UDPHostServerBroadcast());

            }
            catch (Exception ex)
            {
                if (choosenIp != null)
                    Console.WriteLine($"Server deployment with {choosenIp} failed.", ex);
                else
                    Console.WriteLine($"Server deployment with host failed.", ex);

            }
        }

        #region Private Methods

        private static IPAddress GetNetworkInterface()
        {
            var netInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            IPAddress pAddress = null;
            foreach (var localInterface in netInterfaces)
            {
                if (localInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                    localInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet3Megabit ||
                    localInterface.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx ||
                    localInterface.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT ||
                    localInterface.NetworkInterfaceType == NetworkInterfaceType.Fddi ||
                    localInterface.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ||
                    localInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    foreach (UnicastIPAddressInformation ip in localInterface.GetIPProperties().UnicastAddresses)
                    {
                        iPAddress.Add(ip.Address);
                        if (netInterfaces.Count() == 1 && string.IsNullOrWhiteSpace(_host))
                        {
                            pAddress = ip.Address;
                            Console.WriteLine($"For Host choosen next IP adress: {ip.Address} - interface name: {localInterface.Name} - of type: {localInterface.NetworkInterfaceType}");
                        }
                        else if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            //Console.WriteLine($"Discovered {ip.Address} for interface {localInterface.Name} of type {localInterface.NetworkInterfaceType}");
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(_host))
                        pAddress = IPAddress.Parse(_host);
                }
            }
            return pAddress;
        }

        private void UDPHostServerBroadcast()
        {
            int port = 8001;
            UdpClient udp = new UdpClient();

            var addressInt = BitConverter.ToInt32(choosenIp.GetAddressBytes(), 0);
            var maskInt = BitConverter.ToInt32(IPAddress.Parse("255.255.255.0").GetAddressBytes(), 0);
            var broadcastInt = addressInt | ~maskInt;
            var broadcast = new IPAddress(BitConverter.GetBytes(broadcastInt));

            IPEndPoint groupEP = new IPEndPoint(broadcast, port);

            var message = new UDPRequestColoumn()
            {
                Host = "ws://" + choosenIp,
                Port = _port
            };
            var messageString = JsonConvert.SerializeObject(message);

            byte[] sendBytes = Encoding.ASCII.GetBytes(messageString);

            while (true)
            {
                udp.EnableBroadcast = true;
                udp.Send(sendBytes, sendBytes.Length, groupEP);
                Thread.Sleep(10000);
            }
            udp.Close();

        }

        private static async Task AcceptWebSocketClientsAsync(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Console.WriteLine("Waiting for connection...");
                try
                {
                    var context = await listener.GetContextAsync();
                    if (!context.Request.IsWebSocketRequest)
                    {
                        
                        Console.WriteLine($"HTTP {context.Request.ProtocolVersion} connection initialized from {context.Request.RemoteEndPoint}");

                        HttpListenerResponse response = context.Response;

                        // Construct a response.
                        StringBuilder message = new StringBuilder();
                        message.Append("<HTML><BODY>");
                        message.Append("<p>HTTP NOT ALLOWED</p>");
                        message.Append("</BODY></HTML>");
                        byte[] buffer = Encoding.UTF8.GetBytes(message.ToString());

                        // Get a response stream and write the response to it.
                        response.ContentLength64 = buffer.Length;
                        response.StatusCode = 403;
                        Stream output = response.OutputStream;
                        output.Write(buffer, 0, buffer.Length);

                        // You must close the output stream.
                        output.Close();
                        response.Close();
                    }
                    else
                    {
                        var ws = await context.AcceptWebSocketAsync(null, new TimeSpan(0, 0, 3)).ConfigureAwait(false);
                        if (ws != null)
                        {
                            Guid connectionId = Guid.NewGuid();
                            Console.WriteLine($"Client Connected. Client IP: {context.Request.RemoteEndPoint.Address}, ConnectionId {connectionId}");
                            Clients.Add(connectionId, new Tuple<HttpListenerWebSocketContext, Guid?>(ws, null));

                            Task.Run(() => HandleConnectionAsync(ws.WebSocket, token, connectionId));
                        }
                        else
                        {
                            Console.WriteLine("WS context was null.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Acceptance exception", ex);
                }
            }
            Console.WriteLine("Cancellation was requested.");
        }

        private static async Task HandleConnectionAsync(WebSocket ws, CancellationToken cancellation, Guid connectionId)
        {
            try
            {
                while (ws.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                {
                    string message = await ReadString(ws).ConfigureAwait(false);

                    if (message.Contains("method"))
                    {
                        if (!message.Contains("GetImageById"))
                        {
                            Console.WriteLine($"REQUEST FROM CONNECTION {connectionId}: {message}");
                        }
                        string returnString = await JsonRpcProcessor.Process(message, connectionId);
                        if (returnString.Length != 0)
                        {
                            ArraySegment<byte> outputBuffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnString));
                            if (ws.State == WebSocketState.Open)
                            {
                                await ws.SendAsync(outputBuffer, WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);

                                if (!message.Contains("GetImageById") && !message.Contains("GetDirectory"))
                                {
                                    Console.WriteLine($"REPLY TO CONNECTION {connectionId}: {returnString}");
                                    return;
                                }
                            }
                            else
                            {
                                Console.WriteLine($"REPLY ERROR: connection {connectionId} is not open. Websocket state {ws.State }");
                                return;
                            }
                        }
                    }
                    else Console.WriteLine($"MESSAGE: {message}");
                }

                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"Connection exception:", ex);
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "Done", CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Do nothing
                }
            }
            finally
            {
                Tuple<HttpListenerWebSocketContext, Guid?> client;
                Clients.TryGetValue(connectionId, out client);
                if (client != null)
                {
                    Clients.Remove(connectionId);
                    Console.WriteLine($"Disposing connection {connectionId}");
                }
                else
                {
                    Console.WriteLine("Disposing nothing");
                }
                ws.Dispose();
            }
        }

        private static async Task<string> ReadString(WebSocket ws)
        {
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);

            WebSocketReceiveResult result = null;

            using (var ms = new MemoryStream())
            {
                do
                {
                    result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                using (var reader = new StreamReader(ms, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        #endregion


        public static async Task Notify(string rpcMethod)
        {
            string notification = rpcMethod;
            Console.WriteLine($"NOTIFICATION: {notification}");

            foreach (var client in Clients)
            {
                ArraySegment<byte> outputBuffer =
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(notification));

                var context = client.Value.Item1;
                if (context.WebSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await client.Value.Item1.WebSocket.SendAsync(outputBuffer, WebSocketMessageType.Text, true,
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Got Error while trying to notify client", ex);
                    }
                }
                else Console.WriteLine("Trying to notify client with unopened socket, check logic");
            }
        }

        public static async Task Notify(string rpcMethod, string rpcParams)
        {
            JsonNotification request = new JsonNotification
            {
                Method = rpcMethod,
                Params = JsonConvert.DeserializeObject(rpcParams),
            };

            string notification = JsonConvert.SerializeObject(request);
            Console.WriteLine($"NOTIFICATION: {notification}");

            foreach (var client in Clients)
            {
                ArraySegment<byte> outputBuffer =
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(notification));

                var context = client.Value.Item1;
                if (context.WebSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await client.Value.Item1.WebSocket.SendAsync(outputBuffer, WebSocketMessageType.Text, true,
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Got Error while trying to notify client", ex);
                    }
                }
                else Console.WriteLine("Trying to notify client with unopened socket, check logic");
            }
        }

        public void Dispose()
        {
            if (m_listener != null && m_cancellation != null)
            {
                try
                {
                    m_cancellation.Cancel();

                    m_listener.Stop();

                    m_listener = null;
                    m_cancellation = null;
                }
                catch
                {
                    // Log error
                }
            }
        }
    }

    internal class JsonNotification
    {
        public JsonNotification() { }

        [JsonProperty("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        public object Params { get; set; }
    }

    internal class UDPRequestColoumn
    {
        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }
    }
}