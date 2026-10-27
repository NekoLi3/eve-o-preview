using EveOPreview.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace EveOPreview.Services.Implementation
{
	/// <summary>
	/// Watches the EVE Gamelogs and exposes short-lived event labels.
	///
	/// The watcher does no disk I/O at all: it only records changed paths. A
	/// single timer drains that set, reads the appended bytes of the affected
	/// files and applies the configured rules. Only files that are currently
	/// active are tracked, so the 1.5 GB of historical Gamelogs is never read.
	/// </summary>
	sealed class EventLogMonitor : IEventLogMonitor
	{
		#region Private constants
		private const string GAMELOGS_SUBDIRECTORY = @"EVE\logs\Gamelogs";
		private const string GAMELOG_FILE_PATTERN = "*.txt";
		private const int TIMER_PERIOD_MS = 250;
		private const int TIMER_DUE_TIME_MS = 100;
		private const int DISCOVERY_INTERVAL_MS = 30000;
		private const int RECENT_FILE_WINDOW_MINUTES = 10;
		private const int MAX_EVENT_AGE_MS = 5000;
		private const int HEADER_READ_BYTES = 4096;
		private const int MAX_DIRTY_FILES = 4096;
		private const string LISTENER_PREFIX = "Listener:";
		#endregion

		#region Private fields
		private readonly IThumbnailConfiguration _configuration;
		private readonly object _syncRoot = new object();
		private readonly Dictionary<string, GamelogFileState> _fileStates = new Dictionary<string, GamelogFileState>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _dirtyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly EventLabelStore _store = new EventLabelStore();
		private readonly string _gamelogsPath;

		private FileSystemWatcher _watcher;
		private Timer _timer;
		private volatile bool _stopped = true;
		private int _isProcessing;
		private DateTime _lastDiscoveryUtc;
		private IReadOnlyList<EventLabelRule> _rulesReference;
		private EventLogParser _parser;
		#endregion

		private sealed class GamelogFileState
		{
			public string CharacterName;
			public long Offset;
			public long HeaderLength;
			public bool HeaderChecked;
			public long CreationTicks;
		}

		public EventLogMonitor(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;
			this._gamelogsPath = string.IsNullOrEmpty(this._configuration.GamelogsPath)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), EventLogMonitor.GAMELOGS_SUBDIRECTORY)
				: this._configuration.GamelogsPath;
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

			this.EnsureWatcher();

			lock (this._syncRoot)
			{
				if (!this._stopped && this._timer == null)
				{
					this._timer = new Timer(this.TimerTick, null, EventLogMonitor.TIMER_DUE_TIME_MS, EventLogMonitor.TIMER_PERIOD_MS);
				}
			}
		}

		public void Stop()
		{
			lock (this._syncRoot)
			{
				this._stopped = true;
			}

			Timer timer = Interlocked.Exchange(ref this._timer, null);
			timer?.Dispose();

			lock (this._syncRoot)
			{
				this._watcher?.Dispose();
				this._watcher = null;
			}
		}

		public EventLabelSnapshot GetLatestEventForCharacter(string characterName)
		{
			return this._store.GetLatest(characterName, DateTime.UtcNow);
		}

		#region Private methods
		private void TimerTick(object state)
		{
			if (this._stopped)
			{
				return;
			}

			if (Interlocked.Exchange(ref this._isProcessing, 1) != 0)
			{
				return;
			}

			try
			{
				this.ProcessPending();
			}
			catch (Exception)
			{
				// Log files can be rotated at any time; the next tick retries.
			}
			finally
			{
				Interlocked.Exchange(ref this._isProcessing, 0);
			}
		}

		private void ProcessPending()
		{
			if (this._stopped)
			{
				return;
			}

			this.EnsureWatcher();
			this.UpdateParser();

			DateTime nowUtc = DateTime.UtcNow;
			if (this._lastDiscoveryUtc == default || (nowUtc - this._lastDiscoveryUtc).TotalMilliseconds >= EventLogMonitor.DISCOVERY_INTERVAL_MS)
			{
				this._lastDiscoveryUtc = nowUtc;
				this.DiscoverRecentFiles(nowUtc);
			}

			string[] dirtyFiles;
			lock (this._syncRoot)
			{
				dirtyFiles = new string[this._dirtyFiles.Count];
				this._dirtyFiles.CopyTo(dirtyFiles);
				this._dirtyFiles.Clear();
			}

			foreach (string filePath in dirtyFiles)
			{
				if (File.Exists(filePath))
				{
					this.ProcessFile(filePath, nowUtc);
				}
				else
				{
					lock (this._syncRoot)
					{
						this._fileStates.Remove(filePath);
					}
				}
			}

			this._store.RemoveExpired(nowUtc);
		}

		private void UpdateParser()
		{
			IReadOnlyList<EventLabelRule> rules = this._configuration.EventLabelRules;
			if (ReferenceEquals(rules, this._rulesReference))
			{
				return;
			}

			this._rulesReference = rules;
			this._parser = new EventLogParser(rules);
		}

		private void DiscoverRecentFiles(DateTime nowUtc)
		{
			if (!Directory.Exists(this._gamelogsPath))
			{
				return;
			}

			try
			{
				DateTime threshold = nowUtc.AddMinutes(-EventLogMonitor.RECENT_FILE_WINDOW_MINUTES);
				foreach (FileInfo file in new DirectoryInfo(this._gamelogsPath).EnumerateFiles(EventLogMonitor.GAMELOG_FILE_PATTERN, SearchOption.TopDirectoryOnly))
				{
					if (file.LastWriteTimeUtc < threshold)
					{
						continue;
					}

					this.AddDirtyFile(file.FullName);
				}
			}
			catch (Exception)
			{
				// The folder can be temporarily unavailable while EVE starts.
			}
		}

		private void ProcessFile(string filePath, DateTime nowUtc)
		{
			GamelogFileState state;
			lock (this._syncRoot)
			{
				if (!this._fileStates.TryGetValue(filePath, out state))
				{
					state = new GamelogFileState();
					this._fileStates[filePath] = state;
				}
			}

			try
			{
				using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					long length = stream.Length;
					long creationTicks = File.GetCreationTimeUtc(filePath).Ticks;
					if ((state.CreationTicks != 0 && state.CreationTicks != creationTicks) || length < state.Offset)
					{
						// The file was truncated or replaced by EVE.
						state.Offset = 0;
						state.CharacterName = null;
						state.HeaderChecked = false;
					}

					state.CreationTicks = creationTicks;

					if (state.CharacterName == null)
					{
						if (state.HeaderChecked && length <= state.HeaderLength)
						{
							return;
						}

						state.HeaderChecked = true;
						state.HeaderLength = length;
						state.CharacterName = EventLogMonitor.ReadListener(stream);
						if (state.CharacterName == null)
						{
							// Login-screen Gamelogs have no Listener header. Retry
							// only when the file grows, in case the header was still
							// being written.
							return;
						}

						state.Offset = 0;
					}

					if (!this._configuration.ShowEventLabelsInThumbnail)
					{
						// Keep the offset current so re-enabling the labels does
						// not parse a large backlog of old events.
						state.Offset = length;
						return;
					}

					EventLogParser parser = this._parser;
					if (parser == null || parser.RuleCount == 0)
					{
						state.Offset = length;
						return;
					}

					List<string> lines = LogTailReader.ReadLines(stream, ref state.Offset);
					if (lines.Count == 0)
					{
						return;
					}

					foreach (string line in lines)
					{
						if (!EventLogParser.TryParseLine(line, out DateTime eventUtc, out string message))
						{
							continue;
						}

						if (eventUtc > nowUtc)
						{
							// Clock skew between the client and the local machine.
							eventUtc = nowUtc;
						}

						if ((nowUtc - eventUtc).TotalMilliseconds > EventLogMonitor.MAX_EVENT_AGE_MS)
						{
							continue;
						}

						if (!parser.TryMatch(message, state.CharacterName, out string label, out Color color))
						{
							continue;
						}

						this._store.Record(state.CharacterName, label, color, eventUtc,
							this._configuration.EventLabelDurationMs, nowUtc);
					}
				}
			}
			catch (IOException)
			{
				// EVE may hold the file for a moment; the next tick retries.
			}
			catch (UnauthorizedAccessException)
			{
				// Same as above.
			}
			catch (Exception)
			{
				// Do not let one damaged file stop the remaining dirty files.
			}
		}

		private static string ReadListener(FileStream stream)
		{
			long originalPosition = stream.Position;
			try
			{
				int length = (int)Math.Min(stream.Length, EventLogMonitor.HEADER_READ_BYTES);
				if (length <= 0)
				{
					return null;
				}

				byte[] buffer = new byte[length];
				stream.Seek(0, SeekOrigin.Begin);
				int read = stream.Read(buffer, 0, buffer.Length);
				if (read <= 0)
				{
					return null;
				}

				string text = Encoding.UTF8.GetString(buffer, 0, read);
				using (StringReader reader = new StringReader(text))
				{
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						string trimmed = line.Trim().TrimStart('\uFEFF');
						if (trimmed.StartsWith(EventLogMonitor.LISTENER_PREFIX, StringComparison.Ordinal))
						{
							string character = trimmed.Substring(EventLogMonitor.LISTENER_PREFIX.Length).Trim();
							return character.Length > 0 ? character : null;
						}

						if (trimmed.StartsWith("[", StringComparison.Ordinal))
						{
							// The header is over without a Listener line.
							break;
						}
					}
				}

				return null;
			}
			finally
			{
				stream.Seek(originalPosition, SeekOrigin.Begin);
			}
		}

		private void EnsureWatcher()
		{
			if (this._stopped)
			{
				return;
			}

			lock (this._syncRoot)
			{
				if (this._watcher != null || this._stopped)
				{
					return;
				}
			}

			if (!Directory.Exists(this._gamelogsPath))
			{
				return;
			}

			try
			{
				FileSystemWatcher watcher = new FileSystemWatcher(this._gamelogsPath, EventLogMonitor.GAMELOG_FILE_PATTERN)
				{
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
					IncludeSubdirectories = false
				};

				watcher.Created += this.FileChanged_Handler;
				watcher.Changed += this.FileChanged_Handler;
				watcher.Deleted += this.FileChanged_Handler;
				watcher.Renamed += this.FileRenamed_Handler;
				watcher.Error += this.WatcherError_Handler;
				watcher.EnableRaisingEvents = true;

				lock (this._syncRoot)
				{
					if (this._stopped)
					{
						watcher.Dispose();
						return;
					}

					this._watcher?.Dispose();
					this._watcher = watcher;
				}

				// Do not wait for the first timer tick after a restart.
				this._lastDiscoveryUtc = default;
			}
			catch (Exception)
			{
				// The folder may be temporarily unavailable; the timer retries.
			}
		}

		private void FileChanged_Handler(object sender, FileSystemEventArgs e)
		{
			this.AddDirtyFile(e.FullPath);
		}

		private void FileRenamed_Handler(object sender, RenamedEventArgs e)
		{
			this.AddDirtyFile(e.OldFullPath);
			this.AddDirtyFile(e.FullPath);
		}

		private void WatcherError_Handler(object sender, ErrorEventArgs e)
		{
			lock (this._syncRoot)
			{
				this._watcher?.Dispose();
				this._watcher = null;
			}
		}

		private void AddDirtyFile(string filePath)
		{
			if (this._stopped || string.IsNullOrEmpty(filePath))
			{
				return;
			}

			lock (this._syncRoot)
			{
				if (this._dirtyFiles.Count < EventLogMonitor.MAX_DIRTY_FILES)
				{
					this._dirtyFiles.Add(filePath);
				}
			}
		}
		#endregion
	}
}
