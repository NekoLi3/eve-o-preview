using EveOPreview.Configuration;
using System;
using System.Buffers;
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
		private const int REFRESH_PERIOD_MS = 500;
		private const int MAX_READ_BYTES = 256 * 1024;
		private const int READ_OVERLAP_BYTES = 4 * 1024;
		private const int HEADER_READ_BYTES = 64 * 1024;

		private const string LISTENER_PREFIX = "Listener:";
		private const string CHANNEL_CHANGED_MARKER = "EVE System > Channel changed to Local : ";
		#endregion

		#region Private fields
		private readonly IThumbnailConfiguration _configuration;
		private readonly object _syncRoot;
		private readonly Dictionary<string, string> _characterSystems;
		private readonly Dictionary<string, long> _characterSystemSources;
		private readonly Dictionary<string, string> _characterSystemFiles;
		private readonly Dictionary<string, ChatlogFileState> _fileStates;

		private readonly string _chatlogsPath;

		private FileSystemWatcher _watcher;
		private Timer _refreshTimer;
		private volatile bool _stopped;
		private readonly object _refreshGate;
		private bool _refreshActive;
		private bool _refreshPending;
		#endregion

		private sealed class ChatlogFileState
		{
			// Null when the file has no Listener header (i.e. it is not a valid Local chatlog)
			public string CharacterName;
			public long Offset;
			public long CreationTicks;
		}

		public GamelogMonitor(IThumbnailConfiguration configuration)
		{
			this._configuration = configuration;
			this._syncRoot = new object();
			this._refreshGate = new object();
			this._characterSystems = new Dictionary<string, string>();
			this._characterSystemSources = new Dictionary<string, long>();
			this._characterSystemFiles = new Dictionary<string, string>();
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

		internal void Refresh()
		{
			lock (this._refreshGate)
			{
				if (this._stopped)
				{
					return;
				}

				if (this._refreshActive)
				{
					// A watcher event arrived while a read was in progress.
					// Do not drop it: run one more pass after the current one.
					this._refreshPending = true;
					return;
				}

				this._refreshActive = true;
			}

			try
			{
				while (true)
				{
					try
					{
						this.RefreshCore();
					}
					catch (Exception exception)
					{
						// A file may be locked or the folder temporarily unavailable;
						// the periodic timer will retry
						System.Diagnostics.Debug.WriteLine(exception);
					}

					lock (this._refreshGate)
					{
						if (!this._refreshPending || this._stopped)
						{
							this._refreshActive = false;
							this._refreshPending = false;
							return;
						}

						this._refreshPending = false;
					}
				}
			}
			finally
			{
				lock (this._refreshGate)
				{
					if (this._refreshActive)
					{
						this._refreshActive = false;
						this._refreshPending = false;
					}
				}
			}
		}

		// Deterministic refresh for tests: does not depend on the watcher or the
		// periodic timer having fired.
		internal void RefreshNow()
		{
			bool wasStopped = this._stopped;
			this._stopped = false;
			try
			{
				this.RefreshCore();
			}
			finally
			{
				this._stopped = wasStopped;
			}
		}

		private void RefreshCore()
		{
			if (this._stopped || !Directory.Exists(this._chatlogsPath))
			{
				return;
			}

			FileInfo[] files = new DirectoryInfo(this._chatlogsPath)
				.GetFiles(GamelogMonitor.CHATLOG_FILE_PATTERN, SearchOption.TopDirectoryOnly)
				.OrderBy(file => file.LastWriteTimeUtc)
				.ToArray();

			// Oldest files first: the newest file for a character is processed
			// last, so its system value wins.
			foreach (FileInfo file in files)
			{
				if (this._stopped)
				{
					return;
				}

				try
				{
					this.ProcessFile(file);
				}
				catch (Exception exception)
				{
					// A chatlog can rotate or disappear between enumeration and
					// reading. Do not let that skip the remaining files.
					System.Diagnostics.Debug.WriteLine(exception);
				}
			}

			this.CleanupFileStates(files.Select(file => file.FullName));
		}

		private void ProcessFile(FileInfo file)
		{
			string filePath = file.FullName;
			if (!this._fileStates.TryGetValue(filePath, out ChatlogFileState state))
			{
				state = new ChatlogFileState();
				this._fileStates[filePath] = state;
			}

			try
			{
				using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					long length = stream.Length;
					long creationTicks = file.CreationTimeUtc.Ticks;

					if (state.CreationTicks == 0)
					{
						state.CreationTicks = creationTicks;
					}

					if (state.CreationTicks != creationTicks || length < state.Offset)
					{
						// The file was replaced or truncated under the same name - start over.
						state.Offset = 0;
						state.CharacterName = null;
						state.CreationTicks = creationTicks;
					}

					if (state.CharacterName == null)
					{
						state.CharacterName = GamelogMonitor.ReadListener(stream);
						if (state.CharacterName == null)
						{
							// The header may still be written; retry on the next refresh.
							return;
						}

						state.Offset = 0;
					}

					if (length <= state.Offset)
					{
						// Nothing was appended since the last read.
						return;
					}

					long readOffset = state.Offset;
					bool skippedPartialFirstLine = false;

					// Re-read a small overlap so a marker cannot be lost if the
					// previous read stopped between two writes.
					if (readOffset > 0)
					{
						readOffset = Math.Max(0, readOffset - GamelogMonitor.READ_OVERLAP_BYTES);
						readOffset -= readOffset & 1;
						skippedPartialFirstLine = true;
					}

					// A very large burst of writes is not useful for the current
					// system label; only the tail matters.
					if (length - readOffset > GamelogMonitor.MAX_READ_BYTES)
					{
						readOffset = length - GamelogMonitor.MAX_READ_BYTES;
						readOffset -= readOffset & 1;
						skippedPartialFirstLine = true;
					}

					int available = (int)(length - readOffset);
					byte[] buffer = ArrayPool<byte>.Shared.Rent(available);
					try
					{
						stream.Seek(readOffset, SeekOrigin.Begin);
						int read = 0;
						while (read < available)
						{
							int chunk = stream.Read(buffer, read, available - read);
							if (chunk <= 0)
							{
								break;
							}

							read += chunk;
						}

						if (read <= 0)
						{
							return;
						}

						int start = 0;
						if (readOffset == 0 && read >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
						{
							// UTF-16LE BOM. Do not count it as part of the content.
							start = 2;
						}

						// Find the last complete UTF-16LE line. A newline is 0A 00.
						int lastNewline = -1;
						for (int i = start; i + 1 < read; i += 2)
						{
							if (buffer[i] == 0x0A && buffer[i + 1] == 0x00)
							{
								lastNewline = i;
							}
						}

						if (lastNewline < 0)
						{
							// No complete line yet; keep the offset and retry later.
							return;
						}

						int completeBytes = lastNewline + 2 - start;
						string text = Encoding.Unicode.GetString(buffer, start, completeBytes);
						string[] lines = text.Split('\n');
						int firstLine = skippedPartialFirstLine ? 1 : 0;

						for (int i = firstLine; i < lines.Length; i++)
						{
							string line = lines[i].TrimEnd('\r');
							this.ProcessEventLine(filePath, file.LastWriteTimeUtc, state.CharacterName, line);
						}

						long newOffset = readOffset + start + completeBytes;
						if (newOffset > state.Offset)
						{
							state.Offset = newOffset;
						}
					}
					finally
					{
						ArrayPool<byte>.Shared.Return(buffer);
					}
				}
			}
			catch (IOException exception)
			{
				// The file may be locked by EVE right now; the next refresh will retry
				System.Diagnostics.Debug.WriteLine(exception);
			}
			catch (UnauthorizedAccessException exception)
			{
				// Same as above
				System.Diagnostics.Debug.WriteLine(exception);
			}
		}

		private static string ReadListener(FileStream stream)
		{
			long originalPosition = stream.Position;
			byte[] buffer = null;
			try
			{
				long length = stream.Length;
				if (length <= 0)
				{
					return null;
				}

				int available = (int)Math.Min(length, GamelogMonitor.HEADER_READ_BYTES);
				buffer = ArrayPool<byte>.Shared.Rent(available);
				stream.Seek(0, SeekOrigin.Begin);

				int read = 0;
				while (read < available)
				{
					int chunk = stream.Read(buffer, read, available - read);
					if (chunk <= 0)
					{
						break;
					}

					read += chunk;
				}

				if (read <= 0)
				{
					return null;
				}

				int start = (read >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE) ? 2 : 0;
				string text = Encoding.Unicode.GetString(buffer, start, read - start);

				// Do not parse a header line that EVE is still writing. A
				// truncated Listener line would otherwise poison the character
				// name for the whole session.
				int lastNewline = text.LastIndexOf('\n');
				if (lastNewline < 0)
				{
					return null;
				}

				foreach (string rawLine in text.Substring(0, lastNewline + 1).Split('\n'))
				{
					string line = rawLine.Trim();
					if (line.StartsWith(GamelogMonitor.LISTENER_PREFIX, StringComparison.Ordinal))
					{
						return line.Substring(GamelogMonitor.LISTENER_PREFIX.Length).Trim();
					}

					if (line.StartsWith("[", StringComparison.Ordinal))
					{
						// The header ended without a Listener line; this is not
						// a valid Local chatlog.
						return null;
					}
				}

				return null;
			}
			finally
			{
				if (buffer != null)
				{
					ArrayPool<byte>.Shared.Return(buffer);
				}

				stream.Seek(originalPosition, SeekOrigin.Begin);
			}
		}

		private void ProcessEventLine(string filePath, DateTime fileWriteTime, string character, string line)
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

			// A rotated, older file must not overwrite the value from a newer
			// file. The same file is always allowed to advance, even when the
			// file system reports an older or unchanged write timestamp.
			lock (this._syncRoot)
			{
				if (this._characterSystemFiles.TryGetValue(character, out string sourceFile) &&
					!string.Equals(sourceFile, filePath, StringComparison.OrdinalIgnoreCase))
				{
					// The timestamp embedded in the Local_yyyyMMdd_HHmmss name is
					// more reliable than file-system metadata, which can be cached
					// while EVE keeps the log open.
					int nameOrder = string.CompareOrdinal(Path.GetFileName(filePath), Path.GetFileName(sourceFile));
					this._characterSystemSources.TryGetValue(character, out long sourceTicks);
					if (nameOrder < 0 || (nameOrder == 0 && fileWriteTime.Ticks < sourceTicks))
					{
						return;
					}
				}

				this._characterSystems[character] = system;
				this._characterSystemSources[character] = fileWriteTime.Ticks;
				this._characterSystemFiles[character] = filePath;
			}
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
