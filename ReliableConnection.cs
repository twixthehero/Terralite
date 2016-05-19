using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using System.Threading;

namespace Terralite
{
    /// <summary>
    /// ReliableClient extends the base Client functionality
    /// by adding reliable sending. Packets that are transmitted incorrectly
    /// will be resent. Packets that arrive out of order will be fixed.
    /// Packets can be send with a 100% guarantee they will arrive.
    /// </summary>
    public class ReliableConnection
    {
        /// <summary>
        /// Whether to log the extra debug information
        /// </summary>
        public bool Debug { get; set; }

        /// <summary>
        /// Maximum number of send retries.
        /// Default value = 10.
        /// </summary>
        public int MaxRetries { get; set; }

        /// <summary>
        /// Interval to retry sending in millisenconds.
        /// Default value = 0.3f.
        /// </summary>
        public float RetryInterval { get; set; }

        /// <summary>
        /// Whether or not to use MD5 verification.
        /// Defaults to true.
        /// </summary>
        public bool UseMD5 { get; set; }

        /// <summary>
        /// Instance of MD5 class
        /// </summary>
        public MD5 MD5 { get; private set; }

        /// <summary>
        /// Whether or not to use guaranteed ordering.
        /// Defaults to true.
        /// </summary>
        /// <remarks>
        /// When this is set to true, packets received out of order will be acknowledged
        /// but the Receive event will not fire until the missing packets are
        /// received.
        /// </remarks>
        public bool UseOrdering { get; set; }

        public delegate void ReceiveEvent(byte[] data, int numBytes);
        public event ReceiveEvent Receive;

        protected const string DEFAULT_LOG = "log.txt";
        protected const int DEFAULT_PORT = 10346;

        private Socket socket;
        private Thread receiveThread;

        private Dictionary<int, Connection> idToConnection;
        private Dictionary<EndPoint, Connection> epToConnection;

        /// <summary>
        /// Creates a <c>ReliableClient</c> object with the default
        /// log file (log.txt).
        /// </summary>
        public ReliableConnection() : this(DEFAULT_LOG) { }

        /// <summary>
        /// Creates a <c>ReliableClient</c> object using <paramref name="logfile"/>
        /// as the log file.
        /// </summary>
        /// <param name="logfile">The logfile to use. Use <c>null</c> for
        /// no logging.</param>
        /// <param name="port">The port to use</param>
        public ReliableConnection(string logfile, int port = DEFAULT_PORT)
        {
            MaxRetries = 10;
            RetryInterval = 0.3f;
            UseMD5 = true;
            UseOrdering = true;

            MD5 = MD5.Create();
            idToConnection = new Dictionary<int, Connection>();

            if (logfile != null)
                CreateLog(logfile);

            CreateSocket(port);
        }

        /// <summary>
        /// Sets Console.Out to be a FileStream to <paramref name="logfile"/>
        /// </summary>
        /// <param name="logfile"></param>
        private void CreateLog(string logfile)
        {
            Console.SetOut(new StreamWriter(File.OpenWrite(logfile)));
            Log("Output set to " + logfile);
        }

        /// <summary>
        /// Creates the socket object, sets it to non-blocking, and
        /// binds it to a port.
        /// </summary>
        /// <param name="port">Port to try and bind to</param>
        private void CreateSocket(int port)
        {
            Log("Creating socket...");
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;

            try
            {
                Log("Binding socket...");
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            catch (SocketException e)
            {
                Log(e.Message);
                if (e.InnerException != null)
                    Log(e.InnerException);
                Log(e.StackTrace);
            }
            finally
            {
                if (!socket.IsBound)
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                Log("Socket bound to " + socket.LocalEndPoint);
            }
        }

        public void Send(int id, string text)
        {
            Send(id, Encoding.UTF8.GetBytes(text));
        }

        public void Send(int id, byte[] buffer = null, byte[] header = null)
        {
            if (!idToConnection.ContainsKey(id))
            {
                Log("ID doesn't exist in connections: " + id);
                return;
            }

            if (buffer == null && header == null)
                return;

            try
            {
                byte[] head = header ?? Packet.HEADER_NON_RELIABLE;
                byte[] data = Utils.Combine(head, buffer);

                if (data.Length <= Packet.MAX_SEND_SIZE)
                {
                    if (Debug)
                        Log("Sending data size " + data.Length);

                    socket.SendTo(data, idToConnection[id].EndPoint);
                }
                else
                {
                    byte[][] packets = Utils.SplitBuffer(head, buffer);

                    if (Debug)
                        Log("Split buffer into " + packets.GetLength(0) + "packets");

                    foreach (byte[] packet in packets)
                    {
                        if (Debug)
                            Log("Sending data size " + packet.Length);

                        socket.SendTo(packet, idToConnection[id].EndPoint);
                    }
                }
            }
            catch (Exception e)
            {
                Log(e.Message);
                if (e.InnerException != null)
                    Log(e.InnerException);
                Log(e.StackTrace);
            }
        }

        /// <summary>
        /// Sends <paramref name="text"/> reliably and encoded as UTF8
        /// </summary>
        /// <param name="text">Text to send</param>
        public void SendReliable(int id, string text)
        {
            SendReliable(id, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Reliably sends <paramref name="packet"/>
        /// </summary>
        /// <param name="packet">Data to send</param>
        public void SendReliable(int id, byte[] packet)
        {
            GuaranteedPacket gp = idToConnection[id].AddPacket(packet, MD5.ComputeHash(packet));
            Send(id, gp.ByteArray, gp.Header);
        }

        /// <summary>
        /// Called before <c>OnReceive</c> to do packet preprocessing.
        /// </summary>
        /// <param name="header">Header of packet</param>
        /// <param name="data">Packet data to do preprocessing with</param>
        /// <returns>Whether or not <c>OnReceive</c> needs to be called</returns>
        protected virtual bool OnPreReceive(EndPoint endPoint, byte[] header, byte[] data)
        {
            Connection conn = epToConnection[endPoint];
            return conn.OnPreReceive(header, data);
        }

        protected void OnReceive(EndPoint endPoint, byte[] data)
        {

        }

        /// <summary>
        /// Called by an OrderedPacket when its timeout has been reached.
        /// </summary>
        /// <param name="packetid">ID of packet that timed out</param>
        protected void OnPacketTimeout(ushort packetid)
        {
            /*OrderedPacket op = (OrderedPacket)orderedPackets[(object)packetid];
            orderedPackets.Remove(packetid);
            nextExpectedID = (byte)((packetid + 1) % byte.MaxValue);

            OnReceive(op.Data, op.Data.Length);*/
        }

        /// <summary>
        /// Helper function to shorten output lines.
        /// </summary>
        /// <typeparam name="T">Type that <paramref name="obj"/> is</typeparam>
        /// <param name="obj">The data to log to <c>Console.Out</c></param>
        protected void Log<T>(T obj)
        {
            Console.WriteLine(obj);
        }

        /// <summary>
        /// Class to hold data for handling an ordered packet
        /// </summary>
        private class OrderedPacket
        {
            public byte PacketID { get; set; }
            public byte[] Data { get; set; }

            private ReliableConnection reliableClient;
            private System.Timers.Timer timer;

            public OrderedPacket(ReliableConnection rc, byte packetID, byte[] packet, float timeout)
            {
                reliableClient = rc;
                PacketID = packetID;
                Data = packet;

                timer = new System.Timers.Timer(timeout);
                timer.Elapsed += (sender, args) =>
                {
                    reliableClient.OnPacketTimeout(PacketID);
                };
                timer.Start();
            }
        }
    }
}
