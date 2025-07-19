using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

HttpClient httpClient = new();
Dictionary<string, Dictionary<int, string>> packageVersions = new();
Dictionary<string, NuSpec> nuSpecs = new();

Console.WriteLine("argment:cs project names [-token GitHubToken]");

string? githubToken = null;
List<string> projects = new();
if (args.Any(a => a == "-token"))
{
    githubToken = args.Last();
    var temp = args.SkipLast(2);
    if (temp.Any())
    {
        projects = temp.ToList();
    }
}
else
{
    if (args.Any())
    {
        projects = args.ToList();
    }
}


//json data construct
foreach (var x in projects.Select((project, n) => (project, n)))
{
    Console.WriteLine($"{x.project} analyzing..");
    var data = GetNugetJson(x.project);
    try
    {
        var nugetList = JsonSerializer.Deserialize<NugetList>(data);
        foreach (var package in nugetList!.projects.SelectMany(p => p.frameworks).SelectMany(f => f.topLevelPackages.Concat(f.transitivePackages)))
        {
            if (!packageVersions.ContainsKey(package.id)) packageVersions[package.id] = new();
            packageVersions[package.id][x.n] = package.resolvedVersion;
        }
    }
    catch
    {
        Console.WriteLine($"Error:{x.project}");
    }
}

Console.WriteLine($"get nuspec..");
object gate = new();
Parallel.ForEach(packageVersions, kv =>
{
    var nuspec = GetNuSpecAsync(httpClient, kv.Key, kv.Value.First().Value).Result;      //最初に見つかったバージョンの情報を使うことにする
    lock (gate)
    {
        nuSpecs[kv.Key] = nuspec;
        Console.WriteLine($"{kv.Key}");
    }
});
Console.WriteLine($"Completed");

CreateOssList(httpClient, projects, packageVersions.AsReadOnly(), nuSpecs.AsReadOnly());

await Task.Delay(1000);







static void CreateOssList(HttpClient httpClient, IEnumerable<string> projects, ReadOnlyDictionary<string, Dictionary<int, string>> dict, ReadOnlyDictionary<string, NuSpec> nuspecs)
{
    Console.WriteLine($"\r\nCreate CSV file");
    //create csv file
    //header
    var fp = DateTime.Now.ToString("MMddhhmmss") + "dotnetPackageList.csv";
    using var sw = new StreamWriter(fp);
    sw.WriteLine("package," + string.Join(",", projects.Select(f => Path.GetFileNameWithoutExtension(f))) + ",License,LicenseURL,projectURL,repositoryURL,authors");
    //data
    int full = dict.Count();
    foreach (var d in dict.OrderBy(d => d.Key))
    {
        var @base = Enumerable.Range(0, projects.Count());

        sw.Write(d.Key + "," + string.Join(",", @base.Select(x => d.Value.ContainsKey(x) ? d.Value[x] : "")));

        var nuSpec = nuspecs[d.Key];

        sw.WriteLine("," + nuSpec.License + "," + nuSpec.LicenseURL + "," + nuSpec.ProjectURL + "," + nuSpec.RepositoryUrl + "," + nuSpec.Authors);
    }
    Console.WriteLine($"Completed");
}

///https://tech.guitarrapc.com/entry/2025/01/18/235900より
static async Task<NuSpec> GetNuSpecAsync(HttpClient httpClient, string packageId, string version)
{
    var response = await httpClient.GetStringAsync($"https://api.nuget.org/v3-flatcontainer/{packageId}/{version}/{packageId}.nuspec");
    var doc = XDocument.Parse(response);

    var ns = doc?.Root?.Name.Namespace;
    if (ns?.ToString().StartsWith("http://schemas.microsoft.com/packaging") is false)
    {
        return new("NoData", "NoData", "NoData", "NoData", "NoData");
    }

    var licenseValue = doc.Descendants(ns + "metadata")
    .Select(x => x?.Element(ns + "license")?.Value)
    .FirstOrDefault();

    var licenseUrl = doc.Descendants(ns + "metadata")
        .Select(x => x?.Element(ns + "licenseUrl")?.Value)
        .FirstOrDefault();

    var projectUrl = doc.Descendants(ns + "metadata")
    .Select(x => x?.Element(ns + "projectUrl")?.Value)
    .FirstOrDefault();

    var authors = doc.Descendants(ns + "metadata")
    .Select(x => x?.Element(ns + "authors")?.Value)
    .FirstOrDefault();

    var repositoryUrl = doc.Descendants(ns + "metadata")
    .Select(x => x?.Element(ns + "repository")?.Attributes())
    .SelectMany(x => x is null ? [] : x.Where(x => x?.Name == "url"))
    .FirstOrDefault()?.Value;

    return new(licenseValue ?? "", licenseUrl ?? "", projectUrl ?? "", authors ?? "", repositoryUrl ?? "");
}

static string GetNugetJson(string project)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"package list --project {project} --include-transitive --format json",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using (var process = Process.Start(startInfo)!)
    {
        return process.StandardOutput.ReadToEnd();
    }
}

public static class Constants
{
    public static readonly IEnumerable<string> Ignore_Authors = [
        "GrapeCity,",       //ComponentOneを想定、ライセンス表記不要
        "MESCIUS inc."      //ComponentOneを想定、ライセンス表記不要
        ];

    public static readonly IEnumerable<string> Ignore_LicenseUrl = [
        "https://licenses.nuget.org"       //一般的なフォーマットのリンクのため
    ];
}


record NuSpec(string License, string LicenseURL, string ProjectURL, string Authors, string RepositoryUrl);

/// <summary>
/// json解析用
/// </summary>
class NugetList
{
    public class Projects
    {
        public class Frameworks
        {
            public class Package
            {
                public string id { get; set; } = string.Empty;
                public string resolvedVersion { get; set; } = string.Empty;
            }
            public List<Package> topLevelPackages { get; set; } = [];
            public List<Package> transitivePackages { get; set; } = [];
        }
        public List<Frameworks> frameworks { get; set; } = [];
    }
    public List<Projects> projects { get; set; } = [];
}





