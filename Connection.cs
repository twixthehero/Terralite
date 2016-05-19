using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace Terralite
{
    /// <summary>
    /// 
    /// </summary>
    public class Connection
    {
        public EndPoint EndPoint { get; private set; }
        public int ID { get; private set; }

        private ReliableConnection reliableConnection;

        private Dictionary<byte, GuaranteedPacket> guaranteedPackets;
        private byte nextSendID = 0;

        private Dictionary<byte, Timer> timers;
        private int maxTries;

        //private OrderedDictionary orderedPackets;
        private byte nextExpectedID;

        public Connection(ReliableConnection rc, EndPoint endPoint, int id)
        {
            reliableConnection = rc;
            EndPoint = endPoint;
            ID = id;

            guaranteedPackets = new Dictionary<byte, GuaranteedPacket>();
            timers = new Dictionary<byte, Timer>();
        }

        public GuaranteedPacket AddPacket(byte[] packet, byte[] md5)
        {
            GuaranteedPacket p = new GuaranteedPacket(nextSendID, packet, md5);
            guaranteedPackets.Add(nextSendID, p);

            nextSendID = (byte)((nextSendID + 1) % byte.MaxValue);

            Timer timer = new Timer(0.3f);
            timer.AutoReset = true;
            timer.Elapsed += (sender, e) =>
            {
                p.Tries++;
                reliableConnection.Send(ID, p.ByteArray, p.Header);

                if (p.Tries > maxTries)
                    ClearPacket(p.PacketID);
            };
            timer.Start();

            timers.Add(p.PacketID, timer);

            return p;
        }

        public bool OnPreReceive(byte[] header, byte[] data)
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
                        if (reliableConnection.UseMD5)
                        {
                            byte[] hash = new byte[32];
                            Array.Copy(data, hash, hash.Length);

                            if (guaranteedPackets[packetID].CheckMD5(hash))
                                SendAck(packetID);
                        }
                        else
                            SendAck(packetID);

                        if (!reliableConnection.UseOrdering)
                            return true;
                        else
                        {
                            //if already timeout waiting for this packet
                            /*if (packetID < nextExpectedID)
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
                            return false;*/
                            break;
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
        /// Sends an acknowledgement packet for <paramref name="packetid"/>.
        /// </summary>
        /// <param name="packetid">The packet id to acknowledge</param>
        private void SendAck(byte packetid)
        {
            byte[] ack = new byte[2];
            ack[0] = 3;
            ack[1] = packetid;

            reliableConnection.Send(ID, ack);
        }

        public void ClearPacket(byte id)
        {
            guaranteedPackets.Remove(id);
            timers[id].Dispose();
            timers.Remove(id);
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
