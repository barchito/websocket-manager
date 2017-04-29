using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using WebSocketManager.Common;

namespace WebSocketManager
{
    public abstract class WebSocketHandler
    {
        protected WebSocketConnectionManager WebSocketConnectionManager { get; set; }
        private JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
        public WebSocketHandler(WebSocketConnectionManager webSocketConnectionManager)
        {
            WebSocketConnectionManager = webSocketConnectionManager;
        }

        public virtual async Task OnConnectedAsync(WebSocket socket)
        {
            WebSocketConnectionManager.AddSocket(socket);

            await SendMessageAsync(socket, new Message()
            {
                MessageType = MessageType.ConnectionEvent,
                Data = WebSocketConnectionManager.GetId(socket)
            }).ConfigureAwait(false);
        }

        public virtual async Task OnDisconnectedAsync(WebSocket socket)
        {
            await WebSocketConnectionManager.RemoveSocket(WebSocketConnectionManager.GetId(socket)).ConfigureAwait(false);
        }

        public virtual async Task SendMessageAsync(WebSocket socket, Message message)
        {
            if (socket.State != WebSocketState.Open)
                return;

            var serializedMessage = JsonConvert.SerializeObject(message, _jsonSerializerSettings);
            await socket.SendAsync(buffer: new ArraySegment<byte>(array: Encoding.ASCII.GetBytes(serializedMessage),
                                                                  offset: 0,
                                                                  count: serializedMessage.Length),
                                   messageType: WebSocketMessageType.Text,
                                   endOfMessage: true,
                                   cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }

        public virtual async Task SendMessageAsync(string socketId, Message message)
        {
            await SendMessageAsync(WebSocketConnectionManager.GetSocketById(socketId), message).ConfigureAwait(false);
        }

        public virtual async Task SendMessageToAllAsync(Message message)
        {
            foreach (var pair in WebSocketConnectionManager.GetAll())
            {
                if (pair.Value.State == WebSocketState.Open)
                    await SendMessageAsync(pair.Value, message).ConfigureAwait(false);
            }
        }

        public virtual async Task InvokeClientMethodAsync(string socketId, string methodName, object[] arguments)
        {
            var message = new Message()
            {
                MessageType = MessageType.ClientMethodInvocation,
                Data = JsonConvert.SerializeObject(new InvocationDescriptor()
                {
                    MethodName = methodName,
                    Arguments = arguments
                }, _jsonSerializerSettings)
            };

            await SendMessageAsync(socketId, message).ConfigureAwait(false);
        }

        public virtual async Task InvokeClientMethodToAllAsync(string methodName, params object[] arguments)
        {
            foreach (var pair in WebSocketConnectionManager.GetAll())
            {
                if (pair.Value.State == WebSocketState.Open)
                    await InvokeClientMethodAsync(pair.Key, methodName, arguments).ConfigureAwait(false);
            }
        }

        public virtual async Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, string serializedInvocationDescriptor)
        {
            InvocationDescriptor invocationDescriptor = null;
            try
            {
                if (string.IsNullOrEmpty(serializedInvocationDescriptor)) {
                    await SendMessageAsync(socket, new Message()
                    {
                        MessageType = MessageType.Text,
                        Data = "The message is empty"
                    }).ConfigureAwait(false);
                    return;

                }


                invocationDescriptor = JsonConvert.DeserializeObject<InvocationDescriptor>(serializedInvocationDescriptor);
            }
            catch (Exception)
            {
                await SendMessageAsync(socket, new Message()
                {
                    MessageType = MessageType.Text,
                    Data = $"Cannot deserialize the message"
                }).ConfigureAwait(false);
                return;
            }

            if (invocationDescriptor == null || string.IsNullOrEmpty(invocationDescriptor.MethodName))
            {
                await SendMessageAsync(socket, new Message()
                {
                    MessageType = MessageType.Text,
                    Data = $"Cannot find method {invocationDescriptor.MethodName}"
                }).ConfigureAwait(false);
                return;
            }


            var method = this.GetType().GetMethod(invocationDescriptor.MethodName);

            if (method == null)
            {
                await SendMessageAsync(socket, new Message()
                {
                    MessageType = MessageType.Text,
                    Data = $"Cannot find method {invocationDescriptor.MethodName}"
                }).ConfigureAwait(false);
                return;
            }

            try
            {
                method.Invoke(this, invocationDescriptor.Arguments);
            }
            catch (TargetParameterCountException e)
            {
                await SendMessageAsync(socket, new Message()
                {
                    MessageType = MessageType.Text,
                    Data = $"The {invocationDescriptor.MethodName} method does not take {invocationDescriptor.Arguments?.Length} parameters!"
                }).ConfigureAwait(false);
            }

            catch (ArgumentException e)
            {
                await SendMessageAsync(socket, new Message()
                {
                    MessageType = MessageType.Text,
                    Data = $"The {invocationDescriptor.MethodName} method takes different arguments!"
                }).ConfigureAwait(false);
            }
        }
    }
}