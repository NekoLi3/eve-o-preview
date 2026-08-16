using EveOPreview.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace EveOPreview.Services.Implementation
{
	/// <summary>
	/// Tracks the solar system (or station) each EVE character is currently in by
	/// parsing the shared EVE Gamelog file (per user, written to %TMP%\EVE Online\gamelog.txt).
	/// The file only grows, so parsing is incremental: only the lines appended since
	/// the last read are processed.
	/// </summary>
	sealed class GamelogMonitor : IGamelogMonitor
	{
		#region Private constants
		private const string DEFAULT_GAMELOG_SUBDIRECTORY = "EVE Online";
		private const string DEFAULT_GAMELOG_FILENAME = "gamelog.txt";
		private const int WATCHER_RETRY_PERIOD_MS = 5000;

		private const string SESSION_STARTED_PREFIX = "Session Started: ";
		private const string SESSION_ENDED_MARKER = "Session Ended";
		private const string SYSTEM_PREFIX = "System ";
		private const string STATION_PREFIX = "Station ";
		#endregion

		#region Private fields
		private readonly IThumbnailConfiguration _configuration;
		private readonly object _syncRoot;
		private readonly Dictionary<string, string> _characterSystems;

		private readonly string _gamelogPath;

		private FileSystemWatcher _watcher;
		private Timer _retryTimer;
		private long _lastPosition;
		private string _activeSessionCharacter;
		private bool _stopped;
		#endregion

		public GamelogMonitor(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;
			this._syncRoot = new object();
			this._characterSystems = new Dictionary<string, string>();

			this._gamelogPath = this.ResolveGamelogPath();

			this._lastPosition = 0;
			this._stopped = true;
		}

		private string ResolveGamelogPath()
		{
			// Explicit user override wins
			if (!string.IsNullOrEmpty(this._configuration.GamelogPath))
			{
				return this._configuration.GamelogPath;
			}

			// Modern EVE clients (launcher) write logs to Documents\EVE\logs\Gamelogs\gamelog.txt
			string documentsLogsPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
				"EVE", "logs", "Gamelogs", GamelogMonitor.DEFAULT_GAMELOG_FILENAME);

			if (File.Exists(documentsLogsPath) || Directory.Exists(Path.GetDirectoryName(documentsLogsPath)))
			{
				return documentsLogsPath;
			}

			// Legacy clients wrote the gamelog to %TMP%\EVE Online\gamelog.txt
			return Path.Combine(Path.GetTempPath(), GamelogMonitor.DEFAULT_GAMELOG_SUBDIRECTORY, GamelogMonitor.DEFAULT_GAMELOG_FILENAME);
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
				this._lastPosition = 0;
			}

			this.SetupWatcher();
		}

		public void Stop()
		{
			lock (this._syncRoot)
			{
				this._stopped = true;
			}

			this._retryTimer?.Dispose();
			this._retryTimer = null;
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
				this._retryTimer?.Dispose();
				this._retryTimer = null;
			}

			string directory;
			try
			{
				directory = Path.GetDirectoryName(this._gamelogPath);
			}
			catch (Exception)
			{
				this.ScheduleRetry();
				return;
			}

			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			{
				// The EVE Online folder may not exist yet (client never ran, or
				// temp folder was cleaned) - retry periodically instead of failing
				this.ScheduleRetry();
				return;
			}

			try
			{
				FileSystemWatcher watcher = new FileSystemWatcher(directory, GamelogMonitor.DEFAULT_GAMELOG_FILENAME);
				watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
				watcher.Changed += this.GamelogChanged_Handler;
				watcher.Created += this.GamelogChanged_Handler;
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

				this.ReadNewLines();
			}
			catch (Exception)
			{
				this.ScheduleRetry();
			}
		}

		private void ScheduleRetry()
		{
			lock (this._syncRoot)
			{
				if (this._stopped || this._retryTimer != null)
				{
					return;
				}

				this._retryTimer = new Timer((state) => this.SetupWatcher(), null, GamelogMonitor.WATCHER_RETRY_PERIOD_MS, Timeout.Infinite);
			}
		}

		private void GamelogChanged_Handler(object sender, FileSystemEventArgs e)
		{
			this.ReadNewLines();
		}

		private void ReadNewLines()
		{
			lock (this._syncRoot)
			{
				if (this._stopped)
				{
					return;
				}

				try
				{
					if (!File.Exists(this._gamelogPath))
					{
						this._lastPosition = 0;
						this.ScheduleRetry();
						return;
					}

					using (FileStream stream = new FileStream(this._gamelogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					{
						// The file was truncated or rotated since the last read
						if (stream.Length < this._lastPosition)
						{
							this._lastPosition = 0;
						}

						stream.Seek(this._lastPosition, SeekOrigin.Begin);

						using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
						{
							string content = reader.ReadToEnd();
							// ReadToEnd consumed the stream up to its end
							this._lastPosition = stream.Position;

							foreach (string line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
							{
								this.ParseLine(line);
							}
						}
					}
				}
				catch (Exception)
				{
					// The file may be locked or being written to right now;
					// the next watcher event (or retry) will pick it up
					this.ScheduleRetry();
				}
			}
		}

		private void ParseLine(string line)
		{
			// Lines look like: "2026.08.16 02:00:00  Session Started: <name>"
			// The timestamp is followed by TWO spaces before the event text
			string content = line;
			int separatorIndex = line.IndexOf("  ");
			if (separatorIndex >= 0)
			{
				content = line.Substring(separatorIndex + 2);
			}

			if (content.StartsWith(GamelogMonitor.SESSION_STARTED_PREFIX, StringComparison.Ordinal))
			{
				this._activeSessionCharacter = content.Substring(GamelogMonitor.SESSION_STARTED_PREFIX.Length);
			}
			else if (content.Equals(GamelogMonitor.SESSION_ENDED_MARKER, StringComparison.Ordinal))
			{
				this._activeSessionCharacter = null;
			}
			else if (!string.IsNullOrEmpty(this._activeSessionCharacter))
			{
				// Undock keeps the previous system; Dock is followed by a Station entry
				if (content.StartsWith(GamelogMonitor.SYSTEM_PREFIX, StringComparison.Ordinal))
				{
					this._characterSystems[this._activeSessionCharacter] = content.Substring(GamelogMonitor.SYSTEM_PREFIX.Length);
				}
				else if (content.StartsWith(GamelogMonitor.STATION_PREFIX, StringComparison.Ordinal))
				{
					this._characterSystems[this._activeSessionCharacter] = content.Substring(GamelogMonitor.STATION_PREFIX.Length);
				}
			}
		}
		#endregion
	}
}
