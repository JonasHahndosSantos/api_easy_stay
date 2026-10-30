using System.Text.Json.Nodes;

namespace ApiEasyStay.Properties.Services;

internal static class SyncPayload
{
    public static string Merge(string previous, string incoming)
    {
        var result = JsonNode.Parse(previous)!.AsObject();
        foreach (var property in JsonNode.Parse(incoming)!.AsObject())
        {
            var name = result.Select(x => x.Key).FirstOrDefault(
                key => string.Equals(key, property.Key, StringComparison.OrdinalIgnoreCase));
            result[name ?? property.Key] = property.Value?.DeepClone();
        }
        return result.ToJsonString();
    }

    public static bool IsDeleted(string data)
    {
        return JsonNode.Parse(data)!.AsObject().Any(property =>
            string.Equals(property.Key, "DataHoraDeletado", StringComparison.OrdinalIgnoreCase) &&
            property.Value is not null);
    }
}
