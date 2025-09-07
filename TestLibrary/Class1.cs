using System.ComponentModel;

namespace TestLibrary;

/// <summary>
/// A simple test class for testing decompiler services
/// </summary>
public class SimpleClass
{
    public int PublicField = 42;
    private string _privateField = "private";

    public string PublicProperty { get; set; } = "default";

    public int AutoProperty { get; } = 100;

    public void SimpleMethod()
    {
        _ = _privateField;
        Console.WriteLine("Simple method called");
    }

    public int MethodWithParameters(string input, int number)
    {
        return input.Length + number;
    }

    public static void StaticMethod()
    {
        Console.WriteLine("Static method called");
    }

    public virtual void VirtualMethod()
    {
        Console.WriteLine("Virtual method");
    }
}

/// <summary>
/// Interface for testing interface resolution
/// </summary>
public interface ITestInterface
{
    void InterfaceMethod();
    string InterfaceProperty { get; }
}

/// <summary>
/// Abstract base class for testing inheritance
/// </summary>
public abstract class BaseClass
{
    public abstract void AbstractMethod();

    public virtual void VirtualMethod()
    {
        Console.WriteLine("Base virtual method");
    }

    protected string ProtectedField = "protected";
}

/// <summary>
/// Derived class for testing inheritance analysis
/// </summary>
public class DerivedClass : BaseClass, ITestInterface
{
    public override void AbstractMethod()
    {
        Console.WriteLine("Implemented abstract method");
    }

    public override void VirtualMethod()
    {
        base.VirtualMethod();
        Console.WriteLine("Overridden virtual method");
    }

    public void InterfaceMethod()
    {
        Console.WriteLine("Interface method implementation");
    }

    public string InterfaceProperty => "interface property";
}

/// <summary>
/// Generic class for testing generic type resolution
/// </summary>
public class GenericClass<T> where T : class
{
    private T? _value;

    public void GenericMethod(T parameter)
    {
        _value = parameter;
    }

    public T? GetValue() => _value;
}

/// <summary>
/// Enum for testing enum handling
/// </summary>
public enum TestEnum
{
    First,
    Second = 10,
    Third
}

/// <summary>
/// Attribute for testing attribute analysis
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class TestAttribute : Attribute
{
    public string Value { get; }

    public TestAttribute(string value)
    {
        Value = value;
    }
}

/// <summary>
/// Class with attributes for testing attribute extraction
/// </summary>
[Test("class-attribute")]
[Description("Test class with attributes")]
public class AttributedClass
{
    [Test("field-attribute")]
    public string AttributedField = "field";

    [Test("property-attribute")]
    public int AttributedProperty { get; set; }

    [Test("method-attribute")]
    public void AttributedMethod([Test("parameter-attribute")] string param)
    {
        // Method with parameter attribute
    }
}

/// <summary>
/// Static class for testing static member resolution
/// </summary>
public static class StaticUtilities
{
    public static readonly string StaticField = "static";

    public static string StaticProperty => "static property";

    public static void StaticUtilityMethod()
    {
        Console.WriteLine("Static utility method");
    }

    public static T GenericStaticMethod<T>(T input) => input;
}

/// <summary>
/// Nested classes for testing nested type resolution
/// </summary>
public class OuterClass
{
    public class NestedClass
    {
        public void NestedMethod()
        {
            Console.WriteLine("Nested method");
        }

        public class DeeplyNestedClass
        {
            public string DeepProperty { get; set; } = "deep";
        }
    }

    private class PrivateNestedClass
    {
        internal void InternalMethod() { }
    }
}
