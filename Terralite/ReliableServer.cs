using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Terralite
{
	/// <summary>
	/// 
	/// </summary>
	public class ReliableServer : ReliableConnection
	{
		public bool IsRunning { get; private set; } = true;

		public ReliableServer(int port = 0) : base(port)
		{
			LogFile = Path.Combine("networklogs", $"rslog-{DateTime.Now.ToString(LOG_NAME_FORMAT)}.txt");

			receiveThread = new Thread(new ThreadStart(ReceiveThread));
			receiveThread.Start();
		}

		public override void DisconnectAll()
		{
			base.DisconnectAll();

			IsRunning = false;
		}

		protected override void RemoveConnection(Connection conn, bool remove = true)
		{
			//clear all queued packets
			conn.ClearAllPackets();

			//remove Connection from dictionaries
			if (remove)
			{
				idToConnection[conn.ID].ClearTimers();
				idToConnection.Remove(conn.ID);
				epToConnection.Remove(conn.EndPoint);
			}

			Log($"Removed connection {conn.ID}");
		}

		/// <summary>
		/// Receive thread function. Reads data from the socket and calls
		/// <c>ProcessData</c>.
		/// </summary>
		protected override void ReceiveThread()
		{
			byte[] buffer = new byte[Packet.MAX_SEND_SIZE];
			byte[] truncated = null;
			EndPoint ep = new IPEndPoint(IPAddress.Any, 0);

			while (IsRunning)
			{
				try
				{
					Log("ReceiveFrom");
					int numBytes = socket.ReceiveFrom(buffer, ref ep);
					Log($"numBytes {numBytes}");
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

		protected override void ProcessData(EndPoint ep, byte[] buffer)
		{
			if (buffer[0] == Packet.DISCONNECT)
			{
				Log($"{ep} disconnected.");

				RemoveConnection(epToConnection[ep]);

				return;
			}

			if (Debug)
			{
				string data = "";
				foreach (byte b in buffer)
					data += $"[{b}]";
				Log($"Process data: {data}");
			}

			//potential new connection
			if (!epToConnection.ContainsKey(ep))
			{
				IPEndPoint iep = (IPEndPoint)ep;
				CreateConnection(iep.Address, iep.Port, false);
			}

			epToConnection[ep].ProcessData(buffer);
		}
	}
}
