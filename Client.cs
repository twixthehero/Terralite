using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Terralite
{
    public class Client
    {
        public bool IsConnected { get; protected set; }

        private const string DEFAULT_LOG = "log.txt";

        private Socket socket;
        private EndPoint endPoint;
        private Thread receiveThread;

        public Client() : this(DEFAULT_LOG) { }

        public Client(string logfile)
        {
            IsConnected = false;

            if (logfile != null)
                CreateLog(logfile);

            CreateSocket();
        }

        private void CreateLog(string logfile)
        {
            Console.SetOut(new StreamWriter(File.OpenWrite(logfile)));
        }

        private void CreateSocket()
        {
            Console.Write("Creating socket...");
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Console.WriteLine("OK");

            socket.Blocking = false;

            Console.WriteLine("Binding socket...");
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            Console.WriteLine("Socket bound to " + socket.LocalEndPoint);
        }

        public void Connect(string ip, int port)
        {
            if (IsConnected) return;

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
                Console.WriteLine(e.Message);
                if (e.InnerException != null)
                    Console.WriteLine(e.InnerException);
                Console.WriteLine(e.StackTrace);
            }
        }

        public void Disconnect()
        {
            if (!IsConnected) return;


        }

        public void Send(string text)
        {
            Send(Encoding.UTF8.GetBytes(text));
        }

        public void Send(byte[] buffer)
        {

        }

        protected virtual void ReceiveThread()
        {

        }
    }
}
