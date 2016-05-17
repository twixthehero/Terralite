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
        public bool UseMD5 { get; set; }
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
            UseMD5 = true;

            MD5 = MD5.Create();
            guaranteedPackets = new Dictionary<int, GuaranteedPacket>();
        }

        /// <summary>
        /// Sends <paramref name="text"/> reliably and encoded as UTF8
        /// </summary>
        /// <param name="text">Text to send</param>
        public void SendReliable(string text)
        {
            SendReliable(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Reliably sends <paramref name="packet"/>
        /// </summary>
        /// <param name="packet">Data to send</param>
        public void SendReliable(byte[] packet)
        {
            GuaranteedPacket gp = new GuaranteedPacket(this, nextID, packet);
            guaranteedPackets.Add(gp.PacketID, gp);

            nextID = (ushort)((nextID + 1) % ushort.MaxValue);

            Send(gp.ByteArray);
        }

        /// <summary>
        /// Called before <c>OnReceive</c> to do packet preprocessing.
        /// </summary>
        /// <param name="header">Header of packet</param>
        /// <param name="data">Packet data to do preprocessing with</param>
        /// <returns>Whether or not <c>OnReceive</c> needs to be called</returns>
        protected override bool OnPreReceive(byte[] header, byte[] data)
        {
            ushort type = BitConverter.ToUInt16(header, 0);

            //if non-reliable packet
            if (type == 1)
                return true;

            ushort packetID = BitConverter.ToUInt16(header, 2);

            if (guaranteedPackets.ContainsKey(packetID))
            {
                if (UseMD5)
                {
                    byte[] hash = new byte[32];
                    Array.Copy(data, hash, hash.Length);

                    if (guaranteedPackets[packetID].CheckMD5(hash))
                        ClearPacket(packetID);
                }
                else
                    ClearPacket(packetID);

                return true;
            }

            Log("Didn't send packet id " + packetID);
            return false;
        }

        /// <summary>
        /// Called to remove a packet from the list
        /// </summary>
        /// <param name="id">ID of the packet to remove</param>
        private void ClearPacket(int id)
        {
            guaranteedPackets[id].Dispose();
            guaranteedPackets.Remove(id);
        }

        /// <summary>
        /// Class to hold data about a guaranteed packet
        /// </summary>
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

            /// <summary>
            /// Checks a byte[] md5 sum against this packet's MD5 sum
            /// </summary>
            /// <param name="sum">MD5 hash sum</param>
            /// <returns>Whether the MD5s match</returns>
            public bool CheckMD5(byte[] sum)
            {
                if (sum.Length != MD5.Length)
                    return false;

                for (int i = 0; i < sum.Length; i++)
                    if (MD5[i] != sum[i])
                        return false;

                return true;
            }

            /// <summary>
            /// Called when this packet has been acknowledged.
            /// </summary>
            public void Dispose()
            {
                timer.Stop();
            }

            /// <summary>
            /// Called each time timer.Elapsed fires to retry sending
            /// </summary>
            /// <param name="sender">Object that fired the event</param>
            /// <param name="e">Event args</param>
            private void OnRetry(object sender, ElapsedEventArgs e)
            {
                reliableClient.Send(ByteArray, Header);
            }

            /// <summary>
            /// Creates a byte array containing the header information.
            /// </summary>
            /// <returns>Byte array with type 2 + packet id</returns>
            private byte[] CreateHeader()
            {
                byte[] header = new byte[4];

                byte[] type = BitConverter.GetBytes((ushort)2);
                Array.Copy(type, 0, header, 0, type.Length);

                byte[] pid = BitConverter.GetBytes(PacketID);
                Array.Copy(pid, 0, header, 2, pid.Length);

                return header;
            }

            /// <summary>
            /// Creates a byte array containing the MD5 and packet data.
            /// </summary>
            /// <returns>Byte array with MD5 + packet data</returns>
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
