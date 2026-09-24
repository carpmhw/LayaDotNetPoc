using System.Text;

namespace Laya.Core.Configuration;

/// <summary>只讀取 ONNX protobuf 中真正的 external-data 引用，不載入 tensor 權重。</summary>
/// <remarks>
/// 技術選擇：使用 .NET 8 BCL 的 bounded protobuf wire reader，無新增 NuGet 相依。
/// 欄位契約依據 https://github.com/onnx/onnx/blob/v1.17.0/onnx/onnx.proto 。
/// 相較完整 generated ModelProto，這裡以 seek 跳過 raw_data／packed data，避免配置 GiB 權重。
/// 相較現有 Google.Protobuf 3.34.1 CodedInputStream，其公開 API 未提供 PushLimit，
/// 此 reader 直接在每個 tag、varint、長度與子訊息套用父邊界，並限制深度及 metadata 字串。
/// 成本為固定檔案緩衝、最多 64 層堆疊及引用集合；僅由初始化 validator 呼叫。
/// 這是資產引用 reader，不取代 ONNX checker 或 ORT 的 operator／shape 驗證。
/// </remarks>
internal sealed class OnnxExternalDataReader
{
    private readonly Stream _stream;
    private readonly HashSet<string> _locations = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private bool _hasGraph;

    /// <summary>建立共用同一串流且以絕對位置限制訊息範圍的 reader。</summary>
    private OnnxExternalDataReader(Stream stream) => _stream = stream;

    /// <summary>解析模型引用並確保成功或失敗均關閉底層檔案。</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        var reader = new OnnxExternalDataReader(stream);
        reader.ReadMessage(MessageKind.Model, stream.Length, 0);
        if (!reader._hasGraph) throw new InvalidDataException("ONNX model has no graph.");
        return reader._locations.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    /// <summary>依 schema 遞迴走訪訊息，只讀取引用相關欄位並略過其他 payload。</summary>
    private void ReadMessage(MessageKind kind, long end, int depth)
    {
        if (depth > 64) throw new InvalidDataException("ONNX nesting exceeds 64 levels.");
        Dictionary<string, string>? external = null;
        ulong dataLocation = 0;
        var hasDataLocation = false;
        while (_stream.Position < end)
        {
            var (number, wire) = ReadTag(end);
            var child = ChildKind(kind, number);
            if (child is not null)
            {
                RequireWire(wire, 2);
                var childEnd = ReadEnd(end);
                if (kind == MessageKind.Model && number == 7)
                {
                    if (_hasGraph) throw new InvalidDataException("ONNX model contains duplicate graph fields.");
                    _hasGraph = true;
                }
                ReadMessage(child.Value, childEnd, depth + 1);
            }
            else if (kind == MessageKind.Tensor && number == 13)
            {
                RequireWire(wire, 2);
                var entry = ReadEntry(ReadEnd(end));
                external ??= new Dictionary<string, string>(StringComparer.Ordinal);
                if (!external.TryAdd(entry.Key, entry.Value))
                    throw new InvalidDataException($"Duplicate ONNX external_data key '{entry.Key}'.");
            }
            else if (kind == MessageKind.Tensor && number == 14)
            {
                RequireWire(wire, 0);
                if (hasDataLocation) throw new InvalidDataException("Duplicate ONNX data_location.");
                dataLocation = ReadVarint(end);
                hasDataLocation = true;
                if (dataLocation > 1) throw new InvalidDataException("Unknown ONNX data_location.");
            }
            else SkipField(wire, end);
        }

        if (kind == MessageKind.Tensor && (external is not null || dataLocation == 1))
        {
            if (dataLocation != 1 || external is null ||
                !external.TryGetValue("location", out var location) || string.IsNullOrWhiteSpace(location))
                throw new InvalidDataException("ONNX external tensor requires EXTERNAL data_location and a location.");
            _locations.Add(location);
        }
    }

