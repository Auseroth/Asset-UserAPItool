# LDAPult - Adding a New Cloud Provider

This guide shows **every integration point** needed to add support for a new cloud asset management API (e.g., AssetPanda, Lansweeper, etc.).

---

## Overview: What You'll Create

To add a new provider, you'll:
1. **Create one provider class** with API-specific logic
2. **Add one preset** (optional, for convenience)
3. **Register the provider** in the factory
4. **Update UI** (if the provider has unique features)

**Estimated time:** 30-60 minutes for a standard REST API

---

## Step 1: Create the Provider Class

**File:** `LdapCloudSync.Core/Providers/YourProviderClient.cs`

using System.Net.Http.Headers; using LdapCloudSync.Core.Interfaces; using LdapCloudSync.Core.Models; using Serilog;
namespace LdapCloudSync.Core.Providers;
/// <summary> /// Cloud client for [YourProvider] API. /// Handles [YourProvider]-specific authentication and endpoints. /// </summary> public sealed class YourProviderClient : BaseCloudClient { public YourProviderClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null) : base(target, httpClient, logger) { }
/// <summary>
/// Override if your API uses custom authentication.
/// For standard Bearer/Basic/ApiKey, you can omit this.
/// </summary>
protected override void ApplyCustomAuth(HttpRequestMessage request, HttpMethod method, string endpoint)
{
    // Example: Custom header-based auth
    request.Headers.TryAddWithoutValidation("X-API-Key", _target.Connection.ApiKey);
    request.Headers.TryAddWithoutValidation("X-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
}

/// <summary>
/// Optional: Add provider-specific methods like category discovery.
/// </summary>
public async Task<List<(int Id, string Name)>> GetAssetTypesAsync()
{
    var request = BuildRequest(HttpMethod.Get, "/api/asset-types");
    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    // Parse and return your provider's category structure
    return [];
}
}

**If your provider uses standard auth** (Bearer token, Basic, or simple API key header), you don't need to override anything - just inherit `BaseCloudClient` and you're done.

---

## Step 2: Add a Preset (Optional but Recommended)

**File:** `LdapCloudSync.Core/Presets/PresetRegistry.cs`

Add your preset alongside Reftab and SnipeIT:

public static CloudPreset YourProvider { get; } = new() { Name = "YourProvider", ProviderType = "YourProvider", // Must match Step 3 BaseUrl = "https://api.yourprovider.com/v1", AuthType = AuthType.BearerToken, // or ApiKey, BasicAuth, Hmac, etc.
Assets = new PresetEndpoints
{
    GetEndpoint = "/assets",
    PostEndpoint = "/assets",
    PutEndpoint = "/assets/{id}",
    ResponseItemsPath = "$.data", // JSONPath to array of items
    CloudIdField = "id",
    AdMatchField = "cn",
    CloudMatchField = "name",
    DefaultMappings =
    [
        new FieldMapping
        {
            CloudField = "name",
            AdAttributes = ["cn"],
            TransformExpression = null,
            DefaultValue = null
        },
        new FieldMapping
        {
            CloudField = "serial_number",
            AdAttributes = ["serialNumber"],
            TransformExpression = null,
            DefaultValue = null
        }
    ]
},

Users = new PresetEndpoints
{
    // Similar structure for users/loanees
}
};


**Note:** The preset is just a convenience for users. Your provider will work without it as long as you implement the client and register it.

**Then register it** in the static constructor:

