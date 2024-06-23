using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Newtonsoft.Json;

namespace spacecorps2024_server.Socketing
{
    public class WebSocketMiddleWare(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;
        private static readonly ConcurrentDictionary<string, WebSocket> _sockets = new ConcurrentDictionary<string, WebSocket>();

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync();
                var socketId = Guid.NewGuid().ToString();
                _sockets.TryAdd(socketId, socket);

                await Receive(socket, async (result, buffer) =>
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await HandleMessage(socketId, message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _sockets.TryRemove(socketId, out WebSocket removedSocket);
                        if (result.CloseStatus != null)
                        {
                            await socket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);

                        }
                        else
                        {
                            throw new Exception("CloseStatus null for result " + result);
                        }
                    }
                });
            }
            else
            {
                await _next(context);
            }
        }

        private static async Task Receive(WebSocket socket, Action<WebSocketReceiveResult, byte[]> handleMessage)
        {
            var buffer = new byte[1024 * 4];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                handleMessage(result, buffer);
            }
        }

        private static async Task HandleMessage(string socketId, string message)
        {
            var playerMessage = JsonConvert.DeserializeObject<PlayerMessage>(message);
            if (playerMessage != null)
            {
                foreach (var socket in _sockets.Values)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        var broadcastMessage = JsonConvert.SerializeObject(new
                        {
                            playerId = socketId,
                            position = playerMessage.Position
                        });

                        var encodedMessage = Encoding.UTF8.GetBytes(broadcastMessage);
                        await socket.SendAsync(new ArraySegment<byte>(encodedMessage, 0, encodedMessage.Length), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }
    }

    public class PlayerMessage
    {
        public string PlayerId { get; set; }
        public Position Position { get; set; }

    }

    public class Position
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public static class WebSocketMiddleWareExtensions
    {
        public static IApplicationBuilder UseWebSocketMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<WebSocketMiddleWare>();
        }
    }

}
