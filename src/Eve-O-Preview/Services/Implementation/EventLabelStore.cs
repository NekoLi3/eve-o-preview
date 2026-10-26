using System;
using System.Collections.Generic;
using System.Drawing;

namespace EveOPreview.Services.Implementation
{
	/// <summary>
	/// Keeps only the latest event label per character and expires it lazily.
	/// </summary>
	internal sealed class EventLabelStore
	{
		private sealed class Entry
		{
			public EventLabelSnapshot Snapshot;
			public DateTime EventUtc;
			public DateTime ExpiresUtc;
		}

		private readonly object _syncRoot = new object();
		private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

		internal void Record(string characterName, string text, Color color, DateTime eventUtc, int durationMs, DateTime nowUtc)
		{
			if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(text))
			{
				return;
			}

			lock (this._syncRoot)
			{
				if (this._entries.TryGetValue(characterName, out Entry existing) && existing.EventUtc > eventUtc)
				{
					// A newer event is already displayed; never let an older line
					// that was read later replace it.
					return;
				}

				this._entries[characterName] = new Entry
				{
					Snapshot = new EventLabelSnapshot(text, color),
					EventUtc = eventUtc,
					ExpiresUtc = nowUtc.AddMilliseconds(durationMs)
				};
			}
		}

		internal EventLabelSnapshot GetLatest(string characterName, DateTime nowUtc)
		{
			if (string.IsNullOrEmpty(characterName))
			{
				return null;
			}

			lock (this._syncRoot)
			{
				if (!this._entries.TryGetValue(characterName, out Entry entry))
				{
					return null;
				}

				if (entry.ExpiresUtc <= nowUtc)
				{
					this._entries.Remove(characterName);
					return null;
				}

				return entry.Snapshot;
			}
		}

		internal void RemoveExpired(DateTime nowUtc)
		{
			lock (this._syncRoot)
			{
				List<string> expired = null;
				foreach (KeyValuePair<string, Entry> pair in this._entries)
				{
					if (pair.Value.ExpiresUtc <= nowUtc)
					{
						expired = expired ?? new List<string>();
						expired.Add(pair.Key);
					}
				}

				if (expired == null)
				{
					return;
				}

				foreach (string key in expired)
				{
					this._entries.Remove(key);
				}
			}
		}
	}
}
