﻿using System;
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

        private Socket socket;
        private EndPoint endPoint;
        private Thread receiveThread;

        private byte[][] multiPacket = null;

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

                Log("Connected to " + endPoint);
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
                Send(Packet.DISCONNECT_PACKET);
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
        /// Note this function does not do anything if <c>IsConnected</c> is false.
        /// </remarks>
        public void Send(string text)
        {
            Send(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Sends the <paramref name="buffer"/> to the <c>RemoteEndPoint</c>
        /// </summary>
        /// <param name="buffer">A byte[] of data to send. If the buffer plus header
        /// is bigger than 1400 bytes, it will be split across multiple calls to
        /// Socket.SendTo</param>
        /// <param name="header">Header to append to the buffer</param>
        /// <remarks>
        /// Note this function does not do anything if <c>IsConnected</c> is false.
        /// </remarks>
        public void Send(byte[] buffer = null, byte[] header = null)
        {
            if (!IsConnected)
            {
                Log("Not connected to an endpoint.");
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
        /// Receive thread function. Reads data from the socket and calls
        /// <c>ProcessData</c>.
        /// </summary>
        private void ReceiveThread()
        {
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

                    ProcessData(truncated, ep);
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
        /// Called when the receive thread gets data to be processed.
        /// </summary>
        /// <param name="buffer">Main data being processed</param>
        /// <param name="ep">EndPoint the data came from</param>
        protected virtual void ProcessData(byte[] buffer, EndPoint ep)
        {
            if (!ep.Equals(endPoint))
            {
                Log("Received data from unknown remote: " + ep);
                return;
            }
            
            //pieces[0] = header
            //pieces[1] = data
            byte[][] pieces = buffer[0] != Packet.MULTI ? Split(buffer) : ProcessMulti(buffer);

            //Process the data
            if (OnPreReceive(pieces[0], pieces[1]))
                OnReceive(pieces[1], pieces[1].Length);
        }

        /// <summary>
        /// Splits <paramref name="buffer"/> into two byte[] at <paramref name="index"/>.
        /// If index is -1, it calculates where to split using the packet type.
        /// </summary>
        /// <param name="buffer">Byte[] to be split</param>
        /// <param name="index">Index to split at</param>
        /// <returns>Two byte arrays</returns>
        private byte[][] Split(byte[] buffer, int index = -1)
        {
            byte[][] result = new byte[2][];

            //calculate index from header
            if (index == -1)
            {
                switch (buffer[0])
                {
                    case Packet.NON_RELIABLE:
                        index = 1;
                        break;
                    case Packet.RELIABLE:
                    case Packet.ACK:
                        index = 2;
                        break;
                }
            }

            result[0] = new byte[index];
            result[1] = new byte[buffer.Length - index];

            Array.Copy(buffer, result[0], result[0].Length);
            Array.Copy(buffer, index, result[1], 0, result[1].Length);

            return result;
        }

        /// <summary>
        /// Handles storing multi-part packets and reconstructing when all
        /// have arrived.
        /// </summary>
        /// <param name="buffer">One part of the whole packet</param>
        /// <returns>null if all data hasn't been received, otherwise
        /// Two byte arrays</returns>
        private byte[][] ProcessMulti(byte[] buffer)
        {
            //Create a byte array of arrays for storing the parts of this multi packet
            if (multiPacket == null)
                multiPacket = new byte[buffer[1]][];

            //Initialize this slot in the array and copy
            multiPacket[buffer[2]] = new byte[buffer.Length - 3];
            Array.Copy(buffer, 3, multiPacket[buffer[2]], 0, multiPacket[buffer[2]].Length);

            //If we don't have all the packets, return null
            for (int i = 0; i < multiPacket.Length; i++)
                if (multiPacket[i] == null)
                    return null;

            MemoryStream ms = new MemoryStream();
            for (int i = 0; i < multiPacket.Length; i++)
                ms.Write(multiPacket[i], 0, multiPacket[i].Length);

            byte[] whole = new byte[ms.Length];
            ms.Read(whole, 0, whole.Length);

            multiPacket = null;

            return Split(whole);
        }

        /// <summary>
        /// Called before <c>OnReceive</c> to do packet preprocessing.
        /// </summary>
        /// <param name="header">Header of packet</param>
        /// <param name="data">Packet data to do preprocessing with</param>
        /// <returns>Whether or not <c>OnReceive</c> needs to be called</returns>
        protected virtual bool OnPreReceive(byte[] header, byte[] data)
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
