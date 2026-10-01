# Website captures

Run on Windows with .NET 10:

`dotnet run --project Tools/WebsiteCaptures --configuration Release -- .`

Renders the repository's WPF view markup and shared App.xaml resources with
deterministic LMU example data into `Artifacts/website-captures`. No production
app startup, monitoring, user diagnostics, Steam operation or repair is executed.
Only code-behind event hooks are removed for this isolated rendering process.

Website captions must identify these as app screens with example data, not a
new real-world repair or proof of a successful repair. Refresh them when the UI
changes and visually inspect the PNGs before publishing. CI stores the images as
an artifact and emits their base64 bytes for retrieval by the GitHub connector.
