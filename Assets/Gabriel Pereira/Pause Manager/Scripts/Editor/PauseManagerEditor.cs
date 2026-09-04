using System;
using UnityEngine;

using PauseManagement.Core;

namespace PauseManagement.Editor
{
	using UnityEditor;

	/// <summary>
	/// 
	/// </summary>
	[CustomEditor(typeof(PauseManager))]
	public class PauseManagerEditor : Editor
	{
		SerializedProperty m_ScriptProp;

		SerializedProperty m_UseTimeScaleProp;
		SerializedProperty m_PauseOnBlurProp;
		
		SerializedProperty m_UseUnityInputSystemProp;
#if ENABLE_INPUT_SYSTEM
		SerializedProperty m_UsePlayerInputProp;
		SerializedProperty m_PlayerInputProp;
		SerializedProperty m_ActionNameProp;
		SerializedProperty m_UseActionReferenceProp;
		SerializedProperty m_PauseActionReferenceProp;
		SerializedProperty m_PauseActionProp;
#endif
		
		SerializedProperty m_UseUnityInputManagerProp;
		SerializedProperty m_ButtonsListProp;
		
		SerializedProperty m_UseRewiredProp;
#if PAUSE_MANAGER_REWIRED
		SerializedProperty m_PauseOnControllerDisconnectProp;
		SerializedProperty m_ResumeOnControllerConnectProp;
		SerializedProperty m_CheckAllPlayersProp;
		SerializedProperty m_IncludeSystemPlayerProp;
		SerializedProperty m_PlayerIdsProp;
		SerializedProperty m_ActionNamesProp;
#endif

		SerializedProperty m_AssignKeyFromPrefsProp;
		SerializedProperty m_PropertiesListProp;
		SerializedProperty m_PauseKeysProp;
		
		SerializedProperty m_OnPauseEventProp;
		SerializedProperty m_OnResumeEventProp;

#if STEAMWORKS_NET
		SerializedProperty m_PauseOnSteamOverlayActiveProp;
		SerializedProperty m_ResumeOnSteamOverlayInactiveProp;
#endif

