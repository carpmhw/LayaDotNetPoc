using System.Text;

namespace Laya.MemoryBenchmarks.Analysis;

/// <summary>讀取 RFC 4180 quoted CSV records，不將大檔案一次載入記憶體。</summary>
internal static class CsvRecordReader
{
    /// <summary>逐 record 解析 comma、escaped quote、CRLF 與 quoted embedded newline。</summary>
    public static IEnumerable<IReadOnlyList<string>> ReadRecords(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var fields = new List<string>();
        var field = new StringBuilder();
        var insideQuotes = false;
        var quoteClosed = false;
        var hasContent = false;

        while (reader.Read() is var characterCode && characterCode >= 0)
        {
            var character = (char)characterCode;
            hasContent = true;
            if (insideQuotes)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (reader.Peek() == '"')
                {
                    _ = reader.Read();
                    field.Append('"');
                }
                else
                {
                    insideQuotes = false;
                    quoteClosed = true;
                }

                continue;
            }

            if (quoteClosed && character is not (',' or '\r' or '\n'))
            {
                throw new InvalidDataException("CSV contains characters after a closing quote before the next delimiter.");
            }

            if (character == ',' )
            {
                fields.Add(field.ToString());
                field.Clear();
                quoteClosed = false;
                continue;
            }

            if (character is '\r' or '\n')
            {
                if (character == '\r' && reader.Peek() == '\n')
                {
                    _ = reader.Read();
                }

                fields.Add(field.ToString());
                yield return fields.ToArray();
                fields.Clear();
                field.Clear();
                quoteClosed = false;
                hasContent = false;
                continue;
            }

            if (character == '"')
            {
                if (field.Length != 0 || quoteClosed)
                {
                    throw new InvalidDataException("CSV quote appeared inside an unquoted field.");
                }

                insideQuotes = true;
                continue;
            }

            field.Append(character);
        }

        if (insideQuotes)
        {
            throw new InvalidDataException("CSV ended before a quoted field was closed.");
        }

        if (hasContent || fields.Count > 0 || field.Length > 0 || quoteClosed)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }
}
