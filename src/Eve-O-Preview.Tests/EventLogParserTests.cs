using System;
using System.Collections.Generic;
using System.Drawing;
using EveOPreview.Configuration;
using EveOPreview.Services.Implementation;
using Xunit;

namespace EveOPreview.Tests
{
    public class EventLogParserTests
    {
        private const string ScrambleLine =
            "[ 2026.10.04 06:07:10 ] (combat) <color=0xffffffff><b>Warp scramble attempt</b> " +
            "<color=0x77ffffff><font size=10>from</font> <color=0xffffffff><b>Attacker</b> " +
            "<color=0x77ffffff><font size=10>to <b>Target</b>";

        [Fact]
        public void TryParseLine_Reads_Utc_Timestamp_And_Message()
        {
            bool parsed = EventLogParser.TryParseLine(
                "[ 2026.10.04 02:31:01 ] (notify) Your cloak deactivates due to proximity to a nearby Stargate.",
                out DateTime timestamp,
                out string message);

            Assert.True(parsed);
            Assert.Equal(new DateTime(2026, 10, 4, 2, 31, 1, DateTimeKind.Utc), timestamp);
            Assert.Equal("Your cloak deactivates due to proximity to a nearby Stargate.", message);
        }

        [Fact]
        public void TryParseLine_Accepts_Lines_Without_Category()
        {
            bool parsed = EventLogParser.TryParseLine(
                "[ 2026.10.04 02:31:01 ] plain message",
                out DateTime timestamp,
                out string message);

            Assert.True(parsed);
            Assert.Equal(new DateTime(2026, 10, 4, 2, 31, 1, DateTimeKind.Utc), timestamp);
            Assert.Equal("plain message", message);
        }

        [Fact]
        public void TryParseLine_Rejects_Invalid_Lines()
        {
            Assert.False(EventLogParser.TryParseLine("not a log line", out _, out _));
            Assert.False(EventLogParser.TryParseLine("[ invalid ] message", out _, out _));
            Assert.False(EventLogParser.TryParseLine("[ 2026.10.04 02:31:01 ]", out _, out _));
        }

