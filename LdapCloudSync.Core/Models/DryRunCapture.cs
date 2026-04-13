namespace LdapCloudSync.Core.Models;

/// <summary>
/// Captures a single HTTP request that would have been sent during a dry-run sync.
/// Contains the exact serialized JSON that the real sync path produces.
/// </summary>
public sealed class DryRunCapture
{
    public string Method { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string JsonBody { get; set; } = string.Empty;

    /// <summary>
    /// Returns the JSON body reformatted with indentation for display.
    /// </summary>
    public string PrettyJson
    {
        get
        {
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(JsonBody);
                return node?.ToJsonString(new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }) ?? JsonBody;
            }
            catch
            {
                return JsonBody;
            }
        }
    }
}