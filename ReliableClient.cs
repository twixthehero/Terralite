using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using System.Timers;

namespace Terralite
{
    /// <summary>
    /// ReliableClient extends the base Client functionality
    /// by adding reliable sending. Packets that are transmitted incorrectly
    /// will be resent. Packets that arrive out of order will be fixed.
    /// Packets can be send with a 100% guarantee they will arrive.
    /// </summary>
    public class ReliableClient : Client
    {
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

        private Dictionary<byte, GuaranteedPacket> guaranteedPackets;
        private byte nextSendID = 1;

        private OrderedDictionary orderedPackets;
        private byte nextExpectedID;

        /// <summary>
        /// Creates a <c>ReliableClient</c> object with the default
        /// log file (log.txt).
        /// </summary>
        public ReliableClient() : this(DEFAULT_LOG) { }

        /// <summary>
        /// Creates a <c>ReliableClient</c> object using <paramref name="logfile"/>
        /// as the log file.
        /// </summary>
        /// <param name="logfile">The logfile to use. Use <c>null</c> for
        /// no logging.</param>
        /// <param name="port">The port to use</param>
        public ReliableClient(string logfile, int port = DEFAULT_PORT) : base(logfile, port)
        {
            MaxRetries = 10;
            RetryInterval = 0.3f;
            UseMD5 = true;
            UseOrdering = true;

            MD5 = MD5.Create();
            guaranteedPackets = new Dictionary<byte, GuaranteedPacket>();
            orderedPackets = new OrderedDictionary();
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
            GuaranteedPacket gp = new GuaranteedPacket(this, nextSendID, packet, MaxRetries);
            guaranteedPackets.Add(gp.PacketID, gp);

            nextSendID = (byte)((nextSendID + 1) % byte.MaxValue);

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
            byte type = header[0];

            //if non-reliable packet
            if (type == 1)
                return true;
            //if unknown
            else if (type != Packet.RELIABLE && type != Packet.ACK)
            {
                Log("Got unknown packet type " + type);
                return false;
            }

            byte packetID = header[1];

            if (guaranteedPackets.ContainsKey(packetID))
            {
                switch (type)
                {
                    case Packet.RELIABLE:
                        if (UseMD5)
                        {
                            byte[] hash = new byte[32];
                            Array.Copy(data, hash, hash.Length);

                            if (guaranteedPackets[packetID].CheckMD5(hash))
                                SendAck(packetID);
                        }
                        else
                            SendAck(packetID);

                        if (!UseOrdering)
                            return true;
                        else
                        {
                            //if already timeout waiting for this packet
                            if (packetID < nextExpectedID)
                                return false;
                            //if got next expected id
                            else if (packetID == nextExpectedID)
                            {
                                nextExpectedID = (byte)((nextExpectedID + 1) % byte.MaxValue);

                                //while we have the next sequential packet, call OnReceive for it
                                while (orderedPackets.Contains(nextExpectedID))
                                {
                                    OrderedPacket op = (OrderedPacket)orderedPackets[(object)nextExpectedID];
                                    orderedPackets.Remove(nextExpectedID);
                                    nextExpectedID = (byte)((nextExpectedID + 1) % byte.MaxValue);

                                    OnReceive(op.Data, op.Data.Length);
                                }

                                return true;
                            }

                            orderedPackets.Add(packetID, new OrderedPacket(this, packetID, data, Packet.ORDER_TIMEOUT));
                            return false;
                        }
                    case Packet.ACK:
                        ClearPacket(packetID);
                        return true;
                }
            }

            Log("Didn't send packet id " + packetID);
            return false;
        }

        /// <summary>
        /// Called by an OrderedPacket when its timeout has been reached.
        /// </summary>
        /// <param name="packetid">ID of packet that timed out</param>
        protected void OnPacketTimeout(ushort packetid)
        {
            OrderedPacket op = (OrderedPacket)orderedPackets[(object)packetid];
            orderedPackets.Remove(packetid);
            nextExpectedID = (byte)((packetid + 1) % byte.MaxValue);

            OnReceive(op.Data, op.Data.Length);
        }

        /// <summary>
        /// Called to remove a packet from the list
        /// </summary>
        /// <param name="id">ID of the packet to remove</param>
        protected void ClearPacket(byte id)
        {
            guaranteedPackets[id].Dispose();
            guaranteedPackets.Remove(id);
        }

        /// <summary>
        /// Sends an acknowledgement packet for <paramref name="packetid"/>.
        /// </summary>
        /// <param name="packetid">The packet id to acknowledge</param>
        private void SendAck(byte packetid)
        {
            byte[] ack = new byte[2];
            ack[0] = 3;
            ack[1] = packetid;

            Send(ack);
        }

        /// <summary>
        /// Class to hold data about a guaranteed packet
        /// </summary>
        private class GuaranteedPacket
        {
            public byte PacketID { get; private set; }
            public byte[] MD5 { get; private set; }
            public byte[] Header { get; private set; }
            public byte[] ByteArray { get; private set; }

            private ReliableClient reliableClient;
            private byte[] data;
            private Timer timer;
            private int maxTries;

            private int tries = 0;

            public GuaranteedPacket(ReliableClient rc, byte packetID, byte[] packet, int retries)
            {
                PacketID = packetID;
                reliableClient = rc;
                data = packet;
                maxTries = retries;

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
            /// Called each time timer. Elapsed fires to retry sending
            /// </summary>
            /// <param name="sender">Object that fired the event</param>
            /// <param name="e">Event args</param>
            private void OnRetry(object sender, ElapsedEventArgs e)
            {
                tries++;
                reliableClient.Send(ByteArray, Header);

                if (tries > maxTries)
                    reliableClient.ClearPacket(PacketID);
            }

            /// <summary>
            /// Creates a byte array containing the header information.
            /// </summary>
            /// <returns>Byte array with type 2 + packet id</returns>
            private byte[] CreateHeader()
            {
                byte[] header = new byte[2];
                header[0] = 2;
                header[1] = PacketID;

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

        /// <summary>
        /// Class to hold data for handling an ordered packet
        /// </summary>
        private class OrderedPacket
        {
            public byte PacketID { get; set; }
            public byte[] Data { get; set; }

            private ReliableClient reliableClient;
            private Timer timer;

            public OrderedPacket(ReliableClient rc, byte packetID, byte[] packet, float timeout)
            {
                reliableClient = rc;
                PacketID = packetID;
                Data = packet;

                timer = new Timer(timeout);
                timer.Elapsed += (sender, args) =>
                {
                    rc.OnPacketTimeout(PacketID);
                };
                timer.Start();
            }
        }
    }
}
