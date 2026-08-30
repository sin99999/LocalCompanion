using System.Text.Json;
using LocalCompanion.Services.LlamaNative;

namespace LocalCompanion.Core.Tests;

public sealed class LlamaCppInstallerReleasePickTests
{
    [Fact]
    public void PickReleaseWithWindowsBinaries_SkipsTagWithoutWinZip()
    {
        const string json = """
            [
              {
                "tag_name": "v0.3.0",
                "assets": [ { "name": "nightly-tag.txt" } ]
              },
              {
                "tag_name": "b10679",
                "assets": [
                  { "name": "llama-b10679-bin-win-cpu-x64.zip" },
                  { "name": "llama-b10679-bin-win-cuda-12.4-x64.zip" }
                ]
              }
            ]
            """;

        using var doc = JsonDocument.Parse(json);
        var picked = LlamaCppInstaller.PickReleaseWithWindowsBinaries(doc.RootElement);
        Assert.NotNull(picked);
        Assert.Equal("b10679", picked.Value.GetProperty("tag_name").GetString());
    }

    [Fact]
    public void PickReleaseWithWindowsBinaries_EmptyWhenNoWinZip()
    {
        const string json = """
            [
              {
                "tag_name": "v0.3.0",
                "assets": [ { "name": "nightly-tag.txt" } ]
              }
            ]
            """;

        using var doc = JsonDocument.Parse(json);
        Assert.Null(LlamaCppInstaller.PickReleaseWithWindowsBinaries(doc.RootElement));
    }
}
