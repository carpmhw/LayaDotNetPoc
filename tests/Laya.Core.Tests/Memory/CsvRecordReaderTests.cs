extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Analysis;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class CsvRecordReaderTests
{
    /// <summary>驗證 CSV quotes、comma、escaped quote 與 embedded newline 可被重算解析。</summary>
    [Fact]
    public void ReadRecords_ParsesQuotedFieldsAndEmbeddedNewlines()
    {
        const string csv = "run_id,reason,value\r\nrun-1,\"idle, \"\"30 seconds\"\"\",12.5\r\nrun-2,\"line one\nline two\",\r\n";

        var records = CsvRecordReader.ReadRecords(new StringReader(csv)).ToArray();

        Assert.Equal(3, records.Length);
        Assert.Equal(new[] { "run-1", "idle, \"30 seconds\"", "12.5" }, records[1]);
        Assert.Equal(new[] { "run-2", "line one\nline two", string.Empty }, records[2]);
    }

    /// <summary>驗證未終止 quoted field 會回報 malformed CSV。</summary>
    [Fact]
    public void ReadRecords_RejectsUnterminatedQuotedField()
    {
        Assert.Throws<InvalidDataException>(() => CsvRecordReader.ReadRecords(new StringReader("a,\"unterminated")).ToArray());
    }
}
