using DecompilerServer.Services;

namespace DecompilerServer.Tests;

/// <summary>
/// Tests that demonstrate the actual decompilation functionality working end-to-end
/// </summary>
public class DecompilationFunctionalityTests : ServiceTestBase
{
    [Fact]
    public void DecompileSimpleClass_ShouldContainExpectedContent()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        
        // Act
        var document = decompilerService.DecompileMember("T:TestLibrary.SimpleClass");
        var sourceCode = string.Join("\n", document.Lines);
        
        // Assert
        Assert.Contains("class SimpleClass", sourceCode);
        Assert.Contains("PublicField", sourceCode);
        Assert.Contains("PublicProperty", sourceCode);
        Assert.Contains("SimpleMethod", sourceCode);
        Assert.Contains("MethodWithParameters", sourceCode);
        Assert.Contains("StaticMethod", sourceCode);
        Assert.Contains("VirtualMethod", sourceCode);
    }

    [Fact]
    public void DecompileInterface_ShouldContainInterfaceDeclaration()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        
        // Act
        var document = decompilerService.DecompileMember("T:TestLibrary.ITestInterface");
        var sourceCode = string.Join("\n", document.Lines);
        
        // Assert
        Assert.Contains("interface ITestInterface", sourceCode);
        Assert.Contains("InterfaceMethod", sourceCode);
        Assert.Contains("InterfaceProperty", sourceCode);
    }

    [Fact]
    public void DecompileDerivedClass_ShouldShowInheritance()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        
        // Act
        var document = decompilerService.DecompileMember("T:TestLibrary.DerivedClass");
        var sourceCode = string.Join("\n", document.Lines);
        
        // Assert
        Assert.Contains("class DerivedClass", sourceCode);
        Assert.Contains("BaseClass", sourceCode);
        Assert.Contains("ITestInterface", sourceCode);
        Assert.Contains("override", sourceCode);
    }

    [Fact]
    public void DecompileEnum_ShouldShowEnumValues()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        
        // Act
        var document = decompilerService.DecompileMember("T:TestLibrary.TestEnum");
        var sourceCode = string.Join("\n", document.Lines);
        
        // Assert
        Assert.Contains("enum TestEnum", sourceCode);
        Assert.Contains("First", sourceCode);
        Assert.Contains("Second", sourceCode);
        Assert.Contains("Third", sourceCode);
    }

    [Fact]
    public void DecompileGenericClass_ShouldShowGenericConstraints()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        
        // Act
        var document = decompilerService.DecompileMember("T:TestLibrary.GenericClass`1");
        var sourceCode = string.Join("\n", document.Lines);
        
        // Assert
        Assert.Contains("GenericClass", sourceCode);
        Assert.Contains("<T>", sourceCode);
        Assert.Contains("where T : class", sourceCode);
    }

    [Fact]
    public void SourceSlice_ShouldReturnLimitedLines()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        
        // Act
        var slice = decompilerService.GetSourceSlice("T:TestLibrary.SimpleClass", 1, 3);
        
        // Assert
        Assert.Equal(1, slice.StartLine);
        Assert.Equal(3, slice.EndLine);
        Assert.NotEmpty(slice.Code);
        
        // Should be shorter than full document
        var fullDocument = decompilerService.DecompileMember("T:TestLibrary.SimpleClass");
        var fullSource = string.Join("\n", fullDocument.Lines);
        Assert.True(slice.Code.Length < fullSource.Length);
    }

    [Fact]
    public void MemberResolver_CanResolveVariousMembers()
    {
        // Act & Assert - Test different member types
        var classEntity = MemberResolver.ResolveMember("T:TestLibrary.SimpleClass");
        Assert.NotNull(classEntity);
        Assert.Equal("SimpleClass", classEntity.Name);

        var interfaceEntity = MemberResolver.ResolveMember("T:TestLibrary.ITestInterface");
        Assert.NotNull(interfaceEntity);
        Assert.Equal("ITestInterface", interfaceEntity.Name);

        var enumEntity = MemberResolver.ResolveMember("T:TestLibrary.TestEnum");
        Assert.NotNull(enumEntity);
        Assert.Equal("TestEnum", enumEntity.Name);
    }

    [Fact]
    public void ResponseFormatter_ProducesValidJsonForDecompilerResults()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        var document = decompilerService.DecompileMember("T:TestLibrary.SimpleClass");
        
        // Act
        var jsonResponse = ResponseFormatter.Success(document);
        
        // Assert
        Assert.NotNull(jsonResponse);
        Assert.Contains("\"status\":\"ok\"", jsonResponse);
        Assert.Contains("\"memberId\":\"T:TestLibrary.SimpleClass\"", jsonResponse);
        Assert.Contains("\"language\":\"C#\"", jsonResponse);
        Assert.Contains("\"totalLines\":", jsonResponse);
    }
}