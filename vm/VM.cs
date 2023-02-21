using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace vm
{
    internal class VM
    {

        const int NUM_REGISTER = 256;
        const int STACK_SIZE = 1024;
        const int RAM_SIZE = 65536;

        public enum eResultType { ok, insufficient_gas, illegal_opcode, stack_overflow, stack_underflow, access_outside_ram, divide_by_zero, store_to_immediate, illegal_destination, access_outside_rom };

        public enum eOpcodes
        {
            MOVE = 0,
            PUSH = 1,
            POP = 2,
            ADD = 3,
            SUB = 4,
            AND = 5,
            XOR = 6,
            OR = 7,
            NOT = 8,
            MUL = 9,
            DIV = 10,
            INC = 11,
            DEC = 12,
            ROL = 13,
            ROR = 14,
            CMP = 15,
            SET = 16,
            CLR = 17,
            CALL = 18,
            RETURN = 19,
            JZ = 20,
            JNZ = 21,
            JLT = 22,
            JLTE = 23,
            JGT = 24,
            JGTE = 25,
            END = 26,
            SEND = 27,
            STORE = 28,
            READ = 29,
            BALANCE = 30,
            DATA = 31,
            DYN = 32,
            SENDER = 33,
            EXECUTE = 34,
            PREVHASH = 35,
            EXECCOUNTC = 36,
            EXECCOUNTB = 37,
            JMP = 38
        };

        public enum eSourceDestType {
            IMMEDIATE = 0,
            REGISTER = 1,
            MEMORY = 2,
            INDIRECT = 3
        };

        public enum eCompareFlags
        {
            JZ = 0,
            JNZ = 1,
            JLT = 2,
            JLTE = 3,
            JGT = 4,
            JGTE = 5,

            eCompareFlagsSize = 6
        };

        bool[] compareFlags = new bool[(int)eCompareFlags.eCompareFlagsSize];

        public enum eSourceDest
        {
            SOURCE, DEST
        };

        Int64[] register = new Int64[NUM_REGISTER];
        Int64[] stack = new Int64[STACK_SIZE];
        Int64[] ram = new Int64[RAM_SIZE];

        int PC = 0;     //program counter
        int SP = 0;     //stack pointer



        public eResultType Exec(string contractAddress, string[] inParams, Int64 amountSent, Int64 gas )
        {

            Contract contract = LoadContract(contractAddress);

            //todo - find entry point for called function

            eResultType error = eResultType.ok;

            bool done = false;
            while ((!done) && (gas > 0) && (error == eResultType.ok))
            {
                if (PC >= contract.byteCodeLen)
                    error = eResultType.access_outside_rom;
                else
                {
                    if (contract.byteCode[PC] == (byte)eOpcodes.END)
                        done = true;
                    else
                    {
                        error = RunNextOpcode(contract);
                    }
                }
            }

            contract.execCountLifetime++;
            SaveContract(contractAddress, contract);

            return error;

        }


        eResultType RunNextOpcode (Contract contract )
        {
            eResultType error = eResultType.ok;

            if (contract.byteCode[PC] == (byte)eOpcodes.MOVE)
            {
                Int64 value = 0;
                PC++;
                byte opcode2 = contract.byteCode[PC];
                error = Read(opcode2, eSourceDest.SOURCE, contract, ref value);
                if (error == eResultType.ok)
                    error = WriteDest(opcode2, contract, value);
            }

            else if (contract.byteCode[PC] == (byte)eOpcodes.PUSH)
            {
                Int64 value = 0;
                PC++;
                byte opcode2 = contract.byteCode[PC];
                error = Read(opcode2, eSourceDest.SOURCE, contract, ref value);
                if (error == eResultType.ok)
                {
                    if (SP < STACK_SIZE)
                    {
                        stack[SP] = value;
                        SP++;
                    }
                    else
                        error = eResultType.stack_overflow;
                }
            }

            else if (contract.byteCode[PC] == (byte)eOpcodes.POP)
            {
                Int64 value = 0;
                if (SP > 0)
                {
                    SP--;
                    value = stack[SP];
                    byte opcode2 = contract.byteCode[PC];
                    error = WriteDest(opcode2, contract, value);
                }
                else
                    error = eResultType.stack_underflow;
            }

            else if (IsMathOpcode(contract.byteCode[PC]))
            {
                Int64 value1 = 0;
                Int64 value2 = 0;
                byte opcode1 = contract.byteCode[PC];
                PC++;
                byte opcode2 = contract.byteCode[PC];
                byte opcode3 = 0;
                if ((opcode1 == (byte)eOpcodes.ROL) || (opcode1 == (byte)eOpcodes.ROR) || (opcode1 == (byte)eOpcodes.SET) || (opcode1 == (byte)eOpcodes.CLR))
                {
                    opcode3 = contract.byteCode[PC];
                    PC++;
                }

                error = Read(opcode2, eSourceDest.SOURCE, contract, ref value1);
                if (error == eResultType.ok)
                {
                    error = Read(opcode2, eSourceDest.DEST, contract, ref value2);      //note - not used by unary operators NOT, INC and DEC
                    if (error == eResultType.ok)
                    {
                        switch (opcode1)
                        {
                            case (byte)eOpcodes.ADD:
                                error = WriteDest(opcode2, contract, value1 + value2);
                                break;

                            case (byte)eOpcodes.SUB:
                                error = WriteDest(opcode2, contract, value1 - value2);
                                break;

                            case (byte)eOpcodes.AND:
                                error = WriteDest(opcode2, contract, value1 & value2);
                                break;

                            case (byte)eOpcodes.XOR:
                                error = WriteDest(opcode2, contract, value1 ^ value2);
                                break;

                            case (byte)eOpcodes.OR:
                                error = WriteDest(opcode2, contract, value1 | value2);
                                break;

                            case (byte)eOpcodes.NOT:
                                error = WriteDest(opcode2, contract, ~value1);
                                break;

                            case (byte)eOpcodes.MUL:
                                error = WriteDest(opcode2, contract, value1 * value2);
                                break;

                            case (byte)eOpcodes.DIV:
                                if (value2 == 0)
                                    error = eResultType.divide_by_zero;
                                else
                                    error = WriteDest(opcode2, contract, value1 / value2);
                                break;

                            case (byte)eOpcodes.INC:
                                error = WriteDest(opcode2, contract, value1 + 1);
                                break;

                            case (byte)eOpcodes.DEC:
                                error = WriteDest(opcode2, contract, value1 - 1);
                                break;

                            case (byte)eOpcodes.ROL:

                                error = WriteDest(opcode2, contract, value1 << (int)opcode3);
                                break;

                            case (byte)eOpcodes.ROR:
                                error = WriteDest(opcode2, contract, value1 >> (int)opcode3);
                                break;

                            case (byte)eOpcodes.SET:
                            case (byte)eOpcodes.CLR:
                                byte[] bValue1 = BitConverter.GetBytes(value1);
                                BitArray ba = new BitArray(bValue1);
                                byte[] bResult = new byte[8];
                                ba.Set(opcode3, (opcode1 == (byte)eOpcodes.SET));
                                ba.CopyTo(bResult, 0);
                                Int64 result = BitConverter.ToInt64(bResult);
                                error = WriteDest(opcode2, contract, result);
                                break;

                            case (byte)eOpcodes.CMP:
                                //super inefficient, but easy to read/understand
                                compareFlags[(int)eCompareFlags.JZ] = (value1 == value2);
                                compareFlags[(int)eCompareFlags.JNZ] = (value1 != value2);
                                compareFlags[(int)eCompareFlags.JLT] = (value1 < value2);
                                compareFlags[(int)eCompareFlags.JLTE] = (value1 <= value2);
                                compareFlags[(int)eCompareFlags.JGT] = (value1 > value2);
                                compareFlags[(int)eCompareFlags.JGTE] = (value1 >= value2);
                                break;

                        }
                    }
                        
                }
            }

            else if (IsJumpOpcode(contract.byteCode[PC]))
            {
                byte opcode1 = contract.byteCode[PC];

                Int64 value2 = 0;
                PC++;
                byte opcode2 = contract.byteCode[PC];
                error = Read(opcode2, eSourceDest.DEST, contract, ref value2);  //only use lower 16 bits of whatever we get
                if (error == eResultType.ok)
                {
                    int offset = (int)(value2 & 0b1111111111111111);
                    if (
                       ((opcode1 == (byte)eOpcodes.JZ) && (compareFlags[(byte)eCompareFlags.JZ])) ||
                       ((opcode1 == (byte)eOpcodes.JNZ) && (compareFlags[(byte)eCompareFlags.JNZ])) ||
                       ((opcode1 == (byte)eOpcodes.JLT) && (compareFlags[(byte)eCompareFlags.JLT])) ||
                       ((opcode1 == (byte)eOpcodes.JLTE) && (compareFlags[(byte)eCompareFlags.JLTE])) ||
                       ((opcode1 == (byte)eOpcodes.JGT) && (compareFlags[(byte)eCompareFlags.JGT])) ||
                       ((opcode1 == (byte)eOpcodes.JGTE) && (compareFlags[(byte)eCompareFlags.JGTE])) ||
                       (opcode1 == (byte)eOpcodes.JMP)
                        )
                        PC = offset;
                }
            }


            return error;
        }
            


        bool IsMathOpcode ( byte opcode )
        {
            return (
                (opcode == (byte)eOpcodes.ADD) ||
                (opcode == (byte)eOpcodes.SUB) ||
                (opcode == (byte)eOpcodes.AND) ||
                (opcode == (byte)eOpcodes.XOR) ||
                (opcode == (byte)eOpcodes.OR) ||
                (opcode == (byte)eOpcodes.NOT) ||
                (opcode == (byte)eOpcodes.MUL) ||
                (opcode == (byte)eOpcodes.DIV) ||
                (opcode == (byte)eOpcodes.INC) ||
                (opcode == (byte)eOpcodes.DEC) ||
                (opcode == (byte)eOpcodes.ROL) ||
                (opcode == (byte)eOpcodes.ROR) ||
                (opcode == (byte)eOpcodes.SET) ||
                (opcode == (byte)eOpcodes.CLR) ||
                (opcode == (byte)eOpcodes.CMP) 
                );
        }


        bool IsJumpOpcode(byte opcode)
        {
            return (
                (opcode == (byte)eOpcodes.JZ) ||
                (opcode == (byte)eOpcodes.JNZ) ||
                (opcode == (byte)eOpcodes.JLT) ||
                (opcode == (byte)eOpcodes.JLTE) ||
                (opcode == (byte)eOpcodes.JGT) ||
                (opcode == (byte)eOpcodes.JGTE) 
                );
        }

        eResultType Read ( byte opcode, eSourceDest which, Contract contract, ref Int64 result )
        {
            byte source;
            
            if (which == eSourceDest.SOURCE)
                source = (byte)((opcode & 0b00001100) >> 2);
            else
                source = (byte)(opcode & 0b00000011);

            eResultType error = eResultType.ok;

            if (source == (byte)eSourceDestType.IMMEDIATE)
            {
                PC += 2;

                error = ReadInt64(contract, PC, ref result );

                if (error == eResultType.ok)
                    PC += 8;
            }

            else if (source == (byte)eSourceDestType.REGISTER)
            {
                PC += 2;

                if (PC >= contract.byteCodeLen)
                    error = eResultType.access_outside_rom;
                else
                {
                    result = register[contract.byteCode[PC]];
                    PC++;
                }
            }

            else if (source == (byte)eSourceDestType.MEMORY)
            {
                PC += 2;

                if (PC + 1 >= contract.byteCodeLen)
                    error = eResultType.access_outside_rom;
                else
                {
                    result = ram[contract.byteCode[PC] << 8 + contract.byteCode[PC + 1]];
                    PC += 2;
                }
            }

            else if (source == (byte)eSourceDestType.INDIRECT)
            {
                PC += 2;

                if (PC >= contract.byteCodeLen)
                    error = eResultType.access_outside_rom;
                else
                {
                    UInt16 offset = (UInt16)(register[contract.byteCode[PC]] & 0b1111111111111111);
                    result = ram[offset];
                    PC += 1;
                }
            }

            return error;
        }


        eResultType WriteDest(byte opcode, Contract contract, Int64 value)
        {
            byte dest = (byte)(opcode & 0b00000011);

            eResultType error = eResultType.ok;

            if (dest == (byte)eSourceDestType.IMMEDIATE)
            {
                error = eResultType.store_to_immediate;
            }

            else if (dest == (byte)eSourceDestType.REGISTER)
            {
                PC += 2;

                if (PC >= contract.byteCodeLen)
                    error = eResultType.access_outside_rom;
                else
                {
                    register[contract.byteCode[PC]] = value;
                    PC++;
                }
            }

            else if (dest == (byte)eSourceDestType.MEMORY)
            {
                PC += 2;

                if (PC + 1 >= contract.byteCodeLen)
                    error = eResultType.access_outside_rom;
                else
                {
                    ram[contract.byteCode[PC] << 8 + contract.byteCode[PC + 1]] = value;
                    PC += 2;
                }
            }

            else if (dest == (byte)eSourceDestType.INDIRECT)
            {
                PC += 2;

                if (PC >= contract.byteCodeLen)
                    error = eResultType.access_outside_rom;
                else
                {
                    UInt16 offset = (UInt16)(register[contract.byteCode[PC]] & 0b1111111111111111);
                    ram[offset] = value;
                    PC += 1;
                }
            }

            return error;
        }

        eResultType ReadInt64(Contract contract, int ptr, ref Int64 result)
        {
            eResultType error = eResultType.ok;

            if (ptr + 8 >= contract.byteCodeLen)
                error = eResultType.access_outside_rom;
            else
                result = BitConverter.ToInt64(contract.byteCode, PC);

            return error;
        }


        public void SaveContract(string contractAddress, Contract contract)
        {
            if (ValidateContract(contract))
                File.WriteAllText(contractAddress + ".dat", JsonConvert.SerializeObject(contract));
        }

        public Contract LoadContract(string contractAddress)
        {
            Contract contract = new Contract();
            JsonConvert.DeserializeObject<Contract>(File.ReadAllText(contractAddress + ".dat"));
            return contract;
        }


        public bool ValidateContract ( Contract contract )
        {
            try
            {

                foreach (Contract.Function f in contract.function)
                {
                    if (!ValidateOpcodes(contract, f.offset))
                        return false;
                }


            }
            catch(Exception)
            {
                return false;
            }

            return true;
        }

        public bool ValidateOpcodes ( Contract contract, int offset )
        {
            bool done = false;
            bool error = false;
            int ptr = offset;

            while ((!done) && (!error))
            {
                if (ptr >= contract.byteCodeLen)
                    error = true;
                else
                {
                    if (contract.byteCode[ptr] < 38)
                    {
                        //no additional bytes
                        if (contract.byteCode[ptr] == (byte)eOpcodes.RETURN)
                            ptr++;

                        //end of routine
                        else if (contract.byteCode[ptr] == (byte)eOpcodes.END)
                            done = true;

                        //source and destination
                        else if (
                            (
                            (contract.byteCode[ptr] == (byte)eOpcodes.MOVE) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.ADD) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.SUB) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.AND) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.XOR) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.NOT) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.MUL) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.OR) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.DIV) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.INC) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.DEC) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.ROR) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.ROL) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.CMP) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.SET) ||
                            (contract.byteCode[ptr] == (byte)eOpcodes.CLR)
                            )
                            &&
                            (!ValidateSourceAndDest(contract.byteCode, ptr + 1)))

                            error = true;

                        //source only
                        else if (
                                (contract.byteCode[ptr] == (byte)eOpcodes.PUSH) &&
                                (!ValidateSourceOnly(contract.byteCode, ptr + 1)
                            ))
                            error = true;

                        //dest only
                        else if (
                                (
                                (contract.byteCode[ptr] == (byte)eOpcodes.CALL) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.POP) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.JZ) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.JNZ) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.JLT) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.JLTE) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.JGT) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.JGTE) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.BALANCE) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.DYN) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.EXECCOUNTC) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.EXECCOUNTB)
                                ) &&
                                (!ValidateDestOnly(contract.byteCode, ptr + 1)
                            ))
                            error = true;

                        //memory dest only
                        else if (
                                (
                                (contract.byteCode[ptr] == (byte)eOpcodes.DATA) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.SENDER) ||
                                (contract.byteCode[ptr] == (byte)eOpcodes.PREVHASH) 
                                ) &&
                                (!ValidateMemDestOnly(contract.byteCode, ptr + 1)
                            ))
                            error = true;

                        //send
                        else if (
                                (contract.byteCode[ptr] == (byte)eOpcodes.SEND) &&
                                (!ValidateSend(contract.byteCode, ptr + 1)
                            ))
                            error = true;

                        //read
                        else if (
                                (contract.byteCode[ptr] == (byte)eOpcodes.READ) &&
                                (!ValidateRead(contract.byteCode, ptr + 1)
                            ))
                            error = true;

                        //store
                        else if (
                                (contract.byteCode[ptr] == (byte)eOpcodes.STORE) &&
                                (!ValidateStore(contract.byteCode, ptr + 1)
                            ))
                            error = true;



                    }
                    else
                        error = true;
                }
            }

            return error;
        }



        public bool ValidateSourceAndDest(byte[] byteCode, int ptr)
        {
            return true;
        }


        public bool ValidateSourceOnly(byte[] byteCode, int ptr)
        {
            return true;
        }

        public bool ValidateDestOnly(byte[] byteCode, int ptr)
        {
            return true;
        }

        public bool ValidateMemDestOnly(byte[] byteCode, int ptr)
        {
            return true;
        }

        public bool ValidateSend(byte[] byteCode, int ptr)
        {
            return true;
        }

        public bool ValidateRead(byte[] byteCode, int ptr)
        {
            return true;
        }


        public bool ValidateStore(byte[] byteCode, int ptr)
        {
            return true;
        }

    }
}
