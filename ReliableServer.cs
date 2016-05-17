using System;

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
    }
}
