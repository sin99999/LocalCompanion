using System.Text.Json;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class LayaDecisionTests
{
    [Fact]
    public void ShouldSkip_OnlyWhenYesIsClearlyLow()
    {
        Assert.False(LayaDecision.ShouldSkip(null));
        Assert.False(LayaDecision.ShouldSkip(0.35));
        Assert.False(LayaDecision.ShouldSkip(0.9));
        Assert.True(LayaDecision.ShouldSkip(0.34));
    }

    [Fact]
    public void ReadNoul_ReadsTheYesProbability()
    {
        const string json = """
            {
              "answers": {
                "q": { "type": "noul", "noul": 0.22 }
              }
            }
            """;

        Assert.Equal(0.22, LayaDecision.ReadNoul(json, "q"));
        Assert.Null(LayaDecision.ReadNoul("not json", "q"));
        Assert.Null(LayaDecision.ReadNoul(json, "missing"));
    }

    [Fact]
    public void ShouldSearchNotes_NeedsAConfidentSearchChoice()
    {
        const string json = """
            {
              "answers": {
                "route": {
                  "type": "choice",
                  "choice": "search",
                  "confidence": 0.86
                }
              }
            }
            """;

        var choice = LayaDecision.ReadChoice(json, "route");
        Assert.True(LayaDecision.ShouldSearchNotes(choice));

        const string chat = """
            { "answers": { "route": { "choice": "chat", "confidence": 0.99 } } }
            """;
        Assert.False(LayaDecision.ShouldSearchNotes(LayaDecision.ReadChoice(chat, "route")));
        Assert.False(LayaDecision.ShouldSearchNotes(LayaDecision.ReadChoice(json.Replace("0.86", "0.4"), "route")));
    }

    [Fact]
    public void BuildNoul_ClipsTheState()
    {
        var body = LayaDecision.BuildNoul("q", new string('あ', 500), "Is this worth keeping?");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("noul", doc.RootElement.GetProperty("questions").GetProperty("q").GetProperty("type").GetString());
        Assert.Equal(LayaDecision.MaxStateChars, doc.RootElement.GetProperty("state").GetString()!.Length);
    }

    [Fact]
    public void BuildSearchChoice_KeepsADrawRequestWhenItDependsOnSavedNotes()
    {
        var notesOnly = LayaDecision.BuildSearchChoice("絵が欲しい");
        using (var doc = JsonDocument.Parse(notesOnly))
        {
            var criteria = doc.RootElement.GetProperty("questions").GetProperty("route").GetProperty("criteria");
            Assert.Contains("still counts", criteria.GetProperty("search").GetString(), StringComparison.Ordinal);
            Assert.Contains("without the user's saved notes", criteria.GetProperty("chat").GetString(), StringComparison.Ordinal);
        }

        var route = LayaDecision.BuildRouteChoice("絵が欲しい");
        using (var doc = JsonDocument.Parse(route))
        {
            var criteria = doc.RootElement.GetProperty("questions").GetProperty("route").GetProperty("criteria");
            Assert.Contains("still counts", criteria.GetProperty("notes").GetString(), StringComparison.Ordinal);
            Assert.Contains("statute", criteria.GetProperty("web").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FormatRoute_UsesADotAndSkipsTheMessageText()
    {
        Assert.Equal("decision route unread", LayaDecision.FormatRoute(null));
        Assert.Equal("decision route search 0.86", LayaDecision.FormatRoute(new LayaChoice("search", 0.864)));
    }
}