        [Fact]
        public void Cloak_Rule_Produces_Decloak_Label()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("Your cloak deactivates", "DECLOAK", "Orange")
            });

            bool matched = parser.TryMatch(
                "Your cloak deactivates due to proximity to a nearby Stargate (Caldari System).",
                "Pilot",
                out string label,
                out Color color);

            Assert.True(matched);
            Assert.Equal("DECLOAK", label);
            Assert.Equal(Color.Orange, color);
        }

        [Fact]
        public void Following_Rule_Captures_Character_Name()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("Following (?<name>.+?) in warp", "WARP {name}", "DeepSkyBlue")
            });

            bool matched = parser.TryMatch("Following NekoLi in warp", "Pilot", out string label, out Color color);

            Assert.True(matched);
            Assert.Equal("WARP NekoLi", label);
            Assert.Equal(Color.DeepSkyBlue, color);
        }

        [Fact]
        public void Raw_Markup_Fast_Path_Matches_Without_Cleaning()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("Warp scramble attempt", "SCRAM", "Red")
            });

            Assert.True(EventLogParser.TryParseLine(ScrambleLine, out _, out string message));
            bool matched = parser.TryMatch(message, "Target", out string label, out Color color);

            Assert.True(matched);
            Assert.Equal("SCRAM", label);
            Assert.Equal(Color.Red, color);
        }

        [Fact]
        public void SelfOnly_Scramble_Matches_When_Character_Is_The_Target()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("Warp scramble attempt", "SCRAM", "Red", selfOnly: true)
            });

            Assert.True(EventLogParser.TryParseLine(ScrambleLine, out _, out string message));

            Assert.True(parser.TryMatch(message, "Target", out string label, out _));
            Assert.Equal("SCRAM", label);
            Assert.False(parser.TryMatch(message, "Attacker", out _, out _));
            Assert.False(parser.TryMatch(message, "Someone Else", out _, out _));
        }

        [Fact]
        public void SelfOnly_Scramble_Matches_When_Log_Uses_You_For_The_Target()
        {
            const string line =
                "[ 2026.08.12 01:06:31 ] (combat) <color=0xffffffff><b>Warp scramble attempt</b> " +
                "<color=0x77ffffff><font size=10>from</font> <color=0xffffffff><b>Attacker</b> " +
                "<color=0x77ffffff><font size=10>to <b><color=0xffffffff></font>you!";

            var parser = new EventLogParser(new[]
            {
                Rule("Warp scramble attempt", "SCRAM", "Red", selfOnly: true)
            });

            Assert.True(EventLogParser.TryParseLine(line, out _, out string message));
            Assert.True(parser.TryMatch(message, "Any Character", out string label, out _));
            Assert.Equal("SCRAM", label);
        }

        [Fact]
        public void SelfOnly_Scramble_Does_Not_Match_When_You_Are_The_Attacker()
        {
            const string line =
                "[ 2026.07.26 21:54:21 ] (combat) <color=0xffffffff><b>Warp scramble attempt</b> " +
                "<color=0x77ffffff><font size=10>from</font> <color=0xffffffff><b>you</b> " +
                "<color=0x77ffffff><font size=10>to <b><color=0xffffffff></font>Target";

            var parser = new EventLogParser(new[]
            {
                Rule("Warp scramble attempt", "SCRAM", "Red", selfOnly: true)
            });

            Assert.True(EventLogParser.TryParseLine(line, out _, out string message));
            Assert.False(parser.TryMatch(message, "Any Character", out _, out _));
        }

        [Fact]
        public void SelfOnly_Rules_Ignore_Fleet_Wide_Combat_Lines()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("Warp disruption attempt", "DISRUPT", "Red", selfOnly: true)
            });

            const string line = "Warp disruption attempt from Attacker to Target";

            Assert.True(parser.TryMatch(line, "Target", out string label, out _));
            Assert.Equal("DISRUPT", label);
            Assert.False(parser.TryMatch(line, "Unrelated Pilot", out _, out _));
        }

        [Fact]
        public void Cleaned_Fallback_Allows_Rules_Written_Against_Clean_Text()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("from Attacker to Target", "CLEAN")
            });

            Assert.True(EventLogParser.TryParseLine(ScrambleLine, out _, out string message));
            Assert.True(parser.TryMatch(message, "Target", out string label, out _));
            Assert.Equal("CLEAN", label);
        }

        [Fact]
        public void First_Matching_Rule_Wins()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("cloak", "FIRST"),
                Rule("deactivates", "SECOND")
            });

            Assert.True(parser.TryMatch("Your cloak deactivates", "Pilot", out string label, out _));
            Assert.Equal("FIRST", label);
        }

        [Fact]
        public void Invalid_Rules_Are_Skipped()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("[", "INVALID_REGEX", "Red"),
                Rule("cloak", "INVALID_COLOR", "NotAColor"),
                Rule("cloak", "VALID", "Orange")
            });

            Assert.True(parser.TryMatch("Your cloak deactivates", "Pilot", out string label, out Color color));
            Assert.Equal("VALID", label);
            Assert.Equal(Color.Orange, color);
        }

        [Fact]
        public void Disabled_Rules_Are_Skipped()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("cloak", "DISABLED", enabled: false),
                Rule("cloak", "ENABLED")
            });

            Assert.True(parser.TryMatch("Your cloak deactivates", "Pilot", out string label, out _));
            Assert.Equal("ENABLED", label);
        }

        [Fact]
        public void Label_And_Name_Are_Sanitized()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("cloak", "<b>Hi</b>\u0001"),
                Rule("Following (?<name>.+?) in warp", "WARP <b>{name}</b>")
            });

            Assert.True(parser.TryMatch("Your cloak deactivates", "Pilot", out string label, out _));
            Assert.Equal("Hi", label);

            Assert.True(parser.TryMatch("Following NekoLi in warp", "Pilot", out label, out _));
            Assert.Equal("WARP NekoLi", label);
        }

        [Fact]
        public void Long_Labels_Are_Truncated()
        {
            var parser = new EventLogParser(new[]
            {
                Rule("cloak", new string('A', 100))
            });

            Assert.True(parser.TryMatch("Your cloak deactivates", "Pilot", out string label, out _));
            Assert.Equal(64, label.Length);
        }

        [Fact]
        public void ContainsName_Respects_Word_Boundaries()
        {
            Assert.True(EventLogParser.ContainsName("to Target", "Target"));
            Assert.True(EventLogParser.ContainsName("to Target-Two", "Target-Two"));
            Assert.False(EventLogParser.ContainsName("to TargetTwo", "Target"));
            Assert.False(EventLogParser.ContainsName("to TwoTarget", "Target"));
        }

        [Fact]
        public void Empty_Rule_List_Matches_Nothing()
        {
            var parser = new EventLogParser(Array.Empty<EventLabelRule>());

            Assert.False(parser.TryMatch("Your cloak deactivates", "Pilot", out _, out _));
        }

        private static EventLabelRule Rule(string pattern, string label, string color = "White", bool selfOnly = false, bool enabled = true)
        {
            return new EventLabelRule
            {
                Pattern = pattern,
                Label = label,
                Color = color,
                SelfOnly = selfOnly,
                Enabled = enabled
            };
        }
    }
}
