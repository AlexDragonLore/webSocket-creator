using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace webSocket_creator
{
    class Program
    {
        static void Main(string[] args)
        {
            RPCServer server = new RPCServer();
            while (true)
            {
                var rpcReqSoft= Console.ReadLine();
                if (rpcReqSoft == "")
                {
                    Console.WriteLine("Empty string ? ()_()");
                }
                else
                {
                    Task.Run(async () => await
                    RPCServer.Notify(rpcReqSoft)).Wait();
                }
            }
        }
    }
}