using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// 用法: AsmInfo <assembly.dll> [namespacePrefixFilter] [typeNameSubstring]
// 输出该程序集定义的命名空间概览，并按需检索类型。
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: AsmInfo <assembly.dll> [nsFilter] [typeFilter]");
    return 1;
}

string path = args[0];
string nsFilter = args.Length > 1 ? args[1] : "";
string typeFilter = args.Length > 2 ? args[2] : "";

using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
if (!pe.HasMetadata)
{
    Console.WriteLine($"[{Path.GetFileName(path)}] 不是托管程序集（无元数据）");
    return 2;
}

var md = pe.GetMetadataReader();
var asmDef = md.GetAssemblyDefinition();
Console.WriteLine($"程序集 : {md.GetString(asmDef.Name)}");
Console.WriteLine($"版本   : {asmDef.Version}");
Console.WriteLine($"文件   : {Path.GetFileName(path)}  ({new FileInfo(path).Length:N0} bytes)");
Console.WriteLine($"类型数 : {md.TypeDefinitions.Count}");

var nsCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var hits = new List<string>();

foreach (var handle in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(handle);
    string ns = md.GetString(td.Namespace);
    string name = md.GetString(td.Name);

    if (!string.IsNullOrEmpty(ns))
    {
        nsCounts.TryGetValue(ns, out int c);
        nsCounts[ns] = c + 1;
    }

    if (nsFilter.Length > 0 && ns.StartsWith(nsFilter, StringComparison.Ordinal))
    {
        if (typeFilter.Length == 0 || name.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
        {
            hits.Add($"{ns}.{name}");
        }
    }
}

if (nsFilter == "--refs")
{
    Console.WriteLine("\n引用到的程序集：");
    foreach (var h in md.AssemblyReferences.OrderBy(r => md.GetString(md.GetAssemblyReference(r).Name), StringComparer.Ordinal))
    {
        var ar = md.GetAssemblyReference(h);
        Console.WriteLine($"    {md.GetString(ar.Name),-40} {ar.Version}");
    }
    return 0;
}

Console.WriteLine($"顶层命名空间 {nsCounts.Count} 个，前 25 个：");
foreach (var kv in nsCounts.OrderByDescending(k => k.Value).Take(25))
{
    Console.WriteLine($"    {kv.Key,-45} {kv.Value,6} 个类型");
}

if (nsFilter.Length > 0)
{
    hits.Sort(StringComparer.Ordinal);
    Console.WriteLine($"\n匹配 '{nsFilter}*' / '{typeFilter}' 的类型共 {hits.Count} 个：");
    foreach (var h in hits.Take(40)) Console.WriteLine("    " + h);
    if (hits.Count > 40) Console.WriteLine($"    ... 其余 {hits.Count - 40} 个省略");
}

return 0;
