using UnityEngine;
using System.Threading.Tasks;
using System.Threading;

namespace Juicer
{
    public static class Juicer_FreezeFrame
    {
        public static bool CurrentlyFreezing { get; private set; }

        /// <summary>
        /// Freezes the game for a specified amount of time.
        /// Useful for emphasizing impacts in a fighting game.
        /// </summary>
        public static async void Trigger (float duration)
        {
            if(duration <= 0.0f)
                return;

            if(CurrentlyFreezing)
                return;

            float defaultTimeScale = Time.timeScale;

            CurrentlyFreezing = true;
            Time.timeScale = 0.0f;

            await Task.Delay((int)(duration * 1000));

            Time.timeScale = defaultTimeScale;
            CurrentlyFreezing = false;
        }
    }
}
