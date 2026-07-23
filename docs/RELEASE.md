# Release Procedure

1. Confirm `MarketRouteCN/MarketRouteCN.csproj` contains the intended four-part version.
2. Push all changes to `main` and wait for the **Build** workflow to succeed.
3. In GitHub Settings, enable Actions **Read and write permissions**.
4. Create and push a matching tag, for example `v0.5.0.0`.
5. The release workflow will:
   - download the official Dalamud distribution;
   - restore and build the plugin;
   - validate `latest.zip`;
   - publish `MarketRouteCN.zip` to a GitHub Release;
   - update `repo.json` on `main`.
6. Verify:
   - the Release contains `MarketRouteCN.zip`;
   - `repo.json` is not `[]`;
   - the raw custom-repository URL returns a JSON array.
