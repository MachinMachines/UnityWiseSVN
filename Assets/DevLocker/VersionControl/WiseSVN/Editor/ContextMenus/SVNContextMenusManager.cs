using MachinMachines.VersionControl.WiseSVN.ContextMenus.Implementation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace MachinMachines.VersionControl.WiseSVN.ContextMenus
{
	public enum ContextMenusClient
	{
		None,
		TortoiseSVN,	// Good for Windows
		SnailSVN,		// Good for MacOS
	}

	/// <summary>
	/// This class is responsible for the "Assets/SVN/..." context menus that pop up SVN client windows.
	/// You can do this from your code as well. For the most methods you have to provide list of asset paths,
	/// should the method add meta files as well and should it wait for the SVN client window to close.
	/// *** It is recommended to wait for update operations to finish! Check the Update method for more info. ***
	/// </summary>
	public static class SVNContextMenusManager
	{
		public const int MenuItemPriorityStart = -2000;

		private static SVNContextMenusBase m_Integration;

		internal static void SetupContextType(ContextMenusClient client)
		{
			string errorMsg;
			m_Integration = TryCreateContextMenusIntegration(client, out errorMsg);

			if (!string.IsNullOrEmpty(errorMsg)) {
				UnityEngine.Debug.LogError($"WiseSVN: Unsupported context menus client: {client}. Reason: {errorMsg}");
			}

			WiseSVNIntegration.ShowChangesUI -= CheckChangesAll;
			WiseSVNIntegration.ShowChangesUI += CheckChangesAll;

			WiseSVNIntegration.RunUpdateUI -= UpdateAll;
			WiseSVNIntegration.RunUpdateUI += UpdateAll;
		}

		private static SVNContextMenusBase TryCreateContextMenusIntegration(ContextMenusClient client, out string errorMsg)
		{
			if (client == ContextMenusClient.None) {
				errorMsg = string.Empty;
				return null;
			}

#if UNITY_EDITOR_WIN
			switch (client) {

				case ContextMenusClient.TortoiseSVN:
					errorMsg = string.Empty;
					return new TortoiseSVNContextMenus();

				case ContextMenusClient.SnailSVN:
					errorMsg = "SnailSVN is not supported on windows.";
					return null;

				default:
					throw new NotImplementedException(client + " not implemented yet for this platform.");
			}
#else
			switch (client)
			{

				case ContextMenusClient.TortoiseSVN:
					errorMsg = "TortoiseSVN is not supported on MacOS";
					return null;

				case ContextMenusClient.SnailSVN:
					errorMsg = string.Empty;
					return new SnailSVNContextMenus();

				default:
					throw new NotImplementedException(client + " not implemented yet for this platform.");
			}
#endif
		}

		public static string IsCurrentlySupported(ContextMenusClient client)
		{
			string errorMsg = null;

			TryCreateContextMenusIntegration(client, out errorMsg);

			return errorMsg;
		}

		private static IEnumerable<string> GetRootAssetPath()
		{
			yield return ".";	// The root folder of the project (not the Assets folder).
		}

		private static IEnumerable<string> GetSelectedAssetPaths()
		{
			string[] guids = Selection.assetGUIDs;
			for (int i = 0; i < guids.Length; ++i) {
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);

				if (string.IsNullOrEmpty(path))
					continue;

				// All direct folders in packages (the package folder) are returned with ToLower() by Unity.
				// If you have a custom package in development and your folder has upper case letters, they need to be restored.
				if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) {
					path = Path.GetFullPath(path)
						.Replace("\\", "/")
						.Replace(WiseSVNIntegration.ProjectRootUnity + "/", "")
						;

					// If this is a normal package (not a custom one in development), returned path points to the "Library" folder.
					if (!path.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
						continue;
				}

				yield return path;
			}
		}

		[MenuItem("Assets/SVN/Diff \u2044 Resolve", true, MenuItemPriorityStart)]
		public static bool DiffResolveValidate()
		{
			// Might be cool to return false if SVN status is normal or unversioned, but that might slow down the context menu.
			return Selection.assetGUIDs.Length == 1;
		}

		[MenuItem("Assets/SVN/Diff \u2044 Resolve", false, MenuItemPriorityStart)]
		public static void DiffResolve()
		{
			CheckChangesSelected();
		}

		[MenuItem("Assets/SVN/Check Changes All", false, MenuItemPriorityStart + 5)]
		public static void CheckChangesAll()
		{
			// TortoiseSVN handles nested repositories gracefully. SnailSVN - not so much. :(
			m_Integration?.CheckChanges(GetRootAssetPath().Concat(SVNStatusesDatabase.Instance.NestedRepositories), false);
		}

		[MenuItem("Assets/SVN/Check Changes", false, MenuItemPriorityStart + 5)]
		public static void CheckChangesSelected()
		{
			if (Selection.assetGUIDs.Length > 1) {
				m_Integration?.CheckChanges(GetSelectedAssetPaths(), true);

			} else if (Selection.assetGUIDs.Length == 1) {
				var assetPath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);

				var isFolder = System.IO.Directory.Exists(assetPath);

				if (isFolder || (!DiffAsset(assetPath) && !DiffAsset(assetPath + ".meta"))) {
					m_Integration?.CheckChanges(GetSelectedAssetPaths(), true);
				}
			}
		}

		private static bool DiffAsset(string assetPath)
		{
			var statusData = WiseSVNIntegration.GetStatus(assetPath);

			var isModified = statusData.Status != VCFileStatus.Normal
				&& statusData.Status != VCFileStatus.Unversioned
				&& statusData.Status != VCFileStatus.Conflicted
				;
			isModified |= statusData.PropertiesStatus == VCPropertiesStatus.Modified;

			if (isModified) {
				m_Integration?.DiffChanges(assetPath, false);
				return true;
			}

			if (statusData.Status == VCFileStatus.Conflicted || statusData.PropertiesStatus == VCPropertiesStatus.Conflicted) {
				m_Integration?.Resolve(assetPath, false);
				return true;
			}

			return false;
		}

		public static void CheckChanges(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.CheckChanges(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Update All", false, MenuItemPriorityStart + 20)]
		public static void UpdateAll()
		{
			// It is recommended to freeze Unity while updating.
			// If SVN downloads files while Unity is crunching assets, GUID database may get corrupted.
			// TortoiseSVN handles nested repositories gracefully and updates them one after another. SnailSVN - not so much. :(
			m_Integration?.Update(GetRootAssetPath().Concat(SVNStatusesDatabase.Instance.NestedRepositories), false, wait: true);
		}

		// It is recommended to freeze Unity while updating.
		// DANGER: SVN updating while editor is crunching assets IS DANGEROUS! It WILL corrupt your asset guids. Use with caution!!!
		//		   This is the reason why this method freezes your editor and waits for the update to finish.
		public static void Update(IEnumerable<string> assetPaths, bool includeMeta)
		{
			m_Integration?.Update(assetPaths, includeMeta, wait: true);
		}

		// It is recommended to freeze Unity while updating.
		// DANGER: SVN updating while editor is crunching assets IS DANGEROUS! It WILL corrupt your asset guids. Use with caution!!!
		public static void UpdateAndDontWaitDANGER(IEnumerable<string> assetPaths, bool includeMeta)
		{
			m_Integration?.Update(assetPaths, includeMeta, wait: false);
		}



		[MenuItem("Assets/SVN/Commit All", false, MenuItemPriorityStart + 40)]
		public static void CommitAll()
		{
			// TortoiseSVN handles nested repositories gracefully. SnailSVN - not so much. :(
			m_Integration?.Commit(GetRootAssetPath().Concat(SVNStatusesDatabase.Instance.NestedRepositories), false);
		}

		[MenuItem("Assets/SVN/Commit", false, MenuItemPriorityStart + 40)]
		public static void CommitSelected()
		{
			var paths = GetSelectedAssetPaths().ToList();
			if (paths.Count == 1) {

				if (paths[0] == "Assets") {
					// Special case for the "Assets" folder as it doesn't have a meta file and that kind of breaks the TortoiseSVN.
					CommitAll();
					return;
				}

				// TortoiseSVN shows "(multiple targets selected)" for commit path when more than one was specified.
				// Don't specify the .meta unless really needed to.
				var statusData = WiseSVNIntegration.GetStatus(paths[0] + ".meta");
				if (statusData.Status == VCFileStatus.Normal && !statusData.IsConflicted) {
					m_Integration?.Commit(paths, false);
					return;
				}
			}

			m_Integration?.Commit(paths, true);
		}

		public static void Commit(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.Commit(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Add", false, MenuItemPriorityStart + 40)]
		public static void AddSelected()
		{
			m_Integration?.Add(GetSelectedAssetPaths(), true);
		}

		public static void Add(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.Add(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Revert All", false, MenuItemPriorityStart + 60)]
		public static void RevertAll()
		{
			// TortoiseSVN handles nested repositories gracefully. SnailSVN - not so much. :(
			m_Integration?.Revert(GetRootAssetPath().Concat(SVNStatusesDatabase.Instance.NestedRepositories), false, true);
		}

		[MenuItem("Assets/SVN/Revert", false, MenuItemPriorityStart + 60)]
		public static void RevertSelected()
		{
			var paths = GetSelectedAssetPaths().ToList();
			if (paths.Count == 1) {

				if (paths[0] == "Assets") {
					// Special case for the "Assets" folder as it doesn't have a meta file and that kind of breaks the TortoiseSVN.
					RevertAll();
					return;
				}

				// TortoiseSVN shows the meta file for revert even if it has no changes.
				// Don't specify the .meta unless really needed to.
				var statusData = WiseSVNIntegration.GetStatus(paths[0] + ".meta");
				if (statusData.Status == VCFileStatus.Normal && !statusData.IsConflicted) {
					m_Integration?.Revert(paths, false);
					return;
				}
			}

			m_Integration?.Revert(GetSelectedAssetPaths(), true, true);
		}

		public static void Revert(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.Revert(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Resolve All", false, MenuItemPriorityStart + 60)]
		private static void ResolveAllMenu()
		{
			m_Integration?.ResolveAll(false);
		}

		public static void ResolveAll(bool wait = false)
		{
			m_Integration?.ResolveAll(wait);
		}




		private static bool TryShowLockDialog(List<string> selectedPaths, Action<IEnumerable<string>, bool, bool> operationHandler, bool onlyLocked)
		{
			if (selectedPaths.Count == 0)
				return true;

			if (selectedPaths.All(p => Directory.Exists(p))) {
				operationHandler(selectedPaths, false, false);
				return true;
			}

			bool hasModifiedPaths = false;
			var modifiedPaths = new List<string>();
			foreach (var path in selectedPaths) {
				var guid = AssetDatabase.AssetPathToGUID(path);

				var countPrev = modifiedPaths.Count;
				modifiedPaths.AddRange(SVNStatusesDatabase.Instance
					.GetAllKnownStatusData(guid, false, true, true)
					.Where(sd => sd.Status != VCFileStatus.Unversioned)
					.Where(sd => sd.Status != VCFileStatus.Normal || sd.LockStatus != VCLockStatus.NoLock)
					.Where(sd => !onlyLocked || (sd.LockStatus != VCLockStatus.NoLock && sd.LockStatus != VCLockStatus.LockedOther))
					.Select(sd => sd.Path)
					);


				// No change in asset or meta -> just add the asset as it was selected by the user anyway.
				if (modifiedPaths.Count == countPrev) {
					if (!onlyLocked || Directory.Exists(path)) {
						modifiedPaths.Add(path);
					}
				} else {
					hasModifiedPaths = true;
				}
			}

			if (hasModifiedPaths) {
				operationHandler(modifiedPaths, false, false);
				return true;
			}

			return false;
		}

		[MenuItem("Assets/SVN/Get Locks", false, MenuItemPriorityStart + 80)]
		public static void GetLocksSelected()
		{
			if (m_Integration != null) {
				if (!TryShowLockDialog(GetSelectedAssetPaths().ToList(), m_Integration.GetLocks, false)) {

					// This will include the meta which is rarely what you want.
					m_Integration.GetLocks(GetSelectedAssetPaths(), true, false);
				}
			}
		}

		public static void GetLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.GetLocks(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Release Locks", false, MenuItemPriorityStart + 80)]
		public static void ReleaseLocksSelected()
		{
			if (m_Integration != null) {
				if (!TryShowLockDialog(GetSelectedAssetPaths().ToList(), m_Integration.ReleaseLocks, true)) {
					// No locked assets, show nothing.
				}
			}
		}

		public static void ReleaseLocks(IEnumerable<string> assetPaths, bool includeMeta, bool wait = false)
		{
			m_Integration?.ReleaseLocks(assetPaths, includeMeta, wait);
		}



		[MenuItem("Assets/SVN/Show Log All", false, MenuItemPriorityStart + 100)]
		public static void ShowLogAll()
		{
			m_Integration?.ShowLog(GetRootAssetPath().First());
		}

		[MenuItem("Assets/SVN/Show Log", false, MenuItemPriorityStart + 100)]
		public static void ShowLogSelected()
		{
			m_Integration?.ShowLog(GetSelectedAssetPaths().FirstOrDefault());
		}

		public static void ShowLog(string assetPath, bool wait = false)
		{
			m_Integration?.ShowLog(assetPath, wait);
		}



		[MenuItem("Assets/SVN/Blame", false, MenuItemPriorityStart + 100)]
		public static void BlameSelected()
		{
			m_Integration?.Blame(GetSelectedAssetPaths().FirstOrDefault());
		}

		public static void Blame(string assetPath, bool wait = false)
		{
			m_Integration?.Blame(assetPath, wait);
		}



		[MenuItem("Assets/SVN/Cleanup", false, MenuItemPriorityStart + 100)]
		public static void Cleanup()
		{
			m_Integration?.Cleanup(true);
		}

		// It is recommended to freeze Unity while Cleanup is working.
		public static void CleanupAndDontWait()
		{
			m_Integration?.Cleanup(false);
		}

		/// <summary>
		/// Open Repo-Browser at url location. You can get url from local working copy by using WiseSVNIntegration.AssetPathToURL(path);
		/// </summary>
		public static void RepoBrowser(string url, bool wait = false)
		{
			m_Integration?.RepoBrowser(url, wait);
		}

		/// <summary>
		/// Open Switch dialog. localPath specifies the target directory and url the URL to switch to.
		/// Most likely you want the root of the working copy (checkout), not just the Unity project. To get it use WiseSVNIntegration.WorkingCopyRootPath();
		/// </summary>
		public static void Switch(string localPath, string url, bool wait = false)
		{
			m_Integration?.Switch(localPath, url, wait);
		}
	}
}
