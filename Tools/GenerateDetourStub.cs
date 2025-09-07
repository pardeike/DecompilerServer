using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using System.Text;

namespace DecompilerServer;

public static class GenerateDetourStubTool
{
    [McpServerTool, Description("Generate a detour/stub method that calls the original, suitable for patch testing.")]
    public static string GenerateDetourStub(string memberId)
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

            var notes = new List<string>();
            var code = GenerateDetourMethod(method, notes);

            var target = CreateMethodSummary(method);

            var result = new GeneratedCodeResult(target, code, notes);

            return result;
        });
    }

    private static string GenerateDetourMethod(IMethod method, List<string> notes)
    {
        var code = new StringBuilder();
        var declaringType = method.DeclaringType;
        if (declaringType == null)
            throw new ArgumentException("Method has no declaring type");

        // Add header comment
        code.AppendLine("// Generated detour stub method");
        code.AppendLine($"// Original: {method.FullName}");
        code.AppendLine("// This method can be used for testing method interception");
        code.AppendLine();

        // Add using statements
        code.AppendLine("using System;");
        code.AppendLine("using System.Reflection;");
        code.AppendLine("using System.Diagnostics;");
        if (declaringType.Namespace != null && declaringType.Namespace != "System")
        {
            code.AppendLine($"using {declaringType.Namespace};");
        }
        code.AppendLine();

        // Add namespace and class
        code.AppendLine("namespace DetourStubs");
        code.AppendLine("{");
        code.AppendLine("    public static class DetourHelper");
        code.AppendLine("    {");

        // Generate static field for original method
        code.AppendLine("        // Cached original method for reflection-based calls");
        code.AppendLine($"        private static MethodInfo? _originalMethod;");
        code.AppendLine();

        // Generate method signature
        var methodSignature = new StringBuilder();
        methodSignature.Append("        public static ");

        // Return type
        methodSignature.Append(GetTypeDisplayName(method.ReturnType));
        methodSignature.Append(" ");

        // Method name
        methodSignature.Append($"{method.Name}Detour");

        // Generic parameters
        if (method.TypeParameters.Count > 0)
        {
            methodSignature.Append("<");
            methodSignature.Append(string.Join(", ", method.TypeParameters.Select(tp => tp.Name)));
            methodSignature.Append(">");
        }

        // Parameters
        methodSignature.Append("(");

        var parameters = new List<string>();

        // Add instance parameter for instance methods
        if (!method.IsStatic)
        {
            parameters.Add($"{GetTypeDisplayName(declaringType)} __instance");
        }

        // Add original parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            var param = method.Parameters[i];
            var paramStr = GetTypeDisplayName(param.Type);
            paramStr += " " + (string.IsNullOrEmpty(param.Name) ? $"param{i}" : param.Name);
            parameters.Add(paramStr);
        }

        methodSignature.Append(string.Join(", ", parameters));
        methodSignature.Append(")");

        code.AppendLine(methodSignature.ToString());

        // Generic constraints
        foreach (var typeParam in method.TypeParameters)
        {
            if (typeParam.HasValueTypeConstraint || typeParam.HasReferenceTypeConstraint ||
                 typeParam.HasDefaultConstructorConstraint || typeParam.DirectBaseTypes.Any())
            {
                var constraints = new List<string>();

                if (typeParam.HasReferenceTypeConstraint)
                    constraints.Add("class");
                if (typeParam.HasValueTypeConstraint)
                    constraints.Add("struct");

                foreach (var baseType in typeParam.DirectBaseTypes)
                {
                    constraints.Add(GetTypeDisplayName(baseType));
                }

                if (typeParam.HasDefaultConstructorConstraint)
                    constraints.Add("new()");

                if (constraints.Any())
                {
                    code.AppendLine($"            where {typeParam.Name} : {string.Join(", ", constraints)}");
                }
            }
        }

        code.AppendLine("        {");

        // Method body
        code.AppendLine("            // Log method entry");
        code.AppendLine($"            Debug.WriteLine($\"Detour: Entering {method.Name}\");");
        code.AppendLine();

        // Initialize original method if needed
        code.AppendLine("            // Initialize original method reflection info");
        code.AppendLine("            if (_originalMethod == null)");
        code.AppendLine("            {");
        code.AppendLine($"                var type = typeof({GetTypeDisplayName(declaringType)});");

        // Build parameter types array
        if (method.Parameters.Any())
        {
            code.AppendLine("                var paramTypes = new Type[] {");
            foreach (var param in method.Parameters)
            {
                code.AppendLine($"                    typeof({GetTypeDisplayName(param.Type)}),");
            }
            code.AppendLine("                };");
            code.AppendLine($"                _originalMethod = type.GetMethod(\"{method.Name}\", paramTypes);");
        }
        else
        {
            code.AppendLine($"                _originalMethod = type.GetMethod(\"{method.Name}\", Type.EmptyTypes);");
        }

        code.AppendLine("            }");
        code.AppendLine();

        // Call original method
        code.AppendLine("            try");
        code.AppendLine("            {");

        var returnPrefix = method.ReturnType.Kind != TypeKind.Void ? "var result = " : "";
        var castPrefix = method.ReturnType.Kind != TypeKind.Void ? $"({GetTypeDisplayName(method.ReturnType)})" : "";

        // Build parameters array for reflection call
        if (method.Parameters.Any())
        {
            code.AppendLine("                var args = new object?[] {");
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var param = method.Parameters[i];
                var paramName = string.IsNullOrEmpty(param.Name) ? $"param{i}" : param.Name;
                code.AppendLine($"                    {paramName},");
            }
            code.AppendLine("                };");

            if (method.IsStatic)
            {
                code.AppendLine($"                {returnPrefix}{castPrefix}_originalMethod.Invoke(null, args);");
            }
            else
            {
                code.AppendLine($"                {returnPrefix}{castPrefix}_originalMethod.Invoke(__instance, args);");
            }
        }
        else
        {
            if (method.IsStatic)
            {
                code.AppendLine($"                {returnPrefix}{castPrefix}_originalMethod.Invoke(null, null);");
            }
            else
            {
                code.AppendLine($"                {returnPrefix}{castPrefix}_originalMethod.Invoke(__instance, null);");
            }
        }

        code.AppendLine();
        code.AppendLine("                // Log method exit");
        code.AppendLine($"                Debug.WriteLine($\"Detour: Exiting {method.Name}\");");

        if (method.ReturnType.Kind != TypeKind.Void)
        {
            code.AppendLine("                return result;");
        }

        code.AppendLine("            }");
        code.AppendLine("            catch (Exception ex)");
        code.AppendLine("            {");
        code.AppendLine($"                Debug.WriteLine($\"Detour: Exception in {method.Name}: {{ex}}\");");
        code.AppendLine("                throw;");
        code.AppendLine("            }");

        code.AppendLine("        }");
        code.AppendLine("    }");
        code.AppendLine("}");

        // Add notes
        notes.Add("This detour stub method provides logging and delegates to the original method via reflection");
        notes.Add("Use this for testing method interception without changing the original behavior");
        notes.Add("The method signature matches the original with optional instance parameter for non-static methods");

        if (method.IsStatic)
        {
            notes.Add("Original method is static - no instance parameter needed");
        }
        else
        {
            notes.Add("Original method is instance - first parameter is the instance (__instance)");
        }

        if (method.TypeParameters.Any())
        {
            notes.Add("Generic type parameters are preserved but may require additional runtime type handling");
        }

        if (method.Parameters.Any())
        {
            notes.Add("Parameter types are preserved for reflection-based method lookup");
        }

        return code.ToString();
    }

    private static string GetTypeDisplayName(IType type)
    {
        if (type == null)
            return "object";

        // Handle special built-in types
        switch (type.FullName)
        {
            case "System.String":
                return "string";
            case "System.Int32":
                return "int";
            case "System.Int64":
                return "long";
            case "System.Boolean":
                return "bool";
            case "System.Void":
                return "void";
            case "System.Object":
                return "object";
            case "System.Double":
                return "double";
            case "System.Single":
                return "float";
            case "System.Byte":
                return "byte";
            case "System.Int16":
                return "short";
            case "System.Char":
                return "char";
            case "System.Decimal":
                return "decimal";
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
