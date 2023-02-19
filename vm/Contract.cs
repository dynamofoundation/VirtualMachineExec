using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace vm
{
    internal class Contract
    {

        internal class Function
        {
            internal string name;
            internal int offset;
        }

        internal Function[] function;

        internal byte[] byteCode;
        internal int byteCodeLen;

        internal string owner;
        internal Int64 balance;
        internal Int64 execCountLifetime;


    }
}
