using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Timers;

namespace Terralite
{
    /// <summary>
    /// ReliableServer extends the base Server functionality
    /// by adding reliable sending. Packets that are transmitted incorrectly
    /// will be resent. Packets that arrive out of order will be fixed.
    /// Packets can be send with a 100% guarantee they will arrive.
    /// </summary>
    public class ReliableServer : Server
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

        private Dictionary<EndPoint, Dictionary<byte, GuaranteedPacket>> guaranteedPackets;
        private Dictionary<EndPoint, byte> nextSendID;

        private Dictionary<EndPoint, OrderedDictionary> orderedPackets;
        private Dictionary<EndPoint, byte> nextExpectedID;

        /// <summary>
        /// Creates a <c>ReliableServer</c> object with the default
        /// log file (log.txt).
        /// </summary>
        public ReliableServer() : this(DEFAULT_LOG) { }

        /// <summary>
        /// Creates a <c>ReliableServer</c> object using <paramref name="logfile"/>
        /// as the log file.
        /// </summary>
        /// <param name="logfile">The logfile to use. Use <c>null</c> for
        /// no logging.</param>
        /// <param name="port">The port to use</param>
        public ReliableServer(string logfile, int port = DEFAULT_PORT) : base(logfile, port)
        {
            MaxRetries = 10;
            RetryInterval = 0.3f;
            UseMD5 = true;
            UseOrdering = true;

            MD5 = MD5.Create();
            guaranteedPackets = new Dictionary<EndPoint, Dictionary<byte, GuaranteedPacket>>();
            nextSendID = new Dictionary<EndPoint, byte>();
            orderedPackets = new Dictionary<EndPoint, OrderedDictionary>();
            nextExpectedID = new Dictionary<EndPoint, byte>();
        }

        /// <summary>
        /// Reliably sends <paramref name="packet"/>
        /// </summary>
        /// <param name="dest">Where to send the data</param>
        /// <param name="packet">Data to send</param>
        public void SendReliable(EndPoint dest, byte[] packet)
        {
            if (!guaranteedPackets.ContainsKey(dest))
                guaranteedPackets.Add(dest, new Dictionary<byte, GuaranteedPacket>());

            GuaranteedPacket gp = new GuaranteedPacket(this, dest, GetNextSendID(dest), packet, MaxRetries);
            guaranteedPackets[dest].Add(gp.PacketID, gp);

            Send(dest, gp.ByteArray);
        }

        /// <summary>
        /// Gets the next guaranteed packet id for <paramref name="dest"/>.
        /// </summary>
        /// <param name="dest">The destination EndPoint</param>
        /// <returns></returns>
        private byte GetNextSendID(EndPoint dest)
        {
            if (!nextSendID.ContainsKey(dest))
                nextSendID.Add(dest, 0);

            nextSendID[dest] = (byte)((nextSendID[dest] + 1) % byte.MaxValue);

            return nextSendID[dest];
        }

        /// <summary>
        /// Called before <c>OnReceive</c> to do packet preprocessing.
        /// </summary>
        /// <param name="source">Where the packet came from</param>
        /// <param name="header">Header of packet</param>
        /// <param name="data">Packet data to do preprocessing with</param>
        /// <returns>Whether or not <c>OnReceive</c> needs to be called</returns>
        protected override bool OnPreReceive(EndPoint source, byte[] header, byte[] data)
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

