// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using ILCompiler.Reflection.ReadyToRun;
using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace R2RDump
{
    public class CoreDisTools
    {
        private const string _dll = "coredistools";

        public enum TargetArch
        {
            Target_Host, // Target is the same as host architecture
            Target_X86,
            Target_X64,
            Target_Thumb,
            Target_Arm64
        };

        [DllImport(_dll)]
        public static extern IntPtr InitBufferedDisasm(TargetArch Target);

        [DllImport(_dll)]
        public static extern void DumpCodeBlock(IntPtr Disasm, ulong Address, IntPtr Bytes, int Size);

        [DllImport(_dll)]
        [return: MarshalAs(UnmanagedType.I4)]
        public static extern int DumpInstruction(IntPtr Disasm, ulong Address, IntPtr Bytes, int Size);

        [DllImport(_dll)]
        public static extern IntPtr GetOutputBuffer();

        [DllImport(_dll)]
        public static extern void ClearOutputBuffer();

        [DllImport(_dll)]
        public static extern void FinishDisasm(IntPtr Disasm);

        public unsafe static int GetInstruction(IntPtr Disasm, RuntimeFunction rtf, int imageOffset, int rtfOffset, byte[] image, out string instr)
        {
            int instrSize;
            fixed (byte* p = image)
            {
                IntPtr ptr = (IntPtr)(p + imageOffset + rtfOffset);
                instrSize = DumpInstruction(Disasm, (ulong)(rtf.StartAddress + rtfOffset), ptr, rtf.Size);
            }
            IntPtr pBuffer = GetOutputBuffer();
            instr = Marshal.PtrToStringAnsi(pBuffer);
            return instrSize;
        }

        public static IntPtr GetDisasm(Machine machine)
        {
            TargetArch target;
            switch (machine)
            {
                case Machine.Amd64:
                    target = TargetArch.Target_X64;
                    break;
                case Machine.I386:
                    target = TargetArch.Target_X86;
                    break;
                case Machine.Arm64:
                    target = TargetArch.Target_Arm64;
                    break;
                case Machine.ArmThumb2:
                    target = TargetArch.Target_Thumb;
                    break;
                default:
                    R2RDump.WriteWarning($"{machine} not supported on CoreDisTools");
                    return IntPtr.Zero;
            }
            return InitBufferedDisasm(target);
        }
    }

    /// <summary>
    /// Helper class for converting machine instructions to textual representation.
    /// </summary>
    public class Disassembler : IDisposable
    {
        /// <summary>
        /// Indentation of instruction mnemonics in naked mode with no offsets.
        /// </summary>
        private const int NakedNoOffsetIndentation = 4;

        /// <summary>
        /// Indentation of instruction mnemonics in naked mode with offsets.
        /// </summary>
        private const int NakedWithOffsetIndentation = 11;

        /// <summary>
        /// R2R reader is used to access architecture info, the PE image data and symbol table.
        /// </summary>
        private readonly ReadyToRunReader _reader;

        /// <summary>
        /// Dump options
        /// </summary>
        private readonly DumpOptions _options;

        /// <summary>
        /// COM interface to the native disassembler in the CoreDisTools.dll library.
        /// </summary>
        private readonly IntPtr _disasm;

        /// <summary>
        /// Indentation of instruction mnemonics.
        /// </summary>
        public int MnemonicIndentation { get; private set; }

        /// <summary>
        /// Indentation of instruction mnemonics.
        /// </summary>
        public int OperandsIndentation { get; private set; }

        /// <summary>
        /// Store the R2R reader and construct the disassembler for the appropriate architecture.
        /// </summary>
        /// <param name="reader"></param>
        public Disassembler(ReadyToRunReader reader, DumpOptions options)
        {
            _reader = reader;
            _options = options;
            _disasm = CoreDisTools.GetDisasm(_reader.Machine);
            SetIndentations();
        }

        /// <summary>
        /// Shut down the native disassembler interface.
        /// </summary>
        public void Dispose()
        {
            if (_disasm != IntPtr.Zero)
            {
                CoreDisTools.FinishDisasm(_disasm);
            }
        }

        /// <summary>
        /// Set indentations for mnemonics and operands.
        /// </summary>
        private void SetIndentations()
        {
            if (_options.Naked)
            {
                MnemonicIndentation = _options.HideOffsets ? NakedNoOffsetIndentation : NakedWithOffsetIndentation;
            }
            else
            {
                // The length of the byte dump starting with the first hexadecimal digit and ending with the final space
                int byteDumpLength = _reader.Machine switch
                {
                    // Most instructions are no longer than 7 bytes. CorDisasm::dumpInstruction always pads byte dumps
                    // to 7 * 3 characters; see https://github.com/dotnet/llilc/blob/master/lib/CoreDisTools/coredistools.cpp.
                    Machine.I386 => 7 * 3,
                    Machine.Amd64 => 7 * 3,

                    // Instructions are either 2 or 4 bytes long
                    Machine.ArmThumb2 => 4 * 3,

                    // Instructions are dumped as 4-byte hexadecimal integers
                    Machine.Arm64 => 4 * 2 + 1,

                    _ => throw new NotImplementedException()
                };

                MnemonicIndentation = NakedWithOffsetIndentation + byteDumpLength;
            }

            // This leaves 7 characters for the mnemonic
            OperandsIndentation = MnemonicIndentation + 8;
        }

        /// <summary>
        /// Append spaces to the string builder to achieve at least the given indentation.
        /// </summary>
        private static void EnsureIndentation(StringBuilder builder, int lineStartIndex, int desiredIndentation)
        {
            int currentIndentation = builder.Length - lineStartIndex;
            int spacesToAppend = Math.Max(desiredIndentation - currentIndentation, 1);
            builder.Append(' ', spacesToAppend);
        }

        /// <summary>
        /// Parse and dump a single instruction and return its size in bytes.
        /// </summary>
        /// <param name="rtf">Runtime function to parse</param>
        /// <param name="imageOffset">Offset within the PE image byte array</param>
        /// <param name="rtfOffset">Instruction offset within the runtime function</param>
        /// <param name="instruction">Output text representation of the instruction</param>
        /// <returns>Instruction size in bytes - i.o.w. the next instruction starts at rtfOffset + (the return value)</returns>
        public int GetInstruction(RuntimeFunction rtf, int imageOffset, int rtfOffset, out string instruction)
        {
            if (_disasm == IntPtr.Zero)
            {
                instruction = "";
                return rtf.Size;
            }

            int instrSize = CoreDisTools.GetInstruction(_disasm, rtf, imageOffset, rtfOffset, _reader.Image, out instruction);
            CoreDisTools.ClearOutputBuffer();

            // CoreDisTools dumps instructions in the following format:
            //
            //      address: bytes [padding] \t mnemonic [\t operands] \n
            //
            // However, due to an LLVM issue regarding instruction prefixes (https://bugs.llvm.org/show_bug.cgi?id=7709),
            // multiple lines may be returned for a single x86/x64 instruction.

            var builder = new StringBuilder();
            int lineNum = 0;
            // The start index of the last line in builder
            int lineStartIndex = 0;

            // Remove this foreach wrapper and line* variables after the aforementioned LLVM issue is fixed
            foreach (string line in instruction.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colonIndex = line.IndexOf(':');
                int tab1Index = line.IndexOf('\t');

                if ((0 < colonIndex) && (colonIndex < tab1Index))
                {
                    // First handle the address and the byte dump
                    if (_options.Naked)
                    {
                        if (!_options.HideOffsets)
                        {
                            // All lines but the last one must represent single-byte prefixes, so add lineNum to the offset
                            builder.Append($"{rtf.CodeOffset + rtfOffset + lineNum,8:x4}:");
                        }
                    }
                    else
                    {
                        if (_reader.Machine == Machine.Arm64)
                        {
                            // Replace " hh hh hh hh " byte dump with " hhhhhhhh ".
                            // CoreDisTools should be fixed to dump bytes this way for ARM64.
                            uint instructionBytes = BitConverter.ToUInt32(_reader.Image, imageOffset + rtfOffset);
                            builder.Append(line, 0, colonIndex + 1);
                            builder.Append(' ');
                            builder.Append(instructionBytes.ToString("x8"));
                        }
                        else
                        {
                            // Copy the offset and the byte dump
                            int byteDumpEndIndex = tab1Index;
                            do
                            {
                                byteDumpEndIndex--;
                            }
                            while (line[byteDumpEndIndex] == ' ');
                            builder.Append(line, 0, byteDumpEndIndex + 1);
                        }
                        builder.Append(' ');
                    }

                    // Now handle the mnemonic and operands. Ensure proper indentation for the mnemonic.
                    EnsureIndentation(builder, lineStartIndex, MnemonicIndentation);

                    int tab2Index = line.IndexOf('\t', tab1Index + 1);
                    if (tab2Index >= 0)
                    {
                        // Copy everything between the first and the second tabs
                        builder.Append(line, tab1Index + 1, tab2Index - tab1Index - 1);
                        // Ensure proper indentation for the operands
                        EnsureIndentation(builder, lineStartIndex, OperandsIndentation);
                        int afterTab2Index = tab2Index + 1;

                        // Work around an LLVM issue causing an extra space to be output before operands;
                        // see https://reviews.llvm.org/D35946.
                        if ((afterTab2Index < line.Length) &&
                            ((line[afterTab2Index] == ' ') || (line[afterTab2Index] == '\t')))
                        {
                            afterTab2Index++;
                        }

                        // Copy everything after the second tab
                        int savedLength = builder.Length;
                        builder.Append(line, afterTab2Index, line.Length - afterTab2Index);
                        // There should be no extra tabs. Should we encounter them, replace them with a single space.
                        if (line.IndexOf('\t', afterTab2Index) >= 0)
                        {
                            builder.Replace('\t', ' ', savedLength, builder.Length - savedLength);
                        }
                    }
                    else
                    {
                        // Copy everything after the first tab
                        builder.Append(line, tab1Index + 1, line.Length - tab1Index - 1);
                    }
                }
                else
                {
                    // Should not happen. Just replace tabs with spaces.
                    builder.Append(line.Replace('\t', ' '));
                }

                builder.Append('\n');
                lineNum++;
                lineStartIndex = builder.Length;
            }

            instruction = builder.ToString();

            switch (_reader.Machine)
            {
                case Machine.Amd64:
                    ProbeX64Quirks(rtf, imageOffset, rtfOffset, instrSize, ref instruction);
                    break;

                case Machine.I386:
                    ProbeX86Quirks(rtf, imageOffset, rtfOffset, instrSize, ref instruction);
                    break;

                case Machine.ArmThumb2:
                    break;

                case Machine.Arm64:
                    break;

                default:
                    throw new NotImplementedException();
            }

            instruction = instruction.Replace("\n", Environment.NewLine);
            return instrSize;
        }

        const string RelIPTag = "[rip ";

        /// <summary>
        /// Translate RIP-relative offsets to RVA's and convert cell addresses to symbol names
        /// </summary>
        /// <param name="rtf">Runtime function</param>
        /// <param name="imageOffset">Offset within the image byte array</param>
        /// <param name="rtfOffset">Offset within the runtime function</param>
        /// <param name="instrSize">Instruction size</param>
        /// <param name="instruction">Textual representation of the instruction</param>
        private void ProbeX64Quirks(RuntimeFunction rtf, int imageOffset, int rtfOffset, int instrSize, ref string instruction)
        {
            int leftBracket;
            int rightBracketPlusOne;
            int displacement;
            if (TryParseRipRelative(instruction, out leftBracket, out rightBracketPlusOne, out displacement))
            {
                int target = rtf.StartAddress + rtfOffset + instrSize + displacement;
                int newline = instruction.LastIndexOf('\n');
                StringBuilder translated = new StringBuilder();
                translated.Append(instruction, 0, leftBracket);
                if (_options.Naked)
                {
                    String targetName;
                    if (_reader.ImportCellNames.TryGetValue(target, out targetName))
                    {
                        translated.AppendFormat("[{0}]", targetName);
                    }
                    else
                    {
                        translated.AppendFormat("[0x{0:x4}]", target);
                    }
                }
                else
                {
                    translated.AppendFormat("[0x{0:x4}]", target);

                    AppendImportCellName(translated, target);
                }

                translated.Append(instruction, rightBracketPlusOne, newline - rightBracketPlusOne);

                translated.Append(instruction, newline, instruction.Length - newline);
                instruction = translated.ToString();
            }
            else
            {
                ProbeCommonIntelQuirks(rtf, imageOffset, rtfOffset, instrSize, ref instruction);
            }
        }

        /// <summary>
        /// X86 disassembler has a bug in decoding absolute indirections, mistaking them for RIP-relative indirections
        /// </summary>
        /// <param name="rtf">Runtime function</param>
        /// <param name="imageOffset">Offset within the image byte array</param>
        /// <param name="rtfOffset">Offset within the runtime function</param>
        /// <param name="instrSize">Instruction size</param>
        /// <param name="instruction">Textual representation of the instruction</param>
        private void ProbeX86Quirks(RuntimeFunction rtf, int imageOffset, int rtfOffset, int instrSize, ref string instruction)
        {
            int leftBracket;
            int rightBracketPlusOne;
            int absoluteAddress;
            if (TryParseRipRelative(instruction, out leftBracket, out rightBracketPlusOne, out absoluteAddress))
            {
                int target = absoluteAddress - (int)_reader.PEReader.PEHeaders.PEHeader.ImageBase;

                StringBuilder translated = new StringBuilder();
                translated.Append(instruction, 0, leftBracket);
                if (_options.Naked)
                {
                    String targetName;
                    if (_reader.ImportCellNames.TryGetValue(target, out targetName))
                    {
                        translated.AppendFormat("[{0}]", targetName);
                    }
                    else
                    {
                        translated.AppendFormat("[0x{0:x4}]", target);
                    }
                }
                else
                {
                    translated.AppendFormat("[0x{0:x4}]", target);

                    AppendImportCellName(translated, target);
                }

                translated.Append(instruction, rightBracketPlusOne, instruction.Length - rightBracketPlusOne);
                instruction = translated.ToString();
            }
            else
            {
                ProbeCommonIntelQuirks(rtf, imageOffset, rtfOffset, instrSize, ref instruction);
            }
        }

        /// <summary>
        /// Probe quirks that have the same behavior for X86 and X64.
        /// </summary>
        /// <param name="rtf">Runtime function</param>
        /// <param name="imageOffset">Offset within the image byte array</param>
        /// <param name="rtfOffset">Offset within the runtime function</param>
        /// <param name="instrSize">Instruction size</param>
        /// <param name="instruction">Textual representation of the instruction</param>
        private void ProbeCommonIntelQuirks(RuntimeFunction rtf, int imageOffset, int rtfOffset, int instrSize, ref string instruction)
        {
            int instructionRVA = rtf.StartAddress + rtfOffset;
            int nextInstructionRVA = instructionRVA + instrSize;
            if (instrSize == 2 && IsIntelJumpInstructionWithByteOffset(imageOffset + rtfOffset))
            {
                sbyte offset = (sbyte)_reader.Image[imageOffset + rtfOffset + 1];
                ReplaceRelativeOffset(ref instruction, nextInstructionRVA + offset, rtf);
            }
            else if (instrSize == 5 && IsIntel1ByteJumpInstructionWithIntOffset(imageOffset + rtfOffset))
            {
                int offset = BitConverter.ToInt32(_reader.Image, imageOffset + rtfOffset + 1);
                ReplaceRelativeOffset(ref instruction, nextInstructionRVA + offset, rtf);
            }
            else if (instrSize == 5 && IsIntelCallInstructionWithIntOffset(imageOffset + rtfOffset))
            {
                int offset = BitConverter.ToInt32(_reader.Image, imageOffset + rtfOffset + 1);
                int targetRVA = nextInstructionRVA + offset;
                int targetImageOffset = _reader.GetOffset(targetRVA);
                bool pointsOutsideRuntimeFunction = (targetRVA < rtf.StartAddress || targetRVA >= rtf.StartAddress + rtf.Size);
                if (pointsOutsideRuntimeFunction && IsIntel2ByteIndirectJumpPCRelativeInstruction(targetImageOffset, out int instructionRelativeOffset))
                {
                    int thunkTargetRVA = targetRVA + instructionRelativeOffset;
                    bool haveImportCell = _reader.ImportCellNames.TryGetValue(thunkTargetRVA, out string importCellName);

                    if (_options.Naked && haveImportCell)
                    {
                        ReplaceRelativeOffset(ref instruction, $@"qword ptr [{importCellName}]", rtf);
                    }
                    else
                    {
                        ReplaceRelativeOffset(ref instruction, targetRVA, rtf);
                        if (haveImportCell)
                        {
                            int instructionEnd = instruction.IndexOf('\n');
                            StringBuilder builder = new StringBuilder(instruction, 0, instructionEnd, capacity: 256);
                            AppendComment(builder, @$"JMP [0x{thunkTargetRVA:X4}]: {importCellName}");
                            builder.AppendLine();
                            instruction = builder.ToString();
                        }
                    }
                }
                else if (pointsOutsideRuntimeFunction && IsAnotherRuntimeFunctionWithinMethod(targetRVA, rtf, out int runtimeFunctionIndex))
                {
                    string runtimeFunctionName = string.Format("RUNTIME_FUNCTION[{0}]", runtimeFunctionIndex);

                    if (_options.Naked)
                    {
                        ReplaceRelativeOffset(ref instruction, runtimeFunctionName, rtf);
                    }
                    else
                    {
                        ReplaceRelativeOffset(ref instruction, targetRVA, rtf);
                        int instructionEnd = instruction.IndexOf('\n');
                        StringBuilder builder = new StringBuilder(instruction, 0, instructionEnd, capacity: 256);
                        AppendComment(builder, runtimeFunctionName);
                        builder.AppendLine();
                        instruction = builder.ToString();
                    }
                }
                else
                {
                    ReplaceRelativeOffset(ref instruction, nextInstructionRVA + offset, rtf);
                }
            }
            else if (instrSize == 6 && IsIntel2ByteJumpInstructionWithIntOffset(imageOffset + rtfOffset))
            {
                int offset = BitConverter.ToInt32(_reader.Image, imageOffset + rtfOffset + 2);
                ReplaceRelativeOffset(ref instruction, nextInstructionRVA + offset, rtf);
            }
        }

        /// <summary>
        /// Try to parse the [rip +- displacement] section in a disassembled instruction string.
        /// </summary>
        /// <param name="instruction">Disassembled instruction string</param>
        /// <param name="leftBracket">Index of the left bracket in the instruction</param>
        /// <param name="rightBracketPlusOne">Index of the right bracket in the instruction plus one</param>
        /// <param name="displacement">Value of the IP-relative delta</param>
        /// <returns></returns>
        private bool TryParseRipRelative(string instruction, out int leftBracket, out int rightBracket, out int displacement)
        {
            int relip = instruction.IndexOf(RelIPTag);
            if (relip >= 0 && instruction.Length >= relip + RelIPTag.Length + 3)
            {
                int start = relip;
                relip += RelIPTag.Length;
                char sign = instruction[relip];
                if (sign == '+' || sign == '-' &&
                    instruction[relip + 1] == ' ' &&
                    Char.IsDigit(instruction[relip + 2]))
                {
                    relip += 2;
                    int offset = 0;
                    do
                    {
                        offset = 10 * offset + (int)(instruction[relip] - '0');
                    }
                    while (++relip < instruction.Length && Char.IsDigit(instruction[relip]));
                    if (relip < instruction.Length && instruction[relip] == ']')
                    {
                        relip++;
                        if (sign == '-')
                        {
                            offset = -offset;
                        }
                        leftBracket = start;
                        rightBracket = relip;
                        displacement = offset;
                        return true;
                    }
                }
            }

            leftBracket = 0;
            rightBracket = 0;
            displacement = 0;
            return false;
        }

        /// <summary>
        /// Append import cell name to the constructed instruction string as a comment if available.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="importCellRva"></param>
        private void AppendImportCellName(StringBuilder builder, int importCellRva)
        {
            String targetName;
            if (_reader.ImportCellNames.TryGetValue(importCellRva, out targetName))
            {
                AppendComment(builder, targetName);
            }
        }

        /// <summary>
        /// Append a given comment to the string builder.
        /// </summary>
        /// <param name="builder">String builder to append comment to</param>
        /// <param name="comment">Comment to append</param>
        private void AppendComment(StringBuilder builder, string comment)
        {
            int fill = 61 - builder.Length;
            if (fill > 0)
            {
                builder.Append(' ', fill);
            }
            builder.Append(" // ");
            builder.Append(comment);
        }

        /// <summary>
        /// Replace relative offset in the disassembled instruction with the true target RVA.
        /// </summary>
        /// <param name="instruction">Disassembled instruction to modify</param>
        /// <param name="target">Target string to replace offset with</param>
        /// <param name="rtf">Runtime function being disassembled</param>
        private void ReplaceRelativeOffset(ref string instruction, int target, RuntimeFunction rtf)
        {
            int outputOffset = target;
            if (_options.Naked)
            {
                outputOffset -= rtf.StartAddress;
            }
            ReplaceRelativeOffset(ref instruction, string.Format("0x{0:X4}", outputOffset), rtf);
        }

        /// <summary>
        /// Replace relative offset in the disassembled instruction with an arbitrary string.
        /// </summary>
        /// <param name="instruction">Disassembled instruction to modify</param>
        /// <param name="replacementString">String to replace offset with</param>
        /// <param name="rtf">Runtime function being disassembled</param>
        private void ReplaceRelativeOffset(ref string instruction, string replacementString, RuntimeFunction rtf)
        {
            int numberEnd = instruction.IndexOf('\n');
            int number = numberEnd;
            while (number > 0)
            {
                char c = instruction[number - 1];
                if (c >= ' ' && !Char.IsDigit(c) && c != '-')
                {
                    break;
                }
                number--;
            }

            StringBuilder translated = new StringBuilder();
            translated.Append(instruction, 0, number);
            translated.Append(replacementString);
            translated.Append(instruction, numberEnd, instruction.Length - numberEnd);
            instruction = translated.ToString();
        }

        /// <summary>
        /// Returns true when this is one of the x86 / amd64 opcodes used for branch instructions
        /// with single-byte offset.
        /// </summary>
        /// <param name="imageOffset">Offset within the PE image byte array</param>
        private bool IsIntelJumpInstructionWithByteOffset(int imageOffset)
        {
            byte opCode = _reader.Image[imageOffset];
            return
                (opCode >= 0x70 && opCode <= 0x7F) // short conditional jumps
                || opCode == 0xE3 // JCXZ
                || opCode == 0xEB // JMP
                ;
        }

        /// <summary>
        /// Returns true for the call relative opcode with signed 4-byte offset.
        /// </summary>
        /// <param name="imageOffset">Offset within the PE image byte array</param>
        private bool IsIntelCallInstructionWithIntOffset(int imageOffset)
        {
            return _reader.Image[imageOffset] == 0xE8; // CALL rel32
        }

        /// <summary>
        /// Returns true when this is one of the x86 / amd64 near jump / call opcodes
        /// with signed 4-byte offset.
        /// </summary>
        /// <param name="imageOffset">Offset within the PE image byte array</param>
        private bool IsIntel1ByteJumpInstructionWithIntOffset(int imageOffset)
        {
            return _reader.Image[imageOffset] == 0xE9; // JMP rel32
        }

        /// <summary>
        /// Returns true when this is one of the x86 / amd64 conditional near jump
        /// opcodes with signed 4-byte offset.
        /// </summary>
        /// <param name="imageOffset">Offset within the PE image byte array</param>
        private bool IsIntel2ByteJumpInstructionWithIntOffset(int imageOffset)
        {
            byte opCode1 = _reader.Image[imageOffset];
            byte opCode2 = _reader.Image[imageOffset + 1];
            return opCode1 == 0x0F &&
                (opCode2 >= 0x80 && opCode2 <= 0x8F); // near conditional jumps
        }

        /// <summary>
        /// Returns true when this is the 2-byte instruction for indirect jump
        /// with RIP-relative offset.
        /// </summary>
        /// <param name="imageOffset">Offset within the PE image byte array</param>
        /// <returns></returns>
        private bool IsIntel2ByteIndirectJumpPCRelativeInstruction(int imageOffset, out int instructionRelativeOffset)
        {
            byte opCode1 = _reader.Image[imageOffset + 0];
            byte opCode2 = _reader.Image[imageOffset + 1];
            int offsetDelta = 6;

            if (opCode1 == 0x48 && opCode2 == 0x8B && _reader.Image[imageOffset + 2] == 0x15) // MOV RDX, [R2R module]
            {
                imageOffset += 7;
                offsetDelta += 7;
                opCode1 = _reader.Image[imageOffset + 0];
                opCode2 = _reader.Image[imageOffset + 1];
            }

            if (opCode1 == 0xFF && opCode2 == 0x25)
            {
                // JMP [RIP + rel32]
                instructionRelativeOffset = offsetDelta + BitConverter.ToInt32(_reader.Image, imageOffset + 2);
                return true;
            }

            instructionRelativeOffset = 0;
            return false;
        }

        /// <summary>
        /// Check whether a given target RVA corresponds to another runtime function within the same method.
        /// </summary>
        /// <param name="rva">Target RVA to analyze</param>
        /// <param name="rtf">Runtime function being disassembled</param>
        /// <param name="runtimeFunctionIndex">Output runtime function index if found, -1 otherwise</param>
        /// <returns>true if target runtime function has been found, false otherwise</returns>
        private bool IsAnotherRuntimeFunctionWithinMethod(int rva, RuntimeFunction rtf, out int runtimeFunctionIndex)
        {
            for (int rtfIndex = 0; rtfIndex < rtf.Method.RuntimeFunctions.Count; rtfIndex++)
            {
                if (rva == rtf.Method.RuntimeFunctions[rtfIndex].StartAddress)
                {
                    runtimeFunctionIndex = rtfIndex;
                    return true;
                }
            }

            runtimeFunctionIndex = -1;
            return false;
        }
    }
}
