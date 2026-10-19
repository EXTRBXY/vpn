using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NothingVpn.Desktop.Wpf;
using NothingVpn.Desktop.Wpf.Composition;
using NothingVpn.Infrastructure.Composition;

namespace NothingVpn.Application.Tests;

/// <summary>
/// WPF-клиент зависит от Infrastructure только в composition root (REFACTORING.md 3.5). Проверяются сигнатуры
/// и тела методов (IL) всех типов сборки WPF, включая сгенерированные компилятором (лямбды, async), и XAML.
/// </summary>
public sealed class WpfLayerBoundaryTests
{
    private const string CompositionNamespace = "NothingVpn.Desktop.Wpf.Composition";
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Assembly Wpf = typeof(MainViewModel).Assembly;
    private static readonly Assembly Infrastructure = typeof(ApplicationServicesFactory).Assembly;
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(x => (OpCode)x.GetValue(null)!)
        .ToDictionary(x => x.Value);

    [Fact]
    public void WpfTypesOutsideCompositionRoot_DoNotReferenceInfrastructure()
    {
        var violations = Wpf.GetTypes()
            .Where(type => !IsCompositionRoot(type))
            .SelectMany(type => ReferencedTypes(type)
                .Where(reference => reference.Assembly == Infrastructure)
                .Select(reference => $"{type.FullName} -> {reference.FullName}"))
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, "Infrastructure вне composition root:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Scan_DetectsInfrastructureInCompositionRoot()
    {
        // Контроль самой проверки: composition root обязан находиться через тот же анализ IL.
        var references = ReferencedTypes(typeof(DesktopCompositionRoot)).ToList();

        Assert.Contains(typeof(ApplicationServicesFactory), references);
        Assert.Contains(typeof(ApplicationServiceBundle), references);
    }

    [Fact]
    public void WpfXaml_DoesNotReferenceInfrastructure()
    {
        var files = Directory.GetFiles(WpfSourceDir(), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.NotEmpty(files);
        Assert.All(files, path => Assert.DoesNotContain("NothingVpn.Infrastructure", File.ReadAllText(path)));
    }

    private static bool IsCompositionRoot(Type type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type.Namespace == CompositionNamespace;
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        var direct = new List<Type?> { type.BaseType };
        direct.AddRange(type.GetInterfaces());
        direct.AddRange(type.GetFields(Declared).Select(x => x.FieldType));
        direct.AddRange(type.GetProperties(Declared).Select(x => x.PropertyType));
        direct.AddRange(type.GetEvents(Declared).Select(x => x.EventHandlerType));

        var methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));
        foreach (var method in methods)
        {
            if (method is MethodInfo info) direct.Add(info.ReturnType);
            direct.AddRange(method.GetParameters().Select(x => x.ParameterType));
            var body = method.GetMethodBody();
            if (body is null) continue;
            direct.AddRange(body.LocalVariables.Select(x => x.LocalType));
            direct.AddRange(TypesUsedInIl(method, body));
        }

        return direct.Where(x => x is not null).SelectMany(x => Flatten(x!));
    }

    private static IEnumerable<Type> TypesUsedInIl(MethodBase method, MethodBody body)
    {
        var il = body.GetILAsByteArray() ?? [];
        var typeArguments = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE) value = (short)(0xFE00 | il[position++]);
            var opCode = OpCodesByValue[value];
            switch (opCode.OperandType)
            {
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    var token = BitConverter.ToInt32(il, position);
                    var member = method.Module.ResolveMember(token, typeArguments, methodArguments)
                        ?? throw new InvalidOperationException($"Не удалось разрешить токен в {method.DeclaringType}.{method.Name}.");
                    foreach (var used in MemberTypes(member)) yield return used;
                    position += 4;
                    break;
                case OperandType.InlineSwitch:
                    position += 4 + BitConverter.ToInt32(il, position) * 4;
                    break;
                default:
                    position += OperandSize(opCode.OperandType);
                    break;
            }
        }
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member)
    {
        switch (member)
        {
            case Type type:
                yield return type;
                break;
            case FieldInfo field:
                yield return field.DeclaringType!;
                yield return field.FieldType;
                break;
            case MethodBase method:
                if (method.DeclaringType is not null) yield return method.DeclaringType;
                if (method is MethodInfo info)
                {
                    yield return info.ReturnType;
                    if (info.IsGenericMethod)
                        foreach (var argument in info.GetGenericArguments()) yield return argument;
                }
                foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
                break;
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var element in Flatten(type.GetElementType()!)) yield return element;
            yield break;
        }
        yield return type;
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            foreach (var argument in type.GetGenericArguments())
                foreach (var nested in Flatten(argument))
                    yield return nested;
    }

    private static int OperandSize(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4
    };

    private static string WpfSourceDir([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "src", "NothingVpn.Desktop.Wpf"));
}
