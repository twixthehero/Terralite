using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

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

        public bool IsRunning { get; protected set; }

        /// <summary>
        /// Whether to exit when a receive exception is thrown
        /// </summary>
        public bool ExitOnReceiveException { get; set; }

        protected const string DEFAULT_LOG = "log.txt";
        protected const int DEFAULT_PORT = 10346;

        private Socket socket;

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

            if (logfile != null)
                CreateLog(logfile);

            CreateSocket(port);

            //TODO

            IsRunning = true;
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