		readonly GUIContent m_UseTimeScaleGUIContent = new GUIContent("Use time scale?", "Use Unity's time scale to pause/resume the game?");
		readonly GUIContent m_PauseOnBlurGUIContent = new GUIContent("Pause on blur?", "Whether game should be paused when window lost focus?");
		readonly GUIContent m_UseUnityInputManagerGUIContent = new GUIContent("Use Input Manager?", "Use entries of Unity's Input Manager defined on 'Project Settings > Input'?");
		readonly GUIContent m_ButtonsListGUIContent = new GUIContent("Button's List:", "The list of buttons to pause/resume.\nCan be used for local multiplayer (eg. Player 1 Pause, Player 2 Pause, etc).\n\nDefault is one entry with 'Cancel' value.");
		readonly GUIContent m_UseUnityInputSystemGUIContent = new GUIContent("Use Input System?", "Use bindings of Unity's Input System?");
		readonly GUIContent m_UseRewiredGUIContent = new GUIContent("Use Rewired?", "Use bindings of Rewired?");
		readonly GUIContent m_AssignKeyFromPrefsGUIContent = new GUIContent("Use PlayerPrefs?", "Assign custom pause key from PlayerPrefs?");
		readonly GUIContent m_PropertiesListGUIContent = new GUIContent("Properties List:", "The list of property's name from PlayerPrefs to pause/resume.\nCan be used for local multiplayer (eg. Player 1 Pause, Player 2 Pause, etc).\n\nDefault is one entry with 'Pause' value.");
		readonly GUIContent m_PauseKeysGUIContent = new GUIContent("Pause Keys:", "The keys for pausing.");
#if ENABLE_INPUT_SYSTEM
		readonly GUIContent m_UsePlayerInputGUIContent = new GUIContent("Use Player Input?", "Use Player Input from Unity Input System?");
		readonly GUIContent m_PlayerInputGUIContent = new GUIContent("Player Input:", "Player Input from Unity Input System.\n\nIt's necessary to inform this field when notification behaviour type from Player Input is 'Invoke C Sharp Events'.");
		readonly GUIContent m_ActionNameGUIContent = new GUIContent("Action Name:", "The name of the action that represent Pause/Resume from InputSystem asset.");
		readonly GUIContent m_UseActionReferenceGUIContent = new GUIContent("Use reference?", "Use Input Action Asset's reference?");
		readonly GUIContent m_PauseActionReferenceGUIContent = new GUIContent("Action Reference:", "The input action reference from input action asset.");
#endif
#if PAUSE_MANAGER_REWIRED
		readonly GUIContent m_PauseOnControllerDisconnectGUIContent = new GUIContent("Pause on disconnect?", "Pause when controller is disconnected. Default is true.");
		readonly GUIContent m_ResumeOnControllerConnectGUIContent = new GUIContent("Resume on connect?", "Resume when controller is connected. Default is true.");
		readonly GUIContent m_CheckAllPlayersGUIContent = new GUIContent("Check all players?", "Check all players for input. Default is true.");
		readonly GUIContent m_IncludeSystemPlayerGUIContent = new GUIContent("Include the System Player?", "Optionally include the System Player when acquiring list of players? Default is false.");
		readonly GUIContent m_PlayerIdsGUIContent = new GUIContent("Player IDs:", "The list of player IDs from Rewired.");
		readonly GUIContent m_ActionNamesGUIContent = new GUIContent("Action names:", "The list of actions for checking buttons.");
#endif

#if STEAMWORKS_NET
		readonly GUIContent m_PauseOnSteamOverlayActiveGUIContent = new GUIContent("Pause on active?", "Whether game should pause when Steam Overlay is active.");
		readonly GUIContent m_ResumeOnSteamOverlayInactiveGUIContent = new GUIContent("Resume on inactive?", "Whether game should resume when Steam Overlay is inactive.");
#endif

		void OnEnable()
		{
			m_ScriptProp = serializedObject.FindProperty("m_Script");

			m_UseTimeScaleProp = serializedObject.FindProperty("m_UseTimeScale");
			m_PauseOnBlurProp = serializedObject.FindProperty("m_PauseOnBlur");
			
			m_UseUnityInputManagerProp = serializedObject.FindProperty("m_UseUnityInputManager");
			m_ButtonsListProp = serializedObject.FindProperty("m_ButtonsList");
			
			m_UseUnityInputSystemProp = serializedObject.FindProperty("m_UseUnityInputSystem");
#if ENABLE_INPUT_SYSTEM
			m_UsePlayerInputProp = serializedObject.FindProperty("m_UsePlayerInput");
			m_PlayerInputProp = serializedObject.FindProperty("m_PlayerInput");
			m_ActionNameProp = serializedObject.FindProperty("m_ActionName");
			m_UseActionReferenceProp = serializedObject.FindProperty("m_UseActionReference");
			m_PauseActionReferenceProp = serializedObject.FindProperty("m_PauseActionReference");
			m_PauseActionProp = serializedObject.FindProperty("m_PauseAction");
#endif

			m_UseRewiredProp = serializedObject.FindProperty("m_UseRewired");
#if PAUSE_MANAGER_REWIRED
			m_PauseOnControllerDisconnectProp = serializedObject.FindProperty("m_PauseOnControllerDisconnect");
			m_ResumeOnControllerConnectProp = serializedObject.FindProperty("m_ResumeOnControllerConnect");
			m_CheckAllPlayersProp = serializedObject.FindProperty("m_CheckAllPlayers");
			m_IncludeSystemPlayerProp = serializedObject.FindProperty("m_IncludeSystemPlayer");
			m_PlayerIdsProp = serializedObject.FindProperty("m_PlayerIds");
			m_ActionNamesProp = serializedObject.FindProperty("m_ActionNames");
#endif
			
			m_AssignKeyFromPrefsProp = serializedObject.FindProperty("m_AssignKeyFromPrefs");
			m_PropertiesListProp = serializedObject.FindProperty("m_PropertiesList");
			m_PauseKeysProp = serializedObject.FindProperty("m_PauseKeys");
			
			m_OnPauseEventProp = serializedObject.FindProperty("m_PauseEvent");
			m_OnResumeEventProp = serializedObject.FindProperty("m_ResumeEvent");

#if STEAMWORKS_NET
			m_PauseOnSteamOverlayActiveProp = serializedObject.FindProperty("m_PauseOnSteamOverlayActive");
			m_ResumeOnSteamOverlayInactiveProp = serializedObject.FindProperty("m_ResumeOnSteamOverlayInactive");
#endif
		}

