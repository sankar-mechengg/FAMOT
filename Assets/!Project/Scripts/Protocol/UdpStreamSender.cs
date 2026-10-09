using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace FAMOT.Protocol
{
    /// <summary>
    /// Sends text lines as UTF-8 UDP datagrams to a fixed target (typically MATLAB/Python on the
    /// same machine). Fire-and-forget; every failure is swallowed and reported through the return
    /// value. Main thread only.
    /// </summary>
    public sealed class UdpStreamSender : MonoBehaviour
    {
        private const int MaxDatagramBytes = 65000;

        [SerializeField] private string targetAddress = "127.0.0.1";
        [SerializeField] private int targetPort = 8401;
        [SerializeField] private bool streamingEnabled = true;
        [Tooltip("Maximum sends per second. 0 = no limit (every call is sent).")]
        [SerializeField] private int maxRatePerSecond = 0;

        private UdpClient _client;
        private IPEndPoint _endpoint;
        private bool _resolveFailed;
        private double _lastSendTime = double.NegativeInfinity;
        private byte[] _buffer = new byte[2048];

        /// <summary>Raised after a line was sent successfully (for raw logging).</summary>
        public event Action<string> Sent;

        /// <summary>Number of datagrams sent successfully.</summary>
        public long SentCount { get; private set; }

        /// <summary>Last error text, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>Enables or disables streaming at runtime.</summary>
        public bool StreamingEnabled
        {
            get { return streamingEnabled; }
            set { streamingEnabled = value; }
        }

        /// <summary>Maximum sends per second, 0 = unlimited.</summary>
        public int MaxRatePerSecond
        {
            get { return maxRatePerSecond; }
            set { maxRatePerSecond = Mathf.Max(0, value); }
        }

        /// <summary>Human readable target, e.g. "127.0.0.1:8401".</summary>
        public string TargetDescription
        {
            get { return targetAddress + ":" + targetPort; }
        }

        /// <summary>Changes the destination. The socket is recreated lazily on the next send.</summary>
        public void SetTarget(string address, int port)
        {
            targetAddress = address;
            targetPort = port;
            _endpoint = null;
            _resolveFailed = false;
            CloseClient();
        }

        /// <summary>
        /// Sends one line. Returns false if streaming is disabled, the call was rate limited,
        /// the line is empty or too long, or the send failed. Never throws.
        /// </summary>
        public bool Send(string line)
        {
            if (!streamingEnabled || string.IsNullOrEmpty(line))
                return false;

            try
            {
                if (maxRatePerSecond > 0)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now - _lastSendTime < 1.0 / maxRatePerSecond)
                        return false;
                    _lastSendTime = now;
                }

                if (!EnsureClient())
                    return false;

                int maxBytes = Encoding.UTF8.GetMaxByteCount(line.Length);
                if (_buffer.Length < maxBytes)
                    _buffer = new byte[Math.Max(maxBytes, _buffer.Length * 2)];
                int count = Encoding.UTF8.GetBytes(line, 0, line.Length, _buffer, 0);
                if (count > MaxDatagramBytes)
                {
                    LastError = "Line too long for one datagram (" + count + " bytes)";
                    return false;
                }

                _client.Send(_buffer, count, _endpoint);
                SentCount++;

                Action<string> handler = Sent;
                if (handler != null)
                {
                    try { handler(line); }
                    catch (Exception ex) { Debug.LogException(ex, this); }
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private bool EnsureClient()
        {
            if (_endpoint == null)
            {
                if (_resolveFailed)
                    return false;
                if (!TryResolve(targetAddress, targetPort, out _endpoint))
                {
                    _resolveFailed = true;
                    LastError = "Cannot resolve target address '" + targetAddress + "'";
                    Debug.LogWarning("[UdpStreamSender] " + LastError, this);
                    return false;
                }
            }

            if (_client == null)
                _client = new UdpClient(_endpoint.AddressFamily);
            return true;
        }

        private static bool TryResolve(string address, int port, out IPEndPoint endpoint)
        {
            endpoint = null;
            if (string.IsNullOrWhiteSpace(address) || port <= 0 || port > 65535)
                return false;

            string a = address.Trim();
            if (IPAddress.TryParse(a, out IPAddress ip))
            {
                endpoint = new IPEndPoint(ip, port);
                return true;
            }

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(a);
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        endpoint = new IPEndPoint(addresses[i], port);
                        return true;
                    }
                }
                if (addresses.Length > 0)
                {
                    endpoint = new IPEndPoint(addresses[0], port);
                    return true;
                }
            }
            catch (Exception)
            {
                // fall through
            }
            return false;
        }

        private void CloseClient()
        {
            try { if (_client != null) _client.Close(); } catch (Exception) { }
            _client = null;
        }

        private void OnDestroy()
        {
            CloseClient();
        }
    }
}
