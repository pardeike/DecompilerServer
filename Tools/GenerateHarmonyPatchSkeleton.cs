using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using System.Text;

namespace DecompilerServer;

public static class GenerateHarmonyPatchSkeletonTool
{
    [McpServerTool, Description("Generate a Harmony patch skeleton for a given member.")]
    public static string GenerateHarmonyPatchSkeleton(string memberId, string patchKinds = "Prefix,Postfix,Transpiler,Finalizer", bool includeReflectionTargeting = true)
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

            var kinds = patchKinds.Split(',').Select(k => k.Trim()).ToArray();
            var notes = new List<string>();
            var code = GenerateHarmonyPatch(method, kinds, includeReflectionTargeting, notes);

            var target = CreateMethodSummary(method);

            var result = new GeneratedCodeResult(target, code, notes);

            return result;
        });
    }

    private static string GenerateHarmonyPatch(IMethod method, string[] patchKinds, bool includeReflectionTargeting, List<string> notes)
    {
        var code = new StringBuilder();
        var declaringType = method.DeclaringType;

        // Add header comment
        code.AppendLine("// Generated Harmony patch skeleton");
        code.AppendLine($"// Target: {method.FullName}");
        code.AppendLine("// This class provides patch methods for the target method");
        code.AppendLine();

        // Add using statements
        code.AppendLine("using System;");
        code.AppendLine("using System.Collections.Generic;");
        code.AppendLine("using System.Reflection;");
        code.AppendLine("using System.Reflection.Emit;");
        code.AppendLine("using HarmonyLib;");
        if (declaringType?.Namespace != null && declaringType.Namespace != "System")
        {
            code.AppendLine($"using {declaringType.Namespace};");
        }
        code.AppendLine();

        // Add namespace and class
        code.AppendLine("namespace HarmonyPatches");
        code.AppendLine("{");

        // Generate HarmonyPatch attribute
        code.AppendLine("    [HarmonyPatch]");
        code.AppendLine($"    public class {SanitizeClassName(method.DeclaringType?.Name ?? "Unknown")}_{method.Name}Patch");
        code.AppendLine("    {");

        // Add target method specification
        code.AppendLine("        // Target method specification");
        code.AppendLine("        [HarmonyTargetMethod]");
        code.AppendLine("        static MethodBase TargetMethod()");
        code.AppendLine("        {");

        if (includeReflectionTargeting)
        {
            code.AppendLine("            // Using AccessTools for precise targeting");

            if (method.Parameters.Any())
            {
                code.AppendLine("            var parameterTypes = new Type[]");
                code.AppendLine("            {");
                foreach (var param in method.Parameters)
                {
                    code.AppendLine($"                typeof({GetTypeDisplayName(param.Type)}),");
                }
                code.AppendLine("            };");
                code.AppendLine();
                code.AppendLine($"            return AccessTools.Method(typeof({GetTypeDisplayName(declaringType)}), \"{method.Name}\", parameterTypes);");
            }
            else
            {
                code.AppendLine($"            return AccessTools.Method(typeof({GetTypeDisplayName(declaringType)}), \"{method.Name}\");");
            }
        }
        else
        {
            code.AppendLine("            // Alternative: Use type and method name");
            code.AppendLine($"            return typeof({GetTypeDisplayName(declaringType)}).GetMethod(\"{method.Name}\");");
        }

        code.AppendLine("        }");
        code.AppendLine();

        // Generate patch methods based on requested kinds
        foreach (var kind in patchKinds)
        {
            switch (kind.ToLower())
            {
                case "prefix":
                    GeneratePrefixPatch(code, method);
                    break;
                case "postfix":
                    GeneratePostfixPatch(code, method);
                    break;
                case "transpiler":
                    GenerateTranspilerPatch(code, method);
                    break;
                case "finalizer":
                    GenerateFinalizerPatch(code, method);
                    break;
            }
        }

        code.AppendLine("    }");
        code.AppendLine("}");

        // Add notes
        notes.Add("This Harmony patch skeleton provides template methods for intercepting the target method");
        notes.Add("Uncomment and modify the patch methods you need");
        notes.Add("Remember to apply the patches using Harmony.CreateAndPatchAll() or individual patch methods");

        if (includeReflectionTargeting)
        {
            notes.Add("Uses AccessTools for precise method targeting with parameter type arrays");
        }

        if (method.IsStatic)
        {
            notes.Add("Target method is static - no __instance parameter available");
        }
        else
        {
            notes.Add("Target method is instance method - __instance parameter available in patches");
        }

        if (method.ReturnType.Kind != TypeKind.Void)
        {
            notes.Add("Target method has return value - __result parameter available in Postfix/Finalizer");
        }

        return code.ToString();
    }

    private static void GeneratePrefixPatch(StringBuilder code, IMethod method)
    {
        code.AppendLine("        // Prefix patch - runs before the original method");
        code.AppendLine("        [HarmonyPrefix]");
        code.Append("        static bool Prefix(");

        var parameters = new List<string>();

        // Add instance parameter for non-static methods
        if (!method.IsStatic)
        {
            parameters.Add($"{GetTypeDisplayName(method.DeclaringType)} __instance");
        }

        // Add original method parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            var param = method.Parameters[i];
            var paramStr = GetTypeDisplayName(param.Type);
            paramStr += " " + (string.IsNullOrEmpty(param.Name) ? $"param{i}" : param.Name);
            parameters.Add(paramStr);
        }

        code.Append(string.Join(", ", parameters));
        code.AppendLine(")");
        code.AppendLine("        {");
        code.AppendLine("            // Add your prefix logic here");
        code.AppendLine("            // Return false to skip original method execution");
        code.AppendLine("            // Return true to continue with original method");
        code.AppendLine("            return true;");
        code.AppendLine("        }");
        code.AppendLine();
    }

    private static void GeneratePostfixPatch(StringBuilder code, IMethod method)
    {
        code.AppendLine("        // Postfix patch - runs after the original method");
        code.AppendLine("        [HarmonyPostfix]");
        code.Append("        static void Postfix(");

        var parameters = new List<string>();

        // Add instance parameter for non-static methods
        if (!method.IsStatic)
        {
            parameters.Add($"{GetTypeDisplayName(method.DeclaringType)} __instance");
        }

        // Add original method parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            var param = method.Parameters[i];
            var paramStr = GetTypeDisplayName(param.Type);
            paramStr += " " + (string.IsNullOrEmpty(param.Name) ? $"param{i}" : param.Name);
            parameters.Add(paramStr);
        }

        // Add result parameter for non-void methods
        if (method.ReturnType.Kind != TypeKind.Void)
        {
            parameters.Add($"ref {GetTypeDisplayName(method.ReturnType)} __result");
        }

        code.Append(string.Join(", ", parameters));
        code.AppendLine(")");
        code.AppendLine("        {");
        code.AppendLine("            // Add your postfix logic here");
        code.AppendLine("            // You can modify __result for non-void methods");
        code.AppendLine("        }");
        code.AppendLine();
    }

    private static void GenerateTranspilerPatch(StringBuilder code, IMethod method)
    {
        code.AppendLine("        // Transpiler patch - modifies IL instructions");
        code.AppendLine("        [HarmonyTranspiler]");
        code.AppendLine("        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)");
        code.AppendLine("        {");
        code.AppendLine("            var codes = new List<CodeInstruction>(instructions);");
        code.AppendLine();
        code.AppendLine("            // Add your IL modification logic here");
        code.AppendLine("            // Example: Replace specific opcodes or inject new instructions");
        code.AppendLine("            /*");
        code.AppendLine("            for (int i = 0; i < codes.Count; i++)");
        code.AppendLine("            {");
        code.AppendLine("                if (codes[i].opcode == OpCodes.Ldstr && codes[i].operand.ToString() == \"target string\")");
        code.AppendLine("                {");
        code.AppendLine("                    codes[i].operand = \"replacement string\";");
        code.AppendLine("                }");
        code.AppendLine("            }");
        code.AppendLine("            */");
        code.AppendLine();
        code.AppendLine("            return codes.AsEnumerable();");
        code.AppendLine("        }");
        code.AppendLine();
    }

    private static void GenerateFinalizerPatch(StringBuilder code, IMethod method)
    {
        code.AppendLine("        // Finalizer patch - runs after method completion (even on exceptions)");
        code.AppendLine("        [HarmonyFinalizer]");
        code.Append("        static Exception? Finalizer(");

        var parameters = new List<string>();

        // Add instance parameter for non-static methods
        if (!method.IsStatic)
        {
            parameters.Add($"{GetTypeDisplayName(method.DeclaringType)} __instance");
        }

        // Add result parameter for non-void methods
        if (method.ReturnType.Kind != TypeKind.Void)
        {
            parameters.Add($"ref {GetTypeDisplayName(method.ReturnType)} __result");
        }

        // Add exception parameter
        parameters.Add("Exception __exception");

        code.Append(string.Join(", ", parameters));
        code.AppendLine(")");
        code.AppendLine("        {");
        code.AppendLine("            // Add your finalizer logic here");
        code.AppendLine("            // This runs regardless of whether an exception occurred");
        code.AppendLine("            // Return null to not change exception handling");
        code.AppendLine("            // Return a new exception to replace the original");
        code.AppendLine("            // Return __exception to re-throw the original exception");
        code.AppendLine("            return null;");
        code.AppendLine("        }");
        code.AppendLine();
    }

    private static string GetTypeDisplayName(IType type)
    {
        if (type == null) return "object";

        // Handle special built-in types
        switch (type.FullName)
        {
            case "System.String": return "string";
            case "System.Int32": return "int";
            case "System.Int64": return "long";
            case "System.Boolean": return "bool";
            case "System.Void": return "void";
            case "System.Object": return "object";
            case "System.Double": return "double";
            case "System.Single": return "float";
            case "System.Byte": return "byte";
            case "System.Int16": return "short";
            case "System.Char": return "char";
            case "System.Decimal": return "decimal";
        }

        // Handle generic types
        if (type is ParameterizedType paramType)
        {
            var baseName = GetTypeDisplayName(paramType.GenericType);
            var args = string.Join(", ", paramType.TypeArguments.Select(GetTypeDisplayName));
            return $"{baseName}<{args}>";
        }

        // Handle arrays
        if (type.Kind == TypeKind.Array)
        {
            var elementType = ((ArrayType)type).ElementType;
            return GetTypeDisplayName(elementType) + "[]";
        }

        // Use simple name for same namespace, full name otherwise
        return type.Name;
    }

    private static string SanitizeClassName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";

        // Remove characters that aren't valid in C# class names
        var result = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                result.Append(c);
            }
            else if (result.Length > 0) // Don't start with underscore
            {
                result.Append('_');
            }
        }

        return result.Length > 0 ? result.ToString() : "Unknown";
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