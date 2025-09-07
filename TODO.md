# DecompilerServer TODO List

This document contains comprehensive TODOs and recommendations based on a detailed code review of the DecompilerServer project. Items are organized by priority and complexity to guide future AI-assisted development work.

## High Priority - Core Implementation Gaps

### TODO-001: Complete IL Analysis Implementation ✅ COMPLETED
**Priority**: High | **Complexity**: Medium  
**Context**: UsageAnalyzer currently provides only framework methods for IL analysis. The `FindUsages`, `FindCallers`, and `FindCallees` tools work but rely on placeholder implementations.

**Implementation Completed**:
- ✅ Implemented actual IL scanning in `UsageAnalyzer.FindUsages()` with IL byte reading
- ✅ Built proper method call graph analysis using metadata token resolution
- ✅ Added field access detection (ldfld, stfld, ldsfld, stsfld)
- ✅ Implemented method invocation detection (call, callvirt, newobj)
- ✅ Added IL analysis result caching for performance
- ✅ Comprehensive `FindUsagesInMethod` implementation that analyzes IL opcodes
- ✅ Working `FindCallees` method that reads actual IL instructions
- ✅ Proper metadata token handling and member resolution

**Functionality Verified**:
- ✅ Tests pass for `FindUsages` functionality
- ✅ IL analysis correctly identifies different usage types (Call, FieldRead, FieldWrite, NewObject, etc.)
- ✅ Proper pagination and caching implemented

**Status**: ✅ COMPLETED - IL analysis implementation is comprehensive and functional, not placeholder code

### TODO-002: Convert Model Classes to Records ✅ COMPLETED
**Priority**: High | **Complexity**: Low  
**Context**: Original design specified record types but implementation uses classes. This affects immutability and value semantics.

**Files Updated**:
- `Services/ResponseFormatter.cs` - Converted `ServerStatus`, `IndexStatus`, `AssemblyInfo`, `MemberDetails`, `AttributeInfo`, `GeneratedCodeResult`
- `Services/SearchServiceBase.cs` - Converted `SearchResult<T>`, `MemberSummary`, `SearchCacheStats`

**Benefits Achieved**: 
- Better immutability guarantees
- Value equality semantics  
- More concise syntax
- Aligns with original design intent

**Status**: ✅ COMPLETED - All model classes successfully converted to records

### TODO-003: Fix Hanging Test in PlanChunking Tool ✅ COMPLETED
**Priority**: High | **Complexity**: Medium  
**Context**: The test `PlanChunking_WithValidMember_ReturnsChunkPlan` in `ToolImplementationTests.cs` was hanging indefinitely due to an infinite loop in the chunking logic.

**Root Cause**: The infinite loop prevention logic in PlanChunking tool was flawed. When `currentStart = currentEnd + 1 - overlap`, if the overlap was large enough, `currentStart` would not advance, causing an infinite loop.

**Fix Applied**: 
- Simplified and fixed the infinite loop prevention logic to ensure `currentStart` always advances
- Changed the logic to `if (nextStart <= currentStart) { nextStart = currentStart + 1; }`
- Test now passes reliably in ~1 second instead of hanging indefinitely

**Status**: ✅ COMPLETED - Fix verified, all tests passing

### TODO-004: Standardize Pagination Across All Tools ✅ COMPLETED
**Priority**: High | **Complexity**: Low  
**Context**: Some tools use inconsistent pagination patterns. Need uniform cursor-based pagination with consistent response format.

**Tools Verified**:
- `FindUsages` - ✅ Uses consistent `SearchResult<T>` format with all required fields
- `SearchStringLiterals` - ✅ Uses consistent `SearchResult<T>` format with all required fields  
- `SearchMembers` - ✅ Uses consistent `SearchResult<T>` format with all required fields
- `ListNamespaces` - ✅ Uses consistent `SearchResult<T>` format with all required fields
- `GetImplementations` - ✅ Uses consistent `SearchResult<T>` format with all required fields
- All paginated tools - ✅ Use uniform cursor-based pagination

**Requirements Met**:
- ✅ All paginated responses include: `items`, `hasMore`, `nextCursor`, `totalEstimate`
- ✅ Cursor format is consistent (simple index-based cursors)
- ✅ All tools use the `SearchResult<T>` record type for consistent response structure

**Status**: ✅ COMPLETED - All tools use standardized pagination patterns

