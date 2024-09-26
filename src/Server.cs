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
  var recResult = await udpClient.ReceiveAsync();
  var (_, message) = DnsMessage.Read(recResult.Buffer, recResult.Buffer);


  var response = new DnsMessage();
  response.Header = message.Header;
  response.Header.QuestionCount = 0;
  response.Header.AnswerRecordCount = 0;
  response.Header.AdditionalRecordCount = 0;
  response.Header.AuthorityRecordCount = 0;
  response.Header.RespCode = (byte)(response.Header.OpCode == 0 ? 0 : 4);

  response.Header.SetMask(DnsHeader.Masks.IsResponse);

  foreach (var question in message.Questions)
  {
    response.AddQuestion(new Question { Name = question.Name, Type = 1, Class = 1 });
    response.AddAnswer(new ResourceRecord
    {
      Name = question.Name, Type = 1, Class = 1, TTL = 60,
      Data = new Memory<byte>(new byte[] { 8, 8, 8, 8 })
    });
  }

  var memory = new Memory<byte>(new byte[1024]);
  response.Header.Write(memory.Span);
  var mem2 = memory[12..];

  var questionLength = 0;
  foreach (var question in response.Questions)
  {
    var length = question.Write(mem2.Span);
    mem2 = mem2[length..];
    questionLength += length;
  }

  var answerLength = 0;
  foreach (var answer in response.Answers)
  {
    var length = answer.Write(mem2.Span);
    mem2 = mem2[length..];
    answerLength += length;
  }

  var responseMemory = memory[..(12 + questionLength + answerLength)];

  await udpClient.SendAsync(responseMemory, recResult.RemoteEndPoint);
}


public class DomainName
{
  private readonly string[] _labels;

  public DomainName(string[] labels)
  {
    _labels = labels;
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

  public static (int, DomainName) Read(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> completeBuffer)
  {
    var labels = new List<string>();
    var count = 0;
    // read until null termination
    while (buffer[0] != 0)
    {
      var strLen = buffer[0];
      if ((strLen & 192) == 192)
      {
        var ptr = BinaryPrimitives.ReadUInt16BigEndian(buffer) & 0x3FFF;
        var (_, name) = DomainName.Read(completeBuffer[ptr..], completeBuffer);
        return (2, name);
      }

      var str = Encoding.UTF8.GetString(buffer.Slice(1, strLen));
      labels.Add(str);
      buffer = buffer[(1 + strLen)..];
      count += 1 + strLen;
    }

    return (count + 1, new DomainName(labels.ToArray()));
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

  public static (int aCount, ResourceRecord q)
    Read(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> completeBuffer)
  {
    var count = 0;
    var (nCount, name) = DomainName.Read(buffer, completeBuffer);
    count += nCount;
    var type = BinaryPrimitives.ReadUInt16BigEndian(buffer[count..]);
    count += 2;
    var @class = BinaryPrimitives.ReadUInt16BigEndian(buffer[count..]);
    count += 2;
    var ttl = BinaryPrimitives.ReadUInt32BigEndian(buffer[count..]);
    count += 4;
    var dataLength = BinaryPrimitives.ReadUInt16BigEndian(buffer[count..]);
    count += 2;
    var data = new Memory<byte>(buffer.Slice(count, dataLength).ToArray());
    count += dataLength;
    var record = new ResourceRecord
    {
      Name = name, Type = type, Class = @class,
      TTL = ttl, Data = data
    };
    return (count, record);
  }
}

public class Question
{
  public DomainName Name { get; init; }
  public ushort Type { get; init; }
  public ushort Class { get; init; }


  public int Write(Span<byte> buffer)
  {
    var len = Name.Write(buffer);
    buffer = buffer[len..];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, Type);
    BinaryPrimitives.WriteUInt16BigEndian(buffer[2..], Class);
    return len + 4;
  }

  public static (int, Question) Read(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> completeBuffer)
  {
    var (len, name) = DomainName.Read(buffer, completeBuffer);
    buffer = buffer[len..];
    var type = BinaryPrimitives.ReadUInt16BigEndian(buffer);
    var @class = BinaryPrimitives.ReadUInt16BigEndian(buffer[2..]);
    var question = new Question { Name = name, Class = @class, Type = type };
    return (len + 4, question);
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

  public static (int, DnsMessage) Read(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> completeBuffer)
  {
    var (count, header) = DnsHeader.Read(buffer);
    buffer = buffer[count..];

    var questions = new List<Question>();
    for (int i = 0; i < header.QuestionCount; i++)
    {
      var (qCount, q) = Question.Read(buffer, completeBuffer);
      count += qCount;
      buffer = buffer[qCount..];
      questions.Add(q);
    }

    var answers = new List<ResourceRecord>();
    for (int i = 0; i < header.AnswerRecordCount; i++)
    {
      var (aCount, q) = ResourceRecord.Read(buffer, completeBuffer);
      count += aCount;
      buffer = buffer[aCount..];
      answers.Add(q);
    }

    var msg = new DnsMessage
    {
      Header = header, Questions = questions,
      Answers = answers
    };
    return (count, msg);
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
    get => (byte)GetFlagValue(Masks.ResponseCode, 0);
    set => SetFlagValue(value, Masks.ResponseCode, 0);
  }

  public byte OpCode
  {
    get => (byte)GetFlagValue(Masks.OpCode, Offsets.OpCode);
    set => SetFlagValue(value, Masks.OpCode, Offsets.OpCode);
  }

  private ushort GetFlagValue(ushort mask, ushort offset)
  {
    return (ushort)((Flags & mask) >> offset);
  }

  private void SetFlagValue(ushort value, ushort mask, ushort offset)
  {
    Flags |= (ushort)((value << offset) & mask);
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

  public static (int, DnsHeader) Read(ReadOnlySpan<byte> buffer)
  {
    if (buffer.Length < 12)
    {
      throw new ArgumentException("output too short");
    }

    var transactionId = BinaryPrimitives.ReadUInt16BigEndian(buffer);
    var flags = BinaryPrimitives.ReadUInt16BigEndian(buffer[2..]);
    var questionCount = BinaryPrimitives.ReadUInt16BigEndian(buffer[4..]);
    var answerRecordCount = BinaryPrimitives.ReadUInt16BigEndian(buffer[6..]);
    var authorityRecordCount =
      BinaryPrimitives.ReadUInt16BigEndian(buffer[8..]);
    var additionalRecordCount =
      BinaryPrimitives.ReadUInt16BigEndian(buffer[10..]);
    var header = new DnsHeader
    {
      TransactionId = transactionId,
      Flags = flags,
      QuestionCount = questionCount,
      AnswerRecordCount = answerRecordCount,
      AuthorityRecordCount = authorityRecordCount,
      AdditionalRecordCount = additionalRecordCount,
    };
    return (12, header);
  }
}