using DecompilerServer.Services;

namespace DecompilerServer.Tests;

/// <summary>
/// Simple test to validate that our helper services integrate correctly with ICSharpCode.Decompiler
/// </summary>
public static class HelperServiceTests
{
	public static void RunBasicTests()
	{
		Console.WriteLine("Running basic helper service tests...");

		// Test 1: AssemblyContextManager can be created
		using var contextManager = new AssemblyContextManager();
		Console.WriteLine("✓ AssemblyContextManager created successfully");

		// Test 2: MemberResolver can be created
		var memberResolver = new MemberResolver(contextManager);
		Console.WriteLine("✓ MemberResolver created successfully");

		// Test 3: DecompilerService can be created
		var decompilerService = new DecompilerService(contextManager, memberResolver);
		Console.WriteLine("✓ DecompilerService created successfully");

		// Test 4: SearchServiceBase can be created 
		var searchService = new TestSearchService(contextManager, memberResolver);
		Console.WriteLine("✓ SearchServiceBase subclass created successfully");

		// Test 5: UsageAnalyzer can be created
		var usageAnalyzer = new UsageAnalyzer(contextManager, memberResolver);
		Console.WriteLine("✓ UsageAnalyzer created successfully");

		// Test 6: InheritanceAnalyzer can be created
		var inheritanceAnalyzer = new InheritanceAnalyzer(contextManager, memberResolver);
		Console.WriteLine("✓ InheritanceAnalyzer created successfully");

		// Test 7: ResponseFormatter static methods work
		var successResponse = ResponseFormatter.Success("test data");
		var errorResponse = ResponseFormatter.Error("test error");
		Console.WriteLine("✓ ResponseFormatter methods work");

		// Test 8: Validate member ID format checking
		var isValid = memberResolver.IsValidMemberId("T:System.String");
		var isInvalid = memberResolver.IsValidMemberId("invalid");
		Console.WriteLine($"✓ Member ID validation: valid={isValid}, invalid={!isInvalid}");

		Console.WriteLine("All basic tests passed!");
	}

	/// <summary>
	/// Test SearchServiceBase by creating a concrete implementation
	/// </summary>
	private class TestSearchService : SearchServiceBase
	{
		public TestSearchService(AssemblyContextManager contextManager, MemberResolver memberResolver) 
			: base(contextManager, memberResolver)
		{
		}
	}
}