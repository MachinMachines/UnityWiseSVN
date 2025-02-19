using MachinMachines.VersionControl.WiseSVN.Preferences;
using UnityEditor;

namespace MachinMachines.VersionControl.WiseSVN.Branches
{
	/// <summary>
	/// Starts the database if enabled.
	/// </summary>
	[InitializeOnLoad]
	internal static class SVNBranchesDatabaseStarter
	{
		// HACK: If this was the SVNBranchesDatabase itself it causes exceptions on assembly reload.
		//		 The static constructor gets called during reload because the instance exists.
		static SVNBranchesDatabaseStarter()
		{
			var playerPrefs = SVNPreferencesManager.Instance.PersonalPrefs;
			var projectPrefs = SVNPreferencesManager.Instance.ProjectPrefs;

			// HACK: Just touch the SVNBranchesDatabase instance to initialize it.
			if (playerPrefs.EnableCoreIntegration && projectPrefs.EnableBranchesDatabase && SVNBranchesDatabase.Instance.IsActive)
				return;
		}
	}
}
