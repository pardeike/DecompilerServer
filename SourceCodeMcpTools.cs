using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

/// <summary>
/// MCP tool skeleton for decompiling and exploring Unity's Assembly-CSharp.dll via ICSharpCode.Decompiler.
/// Note: All endpoints return an AI implementation prompt, not a working implementation.
/// </summary>
public static class SourceCodeMcpTools
{
	// ---------- Shared guidance embedded into every endpoint prompt ----------
	private const string CommonImplementorGuide = """
You are implementing an MCP server tool endpoint for a C# project that explores Game’s Assembly-CSharp.dll using ICSharpCode.Decompiler.

Core constraints:
- Keep one in-memory context: PEFile (game assembly), UniversalAssemblyResolver (search dirs include Game_Data/Managed), DecompilerTypeSystem, and a configured CSharpDecompiler.
- Build stable IDs as "<mvid-32hex>:<token-8hex>:<kind-code>" where kind codes: T=Type, M=Method/Ctor, P=Property, F=Field, E=Event, N=Namespace.
- Provide thread-safe access with ReaderWriterLockSlim. Fast reads, guarded writes.
- Use lazy indexes. On first access build minimal name/namespace/type maps. Deep indexes (string literals, attribute hits) behind WarmIndex.
- Always paginate. Inputs: limit (default 50, max 500), cursor string. Output: { items, nextCursor }.
- Serialization: System.Text.Json with camelCase, ignore nulls, deterministic ordering for stable diffs.
- Source cache: per-member decompiled C# string plus line index (offsets). Allow ranged retrieval without re-decompile.
- IL: produce readable IL using ICSharpCode.Decompiler.Disassembler + MetadataReader. Avoid reflection-only load.
- Errors: return { error: { code, message, detail? } }. Never throw across MCP boundary.

Recommended types (define in a shared Models.cs):
- record MemberHandle(string Id, string Kind, string Name, string DeclaringType, string Namespace);
- enum MemberKind { Namespace, Type, Method, Property, Field, Event, Constructor }
- record MemberSummary(MemberHandle Handle, string Signature, string Accessibility, bool Static, bool Virtual, bool Abstract, int GenericArity, string? DocSummary);
- record SearchResult<T>(IReadOnlyList<T> Items, string? NextCursor, int TotalEstimate);
- record MemberDetails(MemberSummary Summary, IReadOnlyList<string> Attributes, string? XmlDoc, string? BaseDefinitionId, IReadOnlyList<string> OverrideIds, IReadOnlyList<string> ImplementorIds);
- record SourceDocument(string MemberId, string Language, int TotalLines, string Hash, string? Header, string? Footer);
- record SourceSlice(string MemberId, int StartLine, int EndLine, bool LineNumbers, string Text);
- record UsageRef(MemberHandle InMember, string Kind, int? Line, string? Snippet);
- record GraphResult(IReadOnlyList<MemberHandle> Nodes, IReadOnlyList<(string FromId, string ToId, string Kind)> Edges, string? NextCursor);
- record Stats(long LoadedAtUnix, string Mvid, int Types, int Methods, int Properties, int Fields, long CacheBytes, double IndexBuildSeconds);

Implementation tips:
- Resolver: UniversalAssemblyResolver(gameDir, false, null) and add search dirs: gameDir, gameDir/MonoBleedingEdge/lib/mono, Game*_Data/Managed, UnityPlayer dir as needed.
- Decompiler settings: new CSharpDecompilerSettings { UsingDeclarations = true, ShowXmlDocumentation = true, NamedArguments = true, ... }; tweak via SetDecompileSettings.
- Token/ID utils: from IEntity.MetadataToken and PEFile.Metadata; include module MVID (Guid.ToString("N")).
- Searching:
  * Namespaces: from type system namespaces.
  * Types/members: case-insensitive substring by default, optional regex, optional "qualified" search (e.g., "Verse.Pawn:Tick" or "Pawn.Tick").
  * Filters: kind, namespace, declaring type, attribute, return type, parameter types, accessibility, static/virtual/abstract, generic arity.
- Usages:
  * For target method token, scan candidate methods’ IL or ILAst for call/callvirt/newobj/ldfld/stfld/ldsfld/stsfld matching member tokens.
  * Paginate; don’t fully scan on first page unless WarmIndex was run.
- Graph:
  * Callers = reverse edges from usages.
  * Callees = scan single method body for invocations and field/property accesses.
  * Overrides/Implementations via DecompilerTypeSystem inheritance APIs.
- String literal search:
  * Either pre-index literals (if WarmIndex deep) or lazily scan decompiled text per page.
- Caching keys:
  * Member -> decompiled C# hash (e.g., xxHash64 of code) for change detection.
- Harmony helpers:
  * Emit minimal compilable stubs for Prefix/Postfix/Transpiler/Finalizer with correct generic constraints captured, method binding via HarmonyMethod or AccessTools.
- Safety:
  * Never load untrusted paths. Only within configured game root + allow-list of extra dirs.

Return JSON as the endpoint result. Keep outputs compact and line-range friendly.
""";

