using System.Text.Json;
using ApsSamples.Models;
using ApsSamples.Services;
using Xunit;

namespace ApsSamples.Tests;

public class RevitLinkChangeClassifierTests
{
    private static readonly IReadOnlySet<string> NoKnownIds = new HashSet<string>();

    private const string MepInstanceId = "YWVjZX5ZMUpBM1RzeE9FMk9RaDVOeWgyaTV4X0wyQ35iMjgyOWVhOS1hYjAzLTU2YmQtODA1MS03YTQyMWQzNWViNjk";
    private const string MepTypeId = "YWVjZX5ZMUpBM1RzeE9FMk9RaDVOeWgyaTV4X0wyQ344NTBjYzEwYS05ZWU4LTVhMTQtODA5MC0yZjEyNTg2N2ZjYTk";

    // Captured diffElementGroupByVersionWithLatest response from issue #25 (a link to MEP.rvt was added).
    private const string SampleResponse = """
{
  "data": {
    "diffElementGroupByVersionWithLatest": {
      "pagination": {
        "pageSize": 3,
        "cursor": null
      },
      "result": [
        {
          "type": "MODIFICATION",
          "element": {
            "id": "YWVjZX5ZMUpBM1RzeE9FMk9RaDVOeWgyaTV4X0wyQ344Y2QzZDZjNS0zNWFiLTViMGUtYTkzYi03NGMyYjA2ZmFlYTk",
            "name": "Project Phase Information",
            "properties": {
              "results": [
                {
                  "name": "Edited by",
                  "value": "",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Workset",
                  "value": 13,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "External ID",
                  "value": "81076ca9-9f6e-4243-8d23-d384c57a4703-00000000",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Revit Category Type Id",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Family Name",
                  "value": "Project Phase Information",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Element Name",
                  "value": "Project Phase Information",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Element Context",
                  "value": "Instance",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Revit Element ID",
                  "value": "0",
                  "definition": {
                    "units": null
                  }
                }
              ]
            }
          },
          "differences": {
            "pagination": {
              "cursor": null,
              "pageSize": 0
            },
            "results": []
          }
        },
        {
          "type": "ADDITION",
          "element": {
            "id": "YWVjZX5ZMUpBM1RzeE9FMk9RaDVOeWgyaTV4X0wyQ35iMjgyOWVhOS1hYjAzLTU2YmQtODA1MS03YTQyMWQzNWViNjk",
            "name": "MEP.rvt : 1 : location <Not Shared>",
            "properties": {
              "results": [
                {
                  "name": "Comments",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Mark",
                  "value": "1",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Edited by",
                  "value": "pedronadal",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Workset",
                  "value": 0,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "External ID",
                  "value": "8364e7e9-f13c-4642-b6e3-8f7b80945ed6-00000f04",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Export to IFC",
                  "value": "By Type",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Export to IFC As",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "IFC Predefined Type",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "IfcGUID",
                  "value": "23PEVfyJn6GhRZZtk0b57I",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Revit Category Type Id",
                  "value": "RVT Links",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Family Name",
                  "value": "Linked Revit Model",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Element Name",
                  "value": "MEP.rvt : 1 : location <Not Shared>",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Element Context",
                  "value": "Instance",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Name",
                  "value": "1",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Revit Element ID",
                  "value": "3844",
                  "definition": {
                    "units": null
                  }
                }
              ]
            }
          },
          "differences": {
            "pagination": null,
            "results": null
          }
        },
        {
          "type": "ADDITION",
          "element": {
            "id": "YWVjZX5ZMUpBM1RzeE9FMk9RaDVOeWgyaTV4X0wyQ344NTBjYzEwYS05ZWU4LTVhMTQtODA5MC0yZjEyNTg2N2ZjYTk",
            "name": "MEP.rvt",
            "properties": {
              "results": [
                {
                  "name": "Description",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Manufacturer",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Model",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Type Comments",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "URL",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Assembly Code",
                  "value": "",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Assembly Description",
                  "value": "",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Cost",
                  "value": null,
                  "definition": {
                    "units": {
                      "name": "Currency"
                    }
                  }
                },
                {
                  "name": "Edited by",
                  "value": "pedronadal",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Workset",
                  "value": 0,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "External ID",
                  "value": "8364e7e9-f13c-4642-b6e3-8f7b80945ed6-00000f03",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Export Type to IFC",
                  "value": "Default",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Export Type to IFC As",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Type IFC Predefined Type",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Type IfcGUID",
                  "value": "23PEVfyJn6GhRZZtk0b57L",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Keynote",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Revit Category Type Id",
                  "value": "RVT Links",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Family Name",
                  "value": "Linked Revit Model",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Element Name",
                  "value": "MEP.rvt",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Element Context",
                  "value": "Type",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "File Name",
                  "value": "MEP",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Reference Type",
                  "value": "Overlay",
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Room Bounding",
                  "value": false,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Type Mark",
                  "value": null,
                  "definition": {
                    "units": null
                  }
                },
                {
                  "name": "Revit Element ID",
                  "value": "3843",
                  "definition": {
                    "units": null
                  }
                }
              ]
            }
          },
          "differences": {
            "pagination": null,
            "results": null
          }
        }
      ]
    }
  },
  "extensions": {
    "pointValue": {
      "requestedQueryPointValue": 9,
      "responseQueryPointValue": 0
    }
  }
}
""";

