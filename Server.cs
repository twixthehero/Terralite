using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Terralite
{
    /// <summary>
    /// Base server class using UDP
    /// </summary>
    public class Server
    {
        /// <summary>
        /// Whether to log the extra debug information
        /// </summary>
        public bool Debug { get; set; }

        /// <summary>
        /// Whether the server is currently running
        /// </summary>
        public bool IsRunning { get; protected set; }

        /// <summary>
        /// Whether to exit when a receive exception is thrown
        /// </summary>
        public bool ExitOnReceiveException { get; set; }

        /// <summary>
        /// The port the server is/will be bound to
        /// </summary>
        private int Port { get; set; }

        public delegate void ReceiveEvent(EndPoint remoteEP, byte[] data, int numBytes);
        public event ReceiveEvent Receive;

        protected const string DEFAULT_LOG = "log.txt";
        protected const int DEFAULT_PORT = 10346;

        private Socket socket;

        private Thread receiveThread;
        private Thread packetThread;

        private SafeList<SessionPacket> packets;

        /// <summary>
        /// Creates a <c>Server</c> object with the default
        /// log file (log.txt).
        /// </summary>
        public Server() : this(DEFAULT_LOG) { }

        /// <summary>
        /// Creates a <c>Server</c> object using <paramref name="logfile"/>
        /// as the log file.
        /// </summary>
        /// <param name="logfile">The logfile to use. Use <c>null</c> for
        /// no logging.</param>
        /// <param name="port">The port to use</param>
        public Server(string logfile, int port = DEFAULT_PORT)
        {
            Debug = false;
            Port = port;

            packets = new SafeList<SessionPacket>();

            if (logfile != null)
                CreateLog(logfile);

            Start();
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

        /// <summary>
        /// Called to create the server socket and threads.
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            IsRunning = true;

            CreateSocket(Port);

            receiveThread = new Thread(new ThreadStart(ReceiveThread));
            packetThread = new Thread(new ThreadStart(PacketThread));

            receiveThread.Start();
            packetThread.Start();
        }

        /// <summary>
        /// Called to stop the running threads and close the socket.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;

            receiveThread.Abort();
            packetThread.Abort();

            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        /// <summary>
        /// Send <paramref name="buffer"/> to <paramref name="endPoint"/>
        /// </summary>
        /// <param name="endPoint">EndPoint to send data to</param>
        /// <param name="buffer">Data to send</param>
        /// <param name="header">Header to append to the buffer</param>
        /// <remarks>
        /// Note this function does not do anything if <c>IsRunning</c> is false.
        /// </remarks>
        public void Send(EndPoint endPoint, byte[] buffer, byte[] header = null)
        {
            if (!IsRunning)
            {
                Log("Server is not started.");
                return;
            }

            try
            {
                byte[] head = header ?? Packet.HEADER_NON_RELIABLE;
                byte[] data = Utils.Combine(head, buffer);

                if (data.Length <= Packet.MAX_SEND_SIZE)
                {
                    if (Debug)
                        Log("Sending data size " + data.Length);

                    socket.SendTo(data, endPoint);
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

                        socket.SendTo(packet, endPoint);
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
        /// Convenience function that calls Send(EndPoint, byte[]) on
        /// each EndPoind in <paramref name="endPoints"/>.
        /// </summary>
        /// <param name="endPoints">EndPoints to send data to</param>
        /// <param name="buffer">Data to send</param>
        /// <param name="header">Header to append to the buffer</param>
        public void Send(EndPoint[] endPoints, byte[] buffer, byte[] header = null)
        {
            foreach (EndPoint endPoint in endPoints)
                Send(endPoint, buffer, header);
        }

        /// <summary>
        /// Convenience function that calls Send(EndPoint, byte[]) on
        /// each EndPoind in <paramref name="endPoints"/>.
        /// </summary>
        /// <param name="endPoints">EndPoints to send data to</param>
        /// <param name="buffer">Data to send</param>
        /// <param name="header">Header to append to the buffer</param>
        public void Send(List<EndPoint> endPoints, byte[] buffer, byte[] header = null)
        {
            foreach (EndPoint endPoint in endPoints)
                Send(endPoint, buffer, header);
        }
        
        /// <summary>
        /// Receive thread function. Reads data from the socket and creates
        /// <c>SessionPackets</c>.
        /// </summary>
        private void ReceiveThread()
        {
            EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = null;
            byte[] truncate = null;

            while (IsRunning)
            {
                try
                {
                    buffer = new byte[Packet.MAX_SEND_SIZE];
                    int len = socket.ReceiveFrom(buffer, ref clientEndPoint);
                    truncate = new byte[len];
                    Array.Copy(buffer, truncate, truncate.Length);

                    packets.Add(new SessionPacket(clientEndPoint, truncate, len));
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
                }
            }
        }

        /// <summary>
        /// Packet thread function. Handles invoking the Receive event.
        /// </summary>
        private void PacketThread()
        {
            while (IsRunning)
            {
                if (packets.Count == 0) continue;

                SessionPacket sp = packets[0];
                packets.RemoveAt(0);

                if (OnPreReceive(sp))
                    OnReceive(sp.RemoteEndPoint, sp.Data, sp.Length);
            }
        }

        /// <summary>
        /// Called before <c>OnReceive</c> to do packet preprocessing.
        /// </summary>
        /// <param name="packet">The packet received</param>
        /// <returns>Whether or not <c>OnReceive</c> needs to be called</returns>
        protected virtual bool OnPreReceive(SessionPacket packet)
        {
            return true;
        }

        /// <summary>
        /// Called when data has been received. Invokes all functions
        /// stored in the Receive event.
        /// </summary>
        /// <param name="endPoint">Where this packet came from</param>
        /// <param name="packet">Packet data</param>
        /// <param name="len">Length of the data</param>
        /// <remarks>
        /// Note this function does not do anything if <c>Receive</c> is <c>null</c>.
        /// </remarks>
        protected void OnReceive(EndPoint endPoint, byte[] packet, int len)
        {
            if (Receive != null && len > 0)
                Receive(endPoint, packet, len);
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
        /// Class to hold data about a packet from a specific end point.
        /// </summary>
        protected class SessionPacket
        {
            public EndPoint RemoteEndPoint { get; private set; }
            public byte[] Header { get; private set; }
            public byte[] Data { get; private set; }
            public int Length { get; private set; }
            public bool IsDisconnect { get; private set; }

            public SessionPacket(EndPoint remoteEP, byte[] data, int len)
            {
                RemoteEndPoint = remoteEP;
                IsDisconnect = false;

                switch (data[0])
                {
                    case Packet.NON_RELIABLE:
                        Length = len - 1;
                        Header = new byte[] { data[0] };
                        break;
                    case Packet.RELIABLE:
                    case Packet.ACK:
                        Length = len - 2;
                        Header = new byte[] { data[0], data[1] };
                        Data = new byte[Length];
                        Array.Copy(data, Data, Length);
                        break;
                    case Packet.MULTI:
                        break;
                    case Packet.DISCONNECT:
                        IsDisconnect = true;
                        break;
                }
            }
        }
    }
}
