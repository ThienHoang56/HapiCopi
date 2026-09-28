using System;
using System.IO;
using System.Text;

public static class PngChunkReader
{
    public static string? ExtractTextChunk(byte[] bytes, string targetKeyword)
    {
        if (bytes == null || bytes.Length < 8) return null;
        // Check PNG signature: 89 50 4E 47 0D 0A 1A 0A
        if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
            return null;

        int offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
            offset += 4;
            if (length < 0 || offset + length + 4 > bytes.Length) break;

            string type = Encoding.ASCII.GetString(bytes, offset, 4);
            offset += 4;

            if (type == "tEXt" || type == "zTXt")
            {
                byte[] chunkData = new byte[length];
                Buffer.BlockCopy(bytes, offset, chunkData, 0, length);

                int nullIdx = Array.IndexOf(chunkData, (byte)0);
                if (nullIdx > 0)
                {
                    string keyword = Encoding.ASCII.GetString(chunkData, 0, nullIdx);
                    if (string.Equals(keyword, targetKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        string text = Encoding.UTF8.GetString(chunkData, nullIdx + 1, length - nullIdx - 1);
                        return text;
                    }
                }
            }

            if (type == "IEND") break;
            offset += length + 4; // skip data + CRC
        }
        return null;
    }
}
