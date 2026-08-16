using System.Collections.Generic;

namespace EveOPreview.Services
{
	public interface IProfileManager
	{
		/// <summary>Returns the names of the existing layout profiles, sorted.</summary>
		IList<string> GetProfileNames();

		/// <summary>Stores the current configuration as a profile with the given name (overwrites if it exists).</summary>
		void SaveProfile(string name);

		/// <summary>Applies the given profile to the live configuration.</summary>
		void LoadProfile(string name);

		/// <summary>Deletes the given profile.</summary>
		void DeleteProfile(string name);

		/// <summary>Checks that a profile name is safe to use as a file name.</summary>
		bool IsProfileNameValid(string name);
	}
}
