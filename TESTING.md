# DecompilerServer Testing Framework

This project now includes a comprehensive testing framework using xUnit that validates the functionality of all service helpers using a real test assembly.

## Test Structure

### 1. TestLibrary Project
- **Purpose**: Creates `test.dll` - a sample assembly used for testing decompiler services
- **Location**: `TestLibrary/`
- **Output**: `TestLibrary/bin/Debug/net8.0/test.dll`
- **Contents**: Various C# constructs for testing:
  - Simple classes with fields, properties, and methods
  - Interface definitions
  - Inheritance hierarchies (base classes, derived classes)
  - Generic classes with constraints
  - Enums
  - Nested classes
  - Attributes
  - Static classes and members

### 2. Test Project
- **Framework**: xUnit
- **Location**: `DecompilerServer.Tests/`
- **Base Class**: `ServiceTestBase` - provides common setup with loaded test.dll

### 3. Test Categories

#### SimpleServiceTests
Basic functionality tests that validate core service creation and basic operations.

#### ServiceIntegrationTests  
Integration tests that verify services work together correctly with real assembly data.

#### DecompilationFunctionalityTests
End-to-end tests that validate actual decompilation output contains expected code structures.

## Services Tested

All major service helpers are comprehensively tested:

- **AssemblyContextManager**: Assembly loading, type enumeration, compilation access
- **MemberResolver**: Member ID resolution, validation, normalization, generation
- **DecompilerService**: C# decompilation, caching, source slicing, batch operations
- **SearchServiceBase**: Type and member searching with filtering
- **UsageAnalyzer**: Usage analysis framework (basic functionality)
- **InheritanceAnalyzer**: Inheritance relationship analysis
- **ResponseFormatter**: JSON response formatting

## Running Tests

### xUnit Tests (Recommended)
```bash
# Run all xUnit tests
dotnet test DecompilerServer.Tests/

# Run with verbose output
dotnet test DecompilerServer.Tests/ --verbosity normal

# Run specific test class
dotnet test DecompilerServer.Tests/ --filter "ClassName=SimpleServiceTests"
```

### Legacy Basic Tests (Backward Compatibility)
```bash
# Run original basic tests
dotnet run -- --test
```

## Test Data

The tests use the `test.dll` assembly which contains:
- `TestLibrary.SimpleClass` - Basic class with various member types
- `TestLibrary.ITestInterface` - Interface for implementation testing
- `TestLibrary.BaseClass` - Abstract base class
- `TestLibrary.DerivedClass` - Derived class implementing interface
- `TestLibrary.GenericClass<T>` - Generic class with constraints
- `TestLibrary.TestEnum` - Enum with values
- `TestLibrary.OuterClass.NestedClass` - Nested class structure
- `TestLibrary.AttributedClass` - Class with custom attributes
- `TestLibrary.StaticUtilities` - Static class and members

## Benefits

1. **Real Assembly Testing**: Tests use actual compiled assembly, not mocks
2. **Comprehensive Coverage**: All service helpers are tested with realistic scenarios
3. **Regression Detection**: Tests catch breaking changes in decompilation logic
4. **Documentation**: Tests serve as examples of how to use each service
5. **CI/CD Ready**: Standard xUnit framework integrates with build pipelines
6. **Performance Testing**: Tests validate caching and performance optimizations

## Test Environment

- **.NET 8.0**: Target framework
- **xUnit 2.4.2**: Test framework
- **ICSharpCode.Decompiler**: Real decompilation engine
- **test.dll**: Dedicated test assembly with known structure

The testing framework provides confidence that all decompiler service helpers work correctly with real assemblies and can handle the types of code structures they will encounter in production Unity assemblies.