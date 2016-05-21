using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Terralite
{
    /// <summary>
    /// ReliableConnection allows for sending data reliably or not.
    /// If reliability is used, packets that are transmitted incorrectly
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
        /// Whether to exit when a receive exception is thrown
        /// </summary>
        public bool ExitOnReceiveException { get; set; }

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

        protected const string DEFAULT_LOG = "log.txt";

        private Socket socket;
        private Thread receiveThread;

        private Dictionary<int, Connection> idToConnection;
        private Dictionary<EndPoint, Connection> epToConnection;
        private int nextConnectionID = 1;

        /// <summary>
        /// Creates a <c>ReliableClient</c> object with the default
        /// log file (log.txt).
        /// </summary>
        public ReliableConnection() : this(DEFAULT_LOG) { }

        /// <summary>
        /// Creates a <c>ReliableConnection</c> object using <paramref name="logfile"/>
        /// as the log file.
        /// </summary>
        /// <param name="logfile">The logfile to use. Use <c>null</c> for
        /// no logging.</param>
        public ReliableConnection(string logfile)
        {
            Debug = true;
            ExitOnReceiveException = false;
            MaxRetries = 10;
            RetryInterval = 0.3f;
            UseMD5 = true;
            UseOrdering = true;

            MD5 = MD5.Create();
            idToConnection = new Dictionary<int, Connection>();
            epToConnection = new Dictionary<EndPoint, Connection>();

            if (logfile != null)
                CreateLog(logfile);

            CreateSocket();
        }

        /// <summary>
        /// Whether this <c>ReliableConnection</c> is currently connected to any
        /// remote endpoints
        /// </summary>
        public bool IsConnected
        {
            get { return idToConnection.Count > 0; }
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
        /// binds it.
        /// </summary>
        private void CreateSocket()
        {
            Log("Creating socket...");
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;

            try
            {
                Log("Binding socket...");
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
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
                {
                    Log("Failed to bind socket");

                    if (ExitOnReceiveException)
                        Environment.Exit(1);
                }
            }
        }

        /// <summary>
        /// Parses <paramref name="to"/> and creates an IPAddress from it.
        /// </summary>
        /// <param name="to">IP address or hostname to connect to</param>
        /// <param name="port">Port to connect to</param>
        public void Connect(string to, int port)
        {
            IPAddress address = null;
            bool success = IPAddress.TryParse(to, out address);

            if (!success)
            {
                try
                {
                    address = Dns.GetHostEntry(to).AddressList[0];
                }
                catch (Exception e)
                {
                    Log(e.Message);
                    if (e.InnerException != null)
                        Log(e.InnerException);
                    Log(e.StackTrace);
                }
            }

            if (address == null)
            {
                throw new TerraliteException("Invalid IP address/hostname: '" + to + "'.");
            }

            if (port < 0 || port >= 65536)
            {
                throw new TerraliteException("Invalid port: " + port + ". Must be between (0 - 65535)");
            }

            //successfully resolved the ip/hostname

            if (receiveThread == null)
            {
                receiveThread = new Thread(new ThreadStart(ReceiveThread));
                receiveThread.Start();
            }

            //create new connection
            CreateConnection(address, port);
        }

        /// <summary>
        /// Creates a new connection to <paramref name="address"/> at <paramref name="port"/>.
        /// </summary>
        /// <param name="address">The address to connect to</param>
        /// <param name="port">The port to connect to</param>
        private void CreateConnection(IPAddress address, int port)
        {
            try
            {
                EndPoint ep = new IPEndPoint(address, port);
                Connection conn = new Connection(this, ep, nextConnectionID);
                idToConnection.Add(nextConnectionID, conn);
                epToConnection.Add(ep, conn);

                Log("Connected to " + ep);

                nextConnectionID = (nextConnectionID + 1) % int.MaxValue;
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
        /// Disconnects from a specific connection
        /// </summary>
        /// <param name="id">Connection id to remove</param>
        /// <param name="remove">Whether to remove the Connection object from the dictionaries</param>
        public void Disconnect(int id, bool remove = true)
        {
            if (!IsConnected)
            {
                if (Debug)
                    Log("Not connected to any endpoints!");

                return;
            }

            try
            {
                Send(id, Packet.DISCONNECT_PACKET);

                //remove Connection from dictionaries
                Connection conn = idToConnection[id];
                conn.ClearAllPackets();

                if (remove)
                {
                    idToConnection.Remove(id);
                    epToConnection.Remove(conn.EndPoint);

                    if (idToConnection.Count == 0)
                    {
                        receiveThread.Abort();
                        socket.Shutdown(SocketShutdown.Both);
                        socket.Close();
                    }
                }

                Log("Disconnected from " + conn.EndPoint);
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
        /// Disconnects from all connections
        /// </summary>
        public void DisconnectAll()
        {
            foreach (KeyValuePair<int, Connection> pair in idToConnection)
                Disconnect(pair.Key, false);

            idToConnection.Clear();
            epToConnection.Clear();
        }

        /// <summary>
        /// Sends <paramref name="text"/> encoded as UTF8.
        /// </summary>
        /// <param name="id">Connection id to send to</param>
        /// <param name="text">Message to send</param>
        public void Send(int id, string text)
        {
            Send(id, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Sends the <paramref name="buffer"/> to the connection ID specified.
        /// </summary>
        /// <param name="id">Connection id to send to</param>
        /// <param name="buffer">A byte[] of data to send. If the buffer plus header
        /// is bigger than 1400 bytes, it will be split across multiple calls to
        /// Socket.SendTo</param>
        /// <param name="header">Header to append to the buffer</param>
        /// <remarks>
        /// Note this function does not do anything if <c>IsConnected</c> is false or
        /// the specified id does not exist.
        /// </remarks>
        public void Send(int id, byte[] buffer = null, byte[] header = null)
        {
            if (!IsConnected)
            {
                if (Debug)
                    Log("Not connected to any endpoints!");

                return;
            }

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
        /// <param name="id">Connection id to send to</param>
        /// <param name="text">Text to send</param>
        public void SendReliable(int id, string text)
        {
            SendReliable(id, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Reliably sends <paramref name="packet"/>
        /// </summary>
        /// <param name="id">Connection id to send to</param>
        /// <param name="packet">Data to send</param>
        /// <remarks>
        /// Note this function does not do anything if <c>IsConnected</c> is false or
        /// the specified id does not exist.
        /// </remarks>
        public void SendReliable(int id, byte[] packet)
        {
            if (!IsConnected)
            {
                if (Debug)
                    Log("Not connected to any endpoints!");

                return;
            }

            if (!idToConnection.ContainsKey(id))
            {
                Log("ID doesn't exist in connections: " + id);
                return;
            }

            idToConnection[id].SendPacket(packet, MD5.ComputeHash(packet));
        }

        /// <summary>
        /// Receive thread function. Reads data from the socket and calls
        /// <c>ProcessData</c>.
        /// </summary>
        private void ReceiveThread()
        {
            while (!IsConnected) { /* Wait for the connection to finish being created */}

            byte[] buffer = null;
            byte[] truncated = null;
            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);

            while (IsConnected)
            {
                try
                {
                    buffer = new byte[Packet.MAX_SEND_SIZE];
                    int numBytes = socket.ReceiveFrom(buffer, ref ep);
                    truncated = new byte[numBytes];
                    Array.Copy(buffer, truncated, truncated.Length);

                    ProcessData(ep, truncated);
                }
                catch (SocketException)
                {
                    if (socket.Available == 0)
                        continue;
                }
                catch (Exception e)
                {
                    Log(e.Message);
                    if (e.InnerException != null)
                        Log(e.InnerException);
                    Log(e.StackTrace);

                    if (ExitOnReceiveException)
                        Environment.Exit(1);
                }
            }
        }

        /// <summary>
        /// Checks is connected to <paramref name="ep"/> and calls ProcessData. Otherwise
        /// do nothing.
        /// </summary>
        /// <param name="ep">Connection EndPoint to use for data processing</param>
        /// <param name="buffer">Data to process</param>
        protected virtual void ProcessData(EndPoint ep, byte[] buffer)
        {
            if (!epToConnection.ContainsKey(ep))
            {
                Log("Received data from an unknown remote: " + ep);
                return;
            }

            epToConnection[ep].ProcessData(buffer);
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
    }
}
