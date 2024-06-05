using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SchemaBuilder
{
    /// <summary>
    /// Inspects the IL of the first constructor of a member's declaring type to determine a likely default value.
    /// </summary>
    public static class DefaultValueFromCtor
    {
        public static bool TryGetInitializerValue(MemberInfo member, out object value)
        {
            value = default;
            var memberType = member switch
            {
                FieldInfo fieldInfo => fieldInfo.FieldType,
                MethodInfo methodInfo => methodInfo.ReturnType,
                PropertyInfo propertyInfo => propertyInfo.PropertyType,
                _ => null
            };
            if (memberType == null || member.DeclaringType == null || member.DeclaringType.IsValueType) return false;
            var constructors = member.DeclaringType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (constructors.Length == 0) return false;
            var il = constructors[0].GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            var reader = new BinaryReader(new MemoryStream(il));
            object prevConstant = null;
            while (true)
            {
                var op = reader.BaseStream.ReadByte();
                if (op == -1) break;
                var opCode = op != 0xfe ? OneByteOpcodes[op] : TwoBytesOpcodes[reader.ReadByte()];
                var operand = ReadOperand(member.DeclaringType.Module, opCode, reader);
                if (TryGetConstant(opCode, operand, out var constant))
                {
                    prevConstant = constant;
                    continue;
                }

                if (prevConstant == null)
                    continue;

                if ((opCode == OpCodes.Stfld && ReferenceEquals(operand, member))
                    || (opCode == OpCodes.Callvirt && (
                        ReferenceEquals(operand, member) ||
                        member is PropertyInfo { SetMethod: { } } prop && ReferenceEquals(operand, prop.SetMethod))))
                {
                    try
                    {
                        value = Convert.ChangeType(prevConstant, memberType);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                prevConstant = null;
            }

            return false;
        }

        private static object ReadOperand(
            Module module,
            OpCode op, BinaryReader reader)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineI:
                    return reader.ReadInt32();
                case OperandType.ShortInlineI:
                    return op == OpCodes.Ldc_I4_S ? (object)reader.ReadSByte() : reader.ReadByte();
                case OperandType.InlineR:
                    return reader.ReadDouble();
                case OperandType.ShortInlineR:
                    return reader.ReadSingle();
                case OperandType.InlineI8:
                    return reader.ReadInt64();
                case OperandType.InlineString:
                    return module.ResolveString(reader.ReadInt32());
                case OperandType.InlineNone:
                    return null;
                case OperandType.InlineSwitch:
                    var count = reader.ReadInt32();
                    for (var i = 0; i < count; i++)
                        reader.ReadInt32();
                    return null;
                case OperandType.InlineTok:
                    return module.ResolveMember(reader.ReadInt32());
                case OperandType.InlineType:
                    return module.ResolveType(reader.ReadInt32());
                case OperandType.InlineField:
                    return module.ResolveField(reader.ReadInt32());
                case OperandType.InlineSig:
                    module.ResolveSignature(reader.ReadInt32());
                    return null;
                case OperandType.InlineMethod:
                    return module.ResolveMethod(reader.ReadInt32());
                case OperandType.InlineBrTarget:
                    reader.ReadInt32();
                    return null;
                case OperandType.ShortInlineBrTarget:
                    reader.ReadByte();
                    return null;
                case OperandType.InlineVar:
                    reader.ReadInt16();
                    return null;
                case OperandType.ShortInlineVar:
                    reader.ReadByte();
                    return null;
#pragma warning disable CS0618 // Type or member is obsolete
                case OperandType.InlinePhi:
#pragma warning restore CS0618 // Type or member is obsolete
                default:
                    // Type or member is obsolete
                    throw new ArgumentOutOfRangeException(nameof(op), op, null);
            }
        }

        private static readonly Dictionary<OpCode, object> ConstantOps = new Dictionary<OpCode, object>
        {
            [OpCodes.Ldc_I4_M1] = -1,
            [OpCodes.Ldc_I4_0] = 0,
            [OpCodes.Ldc_I4_1] = 1,
            [OpCodes.Ldc_I4_2] = 2,
            [OpCodes.Ldc_I4_3] = 3,
            [OpCodes.Ldc_I4_4] = 4,
            [OpCodes.Ldc_I4_5] = 5,
            [OpCodes.Ldc_I4_6] = 6,
            [OpCodes.Ldc_I4_7] = 7,
            [OpCodes.Ldc_I4_8] = 8,
            [OpCodes.Ldnull] = null,
        };

        private static bool TryGetConstant(OpCode op, object operand, out object constant)
        {
            if (ConstantOps.TryGetValue(op, out constant))
                return true;
            constant = operand;
            return op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I8 || op == OpCodes.Ldc_R4 || op == OpCodes.Ldc_R8 ||
                   op == OpCodes.Ldstr;
        }

        private static readonly OpCode[] OneByteOpcodes;
        private static readonly OpCode[] TwoBytesOpcodes;

        [MethodImpl(MethodImplOptions.Synchronized)]
        static DefaultValueFromCtor()
        {
            OneByteOpcodes = new OpCode[0xe1];
            TwoBytesOpcodes = new OpCode[0x1f];

            var fields = typeof(OpCodes).GetFields(
                BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                var opcode = (OpCode)field.GetValue(null);
                if (opcode.OpCodeType == OpCodeType.Nternal)
                    continue;
                if (opcode.Size == 1)
                    OneByteOpcodes[opcode.Value] = opcode;
                else
                    TwoBytesOpcodes[opcode.Value & 0xff] = opcode;
            }
        }
    }
}