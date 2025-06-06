

namespace CtYun
{

    public class V
    {
        BigIntStruct m;
        int mp;
        int mpl;
        int mph;
        int um;
        int mt2;

        public void v(BigIntStruct e)
        {
            m = e;
            mp = m.InvDigit();
            mpl = 32767 & mp;
            mph = mp >> 15;
            mt2 = 2 * e.t;
            um = (1 << e.DB - 15) - 1;
        }

        public BigIntStruct Convert(BigIntStruct e)
        {
            var t = new BigIntStruct();
            e.DLShiftTo(m.t,t);
            t.divRemTo(m,t);
            return t;
        }

        public void SqrTo(BigIntStruct e, BigIntStruct t)
        {
            e.SquareTo(t);
            Reduce(t);
        }
        public void Reduce(BigIntStruct e)
        {
            // 保证 e 至少有 mt2 位
            while (e.t <= this.mt2)
                e.Data[e.t++] = 0;

            for (int t = 0; t < m.t; ++t)
            {
                // 取 e[t] 的低 15 位
                int n = e.Data[t] & 0x7FFF;

                // 计算 r
                int r = n * mpl + (((n * mph + ((e.Data[t] >> 15) * mpl) & um) << 15)) & e.DM;

                // 模乘加累积
                int k = t + m.t;
                long carry = m.Am(0, r, e, t, 0, m.t);
                e.Data[k] += (int)carry;

                // 处理进位
                while (e.Data[k] >= e.DV)
                {
                    e.Data[k] -= e.DV;
                    k++;
                    if (k >= e.Data.Length); // 确保不会越界
                    e.Data[k]++;
                }
            }

            // 裁剪多余高位
            e.Clamp();

            // 相当于 e = e >> (m.t * DB)
            e.DrShiftTo(m.t, e);

            // 如果 e >= m，则再减去一次
            if (e.CompareTo(m) >= 0)
                e.SubTo(m, e);
        }

        public void MulTo(BigIntStruct e, BigIntStruct t, BigIntStruct n)
        {
            e.MultiplyTo(t, n);
            Reduce(n);
        }

        public BigIntStruct Revert(BigIntStruct e)
        {
            var t = new BigIntStruct(); // 等价于 l()
            e.CopyTo(t);
            Reduce(t);
            return t;
        }
    }
    public class E
    {
        public int e { get; set; }
        public BigIntStruct n { get; set; } = new();

        public byte[] doPublic(BigIntStruct ek)
        {
           var s= ek.modPowInt(e, n).ToString(16);

           return HexStringToByteArray(s);
        }


        public static byte[] HexStringToByteArray(string s)
        {
            if (s.Length % 2 != 0)
                s = "0" + s;

            byte[] r = new byte[s.Length / 2];
            for (int n = 0; n < s.Length; n += 2)
            {
                r[n / 2] = Convert.ToByte(s.Substring(n, 2), 16);
            }
            return r;
        }
    }

    public class BigIntStruct
    {
        public int DB = 28;

        public int DM = 268435455;

        public  int DV = 268435456;

        private const int F1 = 24;

        private const int F2 = 4;

        private const long FV = 4503599627370496;

        public AutoExpandArray Data { get; set; } = new();
        public int t { get; set; }
        public int s { get; set; }

        public void FromString(byte[] e)
        {
            int n = 8;
            t = 0;
            s = 0;
            int i = 0;
            for (int r = e.Length - 1; r >= 0; r--)
            {
                var l = (e[r] & 0xFF);

                if (i == 0)
                {
                    Data.Add(l);
                    t++;
                }
                else if (i + n > DB)
                {
                    int last = Data[t - 1];
                    last |= (l & ((1 << (DB - i)) - 1)) << i;
                    Data[t - 1] = last;
                    Data.Add((l >> (DB - i)));
                    t++;
                }
                else
                {
                    int last = Data[t - 1];
                    last |= l << i;
                    Data[t - 1] = last;
                }

                i += n;
                if (i >= DB)
                    i -= DB;
            }
        }