    private static JsonElement SampleResults() =>
        JsonDocument.Parse(SampleResponse).RootElement
            .GetProperty("data")
            .GetProperty("diffElementGroupByVersionWithLatest")
            .GetProperty("result");

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Classify_SampleResponse_ReturnsSingleLinkAddedForMep()
    {
        var changes = RevitLinkChangeClassifier.Classify(SampleResults(), NoKnownIds);

        var change = Assert.Single(changes);
        Assert.Equal(RevitWebhookEventTypes.LinkAdded, change.EventType);
        Assert.Equal("MEP.rvt", change.FileName);
        Assert.Equal(1, change.InstanceCount);
        Assert.True(change.TypeChanged);
        Assert.Equal(new[] { MepInstanceId, MepTypeId }, change.ElementIds);
    }

    [Fact]
    public void Classify_NonLinkModification_IsIgnored()
    {
        var results = Parse("""
            [
              {
                "type": "MODIFICATION",
                "element": {
                  "id": "phase",
                  "name": "Project Phase Information",
                  "properties": { "results": [
                    { "name": "Revit Category Type Id", "value": null },
                    { "name": "Family Name", "value": "Project Phase Information" },
                    { "name": "Workset", "value": 13 }
                  ] }
                },
                "differences": { "pagination": { "cursor": null, "pageSize": 0 }, "results": [] }
              }
            ]
            """);

        Assert.Empty(RevitLinkChangeClassifier.Classify(results, NoKnownIds));
    }

    [Fact]
    public void Classify_LinkModification_IsIgnored()
    {
        var results = Parse("""
            [
              {
                "type": "MODIFICATION",
                "element": {
                  "id": "type-1",
                  "name": "MEP.rvt",
                  "properties": { "results": [
                    { "name": "Revit Category Type Id", "value": "RVT Links" },
                    { "name": "Element Context", "value": "Type" }
                  ] }
                },
                "differences": { "pagination": null, "results": null }
              }
            ]
            """);

        Assert.Empty(RevitLinkChangeClassifier.Classify(results, NoKnownIds));
    }

    [Fact]
    public void Classify_NullDifferencesAndMixedValueTypes_DoesNotThrow()
    {
        var results = Parse("""
            [
              {
                "type": "ADDITION",
                "element": {
                  "id": "type-1",
                  "name": "ARCH.rvt",
                  "properties": { "results": [
                    { "name": "Room Bounding", "value": false },
                    { "name": "Workset", "value": 0 },
                    { "name": "Comments", "value": null },
                    { "name": "Revit Category Type Id", "value": "RVT Links" },
                    { "name": "Element Context", "value": "Type" }
                  ] }
                },
                "differences": null
              }
            ]
            """);

        var change = Assert.Single(RevitLinkChangeClassifier.Classify(results, NoKnownIds));
        Assert.Equal("ARCH.rvt", change.FileName);
        Assert.True(change.TypeChanged);
        Assert.Equal(0, change.InstanceCount);
    }

    [Fact]
    public void Classify_NonArrayInput_ReturnsEmpty()
    {
        Assert.Empty(RevitLinkChangeClassifier.Classify(Parse("null"), NoKnownIds));
        Assert.Empty(RevitLinkChangeClassifier.Classify(Parse("{}"), NoKnownIds));
    }