		public override void OnInspectorGUI()
		{
			// Update the serializedProperty - always do this in the beginning of OnInspectorGUI.
			serializedObject.Update();

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(m_ScriptProp);
			EditorGUILayout.EndHorizontal();
			EditorGUI.EndDisabledGroup();

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("General Properties", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(m_UseTimeScaleProp, m_UseTimeScaleGUIContent, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(m_PauseOnBlurProp, m_PauseOnBlurGUIContent, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Controller Properties", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(m_UseUnityInputSystemProp, m_UseUnityInputSystemGUIContent, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			int oldIdentLevel = EditorGUI.indentLevel;

			if (m_UseUnityInputSystemProp.boolValue)
			{
#if ENABLE_INPUT_SYSTEM
				EditorGUI.indentLevel++;

				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.PropertyField(m_UsePlayerInputProp, m_UsePlayerInputGUIContent, GUILayout.ExpandWidth(true));
				EditorGUILayout.EndHorizontal();

				if (m_UsePlayerInputProp.boolValue)
				{
					EditorGUI.indentLevel++;

					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.PropertyField(m_PlayerInputProp, m_PlayerInputGUIContent, GUILayout.ExpandWidth(true));
					EditorGUILayout.EndHorizontal();

					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.PropertyField(m_ActionNameProp, m_ActionNameGUIContent, GUILayout.ExpandWidth(true));
					EditorGUILayout.EndHorizontal();

					EditorGUI.indentLevel--;
				}
				else
				{
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.PropertyField(m_UseActionReferenceProp, m_UseActionReferenceGUIContent, GUILayout.ExpandWidth(true));
					EditorGUILayout.EndHorizontal();

					if (m_UseActionReferenceProp.boolValue)
						EditorGUILayout.PropertyField(m_PauseActionReferenceProp, m_PauseActionReferenceGUIContent);
					else
						EditorGUILayout.PropertyField(m_PauseActionProp);
				}

				EditorGUI.indentLevel--;
#else
				EditorGUILayout.HelpBox(string.Format("Unity's Input System is either not installed or not enabled.{0}{0}If not installed, go to 'Window > Package Manager > Input System' to install it.{0]{0}If not enabled, go to 'Edit > Project Settings > Player > Other Settings > Active Input Handling' and select Input System Package (New) or Both.", Environment.NewLine), MessageType.Warning, true);
#endif
				m_UseUnityInputManagerProp.boolValue = false;
				m_UseRewiredProp.boolValue = false;
				m_AssignKeyFromPrefsProp.boolValue = false;
			}
			else
			{
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.PropertyField(m_UseUnityInputManagerProp, m_UseUnityInputManagerGUIContent, GUILayout.ExpandWidth(true));
				EditorGUILayout.EndHorizontal();

				if (m_UseUnityInputManagerProp.boolValue)
				{
#if ENABLE_LEGACY_INPUT_MANAGER
					EditorGUI.indentLevel++;

					EditorGUILayout.PropertyField(m_ButtonsListProp, m_ButtonsListGUIContent, true, GUILayout.ExpandWidth(true));

					EditorGUI.indentLevel--;
#else
					EditorGUILayout.HelpBox("Unity's Input Manager is not enabled on Project Settings. Go to 'Edit > Project Settings > Player > Other Settings > Active Input Handling' and select Input Manager (Old) or Both.", MessageType.Warning, true);
#endif

					m_AssignKeyFromPrefsProp.boolValue = false;
					m_UseRewiredProp.boolValue = false;
				}
				else
				{
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.PropertyField(m_UseRewiredProp, m_UseRewiredGUIContent, GUILayout.ExpandWidth(true));
					EditorGUILayout.EndHorizontal();

					if (m_UseRewiredProp.boolValue)
					{
#if PAUSE_MANAGER_REWIRED
						EditorGUI.indentLevel++;

						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.PropertyField(m_PauseOnControllerDisconnectProp, m_PauseOnControllerDisconnectGUIContent, GUILayout.ExpandWidth(true));
						EditorGUILayout.EndHorizontal();

						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.PropertyField(m_ResumeOnControllerConnectProp, m_ResumeOnControllerConnectGUIContent, GUILayout.ExpandWidth(true));
						EditorGUILayout.EndHorizontal();

						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.PropertyField(m_CheckAllPlayersProp, m_CheckAllPlayersGUIContent, GUILayout.ExpandWidth(true));
						EditorGUILayout.EndHorizontal();

						if (m_CheckAllPlayersProp.boolValue)
						{
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.PropertyField(m_IncludeSystemPlayerProp, m_IncludeSystemPlayerGUIContent, GUILayout.ExpandWidth(true));
							EditorGUILayout.EndHorizontal();
						}
						else
						{
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.PropertyField(m_PlayerIdsProp, m_PlayerIdsGUIContent, true, GUILayout.ExpandWidth(true));
							EditorGUILayout.EndHorizontal();
						}

						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.PropertyField(m_ActionNamesProp, m_ActionNamesGUIContent, true, GUILayout.ExpandWidth(true));
						EditorGUILayout.EndHorizontal();

						EditorGUI.indentLevel--;
#else
						EditorGUILayout.HelpBox("The Rewired package is not installed.", MessageType.Warning, true);
#endif
						m_AssignKeyFromPrefsProp.boolValue = false;
					}
					else
					{
						EditorGUILayout.BeginVertical();

						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.PropertyField(m_AssignKeyFromPrefsProp, m_AssignKeyFromPrefsGUIContent, GUILayout.ExpandWidth(true));
						EditorGUILayout.EndHorizontal();

						EditorGUI.indentLevel++;

						if (m_AssignKeyFromPrefsProp.boolValue)
						{
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.PropertyField(m_PropertiesListProp, m_PropertiesListGUIContent, true, GUILayout.ExpandWidth(true));
							EditorGUILayout.EndHorizontal();
						}
						else
						{
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.PropertyField(m_PauseKeysProp, m_PauseKeysGUIContent, true, GUILayout.ExpandWidth(true));
							EditorGUILayout.EndHorizontal();
						}

						EditorGUI.indentLevel--;

						EditorGUILayout.EndVertical();
					}
				}
			}

			EditorGUI.indentLevel = oldIdentLevel;

#if STEAMWORKS_NET
			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Steam Overlay Properties", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(m_PauseOnSteamOverlayActiveProp, m_PauseOnSteamOverlayActiveGUIContent, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(m_ResumeOnSteamOverlayInactiveProp, m_ResumeOnSteamOverlayInactiveGUIContent, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();
#endif

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Events Properties", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.PropertyField(m_OnPauseEventProp, GUILayout.ExpandWidth(true));

			EditorGUILayout.Space();

			EditorGUILayout.PropertyField(m_OnResumeEventProp, GUILayout.ExpandWidth(true));

			// Apply changes to the serializedProperty - always do this in the end of OnInspectorGUI.
			serializedObject.ApplyModifiedProperties();
		}
	}
}