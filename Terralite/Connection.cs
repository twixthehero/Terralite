using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Timers;

namespace Terralite
{
	/// <summary>
	/// This class holds data for a network connection to a single end point.
	/// </summary>
	public class Connection
	{
		public const byte R_DISCONNECT = 1;
		public const byte R_TIMEOUT = 2;

		/// <summary>
		/// Whether this connection is currently connected to its
		/// remote end point
		/// </summary>
		public bool Connected { get; private set; }

		/// <summary>
		/// Where this connection is connected to
		/// </summary>
		public EndPoint EndPoint { get; private set; }

		/// <summary>
		/// Connection ID
		/// </summary>
		public int ID { get; private set; }

		public delegate void ReceiveEvent(int connId, byte[] data);
		public event ReceiveEvent Receive;

		public delegate void DisconnectEvent(int connId, byte reason);
		public event DisconnectEvent Disconnect;

		private ReliableConnection _reliableConnection;
		private int _generatedHandshakeNumber;
		private int _receivedHandshakeNumber;

		private Dictionary<byte, GuaranteedPacket> _guaranteedPackets;
		private byte _nextSendID = 1;

		private Dictionary<byte, Timer> _timers;

		private Timer _handshakeTimeout;
		private Timer _handshakeInitTimer;

		/// <summary>
		/// Sends pings to keep alive the connection
		/// </summary>
		private Timer _keepAlive;

		/// <summary>
		/// Handles the connection timing out
		/// </summary>
		private Timer _timeout;

		private OrderedDictionary _orderedPackets;
		private byte _nextExpectedID;
		private bool _firstPacket = true;
		private byte[][] _multiPacket = null;

		public Connection(ReliableConnection reliableConnection, EndPoint endPoint, int id)
		{
			_reliableConnection = reliableConnection;
			EndPoint = endPoint;
			ID = id;

			_guaranteedPackets = new Dictionary<byte, GuaranteedPacket>();
			_timers = new Dictionary<byte, Timer>();
			_orderedPackets = new OrderedDictionary();

			_keepAlive = new Timer(_reliableConnection.KeepAlivePingTime * 1000);
			_keepAlive.Elapsed += (o, e) =>
			{
				SendPing();
			};

			_timeout = new Timer(_reliableConnection.ConnectionTimeout * 1000)
			{
				AutoReset = false
			};
			_timeout.Elapsed += (o, e) =>
			{
				OnTimeout();
			};

			_handshakeInitTimer = new Timer(_reliableConnection.ConnectInterval * 1000);
			_handshakeInitTimer.Elapsed += (o, e) =>
			{
				InitiateHandshakePhase1();
			};

			_handshakeTimeout = new Timer(_reliableConnection.ConnectTimeout * 1000);
			_handshakeTimeout.Elapsed += (o, e) =>
			{
				_handshakeInitTimer.Stop();
				_handshakeTimeout.Stop();
			};
		}

		/// <summary>
		/// Clears the list of ReceiveEvents
		/// </summary>
		public void ClearReceiveEvents()
		{
			Receive = null;
		}

		/// <summary>
		/// Clears the list of DisconnectEvents
		/// </summary>
		public void ClearDisconnectEvents()
		{
			Disconnect = null;
		}

		/// <summary>
		/// Creates a guaranteed packet from the data passed in.
		/// </summary>
		/// <param name="packet">Packet data</param>
		public void SendPacket(byte[] packet)
		{
			GuaranteedPacket guaranteedPacket = new GuaranteedPacket(_nextSendID, packet);
			_guaranteedPackets.Add(_nextSendID, guaranteedPacket);

			_nextSendID = (byte)((_nextSendID + 1) % byte.MaxValue);

			Timer timer = new Timer(_reliableConnection.RetryInterval * 1000)
			{
				AutoReset = true
			};
			timer.Elapsed += (sender, e) =>
			{
				guaranteedPacket.Tries++;
				_reliableConnection.Send(ID, guaranteedPacket.ByteArray, guaranteedPacket.Header);

				if (guaranteedPacket.Tries >= _reliableConnection.MaxRetries)
				{
					timer.Stop();
					_timers.Remove(guaranteedPacket.PacketID);
					_guaranteedPackets.Remove(guaranteedPacket.PacketID);
				}
			};
			timer.Start();
			_timers.Add(guaranteedPacket.PacketID, timer);

			_reliableConnection.Send(ID, guaranteedPacket.ByteArray, guaranteedPacket.Header);
			guaranteedPacket.Tries++;
		}

