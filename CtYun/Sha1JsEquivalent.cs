using System;
using System.Text;

public class Sha1JsEquivalent
{
    public static StringBuilder S(string input)
    {
        var blocks = k(input);
        var hashed = O(blocks, input.Length * 8);
        return A(hashed);
    }

    private static AutoExpandArray k(string e)
    {
        var t = new AutoExpandArray();
        for (int n = 0; n < 8 * e.Length; n += 8)
        {
            int charCode = (int)e[n / 8]; // 等价于 charCodeAt
            int shift = 24 - (n % 32);
            t[n >> 5] |= (charCode & 0xFF) << shift;
        }

        return t;
    }

    private static StringBuilder A(int[] e)
    {
        StringBuilder sb = new StringBuilder();
        for (int n = 0; n < 32 * e.Length; n += 8)
        {
            sb.Append((char)((e[n >> 5] >> (24 - n % 32)) & 0xFF));
        }
        return sb;
    }

    private static int[] O(AutoExpandArray e, int t)
    {
  
        e[t >> 5] |= 0x80 << (24 - (t % 32));
        //int newLength = 16 + (((t + 64) >> 9) << 4);
        //Array.Resize(ref e, newLength);
        e[15 + (((t + 64) >> 9) << 4)] = t;

        int[] n = new int[80];
        int r = 0x67452301;
        int o = unchecked((int)0xEFCDAB89);
        int i = unchecked((int)0x98BADCFE);
        int a = 0x10325476;
        int l = unchecked((int)0xC3D2E1F0);

        for (int s = 0; s < e.Length; s += 16)
        {
            int c = r, u = o, d = i, f = a, p = l;

            for (int h = 0; h < 80; h++)
            {
                if (h < 16)
                    n[h] = e[s + h];
                else
                    n[h] = I(n[h - 3] ^ n[h - 8] ^ n[h - 14] ^ n[h - 16], 1);

                int m = T(T(I(r, 5), x(h, o, i, a)), T(T(l, n[h]), N(h)));
                l = a;
                a = i;
                i = I(o, 30);
                o = r;
                r = m;
            }

            r = T(r, c);
            o = T(o, u);
            i = T(i, d);
            a = T(a, f);
            l = T(l, p);
        }

        return new int[] { r, o, i, a, l };
    }

    private static int x(int e, int t, int n, int r)
    {
        if (e < 20) return (t & n) | (~t & r);
        if (e < 40) return t ^ n ^ r;
        if (e < 60) return (t & n) | (t & r) | (n & r);
        return t ^ n ^ r;
    }

    private static int N(int e)
    {
        if (e < 20) return 0x5A827999;
        if (e < 40) return 0x6ED9EBA1;
        if (e < 60) return unchecked((int)0x8F1BBCDC);
        return unchecked((int)0xCA62C1D6);
    }

    private static int T(int x, int y)
    {
        uint ux = (uint)x;
        uint uy = (uint)y;
        return (int)(ux + uy);
    }

    private static int I(int e, int t)
    {
        return (int)(((uint)e << t) | ((uint)e >> (32 - t)));
    }
}
public class AutoExpandArray
{
    public int Length { get { return data.Count;  } }

    private List<int> data = new List<int>();

    public int this[int index]
    {
        get
        {
            if (index >= data.Count)
                return 0;
            return data[index];
        }
        set
        {
            while (index >= data.Count)
                data.Add(0);
            data[index] = value;
        }
    }

    public void Add(int v)
    {
        data.Add(v);
    }

    public List<int> ToList()
    {
        return data.ToList();
    }
    public int[] ToArray()
    {
        return data.ToArray();
    }
}