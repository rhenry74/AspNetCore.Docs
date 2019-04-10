using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EchoApp
{
    public class Hub
    {
        private static Hub _hub = null;

        public static Hub GetInstance()
        {
            if (_hub == null)
            {
                _hub = new Hub();
            }

            return _hub;
        }

        private class Channel
        {
            public List<Task> Listeners { get; set; }
            public List<WebSocket> Sockets { get; set; }
        }

        private class Message
        {
            public string Recipient { get; set; }
            public string Body { get; set; }
            public string Sender { get; set; }
        }

        private ConcurrentDictionary<string, Channel> _nurseChannels = new ConcurrentDictionary<string, Channel>();

        private ConcurrentDictionary<string, Channel> _schedulerChannels = new ConcurrentDictionary<string, Channel>();

        private List<Message> _messages = new List<Message>();

        public Hub()
        {

        }

        internal void RegisterNurse(string userId, WebSocket socket)
        {
            var listener = Task.Run(() =>
            {
                ListenToNurse(socket, userId);
            });

            if (_nurseChannels.ContainsKey(userId))
            {
                var sockets = _nurseChannels[userId].Sockets;
                sockets.Add(socket);
                var listeners = _nurseChannels[userId].Listeners;
                listeners.Add(listener);
            }
            else
            {
                var sockets = new List<WebSocket>();
                sockets.Add(socket);
                var listeners = new List<Task>();
                listeners.Add(listener);
                var channel = new Channel
                {
                    Listeners = listeners,
                    Sockets = sockets
                };

                _nurseChannels.TryAdd(userId, channel);
            }            
        }

        private async void ListenToNurse(WebSocket socket, string userId)
        {
            try
            {
                var buffer = new byte[1024 * 4];
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                while (!result.CloseStatus.HasValue)
                {
                    var message = Encoding.UTF8.GetString(buffer).TrimEnd();
                    message = message.Substring(0, message.IndexOf('\0'));

                    foreach (var scheduler in _schedulerChannels.Keys)
                    {
                        lock (_messages)
                        {
                            Console.WriteLine($"Adding messge from {userId} to {scheduler}");
                            _messages.Add(new Message
                            {
                                Body = message,
                                Recipient = scheduler,
                                Sender = userId
                            });
                        }
                    }

                    buffer = new byte[1024 * 4];
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
            }
            finally
            {
                CleanUpSocketInNurseChannel(socket);
            }
        }

        private void CleanUpSocketInNurseChannel(WebSocket socket)
        {
            string channelToRemove = null;
            foreach (var id in _nurseChannels.Keys)
            {
                if (_nurseChannels[id].Sockets.Contains(socket))
                {
                    int index = _nurseChannels[id].Sockets.IndexOf(socket);
                    _nurseChannels[id].Sockets.Remove(socket);
                    Task listener = _nurseChannels[id].Listeners[index];
                    _nurseChannels[id].Listeners.Remove(listener);
                    listener.Dispose();
                    if(_nurseChannels[id].Sockets.Count==0)
                    {
                        channelToRemove = id;
                    }
                }
            }
            if (channelToRemove!=null)
            {
                _nurseChannels.Remove(channelToRemove, out Channel temp);
            }
        }

        internal bool MonitorNurse(string userId)
        {
            if (_nurseChannels.Keys.Contains(userId))
            {
                return true;
            }
            return false;
        }

        public bool HasMessageForNurse(string userId)
        {
            Thread.Sleep(10);
            return _messages.Where(m => m.Recipient==userId).Any();
        }

        public string GetMessageFor(string userId)
        {
            lock (_messages)
            {
                var message = _messages.Where(m => m.Recipient == userId).FirstOrDefault();
                Console.WriteLine($"Removing messge from {message.Sender} to {message.Recipient}");
                if (_messages.Remove(message))
                {
                    return message.Sender + ":" + message.Body;
                }
                else
                {
                    //try again
                    Thread.Sleep(100);
                    if (!_messages.Remove(message))
                    {
                        throw new Exception("Cannot remove a message from the message bag.");
                    }
                    return message.Sender + ":" + message.Body;
                }
            }
        }

        //public string GetMessageForScheduler(string userId)
        //{
        //    var message = _messages.Where(m => m.Recipient == userId).FirstOrDefault();
        //    _messages.TryTake(out message);
        //    return message.Sender + ":" + message.Body;
        //}

        internal void RegisterScheduler(string userId, WebSocket socket)
        {
            var listener = Task.Run(() =>
            {
                ListenToScheduler(socket, userId);
            });

            if (_schedulerChannels.ContainsKey(userId))
            {
                var sockets = _schedulerChannels[userId].Sockets;
                sockets.Add(socket);
                var listeners = _schedulerChannels[userId].Listeners;
                listeners.Add(listener);
            }
            else
            {
                var sockets = new List<WebSocket>();
                sockets.Add(socket);
                var listeners = new List<Task>();
                listeners.Add(listener);
                var channel = new Channel
                {
                    Listeners = listeners,
                    Sockets = sockets
                };

                _schedulerChannels.TryAdd(userId, channel);
            }

        }

        internal bool MonitorScheduler(string userId)
        {
            if (_schedulerChannels.Keys.Contains(userId))
            {
                return true;
            }
            return false;
        }

        public bool HasMessageForScheduler(string userId)
        {
            Thread.Sleep(10);
            return _messages.Where(m => m.Recipient==userId).Any();
        }

        
        private async void ListenToScheduler(WebSocket socket, string userId)
        {
            try
            {
                var buffer = new byte[1024 * 4];
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                while (!result.CloseStatus.HasValue)
                {
                    var message = Encoding.UTF8.GetString(buffer).TrimEnd();
                    var idAndMessage = message.Substring(0, message.IndexOf('\0')).Split(':');
                    var nurseId = idAndMessage[0];
                    message = idAndMessage[1];

                    foreach (var nurse in _nurseChannels.Keys)
                    {
                        if (nurse == nurseId)
                        {
                            lock (_messages)
                            {
                                Console.WriteLine($"Adding messge from {userId} to {nurse}");
                                _messages.Add(new Message
                                {
                                    Body = message,
                                    Recipient = nurse,
                                    Sender = userId
                                });
                            }
                        }
                    }

                    // relay scheduler messages to other schedulers, so they see the whole conversation
                    foreach (var scheduler in _schedulerChannels.Keys)
                    {
                        if (scheduler != userId)
                        {
                            lock (_messages)
                            {
                                Console.WriteLine($"Adding messge from {userId} to {scheduler}");
                                _messages.Add(new Message
                                {
                                    Body = message,
                                    Recipient = scheduler,
                                    Sender = userId
                                });
                            }
                        }
                    }

                    buffer = new byte[1024 * 4];
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
            }
            finally
            {
                CleanUpSocketInSchedulerChannel(socket);
            }
        }

        private void CleanUpSocketInSchedulerChannel(WebSocket socket)
        {
            string channelToRemove = null;
            foreach (var id in _schedulerChannels.Keys)
            {
                if (_schedulerChannels[id].Sockets.Contains(socket))
                {
                    int index = _schedulerChannels[id].Sockets.IndexOf(socket);
                    _schedulerChannels[id].Sockets.Remove(socket);
                    Task listener = _schedulerChannels[id].Listeners[index];
                    _schedulerChannels[id].Listeners.Remove(listener);
                    listener.Dispose();
                    if (_schedulerChannels[id].Sockets.Count == 0)
                    {
                        channelToRemove = id;
                    }
                }
            }
            if (channelToRemove != null)
            {
                _schedulerChannels.Remove(channelToRemove, out Channel temp);
            }
        }
    }
}
