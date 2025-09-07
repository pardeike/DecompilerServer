using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer.Services;

/// <summary>
/// Analyzes type inheritance relationships, interface implementations, and override chains.
/// Provides functionality for navigating type hierarchies.
/// </summary>
public class InheritanceAnalyzer
{
	private readonly AssemblyContextManager _contextManager;
	private readonly MemberResolver _memberResolver;

	public InheritanceAnalyzer(AssemblyContextManager contextManager, MemberResolver memberResolver)
	{
		_contextManager = contextManager;
		_memberResolver = memberResolver;
	}

	/// <summary>
	/// Find base types of a given type
	/// </summary>
	public IEnumerable<MemberSummary> FindBaseTypes(string typeId, int limit = 10)
	{
		var type = _memberResolver.ResolveType(typeId)?.GetDefinition();
		if (type == null)
			return Enumerable.Empty<MemberSummary>();

		var baseTypes = new List<MemberSummary>();
		var currentType = type.DirectBaseTypes.FirstOrDefault()?.GetDefinition();

		while (currentType != null && baseTypes.Count < limit)
		{
			// Skip System.Object as it's implicit
			if (currentType.FullName != "System.Object")
			{
				baseTypes.Add(CreateTypeSummary(currentType));
			}

			currentType = currentType.DirectBaseTypes.FirstOrDefault()?.GetDefinition();
		}

		return baseTypes;
	}

	/// <summary>
	/// Find derived types of a given type
	/// </summary>
	public IEnumerable<MemberSummary> FindDerivedTypes(string typeId, int limit = 100, string? cursor = null)
	{
		var targetType = _memberResolver.ResolveType(typeId)?.GetDefinition();
		if (targetType == null)
			return Enumerable.Empty<MemberSummary>();

		var compilation = _contextManager.GetCompilation();
		var allTypes = _contextManager.GetAllTypes();

		var derivedTypes = allTypes.Where(type => 
			IsDirectOrIndirectDerivedFrom(type, targetType));

		// Apply pagination
		var startIndex = 0;
		if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
		{
			startIndex = cursorIndex;
		}

		return derivedTypes
			.Skip(startIndex)
			.Take(limit)
			.Select(CreateTypeSummary);
	}

	/// <summary>
	/// Get interface implementations for a type
	/// </summary>
	public IEnumerable<MemberSummary> GetImplementations(string typeId)
	{
		var type = _memberResolver.ResolveType(typeId)?.GetDefinition();
		if (type == null)
			return Enumerable.Empty<MemberSummary>();

		return type.DirectBaseTypes
			.Where(baseType => baseType.Kind == TypeKind.Interface)
			.Select(iface => iface.GetDefinition())
			.Where(iface => iface != null)
			.Select(iface => CreateTypeSummary(iface!));
	}

	/// <summary>
	/// Find types that implement a specific interface
	/// </summary>
	public IEnumerable<MemberSummary> FindImplementors(string interfaceId, int limit = 100, string? cursor = null)
	{
		var targetInterface = _memberResolver.ResolveType(interfaceId)?.GetDefinition();
		if (targetInterface == null || targetInterface.Kind != TypeKind.Interface)
			return Enumerable.Empty<MemberSummary>();

		var allTypes = _contextManager.GetAllTypes();

		var implementors = allTypes.Where(type => 
			ImplementsInterface(type, targetInterface));

		// Apply pagination
		var startIndex = 0;
		if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
		{
			startIndex = cursorIndex;
		}

		return implementors
			.Skip(startIndex)
			.Take(limit)
			.Select(CreateTypeSummary);
	}

	/// <summary>
	/// Get method override chain
	/// </summary>
	public IEnumerable<MemberSummary> GetOverrides(string methodId)
	{
		var method = _memberResolver.ResolveMethod(methodId);
		if (method == null)
			return Enumerable.Empty<MemberSummary>();

		var overrides = new List<MemberSummary>();

		// Find base definition
		var baseDefinition = GetBaseDefinition(method);
		if (baseDefinition != null && baseDefinition != method)
		{
			overrides.Add(CreateMethodSummary(baseDefinition));
		}

		// Find overridden methods
		var overriddenMethods = FindOverriddenMethods(method);
		overrides.AddRange(overriddenMethods.Select(CreateMethodSummary));

		return overrides;
	}

	/// <summary>
	/// Get method overloads
	/// </summary>
	public IEnumerable<MemberSummary> GetOverloads(string methodId)
	{
		var method = _memberResolver.ResolveMethod(methodId);
		if (method == null || method.DeclaringType == null)
			return Enumerable.Empty<MemberSummary>();

		var declaringType = method.DeclaringType.GetDefinition();
		if (declaringType == null)
			return Enumerable.Empty<MemberSummary>();

		return declaringType.Methods
			.Where(m => m.Name == method.Name && m != method)
			.Select(CreateMethodSummary);
	}

	/// <summary>
	/// Find all members of a specific type
	/// </summary>
	public IEnumerable<MemberSummary> GetMembersOfType(string typeId, string? kind = null)
	{
		var type = _memberResolver.ResolveType(typeId)?.GetDefinition();
		if (type == null)
			return Enumerable.Empty<MemberSummary>();

		var members = new List<IMember>();

		// Add different kinds of members based on filter
		if (string.IsNullOrEmpty(kind) || kind.Equals("method", StringComparison.OrdinalIgnoreCase))
			members.AddRange(type.Methods.Cast<IMember>());

		if (string.IsNullOrEmpty(kind) || kind.Equals("field", StringComparison.OrdinalIgnoreCase))
			members.AddRange(type.Fields.Cast<IMember>());

		if (string.IsNullOrEmpty(kind) || kind.Equals("property", StringComparison.OrdinalIgnoreCase))
			members.AddRange(type.Properties.Cast<IMember>());

		if (string.IsNullOrEmpty(kind) || kind.Equals("event", StringComparison.OrdinalIgnoreCase))
			members.AddRange(type.Events.Cast<IMember>());

		return members.Select(CreateMemberSummary);
	}