    [Fact]
    public void Classify_RemovalWithStrippedPropertiesAndKnownIds_ReturnsLinkRemoved()
    {
        var results = Parse("""
            [
              { "type": "REMOVAL", "element": { "id": "type-1", "name": "MEP.rvt", "properties": { "results": [] } }, "differences": null },
              { "type": "REMOVAL", "element": { "id": "inst-1", "name": "MEP.rvt : 1 : location <Not Shared>", "properties": null }, "differences": null }
            ]
            """);
        var known = new HashSet<string> { "type-1", "inst-1" };

        var change = Assert.Single(RevitLinkChangeClassifier.Classify(results, known));
        Assert.Equal(RevitWebhookEventTypes.LinkRemoved, change.EventType);
        Assert.Equal("MEP.rvt", change.FileName);
        Assert.Equal(1, change.InstanceCount);
        Assert.True(change.TypeChanged);
        Assert.Equal(new[] { "type-1", "inst-1" }, change.ElementIds);
    }

    [Fact]
    public void Classify_RemovalWithLinkProperties_ReturnsLinkRemoved()
    {
        var results = Parse("""
            [
              {
                "type": "REMOVAL",
                "element": {
                  "id": "inst-9",
                  "name": "STR.rvt : 2 : location <Not Shared>",
                  "properties": { "results": [
                    { "name": "Family Name", "value": "Linked Revit Model" },
                    { "name": "Element Context", "value": "Instance" }
                  ] }
                }
              }
            ]
            """);

        var change = Assert.Single(RevitLinkChangeClassifier.Classify(results, NoKnownIds));
        Assert.Equal(RevitWebhookEventTypes.LinkRemoved, change.EventType);
        Assert.Equal("STR.rvt", change.FileName);
        Assert.False(change.TypeChanged);
    }

    [Fact]
    public void Classify_UnknownRemovalWithoutLinkProperties_IsIgnored()
    {
        var results = Parse("""
            [
              { "type": "REMOVAL", "element": { "id": "wall-1", "name": "Basic Wall", "properties": { "results": [] } } }
            ]
            """);
        var known = new HashSet<string> { "type-1" };

        Assert.Empty(RevitLinkChangeClassifier.Classify(results, known));
    }

    [Fact]
    public void Classify_InstanceOnlyAddition_ReturnsLinkAddedWithoutTypeChange()
    {
        var results = Parse("""
            [
              {
                "type": "ADDITION",
                "element": {
                  "id": "inst-2",
                  "name": "MEP.rvt : 2 : location <Not Shared>",
                  "properties": { "results": [
                    { "name": "Revit Category Type Id", "value": "RVT Links" },
                    { "name": "Element Context", "value": "Instance" }
                  ] }
                }
              },
              {
                "type": "ADDITION",
                "element": {
                  "id": "inst-3",
                  "name": "MEP.rvt : 3 : location <Not Shared>",
                  "properties": { "results": [
                    { "name": "Revit Category Type Id", "value": "RVT Links" },
                    { "name": "Element Context", "value": "Instance" }
                  ] }
                }
              }
            ]
            """);

        var change = Assert.Single(RevitLinkChangeClassifier.Classify(results, NoKnownIds));
        Assert.Equal(RevitWebhookEventTypes.LinkAdded, change.EventType);
        Assert.Equal("MEP.rvt", change.FileName);
        Assert.Equal(2, change.InstanceCount);
        Assert.False(change.TypeChanged);
    }

    [Fact]
    public void Classify_AdditionsOfDifferentFiles_ReturnsOneChangePerFile()
    {
        var results = Parse("""
            [
              { "type": "ADDITION", "element": { "id": "a", "name": "MEP.rvt", "properties": { "results": [
                { "name": "Revit Category Type Id", "value": "RVT Links" }, { "name": "Element Context", "value": "Type" } ] } } },
              { "type": "ADDITION", "element": { "id": "b", "name": "STR.rvt", "properties": { "results": [
                { "name": "Revit Category Type Id", "value": "RVT Links" }, { "name": "Element Context", "value": "Type" } ] } } }
            ]
            """);

        var changes = RevitLinkChangeClassifier.Classify(results, NoKnownIds);

        Assert.Equal(new[] { "MEP.rvt", "STR.rvt" }, changes.Select(c => c.FileName));
    }
}
