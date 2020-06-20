using DevLocker.VersionControl.WiseSVN.ContextMenus;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.VersionControl.WiseSVN.Preferences
{
	internal class WiseSVNProjectPreferencesWindow : EditorWindow
	{
		private enum PreferencesTab
		{
			Personal = 0,
			Project = 1,
			About = 2,
		}

		public const string PROJECT_PREFERENCES_MENU = "Assets/SVN/SVN Preferences";
		[MenuItem(PROJECT_PREFERENCES_MENU, false, 200)]
		public static void ShowProjectPreferences()
		{
			var window = GetWindow<WiseSVNProjectPreferencesWindow>(true, "Wise SVN Preferences");
			window.m_PersonalPrefs = SVNPreferencesManager.Instance.PersonalPrefs.Clone();
			window.m_ProjectPrefs = SVNPreferencesManager.Instance.ProjectPrefs.Clone();
			window.ShowUtility();
			window.position = new Rect(500f, 250f, 520f, 400f);
			window.minSize = new Vector2(520f, 400f);
		}

		// So SerializedObject() can work with it.
		[SerializeField] private SVNPreferencesManager.PersonalPreferences m_PersonalPrefs;
		[SerializeField] private SVNPreferencesManager.ProjectPreferences m_ProjectPrefs;

		private PreferencesTab m_SelectedTab = PreferencesTab.Personal;
		private static readonly string[] m_PreferencesTabsNames = Enum.GetNames(typeof(PreferencesTab));

		private Vector2 m_ProjectPreferencesScroll;
		private const string m_DownloadRepositoryChangesHint = "Work online - will ask the repository if there are any changes on the server.\nEnabling this will show locks and out of date additional icons.\nRefreshes might be slower due to the network communication, but shouldn't slow down your editor.";

		private bool m_FoldBranchesDatabaseHint = true;

		private void OnGUI()
		{
			const float labelWidthAdd = 40;
			EditorGUIUtility.labelWidth += labelWidthAdd;

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.LabelField("Save changes:", EditorStyles.boldLabel);

				GUILayout.FlexibleSpace();

				if (GUILayout.Button("Close", GUILayout.MaxWidth(60f))) {
					GUI.FocusControl("");
					Close();
					EditorGUIUtility.ExitGUI();
				}

				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = Color.green / 1.2f;
				if (GUILayout.Button("Save All", GUILayout.MaxWidth(150f))) {

					SanitizeBeforeSave();
					SVNPreferencesManager.Instance.SavePreferences(m_PersonalPrefs, m_ProjectPrefs);

					// When turning on the integration do instant refresh.
					// Works when editor started with disabled integration. Doing it here to avoid circle dependency.
					if (m_PersonalPrefs.EnableCoreIntegration) {
						SVNStatusesDatabase.Instance.InvalidateDatabase();
						SVNBranchesDatabase.Instance.InvalidateDatabase();
					}
				}
				GUI.backgroundColor = prevColor;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			m_SelectedTab = (PreferencesTab)GUILayout.Toolbar((int)m_SelectedTab, m_PreferencesTabsNames);

			m_ProjectPreferencesScroll = EditorGUILayout.BeginScrollView(m_ProjectPreferencesScroll);

			switch (m_SelectedTab) {
				case PreferencesTab.Personal:
					DrawPersonalPreferences();
					break;
				case PreferencesTab.Project:
					DrawProjectPreferences();
					break;
				case PreferencesTab.About:
					DrawAbout();
					break;
			}

			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space();


			EditorGUIUtility.labelWidth -= labelWidthAdd;
		}

		private void SanitizeBeforeSave()
		{
			m_ProjectPrefs.SvnCLIPath = m_ProjectPrefs.SvnCLIPath.Trim();
			m_ProjectPrefs.SvnCLIPathMacOS = m_ProjectPrefs.SvnCLIPathMacOS.Trim();
			m_ProjectPrefs.Exclude.RemoveAll(p => string.IsNullOrWhiteSpace(p));

			if (m_ProjectPrefs.EnableBranchesDatabase) {

				if (m_ProjectPrefs.BranchesDatabaseScanParameters.Count == 0) {
					EditorUtility.DisplayDialog("Branches Database", "In order to use Branches Database, you must provide at least one scan parameters element.\n\nBranches Database will be disabled.", "Ok");
					m_ProjectPrefs.EnableBranchesDatabase = false;
				}

				m_ProjectPrefs.BranchesDatabaseScanParameters = m_ProjectPrefs.BranchesDatabaseScanParameters
					.Select(sp => sp.Sanitized())
					.ToList();

				if (m_ProjectPrefs.BranchesDatabaseScanParameters.Any(sp => !sp.IsValid)) {
					EditorUtility.DisplayDialog("Branches Database", "Some of the branches scan parameters have invalid data. Please fix it.\n\nBranches Database will be disabled.", "Ok");
					m_ProjectPrefs.EnableBranchesDatabase = false;
				}
			}
		}

		private void DrawPersonalPreferences()
		{
			EditorGUILayout.HelpBox("These are personal preferences stored in the registry.\nHint: check the the tooltips.", MessageType.Info);

			m_PersonalPrefs.EnableCoreIntegration = EditorGUILayout.Toggle("Enable SVN integration", m_PersonalPrefs.EnableCoreIntegration);

			EditorGUI.BeginDisabledGroup(!m_PersonalPrefs.EnableCoreIntegration);

			m_PersonalPrefs.PopulateStatusesDatabase = EditorGUILayout.Toggle(new GUIContent("Enable overlay icons", "Enables overlay icons in the project windows.\nPopulates internal cache with statuses of changed entities.\nFile changes may trigger repopulation of the cache."), m_PersonalPrefs.PopulateStatusesDatabase);
			EditorGUI.BeginDisabledGroup(!m_PersonalPrefs.PopulateStatusesDatabase);

			m_PersonalPrefs.AutoRefreshDatabaseInterval = EditorGUILayout.IntField(new GUIContent("Overlay icons refresh interval", "How much seconds to wait for the next overlay icons refresh.\nNOTE: -1 will deactivate it - only file changes will trigger refresh."), m_PersonalPrefs.AutoRefreshDatabaseInterval);

			m_PersonalPrefs.DownloadRepositoryChanges =
				(SVNPreferencesManager.BoolPreference)EditorGUILayout.EnumPopup(
					new GUIContent("Check for repository changes", m_DownloadRepositoryChangesHint + "\n\nNOTE: this will override the project setting. Coordinate this with your team.")
					, m_PersonalPrefs.DownloadRepositoryChanges);

			EditorGUI.EndDisabledGroup();

			m_PersonalPrefs.ContextMenusClient = (ContextMenusClient)EditorGUILayout.EnumPopup(new GUIContent("Context menus client", "Select what client should be used with the context menus."), m_PersonalPrefs.ContextMenusClient);
			if (GUI.changed) {
				var errorMsg = SVNContextMenusManager.IsCurrentlySupported(m_PersonalPrefs.ContextMenusClient);
				if (!string.IsNullOrEmpty(errorMsg)) {
					EditorUtility.DisplayDialog("Context Menus Client Issue", errorMsg, "Ok");
				}
			}

			m_PersonalPrefs.TraceLogs = (SVNTraceLogs)EditorGUILayout.EnumFlagsField(new GUIContent("Trace logs", "Logs for nerds and debugging."), m_PersonalPrefs.TraceLogs);

			EditorGUI.EndDisabledGroup();
		}

		private void DrawProjectPreferences()
		{
			EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder.\nFeel free to add them to your version control system.\nCoordinate any changes here with your team.", MessageType.Warning);

			var so = new SerializedObject(this);
			var sp = so.FindProperty("m_ProjectPrefs");

			m_ProjectPrefs.DownloadRepositoryChanges = EditorGUILayout.Toggle(new GUIContent("Check for repository changes", m_DownloadRepositoryChangesHint), m_ProjectPrefs.DownloadRepositoryChanges);

			m_ProjectPrefs.SvnCLIPath = EditorGUILayout.TextField(new GUIContent("SVN CLI Path", "If you desire to use specific SVN CLI (svn.exe) located in the project, write down its path relative to the root folder."), m_ProjectPrefs.SvnCLIPath);
			m_ProjectPrefs.SvnCLIPathMacOS = EditorGUILayout.TextField(new GUIContent("SVN CLI Path MacOS", "Same as above, but for MacOS."), m_ProjectPrefs.SvnCLIPathMacOS);

			m_ProjectPrefs.EnableBranchesDatabase = EditorGUILayout.Toggle(new GUIContent("Enable Branches Database", "Scans the SVN repository for Unity projects in branches and keeps them in a simple database."), m_ProjectPrefs.EnableBranchesDatabase);
			if (m_ProjectPrefs.EnableBranchesDatabase) {

				EditorGUI.indentLevel++;
				m_FoldBranchesDatabaseHint = EditorGUILayout.Foldout(m_FoldBranchesDatabaseHint, "Branches Database Hint:");
				var branchesHint = "Provide at least one branches scan parameter below.\n\n" +
				                   "\"Entry Point URL\" serves as a starting location where scan will commence.\n\n" +
				                   "\"Branch Signature Root Entries\" should contain names of files or folders that mark the beginning of a branch. This will be shown as a branch's name.\n\n" +
				                   "Example setup:\n" +
				                   "  /branches/VariantA/Server\n" +
				                   "  /branches/VariantA/UnityClient\n" +
				                   "  /branches/VariantB/Server\n" +
				                   "  /branches/VariantB/UnityClient\n\n" +
				                   "Set \"Entry Point URL\" to \"https://companyname.com/branches\" as a starting point.\n" +
				                   "\"VariantA\" and \"VariantB\" should be considered as branch roots.\n" +
				                   "UnityClient folder is not a branch root.\n" +
				                   "Set \"Branch Signature Root Entries\" to { \"Server\", \"UnityClient\" } to match the roots ...\n\n" +
				                   "Only one Unity project per branch is supported!" +
				                   ""
					;
				if (m_FoldBranchesDatabaseHint) {
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space((EditorGUI.indentLevel + 1) * 16f);
					GUILayout.Label(branchesHint, EditorStyles.helpBox);
					EditorGUILayout.EndHorizontal();
				}

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("BranchesDatabaseScanParameters"), new GUIContent("Branches Scan Parameters", "Must have at least one entry to work properly."), true);
				EditorGUI.indentLevel--;
			}

			EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), new GUIContent("Exclude Paths", "Asset paths that will be ignored by the SVN integrations. Use with caution."), true);

			so.ApplyModifiedProperties();
		}

		public static void DrawAbout()
		{
			EditorGUILayout.LabelField("About:", EditorStyles.boldLabel);
			{
				var urlStyle = new GUIStyle(EditorStyles.label);
				urlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
				urlStyle.active.textColor = Color.red;

				const string mail = "NibbleByte3@gmail.com";

				GUILayout.Label("Created by Filip Slavov", GUILayout.ExpandWidth(false));
				if (GUILayout.Button(mail, urlStyle, GUILayout.ExpandWidth(false))) {
					Application.OpenURL("mailto:"+mail);
				}

				EditorGUILayout.LabelField("In collaboration with Snapshot Games");

				if (GUILayout.Button("Icons taken from TortoiseSVN (created by Lьbbe Onken)", urlStyle, GUILayout.ExpandWidth(true))) {
					var assetStoreURL = "https://tortoisesvn.net/";
					Application.OpenURL(assetStoreURL);
				}


				EditorGUILayout.BeginHorizontal();

				if (GUILayout.Button("Plugin at Asset Store", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					var assetStoreURL = "https://assetstore.unity.com/packages/tools/version-control/wise-svn-162636";
					Application.OpenURL(assetStoreURL);
				}

				if (GUILayout.Button("Source at GitHub", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					var githubURL = "https://github.com/NibbleByte/UnityWiseSVN";
					Application.OpenURL(githubURL);
				}

				EditorGUILayout.EndHorizontal();

				EditorGUILayout.BeginHorizontal();

				if (GUILayout.Button("Reddit", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					var redditURL = "https://www.reddit.com/r/Unity3D/comments/fgjovk/finally_a_fully_working_tortoisesvn_snailsvn";
					Application.OpenURL(redditURL);
				}

				if (GUILayout.Button("Unity Forum", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
					var unityForumURL = "https://forum.unity.com/threads/wise-svn-powerful-tortoisesvn-snailsvn-integration.844168";
					Application.OpenURL(unityForumURL);
				}

				EditorGUILayout.EndHorizontal();
			}
		}
	}
}