        public string ToString(int e)
        {

            int t=0;
            if (e == 16)
                t = 4;
            else if (e == 8)
                t = 3;
            else if (e == 2)
                t = 1;
            else if (e == 32)
                t = 5;
            else if (e == 4)
                t = 2;

            int r = (1 << t) - 1;
            bool started = false;
            string result = "";

            int a = this.t;
            int l = DB - a * DB % t;
            int n;

            if (--a >= 0)
            {
                if (l < DB)
                {
                    n = Data[a] >> l;
                    if (n > 0)
                    {
                        started = true;
                        result = n.ToString("x");
                    }
                }

                while (a >= 0)
                {
                    if (l < t)
                    {
                        n = (Data[a] & ((1 << l) - 1)) << (t - l);
                        if (--a >= 0)
                            n |= Data[a] >> (l += DB - t);
                    }
                    else
                    {
                        n = (Data[a] >> (l -= t)) & r;
                        if (l <= 0)
                        {
                            l += DB;
                            a--;
                        }
                    }

                    if (n > 0) started = true;
                    if (started) result += n.ToString("x");
                }
            }

            return started ? result : "0";
        }
        public void MultiplyTo(BigIntStruct e, BigIntStruct t)
        {
            var n = this;
            var r = e;
            int o = n.t;

            t.t = o + r.t;

            while (--o >= 0)
                t.Data[o] = 0;

            for (o = 0; o < r.t; ++o)
            {
                t.Data[o + n.t] = n.Am(0, r.Data[o], t, o, 0, n.t); // 类似 JS 的 am()
            }

            t.s = 0;
            t.Clamp();

            if (this.s != e.s)
                ;
        }
        public void DrShiftTo(int e, BigIntStruct t)
        {
            // 从 this 的第 e 项开始复制，偏移到 t 的 0 开始
            for (int n = e; n < this.t; ++n)
            {
                t.Data[n - e] = Data[n];
            }

            t.t = Math.Max(this.t - e, 0);
            t.s = this.s;
        }
        public void Clamp()
        {
            int e = this.s & DM;
            while (this.t > 0 && Data[this.t - 1] == e)
            {
                this.t--;
            }
        }


        public BigIntStruct modPowInt(int e, BigIntStruct t)
        {
            var n = new V();
            n.v(t);
            return exp(e,n);
        }



        public BigIntStruct exp(int e, V t)
        {
            var o= t.Convert(this);
            var i = h(e) - 1;
            var n = new BigIntStruct();
            var r = new BigIntStruct();
            o.CopyTo(n);  // 初始化 n = o
            while (i-- > 0)  // 假设你提前定义了 bitLength
            {
                t.SqrTo(n, r);  // r = n^2 mod m

                if ((e & (1 << i)) > 0)
                {
                    t.MulTo(r, o, n);  // n = r * o mod m
                }
                else
                {
                    var s = n;
                    n = r;
                    r = s;
                }
            }

            return t.Revert(n);

        }


        public void SquareTo(BigIntStruct e)
        {
            var t = this; // 获取绝对值
            int n = e.t = 2 * t.t;

            // 初始化 e 所有位为 0
            for (int i = 0; i < n; i++)
                e.Data[i] = 0;

            // 主循环，进行平方操作
            for (n = 0; n < t.t - 1; ++n)
            {
                var r = t.Am(n, t.Data[n], e, 2 * n, 0, 1);
                int j = n + t.t;

                // += t.Am(i + 1, 2 * t[i], e, 2 * i + 1, r, t.t - i - 1)
                e.Data[j] += t.Am(n + 1, 2 * t.Data[n], e, 2 * n + 1, r, t.t - n - 1);

                // 处理进位
                if (e.Data[j] >= t.DV)
                {
                    e.Data[j] -= t.DV;
                    e.Data[j + 1] = 1;
                }
            }

            // 最后一位单独计算
            if (e.t > 0)
            {
                e.Data[e.t - 1] += t.Am(n, t.Data[n], e, 2 * n, 0, 1);
            }
            e.s = 0;
            e.Clamp();
        }
        public void CopyTo(BigIntStruct e)
        {
            for (int t = this.t - 1; t >= 0; t--)
                e.Data[t] = Data[t];  // 假设用 data[] 存储每个“位段”
            e.t = this.t;
            e.s = this.s;
        }
        public void divRemTo(BigIntStruct e, BigIntStruct n)
        {
            var r = e;
            var o = this;
            int s = this.s, c = e.s;
            var u = DB - h(r.Data[r.t - 1]);

            var i = new BigIntStruct();
            r.LShiftTo(u, i);
            o.LShiftTo(u, n);
            var d = i.t;
            var f = i.Data[d - 1];
            if (f == 0) return;

            long p = (long)f << F1;
            if (d > 1)
                p += (i.Data[d - 2] >> F2);

            var m  = (double)FV / p;
            var v = (double)(1 << F1) / p;
            var g = 1 << F2;
            ;
            int y = n.t;
            int b = y - d;
            var w = new BigIntStruct();
            i.DLShiftTo(b, w);
            if (n.CompareTo(w) >= 0)
            {
                n.Data[n.t++] = 1;
                n.SubTo(w, n);
            }
            var ONE = new BigIntStruct();
            ONE.Data[0] = 1;
            ONE.t=1;
            ONE.DLShiftTo(d, w);
            w.SubTo(i,i);
            while (i.t < d) i.Data[i.t++] = 0;

            for (; --b >= 0;)
            {
                int qhat;
                y--;
                if (n.Data[y] == f)
                    qhat = DM;
                else
                {

                    qhat = (int)Math.Floor(n.Data[y] * m + (n.Data[y - 1] + g) * v);
                }

                if ((n.Data[y] += i.Am(0, qhat, n, b, 0, d)) < qhat)
                {
                    i.DLShiftTo(b, w);
                    n.SubTo(w, n);
                    while (n.Data[y] < --qhat)
                        n.SubTo(w, n);
                }
            }

            n.t = d;
            n.Clamp();
            if (u>0)
            {
                n.RShiftTo(u, n);
            }
        }