## Medium Priority - API Enhancement

### TODO-005: Implement Unity-Specific Analysis Features
**Priority**: Medium | **Complexity**: Medium  
**Context**: For game code analysis, Unity-specific patterns and attributes are crucial for AI understanding.

**New Tools to Implement**:
- `FindUnityComponents` - Find MonoBehaviour and ScriptableObject classes
- `AnalyzeUnityLifecycle` - Detect Start/Update/OnEnable patterns
- `FindSerializedFields` - Locate [SerializeField] attributes and public fields
- `AnalyzeUnityEvents` - Detect UnityEvent usage patterns
- `FindAssetReferences` - Analyze Resource.Load and AssetDatabase patterns

**Benefits**: Enables AI to understand Unity-specific patterns and provide more relevant game development insights.

### TODO-006: Add Code Quality and Metrics Analysis
**Priority**: Medium | **Complexity**: Medium  
**Context**: AI analysis would benefit from code quality metrics to identify refactoring opportunities.

**New Tools to Implement**:
- `AnalyzeComplexity` - Cyclomatic complexity, method length, nesting depth
- `FindDeadCode` - Unused methods, fields, and types
- `AnalyzeDependencies` - Circular dependencies, coupling metrics
- `SuggestRefactoring` - Identify long parameter lists, large classes
- `AnalyzePerformance` - Find allocation hotspots, boxing operations

**Implementation**: Leverage ILSpy analysis capabilities and AST traversal for metrics calculation.

### TODO-007: Enhance Context-Aware Analysis
**Priority**: Medium | **Complexity**: High  
**Context**: Current tools provide isolated information. AI needs broader context understanding.

**New Features**:
- `GetUsageContext` - Provide calling context with parameter values and conditions
- `AnalyzeCallChains` - Multi-hop call analysis with depth limits
- `FindPatterns` - Detect common design patterns (Singleton, Observer, etc.)
- `GetSemanticSummary` - Extract business logic summaries from method bodies
- `AnalyzeState` - Track field/property dependencies and state mutations

**Benefits**: Enables AI to understand code purpose and relationships beyond simple structural analysis.

### TODO-008: Improve Code Generation Tools
**Priority**: Medium | **Complexity**: Low  
**Context**: Current generation tools are comprehensive but could be more flexible and user-friendly.

**Enhancements Needed**:
- `GenerateHarmonyPatchSkeleton` - Add template options (minimal vs full)
- `GenerateDetourStub` - Add async method support
- `GenerateExtensionMethodWrapper` - Add generic constraint handling
- New tool: `GenerateTestStub` - Create unit test templates for methods
- New tool: `GenerateDocumentationSkeleton` - XML doc templates with parameter descriptions

**Implementation**: Enhance existing generators with template parameters and add new generation capabilities.

## Low Priority - Quality of Life Improvements

### TODO-009: Add Response Mode Options
**Priority**: Low | **Complexity**: Low  
**Context**: Some responses are verbose for AI consumption. Adding summary vs detailed modes would improve usability.

**Implementation**:
- Add `mode` parameter to relevant tools: `summary` | `detailed` | `minimal`
- `GetMemberDetails` - Summary mode returns only essential info
- `SearchTypes`/`SearchMembers` - Summary mode returns only names and IDs
- `GetDecompiledSource` - Summary mode returns signature + documentation only

### TODO-010: Consolidate Overlapping Tools
**Priority**: Low | **Complexity**: Medium  
**Context**: Some tools have overlapping functionality that could be streamlined.

**Consolidation Opportunities**:
- Merge `FindUsages`, `FindCallers`, `FindCallees` into single `AnalyzeUsages` tool with direction parameter
- Combine `GetOverrides` and `GetOverloads` into `GetMethodRelationships`
- Unify `SearchAttributes` with general search tools using attribute filters
- Consider merging simple getters like `GetMemberSignature` into `GetMemberDetails`

**Benefits**: Simpler API surface, reduced cognitive load, fewer edge cases to handle.

### TODO-011: Enhance Error Reporting and Diagnostics
**Priority**: Low | **Complexity**: Low  
**Context**: Current error handling is good but could provide more actionable feedback.

**Improvements**:
- Add error codes for common scenarios (assembly not loaded, invalid member ID formats)
- Include suggestions in error messages ("Did you mean..." for fuzzy member ID matches)
- Add diagnostic tool `ValidateAssembly` for health checks
- Enhance `GetServerStats` with performance metrics and warnings

