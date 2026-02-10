using System.Reflection;
using Sharp.Core;

namespace Sharp.Core.Tests;

public sealed class ArchitectureGuardTests
{
    [Fact]
    public void SharpCore_PublicApi_ShouldNotExposeServiceProviderInterface()
    {
        var forbiddenTypeName = "System.IServ" + "iceProvider";
        var offendingMembers = new List<string>();
        var assembly = typeof(ToolExecutionContext).Assembly;

        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType.FullName == forbiddenTypeName)
                    offendingMembers.Add($"{type.FullName}.{property.Name}");
            }

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static))
            {
                if (method.IsSpecialName)
                    continue;

                if (method.ReturnType.FullName == forbiddenTypeName)
                    offendingMembers.Add($"{type.FullName}.{method.Name} return");

                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.ParameterType.FullName == forbiddenTypeName)
                        offendingMembers.Add($"{type.FullName}.{method.Name}({parameter.Name})");
                }
            }

            foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            {
                foreach (var parameter in ctor.GetParameters())
                {
                    if (parameter.ParameterType.FullName == forbiddenTypeName)
                        offendingMembers.Add($"{type.FullName}.ctor({parameter.Name})");
                }
            }
        }

        Assert.True(
            offendingMembers.Count == 0,
            $"Found forbidden service-provider interface in public API: {string.Join(", ", offendingMembers)}");
    }

    [Fact]
    public void ToolExecutionContext_Constructor_ShouldContainOnlyTwoStringParameters()
    {
        var constructors = typeof(ToolExecutionContext).GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var ctor = Assert.Single(constructors);
        var parameters = ctor.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.All(parameters, parameter => Assert.Equal(typeof(string), parameter.ParameterType));
    }
}
