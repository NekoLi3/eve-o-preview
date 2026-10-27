using EveOPreview.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EveOPreview.Services.Implementation
{
	/// <summary>
	/// Parses Gamelog event lines and applies the configured label rules.
	/// Matching is intentionally cheap: the raw line is tried first, and only
	/// lines that contain markup are cleaned and retried.
	/// </summary>
	internal sealed class EventLogParser
	{
		private const int MAX_LABEL_LENGTH = 64;
		private static readonly Regex MarkupRegex = new Regex("<[^>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private sealed class CompiledRule
		{
			public Regex Pattern;
			public string Label;
			public Color Color;
			public bool SelfOnly;
		}

		private readonly List<CompiledRule> _rules = new List<CompiledRule>();

		internal int RuleCount => this._rules.Count;

		internal EventLogParser(IEnumerable<EventLabelRule> rules)
		{
			if (rules == null)
			{
				return;
			}

			foreach (EventLabelRule rule in rules)
			{
				if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrWhiteSpace(rule.Label))
				{
					continue;
				}

				if (!TryCreateRegex(rule.Pattern, out Regex regex) || !TryParseColor(rule.Color, out Color color))
				{
					continue;
				}

				this._rules.Add(new CompiledRule
				{
					Pattern = regex,
					Label = rule.Label,
					Color = color,
					SelfOnly = rule.SelfOnly
				});
			}
		}

		internal bool TryMatch(string rawMessage, string characterName, out string label, out Color color)
		{
			label = null;
			color = Color.White;

			if (string.IsNullOrEmpty(rawMessage))
			{
				return false;
			}

			TryExtractAttackerName(rawMessage, out string fallbackName);

			if (rawMessage.IndexOf('<') < 0)
			{
				return this.TryMatchCore(rawMessage, characterName, true, fallbackName, out label, out color);
			}

			// Combat lines are full of markup, so a rule written against the clean
			// text cannot match the raw line. Do not run self-only rules against
			// the raw form: the target cannot be determined reliably there.
			if (this.TryMatchCore(rawMessage, characterName, false, fallbackName, out label, out color))
			{
				return true;
			}

			string cleanMessage = StripMarkup(rawMessage);
			return this.TryMatchCore(cleanMessage, characterName, true, fallbackName, out label, out color);
		}

		private bool TryMatchCore(string message, string characterName, bool allowSelfOnly, string fallbackName, out string label, out Color color)
		{
			label = null;
			color = Color.White;

			foreach (CompiledRule rule in this._rules)
			{
				Match match;
				try
				{
					match = rule.Pattern.Match(message);
				}
				catch (RegexMatchTimeoutException)
				{
					continue;
				}

				if (!match.Success)
				{
					continue;
				}

				if (rule.SelfOnly && (!allowSelfOnly || !IsSelfTarget(message, characterName)))
				{
					continue;
				}

				string text = rule.Label;
				if (text.Contains("{name}", StringComparison.Ordinal))
				{
					Group nameGroup = match.Groups["name"];
					string name = nameGroup.Success ? nameGroup.Value : fallbackName;
					text = text.Replace("{name}", Sanitize(name));
				}

				text = Sanitize(text);
				if (string.IsNullOrEmpty(text))
				{
					continue;
				}

				label = text;
				color = rule.Color;
				return true;
			}

			return false;
		}

		internal static bool TryParseLine(string line, out DateTime timestampUtc, out string message)
		{
			timestampUtc = default;
			message = null;

			if (string.IsNullOrEmpty(line) || line[0] != '[')
			{
				return false;
			}

			int closeBracket = line.IndexOf(']');
			if (closeBracket < 0)
			{
				return false;
			}

			string stamp = line.Substring(1, closeBracket - 1).Trim();
			if (!DateTime.TryParseExact(stamp, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestampUtc))
			{
				return false;
			}

			int messageStart = closeBracket + 1;
			while (messageStart < line.Length && line[messageStart] == ' ')
			{
				messageStart++;
			}

			// Optional category marker: (notify), (combat), (None), ...
			if (messageStart < line.Length && line[messageStart] == '(')
			{
				int closeCategory = line.IndexOf(')', messageStart + 1);
				if (closeCategory >= 0)
				{
					messageStart = closeCategory + 1;
					while (messageStart < line.Length && line[messageStart] == ' ')
					{
						messageStart++;
					}
				}
			}

			if (messageStart >= line.Length)
			{
				return false;
			}

			message = line.Substring(messageStart);
			return true;
		}

		internal static string StripMarkup(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "";
			}

			string withoutTags = MarkupRegex.Replace(value.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase), " ");
			return WhitespaceRegex.Replace(withoutTags, " ").Trim();
		}

		/// <summary>
		/// Extracts the attacker pilot from combat lines such as
		/// "Warp disruption attempt from [ship] [pilot] to [target]".
		/// Raw EVE markup keeps the ship and the character in separate text
		/// nodes, so the last text node before "to" is the pilot, not the ship.
		/// </summary>
		internal static bool TryExtractAttackerName(string rawMessage, out string name)
		{
			name = null;
			if (string.IsNullOrEmpty(rawMessage))
			{
				return false;
			}

			if (rawMessage.IndexOf('<') >= 0)
			{
				return TryExtractAttackerNameFromMarkup(rawMessage, out name);
			}

			int fromIndex = rawMessage.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
			if (fromIndex < 0)
			{
				return false;
			}

			int start = fromIndex + 6;
			int toIndex = rawMessage.IndexOf(" to ", start, StringComparison.OrdinalIgnoreCase);
			if (toIndex < 0)
			{
				return false;
			}

			name = Sanitize(rawMessage.Substring(start, toIndex - start));
			return name.Length > 0;
		}

		private static bool TryExtractAttackerNameFromMarkup(string rawMessage, out string name)
		{
			name = null;
			bool inAttacker = false;
			string candidate = null;

			foreach (string part in MarkupRegex.Split(rawMessage))
			{
				string text = WhitespaceRegex.Replace(part, " ").Trim();
				if (text.Length == 0)
				{
					continue;
				}

				if (!inAttacker)
				{
					if (text.Equals("from", StringComparison.OrdinalIgnoreCase) ||
						text.StartsWith("from ", StringComparison.OrdinalIgnoreCase))
					{
						inAttacker = true;
					}

					continue;
				}

				if (text.Equals("to", StringComparison.OrdinalIgnoreCase) ||
					text.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
				{
					break;
				}

				candidate = text;
			}

			if (string.IsNullOrEmpty(candidate))
			{
				return false;
			}

			name = Sanitize(candidate.Trim(' ', '!', '.', ',', ';', ':'));
			return name.Length > 0;
		}

		internal static bool IsSelfTarget(string message, string characterName)
		{
			if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(characterName))
			{
				return false;
			}

			int toIndex = message.LastIndexOf(" to ", StringComparison.OrdinalIgnoreCase);
			if (toIndex < 0)
			{
				return false;
			}

			string target = message.Substring(toIndex + 4);
			// EVE uses the literal word "you" when the listener is the target,
			// instead of repeating the character name.
			return ContainsName(target, "you") || ContainsName(target, characterName);
		}

		internal static bool ContainsName(string text, string name)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name))
			{
				return false;
			}

			int index = 0;
			while ((index = text.IndexOf(name, index, StringComparison.OrdinalIgnoreCase)) >= 0)
			{
				bool leftBoundary = index == 0 || !IsNameChar(text[index - 1]);
				int end = index + name.Length;
				bool rightBoundary = end >= text.Length || !IsNameChar(text[end]);
				if (leftBoundary && rightBoundary)
				{
					return true;
				}

				index = end;
			}

			return false;
		}

		internal static bool TryCreateRegex(string pattern, out Regex regex)
		{
			regex = null;
			if (string.IsNullOrWhiteSpace(pattern))
			{
				return false;
			}

			try
			{
				regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
				return true;
			}
			catch (ArgumentException)
			{
				return false;
			}
		}

		internal static bool TryParseColor(string value, out Color color)
		{
			color = Color.White;
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}

			try
			{
				object parsed = new ColorConverter().ConvertFromInvariantString(value);
				if (parsed is Color parsedColor)
				{
					color = parsedColor;
					return true;
				}
			}
			catch (Exception)
			{
				// Invalid color in the config - the rule is skipped.
			}

			return false;
		}

		private static string Sanitize(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "";
			}

			string result = StripMarkup(value);
			var builder = new StringBuilder(result.Length);
			foreach (char c in result)
			{
				if (!char.IsControl(c))
				{
					builder.Append(c);
				}
			}

			result = builder.ToString().Trim();
			if (result.Length > MAX_LABEL_LENGTH)
			{
				result = result.Substring(0, MAX_LABEL_LENGTH).TrimEnd();
			}

			return result;
		}

		private static bool IsNameChar(char value)
		{
			return char.IsLetterOrDigit(value) || value == '_' || value == '-';
		}
	}
}
