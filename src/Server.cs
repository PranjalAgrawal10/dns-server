using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

Console.WriteLine("Logs from your program will appear here!");

IPAddress ipAddress = IPAddress.Parse("127.0.0.1");

int port = 2053;

IPEndPoint udpEndPoint = new IPEndPoint(ipAddress, port);

// Create UDP socket
UdpClient udpClient = new UdpClient(udpEndPoint);


while (true)
{
  // Receive data
  IPEndPoint sourceEndPoint = new IPEndPoint(IPAddress.Any, 0);
  var recResult = await udpClient.ReceiveAsync();
  var responseHeader = new DnsHeader();
  responseHeader.Flags |= 0x8000;
  var memory = new Memory<byte>(new byte[12]);
  responseHeader.Write(memory.Span);
  await udpClient.SendAsync(memory, recResult.RemoteEndPoint);
}

public class DnsHeader
{
  public ushort TransactionId { get; set; } = 1234;

  public ushort Flags { get; set; }
  public ushort QuestionCount { get; set; }
  public ushort AnswerRecordCount { get; set; }
  public ushort AuthorityRecordCount { get; set; }
  public ushort AdditionalRecordCount { get; set; }

  public void Write(Span<byte> output)
  {
    if (output.Length < 12)
    {
      throw new ArgumentException("output too short");
    }

    BinaryPrimitives.WriteUInt16BigEndian(output, TransactionId);
    BinaryPrimitives.WriteUInt16BigEndian(output[2..], Flags);
    BinaryPrimitives.WriteUInt16BigEndian(output[4..], QuestionCount);
    BinaryPrimitives.WriteUInt16BigEndian(output[6..], AnswerRecordCount);
    BinaryPrimitives.WriteUInt16BigEndian(output[8..], AuthorityRecordCount);
    BinaryPrimitives.WriteUInt16BigEndian(output[10..], AdditionalRecordCount);
  }
}