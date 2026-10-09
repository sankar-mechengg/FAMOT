using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace FAMOT.Protocol
{
    /// <summary>
    /// Receives UDP datagrams on a background thread and delivers them, parsed, on the Unity main
    /// thread (in arrival order). Bind failures are reported with a warning and never throw.
    /// The receive thread is stopped cooperatively (stop flag + socket close), never aborted.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UdpCommandServer : MonoBehaviour
    {
        private const int MaxMessagesPerFrame = 500;
        private const int MaxQueuedDatagrams = 20000;
        private const int ReceiveTimeoutMs = 500;

        [SerializeField] private int listenPort = 8400;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool logUnknownMessages = true;

        private sealed class ReceiverState
        {
            public UdpClient client;
            public Thread thread;
            public volatile bool stop;
            public volatile bool faulted;
        }

        private readonly ConcurrentQueue<(string text, IPEndPoint sender)> _queue =
            new ConcurrentQueue<(string text, IPEndPoint sender)>();

        private ReceiverState _state;
        private volatile string _lastError;
        private long _dropped;

        /// <summary>Raised on the main thread for every datagram, after parsing (Unknown included).</summary>
        public event Action<UdpMessage, IPEndPoint> MessageReceived;

        /// <summary>Raised on the main thread for every datagram's text, before parsing (for raw logging).</summary>
        public event Action<string, IPEndPoint> RawReceived;

        /// <summary>True while the socket is bound and the receive thread is healthy.</summary>
        public bool IsListening
        {
            get { return _state != null && !_state.stop && !_state.faulted; }
        }

        /// <summary>Configured (or current) listen port.</summary>
        public int ListenPort { get { return listenPort; } }

        /// <summary>Sender of the most recently processed datagram, or null.</summary>
        public IPEndPoint LastSender { get; private set; }

        /// <summary>Number of datagrams delivered to subscribers so far.</summary>
        public long ReceivedCount { get; private set; }

        /// <summary>Number of datagrams that failed to parse (Unknown type).</summary>
        public long ParseErrorCount { get; private set; }

        /// <summary>Datagrams discarded because the main thread fell too far behind.</summary>
        public long DroppedCount { get { return Interlocked.Read(ref _dropped); } }

        /// <summary>Last socket / bind error text, or null.</summary>
        public string LastError { get { return _lastError; } }

        private void OnEnable()
        {
            if (autoStart)
                StartListening();
        }

        private void OnDisable()
        {
            StopListening();
        }

        private void OnDestroy()
        {
            StopListening();
        }

        private void OnApplicationQuit()
        {
            StopListening();
        }

        /// <summary>Binds the socket and starts the receive thread. No-op if already listening.</summary>
        public void StartListening()
        {
            if (_state != null)
            {
                if (!_state.faulted)
                    return; // already running
                StopListening(); // faulted: tear down and retry
            }

            UdpClient client = null;
            try
            {
                client = new UdpClient(AddressFamily.InterNetwork);
                client.ExclusiveAddressUse = false;
                client.Client.ReceiveTimeout = ReceiveTimeoutMs;
                try { client.Client.ReceiveBufferSize = 1 << 20; } catch (Exception) { /* optional */ }
                client.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
            }
            catch (Exception ex)
            {
                _lastError = "Bind to UDP port " + listenPort + " failed: " + ex.Message;
                Debug.LogWarning("[UdpCommandServer] Could not listen on UDP port " + listenPort + " (" + ex.Message +
                                 "). The port is probably in use by another process or another running instance; " +
                                 "close it or choose a different port.", this);
                try { if (client != null) client.Close(); } catch (Exception) { }
                return;
            }

            _lastError = null;
            var state = new ReceiverState { client = client };
            state.thread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "FAMOT UDP command receiver"
            };
            _state = state;
            state.thread.Start(state);
        }

        /// <summary>Stops the receive thread and closes the socket. Safe to call repeatedly.</summary>
        public void StopListening()
        {
            ReceiverState state = _state;
            _state = null;
            if (state == null)
                return;

            state.stop = true;
            try { state.client.Close(); } catch (Exception) { }
            try
            {
                if (state.thread != null && state.thread.IsAlive)
                    state.thread.Join(250);
            }
            catch (Exception) { }

            // Drop stale datagrams so they are not delivered after a restart.
            while (_queue.TryDequeue(out _)) { }
        }

        /// <summary>Stops, switches to <paramref name="newPort"/> and starts again.</summary>
        public void Restart(int newPort)
        {
            StopListening();
            listenPort = newPort;
            StartListening();
        }

        /// <summary>Sends a UTF-8 datagram from the listening socket. Fire-and-forget; never throws.</summary>
        public void SendTo(IPEndPoint ep, string text)
        {
            ReceiverState state = _state;
            if (state == null || ep == null || string.IsNullOrEmpty(text))
                return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                state.client.Send(bytes, bytes.Length, ep);
            }
            catch (Exception ex)
            {
                _lastError = "Send failed: " + ex.Message;
            }
        }

        /// <summary>Sends a datagram to <see cref="LastSender"/> (no-op if nothing was received yet).</summary>
        public void ReplyToLastSender(string text)
        {
            IPEndPoint ep = LastSender;
            if (ep != null)
                SendTo(ep, text);
        }

        private void Update()
        {
            if (_queue.IsEmpty)
                return;

            int processed = 0;
            while (processed < MaxMessagesPerFrame && _queue.TryDequeue(out var item))
            {
                processed++;
                Dispatch(item.text, item.sender);
            }
        }

        private void Dispatch(string text, IPEndPoint sender)
        {
            LastSender = sender;
            ReceivedCount++;

            Action<string, IPEndPoint> raw = RawReceived;
            if (raw != null)
            {
                try { raw(text, sender); }
                catch (Exception ex) { Debug.LogException(ex, this); }
            }

            bool ok = UdpMessageParser.TryParse(text, out UdpMessage message);
            if (!ok)
            {
                ParseErrorCount++;
                if (logUnknownMessages)
                    Debug.LogWarning("[UdpCommandServer] Unparsed message from " + sender + ": " + message, this);
            }

            Action<UdpMessage, IPEndPoint> handler = MessageReceived;
            if (handler != null)
            {
                try { handler(message, sender); }
                catch (Exception ex) { Debug.LogException(ex, this); }
            }
        }

        // Runs on the background thread. Touches no Unity API.
        private void ReceiveLoop(object arg)
        {
            var state = (ReceiverState)arg;
            UdpClient client = state.client;

            while (!state.stop)
            {
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = client.Receive(ref remote);
                    if (state.stop)
                        break;
                    if (data == null || data.Length == 0)
                        continue;

                    if (_queue.Count >= MaxQueuedDatagrams)
                    {
                        Interlocked.Increment(ref _dropped);
                        continue;
                    }
                    _queue.Enqueue((Encoding.UTF8.GetString(data), remote));
                }
                catch (ObjectDisposedException)
                {
                    break; // socket closed by StopListening
                }
                catch (SocketException ex)
                {
                    if (state.stop)
                        break;
                    switch (ex.SocketErrorCode)
                    {
                        case SocketError.TimedOut:        // expected: lets us poll the stop flag
                        case SocketError.ConnectionReset: // ICMP port unreachable from an earlier reply
                        case SocketError.MessageSize:     // oversized datagram, skip it
                            continue;
                        default:
                            _lastError = "Receive failed: " + ex.SocketErrorCode + " " + ex.Message;
                            state.faulted = true;
                            return;
                    }
                }
                catch (Exception ex)
                {
                    if (state.stop)
                        break;
                    _lastError = "Receive failed: " + ex.Message;
                    state.faulted = true;
                    return;
                }
            }
        }
    }
}