		/// <summary>
		/// Called when data is received from the managing ReliableConnection.
		/// </summary>
		/// <param name="buffer"></param>
		public void ProcessData(byte[] buffer)
		{
			//pieces[0] = byte[] header
			//pieces[1] = byte[] data
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
			if (_multiPacket == null)
				_multiPacket = new byte[buffer[1]][];

			//Initialize this slot in the array and copy
			_multiPacket[buffer[2]] = new byte[buffer.Length - 3];
			Array.Copy(buffer, 3, _multiPacket[buffer[2]], 0, _multiPacket[buffer[2]].Length);

			//If we don't have all the packets, return null
			for (int i = 0; i < _multiPacket.Length; i++)
				if (_multiPacket[i] == null)
					return null;

			MemoryStream ms = new MemoryStream();
			for (int i = 0; i < _multiPacket.Length; i++)
				ms.Write(_multiPacket[i], 0, _multiPacket[i].Length);

			byte[] whole = new byte[ms.Length];
			ms.Read(whole, 0, whole.Length);

			_multiPacket = null;

			return Utils.Split(whole);
		}

		/// <summary>
		/// Called when the connection handshake finishes
		/// </summary>
		private void OnConnected()
		{
			_reliableConnection.Log("Successfully connected");

			Connected = true;
			_handshakeInitTimer.Stop();
			_handshakeTimeout.Stop();
			_keepAlive.Start();
			_timeout.Start();
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

			if (type < Packet.MIN_VALUE || type > Packet.MAX_VALUE)
			{
				_reliableConnection.Log($"Got unknown packet type {type}");
				return false;
			}

			RestartTimeout();
			byte packetID;

			switch (type)
			{
				case Packet.INIT:
					int value = BitConverter.ToInt32(data, 0);
					InitiateHandshakePhase2(value);

					return false;
				case Packet.INIT_ACK:
					int valueA = BitConverter.ToInt32(data, 0);
					int valueB = BitConverter.ToInt32(data, 4);
					FinalizeHandshake(valueA, valueB);

					return false;
				case Packet.INIT_FIN:
					int a = BitConverter.ToInt32(data, 0);
					int b = BitConverter.ToInt32(data, 4);

					if (_receivedHandshakeNumber != a)
					{
						_reliableConnection.Log($"RX handshake number error during connection handshake. Expected {_receivedHandshakeNumber} got {a}.");
						_reliableConnection.Log("Connection will be closed.");
						_reliableConnection.Disconnect(ID);
					}
					else if (_generatedHandshakeNumber + 1 != b)
					{
						_reliableConnection.Log($"Generated handshake number error during connection handshake. Expected {_generatedHandshakeNumber + 1} got {b}.");
						_reliableConnection.Log("Connection will be closed.");
						_reliableConnection.Disconnect(ID);
					}

					_reliableConnection.Log("Handshake complete!");
					OnConnected();

					return false;
				case Packet.NON_RELIABLE:
					return true;
				case Packet.PING:
					SendPingAck();

					return false;
				case Packet.RELIABLE:
					packetID = header[1];

					SendAck(packetID);

					if (!_reliableConnection.UseOrdering)
						return true;
					else
					{
						if (_firstPacket)
							_nextExpectedID = packetID;

						//if already timeout waiting for this packet
						if (packetID < _nextExpectedID)
							return false;
						//if got next expected id
						else if (packetID == _nextExpectedID)
						{
							_nextExpectedID = (byte)((_nextExpectedID + 1) % byte.MaxValue);

							//while we have the next sequential packet, call OnReceive for it
							while (_orderedPackets.Contains(_nextExpectedID))
							{
								OrderedPacket op = (OrderedPacket)_orderedPackets[(object)_nextExpectedID];
								_orderedPackets.Remove(_nextExpectedID);
								_nextExpectedID = (byte)((_nextExpectedID + 1) % byte.MaxValue);

								OnReceive(op.Data);
							}

							return true;
						}

						_orderedPackets.Add(packetID, new OrderedPacket(this, packetID, data));
						return false;
					}
				case Packet.ACK:
					packetID = header[1];

					if (!_guaranteedPackets.ContainsKey(packetID))
					{
						_reliableConnection.Log($"Didn't send packet id {packetID}");
						return false;
					}

					_reliableConnection.Log($"Got ack for id {packetID}");
					ClearPacket(packetID);
					return false;
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
			Receive?.Invoke(ID, data);
		}

		/// <summary>
		/// Called when the connection times out, i.e. no data is received for
		/// reliableConnection.ConnectionTimeout seconds.
		/// </summary>
		private void OnTimeout()
		{
			_reliableConnection.Log($"Connection {ID} timed out");

			Disconnect?.Invoke(ID, R_TIMEOUT);
			_keepAlive.Stop();
			_reliableConnection.Disconnect(ID);
		}

		/// <summary>
		/// Called when the remote end disconnected. Invokes all functions
		/// stored in the Disconnect event.
		/// </summary>
		public void OnDisconnect()
		{
			Disconnect?.Invoke(ID, R_DISCONNECT);
			_keepAlive.Stop();
			_timeout.Stop();
		}

		/// <summary>
		/// Initiates the handshake process with the remove endpoint associated with
		/// connection ID <paramref name="connectionId"/>
		/// </summary>
		/// <param name="connectionId">The connection ID to initiate a handshake on</param>
		internal void InitiateHandshakePhase1()
		{
			_handshakeInitTimer.Start();
			_handshakeTimeout.Start();

			_reliableConnection.Log("Initiating handshake phase 1");
			_generatedHandshakeNumber = new Random().Next();

			_reliableConnection.Log($"Handshake a: {_generatedHandshakeNumber}");

			_reliableConnection.Send(ID, BitConverter.GetBytes(_generatedHandshakeNumber), Packet.HEADER_INIT);
		}

		/// <summary>
		/// Phase two of the handshake process
		/// </summary>
		/// <param name="valueA">The random value received from the remote end point</param>
		private void InitiateHandshakePhase2(int valueA)
		{
			_reliableConnection.Log("Initiating handshake phase 2");
			_reliableConnection.Log($"Handshake a: {valueA}");

			valueA++;

			_receivedHandshakeNumber = valueA;
			_generatedHandshakeNumber = new Random().Next();

			_reliableConnection.Log($"Handshake b: {_generatedHandshakeNumber}");

			byte[] data = new byte[sizeof(int) * 2];
			byte[] num1 = BitConverter.GetBytes(valueA);
			byte[] num2 = BitConverter.GetBytes(_generatedHandshakeNumber);

			Array.Copy(num1, data, num1.Length);
			Array.Copy(num2, 0, data, 4, num2.Length);

			_reliableConnection.Send(ID, data, Packet.HEADER_INIT_ACK);
		}

		/// <summary>
		/// Finalizes the handshake process
		/// </summary>
		/// <param name="valueA">The value received to be checked</param>
		/// <param name="valueB">The random value received from the remote end point to be incremented</param>
		private void FinalizeHandshake(int valueA, int valueB)
		{
			if (_generatedHandshakeNumber + 1 != valueA)
			{
				_reliableConnection.Log($"Error finalizing connection handshake. Expected {_generatedHandshakeNumber + 1} got {valueA}");
				_reliableConnection.Log("Connection will be closed.");
				_reliableConnection.Disconnect(ID);
			}

			valueB++;

			_reliableConnection.Log($"Finalizing handshake: {valueB}");

			byte[] data = new byte[sizeof(int) * 2];
			byte[] num1 = BitConverter.GetBytes(valueA);
			byte[] num2 = BitConverter.GetBytes(valueB);

			Array.Copy(num1, data, num1.Length);
			Array.Copy(num2, 0, data, 4, num2.Length);

			_reliableConnection.Send(ID, data, Packet.HEADER_INIT_FIN);

			OnConnected();
		}

		/// <summary>
		/// Sends a ping packet.
		/// </summary>
		private void SendPing()
		{
			_reliableConnection.Send(ID, null, Packet.PING_PACKET);
		}

		/// <summary>
		/// Sends an acknowledgement packet for the received ping.
		/// </summary>
		private void SendPingAck()
		{
			_reliableConnection.Send(ID, null, Packet.PING_ACK_PACKET);
		}

		/// <summary>
		/// Sends an acknowledgement packet for <paramref name="packetid"/>.
		/// </summary>
		/// <param name="packetid">The packet id to acknowledge</param>
		private void SendAck(byte packetid)
		{
			_reliableConnection.Send(ID, new byte[] { packetid }, Packet.ACK_PACKET);
		}

		/// <summary>
		/// Called to restart the connection timeout timer.
		/// </summary>
		private void RestartTimeout()
		{
			if (!Connected) return;

			_timeout.Stop();
			_timeout.Start();
		}

		public void ClearTimers()
		{
			_timeout.Stop();
			_keepAlive.Stop();
		}

		/// <summary>
		/// Clears the specified packet id from sending.
		/// </summary>
		/// <param name="id">Packet id to clear</param>
		public void ClearPacket(byte id)
		{
			_guaranteedPackets.Remove(id);
			_timers[id].Dispose();
			_timers.Remove(id);
		}

		/// <summary>
		/// Clears all outgoing packets.
		/// </summary>
		public void ClearAllPackets()
		{
			foreach (KeyValuePair<byte, GuaranteedPacket> pair in _guaranteedPackets)
				_timers[pair.Key].Dispose();

			_guaranteedPackets.Clear();
			_timers.Clear();
		}

		/// <summary>
		/// Class to hold data for handling an ordered packet
		/// </summary>
		private class OrderedPacket
		{
			public byte PacketID { get; set; }
			public byte[] Data { get; set; }

			private Connection connection;

			public OrderedPacket(Connection c, byte packetID, byte[] packet)
			{
				connection = c;
				PacketID = packetID;
				Data = packet;
			}
		}
	}
}
