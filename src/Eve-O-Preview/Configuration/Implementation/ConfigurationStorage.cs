using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace EveOPreview.Configuration.Implementation
{
	class ConfigurationStorage : IConfigurationStorage
	{
		private const string CONFIGURATION_FILE_NAME = "EVE-O-Preview.json";

		private readonly IAppConfig _appConfig;
		private readonly IThumbnailConfiguration _thumbnailConfiguration;
		private readonly object _saveLock = new object();

		public ConfigurationStorage(IAppConfig appConfig, IThumbnailConfiguration thumbnailConfiguration)
		{
			this._appConfig = appConfig;
			this._thumbnailConfiguration = thumbnailConfiguration;
		}

		public void Load()
		{
			string filename = this.GetConfigFileName();

			if (!File.Exists(filename))
			{
				return;
			}

			try
			{
				string rawData = File.ReadAllText(filename);

				JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
				{
					ObjectCreationHandling = ObjectCreationHandling.Replace
				};

				// StageHotkeyArraysToAvoidDuplicates(rawData);

				JsonConvert.PopulateObject(rawData, this._thumbnailConfiguration, jsonSerializerSettings);

				// Validate data after loading it
				this._thumbnailConfiguration.ApplyRestrictions();
			}
			catch (Exception exception) when (exception is IOException
				|| exception is UnauthorizedAccessException
				|| exception is JsonException)
			{
				// Keep the built-in defaults when the configuration cannot be read
				// or is malformed. The application must still start.
				Debug.WriteLine("Unable to load the configuration: " + exception.Message);
			}
		}

		public void Save()
		{
			lock (this._saveLock)
			{
				string filename = this.GetConfigFileName();
				string temporaryFilename = filename + ".tmp";

				try
				{
					string rawData = JsonConvert.SerializeObject(this._thumbnailConfiguration, Formatting.Indented);
					File.WriteAllText(temporaryFilename, rawData);
					File.Move(temporaryFilename, filename, true);
				}
				catch (Exception exception) when (exception is IOException
					|| exception is UnauthorizedAccessException
					|| exception is JsonException
					|| exception is NotSupportedException)
				{
					// Ignore error if for some reason the updated config cannot be written down
					Debug.WriteLine("Unable to save the configuration: " + exception.Message);
					this.TryDeleteFile(temporaryFilename);
				}
			}
		}

		private void TryDeleteFile(string filename)
		{
			try
			{
				if (File.Exists(filename))
				{
					File.Delete(filename);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}

		private string GetConfigFileName()
		{
			return string.IsNullOrEmpty(this._appConfig.ConfigFileName) ? ConfigurationStorage.CONFIGURATION_FILE_NAME : this._appConfig.ConfigFileName;
		}
	}
}