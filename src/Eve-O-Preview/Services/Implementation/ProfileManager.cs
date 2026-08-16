using EveOPreview.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EveOPreview.Services.Implementation
{
	/// <summary>
	/// Stores/loads complete layout+settings snapshots ("profiles") as JSON files in
	/// the "profiles" folder next to the main configuration file. The snapshot is
	/// serialized exactly like the main config (same Newtonsoft path), which makes
	/// loading a profile equivalent to populating the live configuration object.
	/// </summary>
	sealed class ProfileManager : IProfileManager
	{
		#region Private constants
		private const string PROFILES_DIRECTORY_NAME = "profiles";
		private const string PROFILE_FILE_EXTENSION = ".json";

		private static readonly string[] RESERVED_WINDOWS_NAMES = new string[]
		{
			"CON", "PRN", "AUX", "NUL",
			"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
			"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
		};
		#endregion

		#region Private fields
		private readonly IAppConfig _appConfig;
		private readonly IThumbnailConfiguration _thumbnailConfiguration;
		#endregion

		public ProfileManager(IAppConfig appConfig, IThumbnailConfiguration thumbnailConfiguration)
		{
			this._appConfig = appConfig;
			this._thumbnailConfiguration = thumbnailConfiguration;
		}

		public IList<string> GetProfileNames()
		{
			string directory = this.GetProfilesDirectory();
			if (!Directory.Exists(directory))
			{
				return new List<string>();
			}

			return Directory.GetFiles(directory, "*" + ProfileManager.PROFILE_FILE_EXTENSION)
				.Select(Path.GetFileNameWithoutExtension)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		public void SaveProfile(string name)
		{
			if (!this.IsProfileNameValid(name))
			{
				return;
			}

			try
			{
				string directory = this.GetProfilesDirectory();
				Directory.CreateDirectory(directory);

				string rawData = JsonConvert.SerializeObject(this._thumbnailConfiguration, Formatting.Indented);
				File.WriteAllText(this.GetProfileFilePath(name), rawData);
			}
			catch (IOException)
			{
				// The profile could not be written (locked file, invalid name for the OS, ...)
			}
		}

		public void LoadProfile(string name)
		{
			if (!this.IsProfileNameValid(name))
			{
				return;
			}

			try
			{
				string filePath = this.GetProfileFilePath(name);
				if (!File.Exists(filePath))
				{
					return;
				}

				string rawData = File.ReadAllText(filePath);

				JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
				{
					ObjectCreationHandling = ObjectCreationHandling.Replace
				};

				// Same population path as the main configuration storage, so the live
				// config object is fully replaced by the profile contents
				JsonConvert.PopulateObject(rawData, this._thumbnailConfiguration, jsonSerializerSettings);

				this._thumbnailConfiguration.ApplyRestrictions();
			}
			catch (IOException)
			{
				// The profile could not be read; keep the current configuration untouched
			}
		}

		public void DeleteProfile(string name)
		{
			if (!this.IsProfileNameValid(name))
			{
				return;
			}

			try
			{
				string filePath = this.GetProfileFilePath(name);
				if (File.Exists(filePath))
				{
					File.Delete(filePath);
				}
			}
			catch (IOException)
			{
				// The profile could not be deleted; the list refresh will keep showing it
			}
		}

		public bool IsProfileNameValid(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}

			// Reject any path separators / traversal and Windows-invalid characters
			if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				return false;
			}

			// The name must survive a full path round-trip unchanged
			if (Path.GetFileName(name) != name || Path.GetFileNameWithoutExtension(name) != name)
			{
				return false;
			}

			// Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
			// cannot be used as file names even with an extension
			string rootName = name.Split('.')[0];
			if (Array.IndexOf(ProfileManager.RESERVED_WINDOWS_NAMES, rootName.ToUpperInvariant()) >= 0)
			{
				return false;
			}

			return true;
		}

		#region Private methods
		private string GetProfilesDirectory()
		{
			string configPath = string.IsNullOrEmpty(this._appConfig.ConfigFileName)
				? Path.Combine(AppContext.BaseDirectory, "EVE-O-Preview.json")
				: Path.GetFullPath(this._appConfig.ConfigFileName);

			return Path.Combine(Path.GetDirectoryName(configPath), ProfileManager.PROFILES_DIRECTORY_NAME);
		}

		private string GetProfileFilePath(string name)
		{
			return Path.Combine(this.GetProfilesDirectory(), name + ProfileManager.PROFILE_FILE_EXTENSION);
		}
		#endregion
	}
}
