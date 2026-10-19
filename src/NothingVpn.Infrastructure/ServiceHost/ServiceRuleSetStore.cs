using System.Security.Cryptography;
using NothingVpn.Application.Engine;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Infrastructure.ServiceHost;

/// <summary>
/// Rule-set службы (контракт v2): содержимое из запроса пишется в каталог службы под её собственным именем
/// <c>NN-&lt;sha256&gt;.srs</c>. Имя файла из запроса в путь не попадает. Каталог принадлежит только ядру службы:
/// при каждой подготовке и остановке он очищается.
/// </summary>
internal sealed class ServiceRuleSetStore(string directory) : IEngineRuleSetStore
{
    public string Directory { get; } = directory;

    public IReadOnlyList<string> Prepare(IReadOnlyList<EngineRuleSet> ruleSets)
    {
        Clear();
        System.IO.Directory.CreateDirectory(Directory);

        var names = new List<string>(ruleSets.Count);
        for (var i = 0; i < ruleSets.Count; i++)
        {
            var content = ruleSets[i].Content;
            if (content is null)
                throw new InvalidOperationException("Служба принимает rule-set (.srs) только содержимым. Обновите приложение.");
            RuleSetPolicyValidator.EnsureContentWithinLimits([content.Length]);

            var name = $"{i:D2}-{Convert.ToHexString(SHA256.HashData(content))[..16].ToLowerInvariant()}.srs";
            File.WriteAllBytes(Path.Combine(Directory, name), content);
            names.Add(name);
        }

        return names;
    }

    public void Clear()
    {
        if (!System.IO.Directory.Exists(Directory))
            return;

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.srs"))
        {
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
