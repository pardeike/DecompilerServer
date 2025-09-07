using DecompilerServer.Services;
using System.Linq;

namespace Tests;

public class InheritanceAnalyzerTests : ServiceTestBase
{
    [Fact]
    public void FindDerivedTypes_ShouldIncludeMultiLevelInheritance()
    {
        var analyzer = new InheritanceAnalyzer(ContextManager, MemberResolver);

        var derived = analyzer
            .FindDerivedTypes("T:TestLibrary.BaseClass")
            .Select(t => t.Name)
            .ToList();

        Assert.Contains("DerivedClass", derived);
        Assert.Contains("DeepDerivedClass", derived);
    }

    [Fact]
    public void FindDerivedTypes_ShouldHandleMultipleInterfaceImplementation()
    {
        var analyzer = new InheritanceAnalyzer(ContextManager, MemberResolver);

        var derived = analyzer
            .FindDerivedTypes("T:TestLibrary.IAnotherInterface")
            .Select(t => t.Name)
            .ToList();

        Assert.Contains("MultiInterfaceClass", derived);
    }
}