static PresetRegistry() { Register(Reftab); Register(SnipeIt); Register(YourProvider); // Add this line }


---

## Step 3: Register in the Factory

**File:** `LdapCloudSync.Core/Providers/CloudClientFactory.cs`

Add a case for your provider:

public static class CloudClientFactory { public static ICloudClient CreateClient(CloudTargetConfig config, ILogger? logger = null) { return config.PresetOrigin switch { "Reftab" => new ReftabClient(config, logger: logger), "SnipeIT" => new GenericCloudClient(config, logger: logger), // Uses standard auth "YourProvider" => new YourProviderClient(config, logger: logger), // Add this _ => new GenericCloudClient(config, logger: logger) // Fallback for custom/blank }; } }


---

## Step 4: Update UI for Provider-Specific Features (Optional)

If your provider has unique features (like Reftab's category selector), add UI:

### A. Add ViewModel Properties

**File:** `LdapCloudSync.App/ViewModels/CloudTargetViewModel.cs`

arp // Show category selector only for providers that support it public bool ShowCategorySelector => PresetOrigin is "Reftab" or "YourProvider";
// Add provider-specific data public ObservableCollection<CategoryItem> YourProviderCategories { get; } = [];
// Add command to fetch provider-specific data public ICommand RefreshYourProviderDataCommand { get; }
// In constructor: RefreshYourProviderDataCommand = new AsyncRelayCommand(RefreshYourProviderDataAsync);
private async Task RefreshYourProviderDataAsync() { var targetConfig = BuildTargetConfig(); using var client = new YourProviderClient(targetConfig); var types = await client.GetAssetTypesAsync();
YourProviderCategories.Clear();
foreach (var (id, name) in types)
{
    YourProviderCategories.Add(new CategoryItem { Id = id, Name = name });
}
}


### B. Add UI Elements

**File:** `LdapCloudSync.App/Views/CloudTargetDetailView.xaml`

Add provider-specific UI in the appropriate section:

<!-- YourProvider Category Selection --> <StackPanel Visibility="{Binding ShowYourProviderSettings, Converter={StaticResource BoolToVis}}"> <TextBlock Text="Asset Type (YourProvider)" Style="{StaticResource FieldLabel}" /> <ComboBox ItemsSource="{Binding YourProviderCategories}" SelectedValue="{Binding AssetsTargetCategoryId}" DisplayMemberPath="Name" SelectedValuePath="Id" /> <Button Content="Refresh Types" Command="{Binding RefreshYourProviderDataCommand}" Style="{StaticResource SecondaryButton}" Margin="0,4,0,0" /> </StackPanel>

**Note:** Use `ShowYourProviderSettings` to conditionally show/hide UI elements based on the selected provider.


---

## Step 5: Test Your Integration

1. **Build the solution** (Ctrl+Shift+B)
2. **Run the app**
3. **Add a new cloud target**
4. **Select your preset** from the dropdown
5. **Fill in credentials** (Base URL, API Key, etc.)
6. **Click "Test Connection"** - should see 200 OK
7. **Click "Discover Fields"** to verify field mapping works
8. **Run a test sync** (10 records) to verify create/update logic

---

## Architecture Summary
User selects preset "YourProvider" -> PresetOrigin = "YourProvider" saved to config -> CloudClientFactory.CreateClient(config) -> Returns YourProviderClient instance -> SyncOrchestrator uses YourProviderClient for all API calls (test, discover, push)


---

## Configuration Points Reference

| **Component** | **File** | **Action** |
|---|---|---|
| **Provider Logic** | `Providers/YourProviderClient.cs` | Create class, override auth if needed |
| **Preset Template** | `Presets/PresetRegistry.cs` | Add preset config (endpoints, mappings) |
| **Factory Registration** | `Providers/CloudClientFactory.cs` | Add case for `PresetOrigin` |
| **UI (optional)** | `ViewModels/CloudTargetViewModel.cs` | Add properties, commands |
| **UI (optional)** | `Views/CloudTargetDetailView.xaml` | Add UI elements |

---

## Common Scenarios

### **Scenario 1: Standard REST API with Bearer Token**

No custom provider class needed! Just add a preset:

public static CloudPreset SimpleAPI { get; } = new() { Name = "SimpleAPI", ProviderType = "Generic", // Uses GenericCloudClient BaseUrl = "https://api.simple.com", AuthType = AuthType.BearerToken, // ... endpoints };


### **Scenario 2: Custom HMAC Signature (like Reftab)**

Create a provider class that overrides `ApplyCustomAuth()`:

protected override void ApplyCustomAuth(HttpRequestMessage request, HttpMethod method, string endpoint) { // Compute custom signature var signature = ComputeYourCustomHMAC(method, endpoint); request.Headers.TryAddWithoutValidation("Authorization", $"HMAC {signature}"); }


### **Scenario 3: Pagination Required**

Override `FetchExistingRecordsAsync()`:
protected override async Task<List<JsonObject>> FetchExistingRecordsAsync(SyncCategoryConfig categoryConfig) { var allRecords = new List<JsonObject>(); var page = 1;
while (true)
{
    var request = BuildRequest(HttpMethod.Get, $"{categoryConfig.GetEndpoint}?page={page}");
    var response = await _httpClient.SendAsync(request);
    // Parse, add to allRecords, check for next page
    if (noMorePages) break;
    page++;
}

return allRecords;
}


---

## Need Help?

1. **Check existing providers** (`ReftabClient.cs`, `GenericCloudClient.cs`) for examples
2. **API documentation** - look for authentication type, endpoint structure, response format
3. **Test with PowerShell/Postman first** to verify API behavior before coding

---

## Summary

**Minimum steps to add a provider:**
1. Create `YourProviderClient.cs` (override auth if custom)
2. Add preset to `PresetRegistry.cs`
3. Register in `CloudClientFactory.cs`

**Optional:**
4. Add UI for provider-specific features
5. Override methods for pagination, custom error handling, etc.

That's it! The architecture is designed so **80% of REST APIs need only 3 files touched**, and complex ones need just one additional provider class.

---

## Current Providers

### Reftab
- **Authentication:** Custom HMAC (RFC 2822 date + hex-to-base64 encoding)
- **Unique Features:** Asset category selection
- **File:** `ReftabClient.cs`

### Snipe-IT
- **Authentication:** Bearer token (standard)
- **File:** Uses `GenericCloudClient` (no custom class needed)

### Generic (Fallback)
- **Authentication:** Bearer, Basic, ApiKey, standard HMAC
- **File:** `GenericCloudClient.cs`
- **Use for:** Custom/blank presets, or any standard REST API