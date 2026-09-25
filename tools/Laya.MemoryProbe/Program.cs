using Laya.MemoryProbe.Configuration;
using Laya.MemoryProbe.Reporting;

namespace Laya.MemoryProbe;

internal static class Program
{
    /// <summary>執行 Phase 3A 記憶體探針命令列 host。</summary>
    /// <param name="args">命令列參數。</param>
    /// <returns>依 probe run outcome 回傳 process exit code。</returns>
    private static int Main(string[] args)
    {
        try
        {
            var options = MemoryProbeCommandLine.Parse(args);
            var resolution = MemoryProbeCommandLine.ResolveModel(options);
            var repositoryRoot = FindRepositoryRoot();
            return MemoryProbeRunHost.ExecuteAsync(options, resolution, repositoryRoot)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    /// <summary>尋找 repository 根目錄，供 fixture 與 source identity resolver 使用。</summary>
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LayaDotNetPoc.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
