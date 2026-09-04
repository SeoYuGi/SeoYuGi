using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace PauseManagement.Core
{
	/// <summary>
	/// 
	/// </summary>
	public class PauseEventHandler : MonoBehaviour
	{
		/// <summary>
		/// Events to be triggered when game is paused
		/// </summary>
		[SerializeField]
		[FormerlySerializedAs("pauseEvents")]
		private UnityEvent m_PauseEvents = null;

		/// <summary>
		/// Events to be triggered when game is resumed
		/// </summary>
		[SerializeField]
		[FormerlySerializedAs("resumeEvents")]
		private UnityEvent m_ResumeEvents = null;

		// This function is called when the object becomes enabled and active
		void OnEnable()
		{
			PauseManager.PauseAction += PauseHandler;
		}

		// This function is called when the behaviour becomes disabled.
		void OnDisable()
		{
			PauseManager.PauseAction -= PauseHandler;
		}

		void PauseHandler(bool paused)
		{
			if (paused)
				m_PauseEvents.Invoke();
			else
				m_ResumeEvents.Invoke();
		}
	}
}