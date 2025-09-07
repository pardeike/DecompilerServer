using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using System.Text;

namespace DecompilerServer;

public static class GenerateExtensionMethodWrapperTool
{
    [McpServerTool, Description("Generate an extension method wrapper for an instance method to ease call sites in mods.")]
    public static string GenerateExtensionMethodWrapper(string memberId)
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

            if (method.IsStatic)
            {
                throw new ArgumentException($"Method must be an instance method: {memberId}");
            }

            var notes = new List<string>();
            var code = GenerateExtensionMethod(method, notes);

            var target = CreateMethodSummary(method);

            var result = new GeneratedCodeResult(target, code, notes);

            return result;
        });
    }

    private static string GenerateExtensionMethod(IMethod method, List<string> notes)
    {
        var code = new StringBuilder();
        var declaringType = method.DeclaringType;
        if (declaringType == null)
            throw new ArgumentException("Method must have a declaring type");

        // Add header comment
        code.AppendLine("// Generated extension method wrapper");
        code.AppendLine($"// Original: {method.FullName}");
        code.AppendLine();

        // Add using statements
        code.AppendLine("using System;");
        if (declaringType.Namespace != null && declaringType.Namespace != "System")
        {
            code.AppendLine($"using {declaringType.Namespace};");
        }
        code.AppendLine();

        // Add namespace and class
        code.AppendLine("namespace ExtensionMethods");
        code.AppendLine("{");
        code.AppendLine("    public static class Extensions");
        code.AppendLine("    {");

        // Generate method signature
        var methodSignature = new StringBuilder();
        methodSignature.Append("        public static ");

        // Return type
        methodSignature.Append(GetTypeDisplayName(method.ReturnType));
        methodSignature.Append(" ");

        // Method name
        methodSignature.Append(method.Name);

        // Generic parameters
        if (method.TypeParameters.Count > 0)
        {
            methodSignature.Append("<");
            methodSignature.Append(string.Join(", ", method.TypeParameters.Select(tp => tp.Name)));
            methodSignature.Append(">");
        }

        // Parameters - first parameter is the 'this' extension parameter
        methodSignature.Append("(");

        var parameters = new List<string>();

        // Add 'this' parameter for the declaring type
        parameters.Add($"this {GetTypeDisplayName(declaringType)} instance");

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

        // Method body - delegate to original method
        var returnPrefix = method.ReturnType.Kind != TypeKind.Void ? "return " : "";
        var paramNames = new List<string>();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            var param = method.Parameters[i];
            paramNames.Add(string.IsNullOrEmpty(param.Name) ? $"param{i}" : param.Name);
        }

        code.AppendLine($"            {returnPrefix}instance.{method.Name}({string.Join(", ", paramNames)});");

        code.AppendLine("        }");
        code.AppendLine("    }");
        code.AppendLine("}");

        // Add notes
        notes.Add("This extension method allows calling the instance method using extension syntax");
        notes.Add("Usage: instance.ExtensionMethodName(params) instead of ExtensionMethods.Extensions.MethodName(instance, params)");

        if (method.TypeParameters.Any())
        {
            notes.Add("Generic type parameters are preserved from the original method");
        }

        if (method.Parameters.Any())
        {
            notes.Add("Parameter names are preserved from the original method");
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
