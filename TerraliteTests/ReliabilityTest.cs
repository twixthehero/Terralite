using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Sockets;
using Terralite;

namespace TerraliteTests
{
	[TestClass]
	public class ReliabilityTest
	{
		private const byte INIT_ACK = 2;
		private static byte[] HEADER_INIT_ACK = new byte[1] { INIT_ACK };

		[TestMethod]
		public void TestResend()
		{
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 2000));

			ReliableConnection rc = new ReliableConnection();
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

			rc.SendReliable(connId, "Hello Terralite");

			int rxCount = 0;
			
			for (int i = 0; i < rc.MaxRetries * 2; i++)
			{
				byte[] data = new byte[1024];
				len = socket.ReceiveFrom(data, ref from);
				trimmed = new byte[len];
				Array.Copy(data, trimmed, len);

				rxCount++;
			}

			Assert.AreEqual(rc.MaxRetries, rxCount);

			rc.Shutdown();
			socket.Close();
		}
	}
}
