using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DecompilerServer.Services;

public static class IlAnalysisService
{
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];

    static IlAnalysisService()
    {
        foreach (var field in typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            var opCode = (OpCode)field.GetValue(null)!;
            var value = (ushort)opCode.Value;
            if (value < 0x100)
                SingleByteOpCodes[value] = opCode;
            else if ((value & 0xff00) == 0xfe00)
                MultiByteOpCodes[value & 0xff] = opCode;
        }
    }

    public static MethodIlBody ReadMethodBody(IMethod method, AssemblyContextManager contextManager)
    {
        if (method.MetadataToken.IsNil)
            return MethodIlBody.NoBody("metadata_token_missing");

        if (method.MetadataToken.Kind != HandleKind.MethodDefinition)
            return MethodIlBody.NoBody("not_method_definition");

        var peFile = contextManager.GetPEFile();
        var metadata = peFile.Metadata;
        var methodDefinition = metadata.GetMethodDefinition((MethodDefinitionHandle)method.MetadataToken);

        if (methodDefinition.RelativeVirtualAddress == 0)
            return MethodIlBody.NoBody("no_il_body");

        var body = peFile.Reader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
        var il = body.GetILBytes();
        if (il == null || il.Length == 0)
            return MethodIlBody.NoBody("empty_il_body");

        var instructions = ReadInstructions(il, metadata).ToArray();
        return new MethodIlBody(
            HasBody: true,
            NoBodyReason: null,
            RelativeVirtualAddress: methodDefinition.RelativeVirtualAddress,
            MaxStack: body.MaxStack,
            LocalVariablesInitialized: body.LocalVariablesInitialized,
            LocalSignatureToken: body.LocalSignature.IsNil ? null : MetadataTokens.GetToken(body.LocalSignature),
            CodeSize: il.Length,
            Instructions: instructions);
    }

    public static IEnumerable<IlInstructionInfo> ReadInstructions(byte[] il, MetadataReader metadata)
    {
        var position = 0;
        while (position < il.Length)
        {
            var offset = position;
            OpCode opCode;
            var code = il[position++];
            if (code == 0xFE)
            {
                opCode = MultiByteOpCodes[il[position++]];
            }
            else
            {
                opCode = SingleByteOpCodes[code];
            }

            object? operand = null;
            int? operandToken = null;
            string? operandTokenHex = null;
            string? operandKind = null;
            string? operandSummary = null;

            switch (opCode.OperandType)
            {
                case OperandType.InlineBrTarget:
                    var branchDelta = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = position + branchDelta;
                    operandKind = "branchTarget";
                    operandSummary = FormatOffset((int)operand);
                    break;
                case OperandType.ShortInlineBrTarget:
                    var shortBranchDelta = unchecked((sbyte)il[position]);
                    position += 1;
                    operand = position + shortBranchDelta;
                    operandKind = "branchTarget";
                    operandSummary = FormatOffset((int)operand);
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    var token = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = token;
                    operandToken = token;
                    operandTokenHex = $"0x{token:X8}";
                    operandKind = opCode.OperandType.ToString();
                    operandSummary = FormatMetadataToken(metadata, token);
                    break;
                case OperandType.InlineSig:
                    operandToken = BitConverter.ToInt32(il, position);
                    operandTokenHex = $"0x{operandToken.Value:X8}";
                    position += 4;
                    operand = operandToken;
                    operandKind = "signature";
                    operandSummary = $"0x{operandToken.Value:X8}";
                    break;
                case OperandType.InlineString:
                    var stringToken = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = stringToken;
                    operandToken = stringToken;
                    operandTokenHex = $"0x{stringToken:X8}";
                    operandKind = "string";
                    operandSummary = Quote(metadata.GetUserString(MetadataTokens.UserStringHandle(stringToken)));
                    break;
                case OperandType.InlineI:
                    operand = BitConverter.ToInt32(il, position);
                    position += 4;
                    operandKind = "int32";
                    operandSummary = operand.ToString();
                    break;
                case OperandType.ShortInlineI:
                    operand = unchecked((sbyte)il[position]);
                    position += 1;
                    operandKind = "int8";
                    operandSummary = operand.ToString();
                    break;
                case OperandType.InlineI8:
                    operand = BitConverter.ToInt64(il, position);
                    position += 8;
                    operandKind = "int64";
                    operandSummary = operand.ToString();
                    break;
                case OperandType.ShortInlineR:
                    operand = BitConverter.ToSingle(il, position);
                    position += 4;
                    operandKind = "float32";
                    operandSummary = operand.ToString();
                    break;
                case OperandType.InlineR:
                    operand = BitConverter.ToDouble(il, position);
                    position += 8;
                    operandKind = "float64";
                    operandSummary = operand.ToString();
                    break;
                case OperandType.ShortInlineVar:
                    operand = il[position];
                    position += 1;
                    operandKind = "variable";
                    operandSummary = $"V_{operand}";
                    break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(il, position);
                    position += 2;
                    operandKind = "variable";
                    operandSummary = $"V_{operand}";
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, position);
                    position += 4;
                    var basePosition = position + 4 * count;
                    var targets = new int[count];
                    for (var i = 0; i < count; i++)
                    {
                        targets[i] = basePosition + BitConverter.ToInt32(il, position);
                        position += 4;
                    }
                    operand = targets;
                    operandKind = "switchTargets";
                    operandSummary = string.Join(", ", targets.Select(FormatOffset));
                    break;
                case OperandType.InlineNone:
                default:
                    break;
            }

            yield return new IlInstructionInfo(
                Offset: offset,
                OpCode: opCode.Name ?? opCode.ToString() ?? "unknown",
                Operand: operand,
                OperandToken: operandToken,
                OperandTokenHex: operandTokenHex,
                OperandKind: operandKind,
                OperandSummary: operandSummary);
        }
    }

    public static string FormatInstruction(IlInstructionInfo instruction)
    {
        return instruction.OperandSummary == null
            ? $"{FormatOffset(instruction.Offset)}: {instruction.OpCode}"
            : $"{FormatOffset(instruction.Offset)}: {instruction.OpCode} {instruction.OperandSummary}";
    }

    private static string FormatMetadataToken(MetadataReader metadata, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodDefinition(metadata, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberReference(metadata, (MemberReferenceHandle)handle),
                HandleKind.FieldDefinition => FormatFieldDefinition(metadata, (FieldDefinitionHandle)handle),
                HandleKind.TypeDefinition => FormatTypeDefinition(metadata, (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => FormatTypeReference(metadata, (TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => $"typespec 0x{token:X8}",
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    private static string FormatMethodDefinition(MetadataReader metadata, MethodDefinitionHandle handle)
    {
        var definition = metadata.GetMethodDefinition(handle);
        var typeName = FormatTypeDefinition(metadata, definition.GetDeclaringType());
        return $"{typeName}.{metadata.GetString(definition.Name)}";
    }

    private static string FormatFieldDefinition(MetadataReader metadata, FieldDefinitionHandle handle)
    {
        var definition = metadata.GetFieldDefinition(handle);
        var typeName = FormatTypeDefinition(metadata, definition.GetDeclaringType());
        return $"{typeName}.{metadata.GetString(definition.Name)}";
    }

    private static string FormatMemberReference(MetadataReader metadata, MemberReferenceHandle handle)
    {
        var reference = metadata.GetMemberReference(handle);
        var parent = FormatMemberParent(metadata, reference.Parent);
        return $"{parent}.{metadata.GetString(reference.Name)}";
    }

    private static string FormatMemberParent(MetadataReader metadata, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => FormatTypeDefinition(metadata, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => FormatTypeReference(metadata, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => "typespec",
            HandleKind.MethodDefinition => FormatMethodDefinition(metadata, (MethodDefinitionHandle)handle),
            _ => handle.Kind.ToString()
        };
    }

    private static string FormatTypeDefinition(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var definition = metadata.GetTypeDefinition(handle);
        var ns = metadata.GetString(definition.Namespace);
        var name = metadata.GetString(definition.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string FormatTypeReference(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        var ns = metadata.GetString(reference.Namespace);
        var name = metadata.GetString(reference.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string FormatOffset(int offset)
    {
        return $"IL_{offset:X4}";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

public sealed record MethodIlBody(
    bool HasBody,
    string? NoBodyReason,
    int? RelativeVirtualAddress,
    int? MaxStack,
    bool? LocalVariablesInitialized,
    int? LocalSignatureToken,
    int CodeSize,
    IReadOnlyList<IlInstructionInfo> Instructions)
{
    public static MethodIlBody NoBody(string reason)
    {
        return new MethodIlBody(false, reason, null, null, null, null, 0, Array.Empty<IlInstructionInfo>());
    }
}

public sealed record IlInstructionInfo(
    int Offset,
    string OpCode,
    object? Operand,
    int? OperandToken,
    string? OperandTokenHex,
    string? OperandKind,
    string? OperandSummary);
