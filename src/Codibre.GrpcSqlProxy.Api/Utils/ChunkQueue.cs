using System.IO.Compression;
using Avro;
using Avro.Generic;
using Avro.IO;
using Google.Protobuf;
using Codibre.GrpcSqlProxy.Common;

namespace Codibre.GrpcSqlProxy.Api.Utils;

public sealed class ChunkQueue : Queue<MemoryStream>
{
    private MemoryStream _stream;
    private Stream _writeStream;
    private BinaryEncoder _writer;
    private bool _compress;
    private GenericDatumWriter<GenericRecord> _datumWriter;
    private int _chunkSize = 0;
    private int _packetSize;

    public ChunkQueue(bool compress, RecordSchema schemaResult, int packetSize)
    {
        _compress = compress;
        (_stream, _writer, _writeStream) = PrepareStream();
        _datumWriter = new GenericDatumWriter<GenericRecord>(schemaResult);
        _packetSize = packetSize;
    }

    private (MemoryStream, BinaryEncoder, Stream) PrepareStream()
    {
        var stream =  new MemoryStream();
        Stream writeStream = _compress ? new GZipStream(stream, CompressionLevel.Optimal) : stream;
        BinaryEncoder writer = new(writeStream);
        return (stream, writer, writeStream);
    }

    public bool Empty => _stream.Length == 0 && Count == 0;

    public void EnqueueRest() {
        if (_stream.Length > 0) Enqueue(_stream);
        _writeStream.Dispose();
    }

    public void Write(GenericRecord record)
    {
        _datumWriter.Write(record, _writer);
        _chunkSize++;
        if (_chunkSize >= _packetSize)
        {
            Enqueue(_stream);
            _writeStream.Dispose();
            (_stream, _writer, _writeStream) = PrepareStream();
            _chunkSize = 0;
        }
    }

    public ByteString Pop()
    {
        var stream = Dequeue();
        var result = stream.ToByteString();
        stream.Dispose();
        return result;
    }
}