### TODO-012: Add Batch Operation Consistency
**Priority**: Low | **Complexity**: Medium  
**Context**: Only `BatchGetDecompiledSource` exists. Other operations could benefit from batching.

**New Batch Tools**:
- `BatchResolveMemberIds` - Resolve multiple member IDs efficiently
- `BatchGetMemberDetails` - Get details for multiple members
- `BatchFindUsages` - Find usages for multiple members with combined results
- `BatchAnalyzeComplexity` - Analyze multiple methods for complexity metrics

**Benefits**: Reduced round-trip overhead for AI agents processing multiple items.

## Technical Debt and Maintenance

### TODO-013: Address Nullable Reference Warnings ✅ COMPLETED
**Priority**: Low | **Complexity**: Low  
**Context**: Build shows 6 nullable reference warnings in code generation tools.

**Files Verified**:
- `GenerateHarmonyPatchSkeleton.cs` - ✅ No nullable warnings found
- `GenerateExtensionMethodWrapper.cs` - ✅ No nullable warnings found  
- `GenerateDetourStub.cs` - ✅ No nullable warnings found

**Action Completed**: All nullable reference warnings have been resolved. Build shows 0 warnings.

**Status**: ✅ COMPLETED - No nullable reference warnings remain in the codebase

### TODO-014: Consider ServiceLocator Alternatives
**Priority**: Low | **Complexity**: High  
**Context**: ServiceLocator pattern is functional but could be replaced with more modern DI patterns.

**Options**:
- Implement proper DI container usage in MCP tools
- Use factory pattern for service creation
- Consider whether static tool methods are the best approach vs instance-based tools

**Note**: This is architectural and should be carefully considered as it affects the entire tool implementation pattern.

### TODO-015: Performance Optimization Review
**Priority**: Low | **Complexity**: Medium  
**Context**: Current implementation prioritizes correctness over performance. Some optimizations could improve AI workflow speed.

**Areas to Investigate**:
- Parallel processing for search operations
- More aggressive caching of member resolution
- Lazy loading optimization for large assemblies
- Memory usage optimization for source caching
- Consider ReadOnlySpan<char> for string operations in hot paths

## Future Enhancements - Advanced Features

### TODO-016: Add Machine Learning Integration Points
**Priority**: Future | **Complexity**: High  
**Context**: Prepare for future ML-based code analysis by adding structured data export.

**Potential Features**:
- Export code structure as embeddings-friendly format
- Add AST serialization for ML training
- Implement code similarity detection
- Add feature extraction for code classification
- Support for custom analysis pipelines

### TODO-017: Multi-Assembly Analysis Support
**Priority**: Future | **Complexity**: High  
**Context**: Current design focuses on single Assembly-CSharp.dll. Games often have multiple assemblies.

**Extensions Needed**:
- Load and analyze multiple assemblies simultaneously
- Cross-assembly reference analysis
- Assembly dependency graphing
- Multi-assembly search and navigation

### TODO-018: Real-time Analysis and Watching
**Priority**: Future | **Complexity**: High  
**Context**: For development scenarios, real-time file watching and incremental analysis could be valuable.

**Features**:
- File system watching for assembly changes
- Incremental recompilation and analysis
- Delta reporting for changes
- Real-time usage update notifications

## Implementation Guidelines

### For AI Agents Working on These TODOs:

1. **Start with High Priority items** - They have the most impact on functionality
2. **Test thoroughly** - Use the existing xUnit framework and TestLibrary
3. **Follow existing patterns** - Study similar tools for implementation guidance
4. **Update documentation** - Modify HELPER_METHODS_GUIDE.md for new services
5. **Maintain consistency** - Follow established naming conventions and response formats
6. **Performance considerations** - Always consider caching and pagination for new features

### Testing Requirements:
- All new tools must have corresponding tests in `Tests/ToolImplementationTests.cs`
- New services require tests in both `SimpleServiceTests.cs` and `ServiceIntegrationTests.cs`
- Use the TestLibrary for consistent test data
- Validate both functionality and JSON response format

### Documentation Updates:
- Add new tools to HELPER_METHODS_GUIDE.md with implementation patterns
- Update TESTING.md if new testing patterns are introduced
- Keep this TODO.md updated as items are completed or priorities change