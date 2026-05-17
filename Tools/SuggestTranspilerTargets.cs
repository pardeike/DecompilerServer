using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using System.Text;

namespace DecompilerServer;

[McpServerToolType]
public static class SuggestTranspilerTargetsTool
{
    [McpServerTool, Description("Suggest candidate transpiler anchors from actual IL instructions. Read get_il for the complete instruction list before patching.")]
    public static string SuggestTranspilerTargets(string memberId, int maxHints = 10, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
            var contextManager = session.ContextManager;
            var memberResolver = session.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var method = ToolValidation.ResolveMethodOrThrow(session, memberId);

            var body = IlAnalysisService.ReadMethodBody(method, contextManager);
            var targets = AnalyzeTranspilerTargets(body, maxHints);
            var exampleSnippet = GenerateTranspilerExample(method);

            var result = new
            {
                target = CreateMethodSummary(method, memberResolver),
                hints = targets,
                exampleTranspiler = exampleSnippet,
                hasIlBody = body.HasBody,
                noBodyReason = body.NoBodyReason,
                notes = new[]
                {
                    "Hints are derived from the method's actual IL instructions.",
                    "Offsets may vary between builds - prefer opcode and operand patterns for reliable matching.",
                    "Use get_il for the complete instruction listing before writing a transpiler.",
                    "Consider HarmonyLib.CodeMatcher for complex pattern matching."
                }
            };

            return result;
        });
    }

    private static List<TranspilerHint> AnalyzeTranspilerTargets(MethodIlBody body, int maxHints)
    {
        var hints = new List<TranspilerHint>();

        if (!body.HasBody)
            return hints;

        var instructions = body.Instructions.ToArray();
        for (var index = 0; index < instructions.Length && hints.Count < Math.Max(0, maxHints); index++)
        {
            var instruction = instructions[index];
            var rationale = GetRationale(instruction);
            if (rationale == null)
                continue;

            hints.Add(new TranspilerHint
            {
                Offset = instruction.Offset,
                Opcode = instruction.OpCode,
                OperandSummary = instruction.OperandSummary ?? "",
                NearbyOps = GetNearbyOps(instructions, index),
                Rationale = rationale,
                Example = GeneratePatternExample(instruction)
            });
        }

        return hints;
    }

    private static string GenerateTranspilerExample(IMethod method)
    {
        var example = new StringBuilder();

        example.AppendLine("// Example transpiler method for " + method.Name);
        example.AppendLine("[HarmonyTranspiler]");
        example.AppendLine("static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)");
        example.AppendLine("{");
        example.AppendLine("    var codes = new List<CodeInstruction>(instructions);");
        example.AppendLine("    ");
        example.AppendLine("    // Example: Replace a method call");
        example.AppendLine("    for (int i = 0; i < codes.Count; i++)");
        example.AppendLine("    {");
        example.AppendLine("        // Look for specific call pattern");
        example.AppendLine("        if (codes[i].opcode == OpCodes.Call &&");
        example.AppendLine("            codes[i].operand is MethodInfo method &&");
        example.AppendLine("            method.Name == \"TargetMethod\")");
        example.AppendLine("        {");
        example.AppendLine("            // Replace with custom method call");
        example.AppendLine("            codes[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MyClass), \"MyMethod\"));");
        example.AppendLine("        }");
        example.AppendLine("    }");
        example.AppendLine("    ");
        example.AppendLine("    // Example: Insert instructions before a pattern");
        example.AppendLine("    for (int i = 0; i < codes.Count; i++)");
        example.AppendLine("    {");
        example.AppendLine("        if (codes[i].opcode == OpCodes.Ret)");
        example.AppendLine("        {");
        example.AppendLine("            // Insert logging before return");
        example.AppendLine("            codes.Insert(i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Console), \"WriteLine\", new[] { typeof(string) })));");
        example.AppendLine("            codes.Insert(i, new CodeInstruction(OpCodes.Ldstr, \"Method returning\"));");
        example.AppendLine("            break; // Only modify first return");
        example.AppendLine("        }");
        example.AppendLine("    }");
        example.AppendLine("    ");
        example.AppendLine("    return codes.AsEnumerable();");
        example.AppendLine("}");

        return example.ToString();
    }

    private static string? GetRationale(IlInstructionInfo instruction)
    {
        return instruction.OpCode switch
        {
            "call" => "Direct method call - candidate for replacement, guard insertion, or call-site context.",
            "callvirt" => "Virtual/interface call - candidate for interception or receiver/result context.",
            "newobj" => "Object construction - candidate for replacement or constructor argument context.",
            "ldstr" => "String literal load - candidate for matching a stable local anchor.",
            "stfld" or "stsfld" => "Field write - candidate for state mutation interception.",
            "ldfld" or "ldsfld" => "Field read - candidate for state dependency inspection.",
            "ret" => "Return instruction - candidate for final result handling.",
            "throw" => "Exception throw - candidate for error-path anchoring.",
            "brfalse" or "brfalse.s" or "brtrue" or "brtrue.s" => "Conditional branch - candidate for control-flow anchoring.",
            _ => null
        };
    }

    private static string[] GetNearbyOps(IReadOnlyList<IlInstructionInfo> instructions, int index)
    {
        var start = Math.Max(0, index - 2);
        var end = Math.Min(instructions.Count - 1, index + 2);
        return Enumerable.Range(start, end - start + 1)
            .Select(i => IlAnalysisService.FormatInstruction(instructions[i]))
            .ToArray();
    }

    private static string GeneratePatternExample(IlInstructionInfo instruction)
    {
        return instruction.OpCode switch
        {
            "call" => "if (code.opcode == OpCodes.Call && code.operand is MethodInfo method) { /* match exact method */ }",
            "callvirt" => "if (code.opcode == OpCodes.Callvirt && code.operand is MethodInfo method) { /* match exact method */ }",
            "newobj" => "if (code.opcode == OpCodes.Newobj && code.operand is ConstructorInfo ctor) { /* match exact constructor */ }",
            "ldstr" => $"if (code.opcode == OpCodes.Ldstr && Equals(code.operand, {LiteralForExample(instruction.OperandSummary)})) {{ /* anchor */ }}",
            "stfld" or "stsfld" => "if ((code.opcode == OpCodes.Stfld || code.opcode == OpCodes.Stsfld) && code.operand is FieldInfo field) { /* match exact field */ }",
            "ldfld" or "ldsfld" => "if ((code.opcode == OpCodes.Ldfld || code.opcode == OpCodes.Ldsfld) && code.operand is FieldInfo field) { /* match exact field */ }",
            "ret" => "if (code.opcode == OpCodes.Ret) { /* insert before return */ }",
            "brfalse" or "brfalse.s" => "if (code.opcode == OpCodes.Brfalse || code.opcode == OpCodes.Brfalse_S) { /* match false branch */ }",
            "brtrue" or "brtrue.s" => "if (code.opcode == OpCodes.Brtrue || code.opcode == OpCodes.Brtrue_S) { /* match true branch */ }",
            "throw" => "if (code.opcode == OpCodes.Throw) { /* match exception path */ }",
            _ => $"if (code.opcode == OpCodes.{ToOpCodesName(instruction.OpCode)}) {{ /* handle matched instruction */ }}"
        };
    }

    private static string LiteralForExample(string? operandSummary)
    {
        return string.IsNullOrWhiteSpace(operandSummary) ? "\"...\"" : operandSummary;
    }

    private static string ToOpCodesName(string opcode)
    {
        return string.Concat(opcode.Split('.').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static MemberSummary CreateMethodSummary(IMethod method, MemberResolver memberResolver)
    {
        return new MemberSummary
        {
            MemberId = memberResolver.GenerateMemberId(method),
            Name = method.Name,
            FullName = method.FullName,
            Kind = method.IsConstructor ? "Constructor" : "Method",
            DeclaringType = method.DeclaringType?.FullName,
            Namespace = method.DeclaringType?.Namespace,
            Signature = memberResolver.GetMemberSignature(method),
            Accessibility = method.Accessibility.ToString(),
            IsStatic = method.IsStatic,
            IsAbstract = method.IsAbstract,
            IsVirtual = method.IsVirtual
        };
    }
}

public class TranspilerHint
{
    public required int Offset { get; init; }
    public required string Opcode { get; init; }
    public required string OperandSummary { get; init; }
    public required string[] NearbyOps { get; init; }
    public required string Rationale { get; init; }
    public required string Example { get; init; }
}
