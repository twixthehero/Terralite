using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Timers;

namespace Terralite
{
    /// <summary>
    /// This class holds data for a network connection to a single end point.
    /// </summary>
    public class Connection
    {
        /// <summary>
        /// Where this connection is connected to
        /// </summary>
        public EndPoint EndPoint { get; private set; }

        /// <summary>
        /// Connection ID
        /// </summary>
        public int ID { get; private set; }

        /// <summary>
        /// Maximum number of packet send retries
        /// </summary>
        public int MaxTries { get; set; }

        public delegate void ReceiveEvent(byte[] data);
        public event ReceiveEvent Receive;

        private ReliableConnection reliableConnection;

        private Dictionary<byte, GuaranteedPacket> guaranteedPackets;
        private byte nextSendID = 1;

        private Dictionary<byte, Timer> timers;

        private OrderedDictionary orderedPackets;
        private byte nextExpectedID;
        private bool firstPacket = true;
        private byte[][] multiPacket = null;

        public Connection(ReliableConnection rc, EndPoint endPoint, int id)
        {
            reliableConnection = rc;
            EndPoint = endPoint;
            ID = id;
            MaxTries = 10;

            guaranteedPackets = new Dictionary<byte, GuaranteedPacket>();
            timers = new Dictionary<byte, Timer>();
            orderedPackets = new OrderedDictionary();
        }

        /// <summary>
        /// Creates a guaranteed packet from the data passed in.
        /// </summary>
        /// <param name="packet">Packet data</param>
        public void SendPacket(byte[] packet)
        {
            GuaranteedPacket p = new GuaranteedPacket(nextSendID, packet);
            guaranteedPackets.Add(nextSendID, p);

            nextSendID = (byte)((nextSendID + 1) % byte.MaxValue);

            Timer timer = new Timer(0.3f);
            timer.AutoReset = true;
            timer.Elapsed += (sender, e) =>
            {
                p.Tries++;
                reliableConnection.Send(ID, p.ByteArray, p.Header);

                if (p.Tries > MaxTries)
                    ClearPacket(p.PacketID);
            };
            timer.Start();
            timers.Add(p.PacketID, timer);

            reliableConnection.Send(ID, p.ByteArray, p.Header);
        }

        /// <summary>
        /// Called when data is received from the managing ReliableConnection.
        /// </summary>
        /// <param name="buffer"></param>
        public void ProcessData(byte[] buffer)
        {
            //pieces[0] = header
            //pieces[1] = data
            byte[][] pieces = buffer[0] != Packet.MULTI ? Utils.Split(buffer) : ProcessMulti(buffer);

            if (OnPreReceive(pieces[0], pieces[1]))
                OnReceive(pieces[1]);
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

            return Utils.Split(whole);
        }

        /// <summary>
        /// Called before <c>OnReceive</c> to do packet preprocessing.
        /// </summary>
        /// <param name="header">Header of packet</param>
        /// <param name="data">Packet data to do preprocessing with</param>
        /// <returns>Whether or not <c>OnReceive</c> needs to be called</returns>
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
            
            switch (type)
            {
                case Packet.RELIABLE:
                    SendAck(packetID);

                    if (!reliableConnection.UseOrdering)
                        return true;
                    else
                    {
                        if (firstPacket)
                            nextExpectedID = packetID;

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

                                OnReceive(op.Data);
                            }

                            return true;
                        }

                        orderedPackets.Add(packetID, new OrderedPacket(this, packetID, data, Packet.ORDER_TIMEOUT));
                        return false;
                    }
                case Packet.ACK:
                    if (!guaranteedPackets.ContainsKey(packetID))
                    {
                        Log("Didn't send packet id " + packetID);
                        return false;
                    }

                    ClearPacket(packetID);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Called when data has been received. Invokes all functions
        /// stored in the Receive event.
        /// </summary>
        /// <param name="data">Data that was received</param>
        /// <remarks>
        /// Note this function does not do anything if <c>Receive</c> is <c>null</c>.
        /// </remarks>
        public void OnReceive(byte[] data)
        {
            Receive?.Invoke(data);
        }

        /// <summary>
        /// Sends an acknowledgement packet for <paramref name="packetid"/>.
        /// </summary>
        /// <param name="packetid">The packet id to acknowledge</param>
        private void SendAck(byte packetid)
        {
            reliableConnection.Send(ID, new byte[] { packetid }, new byte[] { Packet.ACK });
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

            OnReceive(op.Data);
        }

        /// <summary>
        /// Clears the specified packet id from sending.
        /// </summary>
        /// <param name="id">Packet id to clear</param>
        public void ClearPacket(byte id)
        {
            guaranteedPackets.Remove(id);
            timers[id].Dispose();
            timers.Remove(id);
        }

        /// <summary>
        /// Clears all outgoing packets.
        /// </summary>
        public void ClearAllPackets()
        {
            foreach (KeyValuePair<byte, GuaranteedPacket> pair in guaranteedPackets)
            {
                timers[pair.Key].Dispose();
                timers.Remove(pair.Key);
            }

            guaranteedPackets.Clear();
            timers.Clear();
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

        /// <summary>
        /// Class to hold data for handling an ordered packet
        /// </summary>
        private class OrderedPacket
        {
            public byte PacketID { get; set; }
            public byte[] Data { get; set; }

            private Connection connection;
            private Timer timer;

            public OrderedPacket(Connection c, byte packetID, byte[] packet, float timeout)
            {
                connection = c;
                PacketID = packetID;
                Data = packet;

                timer = new Timer(timeout);
                timer.Elapsed += (sender, args) =>
                {
                    connection.OnPacketTimeout(PacketID);
                };
                timer.Start();
            }
        }
    }
}
