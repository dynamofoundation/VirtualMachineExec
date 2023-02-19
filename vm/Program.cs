using Newtonsoft.Json.Linq;
using System.Collections;

namespace vm
{
    internal class Program
    {
        static void Main(string[] args)
        {

            Int64 value1 = 100;
            byte[] bValue1 = BitConverter.GetBytes(value1);
            BitArray ba = new BitArray(bValue1);

            ba.Set(63, true);
            ba.Set(32, true);

            ba.CopyTo(bValue1, 0);

            Int64 result = BitConverter.ToInt64(bValue1);


        }
    }
}