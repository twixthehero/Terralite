using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Terralite
{
	/// <summary>
	/// ReliableConnection allows for sending data reliably or not.
	/// If reliability is used, packets that are transmitted incorrectly
	/// will be resent. Packets that arrive out of order will be fixed.
	/// Packets can be sent with a 100% guarantee they will arrive.
	/// </summary>
	public class ReliableConnection
	{
		/// <summary>
		/// Interval to resend the connection handshake packet in
		/// seconds.
		/// Default value = 2.
		/// </summary>
		public float ConnectInterval { get; set; }

		/// <summary>
		/// Amount of time without receiving any data before
		/// a connection is considered timed out.
		/// Default value = 40f.
		/// </summary>
		public float ConnectionTimeout { get; set; }

		/// <summary>
		/// Amount of time to try initiating a connection handshake
		/// in seconds.
		/// Default value = 10f;
		/// </summary>
		public float ConnectTimeout { get; set; }

		/// <summary>
		/// Whether to log the extra debug information
		/// </summary>
		public bool Debug { get; set; }

		/// <summary>
		/// Whether to exit when a receive exception is thrown
		/// </summary>
		public bool ExitOnReceiveException { get; set; }

		/// <summary>
		/// Amount of time in seconds to wait between consecutive
		/// keep alive pings.
		/// Default value = 15f.
		/// </summary>
		public float KeepAlivePingTime { get; set; }

		protected string LogFile { get; set; }
		protected const string LOG_NAME_FORMAT = "yyyy-MM-dd HH-mm-ss-ffff";

		/// <summary>
		/// Maximum number of send retries.
		/// Default value = 10.
		/// </summary>
		public int MaxRetries { get; set; }

		/// <summary>
		/// Interval to retry sending guaranteed packets in seconds.
		/// Default value = 0.5f.
		/// </summary>
		public float RetryInterval { get; set; }

		private bool Running { get; set; }

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

		protected Socket socket = null;
		public int Port { get; protected set; } = 0;

		/// <summary>
		/// Thread to handle receiving data
		/// </summary>
		protected Thread receiveThread = null;

		/// <summary>
		/// Default receive event callback
		/// </summary>
		private Connection.ReceiveEvent defaultRxEvent = null;

		/// <summary>
		/// Default disconnect event callback
		/// </summary>
		private Connection.DisconnectEvent defaultDcEvent = null;

		/// <summary>
		/// Mapping of connection ID to Connection object
		/// </summary>
		protected Dictionary<int, Connection> idToConnection;

		/// <summary>
		/// Mapping of Endpoind to Connection object
		/// </summary>
		protected Dictionary<EndPoint, Connection> epToConnection;
		protected int nextConnectionID = 0;

		private StreamWriter writer;

		/// <summary>
		/// Creates a <c>ReliableClient</c> object with the default
		/// log file (log.txt).
		/// </summary>
		/// <param name="port">Port to bind to</param>
		public ReliableConnection(int port = 0)
		{
			ConnectInterval = 2;
			ConnectionTimeout = 40f;
			ConnectTimeout = 10;
			Debug = true;
			ExitOnReceiveException = false;
			KeepAlivePingTime = 15;
			MaxRetries = 10;
			Port = port;
			RetryInterval = 0.5f;
			UseOrdering = true;

			idToConnection = new Dictionary<int, Connection>();
			epToConnection = new Dictionary<EndPoint, Connection>();
			
			LogFile = Path.Combine("networklogs", $"rclog-{DateTime.Now.ToString(LOG_NAME_FORMAT)}.txt");
			OpenLogFile();

			CreateSocket();
		}

		/// <summary>
		/// Whether this <c>ReliableConnection</c> is currently has to any
		/// remote endpoints
		/// </summary>
		public bool HasConnections
		{
			get { return idToConnection.Count > 0; }
		}

		/// <summary>
		/// Whether this <c>ReliableConnection</c> is currently has to any
		/// remote endpoints
		/// </summary>
		/// <param name="connectionId">Connection ID to check connection status</param>
		public bool IsConnected(int connectionId)
		{
			if (!idToConnection.ContainsKey(connectionId))
				return false;

			return idToConnection[connectionId].Connected;
		}

		private void OpenLogFile()
		{
			if (!Directory.Exists("networklogs"))
			{
				Directory.CreateDirectory("networklogs");
			}

			FileStream fs = File.Open(LogFile, FileMode.Create, FileAccess.Write, FileShare.Read);
			writer = new StreamWriter(fs)
			{
				AutoFlush = true
			};
		}

		/// <summary>
		/// Creates the socket object, sets it to non-blocking, and
		/// binds it.
		/// </summary>
		private void CreateSocket()
		{
			if (socket != null) return;

			Log("Creating socket...");
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

			try
			{
				Log("Binding socket...");
				socket.Bind(new IPEndPoint(IPAddress.Any, Port));
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
				{
					Log("Failed to bind socket");

					if (ExitOnReceiveException)
						Environment.Exit(1);
				}
				else
				{
					Port = ((IPEndPoint)socket.LocalEndPoint).Port;
					Log($"Socket bound to {socket.LocalEndPoint}");
				}
			}
		}

		/// <summary>
		/// Parses <paramref name="to"/> and creates an IPAddress from it.
		/// </summary>
		/// <param name="to">IP address or hostname to connect to</param>
		/// <param name="port">Port to connect to</param>
		/// <returns>The connection ID or -1 if the connection creation failed</returns>
		public int Connect(string to, int port)
		{
			bool success = IPAddress.TryParse(to, out IPAddress address);

			if (!success)
			{
				try
				{
					address = Dns.GetHostEntry(to).AddressList[0];
				}
				catch (Exception e)
				{
					Log(e.Message);
					if (e.InnerException != null)
						Log(e.InnerException);
					Log(e.StackTrace);
				}
			}

			if (address == null)
			{
				throw new TerraliteException($"Invalid IP address/hostname: '{to}'.");
			}

			if (port < 0 || port >= 65536)
			{
				throw new TerraliteException($"Invalid port: {port}. Must be between (0 - 65535)");
			}

			//successfully resolved the ip/hostname

			if (receiveThread == null)
			{
				Log("Created receive thread");
				receiveThread = new Thread(new ThreadStart(ReceiveThread));
				receiveThread.Start();
			}

			CreateSocket();

			//create new connection
			int connectionId = CreateConnection(address, port);

			return connectionId;
		}

		/// <summary>
		/// Creates a new connection to <paramref name="address"/> at <paramref name="port"/>.
		/// </summary>
		/// <param name="address">The address to connect to</param>
		/// <param name="port">The port to connect to</param>
		/// <returns>The connection ID or -1 if connection creation failed</returns>
		protected int CreateConnection(IPAddress address, int port, bool initializeHandshake = true)
		{
			try
			{
				EndPoint ep = new IPEndPoint(address, port);
				int id = GetNextConnectionID();
				Connection conn = new Connection(this, ep, id);

				if (defaultRxEvent != null)
					conn.Receive += defaultRxEvent;
				if (defaultDcEvent != null)
					conn.Disconnect += defaultDcEvent;

				idToConnection.Add(id, conn);
				epToConnection.Add(ep, conn);

				if (initializeHandshake)
					conn.InitiateHandshakePhase1();

				Log($"Created connection for {ep}");
				return conn.ID;
			}
			catch (Exception e)
			{
				Log(e.Message);
				if (e.InnerException != null)
					Log(e.InnerException);
				Log(e.StackTrace);
			}

			return -1;
		}

		/// <summary>
		/// Returns the next available connection ID
		/// </summary>
		private int GetNextConnectionID()
		{
			nextConnectionID = (nextConnectionID + 1) % int.MaxValue;

			while (idToConnection.ContainsKey(nextConnectionID))
				nextConnectionID = (nextConnectionID + 1) % int.MaxValue;

			return nextConnectionID;
		}

		/// <summary>
		/// Disconnects from a specific connection
		/// </summary>
		/// <param name="id">Connection id to remove</param>
		/// <param name="remove">Whether to remove the Connection object from the dictionaries</param>
		public void Disconnect(int id, bool remove = true)
		{
			if (!HasConnections)
			{
				if (Debug)
					Log("Not connected to any endpoints!");

				return;
			}

			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			try
			{
				Send(id, null, Packet.DISCONNECT_PACKET);

				Connection conn = idToConnection[id];
				RemoveConnection(conn, remove);

				Log($"Disconnected from {conn.EndPoint}");
			}
			catch (Exception e)
			{
				Log(e.Message);
				if (e.InnerException != null)
					Log(e.InnerException);
				Log(e.StackTrace);
			}
		}

		protected virtual void RemoveConnection(Connection conn, bool remove = true)
		{
			//clear all queued packets
			conn.ClearAllPackets();

			//remove Connection from dictionaries
			if (remove)
			{
				idToConnection[conn.ID].ClearTimers();
				idToConnection.Remove(conn.ID);
				epToConnection.Remove(conn.EndPoint);

				if (idToConnection.Count == 0)
				{
					receiveThread.Abort();
					receiveThread = null;
				}
			}

			Log($"Removed connection {conn.ID}");
		}

		/// <summary>
		/// Disconnects from all connections
		/// </summary>
		public virtual void DisconnectAll()
		{
			foreach (KeyValuePair<int, Connection> pair in idToConnection)
				Disconnect(pair.Key, false);

			idToConnection.Clear();
			epToConnection.Clear();
		}

		/// <summary>
		/// Disconnects from all connections, closes the log file, and shuts
		/// down the reliable connection. 
		/// </summary>
		public virtual void Shutdown()
		{
			Log("Shutting down...");

			DisconnectAll();

			Log("Stopping receive thread...");
			if (receiveThread != null)
			{
				Running = false;

				if (socket.Blocking)
				{
					receiveThread.Abort();
				}
				else
				{
					receiveThread.Join();
				}

				receiveThread = null;
			}

			Log("Closing socket...");
			if (socket != null)
			{
				socket.Close();
				socket = null;
			}

			Log("Closing log...");
			writer.Close();
		}

		#region EVENTS

		/// <summary>
		/// Adds an event callback to the connection with id <paramref name="id"/>.
		/// </summary>
		/// <param name="id">Connection id to add the event to</param>
		/// <param name="evt">The function callback</param>
		public void AddReceiveEvent(int id, Connection.ReceiveEvent evt)
		{
			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			idToConnection[id].Receive += evt;
		}

		/// <summary>
		/// Removes an event callback to the connection with id <paramref name="id"/>.
		/// </summary>
		/// <param name="id">Connection id to remove the event from</param>
		/// <param name="evt">The function callback</param>
		public void RemoveReceiveEvent(int id, Connection.ReceiveEvent evt)
		{
			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			idToConnection[id].Receive -= evt;
		}

		/// <summary>
		/// Removes all receive events from the connection with id <paramref name="id"/>.
		/// </summary>
		/// <param name="id">Connection id to remove from</param>
		public void RemoveAllReceiveEvents(int id)
		{
			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			idToConnection[id].ClearReceiveEvents();
		}

		/// <summary>
		/// Adds an event callback to the connection with id <paramref name="id"/>.
		/// </summary>
		/// <param name="id">Connection id to add the event to</param>
		/// <param name="evt">The function callback</param>
		public void AddDisconnectEvent(int id, Connection.DisconnectEvent evt)
		{
			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			idToConnection[id].Disconnect += evt;
		}

		/// <summary>
		/// Removes an event callback to the connection with id <paramref name="id"/>.
		/// </summary>
		/// <param name="id">Connection id to remove the event from</param>
		/// <param name="evt">The function callback</param>
		public void RemoveDisconnectEvent(int id, Connection.DisconnectEvent evt)
		{
			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			idToConnection[id].Disconnect -= evt;
		}

		/// <summary>
		/// Removes all receive events from the connection with id <paramref name="id"/>.
		/// </summary>
		/// <param name="id">Connection id to remove from</param>
		public void RemoveAllDisconnectEvents(int id)
		{
			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			idToConnection[id].ClearDisconnectEvents();
		}

		/// <summary>
		/// Sets the default receive event callback for new Connections
		/// </summary>
		/// <param name="evt">The function callback</param>
		public void SetDefaultReceiveEvent(Connection.ReceiveEvent evt)
		{
			defaultRxEvent = evt;
		}

		/// <summary>
		/// Sets the default disconnect event callback for new Connections
		/// </summary>
		/// <param name="evt">The function callback</param>
		public void SetDefaultDisconnectEvent(Connection.DisconnectEvent evt)
		{
			defaultDcEvent = evt;
		}

		#endregion

		/// <summary>
		/// Sends <paramref name="text"/> encoded as UTF8.
		/// </summary>
		/// <param name="id">Connection id to send to</param>
		/// <param name="text">Message to send</param>
		public void Send(int id, string text)
		{
			Send(id, Encoding.UTF8.GetBytes(text), -1);
		}

		/// <summary>
		/// Sends the <paramref name="buffer"/> to the connection ID specified.
		/// </summary>
		/// <param name="id">Connection id to send to</param>
		/// <param name="buffer">A byte[] of data to send. If the buffer plus header
		/// is bigger than 1400 bytes, it will be split across multiple calls to
		/// Socket.SendTo</param>
		public void Send(int id, byte[] buffer, int count = -1)
		{
			Send(id, buffer, null, count);
		}

		/// <summary>
		/// Sends the <paramref name="buffer"/> to the connection ID specified.
		/// </summary>
		/// <param name="id">Connection id to send to</param>
		/// <param name="buffer">A byte[] of data to send. If the buffer plus header
		/// is bigger than 1400 bytes, it will be split across multiple calls to
		/// Socket.SendTo</param>
		/// <param name="header">Header to append to the buffer</param>
		/// <remarks>
		/// Note this function does not do anything if <c>IsConnected</c> is false or
		/// the specified id does not exist.
		/// </remarks>
		protected internal void Send(int id, byte[] buffer = null, byte[] header = null, int count = -1)
		{
			if (!HasConnections)
			{
				if (Debug)
					Log("Not connected to any endpoints!");

				return;
			}

			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			if (buffer == null && header == null)
				return;

			try
			{
				byte[] head = header ?? Packet.HEADER_NON_RELIABLE;

				/*if (Debug)
				{
					Log("====== Pre-send data 1 ======");
					Log($"received buf len: {buffer.Length}");
					Log($"received count: {count}");
					Log($"head len: {head.Length}");
				}*/

				byte[] data = Utils.Combine(head, buffer, -1, count);

				/*if (Debug)
				{
					Log("====== Pre-send data 2 ======");
					Log($"data len: {data.Length}");
				}*/

				if (data.Length <= Packet.MAX_SEND_SIZE)
				{
					if (Debug)
					{
						string d = "";
						foreach (byte b in data)
							d += $"[{b}]";
						Log($"Sending: {d}");
					}

					socket.SendTo(data, idToConnection[id].EndPoint);
				}
				else
				{
					byte[][] packets = Utils.SplitBuffer(head, buffer);

					if (Debug)
						Log($"Split buffer into {packets.GetLength(0)} packets");

					foreach (byte[] packet in packets)
					{
						if (Debug)
							Log($"Sending data size {packet.Length}");

						socket.SendTo(packet, idToConnection[id].EndPoint);
					}
				}
			}
			catch (Exception e)
			{
				Log(e.Message);
				if (e.InnerException != null)
					Log(e.InnerException);
				Log(e.StackTrace);
			}
		}

		/// <summary>
		/// Sends <paramref name="text"/> reliably and encoded as UTF8
		/// </summary>
		/// <param name="id">Connection id to send to</param>
		/// <param name="text">Text to send</param>
		public void SendReliable(int id, string text)
		{
			SendReliable(id, Encoding.UTF8.GetBytes(text), -1);
		}

		/// <summary>
		/// Reliably sends <paramref name="packet"/>
		/// </summary>
		/// <param name="id">Connection id to send to</param>
		/// <param name="packet">Data to send</param>
		/// <remarks>
		/// Note this function does not do anything if <c>IsConnected</c> is false or
		/// the specified id does not exist.
		/// </remarks>
		public void SendReliable(int id, byte[] packet, int count = -1)
		{
			if (!HasConnections)
			{
				if (Debug)
					Log("Not connected to any endpoints!");

				return;
			}

			if (!idToConnection.ContainsKey(id))
			{
				Log($"ID doesn't exist in connections: {id}");
				return;
			}

			if (count == -1)
				idToConnection[id].SendPacket(packet);
			else
			{
				byte[] trunc = new byte[count];
				Array.Copy(packet, 0, trunc, 0, count);

				idToConnection[id].SendPacket(trunc);
			}
		}

		/// <summary>
		/// Receive thread function. Reads data from the socket and calls
		/// <c>ProcessData</c>.
		/// </summary>
		protected virtual void ReceiveThread()
		{
			while (!HasConnections) { /* Wait for the connection to finish being created */}

			Running = true;
			byte[] buffer = new byte[Packet.MAX_SEND_SIZE];
			byte[] truncated = null;
			EndPoint ep = new IPEndPoint(IPAddress.Any, 0);

			while (Running)
			{
				try
				{
					int numBytes = socket.ReceiveFrom(buffer, ref ep);
					truncated = new byte[numBytes];
					Array.Copy(buffer, truncated, truncated.Length);

					ProcessData(ep, truncated);
				}
				catch (SocketException)
				{
					try
					{
						if (socket == null || socket.Available == 0)
							continue;
					}
					catch (ObjectDisposedException)
					{
						Log("Socket disposed");
						continue;
					}
				}
				catch (Exception e)
				{
					if (e.Message.Contains("aborted"))
						continue;

					Log(e.Message);
					if (e.InnerException != null)
						Log(e.InnerException);
					Log(e.StackTrace);

					if (ExitOnReceiveException)
						Environment.Exit(1);
				}
			}
		}

		/// <summary>
		/// Checks is connected to <paramref name="ep"/> and calls ProcessData. Otherwise
		/// do nothing.
		/// </summary>
		/// <param name="ep">Connection EndPoint to use for data processing</param>
		/// <param name="buffer">Data to process</param>
		protected virtual void ProcessData(EndPoint ep, byte[] buffer)
		{
			if (Debug)
			{
				string data = "";
				foreach (byte b in buffer)
					data += $"[{b}]";
				Log($"Process data: {data}");
			}

			if (!epToConnection.ContainsKey(ep))
			{
				Log($"Received data from an unknown remote: {ep}");
				return;
			}

			if (buffer[0] == Packet.DISCONNECT)
			{
				Log($"{ep} disconnected.");

				epToConnection[ep].OnDisconnect();
				RemoveConnection(epToConnection[ep]);

				return;
			}

			epToConnection[ep].ProcessData(buffer);
		}

		/// <summary>
		/// Helper function to shorten output lines.
		/// </summary>
		/// <typeparam name="T">Type that <paramref name="obj"/> is</typeparam>
		/// <param name="obj">The data to log to <c>Console.Out</c></param>
		internal void Log<T>(T obj)
		{
			Console.WriteLine(obj.ToString());
			writer.WriteLine(obj.ToString());
		}
	}
}
