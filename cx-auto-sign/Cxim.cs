using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

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
            return start > end ? null : json;
        }

        public static int BytesIndexOf(IReadOnlyList<byte> bytes, IReadOnlyList<byte> value,
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

        private static LongBits ReadLong(IReadOnlyList<byte> bytes, ref int index)
        {
            var length = bytes.Count;
            var n = new LongBits();
            byte b;
            if (!(length - index > 4))
            {
                for (var i = 0; i < 3; ++i)
                {
                    if (index >= length)
                    {
                        throw new IndexOutOfRangeException($"index out of range: {index} + 1 > {length}");
                    }
                    b = bytes[index++];
                    n.Low = (uint)(n.Low | (uint)((127 & b) << 7 * i));
                    if (b < 128)
                    {
                        return n;
                    }
                }
                n.Low = (uint)(n.Low | (uint)((127 & bytes[index++]) << 7 * 3));
                return n;
            }
            for (var i = 0; i < 4; ++i)
            {
                b = bytes[index++];
                n.Low = (uint)(n.Low | (uint)((127 & b) << 7 * i));
                if (b < 128)
                {
                    return n;
                }
            }
            b = bytes[index++];
            n.Low = (uint)(n.Low | (uint)((127 & b) << 28));
            n.High = (uint)(n.High | (uint)((127 & b) >> 4));
            if (b < 128)
            {
                return n;
            }
            if (length - index > 4)
            {
                for (var i = 0; i < 5; ++i)
                {
                    b = bytes[index++];
                    n.High = (uint)(n.High | (uint)((127 & b) << 7 * i + 3));
                    if (b < 128)
                    {
                        return n;
                    }
                }
            }
            else
            {
                for (var i = 0; i < 5; ++i)
                {
                    if (index >= length)
                    {
                        throw new IndexOutOfRangeException($"index out of range: {index} + 1 > {length}");
                    }
                    b = bytes[index++];
                    n.High = (uint)(n.High | (uint)((127 & b) << 7 * i + 3));
                    if (b < 128)
                    {
                        return n;
                    }
                }
            }
            throw new Exception("invalid variant encoding");
        }

        private const long L1E8H = 0x100000000L;

        private class LongBits
        {
            public long Low;
            public long High;

            public LongBits(long low = 0, long high = 0)
            {
                Low = low;
                High = high;
            }

            // public long ToNumber(bool e = false)
            // {
            //     if (e || (uint)High >> 31 == 0)
            //     {
            //         return Low + L1E8H * High;
            //     }
            //     var t = 1 + (uint)~Low;
            //     var r = (uint)~this.High;
            //     if (t != 0)
            //     {
            //         ++r;
            //     }
            //     return -(t + L1E8H * r);
            // }

            public Long ToLong(bool unsigned = false)
            {
                return new Long(Low, High, unsigned);
            }

            public override string ToString()
            {
                return $"{{ Low: {Low}, High: {High} }}";
            }
        }

        private class Long
        {
            private readonly long _low;
            private readonly long _high;
            private readonly bool _unsigned;

            public Long(long low, long high, bool unsigned)
            {
                _low = low;
                _high = high;
                _unsigned = unsigned;
            }

            public long ToNumber()
            {
                var uLow = (uint)_low >> 0;
                return _unsigned
                    ? (uint)_high * L1E8H + uLow
                    : _high * L1E8H + uLow;
            }

            public bool IsZero()
            {
                return _low == 0 && _high == 0;
            }

            public override string ToString()
            {
                return $"{{ Low: {_low}, High: {_high}, Unsigned: {_unsigned} }}";
            }
        }
        
        public static class CmdCourseChatFeedback
        {
            public static readonly byte[] BytesCmd =
                new byte[] { 0x52, 0x18 }.Concat(Encoding.ASCII.GetBytes("CMD_COURSE_CHAT_FEEDBACK")).ToArray();

            private static readonly byte[] BytesAid =
                new byte[] { 0x0A, 0x03 }.Concat(Encoding.ASCII.GetBytes("aid")).ToArray();

            private static readonly byte[] BytesState =
                new byte[] { 0x0A, 0x0B }.Concat(Encoding.ASCII.GetBytes("stuFeedback")).ToArray();

            private static bool? GetState(IReadOnlyList<byte> bytes)
            {
                var i = BytesIndexOf(bytes, BytesState);
                if (i == -1)
                {
                    return null;
                }
                i += BytesState.Length + 3;
                return !ReadLong(bytes, ref i).ToLong().IsZero();
            }
            
            public static string GetStateString(IReadOnlyList<byte> bytes)
            {
                var b = GetState(bytes);
                if (b == null)
                {
                    return null;
                }
                return (bool)b ? "开启" : "关闭";
            }

            public static string GetActiveId(IReadOnlyList<byte> bytes)
            {
                var i = BytesIndexOf(bytes, BytesAid);
                if (i == -1)
                {
                    return null;
                }
                i += BytesAid.Length + 3;
                return ReadLong(bytes, ref i).ToLong().ToNumber().ToString();
            }
        }
    }
}