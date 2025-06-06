using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CtYun
{

    public class LoginInfo()
    {
        public string DesktopId { get; set; }

        public string SessionId { get; set; }
        public string DeviceType { get; set; }
        public string DeviceCode { get; set; }
        public string UserAccount { get; set; }


        public string Password { get; set; }
        public string UserPhone { get; set; }

        public string SecretKey { get; set; }

        public int UserId { get; set; }

        public int TenantId { get; set; }


        public string Version { get; set; }
        public int BufferSize()
        {
            var e = 36;
            e += this.SessionId.Length + 1;
            e += this.DeviceType.Length + 1;
            e += this.DeviceCode.Length + 1;
            e += this.UserAccount.Length + 1;
            return e;
        }
        public int ToBuffer(byte[] buffer, int offset = 0)
        {
            int t = offset;
            uint desktopId = Convert.ToUInt32(this.DesktopId);
            int o = 36;

            // 写入 desktopId
            BitConverter.GetBytes(desktopId).CopyTo(buffer, t);
            t += 4;

            // 写入 sessionId 长度 + 偏移
            BitConverter.GetBytes(this.SessionId.Length + 1).CopyTo(buffer, t);
            t += 4;
            BitConverter.GetBytes(o).CopyTo(buffer, t);
            t += 4;
            o += this.SessionId.Length + 1;

            // 写入 deviceType 长度 + 偏移
            BitConverter.GetBytes(this.DeviceType.Length + 1).CopyTo(buffer, t);
            t += 4;
            BitConverter.GetBytes(o).CopyTo(buffer, t);
            t += 4;
            o += this.DeviceType.Length + 1;

            // 写入 deviceCode 长度 + 偏移
            BitConverter.GetBytes(this.DeviceCode.Length + 1).CopyTo(buffer, t);
            t += 4;
            BitConverter.GetBytes(o).CopyTo(buffer, t);
            t += 4;
            o += this.DeviceCode.Length + 1;

            // 写入 userAccount 长度 + 偏移
            BitConverter.GetBytes(this.UserAccount.Length + 1).CopyTo(buffer, t);
            t += 4;
            BitConverter.GetBytes(o).CopyTo(buffer, t);
            t += 4;
            o += this.UserAccount.Length + 1;

            // 写入 sessionId 内容
            WriteStringWithNullTerminator(buffer, ref t, this.SessionId);

            // 写入 deviceType 内容
            WriteStringWithNullTerminator(buffer, ref t, this.DeviceType);

            // 写入 deviceCode 内容
            WriteStringWithNullTerminator(buffer, ref t, this.DeviceCode);

            // 写入 userAccount 内容
            WriteStringWithNullTerminator(buffer, ref t, this.UserAccount);


            return t;
        }

        private static void WriteStringWithNullTerminator(byte[] buffer, ref int offset, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                buffer[offset++] = (byte)value[i]; // 假设是 ASCII 字符
            }
            buffer[offset++] = 0; // null terminator
        }
    }

    public class SendLoginInfo
    {
        public int Type { get; set; }

        public int Size { get; set; }

        public byte[] Data { get; set; }

        public int BufferSize()
        {
            return this.Data != null ? 6 + this.Data.Length : 6;
        }

        public int ToBuffer(byte[] buffer, int offset = 0)
        {
            // 使用 BinaryWriter 写入 buffer
            using (var ms = new MemoryStream(buffer))
            using (var writer = new BinaryWriter(ms))
            {
                ms.Position = offset;

                // 写入 type（2字节）
                writer.Write((ushort)this.Type); // 假设 this.type 是 ushort
                offset += 2;

                // 写入 data 长度（4字节）
                int dataLength = this.Data != null ? this.Data.Length : 0;
                writer.Write(dataLength);
                offset += 4;

                // 写入 data 本体
                if (this.Data != null && this.Data.Length > 0)
                {
                    writer.Write(this.Data);
                    offset += this.Data.Length;
                }

                return offset;
            }
        }
    }
}
