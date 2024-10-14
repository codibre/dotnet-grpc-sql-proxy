using System.IO.Compression;
using Google.Protobuf;

namespace Codibre.GrpcSqlProxy.Common
{
    public static class ByteStringExtensions
    {
        public static MemoryStream DecompressData(this ByteString gzipData)
        {
            MemoryStream decompressedStream = new();
            using var compressedStream = gzipData.ToMemoryStream();
            using GZipStream gzipStream = new(compressedStream, CompressionMode.Decompress);
            gzipStream.CopyTo(decompressedStream);
            decompressedStream.Position = 0; // Reset the position of the MemoryStream to the beginning
            return decompressedStream;
        }

        public static ByteString ToByteString(this MemoryStream stream) => ByteString.CopyFrom(stream.ToArray());

        public static MemoryStream ToMemoryStream(this ByteString stream)
            => new(stream.Memory.ToArray());
    }
}