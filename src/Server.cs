using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
  var response = new DnsMessage();
  response.Header.SetMask(DnsHeader.Masks.IsResponse);
  response.AddQuestion(new Question
  {
    Name = new DomainName("codecrafters.io"),
    Type = 1, Class = 1
  });
  var memory = new Memory<byte>(new byte[1024]);
  response.Header.Write(memory.Span);
  var mem2 = memory[12..];
  var questionLength = response.Questions[0].Write(mem2.Span);
  await udpClient.SendAsync(memory[..(12 + questionLength)], recResult.RemoteEndPoint);
}


public class DomainName
{
  private readonly string[] _labels;

  public DomainName(string domain)
  {
    _labels = domain.Split('.');
  }

  public int Write(Span<byte> buffer)
  {
    var count = 0;
    foreach (var label in _labels)
    {
      var bytes = WriteLabel(label, buffer);
      buffer = buffer[bytes..];
      count += bytes;
    }

    count++;
    buffer[count] = 0;
    return count;
  }

  private static int WriteLabel(string label, Span<byte> buffer)
  {
    var bytes = Encoding.UTF8.GetByteCount(label);
    buffer[0] = (byte)bytes;
    Encoding.UTF8.GetBytes(label, buffer[1..]);
    return bytes + 1;
  }
}

public class Question
{
  public DomainName Name { get; set; }
  public ushort Type { get; set; }
  public ushort Class { get; set; }

  public int Write(Span<byte> buffer)
  {
    var len = Name.Write(buffer);
    buffer = buffer[len..];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, Type);
    BinaryPrimitives.WriteUInt16BigEndian(buffer[2..], Type);
    return len + 4;
  }
}

public class DnsMessage
{
  public DnsHeader Header { get; set; } = new();
  public List<Question> Questions { get; set; } = new();

  public void AddQuestion(Question question)
  {
    Questions.Add(question);
    Header.QuestionCount++;
  }
}

public class DnsHeader
{
  public ushort TransactionId { get; set; } = 1234;

  public ushort Flags { get; set; }
  public ushort QuestionCount { get; set; }
  public ushort AnswerRecordCount { get; set; }
  public ushort AuthorityRecordCount { get; set; }
  public ushort AdditionalRecordCount { get; set; }

  public static class Offsets
  {
    public const ushort IsResponse = 15;
    public const ushort OpCode = 14;
  }

  public static class Masks
  {
    public const ushort IsResponse = 0x8000;
    public const ushort OpCode = 0x7000;
  }

  public bool IsResponse
  {
    get { return (Flags & Masks.IsResponse) == Masks.IsResponse; }
  }

  public void SetFlagBool(bool value, ushort bitPosition)
  {
    if (value)
    {
      Flags |= (ushort)(1 << bitPosition);
    }
    else
    {
      Flags &= (ushort)(~(1 << bitPosition));
    }
  }

  public void SetMask(ushort mask)
  {
    Flags |= mask;
  }

  public void UnsetMask(ushort mask)
  {
    Flags &= (ushort)~mask;
  }

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