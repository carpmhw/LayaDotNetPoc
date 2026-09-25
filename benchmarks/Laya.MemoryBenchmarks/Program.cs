using Laya.MemoryBenchmarks.Orchestration;

namespace Laya.MemoryBenchmarks;

internal static class Program
{
    /// <summary>執行 Phase 3A 隔離程序矩陣 host。</summary>
    /// <param name="args">命令列參數。</param>
    /// <returns>依 campaign stage 結果回傳 process exit code。</returns>
    private static int Main(string[] args)
    {
        try
        {
            var options = MemoryCampaignCommandLine.Parse(args);
            return MemoryCampaignRunner.ExecuteAsync(options, FindRepositoryRoot())
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    /// <summary>向上尋找 repository solution，供 campaign 子程序解析 project paths。</summary>
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

        throw new DirectoryNotFoundException("Could not locate LayaDotNetPoc.sln from the current working directory.");
    }
}
