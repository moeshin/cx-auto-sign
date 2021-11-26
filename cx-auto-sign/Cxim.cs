using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Serilog;

namespace cx_auto_sign
{
    public static class Cxim
    {
        private static readonly byte[] BytesAttachment =
        {
            0x0a, 0x61, 0x74, 0x74, 0x61, 0x63, 0x68, 0x6D, 0x65, 0x6E, 0x74, 0x10, 0x08, 0x32
        };

        private static readonly byte[] BytesEnd =
        {
            0x1A, 0x16, 0x63, 0x6F, 0x6E, 0x66, 0x65, 0x72, 0x65, 0x6E, 0x63, 0x65, 0x2E, 0x65, 0x61, 0x73,
            0x65, 0x6D, 0x6F, 0x62, 0x2E, 0x63, 0x6F, 0x6D
        };

        public static readonly byte[] BytesCourseHeader =
        {
            0x08, 0x00, 0x40, 0x00, 0x4a
        };

        public static string Pack(byte[] data)
        {
            return new StringBuilder()
                .Append("[\"")
                .Append(Convert.ToBase64String(data))
                .Append("\"]")
                .ToString();
        }

        public static string BuildLoginPackage(string uid, string imToken)
        {
            var timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            using var s = new MemoryStream();
            using var bw = new BinaryWriter(s);
            bw.Write(new byte[] { 0x08, 0x00, 0x12 });
            bw.Write((byte)(52 + uid.Length));   // 接下来到 webim_{timestamp} 的内容长度
            bw.Write(new byte[] { 0x0a, 0x0e });
            bw.Write(Encoding.ASCII.GetBytes("cx-dev#cxstudy"));
            bw.Write(new byte[] { 0x12 });
            bw.Write((byte)uid.Length);
            bw.Write(Encoding.ASCII.GetBytes(uid));
            bw.Write(new byte[] { 0x1a, 0x0b });
            bw.Write(Encoding.ASCII.GetBytes("easemob.com"));
            bw.Write(new byte[] { 0x22, 0x13 });
            bw.Write(Encoding.ASCII.GetBytes($"webim_{timestamp}"));
            bw.Write(new byte[] { 0x1a, 0x85, 0x01 });
            bw.Write(Encoding.ASCII.GetBytes("$t$"));
            bw.Write(Encoding.ASCII.GetBytes($"{imToken}"));
            bw.Write(new byte[] { 0x40, 0x03, 0x4a, 0xc0, 0x01, 0x08, 0x10, 0x12, 0x05, 0x33, 0x2e, 0x30, 0x2e, 0x30, 0x28, 0x00, 0x30, 0x00, 0x4a, 0x0d });
            bw.Write(Encoding.ASCII.GetBytes($"{timestamp}"));
            bw.Write(new byte[] { 0x62, 0x05, 0x77, 0x65, 0x62, 0x69, 0x6d, 0x6a, 0x13, 0x77, 0x65, 0x62, 0x69, 0x6d, 0x5f });
            bw.Write(Encoding.ASCII.GetBytes($"{timestamp}"));
            bw.Write(new byte[] { 0x72, 0x85, 0x01, 0x24, 0x74, 0x24 });
            bw.Write(Encoding.ASCII.GetBytes($"{imToken}"));
            bw.Write(new byte[] { 0x50, 0x00, 0x58, 0x00 });
            return Pack(s.ToArray());
        }

        public static string BuildReleaseSession(string chatId, byte[] session)
        {
            var length = chatId.Length;
            using var s = new MemoryStream();
            using var bw = new BinaryWriter(s);
            bw.Write(BytesCourseHeader);
            bw.Write((byte) (length + 38));
            bw.Write((byte) 0x10);
            bw.Write(session);
            bw.Write(new byte[] { 0x1a, 0x29, 0x12 });
            bw.Write((byte) length);
            bw.Write(Encoding.UTF8.GetBytes(chatId));
            bw.Write(BytesEnd);
            bw.Write(new byte[] { 0x58, 0x00 });
            return Pack(s.ToArray());
        }

        public static JToken GetAttachment(byte[] bytes, ref int start, int end)
        {
            start = BytesIndexOf(bytes, BytesAttachment, start, end);
            if (start == -1)
            {
                return null;
            }

            start += BytesAttachment.Length;
            var length = ReadLength2(bytes, ref start);
            var s = start;
            var e = start += length;
            var json = JObject.Parse(Encoding.Default.GetString(bytes[s..e]));
            // Log.Information("Cxim: Attachment {v}", json.ToString());
            return start > end ? null : json["att_chat_course"];
        }

        private static int BytesIndexOf(IReadOnlyList<byte> bytes, IReadOnlyList<byte> value,
            int start = 0, int end = 0)
        {
            var length = value.Count;
            var len = bytes.Count;
            if (length == 0 || len == 0) return -1;
            var first = value[0];
            for (var i = start; i < len && (end == 0 || i < end); ++i)
            {
                if (bytes[i] != first) continue;
                var isReturn = true;
                for (var j = 1; j < length; ++j)
                {
                    if (bytes[i + j] == value[j]) continue;
                    isReturn = false;
                    break;
                }
                if (isReturn) return i;
            }
            return -1;
        }

        private static int BytesLastIndexOf(IReadOnlyList<byte> bytes, IReadOnlyList<byte> value)
        {
            var length = value.Count;
            var len = bytes.Count;
            if (length == 0 || len == 0) return -1;
            var first = value[0];
            for (var i = len - length; i > -1; --i)
            {
                if (bytes[i] != first) continue;
                var isReturn = true;
                for (var j = 1; j < length; j++)
                {
                    if (bytes[i + j] == value[j]) continue;
                    isReturn = false;
                    break;
                }
                if (isReturn) return i;
            }
            return -1;
        }

        public static string GetChatId(byte[] bytes)
        {
            var index = BytesLastIndexOf(bytes, BytesEnd);
            if (index == -1) return null;
            var i = Array.LastIndexOf(bytes, (byte) 0x12, index);
            if (i == -1) return null;
            var len = bytes[++i];
            return ++i + len == index ? Encoding.UTF8.GetString(bytes[i..index]) : null;
        }

        private static int ReadLength2(IReadOnlyList<byte> bytes, ref int index)
        {
            return bytes[index++] + (bytes[index++] - 1) * 0x80;
        }

        public static int ReadEnd2(IReadOnlyList<byte> bytes, ref int index)
        {
            return ReadLength2(bytes, ref index) + index;
        }
    }
}