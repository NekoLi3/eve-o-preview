using System.Collections.Generic;
using System.Linq;
using EveOPreview.Configuration;
using EveOPreview.Configuration.Implementation;
using Newtonsoft.Json;
using Xunit;

namespace EveOPreview.Tests
{
    public class EventLabelConfigurationTests
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        [Fact]
        public void Configuration_Initializes_Default_Event_Label_Settings()
        {
            var configuration = new ThumbnailConfiguration();

            Assert.True(configuration.ShowEventLabelsInThumbnail);
            Assert.Equal(1500, configuration.EventLabelDurationMs);
            Assert.Equal("", configuration.EventLabelFont);
            Assert.Equal(ZoomAnchor.C, configuration.EventLabelAnchor);
            Assert.Equal("", configuration.GamelogsPath);
            Assert.NotNull(configuration.EventLabelRules);
            Assert.Equal(4, configuration.EventLabelRules.Count);

            Assert.Equal("DECLOAK", configuration.EventLabelRules[0].Label);
            Assert.Equal("Following {name} in warp", configuration.EventLabelRules[1].Label);
            Assert.False(configuration.EventLabelRules[1].SelfOnly);
            Assert.Equal("Scrambled by {name}", configuration.EventLabelRules[2].Label);
            Assert.True(configuration.EventLabelRules[2].SelfOnly);
            Assert.Equal("Disrupted by {name}", configuration.EventLabelRules[3].Label);
            Assert.True(configuration.EventLabelRules[3].SelfOnly);
            Assert.All(configuration.EventLabelRules, rule => Assert.True(rule.Enabled));
        }

        [Fact]
        public void Configuration_Serializes_Event_Label_Settings_With_Expected_Names()
        {
            var configuration = new ThumbnailConfiguration
            {
                EventLabelDurationMs = 2500,
                EventLabelFont = "Segoe UI, 11pt",
                GamelogsPath = "C:\\logs",
                EventLabelRules = new List<EventLabelRule>
                {
                    new EventLabelRule { Pattern = "cloak", Label = "DECLOAK", Color = "Orange" }
                }
            };

            string json = JsonConvert.SerializeObject(configuration, SerializerSettings);

            Assert.Contains("\"ShowEventLabelsInThumbnail\"", json);
            Assert.Contains("\"EventLabelDurationMs\"", json);
            Assert.Contains("\"EventLabelFont\"", json);
            Assert.Contains("\"EventLabelAnchor\"", json);
            Assert.Contains("\"GamelogsPath\"", json);
            Assert.Contains("\"EventLabelRules\"", json);
            Assert.Contains("\"Pattern\"", json);
            Assert.Contains("\"SelfOnly\"", json);
            Assert.Contains("\"Enabled\"", json);
        }

        [Fact]
        public void Configuration_Loads_Event_Label_Rules_From_Json_Replacing_Defaults()
        {
            const string json = @"{
                ""EventLabelDurationMs"": 3000,
                ""EventLabelRules"": [
                    { ""Pattern"": ""cloak"", ""Label"": ""DECLOAK"", ""Color"": ""Orange"", ""SelfOnly"": false, ""Enabled"": true }
                ]
            }";

            var configuration = new ThumbnailConfiguration();
            JsonConvert.PopulateObject(json, configuration, SerializerSettings);

            Assert.Equal(3000, configuration.EventLabelDurationMs);
            Assert.Single(configuration.EventLabelRules);
            Assert.Equal("DECLOAK", configuration.EventLabelRules.First().Label);
        }

        [Fact]
        public void ApplyRestrictions_Clamps_Duration_And_Restores_Missing_Rules()
        {
            var configuration = new ThumbnailConfiguration
            {
                EventLabelDurationMs = 100,
                EventLabelRules = null
            };

            configuration.ApplyRestrictions();

            Assert.Equal(500, configuration.EventLabelDurationMs);
            Assert.NotNull(configuration.EventLabelRules);
            Assert.Equal(4, configuration.EventLabelRules.Count);

            configuration.EventLabelDurationMs = 20000;
            configuration.ApplyRestrictions();

            Assert.Equal(10000, configuration.EventLabelDurationMs);
        }

        [Fact]
        public void ApplyRestrictions_Migrates_Old_Default_Labels_And_Preserves_Custom_Rules()
        {
            var configuration = new ThumbnailConfiguration
            {
                EventLabelRules = new List<EventLabelRule>
                {
                    new EventLabelRule { Pattern = "Following (?<name>.+?) in warp", Label = "WARP {name}", Color = "DeepSkyBlue" },
                    new EventLabelRule { Pattern = "Warp scramble attempt", Label = "SCRAM", Color = "Red", SelfOnly = true },
                    new EventLabelRule { Pattern = "Warp disruption attempt", Label = "DISRUPT", Color = "Red", SelfOnly = true },
                    new EventLabelRule { Pattern = "custom", Label = "CUSTOM {name}", Color = "Green" }
                }
            };

            configuration.ApplyRestrictions();

            Assert.Equal("Following {name} in warp", configuration.EventLabelRules[0].Label);
            Assert.Equal("Scrambled by {name}", configuration.EventLabelRules[1].Label);
            Assert.Equal("Disrupted by {name}", configuration.EventLabelRules[2].Label);
            Assert.Equal("CUSTOM {name}", configuration.EventLabelRules[3].Label);

            // Applying restrictions again must not change already migrated rules.
            configuration.ApplyRestrictions();
            Assert.Equal("Following {name} in warp", configuration.EventLabelRules[0].Label);
        }
    }
}