        public void RShiftTo(int e, BigIntStruct t)
        {
            t.s = this.s;
            int n = e / DB;
            if (n >= this.t)
            {
                t.t = 0;
            }
            else
            {
                int r = e % DB;
                int o = DB - r;
                int i = (1 << r) - 1;

                t.Data[0] = Data[n] >> r;

                for (int a = n + 1; a < this.t; ++a)
                {
                    t.Data[a - n - 1] |= (Data[a] & i) << o;
                    t.Data[a - n] = Data[a] >> r;
                }

                if (r > 0)
                {
                    t.Data [this.t - n - 1] |= (this.s & i) << o;
                }

                t.t = this.t - n;
                t.Clamp();
            }
        }
        public int Am(int e, int t, BigIntStruct n, int r, int o, int i)
        {
            int a = t & 0x3FFF;        // 16383，取低14位
            int l = t >> 14;           // 高14位

            while (--i >= 0)
            {
                int s = Data[e] & 0x3FFF;     // this[e] 低14位
                int c = Data[e++] >> 14;      // this[e] 高14位

                int u = l * s + c * a;

                int temp = a * s + ((u & 0x3FFF) << 14) + n.Data[r] + o;
                o = (temp >> 28) + (u >> 14) + l * c;

                n.Data[r++] = temp & 0xFFFFFFF; // 2^28 - 1 = 268435455
            }

            return o;
        }
        public void SubTo(BigIntStruct e, BigIntStruct t)
        {
            int n = 0, r = 0;
            int o = Math.Min(e.t, this.t);

            // 主减法循环
            while (n < o)
            {
                r += this.Data[n] - e.Data[n];
                t.Data[n++] = r & DM;
                r >>= DB;
            }

            // 如果 this 更长
            if (e.t < this.t)
            {
                for (r -= e.s; n < this.t;)
                {
                    r += this.Data[n];
                    t.Data[n++] = r & DM;
                    r >>= DB;
                }
                r += this.s;
            }
            // 如果 e 更长或相等
            else
            {
                for (r += this.s; n < e.t;)
                {
                    r -= e.Data[n];
                    t.Data[n++] = r & DM;
                    r >>= DB;
                }
                r -= e.s;
            }

            // 设置符号
            t.s = r < 0 ? -1 : 0;

            // 处理最后一位进位或借位
            if (r < -1)
                t.Data[n++] = DV + r;
            else if (r > 0)
                t.Data[n++] = r;

            t.t = n;
            t.Clamp();
        }
        public int CompareTo(BigIntStruct e)
        {
            int t = this.s - e.s;
            if (t != 0)
                return t;

            int n = this.t;
            t = n - e.t;
            if (t != 0)
                return t;

            while (--n >= 0)
            {
                t = Data[n] - e.Data[n];
                if (t != 0)
                    return t;
            }

            return 0;
        }

        public void DLShiftTo(int e, BigIntStruct t)
        {
            int n;

            // 数据整体左移 e 位（单位为“数值块”而非位）
            for (n = this.t - 1; n >= 0; --n)
                t.Data[n + e] = Data[n];

            // 低位补 0
            for (n = e - 1; n >= 0; --n)
                t.Data[n] = 0;

            // 更新目标对象的元数据
            t.t = this.t + e;
            t.s = this.s;
        }

        public void LShiftTo(int e, BigIntStruct t)
        {
            int r = e % DB;
            int o = DB - r;
            int i = (1 << o) - 1;
            int a = e / DB;
            int l = (this.s << r) & DM;

            int n;

            // 高位部分左移
            for (n = Data.Length - 1; n >= 0; --n)
            {
                t.Data[n + a + 1] = (Data[n] >> o) | l;
                l = (Data[n] & i) << r;
            }

            // 补零
            for (n = a - 1; n >= 0; --n)
            {
                t.Data[n] = 0;
            }

            t.Data[a] = l;
            t.t = this.t + a+1;
            t.s = this.s;
            t.Clamp();
        }

        public int InvDigit()
        {
            
            if (t < 1)
                return 0;

            int e = Data[0];

            if ((e & 1) == 0)
                return 0;

            int inv = e & 3; // 相当于 e % 4

            // Newton-Raphson 迭代逐步提升精度
            inv = (inv * (2 - ((e & 15) * inv))) & 15;
            inv = (inv * (2 - ((e & 255) * inv))) & 255;
            inv = (inv * (2 - ((e & 0xFFFF) * inv & 0xFFFF))) & 0xFFFF;
            inv = (inv * (2 - (e * inv % DV))) % DV;

            return inv > 0 ? DV - inv : -inv;
        }
        int h(int e)
        {
            int n = 1;
            int t;

            if ((t = e >> 16) != 0)
            {
                e = t;
                n += 16;
            }
            if ((t = e >> 8) != 0)
            {
                e = t;
                n += 8;
            }
            if ((t = e >> 4) != 0)
            {
                e = t;
                n += 4;
            }
            if ((t = e >> 2) != 0)
            {
                e = t;
                n += 2;
            }
            if ((t = e >> 1) != 0)
            {
                e = t;
                n += 1;
            }

            return n;
        }
    }
}
