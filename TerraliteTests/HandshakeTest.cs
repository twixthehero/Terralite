using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Sockets;
using Terralite;

namespace TerraliteTests
{
    [TestClass]
    public class HandshakeTest
	{
		private const byte INIT = 1;
		private const byte INIT_ACK = 2;
		private const byte INIT_FIN = 3;
		private static byte[] HEADER_INIT = new byte[1] { INIT };
		private static byte[] HEADER_INIT_ACK = new byte[1] { INIT_ACK };
		private static byte[] HEADER_INIT_FIN = new byte[1] { INIT_FIN };

		[TestMethod]
        public void TestInitiate()
		{
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 0));

			ReliableServer rs = new ReliableServer();
			rs.SetDefaultReceiveEvent((id, data) =>
			{
				string d = "";
				foreach (byte b in data)
				{
					d += $"[{b}]";
				}
				Console.WriteLine($"Data: {d}");
			});
			EndPoint to = new IPEndPoint(IPAddress.Loopback, rs.Port);

			Random r = new Random();
			int generated = r.Next();
			byte[] shake1 = Utils.Combine(HEADER_INIT, BitConverter.GetBytes(generated));
			socket.SendTo(shake1, to);
			
			byte[] expected = Utils.Combine(HEADER_INIT_ACK, BitConverter.GetBytes(generated + 1));

			EndPoint from = new IPEndPoint(IPAddress.Any, 0);
			byte[] shake2 = new byte[1024];
			int len = socket.ReceiveFrom(shake2, ref from);
			byte[] trimmed = new byte[len];
			Array.Copy(shake2, trimmed, len);

			for (int i = 0; i < expected.Length; i++)
			{
				Assert.AreEqual(expected[i], trimmed[i]);
			}

			byte[][] pieces2 = Utils.Split(trimmed);
			int calculated = BitConverter.ToInt32(pieces2[1], 0);
			int generated2 = BitConverter.ToInt32(pieces2[1], 4);

			Assert.AreEqual(generated + 1, calculated);

			byte[] shake3 = Utils.Combine(HEADER_INIT_FIN, Utils.Combine(BitConverter.GetBytes(calculated), BitConverter.GetBytes(generated2 + 1)));
			socket.SendTo(shake3, to);

			socket.Close();
			rs.Shutdown();
        }

		[TestMethod]
		public void TestReceive()
		{
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 2000));

			ReliableConnection rc = new ReliableConnection();
			rc.SetDefaultReceiveEvent((id, data) =>
			{
				string d = "";
				foreach (byte b in data)
				{
					d += $"[{b}]";
				}
				Console.WriteLine($"Data: {d}");
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

			Assert.AreEqual(generated + 1, calculated);
			Assert.AreEqual(generated2 + 1, calculated2);
			
			rc.Shutdown();
			socket.Close();
		}
	}
}
