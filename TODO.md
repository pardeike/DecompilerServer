# DecompilerServer TODO List

This document contains comprehensive TODOs and recommendations based on a detailed code review of the DecompilerServer project. Items are organized by priority and complexity to guide future AI-assisted development work.

## High Priority - Core Implementation Gaps

## Medium Priority - API Enhancement

### TODO-001: Implement Unity-Specific Analysis Features
**Priority**: Medium | **Complexity**: Medium  
**Context**: For game code analysis, Unity-specific patterns and attributes are crucial for AI understanding.

**New Tools to Implement**:
- `FindUnityComponents` - Find MonoBehaviour and ScriptableObject classes
- `AnalyzeUnityLifecycle` - Detect Start/Update/OnEnable patterns
- `FindSerializedFields` - Locate [SerializeField] attributes and public fields
- `AnalyzeUnityEvents` - Detect UnityEvent usage patterns
- `FindAssetReferences` - Analyze Resource.Load and AssetDatabase patterns

**Benefits**: Enables AI to understand Unity-specific patterns and provide more relevant game development insights.

### TODO-002: Add Code Quality and Metrics Analysis
**Priority**: Medium | **Complexity**: Medium  
**Context**: AI analysis would benefit from code quality metrics to identify refactoring opportunities.

**New Tools to Implement**:
- `AnalyzeComplexity` - Cyclomatic complexity, method length, nesting depth
- `FindDeadCode` - Unused methods, fields, and types
- `AnalyzeDependencies` - Circular dependencies, coupling metrics
- `SuggestRefactoring` - Identify long parameter lists, large classes
- `AnalyzePerformance` - Find allocation hotspots, boxing operations

**Implementation**: Leverage ILSpy analysis capabilities and AST traversal for metrics calculation.

### TODO-003: Enhance Context-Aware Analysis
**Priority**: Medium | **Complexity**: High  
**Context**: Current tools provide isolated information. AI needs broader context understanding.

**New Features**:
- `GetUsageContext` - Provide calling context with parameter values and conditions
- `AnalyzeCallChains` - Multi-hop call analysis with depth limits
- `FindPatterns` - Detect common design patterns (Singleton, Observer, etc.)
- `GetSemanticSummary` - Extract business logic summaries from method bodies
- `AnalyzeState` - Track field/property dependencies and state mutations

**Benefits**: Enables AI to understand code purpose and relationships beyond simple structural analysis.

### TODO-004: Improve Code Generation Tools
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

### TODO-005: Add Response Mode Options
**Priority**: Low | **Complexity**: Low  
**Context**: Some responses are verbose for AI consumption. Adding summary vs detailed modes would improve usability.

**Implementation**:
- Add `mode` parameter to relevant tools: `summary` | `detailed` | `minimal`
- `GetMemberDetails` - Summary mode returns only essential info
- `SearchTypes`/`SearchMembers` - Summary mode returns only names and IDs
- `GetDecompiledSource` - Summary mode returns signature + documentation only

### TODO-006: Consolidate Overlapping Tools
**Priority**: Low | **Complexity**: Medium  
**Context**: Some tools have overlapping functionality that could be streamlined.

**Consolidation Opportunities**:
- Merge `FindUsages`, `FindCallers`, `FindCallees` into single `AnalyzeUsages` tool with direction parameter
- Combine `GetOverrides` and `GetOverloads` into `GetMethodRelationships`
- Unify `SearchAttributes` with general search tools using attribute filters
- Consider merging simple getters like `GetMemberSignature` into `GetMemberDetails`

**Benefits**: Simpler API surface, reduced cognitive load, fewer edge cases to handle.

### TODO-007: Enhance Error Reporting and Diagnostics
**Priority**: Low | **Complexity**: Low  
**Context**: Current error handling is good but could provide more actionable feedback.

**Improvements**:
- Add error codes for common scenarios (assembly not loaded, invalid member ID formats)
- Include suggestions in error messages ("Did you mean..." for fuzzy member ID matches)
- Add diagnostic tool `ValidateAssembly` for health checks
- Enhance `GetServerStats` with performance metrics and warnings

### TODO-008: Add Batch Operation Consistency
**Priority**: Low | **Complexity**: Medium  
**Context**: Only `BatchGetDecompiledSource` exists. Other operations could benefit from batching.

**New Batch Tools**:
- `BatchResolveMemberIds` - Resolve multiple member IDs efficiently
- `BatchGetMemberDetails` - Get details for multiple members
- `BatchFindUsages` - Find usages for multiple members with combined results
- `BatchAnalyzeComplexity` - Analyze multiple methods for complexity metrics

**Benefits**: Reduced round-trip overhead for AI agents processing multiple items.

## Technical Debt and Maintenance

### TODO-009: Address Nullable Reference Warnings ✅ COMPLETED
**Status**: ✅ COMPLETED - All nullable reference warnings resolved, build shows 0 warnings.

### TODO-010: Consider ServiceLocator Alternatives
**Priority**: Low | **Complexity**: High  
**Context**: ServiceLocator pattern is functional but could be replaced with more modern DI patterns.

**Options**:
- Implement proper DI container usage in MCP tools
- Use factory pattern for service creation
- Consider whether static tool methods are the best approach vs instance-based tools

**Note**: This is architectural and should be carefully considered as it affects the entire tool implementation pattern.

### TODO-011: Performance Optimization Review
**Priority**: Low | **Complexity**: Medium  
**Context**: Current implementation prioritizes correctness over performance. Some optimizations could improve AI workflow speed.

**Areas to Investigate**:
- Parallel processing for search operations
- More aggressive caching of member resolution
- Lazy loading optimization for large assemblies
- Memory usage optimization for source caching
- Consider ReadOnlySpan<char> for string operations in hot paths

## Future Enhancements - Advanced Features

### TODO-012: Add Machine Learning Integration Points
**Priority**: Future | **Complexity**: High  
**Context**: Prepare for future ML-based code analysis by adding structured data export.

**Potential Features**:
- Export code structure as embeddings-friendly format
- Add AST serialization for ML training
- Implement code similarity detection
- Add feature extraction for code classification
- Support for custom analysis pipelines

### TODO-013: Multi-Assembly Analysis Support
**Priority**: Future | **Complexity**: High  
**Context**: Current design focuses on single Assembly-CSharp.dll. Games often have multiple assemblies.

**Extensions Needed**:
- Load and analyze multiple assemblies simultaneously
- Cross-assembly reference analysis
- Assembly dependency graphing
- Multi-assembly search and navigation

### TODO-014: Real-time Analysis and Watching
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
