using EveOPreview.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EveOPreview.Services.Implementation
{
	/// <summary>
	/// Tracks the solar system each EVE character is currently in by parsing the
	/// per-character Local chatlogs (Documents\EVE\logs\Chatlogs\Local_*.txt).
	/// Each file belongs to one character (the "Listener:" header line) and
	/// carries "EVE System > Channel changed to Local : &lt;system&gt;" lines;
	/// the last one in the file is the character's current system. Chatlog files
	/// rotate per session, so for each character the most recently written file
	/// is authoritative and stale files cannot overwrite newer data.
	/// </summary>
	sealed class GamelogMonitor : IGamelogMonitor
	{
		#region Private constants
		private const string CHATLOGS_SUBDIRECTORY = @"EVE\logs\Chatlogs";
		private const string CHATLOG_FILE_PATTERN = "Local_*.txt";
		private const int REFRESH_PERIOD_MS = 3000;

		private const string LISTENER_PREFIX = "Listener:";
		private const string CHANNEL_CHANGED_MARKER = "EVE System > Channel changed to Local : ";
		#endregion

		#region Private fields
		private readonly IThumbnailConfiguration _configuration;
		private readonly object _syncRoot;
		private readonly Dictionary<string, string> _characterSystems;
		private readonly Dictionary<string, long> _characterSystemSources;
		private readonly Dictionary<string, ChatlogFileState> _fileStates;

		private readonly string _chatlogsPath;

		private FileSystemWatcher _watcher;
		private Timer _refreshTimer;
		private bool _stopped;
		#endregion

		private sealed class ChatlogFileState
		{
			// Null when the file has no Listener header (i.e. it is not a valid Local chatlog)
			public string CharacterName;
			public long Offset;
		}

		public GamelogMonitor(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;
			this._syncRoot = new object();
			this._characterSystems = new Dictionary<string, string>();
			this._characterSystemSources = new Dictionary<string, long>();
			this._fileStates = new Dictionary<string, ChatlogFileState>();

			this._chatlogsPath = string.IsNullOrEmpty(this._configuration.ChatlogsPath)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), GamelogMonitor.CHATLOGS_SUBDIRECTORY)
				: this._configuration.ChatlogsPath;

			this._stopped = true;
		}

		public void Start()
		{
			lock (this._syncRoot)
			{
				if (!this._stopped)
				{
					return;
				}

				this._stopped = false;
			}

			this.SetupWatcher();

			// Periodic refresh as a safety net: EVE rotates the chatlog files and
			// writes them in bursts, which the FileSystemWatcher alone can miss
			lock (this._syncRoot)
			{
				if (!this._stopped && this._refreshTimer == null)
				{
					this._refreshTimer = new Timer((state) => this.Refresh(), null, GamelogMonitor.REFRESH_PERIOD_MS, GamelogMonitor.REFRESH_PERIOD_MS);
				}
			}
		}

		public void Stop()
		{
			lock (this._syncRoot)
			{
				this._stopped = true;
			}

			this._refreshTimer?.Dispose();
			this._refreshTimer = null;
			this._watcher?.Dispose();
			this._watcher = null;
		}

		public string GetSystemForCharacter(string characterName)
		{
			if (string.IsNullOrEmpty(characterName))
			{
				return null;
			}

			lock (this._syncRoot)
			{
				return this._characterSystems.TryGetValue(characterName, out string system) ? system : null;
			}
		}

		#region Private methods
		private void SetupWatcher()
		{
			lock (this._syncRoot)
			{
				if (this._stopped)
				{
					return;
				}

				this._watcher?.Dispose();
				this._watcher = null;
			}

			if (!Directory.Exists(this._chatlogsPath))
			{
				// The chatlogs folder may not exist yet (client never ran) -
				// the periodic timer keeps retrying until it appears
				return;
			}

			try
			{
				FileSystemWatcher watcher = new FileSystemWatcher(this._chatlogsPath, GamelogMonitor.CHATLOG_FILE_PATTERN);
				watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
				watcher.Created += this.ChatlogChanged_Handler;
				watcher.Changed += this.ChatlogChanged_Handler;
				watcher.Deleted += this.ChatlogChanged_Handler;
				watcher.Renamed += this.ChatlogChanged_Handler;
				watcher.EnableRaisingEvents = true;

				lock (this._syncRoot)
				{
					if (this._stopped)
					{
						watcher.Dispose();
						return;
					}

					this._watcher = watcher;
				}

				this.Refresh();
			}
			catch (Exception)
			{
				// The folder may be temporarily unavailable; the timer will retry
			}
		}

		private void ChatlogChanged_Handler(object sender, FileSystemEventArgs e)
		{
			this.Refresh();
		}

		private void Refresh()
		{
			lock (this._syncRoot)
			{
				if (this._stopped)
				{
					return;
				}

				try
				{
					if (!Directory.Exists(this._chatlogsPath))
					{
						return;
					}

					// Oldest files first: the newest file for a character is
					// processed last, so its system value wins
					string[] files = Directory.GetFiles(this._chatlogsPath, GamelogMonitor.CHATLOG_FILE_PATTERN)
						.OrderBy(file => File.GetLastWriteTime(file))
						.ToArray();

					foreach (string filePath in files)
					{
						this.ProcessFile(filePath);
					}

					this.CleanupFileStates(files);
				}
				catch (Exception)
				{
					// A file may be locked or the folder temporarily unavailable;
					// the periodic timer will retry
				}
			}
		}

		private void ProcessFile(string filePath)
		{
			if (!this._fileStates.TryGetValue(filePath, out ChatlogFileState state))
			{
				state = new ChatlogFileState();
				this._fileStates[filePath] = state;
			}

			DateTime fileWriteTime;
			try
			{
				fileWriteTime = File.GetLastWriteTime(filePath);
			}
			catch (IOException)
			{
				return;
			}

			try
			{
				using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					// The file was truncated or replaced since the last read - start over
					if (stream.Length < state.Offset)
					{
						state.Offset = 0;
						state.CharacterName = null;
					}

					stream.Seek(state.Offset, SeekOrigin.Begin);

					using (StreamReader reader = new StreamReader(stream, Encoding.Unicode, true, 4096, true))
					{
						// The chatlogs are UTF-16LE; ReadToEnd consumes the stream
						// up to its end, so the offset is the exact byte position
						string content = reader.ReadToEnd();

						if (content.Length == 0)
						{
							return;
						}

						// Only complete lines are processed; an incomplete trailing
						// line (no newline yet) is left for the next refresh
						int lastNewline = content.LastIndexOf('\n');
						if (lastNewline < 0)
						{
							return;
						}

						// UTF-16: every char is 2 bytes
						long newOffset = state.Offset + (long)(lastNewline + 1) * 2;
						string textToProcess = content.Substring(0, lastNewline + 1);

						bool parsingHeader = (state.Offset == 0) && (state.CharacterName == null);
						foreach (string rawLine in textToProcess.Split('\n'))
						{
							string line = rawLine.TrimEnd('\r');

							if (parsingHeader)
							{
								string trimmed = line.Trim();
								if (trimmed.StartsWith(GamelogMonitor.LISTENER_PREFIX, StringComparison.Ordinal))
								{
									state.CharacterName = trimmed.Substring(GamelogMonitor.LISTENER_PREFIX.Length).Trim();
								}
								else if (trimmed.StartsWith("[", StringComparison.Ordinal))
								{
									// The header ended without a Listener line;
									// this is not a valid Local chatlog - ignore it
									parsingHeader = false;
								}
							}

							if (state.CharacterName != null)
							{
								this.ProcessEventLine(fileWriteTime, state.CharacterName, line);
							}
						}

						state.Offset = newOffset;
					}
				}
			}
			catch (IOException)
			{
				// The file may be locked by EVE right now; the next refresh will retry
			}
			catch (UnauthorizedAccessException)
			{
				// Same as above
			}
		}

		private void ProcessEventLine(DateTime fileWriteTime, string character, string line)
		{
			int markerIndex = line.IndexOf(GamelogMonitor.CHANNEL_CHANGED_MARKER, StringComparison.Ordinal);
			if (markerIndex < 0)
			{
				return;
			}

			string system = line.Substring(markerIndex + GamelogMonitor.CHANNEL_CHANGED_MARKER.Length).Trim();
			if (string.IsNullOrEmpty(system))
			{
				return;
			}

			// Only the most recently written file may update a character's system,
			// so a stale (rotated) file can never overwrite the value from a newer one
			if (this._characterSystemSources.TryGetValue(character, out long sourceTicks) && sourceTicks > fileWriteTime.Ticks)
			{
				return;
			}

			this._characterSystems[character] = system;
			this._characterSystemSources[character] = fileWriteTime.Ticks;
		}

		private void CleanupFileStates(IEnumerable<string> existingFiles)
		{
			HashSet<string> existing = new HashSet<string>(existingFiles, StringComparer.OrdinalIgnoreCase);

			foreach (string filePath in this._fileStates.Keys.ToList())
			{
				if (!existing.Contains(filePath))
				{
					this._fileStates.Remove(filePath);
				}
			}
		}
		#endregion
	}
}
