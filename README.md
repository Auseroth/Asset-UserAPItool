# Adding a New Cloud Provider to LdapCloudSync

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
