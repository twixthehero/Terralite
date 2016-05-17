using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Timers;

namespace Terralite
{
    public class ReliableClient : Client
    {
        public int MaxRetries { get; set; }
        public MD5 MD5 { get; private set; }
        /// <summary>
        /// Interval to retry sending in millisenconds
        /// </summary>
        public float RetryInterval { get; set; }

        private Dictionary<int, GuaranteedPacket> guaranteedPackets;
        private ushort nextID = 1;

        public ReliableClient() : this(DEFAULT_LOG) { }
        public ReliableClient(string logfile, int port = DEFAULT_PORT) : base(logfile, port)
        {
            MaxRetries = 10;
            RetryInterval = 0.3f;

            MD5 = MD5.Create();
            guaranteedPackets = new Dictionary<int, GuaranteedPacket>();
        }

        public void SendReliable(string text)
        {
            SendReliable(Encoding.UTF8.GetBytes(text));
        }

        public void SendReliable(byte[] packet)
        {
            GuaranteedPacket gp = new GuaranteedPacket(this, nextID, packet);
            guaranteedPackets.Add(gp.PacketID, gp);

            nextID = (ushort)((nextID + 1) % ushort.MaxValue);

            Send(gp.ByteArray);
        }

        protected override bool OnPreReceive(byte[] header)
        {
            ushort type = BitConverter.ToUInt16(header, 0);

            //if non-reliable packet
            if (type == 1)
                return true;

            ushort packetID = BitConverter.ToUInt16(header, 2);

            if (guaranteedPackets.ContainsKey(packetID))
            {
                guaranteedPackets[packetID].Dispose();
                guaranteedPackets.Remove(packetID);

                return true;
            }

            Log("Didn't send packet id " + packetID);
            return false;
        }

        private class GuaranteedPacket
        {
            public ushort PacketID { get; private set; }
            public byte[] MD5 { get; private set; }
            public byte[] Header { get; private set; }
            public byte[] ByteArray { get; private set; }

            private ReliableClient reliableClient;
            private byte[] data;
            private Timer timer;

            public GuaranteedPacket(ReliableClient rc, ushort packetID, byte[] packet)
            {
                PacketID = packetID;
                reliableClient = rc;
                data = packet;

                MD5 = reliableClient.MD5.ComputeHash(data);
                Header = CreateHeader();
                ByteArray = ToByteArray();

                timer = new Timer(rc.RetryInterval);
                timer.AutoReset = true;
                timer.Elapsed += OnRetry;
                timer.Start();
            }

            public void Dispose()
            {
                timer.Stop();
            }

            private void OnRetry(object sender, ElapsedEventArgs e)
            {
                reliableClient.Send(ByteArray, Header);
            }

            private byte[] CreateHeader()
            {
                byte[] header = new byte[4];

                byte[] type = BitConverter.GetBytes((ushort)2);
                Array.Copy(type, 0, header, 0, type.Length);

                byte[] pid = BitConverter.GetBytes(PacketID);
                Array.Copy(pid, 0, header, 2, pid.Length);

                return header;
            }

            private byte[] ToByteArray()
            {
                byte[] packet = new byte[MD5.Length + data.Length];

                int index = 0;

                Array.Copy(MD5, 0, packet, index, MD5.Length);
                index += MD5.Length;

                Array.Copy(data, 0, packet, index, data.Length);
                index += data.Length;

                return packet;
            }
        }
    }
}
