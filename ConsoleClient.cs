using System;
using System.Net;
using System.Text;

namespace Terralite
{
    public class ConsoleClient
    {
        /// <summary>
        /// Whether to send using <c>Send</c> versus <c>SendReliable</c>
        /// </summary>
        public bool ReliableMode { get; set; }

        private const int DEFAULT_PORT = 10346;

        private ReliableConnection rc;
        private bool run;

        public ConsoleClient()
        {
            ReliableMode = false;

            rc = new ReliableConnection(null);
            rc.Receive += (buffer, len) =>
            {
                Console.WriteLine(Encoding.UTF8.GetString(buffer));
            };

            run = true;

            string line;

            Console.WriteLine("Enter commands below. Type /help for a list of commands");

            do
            {
                Console.Write("> ");
                line = Console.ReadLine();

                if (line[0] == '/')
                    ParseCommand(line);
                /*else if (rc.IsConnected)
                {
                    /*if (ReliableMode)
                        rc.SendReliable(line);
                    else
                        rc.Send(line);
                }*/
                else
                    Console.WriteLine("You must be connected to send text!");
            }
            while (run);

            //rc.Disconnect();
        }

        /// <summary>
        /// Parses a line of input for specific commands
        /// </summary>
        /// <param name="line">Line to parse</param>
        private void ParseCommand(string line)
        {
            string[] parts = line.Substring(1).Split(new string[] { " " }, StringSplitOptions.None);

            switch (parts[0])
            {
                case "h":
                case "help":
                    ShowHelp();
                    break;
                case "c":
                case "connect":
                    string ip;
                    int port = DEFAULT_PORT;
                    bool success;

                    if (parts[1].Contains(":"))
                    {
                        ip = parts[1].Split(':')[0];
                        string portString = parts[1].Split(':')[1];
                        success = int.TryParse(portString, out port);

                        if (!success)
                        {
                            Console.WriteLine("Invalid port '" + portString + "'.");
                            return;
                        }
                    }
                    else
                        ip = parts[1];

                    if (ip == "")
                        ip += "127.0.0.1";

                    IPAddress address;
                    success = IPAddress.TryParse(ip, out address);

                    if (!success)
                    {
                        Console.WriteLine("Invalid IP address '" + parts[1] + "'.");
                        return;
                    }

                    //rc.Connect(ip, port);
                    break;
                case "dc":
                case "disconnect":
                    //rc.Disconnect();
                    break;
                case "r":
                case "reliable":
                    ReliableMode = true;
                    break;
                case "nr":
                case "nonreliable":
                    ReliableMode = false;
                    break;
                case "e":
                case "exit":
                    run = false;
                    break;
                default:
                    Console.WriteLine("Unknown command '" + parts[0] + "'. Type /help for a list of commands");
                    break;
            }
        }

        /// <summary>
        /// Displays the help dialog
        /// </summary>
        private void ShowHelp()
        {
            Console.WriteLine("-----===== Console Client Help =====-----");

            Console.WriteLine("/help - Show this help dialog");
            Console.WriteLine("/connect <IP[:port]> - Connect to an IP address. Default port is 10346");
            Console.WriteLine("/disconnect - Disconnect from the remote end point");
            Console.WriteLine("/reliable - Turn ReliableMode on");
            Console.WriteLine("/nonreliable - Turn ReliableMode off");
            Console.WriteLine("/exit - Exit the program");

            Console.WriteLine("-----=====                     =====-----");
        }

        static void Main(string[] args)
        {
            new ConsoleClient();
        }
    }
}