	private bool IsDirectOrIndirectDerivedFrom(ITypeDefinition type, ITypeDefinition targetBaseType)
	{
		var currentType = type;
		var visited = new HashSet<ITypeDefinition>();

		while (currentType != null && !visited.Contains(currentType))
		{
			visited.Add(currentType);

			foreach (var baseType in currentType.DirectBaseTypes)
			{
				var baseTypeDef = baseType.GetDefinition();
				if (baseTypeDef == targetBaseType)
					return true;

				if (baseTypeDef != null)
					currentType = baseTypeDef;
			}

			// Move to next base type
			currentType = currentType.DirectBaseTypes.FirstOrDefault()?.GetDefinition();
		}

		return false;
	}

	private bool ImplementsInterface(ITypeDefinition type, ITypeDefinition targetInterface)
	{
		return type.DirectBaseTypes.Any(baseType => 
		{
			var baseTypeDef = baseType.GetDefinition();
			return baseTypeDef == targetInterface;
		});
	}

	private IMethod? GetBaseDefinition(IMethod method)
	{
		// Find the base virtual method that this method overrides
		if (!method.IsOverride)
			return null;

		var baseType = method.DeclaringType?.DirectBaseTypes.FirstOrDefault()?.GetDefinition();
		while (baseType != null)
		{
			var baseMethod = baseType.Methods.FirstOrDefault(m => 
				m.Name == method.Name && 
				m.IsVirtual && 
				SignaturesMatch(m, method));

			if (baseMethod != null)
				return baseMethod;

			baseType = baseType.DirectBaseTypes.FirstOrDefault()?.GetDefinition();
		}

		return null;
	}

	private IEnumerable<IMethod> FindOverriddenMethods(IMethod method)
	{
		// Find methods that override this method in derived types
		var declaringTypeDef = method.DeclaringType?.GetDefinition();
		if (declaringTypeDef == null)
			return Enumerable.Empty<IMethod>();

		var derivedTypes = FindDerivedTypes(_memberResolver.GenerateMemberId(declaringTypeDef))
			.Select(summary => _memberResolver.ResolveType(summary.MemberId)?.GetDefinition())
			.Where(type => type != null)
			.Cast<ITypeDefinition>();

		var overriddenMethods = new List<IMethod>();

		foreach (var derivedType in derivedTypes)
		{
			var overridingMethod = derivedType.Methods.FirstOrDefault(m => 
				m.Name == method.Name && 
				m.IsOverride && 
				SignaturesMatch(m, method));

			if (overridingMethod != null)
				overriddenMethods.Add(overridingMethod);
		}

		return overriddenMethods;
	}

	private bool SignaturesMatch(IMethod method1, IMethod method2)
	{
		// Simplified signature matching - full implementation would compare
		// parameter types, return type, generic parameters, etc.
		return method1.Parameters.Count == method2.Parameters.Count;
	}

	private MemberSummary CreateTypeSummary(ITypeDefinition type)
	{
		return new MemberSummary
		{
			MemberId = _memberResolver.GenerateMemberId(type),
			Name = type.Name,
			FullName = type.FullName,
			Kind = "Type",
			DeclaringType = type.DeclaringType?.FullName,
			Namespace = type.Namespace,
			Signature = _memberResolver.GetMemberSignature(type),
			Accessibility = type.Accessibility.ToString(),
			IsStatic = type.IsStatic,
			IsAbstract = type.IsAbstract
		};
	}

	private MemberSummary CreateMethodSummary(IMethod method)
	{
		return new MemberSummary
		{
			MemberId = _memberResolver.GenerateMemberId(method),
			Name = method.Name,
			FullName = method.FullName,
			Kind = method.IsConstructor ? "Constructor" : "Method",
			DeclaringType = method.DeclaringType?.FullName,
			Namespace = method.DeclaringType?.Namespace,
			Signature = _memberResolver.GetMemberSignature(method),
			Accessibility = method.Accessibility.ToString(),
			IsStatic = method.IsStatic,
			IsAbstract = method.IsAbstract,
			IsVirtual = method.IsVirtual
		};
	}

	private MemberSummary CreateMemberSummary(IMember member)
	{
		return new MemberSummary
		{
			MemberId = _memberResolver.GenerateMemberId(member),
			Name = member.Name,
			FullName = member.FullName,
			Kind = GetMemberKind(member),
			DeclaringType = member.DeclaringType?.FullName,
			Namespace = member.DeclaringType?.Namespace,
			Signature = _memberResolver.GetMemberSignature(member),
			Accessibility = member.Accessibility.ToString(),
			IsStatic = member.IsStatic,
			IsAbstract = member.IsAbstract,
			IsVirtual = member.IsVirtual
		};
	}

	private string GetMemberKind(IMember member)
	{
		return member switch
		{
			IMethod method when method.IsConstructor => "Constructor",
			IMethod => "Method",
			IField => "Field",
			IProperty => "Property",
			IEvent => "Event",
			_ => "Unknown"
		};
	}
}