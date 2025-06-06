using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CtYun
{
    public class Encryption
    {
        List<Memory<byte>> buffers = new List<Memory<byte>>();

        public uint auth_mechanism { get; set; } = 1;
        public byte[] Execute(byte[] key)
        {
            ////第一步处理数据
            resolveInboundData(key);

            //this.pub_key = function(e, t) 执行这个返回l
            var pubkey = pub_key();

            //执行function L(e, t) {
            var buffer =  L(128, "\u0000", pubkey);
            return ToBuffer(buffer);
        }
        public byte[] ToBuffer(byte[] buffer)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // 1. 先写入 auth_mechanism（4 字节）
                writer.Write(auth_mechanism);

                // 2. 然后写入原始数据
                writer.Write(buffer);

                // 获取完整的新数据
                return ms.ToArray();

            }

        }
        int Read24BitValue(byte[] s, int startIndex)
        {
            if (startIndex + 2 >= s.Length)
                throw new ArgumentException("Not enough data in array");

            int value = s[startIndex++];
            for (int i = 1; i < 3; i++)
            {
                value <<= 8;
                value |= s[startIndex++];
            }

            return value;
        }


        void resolveInboundData(byte[] data)
        {
            //获取到的数据从索引 16 开始的数据复制一份，删除前面16个字符
            Memory<byte> payload = new Memory<byte>(data, 16, data.Length - 16);
            buffers.Add(payload);
        }

        E pub_key()
        {
            byte[] e = buffers[0].ToArray().AsMemory(32, 129).ToArray();
            var l = new E();
            l.n.FromString(e);
            l.e = Read24BitValue(buffers[0].ToArray(), 163);
            return l;
        }


        byte[] L(int e, string t,E pubkey)
        {
            byte[] r = new byte[20];
            RandomNumberGenerator.Fill(r);
            //随机生成密钥
#if DEBUG
            r = new byte[]
        {
    90,  64, 187, 211, 235,   2,  14, 254,
   104, 220,  29, 151, 185, 105, 121, 211,
    98, 253,  44, 232
        };
#endif

            var i = e - 1 - r.Length;
            var l = i - t.Length - 1;
            var a = new byte[i];
            var resulta = Sha1JsEquivalent.S("");
            for (int k = 0; k < resulta.Length; k++)
            {
                a[k] = (byte)resulta[k];
            }
            a[l] = 1;
            for (int o = 0; o < t.Length; o++)
            {
                a[o + l + 1] = (byte)t[o]; // 等价于 t.charCodeAt(o)
            }
            var c = new byte[i];
            P(c, r);
            // 假设 u, r, a 是 byte[] 类型
            for (int o = 0; o < c.Length; o++)
            {
                a[o] ^= c[o];
            }
            var u = new byte[20];
            P(u, a);

            for (int o = 0; o < u.Length; o++)
            {
                r[o] ^= u[o];
            }

            // 构造字符串 d
            StringBuilder dBuilder = new StringBuilder();

            // 第一个字符是 \x00
            dBuilder.Append((char)0);

            // 把 r 中的每个字节转成字符追加
            for (int o = 0; o < r.Length; o++)
            {
                dBuilder.Append((char)r[o]);
            }

            // 把 a 中的每个字节转成字符追加
            for (int o = 0; o < a.Length; o++)
            {
                dBuilder.Append((char)a[o]);
            }

            // 最终结果字符串
            byte[] ri = new byte[dBuilder.Length];
            for (int n = 0; n < dBuilder.Length; n++)
            {
                ri[n] = (byte)dBuilder[n]; // 或者 i[n] = (int)o[n]; 也可
            }
            var le = new BigIntStruct();
            le.FromString(ri);
            le.Clamp();

            return pubkey.doPublic(le);
        }
        void P(byte[] e, byte[] t)
        {
            int n = 0, o = 0;

            while (o < e.Length)
            {
                // 构造 i 字符串
                var sb = new StringBuilder();
                for (int r = 0; r < t.Length; r++)
                    sb.Append((char)t[r]);

                sb.Append((char)((n >> 24) & 0xFF));
                sb.Append((char)((n >> 16) & 0xFF));
                sb.Append((char)((n >> 8) & 0xFF));
                sb.Append((char)(n & 0xFF));



                string i = sb.ToString();

                // 计算 SHA-1 值
                var a = Sha1JsEquivalent.S(i); // 注意：S 是你之前的函数，返回类似 JS 的二进制字符串

                for (int r = 0; r < a.Length && o < e.Length; r++, o++)
                    e[o] = (byte)a[r]; // 类似 JS 的 charCodeAt
                n++;
            }
        }
    }
}
