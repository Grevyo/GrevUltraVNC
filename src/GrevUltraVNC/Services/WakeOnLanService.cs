using System.Net;
using System.Net.Sockets;

namespace GrevUltraVNC.Services;

public sealed class WakeOnLanService
{
    public async Task SendAsync(string macAddress)
    {
        var mac = ParseMac(macAddress);
        var packet = new byte[6 + 16 * mac.Length];
        Array.Fill(packet, (byte)0xFF, 0, 6);

        for (var i = 6; i < packet.Length; i += mac.Length)
            Buffer.BlockCopy(mac, 0, packet, i, mac.Length);

        using var udp = new UdpClient { EnableBroadcast = true };
        var destination = new IPEndPoint(IPAddress.Broadcast, 9);
        for (var i = 0; i < 3; i++)
        {
            await udp.SendAsync(packet, packet.Length, destination);
            await Task.Delay(75);
        }
    }

    private static byte[] ParseMac(string value)
    {
        var compact = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (compact.Length != 12)
            throw new FormatException("Enter a valid 12-digit MAC address first.");

        return Enumerable.Range(0, 6)
            .Select(i => Convert.ToByte(compact.Substring(i * 2, 2), 16))
            .ToArray();
    }
}
