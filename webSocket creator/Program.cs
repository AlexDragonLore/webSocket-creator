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
            //"{\"jsonrpc\":\"2.0\",\"method\":\"UpdateReserve\",\"id\":\"6758e036-dd05-4361-aa36-9867a381e158\",\"params\":{\"guid\":null,\"date\":\"2021-08-20T20:30:00+03:00\",\"registerTime\":\"2021-08-20T13:38:39+03:00\",\"duration\":85,\"number\":\"2935\",\"type\":\"reserve\",\"guestCount\":\"3\",\"status\":\"other\",\"comment\":\"\",\"tablesGUIDs\":[\"d18f961b-d9db-4a6d-96e5-4b4510950a1e\"],\"isRemind\":1,\"cancelReason\":\"\",\"DepositSum\":null,\"comingTime\":null,\"closingTime\":null,\"guest\":{\"surname\":null,\"middleName\":null,\"birthday\":null,\"gender\":null,\"name\":\"\u0412\u0438\u043a\u0442\u043e\u0440 \u041b\u0435\u0431\u0435\u0434\u0435\u04322\",\"cards\":[{\"cardNumber\":null,\"cardTrack\":null}],\"phones\":[\"79196980879\"],\"emails\":[\"rammy231@gmail.com\"]}}}"
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