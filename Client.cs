using System;

namespace UDP
{
    public class Client
    {
        private string _ip;
        private int _port;

        public Client()
        {
            IsConnected = false;
        }

        public bool IsConnected { get; protected set; }

        public void Connect(string ip, int port)
        {

        }

        public void Disconnect()
        {

        }

        public void Send(byte[] buffer)
        {

        }
    }
}