	private static string BuildPrompt(string endpointName, string specifics) =>
		$"{CommonImplementorGuide}\n\n### Endpoint: {endpointName}\n{specifics}";

	// ----------------------------
	// 1) Setup and lifecycle
	// ----------------------------

	[McpServerTool, Description("Load or reload the Game Assembly-CSharp.dll and build minimal indexes.")]
	public static string LoadAssembly(
		string gameDir,
		string assemblyFile = "Assembly-CSharp.dll",
		string[]? additionalSearchDirs = null,
		bool rebuildIndex = true)
	{
		return BuildPrompt(nameof(LoadAssembly), """
Goal: Initialize or refresh the global decompiler context.

Inputs:
- gameDir: absolute path to Game install root (contains Game*_Data/Managed).
- assemblyFile: relative or absolute path to Assembly-CSharp.dll.
- additionalSearchDirs: optional extra directories to add to resolver.
- rebuildIndex: if true, drop caches and rebuild minimal indexes.

Behavior:
- Validate paths and file existence. Return error if missing.
- Dispose existing context if already loaded.
- Create UniversalAssemblyResolver and add search dirs (Managed, MonoBleedingEdge, Unity, plus additionalSearchDirs).
- Load PEFile, TypeSystem, and CSharpDecompiler.
- Record MVID and build minimal indexes: namespaces, type name map, simple member name map.
- Return:
  {
    status: "ok",
    mvid: "<32hex>",
    assemblyPath: "<resolved>",
    types: <count>,
    methods: <count-estimate>,
    namespaces: <count>,
    warmed: false
  }
""");
	}

	[McpServerTool, Description("Unload assembly and free all caches and indexes.")]
	public static string Unload()
	{
		return BuildPrompt(nameof(Unload), """
Goal: Dispose the global decompiler context and clear caches.

Behavior:
- Acquire write lock, dispose PEFile and resolver, clear dictionaries, reset stats.
- Return { status: "ok" }.
""");
	}

	[McpServerTool, Description("Get current server status, including assembly MVID and cache stats.")]
	public static string Status()
	{
		return BuildPrompt(nameof(Status), """
Goal: Report whether an assembly is loaded and key counters.

Output:
{
  loaded: bool,
  mvid?: string,
  assemblyPath?: string,
  startedAtUnix?: long,
  settings?: { ... decompiler flags ... },
  stats?: Stats,
  indexes: { namespaces: int, types: int, nameIndexReady: bool, stringLiteralIndexReady: bool }
}
""");
	}

	[McpServerTool, Description("Update decompiler settings (e.g., UsingDeclarations, ShowXmlDocumentation).")]
	public static string SetDecompileSettings(Dictionary<string, object> settings)
	{
		return BuildPrompt(nameof(SetDecompileSettings), """
Goal: Apply a subset of CSharpDecompilerSettings at runtime.

Inputs: settings as string->value. Supported keys:
- "UsingDeclarations", "ShowXmlDocumentation", "NamedArguments", "MakeAssignmentExpressions", "AlwaysUseBraces", ...
Behavior:
- Validate known keys, ignore unknown, persist to context, clear code cache (since output can change).
- Return current effective settings.
""");
	}

