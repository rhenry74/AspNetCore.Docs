using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EchoApp
{
    public class NurseServer
    {
        private string _route;

        public NurseServer(string route)
        {
            _route = route;
        }

        public async Task Start(HttpContext context, Func<Task> next)
        {            
            if (context.Request.Path == _route)
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await Process(context, webSocket);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }
            else
            {
                await next();
            }
        
        }

        private async Task Process(HttpContext context, WebSocket webSocket)
        {
            string userId = null;

            var buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (!result.CloseStatus.HasValue)
            {
                //on first connecting the client will send the user's id
                userId = Encoding.UTF8.GetString(buffer);
                userId = userId.Substring(0, userId.IndexOf('\0'));
            }

            var hub = Hub.GetInstance();
            hub.RegisterNurse(userId, webSocket);

            while (hub.MonitorNurse(userId))
            {
                if (hub.HasMessageForNurse(userId))
                {
                    string message = hub.GetMessageFor(userId);

                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(message), 0, message.Length), 
                        WebSocketMessageType.Text, true, CancellationToken.None);                    
                }                
            }

            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }
    }
}

