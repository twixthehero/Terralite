using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terralite;

namespace TerraliteTests
{
	[TestClass]
	public class OrderingTest
	{
		private const byte INIT_ACK = 2;
		private const byte RELIABLE = 11;
		private const byte PING = 25;
		private const byte PING_ACK = 26;
		private static byte[] HEADER_INIT_ACK = new byte[1] { INIT_ACK };
		private static byte[] PING_ACK_PACKET = new byte[1] { PING_ACK };

		private int expectedId = 2;

		[TestMethod]
		public void TestOrdering()
		{
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 2000));

			ReliableConnection rc = new ReliableConnection();
			rc.SetDefaultReceiveEvent((id, data) =>
			{
				Assert.AreEqual(expectedId, data[0]);
			});
			int connId = rc.Connect("127.0.0.1", 2000);

			EndPoint from = new IPEndPoint(IPAddress.Any, 0);
			byte[] shake1 = new byte[1024];
			int len = socket.ReceiveFrom(shake1, ref from);
			byte[] trimmed = new byte[len];
			Array.Copy(shake1, trimmed, len);

			byte[][] pieces = Utils.Split(trimmed);
			int generated = BitConverter.ToInt32(pieces[1], 0);

			int generated2 = new Random().Next();
			socket.SendTo(Utils.Combine(HEADER_INIT_ACK, Utils.Combine(BitConverter.GetBytes(generated + 1), BitConverter.GetBytes(generated2))), from);

			byte[] shake3 = new byte[1024];
			len = socket.ReceiveFrom(shake3, ref from);
			trimmed = new byte[len];
			Array.Copy(shake3, trimmed, len);

			pieces = Utils.Split(trimmed);
			int calculated = BitConverter.ToInt32(pieces[1], 0);
			int calculated2 = BitConverter.ToInt32(pieces[1], 4);

			socket.SendTo(BuildReliablePacket(3), from);
			socket.SendTo(BuildReliablePacket(2), from);

			Thread.Sleep(10000);

			rc.Shutdown();
			socket.Close();
		}

		private byte[] BuildReliablePacket(byte id)
		{
			byte[] data = new byte[10];
			data[0] = RELIABLE;

			for (int i = 1; i < data.Length; i++)
			{
				data[i] = id;
			}

			expectedId++;

			return data;
		}
	}
}
