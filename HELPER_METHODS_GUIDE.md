# DecompilerServer Helper Methods Guide

This document provides a comprehensive guide to the helper methods implemented for the Unity Assembly-CSharp.dll MCP server. These helper methods provide the foundation for implementing all 38 endpoint tools efficiently and consistently.

## Core Helper Services

### 1. AssemblyContextManager
**Purpose:** Manages the loaded assembly context, including PEFile, TypeSystem, and CSharpDecompiler instances.

**Key Methods:**
- `LoadAssembly(gameDir, assemblyFile, additionalSearchDirs)` - Load and initialize assembly context
- `GetDecompiler()` - Get current CSharpDecompiler instance
- `GetCompilation()` - Get current ICompilation/TypeSystem
- `GetAllTypes()` - Get all types in the assembly
- `GetNamespaces()` - Get all namespaces
- `UpdateSettings(settings)` - Update decompiler settings

**Properties:**
- `IsLoaded` - Whether assembly is loaded
- `Mvid` - Assembly MVID
- `AssemblyPath` - Path to loaded assembly
- `TypeCount`, `NamespaceCount` - Basic statistics

### 2. MemberResolver
**Purpose:** Central service for resolving member IDs to IEntity objects and handling member ID normalization.

**Key Methods:**
- `ResolveMember(memberId)` - Resolve any member ID to IEntity
- `ResolveType(typeId)` - Resolve specifically to IType
- `ResolveMethod(methodId)` - Resolve specifically to IMethod
- `NormalizeMemberId(memberId)` - Normalize to consistent format
- `GenerateMemberId(entity)` - Generate ID from IEntity
- `GetMemberSignature(entity)` - Get human-readable signature
- `IsValidMemberId(memberId)` - Validate member ID format

### 3. DecompilerService
**Purpose:** Handles C# decompilation with caching and provides source document management.

**Key Methods:**
- `DecompileMember(memberId, includeHeader)` - Decompile and cache source document
- `GetSourceSlice(memberId, startLine, endLine)` - Get line range from cached source
- `BatchDecompile(memberIds)` - Efficiently decompile multiple members
- `ClearCache()` - Clear source cache
- `GetCacheStats()` - Get cache statistics

### 4. SearchServiceBase
**Purpose:** Base class providing common search and pagination functionality.

**Key Methods:**
- `SearchTypes(query, regex, filters...)` - Search types with various filters
- `SearchMembers(query, regex, filters...)` - Search members with rich filtering
- `ApplyPagination(source, limit, cursor)` - Apply cursor-based pagination

### 5. UsageAnalyzer
**Purpose:** Analyzes IL code to find usage patterns, callers, and callees (framework for IL analysis).

**Key Methods:**
- `FindUsages(memberId, limit, cursor)` - Find all usages of a member
- `FindCallers(methodId, limit, cursor)` - Find direct callers of a method
- `FindCallees(methodId, limit, cursor)` - Find what a method calls
- `FindStringLiterals(query, regex, limit, cursor)` - Find string literals

**Note:** IL analysis methods are framework implementations - full IL inspection would require additional development.

### 6. InheritanceAnalyzer
**Purpose:** Analyzes type inheritance relationships, interface implementations, and override chains.

**Key Methods:**
- `FindBaseTypes(typeId, limit)` - Get inheritance chain upward
- `FindDerivedTypes(typeId, limit, cursor)` - Get derived types
- `GetImplementations(typeId)` - Get interface implementations
- `FindImplementors(interfaceId, limit, cursor)` - Find types implementing interface
- `GetOverrides(methodId)` - Get method override chain
- `GetOverloads(methodId)` - Get method overloads
- `GetMembersOfType(typeId, kind)` - Get all members of a type

### 7. ResponseFormatter
**Purpose:** Provides standardized JSON response formatting for all MCP server endpoints.

**Key Methods:**
- `Success(data)` - Format successful response with data
- `Error(message, details)` - Format error response
- `SearchResult(result)` - Format paginated search results
- `Status(status)` - Format server status
- `SourceDocument(document)` - Format source document metadata
- `SourceSlice(slice)` - Format source code slice
- `TryExecute(operation)` - Execute with automatic error handling

## Implementation Patterns

### Pattern 1: Basic Endpoint (Status, ListNamespaces)
```csharp
public static string MyEndpoint()
{
    return ResponseFormatter.TryExecute(() => {
        if (!contextManager.IsLoaded)
            throw new InvalidOperationException("No assembly loaded");
        
        var data = GetMyData();
        return data;
    });
}
```

### Pattern 2: Member Resolution Endpoint (GetMemberDetails, GetDecompiledSource)
```csharp
public static string MyEndpoint(string memberId)
{
    return ResponseFormatter.TryExecute(() => {
        var member = memberResolver.ResolveMember(memberId);
        if (member == null)
            throw new ArgumentException($"Cannot resolve member: {memberId}");
        
        var result = ProcessMember(member);
        return result;
    });
}
```

### Pattern 3: Search Endpoint (SearchTypes, SearchMembers)
```csharp
public static string MySearchEndpoint(string query, bool regex, int limit, string cursor)
{
    return ResponseFormatter.TryExecute(() => {
        var results = searchService.SearchSomething(query, regex, limit, cursor);
        return ResponseFormatter.SearchResult(results);
    });
}
```

### Pattern 4: Code Generation Endpoint (GenerateHarmonyPatchSkeleton)
```csharp
public static string MyGenerateEndpoint(string memberId, string options)
{
    return ResponseFormatter.TryExecute(() => {
        var member = memberResolver.ResolveMember(memberId);
        var target = CreateMemberSummary(member);
        var code = GenerateCode(member, options);
        var notes = GetGenerationNotes();
        
        return new GeneratedCodeResult { Target = target, Code = code, Notes = notes };
    });
}
```

## Additional Helper Methods Needed

Based on the endpoint analysis, these additional helper methods would be beneficial:

1. **ILService** - For GetIL endpoint (IL disassembly and ILAst generation)
2. **AttributeExtractor** - For extracting and formatting member attributes
3. **XmlDocumentationProvider** - For extracting XML documentation
4. **HarmonyCodeGenerator** - For generating Harmony patch skeletons
5. **DetourCodeGenerator** - For generating detour stubs
6. **ExtensionMethodGenerator** - For generating extension method wrappers

## Endpoint Implementation Guide

Each endpoint now includes specific helper method recommendations in their comments. The pattern is:

```
Helper methods to use:
- HelperService.Method() for primary functionality
- ResponseFormatter.TryExecute() for error handling  
- ResponseFormatter.SpecificMethod() for response formatting
- Additional helper needed: SpecificService for specialized functionality
```

This provides clear guidance for implementing each endpoint consistently while leveraging the shared helper infrastructure.