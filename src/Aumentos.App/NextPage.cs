using System;
using System.Text;
using System.Text.Json;

namespace Aumentos;

internal static class NextPage
{
    internal static string ComponentStream(string html)
    {
        const string marker = "self.__next_f.push(";
        var output = new StringBuilder();
        int offset = 0;
        while ((offset = html.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            offset += marker.Length;
            var end = html.IndexOf("</script>", offset, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;
            var close = html.LastIndexOf(')', end, end - offset + 1);
            if (close > offset)
            {
                using var json = JsonDocument.Parse(html.AsMemory(offset, close - offset));
                var args = json.RootElement;
                if (args.ValueKind == JsonValueKind.Array && args.GetArrayLength() == 2 &&
                    args[0].ValueKind == JsonValueKind.Number && args[0].TryGetInt32(out var type) && type == 1 &&
                    args[1].ValueKind == JsonValueKind.String)
                    output.Append(args[1].GetString());
            }
            offset = end;
        }
        return output.ToString();
    }
}
