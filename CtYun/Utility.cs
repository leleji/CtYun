using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CtYun
{
    internal class Utility
    {
        public static void WriteLine(ConsoleColor consolecolor, object value)
        {
            Console.ForegroundColor = consolecolor;
            Console.WriteLine(Time() + value);
        }
        private static string Time()
        {
            return "[" + DateTime.Now.ToString("HH:mm:ss.ff") + "] ";
        }
    }
}
