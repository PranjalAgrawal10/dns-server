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
  var message = new DnsMessage();
  message.Read(recResult.Buffer);
  
  var response = new DnsMessage();
  response.Header = message.Header;
  response.Header.QuestionCount = 0;
  response.Header.AnswerRecordCount = 0;
  response.Header.AdditionalRecordCount = 0;
  response.Header.AuthorityRecordCount = 0;
  Console.WriteLine($"Flag: {response.Header.Flags}");
  Console.WriteLine($"OpCode: {response.Header.OpCode}");
  response.Header.RespCode = (byte)(response.Header.OpCode == 0 ? 0 : 4);
  Console.WriteLine($"RespCode: {response.Header.RespCode}");


  response.Header.SetMask(DnsHeader.Masks.IsResponse);
  response.AddQuestion(new Question
  {
    Name = new DomainName("codecrafters.io"),
    Type = 1, Class = 1
  });
  
  response.AddAnswer(new ResourceRecord()
  {
    Name = new DomainName("codecrafters.io"), Type = 1, Class = 1, TTL = 60,
    Data = new Memory<byte>(new byte[] { 8, 8, 8, 8 })
  });
  
  var memory = new Memory<byte>(new byte[1024]);
  response.Header.Write(memory.Span);
  var mem2 = memory[12..];

  var questionLength = response.Questions[0].Write(mem2.Span);
  mem2 = mem2[questionLength..];
  var answerLength = response.Answers[0].Write(mem2.Span);
  await udpClient.SendAsync(memory[..(12 + questionLength + answerLength)], recResult.RemoteEndPoint);
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

public class ResourceRecord
{
  //     Name	Label Sequence	The domain name encoded as a sequence of labels.
  public DomainName Name { get; set; }

  //     Type	2-byte Integer	1 for an A record, 5 for a CNAME record etc.,
  //     full list here
  public ushort Type { get; set; }

  //     Class	2-byte Integer	Usually set to 1 (full list here)
  public ushort Class { get; set; }

  // TTL (Time-To-Live)	4-byte Integer	The duration in seconds a record can be
  // cached before requerying.
  public uint TTL { get; set; }

  //     Length (RDLENGTH)	2-byte Integer	Length of the RDATA field in
  //     bytes.
  // public ushort Length { get; set; }
  // Field	Type	Description
  //     Data (RDATA)	Variable	Data specific to the record type.
  public Memory<byte> Data { get; set; }

  public int Write(Span<byte> buffer)
  {
    var count = 0;
    count += Name.Write(buffer);
    BinaryPrimitives.WriteUInt16BigEndian(buffer[count..], Type);
    count += 2;
    BinaryPrimitives.WriteUInt16BigEndian(buffer[count..], Class);
    count += 2;
    BinaryPrimitives.WriteUInt32BigEndian(buffer[count..], TTL);
    count += 4;
    BinaryPrimitives.WriteUInt16BigEndian(buffer[count..], (ushort)Data.Length);
    count += 2;
    Data.Span.CopyTo(buffer[count..]);
    count += Data.Length;
    return count;
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
  public List<ResourceRecord> Answers { get; set; } = new();


  public void AddQuestion(Question question)
  {
    Questions.Add(question);
    Header.QuestionCount++;
  }

  public void AddAnswer(ResourceRecord record)
  {
    Answers.Add(record);
    Header.AnswerRecordCount++;
  }
  public int Read(ReadOnlySpan<byte> buffer) {
    var count = Header.Read(buffer);
    return count;
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
    public const ushort OpCode = 11;
  }

  public static class Masks
  {
    public const ushort IsResponse = 0x8000;
    public const ushort OpCode = 0x7800;
    public const ushort ResponseCode = 0xF;
  }

  public bool IsResponse => (Flags & Masks.IsResponse) == Masks.IsResponse;

  public byte RespCode
  {
    get => (byte)(Flags & Masks.ResponseCode);
    set => Flags |= (ushort)(value & Masks.ResponseCode);
  }

  public byte OpCode
  {
    get => (byte)((Flags & Masks.OpCode) >> Offsets.OpCode);
    set
    {
      var i = (value << Offsets.OpCode);
      var opCode = i & Masks.OpCode;
      Flags |= (ushort)opCode;
    }
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

  public int Read(ReadOnlySpan<byte> buffer)
  {
    if (buffer.Length < 12)
    {
      throw new ArgumentException("output too short");
    }

    TransactionId = BinaryPrimitives.ReadUInt16BigEndian(buffer);
    Flags = BinaryPrimitives.ReadUInt16BigEndian(buffer[2..]);
    QuestionCount = BinaryPrimitives.ReadUInt16BigEndian(buffer[4..]);
    AnswerRecordCount = BinaryPrimitives.ReadUInt16BigEndian(buffer[6..]);
    AuthorityRecordCount = BinaryPrimitives.ReadUInt16BigEndian(buffer[8..]);
    AdditionalRecordCount = BinaryPrimitives.ReadUInt16BigEndian(buffer[10..]);
    return 12;
  }
}