    /// <summary>對應 ONNX 中能包含 graph、node 或 tensor 的 schema 欄位。</summary>
    private static MessageKind? ChildKind(MessageKind parent, int field) => (parent, field) switch
    {
        (MessageKind.Model, 7) => MessageKind.Graph,
        (MessageKind.Model, 20) => MessageKind.Training,
        (MessageKind.Model, 25) => MessageKind.Function,
        (MessageKind.Graph, 1) => MessageKind.Node,
        (MessageKind.Graph, 5) => MessageKind.Tensor,
        (MessageKind.Graph, 15) => MessageKind.SparseTensor,
        (MessageKind.Node, 5) => MessageKind.Attribute,
        (MessageKind.Attribute, 5 or 10) => MessageKind.Tensor,
        (MessageKind.Attribute, 6 or 11) => MessageKind.Graph,
        (MessageKind.Attribute, 22 or 23) => MessageKind.SparseTensor,
        (MessageKind.SparseTensor, 1 or 2) => MessageKind.Tensor,
        (MessageKind.Training, 1 or 2) => MessageKind.Graph,
        (MessageKind.Function, 7) => MessageKind.Node,
        (MessageKind.Function, 11) => MessageKind.Attribute,
        _ => null
    };

    /// <summary>讀取 external_data 的 key/value，拒絕重複或不完整欄位。</summary>
    private KeyValuePair<string, string> ReadEntry(long end)
    {
        string? key = null;
        string? value = null;
        while (_stream.Position < end)
        {
            var (number, wire) = ReadTag(end);
            if (number is 1 or 2)
            {
                RequireWire(wire, 2);
                var textEnd = ReadEnd(end);
                var length = textEnd - _stream.Position;
                if (length > 65536) throw new InvalidDataException("ONNX external metadata exceeds 64 KiB.");
                var bytes = new byte[(int)length];
                _stream.ReadExactly(bytes);
                var text = StrictUtf8.GetString(bytes);
                if (number == 1 && key is null) key = text;
                else if (number == 2 && value is null) value = text;
                else throw new InvalidDataException("Duplicate ONNX external metadata field.");
            }
            else SkipField(wire, end);
        }
        if (string.IsNullOrEmpty(key) || value is null)
            throw new InvalidDataException("Incomplete ONNX external metadata entry.");
        return new KeyValuePair<string, string>(key, value);
    }

    /// <summary>解碼合法的 protobuf tag，拒絕零欄號與 ONNX 未使用的 group wire type。</summary>
    private (int Number, int Wire) ReadTag(long end)
    {
        var tag = ReadVarint(end);
        if (tag > uint.MaxValue || tag >> 3 == 0 || (tag & 7) is 3 or 4 or 6 or 7)
            throw new InvalidDataException("Invalid ONNX protobuf tag.");
        return ((int)(tag >> 3), (int)(tag & 7));
    }

    /// <summary>限制最多十 bytes 的 varint，任何讀取均不得跨越目前訊息邊界。</summary>
    private ulong ReadVarint(long end)
    {
        ulong result = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            if (_stream.Position >= end) throw new InvalidDataException("Truncated ONNX protobuf varint.");
            var value = _stream.ReadByte();
            if (value < 0 || (shift == 63 && value > 1))
                throw new InvalidDataException("Invalid ONNX protobuf varint.");
            result |= (ulong)(value & 127) << shift;
            if ((value & 128) == 0) return result;
        }
        throw new InvalidDataException("Invalid ONNX protobuf varint.");
    }

    /// <summary>取得 length-delimited 欄位結尾，先驗父邊界再進入或跳過 payload。</summary>
    private long ReadEnd(long parentEnd)
    {
        var length = ReadVarint(parentEnd);
        if (length > int.MaxValue || length > (ulong)(parentEnd - _stream.Position))
            throw new InvalidDataException("ONNX protobuf length exceeds its message boundary.");
        return _stream.Position + (long)length;
    }

    /// <summary>以 seek 跳過不需要的資料，絕不將 raw_data 配置為 byte 陣列。</summary>
    private void SkipField(int wire, long end)
    {
        if (wire == 0) { ReadVarint(end); return; }
        if (wire == 2) { _stream.Position = ReadEnd(end); return; }
        var length = wire == 1 ? 8 : 4;
        if (end - _stream.Position < length) throw new InvalidDataException("Truncated ONNX fixed-width field.");
        _stream.Seek(length, SeekOrigin.Current);
    }

    /// <summary>防止引用相關欄位因錯誤 wire type 而被當成未知資料略過。</summary>
    private static void RequireWire(int actual, int expected)
    {
        if (actual != expected) throw new InvalidDataException("Invalid ONNX protobuf wire type.");
    }

    private enum MessageKind { Model, Graph, Node, Attribute, Tensor, SparseTensor, Training, Function }
}
