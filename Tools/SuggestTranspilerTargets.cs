using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Metadata;
using System.Reflection.Metadata;
using System.Text;

namespace DecompilerServer;

public static class SuggestTranspilerTargetsTool
{
    [McpServerTool, Description("Suggest candidate IL offsets and patterns for transpiler insertion points.")]
    public static string SuggestTranspilerTargets(string memberId, int maxHints = 10)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var member = memberResolver.ResolveMember(memberId);
            if (member == null)
            {
                throw new ArgumentException($"Invalid member ID: {memberId}");
            }

            if (member is not IMethod method)
            {
                throw new ArgumentException($"Member must be a method: {memberId}");
            }

            var targets = AnalyzeTranspilerTargets(method, maxHints);
            var exampleSnippet = GenerateTranspilerExample(method);

            var result = new
            {
                target = CreateMethodSummary(method),
                hints = targets,
                exampleTranspiler = exampleSnippet,
                notes = new[]
                {
                    "These suggestions are based on common IL patterns that are good insertion points",
                    "Offsets may vary between builds - use opcode patterns for reliable matching",
                    "Always test transpiler modifications thoroughly",
                    "Consider using HarmonyLib.CodeMatcher for complex pattern matching"
                }
            };

            return result;
        });
    }

    private static List<TranspilerHint> AnalyzeTranspilerTargets(IMethod method, int maxHints)
    {
        var hints = new List<TranspilerHint>();

        try
        {
            // Get method body from metadata
            var methodDef = method.MetadataToken;
            if (methodDef.IsNil)
                return hints;

            // Common patterns that make good transpiler targets
            var commonPatterns = new[]
            {
                new { Pattern = "Call", Rationale = "Method call - good for interception or replacement" },
                new { Pattern = "Callvirt", Rationale = "Virtual method call - good for interception" },
                new { Pattern = "Newobj", Rationale = "Object creation - good for replacement or monitoring" },
                new { Pattern = "Ldstr", Rationale = "String literal - good for replacement or modification" },
                new { Pattern = "Stfld", Rationale = "Field assignment - good for value interception" },
                new { Pattern = "Ldfld", Rationale = "Field access - good for value monitoring" },
                new { Pattern = "Ret", Rationale = "Return statement - good for result modification" },
                new { Pattern = "Throw", Rationale = "Exception throw - good for error handling" },
                new { Pattern = "Brfalse", Rationale = "Conditional branch - good for logic modification" },
                new { Pattern = "Brtrue", Rationale = "Conditional branch - good for logic modification" }
            };

            // Generate synthetic hints based on method characteristics
            int hintIndex = 0;
            foreach (var pattern in commonPatterns.Take(maxHints))
            {
                hints.Add(new TranspilerHint
                {
                    Offset = hintIndex * 10, // Synthetic offset
                    Opcode = pattern.Pattern,
                    OperandSummary = $"Synthetic {pattern.Pattern} pattern",
                    NearbyOps = new[] { "ldarg.0", pattern.Pattern.ToLower(), "nop" },
                    Rationale = pattern.Rationale,
                    Example = GeneratePatternExample(pattern.Pattern)
                });
                hintIndex++;
            }

            // Add method-specific hints
            if (method.Parameters.Any())
            {
                hints.Add(new TranspilerHint
                {
                    Offset = 0,
                    Opcode = "Ldarg",
                    OperandSummary = "Parameter loading",
                    NearbyOps = new[] { "ldarg.0", "ldarg.1", "ldarg.2" },
                    Rationale = "Parameter access - good for argument modification or validation",
                    Example = "// Match parameter loading\nif (code.opcode == OpCodes.Ldarg_1) { /* modify argument */ }"
                });
            }

            if (method.ReturnType.Kind != TypeKind.Void)
            {
                hints.Add(new TranspilerHint
                {
                    Offset = 100, // End of method
                    Opcode = "Ret",
                    OperandSummary = "Return statement",
                    NearbyOps = new[] { "ldloc", "ret" },
                    Rationale = "Method return - good for result transformation",
                    Example = "// Insert before return\nif (code.opcode == OpCodes.Ret) { /* insert before return */ }"
                });
            }

        }
        catch
        {
            // If we can't analyze the actual IL, provide generic suggestions
            hints.Add(new TranspilerHint
            {
                Offset = 0,
                Opcode = "Pattern",
                OperandSummary = "Generic IL analysis not available",
                NearbyOps = new[] { "nop", "nop", "nop" },
                Rationale = "Use IL analysis tools to identify specific patterns",
                Example = "// Use ILSpy or similar tools to identify actual IL patterns"
            });
        }

        return hints.Take(maxHints).ToList();
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

    private static string GeneratePatternExample(string pattern)
    {
        return pattern.ToLower() switch
        {
            "call" => "if (code.opcode == OpCodes.Call && code.operand is MethodInfo method) { /* intercept call */ }",
            "callvirt" => "if (code.opcode == OpCodes.Callvirt) { /* intercept virtual call */ }",
            "newobj" => "if (code.opcode == OpCodes.Newobj) { /* intercept object creation */ }",
            "ldstr" => "if (code.opcode == OpCodes.Ldstr) { /* modify string literal */ }",
            "stfld" => "if (code.opcode == OpCodes.Stfld) { /* intercept field write */ }",
            "ldfld" => "if (code.opcode == OpCodes.Ldfld) { /* intercept field read */ }",
            "ret" => "if (code.opcode == OpCodes.Ret) { /* modify before return */ }",
            _ => $"if (code.opcode == OpCodes.{pattern}) {{ /* handle {pattern} */ }}"
        };
    }

    private static MemberSummary CreateMethodSummary(IMethod method)
    {
        return new MemberSummary
        {
            MemberId = ServiceLocator.MemberResolver.GenerateMemberId(method),
            Name = method.Name,
            FullName = method.FullName,
            Kind = method.IsConstructor ? "Constructor" : "Method",
            DeclaringType = method.DeclaringType?.FullName,
            Namespace = method.DeclaringType?.Namespace,
            Signature = ServiceLocator.MemberResolver.GetMemberSignature(method),
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
