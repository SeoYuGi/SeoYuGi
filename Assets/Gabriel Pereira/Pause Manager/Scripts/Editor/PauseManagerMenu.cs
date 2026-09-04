using System;
using UnityEditor;

using PauseManagement.Core;

namespace PauseManagement.Editor
{
	public class PauseManagerMenu
	{
		/// <summary>
		/// 
		/// </summary>
		[MenuItem("Tools / Pause Manager / About", false, 100)]
		static void About()
		{
			EditorUtility.DisplayDialog("Pause Manager", string.Format("Made by Gabriel Pereira{0}{0}Version: {1}", Environment.NewLine, PauseManager.Version), "OK");
		}
	}
}