            if (guaranteedPackets[source].ContainsKey(packetID))
            {
                switch (type)
                {
                    case Packet.RELIABLE:
                        if (UseMD5)
                        {
                            byte[] hash = new byte[32];
                            Array.Copy(data, hash, hash.Length);

                            if (guaranteedPackets[source][packetID].CheckMD5(hash))
                                SendAck(source, packetID);
                        }
                        else
                            SendAck(source, packetID);

                        if (!UseOrdering)
                            return true;
                        else
                        {
                            //if already timeout waiting for this packet
                            if (packetID < nextExpectedID[source])
                                return false;
                            //if got next expected id
                            else if (packetID == nextExpectedID[source])
                            {
                                nextExpectedID[source] = (byte)((nextExpectedID[source] + 1) % byte.MaxValue);

                                //while we have the next sequential packet, call OnReceive for it
                                while (orderedPackets[source].Contains(nextExpectedID))
                                {
                                    OrderedPacket op = (OrderedPacket)orderedPackets[source][(object)nextExpectedID[source]];
                                    orderedPackets[source].Remove(nextExpectedID[source]);
                                    nextExpectedID[source] = (byte)((nextExpectedID[source] + 1) % byte.MaxValue);

                                    OnReceive(op.Source, op.Data, op.Data.Length);
                                }

                                return true;
                            }

                            //TODO - remove ordereddictionaries when clients disconnect
                            if (!orderedPackets.ContainsKey(source))
                                orderedPackets.Add(source, new OrderedDictionary());

                            orderedPackets[source].Add(packetID, new OrderedPacket(this, source, packetID, data, Packet.ORDER_TIMEOUT));
                            return false;
                        }
                    case Packet.ACK:
                        ClearPacket(source, packetID);
                        return true;
                }
            }

            Log("Didn't send packet id " + packetID);
            return false;
        }

        /// <summary>
        /// Called by an OrderedPacket when its timeout has been reached.
        /// </summary>
        /// <param name="source">Where this packet came from</param>
        /// <param name="packetid">ID of packet that timed out</param>
        protected void OnPacketTimeout(EndPoint source, ushort packetid)
        {
            OrderedPacket op = (OrderedPacket)orderedPackets[source][(object)packetid];
            orderedPackets[source].Remove(packetid);
            nextExpectedID[source] = (byte)((packetid + 1) % byte.MaxValue);

            OnReceive(op.Source, op.Data, op.Data.Length);
        }

        /// <summary>
        /// Called to remove a packet from the list
        /// </summary>
        /// <param name="dest">Where this packet was sent</param>
        /// <param name="id">ID of the packet to remove</param>
        protected void ClearPacket(EndPoint dest, byte id)
        {
            guaranteedPackets[dest][id].Dispose();
            guaranteedPackets[dest].Remove(id);
        }

        /// <summary>
        /// Sends an acknowledgement packet for <paramref name="packetid"/>.
        /// </summary>
        /// <param name="dest">Where to send the acknowledgement</param>
        /// <param name="packetid">The packet id to acknowledge</param>
        private void SendAck(EndPoint dest, byte packetid)
        {
            byte[] ack = new byte[2];
            ack[0] = 3;
            ack[1] = packetid;

            Send(dest, ack);
        }

        /// <summary>
        /// Class to hold data about a guaranteed packet
        /// </summary>
        private class GuaranteedPacket
        {
            public EndPoint Destination { get; private set; }
            public byte PacketID { get; private set; }
            public byte[] MD5 { get; private set; }
            public byte[] Header { get; private set; }
            public byte[] ByteArray { get; private set; }

            private ReliableServer reliableServer;
            private byte[] data;
            private Timer timer;
            private int maxTries;

            private int tries = 0;

            public GuaranteedPacket(ReliableServer rs, EndPoint dest, byte packetID, byte[] packet, int retries)
            {
                PacketID = packetID;
                reliableServer = rs;
                data = packet;
                maxTries = retries;

                MD5 = reliableServer.MD5.ComputeHash(data);
                Header = CreateHeader();
                ByteArray = ToByteArray();

                timer = new Timer(rs.RetryInterval);
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
                reliableServer.Send(Destination, ByteArray, Header);

                if (tries > maxTries)
                    reliableServer.ClearPacket(Destination, PacketID);
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
            public EndPoint Source { get; set; }
            public byte PacketID { get; set; }
            public byte[] Data { get; set; }

            private ReliableServer reliableServer;
            private Timer timer;

            public OrderedPacket(ReliableServer rs, EndPoint source, byte packetID, byte[] packet, float timeout)
            {
                reliableServer = rs;
                Source = source;
                PacketID = packetID;
                Data = packet;

                timer = new Timer(timeout);
                timer.Elapsed += (sender, args) =>
                {
                    reliableServer.OnPacketTimeout(Source, PacketID);
                };
                timer.Start();
            }
        }
    }
}
