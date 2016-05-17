using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Terralite
{
    /// <summary>
    /// Base client class using UDP
    /// </summary>
    public class Client
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
        /// Whether this <c>Client</c> is currently connected to a
        /// remote endpoint
        /// </summary>
        public bool IsConnected { get; private set; }

        public delegate void ReceiveEvent(byte[] data, int numBytes);
        public event ReceiveEvent Receive;

        protected const string DEFAULT_LOG = "log.txt";
        protected const int DEFAULT_PORT = 10346;
        private const int MAX_SIZE = 1400;
        private const int MAX_SEND_SIZE = 1450;
        private static byte[] HEADER_NON_RELIABLE = new byte[4] { 1, 0, 0, 0 };

        private Socket socket;
        private EndPoint endPoint;
        private Thread receiveThread;

        /// <summary>
        /// Creates a <c>Client</c> object with the default
        /// log file (log.txt).
        /// </summary>
        public Client() : this(DEFAULT_LOG) { }

        /// <summary>
        /// Creates a <c>Client</c> object using <paramref name="logfile"/>
        /// as the log file.
        /// </summary>
        /// <param name="logfile">The logfile to use. Use <c>null</c> for
        /// no logging.</param>
        /// <param name="port">The port to use</param>
        public Client(string logfile, int port = DEFAULT_PORT)
        {
            Debug = false;
            ExitOnReceiveException = false;
            IsConnected = false;

            if (logfile != null)
                CreateLog(logfile);

            CreateSocket(port);
        }

        /// <summary>
        /// Gets the local EndPoint this socket is bound to
        /// </summary>
        public EndPoint LocalEndPoint
        {
            get { return socket.LocalEndPoint; }
        }

        /// <summary>
        /// Gets the remote EndPoint this socket is connected to
        /// </summary>
        public EndPoint RemoteEndPoint
        {
            get { return endPoint; }
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
        /// Sets the remote EndPoint to send data to. Also
        /// starts the receiving thread.
        /// </summary>
        /// <param name="ip">IP Address to send to</param>
        /// <param name="port">Port to send to</param>
        public void Connect(string ip, int port)
        {
            if (IsConnected)
            {
                Log("Already connected to " + endPoint);
                return;
            }

            IPAddress address;
            bool success = IPAddress.TryParse(ip, out address);

            if (!success)
            {
                throw new TerraliteException("Invalid IP address: " + ip);
            }

            if (port < 0 || port >= 65536)
            {
                throw new TerraliteException("Invalid port: " + port + ". Must be between (0 - 65535)");
            }

            try
            {
                endPoint = new IPEndPoint(address, port);
                IsConnected = true;

                receiveThread = new Thread(new ThreadStart(ReceiveThread));
                receiveThread.Start();
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
        /// Aborts the receive thread, sends a disconnect packet,
        /// and closes the socket.
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected)
            {
                //Log("Not connected to an endpoint.");
                return;
            }

            try
            {
                receiveThread.Abort();
                socket.SendTo(Packet.DISCONNECT, endPoint);
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();

                Log("Client disconnected");
                IsConnected = false;
            }
            catch (SocketException e)
            {
                Log(e.Message);
                if (e.InnerException != null)
                    Log(e.InnerException);
                Log(e.StackTrace);
            }
        }
        
        /// <summary>
        /// Encodes the <paramref name="text"/> parameter using UTF8
        /// and calls Send(byte[])
        /// </summary>
        /// <param name="text">A string of text to send</param>
        /// <remarks>
        /// Note this function does not do anything if IsConnected is false.
        /// </remarks>
        public void Send(string text)
        {
            Send(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Sends the <paramref name="buffer"/> to the <c>RemoteEndPoint</c>
        /// </summary>
        /// <param name="buffer">A byte[] of data to send. Data bigger
        /// than 1400 bytes will be split across multiple calls to
        /// Socket.SendTo</param>
        /// <remarks>
        /// Note this function does not do anything if <c>IsConnected</c> is false.
        /// </remarks>
        public void Send(byte[] buffer, byte[] header = null)
        {
            if (!IsConnected)
            {
                Log("Not connected to an endpoint.");
                return;
            }

            try
            {
                byte[] head = header ?? HEADER_NON_RELIABLE;
                byte[] data = Combine(head, buffer);

                if (data.Length <= MAX_SEND_SIZE)
                {
                    if (Debug)
                        Log("Sending data size " + data.Length);

                    socket.SendTo(data, endPoint);
                }
                else
                {
                    byte[][] packets = SplitBuffer(head, buffer);

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
            catch (SocketException e)
            {
                Log(e.Message);
                if (e.InnerException != null)
                    Log(e.InnerException);
                Log(e.StackTrace);
            }
        }

        /// <summary>
        /// Takes two byte arrays and returns their combination
        /// </summary>
        /// <param name="buffer1">The first byte array</param>
        /// <param name="buffer2">The second byte array</param>
        /// <returns><paramref name="buffer1"/> + <paramref name="buffer2"/></returns>
        private byte[] Combine(byte[] buffer1, byte[] buffer2)
        {
            byte[] result = new byte[buffer1.Length + buffer2.Length];

            Array.Copy(buffer1, 0, result, 0, buffer1.Length);
            Array.Copy(buffer2, 0, result, buffer1.Length, buffer2.Length);

            return result;
        }

        /// <summary>
        /// Splits <paramref name="buffer"/> into two byte[] at <paramref name="index"/>.
        /// </summary>
        /// <param name="buffer">Byte[] to be split</param>
        /// <param name="index">Index to split at</param>
        /// <returns>Two byte arrays</returns>
        private byte[][] Split(byte[] buffer, int index = 4)
        {
            byte[][] result = new byte[2][];

            result[0] = new byte[index];
            result[1] = new byte[buffer.Length - (index + 1)];

            Array.Copy(buffer, result[0], buffer.Length);
            Array.Copy(buffer, index, result[1], 0, result[1].Length);

            return result;
        }

        /// <summary>
        /// Takes <paramref name="buffer"/> and returns an array of byte[]
        /// that each hold up to MAX_SIZE (1400 bytes) of data.
        /// </summary>
        /// <param name="header">Header to be appended</param>
        /// <param name="buffer">Data to be split</param>
        /// <returns></returns>
        private byte[][] SplitBuffer(byte[] header, byte[] buffer)
        {
            byte[][] result = new byte[buffer.Length / MAX_SIZE][];
            int size;

            byte[] tmp;

            for (uint i = 0; i < result.GetLength(0); i++)
            {
                size = i == result.GetLength(0) - 1 ? buffer.Length % MAX_SIZE : MAX_SIZE;
                tmp = new byte[4 + size];
                Array.Copy(buffer, i * 1404, tmp, 0, size);

                result[i] = Combine(header, tmp);
            }

            return result;
        }

        /// <summary>
        /// Receive thread function. Reads data from the socket and calls
        /// <c>ProcessData</c>.
        /// </summary>
        private void ReceiveThread()
        {
            byte[] buffer = new byte[MAX_SIZE];

            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);

            while (IsConnected)
            {
                try
                {
                    int numBytes = socket.ReceiveFrom(buffer, ref ep);

                    ProcessData(buffer, ep);
                }
                catch (SocketException)
                {
                    if (socket.Available == 0)
                        continue;
                    else
                    {
                        byte[] remainder = new byte[socket.Available];
                        int extraBytes = socket.ReceiveFrom(remainder, ref ep);

                        ProcessData(buffer, ep, remainder);
                    }
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
        /// Called when the receive thread gets data to be processed.
        /// </summary>
        /// <param name="buffer">Main data being processed</param>
        /// <param name="ep">EndPoint the data came from</param>
        /// <param name="remainder">Data that did not fit in the first ReceiveFrom call</param>
        /// <remarks>
        /// If <paramref name="remainder"/> is not <c>null</c>, it is combined with data
        /// into one array.
        /// </remarks>
        protected virtual void ProcessData(byte[] buffer, EndPoint ep, byte[] remainder = null)
        {
            if (!ep.Equals(endPoint))
            {
                Log("Received data from unknown remote: " + ep);
                return;
            }

            if (remainder == null)
            {
                //pieces[0] = 4 byte header
                //pieces[1] = data
                byte[][] pieces = Split(buffer);

                //Process the data
                if (OnPreReceive(pieces[0]))
                    OnReceive(pieces[1], pieces[1].Length);
            }
            else
            {
                //Create one buffer with the entire data
                byte[] data = new byte[buffer.Length + remainder.Length];
                Array.Copy(buffer, data, buffer.Length);
                Array.Copy(remainder, 0, data, buffer.Length, remainder.Length);

                ProcessData(data, ep);
            }
        }

        /// <summary>
        /// Called before <c>OnReceive</c> to do packet preprocessing.
        /// </summary>
        /// <param name="data">Packet to do preprocessing with</param>
        /// <returns>Whether or not <c>OnReceive</c> needs to be called</returns>
        protected virtual bool OnPreReceive(byte[] data)
        {
            return true;
        }

        /// <summary>
        /// Called when data has been received. Invokes all functions
        /// stored in the Receive event.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="len"></param>
        /// <remarks>
        /// Note this function does not do anything if <c>Receive</c> is <c>null</c>.
        /// </remarks>
        protected void OnReceive(byte[] packet, int len)
        {
            if (Receive != null && len > 0)
                Receive(packet, len);
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