	[McpServerTool, Description("Optionally precompute heavier indexes (string literals, attribute hits). Time-boxed.")]
	public static string WarmIndex(bool deep = false, double maxSeconds = 5.0)
	{
		return BuildPrompt(nameof(WarmIndex), """
Goal: Time-boxed background-style index build within the request.

Behavior:
- Within maxSeconds, build or extend:
  * string literal index (memberId -> literals)
  * attribute index (attribute full name -> memberIds)
  * quick callers map for hot methods (heuristic: size/complexity)
- Report progress:
  { status: "ok", deepRequested: bool, elapsedMs: int, built: ["strings","attributes","callers?"] }
""");
	}

	[McpServerTool, Description("Clear caches and indexes. Scope: 'all' | 'source' | 'usages' | 'attributes'.")]
	public static string ClearCaches(string scope = "all")
	{
		return BuildPrompt(nameof(ClearCaches), """
Goal: Free memory and force recomputation as needed.

Behavior:
- Validate scope, clear corresponding dictionaries.
- Return { status: "ok", cleared: "<scope>" }.
""");
	}

	// ----------------------------
	// 2) Discovery
	// ----------------------------

	[McpServerTool, Description("List namespaces. Optional prefix filter and pagination.")]
	public static string ListNamespaces(string? prefix = null, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(ListNamespaces), """
Inputs: prefix (optional), limit, cursor.

Behavior:
- Enumerate distinct namespaces from type system.
- Prefix match if provided (case-insensitive).
- Return SearchResult<MemberHandle> where Kind="Namespace" and Name="<ns>".
""");
	}

	[McpServerTool, Description("Search types by name. Supports regex and filters.")]
	public static string SearchTypes(
		string query,
		bool regex = false,
		string? namespaceFilter = null,
		bool includeNested = true,
		int limit = 50,
		string? cursor = null)
	{
		return BuildPrompt(nameof(SearchTypes), """
Inputs: query, regex?, namespaceFilter?, includeNested?, limit, cursor.

Behavior:
- Case-insensitive substring by default; if regex=true use compiled Regex with timeout.
- Filters: namespace, include nested types or not.
- Return SearchResult<MemberSummary> with Kind="Type". Signature includes generic arity and base type.
""");
	}

	[McpServerTool, Description("Search members (methods, ctors, properties, fields, events) with rich filters.")]
	public static string SearchMembers(
		string query,
		bool regex = false,
		string? namespaceFilter = null,
		string? declaringTypeFilter = null,
		string? attributeFilter = null,
		string? returnTypeFilter = null,
		string[]? paramTypeFilters = null,
		string? kind = null, // "Method|Property|Field|Event|Constructor" or null
		string? accessibility = null, // "public|internal|protected|private"
		bool? isStatic = null,
		bool? isAbstract = null,
		bool? isVirtual = null,
		int? genericArity = null,
		int limit = 50,
		string? cursor = null)
	{
		return BuildPrompt(nameof(SearchMembers), """
Behavior:
- Search by name with substring or regex.
- Apply filters: namespace, declaring type, attribute presence (by full name), return type match, parameter type contains-all, kind, accessibility, static/abstract/virtual, generic arity.
- Return SearchResult<MemberSummary>. Signature shows short C# signature.
- Include a small doc summary if XML doc is available.
""");
	}

	[McpServerTool, Description("Get all types inside a namespace. Optional deep traversal for child namespaces.")]
	public static string GetTypesInNamespace(string ns, bool deep = false, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(GetTypesInNamespace), """
Behavior:
- Enumerate types whose Namespace equals ns (or startsWith(ns + ".") if deep).
- Return SearchResult<MemberSummary> for types.
""");
	}

	[McpServerTool, Description("List members of a given type with filters and pagination.")]
	public static string GetMembersOfType(
		string typeId,
		string? kind = null,
		string? accessibility = null,
		bool? isStatic = null,
		bool includeInherited = false,
		int limit = 100,
		string? cursor = null)
	{
		return BuildPrompt(nameof(GetMembersOfType), """
Behavior:
- Resolve typeId to ITypeDefinition.
- Enumerate members. If includeInherited, include base members with override/hidden markers.
- Filter by kind/accessibility/static.
- Return SearchResult<MemberSummary>.
""");
	}

	[McpServerTool, Description("Find overloads for a method name within its declaring type.")]
	public static string GetOverloads(string memberId)
	{
		return BuildPrompt(nameof(GetOverloads), """
Behavior:
- Resolve memberId to a method (or property accessor). Enumerate same-name methods in declaring type.
- Return SearchResult<MemberSummary> with overloads ordered by parameter count then specificity.
""");
	}

	[McpServerTool, Description("Find override chain of a virtual method. Base definition and overrides.")]
	public static string GetOverrides(string methodId)
	{
		return BuildPrompt(nameof(GetOverrides), """
Behavior:
- Resolve methodId. Get base definition. Walk derived types to collect overrides.
- Return { baseDefinition: MemberSummary, overrides: MemberSummary[] }.
""");
	}

	[McpServerTool, Description("Find implementations of an interface or interface method.")]
	public static string GetImplementations(string interfaceTypeOrMethodId, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(GetImplementations), """
Behavior:
- If typeId is interface type, find all types implementing it; else if methodId is interface method, find concrete implementations.
- Return SearchResult<MemberSummary>.
""");
	}

	[McpServerTool, Description("Find derived types of a base class. Optionally include indirect.")]
	public static string FindDerivedTypes(string baseTypeId, bool transitive = true, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(FindDerivedTypes), """
Behavior:
- Resolve base type. Enumerate direct or transitive derivatives via type system hierarchy.
- Return SearchResult<MemberSummary>.
""");
	}

	[McpServerTool, Description("Get base types and optionally implemented interfaces.")]
	public static string FindBaseTypes(string typeId, bool includeInterfaces = true)
	{
		return BuildPrompt(nameof(FindBaseTypes), """
Behavior:
- Resolve typeId. Return ordered list: base class chain and interfaces (if requested).
- Output: { bases: MemberSummary[], interfaces?: MemberSummary[] }.
""");
	}

	// ----------------------------
	// 3) Drill-down
	// ----------------------------

	[McpServerTool, Description("Quick signature preview for any member.")]
	public static string GetMemberSignature(string memberId)
	{
		return BuildPrompt(nameof(GetMemberSignature), """
Behavior:
- Resolve memberId. Produce concise C# signature string and minimal MemberSummary.
- Output: { summary: MemberSummary, signature: string }.
""");
	}

	[McpServerTool, Description("Detailed metadata for a member: attributes, docs, inheritance links.")]
	public static string GetMemberDetails(string memberId)
	{
		return BuildPrompt(nameof(GetMemberDetails), """
Behavior:
- Resolve member. Collect:
  * Attributes (full names, constructor args where simple)
  * XmlDoc (summary only or full XML)
  * BaseDefinitionId (for virtuals)
  * OverrideIds (if this overrides others)
  * ImplementorIds (if interface)
- Return MemberDetails.
""");
	}

	[McpServerTool, Description("Decompile a member to C#. Caches document and returns document metadata.")]
	public static string GetDecompiledSource(string memberId, bool includeHeader = true)
	{
		return BuildPrompt(nameof(GetDecompiledSource), """
Behavior:
- Resolve member. Decompile to C# with current settings.
- Store in cache with line index. Compute stable hash.
- Return SourceDocument { memberId, language: "C#", totalLines, hash, header? }.
- Do not return the full code here. Use GetSourceSlice for text.
""");
	}

	[McpServerTool, Description("Return a line range of the decompiled source for a member.")]
	public static string GetSourceSlice(
		string memberId,
		int startLine,
		int endLine,
		bool includeLineNumbers = false,
		int context = 0)
	{
		return BuildPrompt(nameof(GetSourceSlice), """
Behavior:
- Ensure document in cache (call decompile if missing).
- Expand start/end by 'context' within bounds.
- Return SourceSlice with exact lines and optional prefixed line numbers.
- Validate ranges and cap large requests.
""");
	}

	[McpServerTool, Description("Get IL for a method or constructor. Format: IL or ILAst.")]
	public static string GetIL(string memberId, string format = "IL")
	{
		return BuildPrompt(nameof(GetIL), """
Behavior:
- Resolve method/ctor. If format=="IL", use Decompiler.Disassembler to dump method body with tokens.
- If "ILAst", produce high-level IL (if available in library version) or return error if unsupported.
- Output: { memberId, format, text, totalLines } and page with GetSourceSlice-like slicing if too large.
""");
	}

	[McpServerTool, Description("AST outline: lightweight tree summary for a member for quick orientation.")]
	public static string GetAstOutline(string memberId, int maxDepth = 2)
	{
		return BuildPrompt(nameof(GetAstOutline), """
Behavior:
- Use CSharpDecompiler to get SyntaxTree, then extract a compact outline up to maxDepth: nodes with kind, short text, child counts, and line spans.
- Output a small JSON tree suitable for quick LLM reasoning.
""");
	}

	[McpServerTool, Description("Get raw XML doc for a member if available.")]
	public static string GetXmlDoc(string memberId)
	{
		return BuildPrompt(nameof(GetXmlDoc), """
Behavior:
- Resolve member. Return raw XML documentation string if present, else null.
""");
	}

	// ----------------------------
	// 4) Cross-references and search
	// ----------------------------

	[McpServerTool, Description("Find usages of a member across the assembly. Time-box and paginate.")]
	public static string FindUsages(string memberId, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(FindUsages), """
Behavior:
- Identify metadata token(s) for the target.
- Iterate candidate methods (heuristic order: smaller first; optionally from prebuilt callers map).
- Inspect IL for instructions referencing target token (calls, ld/st fld, newobj).
- Produce UsageRef { InMember, Kind, Line?, Snippet? }. If line unknown, omit.
- Paginate. Respect time budgets per call.
""");
	}

	[McpServerTool, Description("List direct callers of a method.")]
	public static string FindCallers(string methodId, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(FindCallers), """
Behavior:
- Reuse FindUsages specialized for call/callvirt/newobj edges.
- Return SearchResult<MemberSummary> of caller members.
""");
	}

	[McpServerTool, Description("List direct callees invoked by a method.")]
	public static string FindCallees(string methodId, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(FindCallees), """
Behavior:
- Scan target method IL for invocation instructions. Resolve targets to handles.
- Return SearchResult<MemberSummary>.
""");
	}

	[McpServerTool, Description("Search string literals across code. Regex optional.")]
	public static string SearchStringLiterals(string pattern, bool regex = false, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(SearchStringLiterals), """
Behavior:
- If string-literal index exists, query it. Else lazily scan methods in pages, decompiling or reading IL constants.
- Return UsageRef entries with small code snippet containing the literal.
""");
	}

	[McpServerTool, Description("Find members decorated with a specific attribute type.")]
	public static string SearchAttributes(string attributeFullName, string? kind = null, int limit = 100, string? cursor = null)
	{
		return BuildPrompt(nameof(SearchAttributes), """
Behavior:
- attributeFullName must be fully-qualified (e.g., "Verse.StaticConstructorOnStartup").
- Use attribute index if available or scan member metadata lazily.
- Optional kind filter.
- Return SearchResult<MemberSummary>.
""");
	}

	// ----------------------------
	// 5) Modding helpers
	// ----------------------------

	[McpServerTool, Description("Generate a Harmony patch skeleton for a given member.")]
	public static string GenerateHarmonyPatchSkeleton(
		string memberId,
		string patchKinds = "Prefix,Postfix,Transpiler,Finalizer",
		bool includeReflectionTargeting = true)
	{
		return BuildPrompt(nameof(GenerateHarmonyPatchSkeleton), """
Behavior:
- Resolve target and construct a compilable C# snippet that includes:
  * [HarmonyPatch] targeting Type and method (with overload disambiguation via argument types).
  * Stubs for requested patchKinds with correct signatures (Prefix/Postfix parameter patterns incl. ref/out, __instance, __result, __state).
  * If includeReflectionTargeting, show an alternative using AccessTools.Method with parameter type array to bind exact overload.
- Return { target: MemberSummary, code: string, notes: string[] }.
""");
	}

	[McpServerTool, Description("Suggest candidate IL offsets and patterns for transpiler insertion points.")]
	public static string SuggestTranspilerTargets(string memberId, int maxHints = 10)
	{
		return BuildPrompt(nameof(SuggestTranspilerTargets), """
Behavior:
- Analyze IL and produce up to maxHints anchors: { offset, opcode, operandSummary, nearbyOps[], rationale }.
- Include example transpiler snippet showing a CodeInstruction search pattern and insertion.
""");
	}

	[McpServerTool, Description("Generate a detour/stub method that calls the original, suitable for patch testing.")]
	public static string GenerateDetourStub(string memberId)
	{
		return BuildPrompt(nameof(GenerateDetourStub), """
Behavior:
- Emit a static wrapper method with identical signature that logs entry/exit and delegates to original via MethodInfo or Harmony delegate helper.
- Include notes on generics and ref/out safety.
""");
	}

	[McpServerTool, Description("Generate an extension method wrapper for an instance method to ease call sites in mods.")]
	public static string GenerateExtensionMethodWrapper(string memberId)
	{
		return BuildPrompt(nameof(GenerateExtensionMethodWrapper), """
Behavior:
- If target is instance method, create a public static extension method on declaring type (or interface if applicable).
- Preserve generic parameters and constraints in the wrapper.
- Return code string.
""");
	}

	// ----------------------------
	// 6) LLM ergonomics and diagnostics
	// ----------------------------

	[McpServerTool, Description("Plan line-range chunks for a member’s source for LLM-friendly paging.")]
	public static string PlanChunking(string memberId, int targetChunkSize = 6000, int overlap = 2)
	{
		return BuildPrompt(nameof(PlanChunking), """
Behavior:
- Use cached document length and average line length to partition into ranges producing roughly targetChunkSize characters.
- Include 'overlap' lines between chunks.
- Return: { memberId, chunks: [ { startLine, endLine }, ... ] }.
""");
	}

	[McpServerTool, Description("Fetch multiple members’ decompiled source in one call with size caps.")]
	public static string BatchGetDecompiledSource(string[] memberIds, int maxTotalChars = 200_000)
	{
		return BuildPrompt(nameof(BatchGetDecompiledSource), """
Behavior:
- For each memberId, decompile if needed and append to output until maxTotalChars reached.
- Return array of { doc: SourceDocument, firstSlice: SourceSlice } to give immediate text plus metadata.
- Include truncated flag if cap reached.
""");
	}

	[McpServerTool, Description("Normalize a possibly partial or human-entered identifier into a canonical memberId.")]
	public static string NormalizeMemberId(string input)
	{
		return BuildPrompt(nameof(NormalizeMemberId), """
Behavior:
- Accept forms like "Verse.Pawn:Tick", "Pawn.Tick", tokens like "0x06012345", or full IDs.
- Attempt to resolve uniquely. If ambiguous, return candidates.
- Output: { normalizedId?: string, candidates?: MemberSummary[] }.
""");
	}

	[McpServerTool, Description("Resolve a memberId and return a one-line summary for quick validation.")]
	public static string ResolveMemberId(string memberId)
	{
		return BuildPrompt(nameof(ResolveMemberId), """
Behavior:
- Resolve and return MemberSummary or error if unknown.
""");
	}

	[McpServerTool, Description("Basic health and timing info for the server.")]
	public static string GetServerStats()
	{
		return BuildPrompt(nameof(GetServerStats), """
Behavior:
- Return Stats plus memory footprint estimates of caches and index freshness flags.
""");
	}

	[McpServerTool, Description("Connectivity check. Returns 'pong' and current MVID if loaded.")]
	public static string Ping()
	{
		return BuildPrompt(nameof(Ping), """
Behavior:
- Return { pong: true, mvid?: string, timeUnix: long }.
""");
	}
}
